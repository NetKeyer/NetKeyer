///*!	\file Panadapter.cs
// *	\brief Represents a single Panadapter display
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2012-03-05
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Diagnostics;
using System.Globalization;

using Flex.UiWpfFramework.Mvvm;
using Flex.Util;
using System.Net;

namespace Flex.Smoothlake.FlexLib
{
    public class Panadapter : ObservableObject
    {
        private Radio _radio;
        private ushort[] _buf;

        /// <summary>
        /// The Panadapter object constructor
        /// </summary>
        /// <param name="radio">The Radio object to which to add the Panadapter to</param>
        internal Panadapter(Radio radio)
        {
            //Debug.WriteLine("Panadapter::Panadapter");
            _radio = radio;
        }

        // EW: Should this be included in the full update for a Panadapter so this doesn't have to be requested separately?
        public void GetRFGainInfo()
        {
            _radio.SendReplyCommand(new ReplyHandler(UpdateRFGainInfo), "display pan rfgain_info 0x" + _streamID.ToString("X"));
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
                    int.TryParse(vals[i], out _rf_gain_markers[i-3]);
            }

            RaisePropertyChanged("RFGainLow");
            RaisePropertyChanged("RFGainHigh");
            RaisePropertyChanged("RFGainStep");
            RaisePropertyChanged("RFGainMarkers");
        }

        private uint _clientHandle;
        public uint ClientHandle
        {
            get { return _clientHandle; }
            internal set { _clientHandle = value; }
        }

        private uint _streamID;
        public uint StreamID
        {
            get { return _streamID; }
            internal set { _streamID = value; }
        }

        internal uint _childWaterfallStreamID = uint.MaxValue;
        public uint ChildWaterfallStreamID
        {
            get { return _childWaterfallStreamID; }
            internal set { _childWaterfallStreamID = value; }
        }

        private bool _isBandZoomOn;
        public bool IsBandZoomOn
        {
            get { return _isBandZoomOn; }
            set
            {
                _isBandZoomOn = value;
                _radio.SendCommand(String.Format("display pan set 0x{0} band_zoom={1}", _streamID.ToString("X"), Convert.ToByte(value)));
                RaisePropertyChanged("IsBandZoomOn");
            }
        }

        private bool _isSegmentZoomOn;
        public bool IsSegmentZoomOn
        {
            get { return _isSegmentZoomOn; }
            set
            {
                _isSegmentZoomOn = value;
                _radio.SendCommand(String.Format("display pan set 0x{0} segment_zoom={1}", _streamID.ToString("X"), Convert.ToByte(value)));
                RaisePropertyChanged("IsSegmentZoomOn");
            }
        }

        private bool _wnb_on = false;
        /// <summary>
        /// Enables or disables the Wideband Noise Blanker (WNB) for the Panadapter.
        /// </summary>
        public bool WNBOn
        {
            get { return _wnb_on; }
            set
            {
                if (_wnb_on != value)
                {
                    _wnb_on = value;
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " wnb=" + Convert.ToByte(value));
                    RaisePropertyChanged("WNBOn");
                }
            }
        }

        private int _wnb_level;
        /// <summary>
        /// Gets or sets the Wideband Noise Blanker (WNB) level from 0 to 100.
        /// </summary>
        public int WNBLevel
        {
            get { return _wnb_level; }
            set
            {
                int new_val = value;
                // check the limits
                if (new_val < 0) new_val = 0;
                if (new_val > 100) new_val = 100;

                if (_wnb_level != new_val)
                {
                    _wnb_level = new_val;
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " wnb_level=" + _wnb_level);
                    RaisePropertyChanged("WNBLevel");
                }
                else if (new_val != value)
                {
                    RaisePropertyChanged("WNBLevel");
                }
            }
        }

        private bool _wnb_updating = false;
        /// <summary>
        /// Gets whether the Noise Blanker is currently updating
        /// </summary>
        public bool WNBUpdating
        {
            get { return _wnb_updating; }
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
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " rxant=" + _rxant);
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
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " rfgain=" + _rfGain);
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
                    RaisePropertyChanged("RFGainStep");
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
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " daxiq_channel=" + _daxIQChannel);
                    RaisePropertyChanged("DAXIQChannel");
                }
            }
        }

        //private Size _size;
        //public Size Size
        //{
        //    get { return _size; }
        //    set
        //    {
        //        Debug.WriteLine("Panadapter::Size = " + value.Width + "x" + value.Height + " (StreamID: 0x" + _stream_id.ToString("X") + ")");
        //        if (_size != value)
        //        {
        //            int W = (int)Math.Round(value.Width);
        //            int H = (int)Math.Round(value.Height);

        //            if (buf.Length < W)
        //                buf = new ushort[W];

        //            _size = value;
        //            //Width = (int)_size.Width;
        //            //Height = (int)_size.Height;
        //            Debug.WriteLine("Radio::SendCommand(display pan set 0x" + _stream_id.ToString("X") + " xpixels=" + W + " ypixels=" + H + ")");
        //            _radio.SendCommand("display pan set 0x" + _stream_id.ToString("X") + " xpixels=" + W + " ypixels=" + H);
        //            RaisePropertyChanged("Size");
        //        }
        //    }
        //}

        private int _width;
        public int Width
        {
            get { return _width; }
            set
            {
                //Debug.WriteLine("Panadapter::Width = " + value + " (StreamID: 0x" + _stream_id.ToString("X") + ")");
                if (_width != value)
                {
                    if(_buf == null || _buf.Length < value)
                        _buf = new ushort[value];

                    _width = value;

                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " xpixels=" + _width);
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
                //Debug.WriteLine("Panadapter::Height = " + value + " (StreamID: 0x" + _stream_id.ToString("X") + ")");
                if (_height != value)
                {
                    _height = value;
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " ypixels=" + _height);
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
                _band = value;
                _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " band=" + _band);
                RaisePropertyChanged("Band");
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
                    _radio.SendReplyCommand(new ReplyHandler(SetCenterFreqReply), "display pan set 0x" + _streamID.ToString("X") + " center=" + StringHelper.DoubleToString(_centerFreq, "f6"));
                    RaisePropertyChanged("CenterFreq");
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
                Debug.WriteLine("Panadapter::SetCenterFreqReply: Invalid reply string (" + s + ")");
                return;
            }

            if (_centerFreq != temp)
            {
                _centerFreq = temp;
                RaisePropertyChanged("CenterFreq");
            }
        }

        private double _maxBandwidth;
        public double MaxBandwidth
        {
            get { return _maxBandwidth; }
        }

        private double _minBandwidth;
        public double MinBandwidth
        {
            get { return _minBandwidth; }
        }

        private double _bandwidth;
        public double Bandwidth
        {
            get { return _bandwidth; }
            set
            {
                double new_bw = value;
                double new_center = _centerFreq;

                // check bandwidth limits
                if (new_bw > _maxBandwidth) new_bw = _maxBandwidth;
                else if (new_bw < _minBandwidth) new_bw = _minBandwidth;

                if (_bandwidth != new_bw)
                {
                    _bandwidth = new_bw;
                    string cmd = "display pan set 0x" + _streamID.ToString("X") + " bandwidth=" + StringHelper.DoubleToString(new_bw, "f6");
                    if (_autoCenter) cmd += " autocenter=1";
                    _radio.SendReplyCommand(new ReplyHandler(SetBandwidthReply), cmd);
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
                    _autoCenter = value;
                RaisePropertyChanged("AutoCenter");
            }
        }

        private void SetBandwidthReply(int seq, uint resp_val, string s)
        {
            if (resp_val == 0) return;

            double temp;
            bool b = StringHelper.TryParseDouble(s, out temp);
            if (!b)
            {
                Debug.WriteLine("Panadapter::SetBandwidthReply: Invalid reply string (" + s + ")");
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
                    _radio.SendReplyCommand(new ReplyHandler(SetLowDbmReply), "display pan set 0x" + _streamID.ToString("X") + " min_dbm=" + StringHelper.DoubleToString(_lowDbm, "f6"));
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
                Debug.WriteLine("Panadapter::SetMinDbmReply: Invalid reply string (" + s + ")");
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
                    _radio.SendReplyCommand(new ReplyHandler(SetHighDbmReply), "display pan set 0x" + _streamID.ToString("X") + " max_dbm=" + StringHelper.DoubleToString(_highDbm, "f6"));
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
                Debug.WriteLine("Panadapter::SetHighDbmReply: Invalid reply string (" + s + ")");
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
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " fps=" + value);
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
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " average=" + value);
                    RaisePropertyChanged("Average");
                }
            }
        }

        private string[] _rx_antenna_list;
        /// <summary>
        /// A list of the available RX Antenna ports on 
        /// the radio, i.e. "ANT1", "ANT2", "RX_A", 
        /// "RX_B", "XVTR"
        /// </summary>
        public string[] RXAntennaList
        {
            get { return (string[])_rx_antenna_list.Clone(); }
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
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("x") + " weighted_average=" + Convert.ToByte(_weightedAverage));
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

        private bool _loggerDisplayEnabled = false;
        public bool LoggerDisplayEnabled
        {
            get { return _loggerDisplayEnabled; }
            set
            {
                if (_loggerDisplayEnabled != value)
                {
                    _loggerDisplayEnabled = value;
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("x") + " n1mm_spectrum_enable=" + Convert.ToByte(_loggerDisplayEnabled));
                    RaisePropertyChanged("LoggerDisplayEnabled");
                }
            }
        }

        private IPAddress _loggerDisplayIPAddress = null;
        public IPAddress LoggerDisplayIPAddress
        {
            get { return _loggerDisplayIPAddress; }
            set
            {
                if (_loggerDisplayIPAddress != value)
                {
                    _loggerDisplayIPAddress = value;
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("x") + " n1mm_address=" + _loggerDisplayIPAddress.ToString());
                    RaisePropertyChanged("LoggerDisplayIPAddress");
                }
            }
        }

        private ushort _loggerDisplayPort = 0;
        public ushort LoggerDisplayPort
        {
            get { return _loggerDisplayPort; }
            set
            {
                if (_loggerDisplayPort != value)
                {
                    _loggerDisplayPort = value;
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("x") + " n1mm_port=" + _loggerDisplayPort);
                    RaisePropertyChanged("LoggerDisplayPort");
                }
            }
        }

        private byte _loggerDisplayRadioNum = 0;
        public byte LoggerDisplayRadioNum
        {
            get { return _loggerDisplayRadioNum; }
            set
            {
                if (_loggerDisplayRadioNum != value)
                {
                    _loggerDisplayRadioNum = value;
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("x") + " n1mm_radio=" + _loggerDisplayRadioNum);
                    RaisePropertyChanged("LoggerDisplayRadioNum");
                }
            }
        }

        private string _xvtr;
        public string XVTR
        {
            get { return _xvtr; }
            set
            {
                if (_xvtr != value)
                {
                    _xvtr = value;
                    RaisePropertyChanged("XVTR");
                }
            }
        }

        private string _preamp;
        public string Preamp
        {
            get { return _preamp; }
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
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " loopa=" + Convert.ToByte(_loopA));
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
                    _radio.SendCommand("display pan set 0x" + _streamID.ToString("X") + " loopb=" + Convert.ToByte(_loopB));
                    RaisePropertyChanged("LoopB");
                }
            }
        }

        private int _fftPacketTotalCount = 0;
        public int FFTPacketTotalCount
        {
            get { return _fftPacketTotalCount; }
            set
            {
                if (_fftPacketTotalCount != value)
                {
                    _fftPacketTotalCount = value;
                    // only raise the property change every 100 packets (performance)
                    if (_fftPacketTotalCount % 100 == 0) RaisePropertyChanged("FFTPacketTotalCount");
                }
            }
        }

        private int _fftPacketErrorCount = 0;
        public int FFTPacketErrorCount
        {
            get { return _fftPacketErrorCount; }
            set
            {
                if (_fftPacketErrorCount != value)
                {
                    _fftPacketErrorCount = value;
                    RaisePropertyChanged("FFTPacketErrorCount");
                }
            }
        }

        bool _closing = false;
        public void Close()
        {
            // if we have already called close, don't do this stuff again
            if (_closing) return;

            // set the closing flag
            _closing = true;

            Debug.WriteLine("Panadapter::Close (0x" + _streamID.ToString("X") + ")");
            _radio.SendCommand("display pan remove 0x" + _streamID.ToString("X"));
            _radio.RemovePanadapter(this);
        }

        private uint _current_frame = 0;
        private int _frame_bins = 0;
        private const int ERROR_THRESHOLD = 10;
        private bool _expecting_new_frame = true;

        // Adds data to the FFT buffer from the radio -- not intended to be used by the client
        internal void AddData(ushort[] data, uint start_bin, uint frame, int packet_count)
        {
            //Debug.WriteLine("AddData: start_bin:"+start_bin+" frame:"+frame);
            // check boundaries
           // Debug.WriteLine("Frame: {0}", frame);

            if (start_bin + data.Length > _width)
            {
                Debug.WriteLine("PAN Packet too large for current width");
                _expecting_new_frame = true;
                return;
            }

            // prevent array out of bounds exception for Array.Copy
            if (_buf == null || data.Length > _buf.Length)
            {
                // allocate a new buffer for future data
                _buf = new ushort[_width];

                // clear the bin data out
                _frame_bins = 0;
                _expecting_new_frame = true;
                return;
            }

            if ( frame < _current_frame )
            {
                // This has already been counted and we do not want an old frame
                // to interrupt a new one so we just disregard this and keep going
                return;
            }

            // Frame is changing 
            if (frame != _current_frame)
            {
                // New frame so add to count
                FFTPacketTotalCount++;

                // Is this expected ? (we just finished a previous frame)
                if (!_expecting_new_frame)
                { 
                    Debug.WriteLine("Expected frame {0} but got frame {1}", _current_frame, frame);
                    FFTPacketErrorCount++;
                }

                // Set new frame and clear the bins
                _current_frame = frame;
                _frame_bins = 0;
                _expecting_new_frame = false;
            }

            // copy data into the buffer
            Array.Copy(data, 0, _buf, start_bin, data.Length);

            // update bin data
            _frame_bins += data.Length;

            // if the buffer is full, fire the event
            if (_frame_bins == _width)
            {
                try
                {
                    OnDataReady(this, _buf);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Display OnDataReady failed with Exception: " + ex.ToString());
                }

                _expecting_new_frame = true;

                // allocate a new buffer for future data
                _buf = new ushort[_width];

                // clear the bin data out
                _frame_bins = 0;
            }
        }

        private void ProcessFFTPacketThread()
        {

        }

        public delegate void DataReadyEventHandler(Panadapter pan, ushort[] data);
        public event DataReadyEventHandler DataReady;
        private void OnDataReady(Panadapter pan, ushort[] data)
        {
            if (DataReady != null)
                DataReady(pan, data);
        }

        public void ClickTuneRequest(double clicked_freq_MHz)
        {
            _radio.SendCommand("slice m " + StringHelper.DoubleToString(clicked_freq_MHz, "f6") + " pan=0x" + _streamID.ToString("X"));
        }

        bool _fullStatusReceived = false;
        public void StatusUpdate(string s)
        {            
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Display::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "average":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _average = temp;
                            RaisePropertyChanged("Average");
                        }
                        break;

                    case "ant_list":
                        {
                            // We don't want to raise the property if the list did not change.  However, checking
                            // for this causes a race condition that brings up duplicate slices for some reason.
                            //if (_rx_antenna_list != null && _rx_antenna_list.SequenceEqual(value.Split(',')))
                            //    continue;

                            _rx_antenna_list = value.Split(',');
                            RaisePropertyChanged("RXAntennaList");
                        }
                        break;

                    case "band":
                        {
                            string temp;
                            //TODO: Maybe add checks but we can read the string value without parsing
                            temp = value;

                            _band = temp;
                            RaisePropertyChanged("Band");
                        }
                        break;

                    case "bandwidth":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _bandwidth = temp; // in MHz
                            RaisePropertyChanged("Bandwidth");
                        }
                        break;

                    case "band_zoom":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_isBandZoomOn == Convert.ToBoolean(temp))
                                continue;

                            _isBandZoomOn = Convert.ToBoolean(temp);
                            RaisePropertyChanged("IsBandZoomOn");
                        }
                        break;

                    case "center":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
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

                    case "daxiq_channel":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _daxIQChannel = (int)temp;
                            RaisePropertyChanged("DAXIQChannel");
                        }
                        break;

                    case "fps":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _fps = temp;
                            RaisePropertyChanged("FPS");
                        }
                        break;

                    case "loopa":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate -- loopa: Invalid value (" + kv + ")");
                                continue;
                            }

                            _loopA = Convert.ToBoolean(temp);
                            RaisePropertyChanged("LoopA");
                        }
                        break;

                    case "loopb":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate -- loopb: Invalid value (" + kv + ")");
                                continue;
                            }

                            _loopB = Convert.ToBoolean(temp);
                            RaisePropertyChanged("LoopB");
                        }
                        break;

                    case "min_bw":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _minBandwidth = temp;
                            RaisePropertyChanged("MinBandwidth");
                        }
                        break;

                    case "min_dbm":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _lowDbm = temp;
                            RaisePropertyChanged("LowDbm");
                        }
                        break;

                    case "max_bw":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _maxBandwidth = temp;
                            RaisePropertyChanged("MaxBandwidth");
                        }
                        break;

                    case "max_dbm":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _highDbm = temp;
                            RaisePropertyChanged("HighDbm");
                        }
                        break;

                    case "pre":
                        {
                            _preamp = value;
                            RaisePropertyChanged("Preamp");
                        }
                        break;

                    case "rfgain":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _rfGain = temp;
                            RaisePropertyChanged("RFGain");
                        }
                        break;

                    case "rxant":
                        {
                            _rxant = value;
                            RaisePropertyChanged("RXAnt");
                        }
                        break;

                    case "segment_zoom":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_isSegmentZoomOn == Convert.ToBoolean(temp))
                                continue;

                            _isSegmentZoomOn = Convert.ToBoolean(temp);
                            RaisePropertyChanged("IsSegmentZoomOn");
                        }
                        break;

                    case "waterfall":
                        {
                            uint fall_id;
                            bool b = StringHelper.TryParseInteger(value, out fall_id);

                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _childWaterfallStreamID = fall_id;
                            
                            _fullStatusReceived = true;
                            //RaisePropertyChanged("ChildWaterfallStreamID");
                        }
                        break;

                    case "weighted_average":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _weightedAverage = Convert.ToBoolean(temp);
                            RaisePropertyChanged("WeightedAverage");
                        }
                        break;

                    case "wide":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate -- wide: Invalid value (" + kv + ")");
                                continue;
                            }

                            _wide = Convert.ToBoolean(temp);
                            RaisePropertyChanged("Wide");
                        }
                        break;

                    case "wnb":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_wnb_on == Convert.ToBoolean(temp))
                                continue;

                            _wnb_on = Convert.ToBoolean(temp);
                            RaisePropertyChanged("WNBOn");
                        }
                        break;

                    case "wnb_level":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 100)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_wnb_level == (int)temp)
                                continue;

                            _wnb_level = (int)temp;
                            RaisePropertyChanged("WNBLevel");
                        }
                        break;

                    case "wnb_updating":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_wnb_updating == Convert.ToBoolean(temp))
                                continue;

                            _wnb_updating = Convert.ToBoolean(temp);
                            RaisePropertyChanged("WNBUpdating");
                        }
                        break;

                    case "x_pixels":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
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

                    case "xvtr":
                        {
                            _xvtr = value;
                            RaisePropertyChanged("XVTR");
                        }
                        break;


                    case "y_pixels":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Panadapter::StatusUpdate: Invalid value (" + kv + ")");
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

                    case "daxiq_rate":
                    case "capacity":
                    case "available":
                        // keep these from showing up in the debug output
                        break;

                    default:
                        Debug.WriteLine("Panadapter::StatusUpdate: Key not parsed (" + kv + ")");
                        break;
                }
            }

            if (_fullStatusReceived && !_ready)
                CheckReady();
        }

        private bool _ready = false;
        internal bool Ready
        {
            get { return _ready; }
        }

        public void CheckReady()
        {
            if (!_ready && _childWaterfallStreamID == 0) // This means that we got an panadapter status that said that there is no waterfall object associated 
            {
                _ready = true;
                _radio.OnPanadapterAdded(this, null);

                lock (_radio.SliceList)
                {
                    foreach (Slice s in _radio.SliceList)
                    {
                        if (s.PanadapterStreamID == _streamID)
                            s.CheckReady();
                    }
                }
            }
            else
            {
                Waterfall fall = _radio.FindWaterfallByParentStreamID(_streamID);
                if (!_ready && fall != null && fall.Ready)
                {
                    _ready = true;
                    _radio.OnPanadapterAdded(this, fall);

                    lock (_radio.SliceList)
                    {
                        foreach (Slice s in _radio.SliceList)
                        {
                            if (s.PanadapterStreamID == _streamID)
                            {
                                s.CheckReady();
                            }
                        }
                    }
                }
            }
        }
    }

}
