// ****************************************************************************
///*!	\file Memory.cs
// *	\brief Core FlexLib source
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2014-09-30
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
// using System.Windows.Media; // for Color class

using Flex.UiWpfFramework.Mvvm;
using Flex.Util;


namespace Flex.Smoothlake.FlexLib
{
    public enum FMTXOffsetDirection
    {
        Down,
        Simplex,
        Up        
    }

    public enum FMToneMode
    {
        Off,
        CTCSS_TX,
        // CTCSS_TXRX -- to be uncommented when PL decode is added
    }

    public class Memory : ObservableObject
    {
        private Radio _radio;

        public Memory(Radio radio, int index)
        {
            _radio = radio;
            _index = index;
        }

        public void Remove()
        {
            if (_radio != null)
                _radio.SendCommand("memory remove " + _index);
        }

        public void Select()
        {
            if (_radio != null)
                _radio.SendCommand("memory apply " + _index);
        }

        private int _index = -1;
        public int Index
        {
            get { return _index; }
        }

        private string _owner;
        public string Owner
        {
            get { return _owner; }
            set
            {
                if(_owner != value)
                {
                    _owner = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " owner=" + _owner.Replace(' ', '\u007f')); // send spaces as something else
                    RaisePropertyChanged("Owner");
                }
            }
        }
            
        private string _group;
        public string Group
        {
            get { return _group; }
            set
            {
                if (_group != value)
                {
                    _group = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " group=" + _group.Replace(' ', '\u007f')); // send spaces as something else
                    RaisePropertyChanged("Group");
                }
            }
        }

        private double _freq; // in MHz
        public double Freq
        {
            get { return _freq; }
            set
            {
                if (_freq != value)
                {
                    _freq = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " freq=" + StringHelper.DoubleToString(_freq, "f6"));
                    RaisePropertyChanged("Freq");
                }
            }
        }

        private string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " name=" + _name.Replace(' ', '\u007f')); // send spaces as something else
                    RaisePropertyChanged("Name");
                }
            }
        }

        //private List<string> _modeList = new List<string>();

        //internal void UpdateModeList(List<string> mode_list)
        //{
        //    string saved_mode = _mode;
        //    _modeList.Clear();
        //    _modeList.AddRange(mode_list);

        //    if (_modeList.Contains(saved_mode))
        //        Mode = saved_mode;
        //    else
        //        Mode = "USB";
        //}

        private string _mode;
        public string Mode 
        {
            get { return _mode; }
            set
            {
                if (_mode != value)
                {
                    _mode = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " mode=" + _mode);
                    RaisePropertyChanged("Mode");
                }
            }
        }

        private int _step; // in Hz
        public int Step
        {
            get { return _step; }
            set
            {
                int new_value = value;

                if (new_value < 1)
                    new_value = 1;

                if (_step != new_value)
                {
                    _step = new_value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " step=" + _step);
                    RaisePropertyChanged("Step");
                }
                else if (new_value != value)
                {
                    RaisePropertyChanged("Step");
                }
            }
        }

        private FMTXOffsetDirection _offsetDirection;
        public FMTXOffsetDirection OffsetDirection 
        {
            get { return _offsetDirection; }
            set
            {
                if (_offsetDirection != value)
                {
                    _offsetDirection = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " repeater=" + FMTXOffsetDirectionToString(_offsetDirection));
                    RaisePropertyChanged("OffsetDirection");
                }
            }
        }

        private double _repeaterOffset;
        public double RepeaterOffset
        {
            get { return _repeaterOffset; }
            set
            {
                if (_repeaterOffset != value)
                {
                    _repeaterOffset = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " repeater_offset=" + StringHelper.DoubleToString(_repeaterOffset, "f6"));
                    RaisePropertyChanged("RepeaterOffset");
                }
            }
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
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " tone_mode=" + FMToneModeToString(_toneMode));
                    RaisePropertyChanged("ToneMode");
                }
            }
        }

        private string _toneValue;
        public string ToneValue 
        {
            get { return _toneValue; }
            set
            {
                if (_toneValue != value)
                {
                    // make sure that the new value is valid given the tone mode
                    if (!ValidateToneValue(value))
                    {
                        Debug.WriteLine("Memory::ToneValue::Set - Invalid Tone Value (" + value + ")");
                        RaisePropertyChanged("ToneValue");
                        return;
                    }
                    
                    _toneValue = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " tone_value=" + _toneValue);
                    RaisePropertyChanged("ToneValue");
                }
            }
        }

        private bool ValidateToneValue(string s)
        {
            bool ret_val = false;
            switch (_toneMode)
            {
                case FMToneMode.CTCSS_TX:
                    float freq;
                    bool b = float.TryParse(s, out freq);

                    if (!b)
                    {
                        ret_val = false;
                    }
                    else
                    {
                        if (freq < 0.0f || freq > 300.0f)
                            ret_val = false;
                        else ret_val = true;
                    }
                    break;
            }

            return ret_val;
        }

        private bool _squelchOn;
        public bool SquelchOn
        {
            get { return _squelchOn; }
            set
            {
                if (_squelchOn != value)
                {
                    _squelchOn = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " squelch=" + Convert.ToByte(_squelchOn));
                    RaisePropertyChanged("SquelchOn");
                }
            }
        }

        private int _squelchLevel;
        public int SquelchLevel
        {
            get { return _squelchLevel; }
            set
            {
                int new_level = value;
                // check the limits
                if (new_level > 100) new_level = 100;
                if (new_level < 0) new_level = 0;

                if (_squelchLevel != new_level)
                {
                    _squelchLevel = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " squelch_level=" + _squelchLevel);
                    RaisePropertyChanged("SquelchLevel");
                }
                else if (new_level != value)
                {
                    RaisePropertyChanged("SquelchLevel");
                }
            }
        }

        private int _rfPower;
        [Obsolete("RF Power is no longer used in Memory form. Use Transmit Profiles to save RF Power")]
        public int RFPower 
        {
            get { return _rfPower; }
            set
            {                
                int new_power = value;

                // check limits
                if (new_power < 0) new_power = 0;
                if (new_power > 100) new_power = 100;

                if (_rfPower != new_power)
                {
                    _rfPower = new_power;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " power=" + _rfPower);
                    RaisePropertyChanged("RFPower");
                }
                else if (new_power != value)
                {
                    RaisePropertyChanged("RFPower");
                }
            }
        }

        private int _rxFilterLow;
        public int RXFilterLow 
        {
            get { return _rxFilterLow; }
            set
            {
                int new_cut = value;
                if (new_cut > _rxFilterHigh - 10) new_cut = _rxFilterHigh - 10;
                switch (_mode)
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
                        if (new_cut < -12000)
                            new_cut = -12000;
                        /* We really can't take into account the Mark here so we will rely on the apply_memory to correctly bound filters */
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
                        if (new_cut < 0.0) new_cut = 0;
                        break;
                }

                if (_rxFilterLow != new_cut)
                {
                    _rxFilterLow = new_cut;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " rx_filter_low=" + _rxFilterLow);
                    RaisePropertyChanged("RXFilterLow");
                }
                else if (new_cut != value)
                {
                    RaisePropertyChanged("RXFilterLow");
                }
            }
        }

        private int _rxFilterHigh;
        public int RXFilterHigh
        {
            get { return _rxFilterHigh; }
            set
            {
                int new_cut = value;
                if (new_cut < _rxFilterLow + 10) new_cut = _rxFilterLow + 10;
                switch (_mode)
                {
                    case "LSB":
                    case "DIGL":
                        if (new_cut > 0) new_cut = 0;
                        break;
                    case "RTTY":
                        if (new_cut > 4000)
                            new_cut = 4000;
                        /* Max RTTY Mark is 4000 - we can't really rely on any slice here. Depend on memory_appy to correctly bound */
                        break;
                    case "CW":
                        if (new_cut > 12000 - _radio.CWPitch)
                            new_cut = 12000 - _radio.CWPitch;
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

                if (_rxFilterHigh != new_cut)
                {
                    _rxFilterHigh = new_cut;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " rx_filter_high=" + _rxFilterHigh);
                    RaisePropertyChanged("RXFilterHigh");
                }
                else if (new_cut != value)
                {
                    RaisePropertyChanged("RXFilterHigh");
                }
            }
        }

        private int _rttyMark; // in Hz
        public int RTTYMark
        {
            get { return _rttyMark; }
            set
            {
                if (_rttyMark != value)
                {
                    _rttyMark = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " rtty_mark=" + _rttyMark);
                    RaisePropertyChanged("RTTYMark");
                }
            }
        }

        private int _rttyShift; // in Hz
        public int RTTYShift
        {
            get { return _rttyShift; }
            set
            {
                if (_rttyShift != value)
                {
                    _rttyShift = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " rtty_shift=" + _rttyShift);
                    RaisePropertyChanged("RTTYShift");
                }
            }
        }

        private int _diglOffset; // in Hz
        public int DIGLOffset
        {
            get { return _diglOffset; }
            set
            {
                if (_diglOffset != value)
                {
                    _diglOffset = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " digl_offset=" + _diglOffset);
                    RaisePropertyChanged("DIGLOffset");
                }
            }
        }

        private int _diguOffset; // in Hz
        public int DIGUOffset
        {
            get { return _diguOffset; }
            set
            {
                if (_diguOffset != value)
                {
                    _diguOffset = value;
                    if (_index >= 0)
                        _radio.SendCommand("memory set " + _index + " digu_offset=" + _diguOffset);
                    RaisePropertyChanged("DIGUOffset");
                }
            }
        }


/*
        private bool _highlight;
        public bool Highlight
        {
            get { return _highlight; }
            set
            {
                if (_highlight != value)
                {
                    _highlight = value;
                    // Send value to radio
                    RaisePropertyChanged("Highlight");
                }
            }
        }

        private Color _highlightColor;
        public Color HighlightColor 
        {
            get { return _highlightColor; }
            set
            {
                if (_highlightColor != value)
                {
                    _highlightColor = value;
                    // Send value to radio
                    RaisePropertyChanged("HighlightColor");
                }
            }
        }
*/

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

        private bool TryParseFMToneMode(string s, out FMToneMode mode)
        {
            bool ret_val = true;
            mode = FMToneMode.Off; // default out param
            switch (s.ToLower())
            {
                case "off": mode = FMToneMode.Off; break;
                case "ctcss_tx": mode = FMToneMode.CTCSS_TX; break;
                default: ret_val = false; break;
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
            dir = FMTXOffsetDirection.Simplex;
            switch (s.ToLower())
            {
                case "down": dir = FMTXOffsetDirection.Down; break;
                case "simplex": dir = FMTXOffsetDirection.Simplex; break;
                case "up": dir = FMTXOffsetDirection.Up; break;
                default: ret_val = false; break;
            }
            return ret_val;
        }

        public void StatusUpdate(string s)
        {
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Memory::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "owner":
                        {
                            _owner = value.Replace('\u007f', ' '); // convert back to spaces
                            RaisePropertyChanged("Owner");
                        }
                        break;

                    case "group":
                        {
                            _group = value.Replace('\u007f', ' '); // convert back to spaces
                            RaisePropertyChanged("Group");
                        }
                        break;

                    case "freq":
                        {
                            double temp; // in MHz
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _freq = temp;
                            RaisePropertyChanged("Freq");
                        }
                        break;

                    case "name":
                        {
                            _name = value.Replace('\u007f', ' '); // convert back to spaces
                            RaisePropertyChanged("Name");
                        }
                        break;

                    case "mode":
                        {
                            _mode = value;
                            RaisePropertyChanged("Mode");
                        }
                        break;

                    case "step":
                         {
                            int temp; // in Hz
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _step = temp;
                            if (_step < 1)
                                _step = 1;
                            RaisePropertyChanged("Step");
                        }
                        break;

                    case "repeater":
                        {
                            FMTXOffsetDirection dir;
                            bool b = TryParseFMTXOffsetDirection(value, out dir);

                            if (!b)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _offsetDirection = dir;
                            RaisePropertyChanged("OffsetDirection");
                        }
                        break;

                    case "repeater_offset":
                        {
                            double temp; // in MHz
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _repeaterOffset = temp;
                            RaisePropertyChanged("RepeaterOffset");
                        }
                        break;

                    case "tone_mode":
                        {
                            FMToneMode mode;
                            bool b = TryParseFMToneMode(value, out mode);

                            if (!b)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _toneMode = mode;
                            RaisePropertyChanged("ToneMode");
                        }
                        break;

                    case "tone_value":
                        {
                            _toneValue = value;
                            RaisePropertyChanged("ToneValue");
                        }
                        break;

                    case "squelch":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

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
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _squelchLevel = temp;
                            RaisePropertyChanged("SquelchLevel");
                        }
                        break;

                    case "power":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _rfPower = temp;
                            RaisePropertyChanged("RFPower");
                        }
                        break;

                    case "rx_filter_low":
                        {
                            int temp; // in Hz
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _rxFilterLow = temp;
                            RaisePropertyChanged("RXFilterLow");
                        }
                        break;

                    case "rx_filter_high":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _rxFilterHigh = temp;
                            RaisePropertyChanged("RXFilterHigh");
                        }
                        break;

                    case "rtty_mark":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

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
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _rttyShift = temp;
                            RaisePropertyChanged("RTTYShift");
                        }
                        break;

                    case "digl_offset":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

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
                                Debug.WriteLine("Memory::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _diguOffset = temp;
                            RaisePropertyChanged("DIGUOffset");
                        }
                        break;

                    case "highlight":
                    case "highlight_color":
                        // keep these from showing up in the debug output
                        break;

                    default:
                        Debug.WriteLine("Memory::StatusUpdate: Key not parsed (" + kv + ")");
                        break;
                }
            }
        }
    }
}
