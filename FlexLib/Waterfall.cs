// ****************************************************************************
///*!	\file Waterfall.cs
// *	\brief Represents a single Waterfall display
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2014-03-10
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

using Flex.UiWpfFramework.Mvvm;
using Flex.Util;
using Flex.Smoothlake.Vita;


namespace Flex.Smoothlake.FlexLib
{
    public class Waterfall : ObservableObject
    {
        private Radio _radio;
        private Dictionary<uint, WaterfallTile> _fragmentedWaterfallTileDict;

        internal Waterfall(Radio radio)
        {
            //Debug.WriteLine("Waterfall::Waterfall");
            _radio = radio;
            GetRFGainInfo();
            _fragmentedWaterfallTileDict = new Dictionary<uint, WaterfallTile>();

            // TODO: Remove this once we have data coming from the radio
            //Thread t = new Thread(new ThreadStart(RunTestData));
            //t.Name = "Waterfall Run Test Data";
            //t.IsBackground = true;
            //t.Priority = ThreadPriority.Normal;
            //t.Start();
        }

        private void RunTestData()
        {
            while (_radio != null)
            {
                WaterfallTile tile = GenerateTestDataTile();
                OnDataReady(this, tile);
                Thread.Sleep((int)tile.LineDurationMS);
            }
        }

        private uint test_timecode = 0;
        private Random test_random = new Random();
        private WaterfallTile GenerateTestDataTile()
        {
            WaterfallTile tile = new WaterfallTile();
            tile.Timecode = test_timecode++;
            tile.FrameLowFreq = 14.0;
            tile.BinBandwidth = (0.2 / _width);
            tile.LineDurationMS = 100;
            tile.Width = (ushort)(_width*1.2);
            tile.Height = 1;
            
            tile.Data = new ushort[tile.Width*tile.Height];

            for (int i = 0; i < tile.Width * tile.Height; i++)
            {
                if (i >= 57 && i <= 60)
                    tile.Data[i] = (ushort)test_random.Next(13000, 50000);
                else tile.Data[i] = (ushort)test_random.Next(13000, 16000);
                //tile.Data[i] = (ushort)(i * 35);
            }

            return tile;
        }
 
        private void GetRFGainInfo()
        {
            _radio.SendReplyCommand(new ReplyHandler(UpdateRFGainInfo), "display panafall rfgain_info 0x" + _stream_id.ToString("X"));
        }

        private void UpdateRFGainInfo(int seq, uint resp_val, string s)
        {
            if (resp_val != 0) return;

            string[] vals = s.Split(',');
            if (vals.Length < 3) return;

            int.TryParse(vals[0], out _rf_gain_low);
            int.TryParse(vals[1], out _rf_gain_high);
            int.TryParse(vals[2], out _rf_gain_step);

            if(vals.Length > 3)
            {
                _rf_gain_markers = new int[vals.Length-3];
                for (int i = 3; i < vals.Length; i++)
                    int.TryParse(vals[i], out _rf_gain_markers[i - 3]);
            }

            RaisePropertyChanged("RFGainLow");
            RaisePropertyChanged("RFGainHigh");
            RaisePropertyChanged("RFGainStep");
            RaisePropertyChanged("RFGainMarkers");
        }

        internal uint _stream_id;
        public uint StreamID
        {
            get { return _stream_id; }
            internal set { _stream_id = value; }
        }

        private uint _clientHandle;
        public uint ClientHandle
        {
            get { return _clientHandle; }
            internal set { _clientHandle = value; }
        }

        internal uint _parentPanadapterStreamID = 0;
        public uint ParentPanadapterStreamID
        {
            get { return _parentPanadapterStreamID; }
            internal set { _parentPanadapterStreamID = value; }
        }

        private string _rxant;
        public string RXAnt
        {
            get { return _rxant; }
            set
            {
                if (_rxant != value)
                {
                    _rxant = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " rxant=" + _rxant);
                    RaisePropertyChanged("RXAnt");
                }
            }
        }

        private int _rfGain;
        public int RFGain
        {
            get { return _rfGain; }
            set
            {
                if (_rfGain != value)
                {
                    _rfGain = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " rfgain=" + _rfGain);
                    RaisePropertyChanged("RFGain");
                }
            }
        }

        private int _rf_gain_low;
        public int RFGainLow
        {
            get { return _rf_gain_low; }
            set
            {
                if (_rf_gain_low != value)
                {
                    _rf_gain_low = value;
                    RaisePropertyChanged("RFGainLow");
                }
            }
        }

        private int _rf_gain_high;
        public int RFGainHigh
        {
            get { return _rf_gain_high; }
            set
            {
                if (_rf_gain_high != value)
                {
                    _rf_gain_high = value;
                    RaisePropertyChanged("RFGainHigh");
                }
            }
        }

        private int _rf_gain_step;
        public int RFGainStep
        {
            get { return _rf_gain_step; }
            set
            {
                if (_rf_gain_step != value)
                {
                    _rf_gain_step = value;
                    RaisePropertyChanged("RFGainHigh");
                }
            }
        }

        private int[] _rf_gain_markers;
        public int[] RFGainMarkers
        {
            get { return _rf_gain_markers; }
            set
            {
                _rf_gain_markers = value;
                RaisePropertyChanged("RFGainMarkers");
            }
        }

        private int _daxIQChannel;
        public int DAXIQChannel
        {
            get { return _daxIQChannel; }
            set
            {
                if (_daxIQChannel != value)
                {
                    _daxIQChannel = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " daxiq_channel=" + _daxIQChannel);
                    RaisePropertyChanged("DAXIQChannel");

                    //_radio.SetIQStreamWaterfall(_daxIQChannel, this);
                }
            }
        }

        private int _width;
        public int Width
        {
            get { return _width; }
            set
            {
                //Debug.WriteLine("Waterfall::Width = " + value + " (StreamID: 0x" + _stream_id.ToString("X") + ")");
                if (_width != value)
                {
                    _width = value;
                    // Do not need to send command since Radio only expects the panadapter portion of the Y-Pixels
                    //_radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " xpixels=" + _width);
                    RaisePropertyChanged("Width");
                }
            }
        }

        private int _height;
        public int Height
        {
            get { return _height; }
            set
            {
                if (_height != value)
                {
                    _height = value;
                    // Do not need to send command since Radio only expects the panadapter portion of the Y-Pixels
                    //_radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " ypixels=" + _height);
                    RaisePropertyChanged("Height");
                }
            }
        }

        private string _band;
        public string Band
        {
            get { return _band; }
            set
            {
                if (true )
                {
                    _band = value;
                    _radio.SendReplyCommand(new ReplyHandler(SetCenterFreqReply), "display panafall set 0x" + _stream_id.ToString("X") + " band=" + _band);
                    RaisePropertyChanged("Band");
                }
            }
        }

        private double _centerFreq;
        public double CenterFreq
        {
            get { return _centerFreq; }
            set
            {
                double new_freq = value;

                if (_centerFreq != new_freq)
                {
                    _centerFreq = new_freq;
                    //_radio.SendReplyCommand(new ReplyHandler(SetCenterFreqReply), "display panafall set 0x" + _stream_id.ToString("X") + " center=" + StringHelper.DoubleToString(_centerFreq, "f6"));
                    RaisePropertyChanged("CenterFreq");
                    //Debug.WriteLine("Cmd: Pan 0x" + _stream_id.ToString("X") + " Freq:" + _centerFreq.ToString("f6"));
                }
            }
        }

        private void SetCenterFreqReply(int seq, uint resp_val, string s)
        {
            if (resp_val == 0) return;

            double temp;
            bool b = StringHelper.TryParseDouble(s, out temp);
            if (!b)
            {
                Debug.WriteLine("Waterfall::SetCenterFreqReply: Invalid reply string (" + s + ")");
                return;
            }

            _centerFreq = temp;
            RaisePropertyChanged("CenterFreq");
        }

        private const double MAX_BANDWIDTH = 24.576 * 0.6;
        private const double MIN_BANDWIDTH = 0.006 * 0.6;
        private double _bandwidth;
        public double Bandwidth
        {
            get { return _bandwidth; }
            set
            {
                double new_bw = value;
                double new_center = _centerFreq;

                // check bandwidth limits
                if (new_bw > MAX_BANDWIDTH) new_bw = MAX_BANDWIDTH;
                else if (new_bw < MIN_BANDWIDTH) new_bw = MIN_BANDWIDTH;
                
                if (_bandwidth != new_bw)// && (new_center - new_bw / 2 > 0))
                {
                    _bandwidth = new_bw;
                    //string cmd = "display panafall set 0x" + _stream_id.ToString("X") + " bandwidth=" + StringHelper.DoubleToString(new_bw, "f6");
                    //if (_autoCenter) cmd += " autocenter=1";
                    //_radio.SendReplyCommand(new ReplyHandler(SetBandwidthReply), cmd);
                    RaisePropertyChanged("Bandwidth");
                }
                else if (new_bw != value)
                {
                    RaisePropertyChanged("Bandwidth");
                }
            }
        }

        private bool _autoCenter = false;
        public bool AutoCenter
        {
            get { return _autoCenter; }
            set
            {
                if (_autoCenter != value)
                {
                    _autoCenter = value;
                    RaisePropertyChanged("AutoCenter");
                }
            }
        }

        private void SetBandwidthReply(uint resp_val, string s)
        {
            if (resp_val == 0) return;

            double temp;
            bool b = StringHelper.TryParseDouble(s, out temp);
            if (!b)
            {
                Debug.WriteLine("Waterfall::SetBandwidthReply: Invalid reply string (" + s + ")");
                return;
            }

            _bandwidth = temp;
            RaisePropertyChanged("Bandwidth");
        }

        private double _lowDbm;
        public double LowDbm
        {
            get { return _lowDbm; }
            set
            {
                if (value < -180.0) value = -180.0;
                if (_lowDbm != value)
                {
                    _lowDbm = value;
                    _radio.SendReplyCommand(new ReplyHandler(SetLowDbmReply), "display panafall set 0x" + _stream_id.ToString("X") + " min_dbm=" + StringHelper.DoubleToString(_lowDbm, "f6"));
                    RaisePropertyChanged("LowDbm");
                }
            }
        }

        private void SetLowDbmReply(int seq, uint resp_val, string s)
        {
            if (resp_val == 0) return;

            double temp;
            bool b = StringHelper.TryParseDouble(s, out temp);
            if (!b)
            {
                Debug.WriteLine("Waterfall::SetLowDbmReply: Invalid reply string (" + s + ")");
                return;
            }

            _lowDbm = temp;
            RaisePropertyChanged("LowDbm");
        }

        private double _highDbm;
        public double HighDbm
        {
            get { return _highDbm; }
            set
            {
                if (value > 20.0) value = 20.0;
                if (_highDbm != value)
                {
                    _highDbm = value;
                    _radio.SendReplyCommand(new ReplyHandler(SetHighDbmReply), "display panafall set 0x" + _stream_id.ToString("X") + " max_dbm=" + StringHelper.DoubleToString(_highDbm, "f6"));
                    RaisePropertyChanged("HighDbm");
                }
            }
        }

        private void SetHighDbmReply(int seq, uint resp_val, string s)
        {
            if (resp_val == 0) return;

            double temp;
            bool b = StringHelper.TryParseDouble(s, out temp);
            if (!b)
            {
                Debug.WriteLine("Waterfall::SetHighDbmReply: Invalid reply string (" + s + ")");
                return;
            }

            _highDbm = temp;
            RaisePropertyChanged("HighDbm");
        }

        private int _fps;
        public int FPS
        {
            get { return _fps; }
            set
            {
                if (_fps != value)
                {
                    _fps = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " fps=" + value);
                    RaisePropertyChanged("FPS");
                }
            }
        }

        private int _average;
        public int Average
        {
            get { return _average; }
            set
            {
                if (_average != value)
                {
                    _average = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " average=" + value);
                    RaisePropertyChanged("Average");
                }
            }
        }

        private bool _weightedAverage;
        public bool WeightedAverage
        {
            get { return _weightedAverage; }
            set
            {
                if (_weightedAverage != value)
                {
                    _weightedAverage = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("x") + " weighted_average=" + Convert.ToByte(_weightedAverage));
                    RaisePropertyChanged("WeightedAverage");
                }
            }
        }

        private bool _wide;
        public bool Wide
        {
            get { return _wide; }
            set
            {
                if (_wide != value)
                {
                    _wide = value;
                    RaisePropertyChanged("Wide");
                }
            }
        }

        private bool _loopA;
        public bool LoopA
        {
            get { return _loopA; }
            set
            {
                if (_loopA != value)
                {
                    _loopA = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " loopa=" + Convert.ToByte(_loopA));
                    RaisePropertyChanged("LoopA");
                }
            }
        }

        private bool _loopB;
        public bool LoopB
        {
            get { return _loopB; }
            set
            {
                if (_loopB != value)
                {
                    _loopB = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " loopb=" + Convert.ToByte(_loopB));
                    RaisePropertyChanged("LoopB");
                }
            }
        }

        private int _fallLineDurationMs;
        public int FallLineDurationMs
        {
            get { return _fallLineDurationMs; }
            set
            {
                if (_fallLineDurationMs != value)
                {
                    _fallLineDurationMs = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " line_duration=" + _fallLineDurationMs.ToString());
                    RaisePropertyChanged("FallLineDurationMs");
                }
            }
        }

        private ushort _fallBlackLevel;
        public ushort FallBlackLevel
        {
            get { return _fallBlackLevel; }
            set
            {
                if (_fallBlackLevel != value)
                {
                    _fallBlackLevel = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " black_level=" + _fallBlackLevel.ToString());
                     RaisePropertyChanged("FallBlackLevel");
                }
            }
        }

        private int _fallColorGain;
        public int FallColorGain
        {
            get { return _fallColorGain; }
            set
            {
                if (_fallColorGain != value)
                {
                    _fallColorGain = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " color_gain=" + _fallColorGain.ToString());
                    RaisePropertyChanged("FallColorGain");
                }
            }
        }

        private bool _autoBlackLevelEnable;
        public bool AutoBlackLevelEnable
        {
            get { return _autoBlackLevelEnable; }
            set
            {
                if (_autoBlackLevelEnable != value)
                {
                    _autoBlackLevelEnable = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " auto_black=" + Convert.ToByte(_autoBlackLevelEnable));
                    RaisePropertyChanged("AutoBlackLevelEnable");
                }
            }
        }

        private int _fallGradientIndex;
        public int FallGradientIndex
        {
            get { return _fallGradientIndex; }
            set
            {
                if (_fallGradientIndex != value)
                {
                    _fallGradientIndex = value;
                    _radio.SendCommand("display panafall set 0x" + _stream_id.ToString("X") + " gradient_index=" + _fallGradientIndex.ToString());
                    RaisePropertyChanged("FallGradientIndex");
                }
            }
        }

        private int _fallPacketTotalCount = 0;
        public int FallPacketTotalCount
        {
            get { return _fallPacketTotalCount; }
            set
            {
                if (_fallPacketTotalCount != value)
                {
                    _fallPacketTotalCount = value;
                    // only raise the property change every 100 packets (performance)
                    if (_fallPacketTotalCount % 100 == 0) RaisePropertyChanged("FallPacketTotalCount");
                }
            }
        }

        private int _fallPacketErrorCount = 0;
        public int FallPacketErrorCount
        {
            get { return _fallPacketErrorCount; }
            set
            {
                if (_fallPacketErrorCount != value)
                {
                    _fallPacketErrorCount = value;
                    RaisePropertyChanged("FallPacketErrorCount");
                }
            }
        }

        public void Close()
        {
            Debug.WriteLine("Waterfall::Close (0x" + _stream_id.ToString("X") + ")");
            _radio.SendCommand("display panafall remove 0x" + _stream_id.ToString("X"));
            _radio.RemoveWaterfall(this);
            _radio = null;
        }

        // Adds a tile from the radio
        internal void AddData(WaterfallTile tile, int packet_count)
        {
            //Debug.WriteLine("WaterfallTile auto_black: " + tile.AutoBlackLevel + "  min("+min_bin.ToString().PadLeft(4, ' ')+"): "+min + "  delta: " + (min - tile.AutoBlackLevel) + "  max("+max_bin.ToString().PadLeft(4, ' ')+"): " + max);
            // is this packet a complete tile?
            if (tile.Data.Length == tile.TotalBinsInFrame)
            {
                // yes -- mark it and signal that it is ready
                tile.IsFrameComplete = true;
                OnDataReady(this, tile);
                FallPacketTotalCount++;

            }
            else
            {
                // no - add it to the list to be processed
                tile.IsFrameComplete = false;
                AddFragmentedTile(tile);
            }   
        }

        public delegate void DataReadyEventHandler(Waterfall fall, WaterfallTile tile);
        public event DataReadyEventHandler DataReady;
        private void OnDataReady(Waterfall fall, WaterfallTile tile)
        {
            if (DataReady != null)
                DataReady(fall, tile);
        }

        private void AddFragmentedTile(WaterfallTile new_tile)
        {
            // is there an existing incomplete tile with a matching Timecode?
            lock (_fragmentedWaterfallTileDict)
            {
                if (_fragmentedWaterfallTileDict.ContainsKey(new_tile.Timecode))
                {
                    // yes -- lets combine the info
                    WaterfallTile tile = _fragmentedWaterfallTileDict[new_tile.Timecode];

                    // make sure this is already resized (it should be)
                    Debug.Assert(tile.Data.Length == new_tile.TotalBinsInFrame);

                    // copy the data
                    Array.Copy(new_tile.Data, 0, tile.Data, new_tile.FirstBinIndex, new_tile.Width);
                    tile.Width += new_tile.Width;

                    // is all of the data here?
                    if (tile.Width == tile.TotalBinsInFrame)
                    {
                        // yes -- mark it complete and send it along!
                        tile.IsFrameComplete = true;
                        OnDataReady(this, tile);

                        // remove this tile from the fragmented list
                        _fragmentedWaterfallTileDict.Remove(tile.Timecode);
                    }
                }
                else // no -- lets just add this tile to the incomplete list
                {
                    // make a data array with room for the other pieces
                    ushort[] resized_data = new ushort[new_tile.TotalBinsInFrame];

                    // copy the data to the appropriate spot
                    Array.Copy(new_tile.Data, 0, resized_data, new_tile.FirstBinIndex, new_tile.Data.Length);

                    // now assign the newly resized array to the tile
                    new_tile.Data = resized_data;

                    // insert the new tile into the incomplete dictionary
                    _fragmentedWaterfallTileDict.Add(new_tile.Timecode, new_tile);

                    FallPacketTotalCount++;
                }

                // remove stale incomplete tiles (due to packet loss for example) periodically
                if (++_cleanupCounter % CLEANUP_COUNT_TRIGGER == 0)
                    CleanUpFragmentedTiles(new_tile.Timecode);
            }
        }

        private int _cleanupCounter = 0;
        private const int CLEANUP_COUNT_TRIGGER = 1000;
        private const int CLEANUP_TIMECODE_DELTA_THRESHOLD = 10;

        private void CleanUpFragmentedTiles(uint last_timecode)
        {
            // note -- only called within a lock, so no need to lock here
            foreach (uint timecode_key in _fragmentedWaterfallTileDict.Keys.ToArray<uint>())
            {
                if (timecode_key < last_timecode - CLEANUP_TIMECODE_DELTA_THRESHOLD &&
                    last_timecode > CLEANUP_TIMECODE_DELTA_THRESHOLD) // make sure we don't have uint wrapping issues
                {
                    _fragmentedWaterfallTileDict.Remove(timecode_key);
                    FallPacketErrorCount++;
                }
            }
        }
        
        private bool _fullStatusUpdate = false;
        public void StatusUpdate(string s)
        {
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Waterfall::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "x_pixels":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if ((int)temp != _width)
                            {
                                //Size = new Size((int)temp, _size.Height);
                                //if (buf.Length < _size.Width)
                                  //  buf = new ushort[(int)_size.Width];

                                //RaisePropertyChanged("Size");
                            }
                        }
                        break;

                    case "y_pixels":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if ((int)temp != _height)
                            {
                                //Size = new Size(_size.Width, (int)temp);
                                //if (buf.Length < _size.Width)
                                  //  buf = new ushort[(int)_size.Width];
                               // RaisePropertyChanged("Size");
                            }
                        }
                        break;

                    case "center":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _centerFreq = temp; // in MHz
                            RaisePropertyChanged("CenterFreq");
                            //Debug.WriteLine("Status: Pan 0x" + _stream_id.ToString("X") + " Freq:" + _centerFreq.ToString("f6"));
                        }
                        break;

                    case "client_handle":
                        {
                            uint temp;
                            bool b = StringHelper.TryParseInteger(value, out temp);

                            if (!b) continue;

                            _clientHandle = temp;
                            RaisePropertyChanged("ClientHandle");
                        }
                        break;

                    //case "bandwidth":
                    //    {
                    //        double temp;
                    //        bool b = StringHelper.DoubleTryParse(value, out temp);
                    //        if (!b)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        _bandwidth = temp; // in MHz
                    //        RaisePropertyChanged("Bandwidth");
                    //    }
                    //    break;

                    //case "min_dbm":
                    //    {
                    //        double temp;
                    //        bool b = StringHelper.DoubleTryParse(value, out temp);
                    //        if (!b)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        _lowDbm = temp;
                    //        RaisePropertyChanged("MinDbm");
                    //    }
                    //    break;

                    //case "max_dbm":
                    //    {
                    //        double temp;
                    //        bool b = StringHelper.DoubleTryParse(value, out temp);
                    //        if (!b)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        _highDbm = temp;
                    //        RaisePropertyChanged("MaxDbm");
                    //    }
                    //    break;

                    //case "fps":
                    //    {
                    //        int temp;
                    //        bool b = int.TryParse(value, out temp);
                    //        if (!b)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        _fps = temp;
                    //        RaisePropertyChanged("FPS");
                    //    }
                    //    break;

                    //case "average":
                    //    {
                    //        int temp;
                    //        bool b = int.TryParse(value, out temp);
                    //        if (!b)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        _average = temp;
                    //        RaisePropertyChanged("Average");
                    //    }
                    //    break;

                    //case "rfgain":
                    //    {
                    //        double temp;
                    //        bool b = StringHelper.DoubleTryParse(value, out temp);
                    //        if (!b)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        _rfGain = temp;
                    //        RaisePropertyChanged("RFGain");
                    //    }
                    //    break;

                    //case "rxant":
                    //    {
                    //        _rxant = value;
                    //        RaisePropertyChanged("RXAnt");
                    //    }
                    //    break;

                    //case "wide":
                    //    {
                    //        byte temp;
                    //        bool b = byte.TryParse(value, out temp);
                    //        if (!b || temp > 1)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate -- wide: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        _wide = Convert.ToBoolean(temp);
                    //        RaisePropertyChanged("Wide");
                    //    }
                    //    break;

                    //case "loopa":
                    //    {
                    //        byte temp;
                    //        bool b = byte.TryParse(value, out temp);
                    //        if (!b || temp > 1)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate -- loopa: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        _loopA = Convert.ToBoolean(temp);
                    //        RaisePropertyChanged("LoopA");
                    //    }
                    //    break;

                    //case "loopb":
                    //    {
                    //        byte temp;
                    //        bool b = byte.TryParse(value, out temp);
                    //        if (!b || temp > 1)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate -- loopb: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        _loopB = Convert.ToBoolean(temp);
                    //        RaisePropertyChanged("LoopB");
                    //    }
                    //    break;
                    //case "band":
                    //    {
                    //        string temp;
                    //        //TODO: Maybe add checks but we can read the string value without parsing
                    //        temp = value;

                    //        _band = temp;
                    //        RaisePropertyChanged("Band");
                    //    }
                    //    break;

                    //case "daxiq_channel":
                    //    {
                    //        uint temp;
                    //        bool b = uint.TryParse(value, out temp);

                    //        if (!b)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        //_radio.ClearIQStreamWaterfall(_daxIQChannel, this);

                    //        _daxIQChannel = (int)temp;
                    //        RaisePropertyChanged("DAXIQChannel");

                    //        //_radio.SetIQStreamWaterfall(_daxIQChannel, this);
                    //    }
                    //    break;

                    //case "weighted_average":
                    //    {
                    //        byte temp;
                    //        bool b = byte.TryParse(value, out temp);

                    //        if (!b)
                    //        {
                    //            Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                    //            continue;
                    //        }

                    //        _weightedAverage = Convert.ToBoolean(temp);
                    //        RaisePropertyChanged("WeightedAverage");
                    //    }
                    //    break;

                    case "panadapter":
                        {
                            uint pan_id;
                            bool b = StringHelper.TryParseInteger(value, out pan_id);

                            if (!b)
                            {
                                Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _parentPanadapterStreamID = pan_id;
                            _fullStatusUpdate = true;
                        }
                        break;
                    case "line_duration":
                        {
                            int line_duration;

                            bool b = int.TryParse(value, out line_duration);

                            if (!b)
                            {
                                Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _fallLineDurationMs = line_duration;
                            RaisePropertyChanged("FallLineDurationMs");
                        }
                        break;
                    case "color_gain":
                        {
                            int color_gain;

                            bool b = int.TryParse(value, out color_gain);


                            if (!b)
                            {
                                Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _fallColorGain = color_gain;
                            RaisePropertyChanged("FallColorGain");

                        }
                        break;
                    case "black_level":
                        {
                            int black_level;

                            bool b = int.TryParse(value, out black_level);


                            if (!b)
                            {
                                Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _fallBlackLevel = (ushort)black_level;
                            RaisePropertyChanged("FallBlackLevel");

                        }
                        break;
                    case "auto_black":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Waterfall::StatusUpdate -- loopb: Invalid value (" + kv + ")");
                                continue;
                            }

                            _autoBlackLevelEnable = Convert.ToBoolean(temp);
                            RaisePropertyChanged("AutoBlackLevelEnable");
                        }
                        break;
                    case "gradient_index":
                        {
                            int gradient_index;

                            bool b = int.TryParse(value, out gradient_index);


                            if (!b)
                            {
                                Debug.WriteLine("Waterfall::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _fallGradientIndex = (ushort)gradient_index;
                            RaisePropertyChanged("FallGradientIndex");


                        }
                        break;
                    
                    case "bandwidth":
                    case "rfgain":
                    case "rxant":
                    case "wide":
                    case "loopa":
                    case "loopb":
                    case "band":
                    case "daxiq":
                    case "daxiq_rate":
                    case "capacity":
                    case "available":
                    case "xvtr":
                    case "band_zoom":
                    case "segment_zoom":
                    case "daxiq_channel":
                        // keep these from showing up in the debug output
                        break;

                    default:
                        Debug.WriteLine("Waterfall::StatusUpdate: Key not parsed (" + kv + ")");
                        break;
                }
            }

            if (_fullStatusUpdate && !_ready)
                CheckReady();
        }

        private bool _ready = false;
        internal bool Ready
        {
            get { return _ready; }
        }

        public void CheckReady()
        {
            if (_ready) return;

            Panadapter pan = _radio.FindPanadapterByStreamID(_parentPanadapterStreamID);
            if (!_ready && pan != null)
            {
                _ready = true;
                _radio.OnWaterfallAdded(this);
                pan.CheckReady();
            }
        }
    }
}
