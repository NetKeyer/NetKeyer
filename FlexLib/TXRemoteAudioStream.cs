// ****************************************************************************
///*!	\file TXRemoteAudioStream.cs
// *	\brief Represents a single remote audio transmit stream
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2013-11-18
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Threading;
using Flex.UiWpfFramework.Mvvm;
using Flex.Smoothlake.Vita;

namespace Flex.Smoothlake.FlexLib
{
    public class TXRemoteAudioStream : ObservableObject
    {
        private Radio _radio;
        public TXRemoteAudioStream(Radio radio)
        {
            _radio = radio;
        }

        private bool _closing = false;
        internal bool Closing
        {
            set { _closing = value; }
        }

        private uint _streamID;
        public uint StreamID
        {
            get { return _streamID; }
            internal set
            {
                _streamID = value;
            }
        }

        private string _clientHandle;
        public string ClientHandle
        {
            get { return _clientHandle; }
            internal set
            {
                _clientHandle = value;
            }
        }



        private bool _radioAck = false;
        public bool RadioAck
        {
            get { return _radioAck; }
            internal set
            {
                if (_radioAck != value)
                {
                    _radioAck = value;
                    RaisePropertyChanged("RadioAck");
                }
            }
        }

        private int _byteSumTX = 0;
        private double _bytesPerSecToRadio;
        public double BytesPerSecToRadio
        {
            get { return _bytesPerSecToRadio; }
            set
            {
                if (_bytesPerSecToRadio != value)
                {
                    _bytesPerSecToRadio = value;
                    RaisePropertyChanged("BytesPerSecToRadio");
                }
            }
        }

        public bool RequestTXRemoteAudioStreamFromRadio()
        {
            // check to see if this object has already been activated
            if (_radioAck) return false;

            // check to ensure this object is tied to a radio object
            if (_radio == null) return false;

            // check to make sure the radio is connected
            if (!_radio.Connected) return false;

            // send the command to the radio to create the object...need to change this..
            //_radio.SendReplyCommand(new ReplyHandler(UpdateStreamID), "stream create opus");

            return true;
        }

        public void Close()
        {
            Debug.WriteLine("TXRemoteAudioStream::Close (0x" + _streamID.ToString("X") + ")");
            _radio.RemoveTXRemoteAudioStream(_streamID);
        }

        internal void Remove()
        {
            _closing = true;
            Debug.WriteLine("TXRemoteAudioStream::Remove (0x" + _streamID.ToString("X") + ")");
            _radio.SendCommand("stream remove 0x" + _streamID.ToString("X"));
        }

        private VitaOpusDataPacket _txPacket;
        public void AddTXData(byte[] tx_data)
        {
            Interlocked.Add(ref _byteSumTX, tx_data.Length);

            if (_txPacket == null)
            {
                _txPacket = new VitaOpusDataPacket();
                _txPacket.header.pkt_type = VitaPacketType.ExtDataWithStream;
                _txPacket.header.c = true;
                _txPacket.header.t = false;
                _txPacket.header.tsi = VitaTimeStampIntegerType.Other;
                _txPacket.header.tsf = VitaTimeStampFractionalType.SampleCount;

                _txPacket.stream_id = _streamID;
                _txPacket.class_id.OUI = 0x001C2D;
                _txPacket.class_id.InformationClassCode = 0x534C;
                _txPacket.class_id.PacketClassCode = 0x8005;
            }
            
            _txPacket.payload = tx_data;

            // set the length of the packet
            // packet_size is the 32 bit word length?
            _txPacket.header.packet_size = (ushort)Math.Ceiling(_txPacket.payload.Length / 4.0 + 7.0); // 7*4=28 bytes of Vita overhead

            try
            {
                // send the packet to the radio
                _radio.VitaSock.SendUDP(_txPacket.ToBytesTX());
            }
            catch (Exception e)
            {
                Debug.WriteLine($"TXRemoteAudioStream: AddTXData sendTo() exception = {e}");
            }
            // bump the packet count
            _txPacket.header.packet_count = (byte)((_txPacket.header.packet_count + 1) % 16);
        }

        private bool _isCompressed;
        public bool IsCompressed
        {
            get { return _isCompressed; }
            set
            {
                _isCompressed = value;
                RaisePropertyChanged("IsCompressed");
            }
        }

        public void StatusUpdate(string s)
        {
            bool set_radio_ack = false;
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("TXRemoteAudioStream::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "compression":
                        IsCompressed = value.ToLower() == "opus" ? true : false;
                        break;

                    case "client_handle":
                        if (value.StartsWith("0x"))
                            _clientHandle = value.Substring(2);
                        else
                            _clientHandle = value;

                        if (!_radioAck)
                            set_radio_ack = true;
                        break;

                    default:
                        Debug.WriteLine("TXRemoteAudioStream::StatusUpdate: Key not parsed (" + kv + ")");
                        break;
                }
            }

            if (set_radio_ack)
            {
                RadioAck = true;
                _radio.OnTXRemoteAudioStreamAdded(this);
            }
        }
    }
}
