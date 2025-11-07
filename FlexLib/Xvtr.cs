// ****************************************************************************
///*!	\file XVTR.cs
// *	\brief Class for handling Transverters
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2014-07-03
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

using Flex.UiWpfFramework.Mvvm;
using Flex.Util;


namespace Flex.Smoothlake.FlexLib
{
    public class Xvtr : ObservableObject
    {
        private Radio _radio;

        public Xvtr(Radio radio)
        {
            _radio = radio;
        }

        public void Remove()
        {
            if (_radio.Connected)
            {
                _radio.SendCommand("xvtr remove " + _index);
            }
        }
        
        private int _index = -1;
        public int Index
        {
            get { return _index; }
            internal set
            {
                _index = value;
                RaisePropertyChanged("Index");
            }
        }

        private int _order = -1;
        public int Order
        {
            get { return _order; }
            set
            {
                if (_order != value)
                {
                    _order = value;
                    _radio.SendCommand("xvtr set " + _index + " order=" + _order);
                    RaisePropertyChanged("Order");
                }
            }
        }

        private string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                string new_name = value;

                // verify that the length of the name is no more than 4 characters
                if(new_name.Length > 4)
                    new_name = new_name.Substring(0, 4);

                if (_name != new_name)
                {
                    _name = new_name;
                    _radio.SendCommand("xvtr set " + _index + " name=" + _name);
                    RaisePropertyChanged("Name");
                }
                else if (new_name != value)
                {
                    RaisePropertyChanged("Name");
                }
            }
        }

        private double _rfFreq; // in MHz
        public double RFFreq
        {
            get { return _rfFreq; }
            set
            {
                if (_rfFreq != value)
                {
                    _rfFreq = value;
                    _radio.SendCommand("xvtr set " + _index + " rf_freq=" + StringHelper.DoubleToString(_rfFreq, "f6"));
                    RaisePropertyChanged("RFFreq");
                }
            }
        }

        private double _ifFreq; // in MHz
        public double IFFreq
        {
            get { return _ifFreq; }
            set
            {
                if (_ifFreq != value)
                {
                    _ifFreq = value;
                    _radio.SendCommand("xvtr set " + _index + " if_freq=" + StringHelper.DoubleToString(_ifFreq, "f6"));
                    RaisePropertyChanged("IFFreq");
                }
            }
        }

        private double _loError; // in MHz
        public double LOError
        {
            get { return _loError; }
            set
            {
                if (_loError != value)
                {
                    _loError = value;
                    _radio.SendCommand("xvtr set " + _index + " lo_error=" + StringHelper.DoubleToString(_loError, "f6"));
                    RaisePropertyChanged("LOError");
                }
            }
        }

        private double _rxGain; // in dB
        public double RXGain
        {
            get { return _rxGain; }
            set
            {
                if (_rxGain != value)
                {
                    _rxGain = value;
                    _radio.SendCommand("xvtr set " + _index + " rx_gain=" + StringHelper.DoubleToString(_rxGain, "f2"));
                    RaisePropertyChanged("RXGain");
                }
            }
        }

        private bool _rxOnly;
        public bool RXOnly
        {
            get { return _rxOnly; }
            set
            {
                if (_rxOnly != value)
                {
                    _rxOnly = value;
                    _radio.SendCommand("xvtr set " + _index + " rx_only=" + Convert.ToByte(_rxOnly));
                    RaisePropertyChanged("RXOnly");
                }
            }
        }

        private double _maxPower; // in dBm
        public double MaxPower
        {
            get { return _maxPower; }
            set
            {
                double new_power = value;

                if (_ifFreq < 80.0)
                {
                    if (_radio.Model == "FLEX-6400M" || _radio.Model == "FLEX-6400" || _radio.Model == "FLEX-6600M" || _radio.Model == "FLEX-6600")
                    {
                        if (new_power > 10.0)
                            new_power = 10.0;
                    }
                    else
                    {
                        if (new_power > 15.0)
                            new_power = 15.0;
                    }
                }
                else
                {
                    if (new_power > 8.0)
                        new_power = 8.0;
                }
                
                if (new_power < -10.0) new_power = -10.0;

                if (_maxPower != new_power)
                {
                    _maxPower = new_power;
                    _radio.SendCommand("xvtr set " + _index + " max_power=" + StringHelper.DoubleToString(_maxPower, "f2"));
                    RaisePropertyChanged("MaxPower");
                }
                else if (new_power != value)
                {
                    RaisePropertyChanged("MaxPower");
                }
            }
        }

        /// <summary>
        /// Signifies whether the radio has enough good information to use this XVTR definition.
        /// A high limit less than low limit is one example of an invalid XVTR definition.
        /// </summary>
        private bool _valid;
        public bool Valid
        {
            get { return _valid; }
            private set
            {
                if (_valid != value)
                {
                    _valid = value;
                    RaisePropertyChanged("Valid");
                }
            }
        }

        public void StatusUpdate(string s)
        {
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Xvtr::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1].Replace("\"", "");

                switch (key.ToLower())
                {
                    case "if_freq":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Xvtr::StatusUpdate: Invalid frequency (" + kv + ")");
                                continue;
                            }

                            _ifFreq = temp;
                            RaisePropertyChanged("IFFreq");
                        }
                        break;

                    case "lo_error":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Xvtr::StatusUpdate: Invalid frequency (" + kv + ")");
                                continue;
                            }

                            _loError = temp;
                            RaisePropertyChanged("LOError");
                        }
                        break;

                    case "max_power":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Xvtr::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _maxPower = temp;
                            RaisePropertyChanged("MaxPower");
                        }
                        break;

                    case "name":
                        {   
                            _name = value;
                            RaisePropertyChanged("Name");
                        }
                        break;

                    case "order":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 16)
                            {
                                Debug.WriteLine("Xvtr::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _order = (int)temp;
                            RaisePropertyChanged("MaxPower");
                        }
                        break;

                    case "rf_freq":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Xvtr::StatusUpdate: Invalid frequency (" + kv + ")");
                                continue;
                            }

                            _rfFreq = temp;
                            RaisePropertyChanged("RFFreq");
                        }
                        break;

                    case "rx_gain":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Xvtr::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _rxGain = temp;
                            RaisePropertyChanged("RXGain");
                        }
                        break;

                    case "rx_only":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Xvtr::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _rxOnly = Convert.ToBoolean(temp);
                            RaisePropertyChanged("RXOnly");
                        }
                        break;

                    case "is_valid":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Xvtr::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _valid = Convert.ToBoolean(temp);
                            RaisePropertyChanged("Valid");
                        }
                        break;
                }                
            }
        }
    }
}
