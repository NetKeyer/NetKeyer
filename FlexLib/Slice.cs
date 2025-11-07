// ****************************************************************************
///*!	\file Slice.cs
// *	\brief Represents a single Slice receiver
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
using System.Collections.Generic;
using System.Linq;

using System.Diagnostics;
using Flex.UiWpfFramework.Mvvm;
using System.Globalization;
using Flex.Util;


namespace Flex.Smoothlake.FlexLib
{
    public enum AGCMode
    {
        None,
        Off,
        Slow,
        Medium,
        Fast
    }

    public class Slice : ObservableObject
    {
        private Radio _radio;
        public Radio Radio
        {
            get { return _radio; }
        }

        private List<Meter> _meters;

        internal Slice(Radio radio)
        {
            this._meters = new List<Meter>();
            this._radio = radio;
        }
        
        private int _index = -1;
        /// <summary>
        /// Gets the slice index of the Slice.
        /// </summary>
        public int Index
        {
            get { return _index; }
            internal set { _index = value; }
        }

        private string _letter;
        public string Letter
        {
            get { return _letter; }
            internal set { _letter = value; }
        }

        private uint _clientHandle;
        public uint ClientHandle
        {
            get { return _clientHandle; }
            internal set { _clientHandle = value; }
        }

        private List<string> _mode_list = new List<string>();
        /// <summary>
        /// A list of available modes for this slice
        /// </summary>
        public List<string> ModeList
        {
            get { return new List<string>(_mode_list); }
            set { _mode_list = value; }
        }

        private void UpdateIndex(int seq, uint resp_val, string s)
        {
            if (resp_val != 0) return;

            bool b = int.TryParse(s, out _index);

            if (!b)
            {
                Debug.WriteLine("Slice::UpdateIndex-Error parsing Index (" + s + ")");
                return;
            }
            
            _radio.AddSlice(this);
        }

        private bool _active;
        /// <summary>
        /// Gets or sets the whether the Slice is the Active Slice.
        /// </summary>
        public bool Active
        {
            get { return _active; }
            set
            {
                _UpdateActive(value, update_radio:true);
            }
        }

        private void _UpdateActive(bool new_active_value, bool update_radio)
        {
            if (_active != new_active_value)
            {
                _active = new_active_value;

                if(update_radio)
                    _radio.SendCommand("slice set " + _index + " active=" + Convert.ToByte(_active));
                RaisePropertyChanged("Active");

                _radio.UpdateActiveSlice();
            }
        }

        private Panadapter _panadapter;
        /// <summary>
        /// Gets the Panadapter object that the Slice is associated with.
        /// </summary>
        public Panadapter Panadapter
        {
            get { return _panadapter; }
            internal set { _panadapter = value; }
        }

        private uint _panadapterStreamID = 1;
        /// <summary>
        /// Gets the Stream ID of the Panadapter object that the Slice 
        /// is associated with.
        /// </summary>
        public uint PanadapterStreamID
        {
            get { return _panadapterStreamID; }
            internal set
            {
                _panadapterStreamID = value;
            }
        }

        private string _owner;
        public string Owner
        {
            get { return _owner; }
            set
            {
                _owner = value;
                RaisePropertyChanged("Owner");
            }
        }

        private string[] _rx_ant_list;
        /// <summary>
        /// A list of the available RX Antenna ports on 
        /// the radio, i.e. "ANT1", "ANT2", "RX_A", 
        /// "RX_B", "XVTR"
        /// </summary>
        public string[] RXAntList
        {
            get
            {
                if (_rx_ant_list == null)
                    return null;
                else
                    return (string[])_rx_ant_list.Clone();
            }
        }
        
        //TODO:  The antenna selections should be enums?
        private string _rxant;
        /// <summary>
        /// Gets or sets the receive antenna for the slice as a string:  
        /// "ANT1", "ANT2", "RX_A", "RX_B", "XVTR"
        /// </summary>
        public string RXAnt
        {
            get { return _rxant; }
            set
            {
                if (_rxant != value)
                {
                    _rxant = value;
                   if(_rxant != null)
                        _radio.SendCommand("slice set " + _index + " rxant=" + _rxant);
                    RaisePropertyChanged("RXAnt");
                }
            }
        }

        private int _rfGain;
        /// <summary>
        /// Sets the RF gain for the SCU on which this Slice is on (-10, 0 , 10, 20, 30)
        /// </summary>
        [Obsolete("This property is no longer used. Use the RFGain property in Panadapter.cs instead.")]
        public int RFGain
        {
            get { return _rfGain; }
            set
            {
                if (_rfGain != value)
                {
                    _rfGain = value;
                    _radio.SendCommand("slice set" + _index + " rfgain=" + _rfGain);
                    RaisePropertyChanged("RFGain");
                }
            }
        }

        private string[] _tx_ant_list;
        /// <summary>
        /// A list of the available RX Antenna ports on 
        /// the radio, i.e. "ANT1", "ANT2", "RX_A", 
        /// "RX_B", "XVTR"
        /// </summary>
        public string[] TXAntList
        {
            get
            {
                if (_tx_ant_list == null)
                    return new string[] { "ANT1", "ANT2", "XVTR" };
                else
                    return (string[])_tx_ant_list.Clone();
            }
        }

        private string _txant;
        /// <summary>
        /// Gets or sets the transmit antenna for the slice as a string:
        /// "ANT1", "ANT2", "XVTR"
        /// </summary>
        public string TXAnt
        {
            get { return _txant; }
            set
            {
                _UpdateTXAnt(value, update_radio: true);
            }
        }

        private void _UpdateTXAnt(string tx_ant, bool update_radio)
        {
            if (_txant != tx_ant)
            {
                _txant = tx_ant;
                if (update_radio && _txant != null)
                    _radio.SendCommand("slice set " + _index + " txant=" + _txant);
                RaisePropertyChanged("TXAnt");

                if (_isTransmitSlice)
                    _radio.UpdateActiveAmplifier();
            }
        }

        private bool _wide;
        /// <summary>
        /// Gets the 'wide' state of the radio, if applicable.  When true,
        /// the receive preselector filters in the radio are bypassed.
        /// </summary>
        public bool Wide
        {
            get { return _wide; }
            internal set
            {
                _wide = value;
                RaisePropertyChanged("Wide");
            }
        }

        /// <summary>
        /// Gets or sets the demodulation mode for the slice as a string: 
        /// "USB", "DIGU", "LSB", "DIGL", "CW", "DSB", "AM", "SAM", "FM"
        /// </summary>
        private string _demodMode = "USB";
        public string DemodMode
        {
            get { return _demodMode; }
            set
            {
                if (_demodMode != value && value != null)
                {
                    _demodMode = value.ToUpper();
                    _radio.SendCommand("slice set " + _index + " mode=" + _demodMode);
                    RaisePropertyChanged("DemodMode");
                }
            }
        }

        private bool _lock = true;
        /// <summary>
        /// Gets or sets whether or not the Slice is locked.  When locked, 
        /// the Slice frequency cannot be changed.
        /// </summary>
        public bool Lock
        {
            get { return _lock; }
            set 
            {
                if (_lock != value)
                {
                    _lock = value;
                    string cmd = "slice lock " + _index;
                    if (!_lock) cmd = "slice unlock " + _index;
                    _radio.SendCommand(cmd);
                    RaisePropertyChanged("Lock");
                }
            }
        }

        private bool _autoPan = true;
        public bool AutoPan
        {
            get { return _autoPan; }
            set
            {
                if (_autoPan != value)
                {
                    _autoPan = value;
                    RaisePropertyChanged("AutoPan");
                }
            }
        }

        private int _daxChannel;
        /// <summary>
        /// Gets or sets the DAX Channel for the Slice, from 0 to 8
        /// </summary>
        public int DAXChannel
        {
            get { return _daxChannel; }
            set
            {
                if (_daxChannel != value)
                {
                    _daxChannel = value;
                    _radio.SendCommand("slice set " + _index + " dax=" + _daxChannel);
                    RaisePropertyChanged("DAXChannel");
                }
            }
        }

        private double _freq; // the frequency in MHz
        /// <summary>
        /// The frequency of the Slice in MHz
        /// </summary>
        public double Freq
        {
            get { return _freq; }
            set
            {
                if (_lock)
                {
                    RaisePropertyChanged("Freq");
                    return;
                }

                //if (value < 0.01 || // check low limit
                //    (value > 122.88 - 0.01 && value < 122.88 + 0.01) || // check middle range
                //    (value > 245.76 - 0.01)) // check high limit
                //{
                //    RaisePropertyChanged("Freq");
                //    return;
                //}

                if (_freq != value)
                {
                    _freq = value;

                    // Change the diversity child frequency so that does not appear to lag behind
                    if (_diversity_on && !_diversity_child && _diversitySlicePartner != null)
                    {
                        if (_diversitySlicePartner._freq != _freq)
                        {
                            _diversitySlicePartner._freq = _freq;
                            _diversitySlicePartner.RaisePropertyChanged("Freq");
                        }
                    }

                    string cmd = "slice tune " + _index + " " + StringHelper.DoubleToString(_freq, "f6");
                    if(!_autoPan) cmd += " autopan=0";
                    _radio.SendReplyCommand(new ReplyHandler(SetFreqReply), cmd);
                    RaisePropertyChanged("Freq");
                }
            }
        }

        private void SetFreqReply(int seq, uint resp_val, string s)
        {
            if (resp_val == 0) return;

            double temp;
            bool b = StringHelper.TryParseDouble(s, out temp);
            if (!b)
            {
                Debug.WriteLine("Slice::SetFreqReply: Invalid reply string (" + s + ")");
                return;
            }

            _freq = temp;
            RaisePropertyChanged("Freq");
        }

        private void _SetFilter(int low, int high)
        {
            if (low >= high) return;

            if (_filterLow != low || _filterHigh != high)
            {
                _filterLow = low;
                _filterHigh = high;
                _radio.SendCommand("filt " + _index + " " + low + " " + high);
            }
        }

        private int _rttyMark;
        /// <summary>
        /// Gets or sets the Slice RTTY Mark offset
        /// </summary>
        public int RTTYMark
        {
            get { return _rttyMark; }
            set
            {
                if (_rttyMark != value)
                {
                    _rttyMark = value;
                    _radio.SendCommand("slice set " + _index + " rtty_mark=" + _rttyMark);
                    RaisePropertyChanged("RTTYMark");
                }
            }
        }

        private int _rttyShift;
        /// <summary>
        /// Gets or sets the Slice RTTY Shift offset
        /// </summary>
        public int RTTYShift
        {
            get { return _rttyShift; }
            set
            {
                if (_rttyShift != value)
                {
                    _rttyShift = value;
                    _radio.SendCommand("slice set " + _index + " rtty_shift=" + _rttyShift);
                    RaisePropertyChanged("RTTYShift");
                }
            }
        }

        private int _diglOffset;
        /// <summary>
        /// Gets or sets the Slice DIGL offset
        /// </summary>
        public int DIGLOffset
        {
            get { return _diglOffset; }
            set
            {
                if (_diglOffset != value)
                {
                    _diglOffset = value;
                    _radio.SendCommand("slice set " + _index + " digl_offset=" + _diglOffset);
                    RaisePropertyChanged("DIGLOffset");
                }
            }
        }

        private int _diguOffset;
        /// <summary>
        /// Gets or sets the Slice DIGL offset
        /// </summary>
        public int DIGUOffset
        {
            get { return _diguOffset; }
            set
            {
                if (_diguOffset != value)
                {
                    _diguOffset = value;
                    _radio.SendCommand("slice set " + _index + " digu_offset=" + _diguOffset);
                    RaisePropertyChanged("DIGUOffset");
                }
            }
        }

        private int _filterLow;
        /// <summary>
        /// Gets or sets the Slice receive filter low cut in Hz
        /// </summary>
        public int FilterLow
        {
            get { return _filterLow; }
            set
            {
                if (_demodMode == "FM" || _demodMode == "NFM" )
                {
                    // don't allow FM filter width to be changed via the API
                    Debug.WriteLine("Cannot change RX filter width when in FM mode.");
                    RaisePropertyChanged("FilterLow");
                    return;
                }

                int new_cut = value; 
                if (new_cut > _filterHigh - 10) new_cut = _filterHigh - 10;
                switch(_demodMode)
                {
                    case "LSB":
                    case "DIGL":
                        if (new_cut < -12000) new_cut = -12000;
                        break;
                    case "CW":
                        if (new_cut < -12000 - _radio.CWPitch) 
                            new_cut = -12000 - _radio.CWPitch;
                        break;
                    case "RTTY":
                        if (new_cut < -12000 + _rttyMark)
                            new_cut = -12000 + _rttyMark;
                        if (new_cut > -(50 + _rttyShift))
                            new_cut = -(50 + _rttyShift);
                        break;
                    case "DSB":
                    case "AM":
                    case "SAM":
                    case "FM":
                    case "NFM":
                    case "DFM":
                    case "DSTR":
                    case "AME":
                        if (new_cut < -12000) new_cut = -12000;
                        if (new_cut > -10) new_cut = -10;
                        break;
                    case "USB":
                    case "DIGU":
                    case "FDV":
                    default:
                        if (new_cut < 0) new_cut = 0;
                        break;
                }

                if (_filterLow != new_cut)
                {
                    _SetFilter(new_cut, _filterHigh);
                    RaisePropertyChanged("FilterLow");
                }
                else if (new_cut != value)
                {
                    RaisePropertyChanged("FilterLow");
                }
            }
        }

        private int _filterHigh;
        /// <summary>
        /// Gets or sets the Slice receive filter high cut in Hz
        /// </summary>
        public int FilterHigh
        {
            get { return _filterHigh; }
            set
            {
                if (_demodMode == "FM" || _demodMode == "NFM" )
                {
                    // don't allow FM filter width to be changed via the API
                    Debug.WriteLine("Cannot change RX filter width when in FM mode.");
                    RaisePropertyChanged("FilterHigh");
                    return;
                }
                int new_cut = value;
                if (new_cut < _filterLow + 10) new_cut = _filterLow + 10;
                switch (_demodMode)
                {
                    case "LSB":
                    case "DIGL":
                        if (new_cut > 0) new_cut = 0;
                        break;
                    case "CW":
                        if (new_cut > 12000 - _radio.CWPitch)
                            new_cut = 12000 - _radio.CWPitch;
                        break;
                    case "RTTY":
                        if (new_cut > 0 + _rttyMark)
                            new_cut = 0 + _rttyMark;

                        if (new_cut < 50)
                            new_cut = 50;
                        break;
                    case "DSB":
                    case "AM":
                    case "SAM":
                    case "FM":  
                    case "NFM":
                    case "DFM":
                    case "DSTR":
                    case "AME":
                        if (new_cut > 12000) new_cut = 12000;
                        if (new_cut < 10) new_cut = 10;
                        break;
                    case "USB":
                    case "DIGU":
                    case "FDV":
                    default:
                        if (new_cut > 12000) new_cut = 12000;
                        break;
                }

                if (_filterHigh != new_cut)
                {
                    _SetFilter(_filterLow, new_cut);
                    RaisePropertyChanged("FilterHigh");
                }
                else if (new_cut != value)
                {
                    RaisePropertyChanged("FilterHigh");
                }
            }
        }

        /// <summary>
        /// Updates the Slice receive filter bandwidth
        /// </summary>
        /// <param name="low">The filter low cut frequency in Hz</param>
        /// <param name="high">The filter high cut frequecny in Hz</param>
        public void UpdateFilter(int low, int high)
        {
            if (_demodMode == "FM" || _demodMode == "NFM")
            {
                // don't allow FM filter width to be changed via the API
                Debug.WriteLine("Cannot change RX filter width when in FM mode.");
                RaisePropertyChanged("FilterLow");
                RaisePropertyChanged("FilterHigh");
                return;
            }

            switch (_demodMode)
            {
                case "LSB":
                case "DIGL":
                    if (low < -12000) low = -12000;
                    if (high > 0) high = 0;
                    if (high - low < 50) low = high - 50;
                    break;
                case "RTTY":
                    if (low < -12000 + _rttyMark) low = -12000 + _rttyMark;
                    if (high > 0 + _rttyMark) high = 0 + _rttyMark;
                    if (high - low < 50) high = low + 50;

                    int high_limit_min = 50;
                    int low_limit_max = -(50 + _rttyShift);
                    if (low > low_limit_max)
                        low = low_limit_max;
                    if (high < high_limit_min)
                        high = high_limit_min;
                    break;
                case "CW":
                    if (low < -12000 - _radio.CWPitch) low = -12000 - _radio.CWPitch;
                    if (high > 12000 - _radio.CWPitch) high = 12000 - _radio.CWPitch;
                    if (high - low < 50) high = low + 50;
                    break;
                case "DSB":
                case "SAM":
                case "AM":
                case "FM":
                case "NFM":
                case "DFM":
                case "DSTR":
                case "AME":
                    if (low < -12000) low = -12000;
                    if (high > 12000) high = 12000;
                    if (high - low < 50) high = low + 50;
                    break;
                case "USB":
                case "DIGU":
                case "FDV":
                default:
                    if (low < 0) low = 0;
                    if (high > 12000) high = 12000;
                    if (high - low < 50) high = low + 50;
                    break;
            }

            _SetFilter(low, high);
            RaisePropertyChanged("FilterLow");
            RaisePropertyChanged("FilterHigh");
        }

        private int _audioPan = 50;
        /// <summary>
        /// Gets or sets the left-right pan for the Slice audio from 0 to 100.  
        /// A value of 50 pans evenly between left and right.
        /// </summary>
        public int AudioPan
        {
            get { return _audioPan; }
            set
            {
                int new_val = value;
                // check the limits
                if (new_val < 0) new_val = 0;
                if (new_val > 100) new_val = 100;

                if (_audioPan != new_val)
                {
                    _audioPan = new_val;
                    _radio.SendCommand("slice set " + _index + " audio_pan=" + _audioPan);
                    RaisePropertyChanged("AudioPan");
                }
                else if (new_val != value)
                {
                    RaisePropertyChanged("AudioPan");
                }
            }
        }

        private int _audioGain = 50;
        /// <summary>
        /// Sets the Slice audio level from 0 to 100.
        /// </summary>
        public int AudioGain
        {
            get { return _audioGain; }
            set
            {
                // check the limits
                int new_gain = value;
                if (new_gain < 0) new_gain = 0;
                if (new_gain > 100) new_gain = 100;

                if (_audioGain != new_gain)
                {
                    _audioGain = new_gain;
                    _radio.SendCommand("slice set " + _index + " audio_level=" + _audioGain);

                    RaisePropertyChanged("AudioGain");
                }
                else if (new_gain != value)
                {
                    RaisePropertyChanged("AudioGain");
                }
            }
        }

        private bool _mute;
        /// <summary>
        /// Gets or sets the mute state of the Slice.  When true, the
        /// Slice audio is muted.
        /// </summary>
        public bool Mute
        {
            get { return _mute; }
            set 
            {
                if (_mute != value)
                {
                    _mute = value;
                    _radio.SendCommand("slice set " + _index + " audio_mute=" + Convert.ToByte(value));
                    RaisePropertyChanged("Mute");
                }            
            }
        }

        private bool _anf_on = false;
        /// <summary>
        /// Enables or disables the auto-notch filter (ANF) for the Slice.
        /// </summary>
        public bool ANFOn
        {
            get { return _anf_on; }
            set
            {
                if (_anf_on != value)
                {
                    _anf_on = value;
                    _radio.SendCommand("slice set " + _index + " anf=" + Convert.ToByte(value));
                    RaisePropertyChanged("ANFOn");
                }
            }
        }

        private bool _apf_on = false;
        /// <summary>
        /// Enables or disables the auto-peaking filter (APF) for the Slice.
        /// </summary>
        public bool APFOn
        {
            get { return _apf_on; }
            set
            {
                if (_apf_on != value)
                {
                    _apf_on = value;
                    _radio.SendCommand("slice set " + _index + " apf=" + Convert.ToByte(value));
                    RaisePropertyChanged("APFOn");
                }
            }
        }

        private int _anf_level;
        /// <summary>
        /// Gets or sets the auto-notch filter (ANF) level from 0 to 100.
        /// </summary>
        public int ANFLevel
        {
            get { return _anf_level; }
            set
            {
                // check limits
                int new_value = value;
                if (new_value > 100) new_value = 100;
                if (new_value < 0) new_value = 0;

                if (_anf_level != new_value)
                {
                    _anf_level = new_value;
                    _radio.SendCommand("slice set " + _index + " anf_level=" + _anf_level);
                    RaisePropertyChanged("ANFLevel");
                }
                else if (new_value != value)
                {
                    RaisePropertyChanged("ANFLevel");
                }
            }
        }


        private int _apf_level;
        /// <summary>
        /// Gets or sets the auto-peaking filter (APF) level from 0 to 100.
        /// </summary>
        public int APFLevel
        {
            get { return _apf_level; }
            set
            {
                // check limits
                int new_value = value;
                if (new_value > 100) new_value = 100;
                if (new_value < 0) new_value = 0;

                if (_apf_level != new_value)
                {
                    _apf_level = new_value;
                    _radio.SendCommand("slice set " + _index + " apf_level=" + _apf_level);
                    RaisePropertyChanged("APFLevel");
                }
                else if (new_value != value)
                    RaisePropertyChanged("APFLevel");
            }
        }

        private bool _diversity_on = false;
        /// <summary>
        /// Enables or disables the simple Diversity reception for the Slice.
        /// Only available for the FLEX-6700 and FLEX-6700R.
        /// </summary>
        public bool DiversityOn
        {
            get { return _diversity_on; }
            set
            {
                if (_radio.DiversityIsAllowed)
                {
                    if (_diversity_on != value)
                    {
                        _diversity_on = value;
                        _radio.SendCommand("slice set " + _index + " diversity=" + Convert.ToByte(value));
                        RaisePropertyChanged("DiversityOn");                    
                    }
                }
                else
                {
                    Debug.WriteLine("Cannot enable diversity this radio model (" + _radio.Model + ").  " +
                        "FLEX-6700 or FLEX-6700R required.");
                }
            }
        }

        private bool _diversity_child = false;
        /// <summary>
        /// Enables or disables the simple Diversity reception for the Slice.
        /// Only available for the FLEX-6700 and FLEX-6700R.
        /// </summary>
        public bool DiversityChild
        {
            get { return _diversity_child; }
            internal set
            {
                if (_radio.DiversityIsAllowed)
                {
                    if (_diversity_child != value)
                        _diversity_child = value;
                }
                else
                {
                    Debug.WriteLine("Cannot enable diversity on this radio model (" + _radio.Model + ").  " +
                        "FLEX-6600, 6700, 8600, or 6700R required.");
                }
            }
        }

        private int _diversity_index = -1;
        /// <summary>
        /// The slice index of the paired diversity slice.
        /// </summary>
        public int DiversityIndex
        {
            get { return _diversity_index; }
            internal set
            {
                if (_diversity_index != value)
                {
                    _diversity_index = value;

                    if (_diversity_on)
                    {
                        _diversitySlicePartner = _radio.FindSliceByIndex(_diversity_index);
                    }
                    else
                    {
                        _diversitySlicePartner = null;
                    }

                    RaisePropertyChanged("DiversityIndex");
                }
            }
        }

        private Slice _diversitySlicePartner;
        /// <summary>
        /// The diversity Slice is associated with this Slice, if
        /// this Slice is a diversity Slice parent or child.
        /// If this Slice is a diveristy parent, DiversitySlicePartner
        /// will be the diversity child Slice and vice versa.
        /// </summary>
        public Slice DiversitySlicePartner
        {
            get { return _diversitySlicePartner; }
            internal set
            {
                if (_diversitySlicePartner != value)
                {
                    _diversitySlicePartner = value;
                }
            }
        }

        private bool _wnb_on = false;
        /// <summary>
        /// Enables or disables the Wideband Noise Blanker (WNB) for the Slice.
        /// </summary>
        public bool WNBOn
        {
            get { return _wnb_on; }
            set
            {
                if (_wnb_on != value)
                {
                    _wnb_on = value;
                    _radio.SendCommand("slice set " + _index + " wnb=" + Convert.ToByte(value));
                    RaisePropertyChanged("WNBOn");
                }
            }
        }

        private bool _nb_on = false;
        /// <summary>
        /// Enables or disables the Noise Blanker (NB) for the Slice.
        /// </summary>
        public bool NBOn
        {
            get { return _nb_on; }
            set
            {
                if (_nb_on != value)
                {
                    _nb_on = value;
                    _radio.SendCommand("slice set " + _index + " nb=" + Convert.ToByte(value));
                    RaisePropertyChanged("NBOn");
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
                    _radio.SendCommand("slice set " + _index + " wnb_level=" + _wnb_level);
                    RaisePropertyChanged("WNBLevel");
                }
                else if (new_val != value)
                {
                    RaisePropertyChanged("WNBLevel");
                }
            }
        }

        private int _nb_level;
        /// <summary>
        /// Gets or sets the Noise Blanker (NB) level from 0 to 100.
        /// </summary>
        public int NBLevel
        {
            get { return _nb_level; }
            set
            {
                int new_val = value;
                // check the limits
                if (new_val < 0) new_val = 0;
                if (new_val > 100) new_val = 100;

                if (_nb_level != new_val)
                {
                    _nb_level = new_val;
                    _radio.SendCommand("slice set " + _index + " nb_level=" + _nb_level);
                    RaisePropertyChanged("NBLevel");
                }
                else if (new_val != value)
                {
                    RaisePropertyChanged("NBLevel");
                }
            }
        }

        private bool _nr_on = false;
        /// <summary>
        /// Enables or disables the Noise Reduction (NR) for the Slice.
        /// </summary>
        public bool NROn
        {
            get { return _nr_on; }
            set
            {
                if (_nr_on != value)
                {
                    _nr_on = value;
                    _radio.SendCommand("slice set " + _index + " nr=" + Convert.ToByte(_nr_on));
                    RaisePropertyChanged("NROn");
                }
            }
        }

        private int _nr_level;
        /// <summary>
        /// Gets or sets the Noise Reduction (NR) level from 0 to 100 for the Slice.
        /// </summary>
        public int NRLevel
        {
            get { return _nr_level; }
            set
            {
                int new_val = value;
                // check the limits
                if (new_val < 0) new_val = 0;
                if (new_val > 100) new_val = 100;

                if (_nr_level != new_val)
                {
                    _nr_level = new_val;
                    _radio.SendCommand("slice set " + _index + " nr_level=" + _nr_level);
                    RaisePropertyChanged("NRLevel");
                }
                else if (new_val != value)
                    RaisePropertyChanged("NRLevel");
            }
        }

        private AGCMode StringToAGCMode(string s)
        {
            AGCMode mode = AGCMode.None;
            switch (s.ToLower())
            {
                case "off": mode = AGCMode.Off; break;
                case "med": mode = AGCMode.Medium; break;
                case "slow": mode = AGCMode.Slow; break;
                case "fast": mode = AGCMode.Fast; break;
            }

            return mode;
        }

        private string AGCModeToString(AGCMode mode)
        {
            string s = "";
            switch (mode)
            {
                case AGCMode.Off: s = "off"; break;
                case AGCMode.Slow: s = "slow"; break;
                case AGCMode.Medium: s = "med"; break;
                case AGCMode.Fast: s = "fast"; break;
            }

            return s;
        }

        private AGCMode _agc_mode;
        /// <summary>
        /// Gets or sets the current AGC mode for the Slice.
        /// </summary>
        public AGCMode AGCMode
        {
            get { return _agc_mode; }
            set
            {
                if (_agc_mode != value)
                {
                    _agc_mode = value;
                    _radio.SendCommand("slice set " + _index + " agc_mode=" + AGCModeToString(_agc_mode));
                    RaisePropertyChanged("AGCMode");
                }
            }
        }

        private int _agc_threshold;
        public int AGCThreshold
        {
            get { return _agc_threshold; }
            set
            {

                int new_value = value;
                if (new_value > 100) new_value = 100;
                if (new_value < 0) new_value = 0;

                if (_agc_threshold != new_value)
                {
                    _agc_threshold = new_value;
                    _radio.SendCommand("slice set " + _index + " agc_threshold=" + _agc_threshold);
                    RaisePropertyChanged("AGCThreshold");
                }
                else if (new_value != value)
                    RaisePropertyChanged("AGCThreshold");
            }
        }

        private int _agc_off_level;
        public int AGCOffLevel
        {
            get { return _agc_off_level; }
            set
            {
                int new_value = value;
                if (new_value > 100) new_value = 100;
                if (new_value < 0) new_value = 0;

                if (_agc_off_level != new_value)
                {
                    _agc_off_level = new_value;
                    _radio.SendCommand("slice set " + _index + " agc_off_level=" + _agc_off_level);
                    RaisePropertyChanged("AGCOffLevel");
                }
                else if (new_value != value)
                    RaisePropertyChanged("AGCOffLevel");
            }
        }

        private bool _isTransmitSlice = false;
        public bool IsTransmitSlice
        {
            get { return _isTransmitSlice; }
            set
            {
                _UpdateIsTransmitSlice(value, update_radio:true);
            }
        }

        private void _UpdateIsTransmitSlice(bool is_transmit_slice, bool update_radio)
        {
            if (_isTransmitSlice != is_transmit_slice)
            {
                _isTransmitSlice = is_transmit_slice;
                if(update_radio)
                    _radio.SendCommand("slice set " + _index + " tx=" + Convert.ToByte(_isTransmitSlice));
                RaisePropertyChanged("IsTransmitSlice");

                _radio.UpdateTransmitSlice();

                if (_isTransmitSlice)
                    _radio.UpdateActiveAmplifier();

                _radio.UpdateGuiClientListTXSlices();
            }
        }      

        private bool _eqCompBypass = false;
        public bool EqCompBypass
        {
            get { return _eqCompBypass; }
            set
            {
                if (_eqCompBypass != value)
                {
                    _eqCompBypass = value;
                    RaisePropertyChanged("EqCompBypass");
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
                    _radio.SendCommand("slice set " + _index + " loopa=" + Convert.ToByte(_loopA));
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
                    _radio.SendCommand("slice set " + _index + " loopb=" + Convert.ToByte(_loopB));
                    RaisePropertyChanged("LoopB");
                }
            }
        }

        private bool _qsk;
        public bool QSK
        {
            get { return _qsk; }
        }

        private bool _ritOn;
        public bool RITOn
        {
            get { return _ritOn; }
            set
            {
                if (_ritOn != value)
                {
                    _ritOn = value;
                    _radio.SendCommand("slice set " + _index + " rit_on=" + Convert.ToByte(_ritOn));
                    RaisePropertyChanged("RITOn");
                }
            }
        }

        private int _ritFreq; // in Hz
        public int RITFreq
        {
            get { return _ritFreq; }
            set
            {
                // check limits
                if (value > 99999 || value < -99999)
                {
                    RaisePropertyChanged("RITFreq");
                    return;
                }

                if (_ritFreq != value)
                {
                    _ritFreq = value;
                    _radio.SendCommand("slice set " + _index + " rit_freq=" + _ritFreq);
                    RaisePropertyChanged("RITFreq");
                }
            }
        }

        private bool _xitOn;
        public bool XITOn
        {
            get { return _xitOn; }
            set
            {
                if (_xitOn != value)
                {
                    _xitOn = value;
                    _radio.SendCommand("slice set " + _index + " xit_on=" + Convert.ToByte(_xitOn));
                    RaisePropertyChanged("XITOn");
                }
            }
        }

        private int _xitFreq; // in Hz
        public int XITFreq
        {
            get { return _xitFreq; }
            set
            {
                // check limits
                if (value > 99999 || value < -99999)
                {
                    RaisePropertyChanged("XITFreq");
                    return;
                }

                if (_xitFreq != value)
                {
                    _xitFreq = value;
                    _radio.SendCommand("slice set " + _index + " xit_freq=" + _xitFreq);
                    RaisePropertyChanged("XITFreq");
                }
            }
        }

        private int _tuneStep = 10;
        public int TuneStep
        {
            get { return _tuneStep; }
            set
            {
                int new_value = value;
                if (new_value < 1)
                    new_value = 1;

                if (_tuneStep != new_value)
                {
                    _tuneStep = new_value;
                    _radio.SendCommand("slice set " + _index + " step=" + _tuneStep);
                    RaisePropertyChanged("TuneStep");
                }
                else if (new_value != value)
                {
                    RaisePropertyChanged("TuneStep");
                }
            }
        }

        private int[] _tuneStepList = { 1, 10, 50, 100, 500, 1000, 2000, 3000 };
        public int[] TuneStepList
        {
            get { return _tuneStepList; }
            set
            {
                if (value == null) return;

                if (_tuneStepList == null || 
                    (_tuneStepList != value && !_tuneStepList.SequenceEqual(value)))
                {
                    _tuneStepList = value;
                    string cmd = "slice set " + _index + "step_list=";
                    for (int i = 0; i < _tuneStepList.Length; i++)
                    {
                        cmd += _tuneStepList[i];
                        if (i != _tuneStepList.Length - 1) cmd += ",";
                    }
                    _radio.SendCommand(cmd);

                    RaisePropertyChanged("TuneStepList");
                }
            }
        }

        private bool _record_on = false;
        /// <summary>
        /// Enables or disables audio recording for the Slice.
        /// </summary>
        public bool RecordOn
        {
            get { return _record_on; }
            set
            {
                if (_record_on != value)
                {
                    _record_on = value;
                    _radio.SendCommand("slice set " + _index + " record=" + Convert.ToByte(_record_on));
                    RaisePropertyChanged("RecordOn");
                }
            }
        }

        private bool _playOn = false;
        /// <summary>
        /// Enables or disables audio recording playback for the Slice.
        /// </summary>
        public bool PlayOn
        {
            get { return _playOn; }
            set
            {
                if (_playOn != value)
                {
                    _playOn = value;
                    _radio.SendCommand("slice set " + _index + " play=" + Convert.ToByte(_playOn));
                    RaisePropertyChanged("PlayOn");
                }
            }
        }

        private bool _playEnabled = true;
        /// <summary>
        /// Enables or disables the play button for the Slice
        /// </summary>
        public bool PlayEnabled
        {
            get { return _playEnabled; }            
        }

        #region FM Properties

        private string FMToneModeToString(FMToneMode mode)
        {
            string ret_val = "";
            switch (mode)
            {
                case FMToneMode.Off: ret_val = "off"; break;
                case FMToneMode.CTCSS_TX: ret_val = "ctcss_tx"; break;
            }
            return ret_val;
        }

        private FMToneMode StringToFMToneMode(string s)
        {
            FMToneMode ret_val = FMToneMode.Off;
            switch (s.ToLower())
            {
                case "off": ret_val = FMToneMode.Off; break;
                case "ctcss_tx": ret_val = FMToneMode.CTCSS_TX; break;
            }
            return ret_val;
        }

        private string FMTXOffsetDirectionToString(FMTXOffsetDirection dir)
        {
            string ret_val = "";
            switch (dir)
            {
                case FMTXOffsetDirection.Down: ret_val = "down"; break;
                case FMTXOffsetDirection.Simplex: ret_val = "simplex"; break;
                case FMTXOffsetDirection.Up: ret_val = "up"; break;
            }
            return ret_val;
        }

        private bool TryParseFMTXOffsetDirection(string s, out FMTXOffsetDirection dir)
        {
            bool ret_val = true;
            dir = FMTXOffsetDirection.Simplex; // default the output param
            switch (s.ToLower())
            {
                case "down": dir = FMTXOffsetDirection.Down; break;
                case "simplex": dir = FMTXOffsetDirection.Simplex; break;
                case "up": dir = FMTXOffsetDirection.Up; break;
                default: ret_val = false; break;
            }
            return ret_val;
        }

        private FMToneMode _toneMode;
        public FMToneMode ToneMode
        {
            get { return _toneMode; }
            set
            {
                if (_toneMode != value)
                {
                    _toneMode = value;
                    _radio.SendCommand("slice set " + _index + " fm_tone_mode=" + FMToneModeToString(_toneMode));
                    RaisePropertyChanged("ToneMode");
                }
            }
        }

        private string _fmToneValue;
        /// <summary>
        /// Used to set the value of the FM Tone. In most cases this is the repeater tone.S
        /// </summary>
        public string FMToneValue
        {
            get { return _fmToneValue; }
            set
            {
                if (_fmToneValue != value)
                {
                    _fmToneValue = value;
                    _radio.SendCommand("slice set " + _index + " fm_tone_value=" + _fmToneValue);
                    RaisePropertyChanged("FMToneValue");
                }
            }
        }

        private int _fmDeviation;
        /// <summary>
        /// Controls the FM deviation for a given slice. If the slice is also the transmitter it updates the transmit FM deviation as well.
        /// </summary>
        public int FMDeviation
        {
            get { return _fmDeviation; }
            set
            {
                if (_fmDeviation != value)
                {
                    _fmDeviation = value;
                    _radio.SendCommand("slice set " + _index + " fm_deviation=" + _fmDeviation);
                    RaisePropertyChanged("FMDeviation");
                }
            }
        }

        private bool _dfmPreDeEmphasis;
        /// <summary>
        /// Gets or sets whether de-emphasis is enabled on RX and if the slice is the transmitter then it also controls pre-emphasis.
        /// </summary>
        public bool DFMPreDeEmphasis
        {
            get { return _dfmPreDeEmphasis; }
            set
            {
                if (_dfmPreDeEmphasis != value)
                {
                    _dfmPreDeEmphasis = value;
                    _radio.SendCommand("slice set " + _index + " dfm_pre_de_emphasis=" + Convert.ToByte(_dfmPreDeEmphasis));
                    RaisePropertyChanged("DFMPreDeEmphasis");
                }
            }
        }

        private bool _squelchOn;
        /// <summary>
        /// Gets or sets whether the squelch algorithm is on for a given slice. 
        /// </summary>
        public bool SquelchOn
        {
            get { return _squelchOn; }
            set
            {
                if (_squelchOn != value)
                {
                    _squelchOn = value;
                    _radio.SendCommand("slice set " + _index + " squelch=" + Convert.ToByte(_squelchOn));
                    RaisePropertyChanged("SquelchOn");
                }
            }
        }

        private int _squelchLevel;
        /// <summary>
        /// Gets or sets the squelch level for modes with squelch. 0 - 100 is valid.
        /// </summary>
        public int SquelchLevel
        {
            get { return _squelchLevel; }
            set
            {
                int new_val = value;
                // check the limits
                if (new_val < 0) new_val = 0;
                if (new_val > 100) new_val = 100;

                if (_squelchLevel != new_val)
                {
                    _squelchLevel = new_val;
                    _radio.SendCommand("slice set " + _index + " squelch_level=" + _squelchLevel);
                    RaisePropertyChanged("SquelchLevel");
                }
                else if (new_val != value)
                {
                    RaisePropertyChanged("SquelchLevel");
                }
            }
        }

        // an offset applied to the transmitter frequency
        private double _txOffsetFreq;
        /// <summary>
        /// Gets or sets the offset frequency of the transmitter. 
        /// </summary>
        public double TXOffsetFreq
        {
            get { return _txOffsetFreq; }
            set
            {
                if (_txOffsetFreq != value)
                {
                    _txOffsetFreq = value;
                    _radio.SendCommand("slice set " + _index + " tx_offset_freq=" + StringHelper.DoubleToString(_txOffsetFreq, "f6"));
                    RaisePropertyChanged("TXOffsetFreq");
                }
            }
        }

        // the absolute value separation of the RX from the TX in FM mode --
        // just stored in the radio and acted upon in the client by setting the TXOffsetFreq
        private double _fmRepeaterOffsetFreq;
        /// <summary>
        /// Gets or sets the OffsetFrequency used for transmitting a wide split in FM.
        /// </summary>
        public double FMRepeaterOffsetFreq
        {
            get { return _fmRepeaterOffsetFreq; }
            set
            {
                if (_fmRepeaterOffsetFreq != value)
                {
                    _fmRepeaterOffsetFreq = value;
                    _radio.SendCommand("slice set " + _index + " fm_repeater_offset_freq=" + StringHelper.DoubleToString(_fmRepeaterOffsetFreq, "f6"));
                    RaisePropertyChanged("FMRepeaterOffsetFreq");
                }
            }
        }

        private FMTXOffsetDirection _repeaterOffsetDirection = FMTXOffsetDirection.Simplex;
        /// <summary>
        /// Gets or sets the direction that the TX Offset will be applied in.
        /// </summary>
        public FMTXOffsetDirection RepeaterOffsetDirection
        {
            get { return _repeaterOffsetDirection; }
            set
            {
                if (_repeaterOffsetDirection != value)
                {
                    _repeaterOffsetDirection = value;
                    _radio.SendCommand("slice set " + _index + " repeater_offset_dir=" + FMTXOffsetDirectionToString(_repeaterOffsetDirection));
                    RaisePropertyChanged("RepeaterOffsetDirection");
                }
            }
        }

        private bool _fmTX1750 = false;
        /// <summary>
        /// Gets or sets whether the FM 1750 Hz PL tone is enabled (EU only)
        /// </summary>
        public bool FMTX1750
        {
            get { return _fmTX1750; }
            set
            {
                if (_fmTX1750 == value)
                    return;

                _fmTX1750 = value;
                _radio.SendCommand("slice set " + _index + " fm_tone_burst=" + Convert.ToByte(_fmTX1750));
                RaisePropertyChanged("FMTX1750");
            }
        }

        #endregion
                
        [Obsolete] // use Close
        public void Remove()
        {
            Close();
        }

        bool _closing = false;
        public void Close()
        {
            // if we have already called close, don't do this stuff again
            if (_closing) return;

            // set the closing flag
            _closing = true;

            _radio.SendCommand("slice remove " + _index);
            _radio.RemoveSlice(this);
        }

        #region Meter Routines

        internal void AddMeter(Meter m)
        {
            if (!_meters.Contains(m))
            {
                _meters.Add(m);

                if (m.Name == "LEVEL")
                {
                    m.DataReady += new Meter.DataReadyEventHandler(meterLEVEL_DataReady);
                }

                OnMeterAdded(m);
            }
        }

        private void meterLEVEL_DataReady(Meter meter, float data)
        {
            OnSMeterDataReady(data);
        }

        internal void RemoveMeter(Meter m)
        {
            if (_meters.Contains(m))
            {
                _meters.Remove(m);

                if (m.Name == "LEVEL")
                    m.DataReady -= meterLEVEL_DataReady;

                OnMeterRemoved(m);
            }
        }

        public delegate void MeterAddedEventHandler(Slice slc, Meter m);
        public event MeterAddedEventHandler MeterAdded;
        private void OnMeterAdded(Meter m)
        {
            if (MeterAdded != null)
                MeterAdded(this, m);
        }

        public delegate void MeterRemovedEventHandler(Slice slc, Meter m);
        public event MeterRemovedEventHandler MeterRemoved;
        private void OnMeterRemoved(Meter m)
        {
            if (MeterRemoved != null)
                MeterRemoved(this, m);
        }

        public Meter FindMeterByIndex(int index)
        {
            foreach (Meter m in _meters)
            {
                if (m.Index == index)
                    return m;
            }

            return null;
        }

        public Meter FindMeterByName(string s)
        {
            foreach (Meter m in _meters)
            {
                if (m.Name == s)
                    return m;
            }

            return null;
        }

        #endregion

        bool _fullStatusReceived = false;
        public void StatusUpdate(string s)
        {            
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                if (kv.StartsWith("waveform_status"))
                {
                    // figure out the whole command string (may include spaces)
                    string status = s.Substring(s.IndexOf(kv)+"waveform_status ".Length);
                    OnWaveformStatusReceived(status);

                    // ensure that we don't process pieces of the command as other status key/value pairs
                    return;
                }
                else
                {
                    string[] tokens = kv.Split('=');
                    if (tokens.Length != 2)
                    {
                        if (!string.IsNullOrEmpty(kv)) Debug.WriteLine($"Slice::StatusUpdate: Invalid key/value pair ({kv})");
                        continue;
                    }

                    string key = tokens[0];
                    string value = tokens[1];

                    switch (key.ToLower())
                    {
                        case "active":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _UpdateActive(Convert.ToBoolean(temp), update_radio:false);
                            }
                            break;

                        case "agc_mode":
                            {
                                AGCMode mode = StringToAGCMode(value);
                                if (mode == AGCMode.None)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid AGCMode (" + kv + ")");
                                    continue;
                                }

                                if (_agc_mode == mode)
                                    continue;

                                _agc_mode = mode;
                                RaisePropertyChanged("AGCMode");
                            }
                            break;

                        case "agc_off_level":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 100)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_agc_off_level == (int)temp)
                                    continue;

                                _agc_off_level = (int)temp;
                                RaisePropertyChanged("AGCOffLevel");
                            }
                            break;

                        case "agc_threshold":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 100)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_agc_threshold == (int)temp)
                                    continue;

                                _agc_threshold = (int)temp;
                                RaisePropertyChanged("AGCThreshold");
                            }
                            break;

                        case "anf":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_anf_on == Convert.ToBoolean(temp))
                                    continue;

                                _anf_on = Convert.ToBoolean(temp);
                                RaisePropertyChanged("ANFOn");
                            }
                            break;

                        case "anf_level":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 100)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_anf_level == (int)temp)
                                    continue;

                                _anf_level = (int)temp;
                                RaisePropertyChanged("ANFLevel");
                            }
                            break;

                        case "ant_list":
                            {
                                if (_rx_ant_list != null && _rx_ant_list.SequenceEqual(value.Split(',')))
                                    continue;

                                _rx_ant_list = value.Split(',');
                                RaisePropertyChanged("RXAntList");
                            }
                            break;

                        case "tx_ant_list":
                            {
                                if (_tx_ant_list != null && _tx_ant_list.SequenceEqual(value.Split(',')))
                                    continue;

                                _tx_ant_list = value.Split(',');
                                RaisePropertyChanged("TXAntList");
                            }
                            break;
                        case "apf":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_apf_on == Convert.ToBoolean(temp))
                                    continue;

                                _apf_on = Convert.ToBoolean(temp);
                                RaisePropertyChanged("APFOn");
                            }
                            break;

                        case "apf_level":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 100)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_apf_level == (int)temp)
                                    continue;

                                _apf_level = (int)temp;
                                RaisePropertyChanged("APFLevel");
                            }
                            break;

                        case "audio_level":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 100)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_audioGain == (int)temp)
                                    continue;

                                _audioGain = (int)temp;
                                RaisePropertyChanged("AudioGain");
                            }
                            break;

                        case "audio_mute":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_mute == Convert.ToBoolean(temp))
                                    continue;

                                _mute = Convert.ToBoolean(temp);
                                RaisePropertyChanged("Mute");
                            }
                            break;

                        case "audio_pan":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 100)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_audioPan == (int)temp)
                                    continue;

                                _audioPan = (int)temp;
                                RaisePropertyChanged("AudioPan");
                            }
                            break;

                        case "client_handle":
                            {
                                uint temp;
                                bool b = StringHelper.TryParseInteger(value, out temp);

                                if (!b) continue;

                                if (_clientHandle != temp)
                                {
                                    _clientHandle = temp;
                                    RaisePropertyChanged("ClientHandle");

                                    _radio.UpdateGuiClientListTXSlices();
                                }
                            }
                            break;

                        case "dax":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_daxChannel != (int)temp)
                                {
                                    _daxChannel = (int)temp;
                                    RaisePropertyChanged("DAXChannel");
                                }
                            }
                            break;

                        case "dfm_pre_de_emphasis":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_dfmPreDeEmphasis == Convert.ToBoolean(temp))
                                    continue;

                                _dfmPreDeEmphasis = Convert.ToBoolean(temp);
                                RaisePropertyChanged("DFMPreDeEmphasis");
                            }
                            break;

                        case "diversity":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_diversity_on == Convert.ToBoolean(temp))
                                    continue;

                                _diversity_on = Convert.ToBoolean(temp);
                                RaisePropertyChanged("DiversityOn");
                            }
                            break;

                        case "diversity_child":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_diversity_child == Convert.ToBoolean(temp))
                                    continue;

                                _diversity_child = Convert.ToBoolean(temp);
                                RaisePropertyChanged("DiversityChild");
                            }
                            break;

                        case "diversity_index":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                DiversityIndex = temp;
                            }
                            break;

                        case "filter_lo":
                            {
                                int temp; // in Hz
                                bool b = int.TryParse(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_filterLow == temp)
                                    continue;

                                _filterLow = temp;
                                RaisePropertyChanged("FilterLow");
                            }
                            break;

                        case "filter_hi":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_filterHigh == temp)
                                    continue;

                                _filterHigh = temp;
                                RaisePropertyChanged("FilterHigh");
                            }
                            break;

                        case "digl_offset":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value(" + kv + ")");
                                    continue;
                                }

                                if (_diglOffset == temp)
                                    continue;

                                _diglOffset = temp;
                                RaisePropertyChanged("DIGLOffset");
                            }
                            break;

                        case "digu_offset":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value(" + kv + ")");
                                    continue;
                                }

                                if (_diguOffset == temp)
                                    continue;

                                _diguOffset = temp;
                                RaisePropertyChanged("DIGUOffset");
                            }
                            break;    

                        case "fm_repeater_offset_freq":
                            {
                                double temp;
                                bool b = StringHelper.TryParseDouble(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid frequency (" + kv + ")");
                                    continue;
                                }

                                if (_fmRepeaterOffsetFreq == temp)
                                    continue;

                                _fmRepeaterOffsetFreq = temp;
                                RaisePropertyChanged("FMRepeaterOffsetFreq");
                            }
                            break;

                        case "fm_tone_burst":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_fmTX1750 == Convert.ToBoolean(temp))
                                    continue;

                                _fmTX1750 = Convert.ToBoolean(temp);
                                RaisePropertyChanged("FMTX1750");
                            }
                            break;

                        case "fm_tone_mode":
                            {
                                FMToneMode mode = StringToFMToneMode(value);
                                //if (mode == FMToneMode.None)
                                //{
                                //    Debug.WriteLine("Slice::StatusUpdate: Invalid FMToneMode (" + kv + ")");
                                //    continue;
                                //}

                                if (_toneMode == mode)
                                    continue;

                                _toneMode = mode;
                                RaisePropertyChanged("ToneMode");
                            }
                            break;

                        case "fm_tone_value":
                            {
                                // TODO: this should just handle the string -- hacking double handling to prevent trailing zeros until this is addressed in the firmware
                                double temp;
                                bool b = StringHelper.TryParseDouble(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_fmToneValue == temp.ToString("f1"))
                                    continue;

                                _fmToneValue = temp.ToString("f1");
                                RaisePropertyChanged("FMToneValue");
                            }
                            break;

                        case "fm_deviation":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_fmDeviation == temp)
                                    continue;

                                _fmDeviation = temp;
                                RaisePropertyChanged("FMDeviation");
                            }
                            break;

                        case "in_use":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                bool slice_in_use = Convert.ToBoolean(temp);
                                if (slice_in_use)
                                    _fullStatusReceived = true;
                            }
                            break;

                        case "index_letter":
                            {
                                _letter = value;
                                RaisePropertyChanged("Letter");
                            }
                            break;

                        case "lock":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_lock == Convert.ToBoolean(temp))
                                    continue;

                                _lock = Convert.ToBoolean(temp);
                                RaisePropertyChanged("Lock");
                            }
                            break;

                        case "loopa":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate -- loopa: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_loopA == Convert.ToBoolean(temp))
                                    continue;

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
                                    Debug.WriteLine("Slice::StatusUpdate -- loopb: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_loopB == Convert.ToBoolean(temp))
                                    continue;

                                _loopB = Convert.ToBoolean(temp);
                                RaisePropertyChanged("LoopB");
                            }
                            break;

                        case "mode":
                            {
                                if (_demodMode == value)
                                    continue;

                                _demodMode = value;
                                RaisePropertyChanged("DemodMode");
                            }
                            break;

                        case "mode_list":
                            {
                                // Uncommenting this causes the slice mode list to become blank.
                                //if (_mode_list != null && _mode_list.SequenceEqual(new List<string>(value.Split(','))))
                                //    continue;

                                _mode_list = new List<string>(value.Split(','));
                                RaisePropertyChanged("ModeList");
                            }
                            break;

                        case "nb":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_nb_on == Convert.ToBoolean(temp))
                                    continue;

                                _nb_on = Convert.ToBoolean(temp);
                                RaisePropertyChanged("NBOn");
                            }
                            break;

                        case "nb_level":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 100)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_nb_level == (int)temp)
                                    continue;

                                _nb_level = (int)temp;
                                RaisePropertyChanged("NBLevel");
                            }
                            break;

                        case "nr":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_nr_on == Convert.ToBoolean(temp))
                                    continue;

                                _nr_on = Convert.ToBoolean(temp);
                                RaisePropertyChanged("NROn");
                            }
                            break;

                        case "nr_level":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 100)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_nr_level == (int)temp)
                                    continue;

                                _nr_level = (int)temp;
                                RaisePropertyChanged("NRLevel");
                            }
                            break;

                        case "owner":
                            {
                                if (_owner == value)
                                    continue;

                                _owner = value;
                                RaisePropertyChanged("Owner");
                            }
                            break;

                        case "pan":
                            {
                                uint temp;
                                bool b = StringHelper.TryParseInteger(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_panadapterStreamID != temp)
                                {
                                    _panadapterStreamID = temp;

                                    Panadapter pan = _radio.FindPanadapterByStreamID(_panadapterStreamID);
                                    if (pan != null)
                                    {
                                        this.Panadapter = pan;
                                        _radio.OnSlicePanReferenceChange(this);
                                    }
                                    RaisePropertyChanged("Panadapter");
                                }
                            }
                            break;

                        case "play":
                            {
                                uint temp;
                                if (value == "disabled")
                                {
                                    _playEnabled = false;
                                    RaisePropertyChanged("PlayEnabled");
                                }
                                else
                                {
                                    _playEnabled = true;
                                    RaisePropertyChanged("PlayEnabled");

                                    bool b = uint.TryParse(value, out temp);
                                    if (!b || temp > 1)
                                    {

                                        // check if the string is "disabled"
                                        Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                        continue;
                                    }

                                    if (_playOn == Convert.ToBoolean(temp))
                                        continue;

                                    _playOn = Convert.ToBoolean(temp);
                                    RaisePropertyChanged("PlayOn");
                                }
                            }
                            break;

                        case "qsk":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseTransmitStatus - qsk: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_qsk == Convert.ToBoolean(temp))
                                    continue;

                                _qsk = Convert.ToBoolean(temp);
                                RaisePropertyChanged("QSK");
                            }
                            break;

                        case "record":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_record_on == Convert.ToBoolean(temp))
                                    continue;

                                _record_on = Convert.ToBoolean(temp);
                                RaisePropertyChanged("RecordOn");
                            }
                            break;

                        case "repeater_offset_dir":
                            {
                                FMTXOffsetDirection dir;
                                bool b = TryParseFMTXOffsetDirection(value, out dir);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_repeaterOffsetDirection == dir)
                                    continue;

                                _repeaterOffsetDirection = dir;
                                RaisePropertyChanged("RepeaterOffsetDirection");
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

                        case "rf_frequency":
                            {
                                double temp;
                                bool b = StringHelper.TryParseDouble(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid frequency (" + kv + ")");
                                    continue;
                                }

                                if (_freq == temp)
                                    continue;

                                _freq = temp;
                                RaisePropertyChanged("Freq");
                            }
                            break;

                        case "rit_on":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_ritOn == Convert.ToBoolean(temp))
                                    continue;

                                _ritOn = Convert.ToBoolean(temp);
                                RaisePropertyChanged("RITOn");
                            }
                            break;

                        case "rit_freq":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_ritFreq == temp)
                                    continue;

                                _ritFreq = temp;
                                RaisePropertyChanged("RITFreq");
                            }
                            break;

                        case "rtty_mark":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value(" + kv + ")");
                                    continue;
                                }

                                if (_rttyMark == temp)
                                    continue;

                                _rttyMark = temp;
                                RaisePropertyChanged("RTTYMark");
                            }
                            break;

                        case "rtty_shift":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value(" + kv + ")");
                                    continue;
                                }

                                if (_rttyShift == temp)
                                    continue;

                                _rttyShift = temp;
                                RaisePropertyChanged("RTTYShift");
                            }
                            break;

                        case "rxant":
                            {
                                if (_rxant == value)
                                    continue;

                                _rxant = value;
                                RaisePropertyChanged("RXAnt");
                            }
                            break;

                        case "squelch":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_squelchOn == Convert.ToBoolean(temp))
                                    continue;

                                _squelchOn = Convert.ToBoolean(temp);
                                RaisePropertyChanged("SquelchOn");
                            }
                            break;

                        case "squelch_level":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_squelchLevel == temp)
                                    continue;

                                _squelchLevel = temp;
                                RaisePropertyChanged("SquelchLevel");
                            }
                            break;

                        case "step":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_tuneStep == (int)temp)
                                    continue;

                                _tuneStep = (int)temp;
                                RaisePropertyChanged("TuneStep");
                            }
                            break;

                        case "step_list":
                            {
                                string[] vals = value.Split(',');
                                int[] list = new int[vals.Length];
                                bool bad_value = false;
                                for (int i = 0; i < vals.Length; i++)
                                {
                                    uint temp;
                                    bool b = uint.TryParse(vals[i], out temp);

                                    if (!b)
                                    {
                                        Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                        bad_value = true;
                                        break;
                                    }

                                    list[i] = (int)temp;
                                }

                                if (bad_value)
                                    continue;

                                if (_tuneStepList == null || !_tuneStepList.SequenceEqual(list))
                                {
                                    _tuneStepList = list;
                                    RaisePropertyChanged("TuneStepList");
                                }
                            }
                            break;

                        case "tx":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _UpdateIsTransmitSlice(Convert.ToBoolean(temp), update_radio: false);
                            }
                            break;

                        case "tx_offset_freq":
                            {
                                double temp;
                                bool b = StringHelper.TryParseDouble(value, out temp);
                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid frequency (" + kv + ")");
                                    continue;
                                }

                                if (_txOffsetFreq == temp)
                                    continue;

                                _txOffsetFreq = temp;
                                RaisePropertyChanged("TXOffsetFreq");
                            }
                            break;

                        case "txant":
                            {
                                if (_txant == value)
                                    continue;

                                _UpdateTXAnt(value, update_radio:false);
                            }
                            break;

                        case "wide":
                            {
                                uint temp;
                                bool b = uint.TryParse(value, out temp);
                                if (!b || temp > 1)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    break;
                                }

                                if (_wide == Convert.ToBoolean(temp))
                                    continue;

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
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
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
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_wnb_level == (int)temp)
                                    continue;

                                _wnb_level = (int)temp;
                                RaisePropertyChanged("WNBLevel");
                            }
                            break;

                        case "xit_on":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_xitOn == Convert.ToBoolean(temp))
                                    continue;

                                _xitOn = Convert.ToBoolean(temp);
                                RaisePropertyChanged("XITOn");
                            }
                            break;

                        case "xit_freq":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Slice::StatusUpdate: Invalid value (" + kv + ")");
                                    continue;
                                }

                                if (_xitFreq == temp)
                                    continue;

                                _xitFreq = temp;
                                RaisePropertyChanged("XITFreq");
                            }
                            break;

                        case "dax_clients":
                        case "record_time":
                        case "diversity_parent":
                            // keep these from showing up in the debug output
                            break;

                        default:
                            Debug.WriteLine("Slice::StatusUpdate: Key not parsed (" + kv + ")");
                            break;
                    }
                }
            }

            if (_fullStatusReceived && !_ready)
                CheckReady();
        }

        private bool _ready = false;

        public void CheckReady()
        {
            if (_ready) return;
                 
            if (_panadapter == null)
                _panadapter = _radio.FindPanadapterByStreamID(_panadapterStreamID);

            if ((!_ready && _panadapter != null && _panadapter.Ready) || 
                _panadapterStreamID == 0) // headless Slice
            {
                _ready = true;
                _radio.OnSliceAdded(this);
            }
        }

        public delegate void SMeterDataReadyEventHandler(float data);
        public event SMeterDataReadyEventHandler SMeterDataReady;
        private void OnSMeterDataReady(float data)
        {
            if (SMeterDataReady != null)
                SMeterDataReady(data);
        }

        public delegate void WaveformStatusReceivedEventHandler(Slice slc, string status);
        public event WaveformStatusReceivedEventHandler WaveformStatusReceived;
        private void OnWaveformStatusReceived(string status)
        {
            if (WaveformStatusReceived != null)
                WaveformStatusReceived(this, status);
        }

        public void SendWaveformCommand(string s)
        {
            // should there be some kind of guard here to prevent sending a waveform command if we aren't in a waveform mode?
            _radio.SendCommand("slice waveform_cmd " + _index + " " + s);
        }

        public override string ToString()
        {
            return _index + ": " + StringHelper.DoubleToString(_freq, "f6") + " " + _demodMode + " [" + _filterLow + "," + _filterHigh + "]";
        }
    }
}
