// ****************************************************************************
///*!	\file NetCWStream.cs
// *	\brief Represents a single network CW Stream
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2016-4-1
// *	\author Ed Gonzalez, KG5FBT
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Flex.UiWpfFramework.Mvvm;
using Flex.Util;
using Flex.Smoothlake.Vita;
using System.Threading;

namespace Flex.Smoothlake.FlexLib
{
    public class NetCWStream : ObservableObject
    {
        private Radio _radio;
        private int _tx_index = 1;

        public NetCWStream(Radio radio)
        {
            _radio = radio;
        }

        private uint _txStreamID;
        public uint TXStreamID
        {
            get { return _txStreamID; }
        }

        private int _txCount = 0;
        public int TXCount
        {
            get { return _txCount; }
            set
            {
                _txCount = value;
            }
        }

        public bool RequestNetCWStreamFromRadio()
        {
            // check to ensure this object is tied to a radio object
            if (_radio == null) return false;

            // check to make sure the radio is connected
            if (!_radio.Connected) return false;

            // send the command to the radio to create the object...need to change this..
            _radio.SendReplyCommand(new ReplyHandler(UpdateStreamID), "stream create netcw");

            return true;
        }

        private void UpdateStreamID(int seq, uint resp_val, string s)
        {
            if (resp_val != 0) return;

            bool b = StringHelper.TryParseInteger(s, out _txStreamID);

            if (!b)
            {
                Debug.WriteLine("NetCWStream::UpdateStreamID-Error parsing Stream ID (" + s + ")");
                return;
            }
        }


        public void Close()
        {
            Debug.WriteLine("NetCWStream::Close (0x" + _txStreamID.ToString("X") + ")");
        }

        internal void Remove()
        {
            Debug.WriteLine("NetCWStream::Remove (0x" + _txStreamID.ToString("X") + ")");
            _radio.SendCommand("stream remove 0x" + _txStreamID.ToString("X"));
        }

        public int GetNextIndex()
        {
            lock (this)
            {
                return _tx_index++;
            }
        }

        //private VitaIFDataPacket _txPacket2;
        private VitaOpusDataPacket _txPacket;
        public void AddTXData(string s)
        {

            byte[] tx_data = System.Text.Encoding.ASCII.GetBytes(s);
            _txCount += tx_data.Length;

            if (_txPacket == null)
            {
                _txPacket = new VitaOpusDataPacket();
                _txPacket.header.pkt_type = VitaPacketType.ExtDataWithStream;
                _txPacket.header.c = true;
                _txPacket.header.t = false;
                _txPacket.header.tsi = VitaTimeStampIntegerType.Other;
                _txPacket.header.tsf = VitaTimeStampFractionalType.SampleCount;

                _txPacket.stream_id = _txStreamID;
                _txPacket.class_id.OUI = 0x001C2D;
                _txPacket.class_id.InformationClassCode = 0x534C;
                _txPacket.class_id.PacketClassCode = 0x03E3;

                //_txPacket.payload = new float[256];
                _txPacket.payload = new byte[tx_data.Length];
            }

            int samples_sent = 0;

            while (samples_sent < tx_data.Length)
            {
                // how many samples should we send?
                //int num_samples_to_send = Math.Min(256, tx_data.Length - samples_sent);
                int num_samples_to_send = Math.Min(tx_data.Length, tx_data.Length - samples_sent);
                _txPacket.payload = new byte[tx_data.Length];
                //int num_samples_to_send = tx_data.Length;

                // copy the incoming data into the packet payload
                Array.Copy(tx_data, samples_sent, _txPacket.payload, 0, num_samples_to_send);

                // set the length of the packet
                // packet_size is the 32 bit word length?
                _txPacket.header.packet_size = (ushort)Math.Ceiling((double)num_samples_to_send / 4.0 + 7.0); // 7*4=28 bytes of Vita overhead

                try
                {
                    // send the packet to the radio
                    _radio.VitaSock.SendUDP(_txPacket.ToBytesTX());
                }
                catch (Exception e)
                {
                    Debug.WriteLine("NetCWSTream: AddTXData sendTo() exception = " + e.ToString());
                }
                // bump the packet count
                _txPacket.header.packet_count = (byte)((_txPacket.header.packet_count + 1) % 16);

                // adjust the samples sent
                samples_sent += num_samples_to_send;
            }
        }

    }
}
