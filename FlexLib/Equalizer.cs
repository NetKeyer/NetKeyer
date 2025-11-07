// ****************************************************************************
///*!	\file Equalizer.cs
// *	\brief Represents an Equalizer
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2013-06-13
// *	\author Ed Gonzalez
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

using System.Diagnostics;
using System.ComponentModel;
using Flex.UiWpfFramework.Mvvm;
using System.Globalization;

namespace Flex.Smoothlake.FlexLib
{
    public enum EqualizerSelect
    {
        None,
        TX,
        RX
    }

    public class Equalizer : ObservableObject
    {
        private Radio _radio;

        internal Equalizer(Radio radio, EqualizerSelect eq_select)
        {
            _radio = radio;
            //this.EQ_enabled = true;
            _eq_select = eq_select;

            int default_state_in_codec = 0; // +0db EQ Level

            _level_32Hz = default_state_in_codec;
            _level_63Hz = default_state_in_codec;
            _level_125Hz = default_state_in_codec;
            _level_250Hz = default_state_in_codec;
            _level_500Hz = default_state_in_codec;
            _level_1000Hz = default_state_in_codec;
            _level_2000Hz = default_state_in_codec;
            _level_4000Hz = default_state_in_codec;
            _level_8000Hz = default_state_in_codec;
        }

        #region Model Properties

        private bool _radio_ack = false;
        public bool RadioAck
        {
            get { return _radio_ack; }
            internal set
            {
                if (_radio_ack != value)
                {
                    _radio_ack = value;
                    RaisePropertyChanged("RadioAck");
                }
            }
        }

        private EqualizerSelect _eq_select;
        public EqualizerSelect EQ_select
        {
            get { return _eq_select; }
        }

        private bool _eq_enabled;
        public bool EQ_enabled
        {
            get { return _eq_enabled; }
            set
            {
                if (_eq_enabled != value)
                {
                    if (_eq_select != EqualizerSelect.None)
                    {
                        _eq_enabled = value;
                        _radio.SendCommand("eq " + _eq_select.ToString() + "sc mode=" + _eq_enabled.ToString());
                    }
                    RaisePropertyChanged("EQ_enabled");
                }
            }
        }

        private int _level_32Hz;
        public int level_32Hz
        {
            get { return _level_32Hz; }
            set
            {
                if (_level_32Hz != value)
                {
                    _level_32Hz = value;
                    // Currently 32Hz is not a valid EQ Control in the radio
                    _radio.SendCommand("eq " + _eq_select.ToString() + "sc 32Hz=" + (_level_32Hz).ToString());
                    RaisePropertyChanged("level_32Hz");
                }
            }
        }

        private int _level_63Hz;
        public int level_63Hz
        {
            get { return _level_63Hz; }
            set
            {
                if (_level_63Hz != value)
                {
                    _level_63Hz = value;
                    _radio.SendCommand("eq " + _eq_select.ToString() + "sc 63Hz=" + (_level_63Hz).ToString());
                    RaisePropertyChanged("level_63Hz");
                }
            }
        }

        private int _level_125Hz;
        public int level_125Hz
        {
            get { return _level_125Hz; }
            set
            {
                if (_level_125Hz != value)
                {
                    _level_125Hz = value;
                    _radio.SendCommand("eq " + _eq_select.ToString() + "sc 125Hz=" + (_level_125Hz).ToString());
                    RaisePropertyChanged("level_125Hz");
                }
            }
        }

        private int _level_250Hz;
        public int level_250Hz
        {
            get { return _level_250Hz; }
            set
            {
                if (_level_250Hz != value)
                {
                    _level_250Hz = value;
                    _radio.SendCommand("eq " + _eq_select.ToString() + "sc 250Hz=" + (_level_250Hz).ToString());
                    RaisePropertyChanged("level_250Hz");
                }
            }
        }

        private int _level_500Hz;
        public int level_500Hz
        {
            get { return _level_500Hz; }
            set
            {
                if (_level_500Hz != value)
                {
                    _level_500Hz = value;
                    _radio.SendCommand("eq " + _eq_select.ToString() + "sc 500Hz=" + (_level_500Hz).ToString());
                    RaisePropertyChanged("level_500Hz");
                }
            }
        }

        private int _level_1000Hz;
        public int level_1000Hz
        {
            get { return _level_1000Hz; }
            set
            {
                if (_level_1000Hz != value)
                {
                    _level_1000Hz = value;
                    _radio.SendCommand("eq " + _eq_select.ToString() + "sc 1000Hz=" + (_level_1000Hz).ToString());
                    RaisePropertyChanged("level_1000Hz");
                }
            }
        }

        private int _level_2000Hz;
        public int level_2000Hz
        {
            get { return _level_2000Hz; }
            set
            {
                if (_level_2000Hz != value)
                {
                    _level_2000Hz = value;
                    _radio.SendCommand("eq " + _eq_select.ToString() + "sc 2000Hz=" + (_level_2000Hz).ToString());
                    RaisePropertyChanged("level_2000Hz");
                }
            }
        }

        private int _level_4000Hz;
        public int level_4000Hz
        {
            get { return _level_4000Hz; }
            set
            {
                if (_level_4000Hz != value)
                {
                    _level_4000Hz = value;
                    _radio.SendCommand("eq " + _eq_select.ToString() + "sc 4000Hz=" + (_level_4000Hz).ToString());
                    RaisePropertyChanged("level_4000Hz");
                }
            }
        }

        private int _level_8000Hz;
        public int level_8000Hz
        {
            get { return _level_8000Hz; }
            set
            {
                if (_level_8000Hz != value)
                {
                    _level_8000Hz = value;
                    _radio.SendCommand("eq " + _eq_select.ToString() + "sc 8000Hz=" + (_level_8000Hz).ToString());
                    RaisePropertyChanged("level_8000Hz");
                }
            }
        }

        #endregion

        public void RequestEqualizerInfo()
        {
            _radio.SendCommand("eq " + _eq_select.ToString() + "sc info");
        }

        public bool RequestEqualizerFromRadio()
        {
            // check to see if this Equalizer has already been activated
            if (_radio_ack) return false;

            // check to ensure this object is tied to a radio object
            if (_radio == null) return false;

            // check to make sure the radio is connected
            if (!_radio.Connected) return false;

            // send the command to the radio to create the display
            _radio.SendReplyCommand(new ReplyHandler(UpdateEQMode), "eq " + _eq_select.ToString() + "sc info");

            return true;
        }

        private void UpdateEQMode(int seq, uint resp_val, string s)
        {
            if (resp_val != 0) return;

            //bool b = bool.TryParse(s, out _eq_enabled);
            // TODO: Maybe come back and change this with further EQ Expansion
            bool b = true;
            if (!b)
            {
                Debug.WriteLine("Equalizer: UpdateEQMode-Error parsing Mode (" + s + ")");
                return;
            }

            _radio.AddEqualizer(this);
        }
        
        public void StatusUpdate(string s)
        {
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    if (!string.IsNullOrEmpty(kv))
                    {
                        Debug.WriteLine($"Equalizer::StatusUpdate: Invalid key/value pair ({kv})");
                    }
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key)
                {
                    case "mode":
                        {
                            int mode;
                            bool b = int.TryParse(value, out mode);
                            if (!b)
                            {
                                Debug.WriteLine("Equalizer::StatusUpdate: Invalid Mode (" + kv + ")");
                                continue;
                            }
                            if (mode == 1)
                            {
                                _eq_enabled = true;
                            }
                            else if (mode == 0)
                            {
                                _eq_enabled = false;
                            }
                            RaisePropertyChanged("EQ_enabled");
                        }
                        break;
                    case "32Hz":
                        {
                            int level;
                            bool b = int.TryParse(value, out level);
                            if (!b)
                            {
                                Debug.WriteLine("Equalizer::StatusUpdate: Invalid 32Hz Level (" + kv + ")");
                                continue;
                            }

                            _level_32Hz = level;
                            RaisePropertyChanged("level_32Hz");
                        }
                        break;
                    case "63Hz":
                        {
                            int level;
                            bool b = int.TryParse(value, out level);
                            if (!b)
                            {
                                Debug.WriteLine("Equalizer::StatusUpdate: Invalid 63Hz Level (" + kv + ")");
                                continue;
                            }

                            _level_63Hz = level;
                            RaisePropertyChanged("level_63Hz");
                        }
                        break;
                    case "125Hz":
                        {
                            int level;
                            bool b = int.TryParse(value, out level);
                            if (!b)
                            {
                                Debug.WriteLine("Equalizer::StatusUpdate: Invalid 125Hz Level (" + kv + ")");
                                continue;
                            }

                            _level_125Hz = level;
                            RaisePropertyChanged("level_125Hz");
                            break;
                        }
                    case "250Hz":
                        {
                            int level;
                            bool b = int.TryParse(value, out level);
                            if (!b)
                            {
                                Debug.WriteLine("Equalizer::StatusUpdate: Invalid 250Hz Level (" + kv + ")");
                                continue;
                            }

                            _level_250Hz = level;
                            RaisePropertyChanged("level_250Hz");
                        }
                        break;
                    case "500Hz":
                        {
                            int level;
                            bool b = int.TryParse(value, out level);
                            if (!b)
                            {
                                Debug.WriteLine("Equalizer::StatusUpdate: Invalid 500Hz Level (" + kv + ")");
                                continue;
                            }

                            _level_500Hz = level;
                            RaisePropertyChanged("level_500Hz");
                        }
                        break;
                    case "1000Hz":
                        {
                            int level;
                            bool b = int.TryParse(value, out level);
                            if (!b)
                            {
                                Debug.WriteLine("Equalizer::StatusUpdate: Invalid 1000Hz Level (" + kv + ")");
                                continue;
                            }

                            _level_1000Hz = level;
                            RaisePropertyChanged("level_1000Hz");
                        }
                        break;
                    case "2000Hz":
                        {
                            int level;
                            bool b = int.TryParse(value, out level);
                            if (!b)
                            {
                                Debug.WriteLine("Equalizer::StatusUpdate: Invalid 2000Hz Level (" + kv + ")");
                                continue;
                            }

                            _level_2000Hz = level;
                            RaisePropertyChanged("level_2000Hz");
                        }
                        break;
                    case "4000Hz":
                        {
                            int level;
                            bool b = int.TryParse(value, out level);
                            if (!b)
                            {
                                Debug.WriteLine("Equalizer::StatusUpdate: Invalid 4000Hz Level (" + kv + ")");
                                continue;
                            }

                            _level_4000Hz = level;
                            RaisePropertyChanged("level_4000Hz");
                        }
                        break;
                    case "8000Hz":
                        {
                            int level;
                            bool b = int.TryParse(value, out level);
                            if (!b)
                            {
                                Debug.WriteLine("Equalizer::StatusUpdate: Invalid 8000Hz Level (" + kv + ")");
                                continue;
                            }

                            _level_8000Hz = level;
                            RaisePropertyChanged("level_8000Hz");
                        }
                        break;
                    default:
                        Debug.WriteLine("Equalizer::StatusUpdate: Key not parsed (" + kv + ")");
                        break;
                }
            }
        }
    }
}
