// ****************************************************************************
///*!	\file RXAudioStream.cs
// *	\brief Represents the base class for any recieve audio stream
// *
// *	\copyright	Copyright 2012-2019 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2018-10-15
// *	\author Abed Haque, AB5ED
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Timers;
using Flex.Smoothlake.Vita;
using Flex.UiWpfFramework.Mvvm;


namespace Flex.Smoothlake.FlexLib
{
    public class RXAudioStream : ObservableObject
    {
        protected Radio _radio;
        protected System.Timers.Timer _statsTimer = new System.Timers.Timer(1000);
        public RXAudioStream(Radio radio)
        {
            _radio = radio;
            _statsTimer.AutoReset = true;
            _statsTimer.Elapsed += UpdateRXRate;
        }

        protected uint _clientHandle;
        public uint ClientHandle
        {
            get { return _clientHandle; }
            set { _clientHandle = value; }
        }

        protected bool _closing = false;
        internal bool Closing
        {
            set { _closing = value; }
        }

        protected bool _radioAck = false;
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

        protected uint _streamId;
        public uint StreamID
        {
            get { return _streamId; }
            internal set { _streamId = value; }
        }

        private int _byteSum = 0;
        private int _bytesPerSecFromRadio;
        public int BytesPerSecFromRadio
        {
            get => _bytesPerSecFromRadio;
            set
            {
                if (_bytesPerSecFromRadio == value)
                    return;
                
                _bytesPerSecFromRadio = value;
                RaisePropertyChanged("BytesPerSecFromRadio");
            }
        }

        private int error_count_out_of_order = 0;
        private int error_count_total = 0;
        public int ErrorCount
        {
            get { return error_count_total; }
            set
            {
                if (error_count_total != value)
                {
                    error_count_total = value;
                    RaisePropertyChanged("ErrorCount");
                }
            }
        }

        private int total_count = 0;
        public int TotalCount
        {
            get { return total_count; }
            set
            {
                if (total_count != value)
                {
                    total_count = value;
                    // only raise the property change every 100 packets (performance)
                    if (total_count % 100 == 0) RaisePropertyChanged("TotalCount");
                }
            }
        }

        // note that the "Lost" indicator assumes we only lost a single packet -- this is probably reasonable for "decent" networks
        private void PrintStats()
        {
            int lost = error_count_total - error_count_out_of_order;
            Debug.WriteLine("Audio Stream 0x" + _streamId.ToString("X").PadLeft(8, '0') +
                "-Reversed: " + error_count_out_of_order + " (" + (error_count_out_of_order * 100.0 / total_count).ToString("f2") + ")" +
                "  Lost: " + lost + " (" + (lost * 100.0 / total_count).ToString("f2") + ")" +
                "  Total: " + total_count);
        }

        private const int NOT_INITIALIZED = 99;
        private int last_packet_count = NOT_INITIALIZED;
        internal void AddRXData(VitaIFDataPacket packet)
        {
            TotalCount++;
#if DEBUG_STATS
            if (total_count % 1000 == 0) PrintStats();
#endif

            Interlocked.Add(ref _byteSum, packet.Length);

            int packet_count = packet.header.packet_count;
            OnRXDataReady(this, packet.payload);

            // normal case -- this is the next packet we are looking for, or it is the first one
            if (packet_count == (last_packet_count + 1) % 16 || last_packet_count == NOT_INITIALIZED)
            {
                last_packet_count = packet_count;
            }
            else
            {
                error_count_out_of_order++;
                ErrorCount++;
                last_packet_count = packet_count;
            }
        }

        public Object OpusRXListLockObj = new Object();
        public double LastOpusTimestampConsumed = 0;
        public SortedList<double, VitaOpusDataPacket> _opusRXList = new SortedList<double, VitaOpusDataPacket>();

        internal void AddRXData(VitaOpusDataPacket packet)
        {
            TotalCount++;
#if DEBUG_STATS
            if (TotalCount % 1000 == 0) PrintStats();
#endif
            //Debug.WriteLine("OpusTimestamp: " + packet.timestamp_int + "." + packet.timestamp_frac);

            double timestamp_key = packet.timestamp_int + (packet.timestamp_frac / Math.Pow(2, 16));

            //Debug.WriteLine("OpusTimestampKey: " + timestamp_key);
            
            Interlocked.Add(ref _byteSum, packet.Length);

            int packet_count = packet.header.packet_count;

            // Only queue if the packet is more recent than the last one the 
            // Audio callback consumed

            if (LastOpusTimestampConsumed < timestamp_key)
            {
                lock (OpusRXListLockObj)
                {
                    if (_opusRXList.Count > 30)
                    {
                        Debug.Write("X");
                        _opusRXList.Clear(); /* Overflow event */
                    }

                    _opusRXList.Add(timestamp_key, packet);
                }
            }
            else
            {
                // Old data we no longer care about
                Debug.Write("o");
            }
            //normal case -- this is the next packet we are looking for, or it is the first one
            if (packet_count == (last_packet_count + 1) % 16 || last_packet_count == NOT_INITIALIZED)
            {
                last_packet_count = packet_count;
            }
            else
            {
                Debug.WriteLine("Opus Audio: Expected " + ((last_packet_count + 1) % 16) + "  got " + packet_count);
                ErrorCount++;

                last_packet_count = packet_count;
            }

            OnOpusPacketReceived();
        }
        
        protected bool _shouldApplyRxGainScalar = false;
        protected float _rxGainScalar = 1.0f;

        public delegate void DataReadyEventHandler(RXAudioStream rxAudioStream, float[] rx_data);
        public event DataReadyEventHandler DataReady;
        private void OnRXDataReady(RXAudioStream rxAudioStream, float[] rx_data)
        {
            if (DataReady != null)
            {
                if (_shouldApplyRxGainScalar)
                {
                    // MICAudioStream can apply an RX Gain on the client side
                    for (int i = 0; i < rx_data.Length; i++)
                    {
                        rx_data[i] = rx_data[i] * _rxGainScalar;
                    }
                }
                
                DataReady(rxAudioStream, rx_data);
            }
        }

        public delegate void OpusPacketReceivedEventHandler();
        public event OpusPacketReceivedEventHandler OpusPacketReceived;
        private void OnOpusPacketReceived()
        {
            OpusPacketReceived?.Invoke();
        }

        protected void UpdateRXRate(Object source, ElapsedEventArgs e)
        {
            _bytesPerSecFromRadio = _byteSum;
            _byteSum = 0;

            RaisePropertyChanged("BytesPerSecFromRadio");
        }
    }
}
