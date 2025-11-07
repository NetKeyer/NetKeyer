// ****************************************************************************
///*!	\file DAXTXAudioStream.cs
// *	\brief Represents a single TX Audio Stream (narrow, mono)
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
using Flex.Smoothlake.FlexLib.Interface;
using Flex.UiWpfFramework.Mvvm;
using Flex.Smoothlake.Vita;
using Flex.Util;

namespace Flex.Smoothlake.FlexLib
{
    public class DAXTXAudioStream : ObservableObject, IDaxTxStream
    {
        private const int TX_SAMPLES_PER_PACKET = 128; // Samples can be mono-int or stereo-float
        private Radio _radio;
        private bool _closing = false;

        internal bool Closing
        {
            set { _closing = value; }
        }

        public DAXTXAudioStream(Radio radio)
        {
            _radio = radio;
        }

        private uint _clientHandle;
        public uint ClientHandle
        {
            get { return _clientHandle; }
            set { _clientHandle = value; }
        }

        private uint _txStreamID;
        public uint TXStreamID
        {
            get { return _txStreamID; }
            internal set
            {
                if (_txStreamID != value)
                {
                    _txStreamID = value;
                    RaisePropertyChanged("TXStreamID");
                }
            }
        }

        private bool _transmit = false;
        public bool Transmit
        {
            get { return _transmit; }
            set
            {
                if (_transmit != value)
                {
                    _transmit = value;
                    // we no longer need to send this state back down to the radio as it is tracked from the radio (and the
                    // client doesn't need to use this other than for informational purposes)
                    //_radio.SendCommand("stream set 0x" + _txStreamID.ToString("X") + " tx=" + Convert.ToByte(_transmit));
                    RaisePropertyChanged("Transmit");
                }
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
    
        private int _txGain;
        public int TXGain
        {
            get { return _txGain; }
            set
            {
                int new_gain = value;

                // check limits
                if (new_gain > 100) new_gain = 100;
                if (new_gain < 0) new_gain = 0;

                if (_txGain != new_gain)
                {
                    _txGain = value;
                    RaisePropertyChanged("TXGain");
                }
                else if (new_gain != value)
                {
                    RaisePropertyChanged("TXGain");
                }
            }
        }

        public int Gain
        {
            get => TXGain;
            set
            {
                if (_txGain == value)
                    return;

                TXGain = value;
                RaisePropertyChanged("Gain");
            }
        }

        public void Close()
        {
            Debug.WriteLine("TXAudioStream::Close (0x" + _txStreamID.ToString("X") + ")");
            _closing = true;
            _radio.SendCommand("stream remove 0x" + _txStreamID.ToString("X"));
            _radio.RemoveDAXTXAudioStream(_txStreamID);
        }
        
        private VitaIFDataPacket _txPacket;
        public void AddTXData(float[] tx_data_stereo, bool sendReducedBW = false)
        {
            // skip this if we are not the DAX TX Client
            if (!_transmit) return;

            if (_txPacket == null)
            {
                _txPacket = new VitaIFDataPacket();
                _txPacket.header.pkt_type = VitaPacketType.IFDataWithStream;
                _txPacket.header.c = true;
                _txPacket.header.t = false;
                _txPacket.header.tsi = VitaTimeStampIntegerType.Other;
                _txPacket.header.tsf = VitaTimeStampFractionalType.SampleCount;

                _txPacket.stream_id = _txStreamID;
                _txPacket.class_id.OUI = 0x001C2D;
                _txPacket.class_id.InformationClassCode = 0x534C;
            }

            if (sendReducedBW)
            {
                _txPacket.class_id.PacketClassCode = 0x0123;
            }
            else
            {
                _txPacket.class_id.PacketClassCode = 0x03E3;
            }

            // Send entire passed in buffer but warn if not a correct multiple
            if (tx_data_stereo.Length != TX_SAMPLES_PER_PACKET * 2) // * 2 for stereo
            {
                Debug.WriteLine("Invalid number of samples passed in. Expecting {0} samples of left/right float pairs", TX_SAMPLES_PER_PACKET);
                return;
            }

            float[] tx_data;

            if (sendReducedBW)
            {
                // Deinterleave since we're only sending MONO
                tx_data = new float[tx_data_stereo.Length / 2];
                for (int i = 0; i < tx_data.Length; i++)
                {
                    tx_data[i] = tx_data_stereo[i * 2];
                }
            }
            else
            {
                tx_data = tx_data_stereo;
            }

            int num_samples_to_send = 0;

            // how many samples should we send?
            if (sendReducedBW)
            {
                num_samples_to_send = TX_SAMPLES_PER_PACKET;

                _txPacket.payload_int16 = new Int16[num_samples_to_send];

                for (int i = 0; i < num_samples_to_send; i++)
                {
                    if (tx_data[i] > 1.0)
                        tx_data[i] = 1.0f;
                    else if (tx_data[i] < -1.0)
                        tx_data[i] = -1.0f;

                    _txPacket.payload_int16[i] = (Int16)(tx_data[i] * 32767);
                }

                // set the length of the packet -- note this is in 4 byte word units
                _txPacket.header.packet_size = (ushort)((num_samples_to_send * sizeof(Int16) / 4) + 7); // 7*4=28 bytes of Vita overhead
            }
            else
            {
                num_samples_to_send = TX_SAMPLES_PER_PACKET * 2; // *2 for stereo

                _txPacket.payload = new float[num_samples_to_send];

                // copy the incoming data into the packet payload
                Array.Copy(tx_data, 0, _txPacket.payload, 0, num_samples_to_send);

                // set the length of the packet -- note this is in 4 byte word units
                _txPacket.header.packet_size = (ushort)(num_samples_to_send + 7); // 7*4=28 bytes of Vita overhead
            }

            // send the packet to the radio
            //Debug.WriteLine("sending from channel " + _daxChannel);

            try
            {

                _radio.VitaSock.SendUDP(_txPacket.ToBytes(use_int16_payload: sendReducedBW));
            }
            catch (Exception e)
            {
                Debug.WriteLine("TXAudioStream: AddTXData Exception (" + e.ToString() + ")");
            }
            //Debug.Write("("+num_samples_to_send+")");

            // bump the packet count
            _txPacket.header.packet_count = (byte)((_txPacket.header.packet_count + 1) % 16);
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
                    Debug.WriteLine("DAXTXAudioStream::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "client_handle":
                        {
                            uint temp;
                            bool b = StringHelper.TryParseInteger(value, out temp);

                            if (!b) continue;

                            _clientHandle = temp;
                            RaisePropertyChanged("ClientHandle");

                            if (!_radioAck)
                                set_radio_ack = true;
                        }
                        break;

                    case "tx":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("DAXTXAudioStream::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            bool new_transmit_state = Convert.ToBoolean(temp);
                            if (_transmit == new_transmit_state)
                                continue;

                            _transmit = new_transmit_state;
                            RaisePropertyChanged("Transmit");
                        }
                        break;

                    default:
                        Debug.WriteLine("DAXTXAudioStream::StatusUpdate: Key not parsed (" + kv + ")");
                        break;
                }
            }

            if (set_radio_ack)
            {
                RadioAck = true;
                _radio.OnDAXTXAudioStreamAdded(this);                
            }
        }
    }
}
