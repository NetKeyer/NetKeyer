// ****************************************************************************
///*!	\file Tuner.cs
// *	\brief Represents a single hardware tuner
// *
// *	\copyright	Copyright 2024 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2024-04-01
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

using Flex.UiWpfFramework.Mvvm;
using System.Diagnostics;
// using System.Windows.Navigation;


namespace Flex.Smoothlake.FlexLib
{
    public enum TunerState
    {
        PowerUp,
        SelfCheck,
        Standby,
        Operate,
        Bypass,
        Fault,
        Unknown
    }

    public class Tuner : ObservableObject
    {
        // Variable Declaration
        private Radio _radio;

        private string _handle;
        public string Handle
        {
            get => _handle;
        }

        private string _serialNumber;
        public string SerialNumber
        {
            get => _serialNumber;
            private set
            {
                if (_serialNumber == value) return;
                _serialNumber = value;
                RaisePropertyChanged(nameof(SerialNumber));
            }
        }

        private string _version;
        public string Version
        {
            get => _version;
            private set
            {
                if (_version == value) return;
                _version = value;
                RaisePropertyChanged(nameof(Version));
            }
        }

        private string _nickname;
        public string Nickname
        {
            get => _nickname;
            private set
            {
                if (_nickname == value) return;
                _nickname = value;
                RaisePropertyChanged(nameof(Nickname));
            }
        }

        public string Model { get; private set; }

        private bool _one_by_three = false;
        public bool OneByThree
        {
            get => _one_by_three;
            private set
            {
                if (_one_by_three == value) return;
                _one_by_three = value;
                RaisePropertyChanged(nameof(OneByThree));
            }
        }

        private bool _dhcp;
        public bool Dhcp
        {
            get => _dhcp;
            set
            {
                if (_dhcp == value) return;
                _dhcp = value;
                RaisePropertyChanged(nameof(Dhcp));
            }
        }
            
        private IPAddress _ip;
        public IPAddress IP
        {
            get => _ip;
            private set
            {
                if (_ip == value) return;
                _ip = value;
                RaisePropertyChanged(nameof(IP));
            }
        }

        private IPAddress _netmask;
        public IPAddress Netmask
        {
            get => _netmask;
            private set
            {
                if (_netmask == value) return;
                _netmask = value;
                RaisePropertyChanged(nameof(Netmask));
            }
        }

        private IPAddress _gateway;
        public IPAddress Gateway
        {
            get => _gateway;
            private set
            {
                if (_gateway == value) return;
                _gateway = value;
                RaisePropertyChanged(nameof(Gateway));
            }
        }

        public int Port { get; private set; }

        private string _ant;
        private string Ant
        {
            get => _ant;
            set
            {
                if (_ant == value) return;
                _ant = value;
                ParseAntenna(_ant);
            }
        }

        private string _port_a_ant = "";
        public string PortAAnt
        {
            get => _port_a_ant;
            set
            {
                if (_port_a_ant == value) return;
                _port_a_ant = value;
                RaisePropertyChanged(nameof(PortAAnt));
            }
        }

        private string _port_b_ant = "";
        public string PortBAnt
        {
            get => _port_b_ant;
            set
            {
                if (_port_b_ant == value) return;
                _port_b_ant = value;
                RaisePropertyChanged(nameof(PortBAnt));
            }
        }

        private void ParseAntenna(string s)
        {
            if (string.IsNullOrEmpty(s)) return;

            string[] port_ants = s.Split(',');
            
            // Handle unexpected results
            if (port_ants.Length > 2)
            {
                // We will proceed as usual, but will ignore additional values beyond the first 2
                Debug.WriteLine("Tuner::ParseAntenna: Unexpected format (" + s + ")");
            }

            PortAAnt = port_ants[0];

            if (port_ants.Length > 1)
                PortBAnt = port_ants[1];
            else
                PortBAnt = "";
        }

        private List<Meter> _meters = new List<Meter>();

        private void UpdateState()
        {
            if (!_isOperate)
            {
                State = TunerState.Standby;
            }
            else
            {
                if (!_isBypass) State = TunerState.Operate;
                else State = TunerState.Bypass;
            }
        }

        private TunerState _state = TunerState.Unknown;
        public TunerState State
        {
            get => _state;
            internal set
            {
                if (_state == value) return;
                _state = value;
                RaisePropertyChanged(nameof(State));
            }
        }

        private bool _isOperate = false; // also known as "not standby"
        public bool IsOperate
        {
            get => _isOperate;
            set
            {
                if (_isOperate == value) return;
                _isOperate = value;
                _radio.SendCommand("tgxl set handle=" + _handle + " mode=" + Convert.ToByte(_isOperate));
                RaisePropertyChanged(nameof(IsOperate));

                UpdateState();
            }
        }

        private bool _isBypass = false;
        public bool IsBypass
        {
            get => _isBypass;
            set
            {
                if (_isBypass == value) return;
                _isBypass = value;
                _radio.SendCommand("tgxl set handle=" + _handle + " bypass=" + Convert.ToByte(_isBypass));
                RaisePropertyChanged(nameof(IsBypass));

                UpdateState();
            }
        }

        private bool _isTuning = false;
        public bool IsTuning
        {
            get => _isTuning;
            set
            {
                if (_isTuning == value) return;
                _isTuning = value;
                RaisePropertyChanged(nameof(IsTuning));
            }
        }

        private int _relayC1 = 0;
        public int RelayC1
        {
            get => _relayC1;
            internal set
            {
                if (_relayC1 == value) return;
                _relayC1 = value;
                RaisePropertyChanged(nameof(RelayC1));
            }
        }

        private int _relayC2 = 0;
        public int RelayC2
        {
            get => _relayC2;
            internal set
            {
                if (_relayC2 == value) return;
                _relayC2 = value;
                RaisePropertyChanged(nameof(RelayC2));
            }
        }

        private int _relayL = 0;
        public int RelayL
        {
            get => _relayL;
            internal set
            {
                if (_relayL == value) return;
                _relayL = value;
                RaisePropertyChanged(nameof(RelayL));
            }
        }

        private bool _pttA;
        public bool PttA
        {
            get => _pttA;
            set
            {
                if (_pttA == value) return;
                _pttA = value;
                RaisePropertyChanged(nameof(PttA));
            }
        }

        private bool _pttB;
        public bool PttB
        {
            get => _pttB;
            set
            {
                if (_pttB == value) return;
                _pttB = value;
                RaisePropertyChanged(nameof(PttB));
            }
        }

        public void AutoTune()
        {
            if (_radio.TransmitSlice == null)
            {
                Debug.WriteLine("Autotune skipped as there is no Transmit Slice selected");
                return;
            }

            if (_radio.TransmitSlice.TXAnt != _port_a_ant && _radio.TransmitSlice.TXAnt != _port_b_ant)
            {
                Debug.WriteLine("AutoTune skipped as Tuner Antenna config doesn't match TX Ant");
                return;
            }

            if (_radio.InterlockState != InterlockState.Ready)
            {
                Debug.WriteLine("AutoTune skipped as interlock is not ready (" + _radio.InterlockState.ToString() + ")");
                return;
            }

            if (!IsOperate) IsOperate = true;
            if (IsBypass) IsBypass = false;
            
            _radio.SendCommand("tgxl autotune handle=" + _handle);
        }

        // Constructor
        public Tuner(Radio radio, string handle)
        {
            _radio = radio;
            _handle = handle;

            foreach (Meter m in _radio.FindMetersByTuner(this))
                AddMeter(m);
        }

        #region Meter Routines

        internal void AddMeter(Meter m)
        {
            lock (_meters)
            {
                if (!_meters.Contains(m))
                {
                    _meters.Add(m);
                    OnMeterAdded(m);
                }
            }
        }

        internal void RemoveMeter(Meter m)
        {
            lock (_meters)
            {
                if (_meters.Contains(m))
                {
                    _meters.Remove(m);
                    OnMeterRemoved(m);
                }
            }
        }

        public delegate void MeterAddedEventHandler(Tuner tuner, Meter m);
        public event MeterAddedEventHandler MeterAdded;
        private void OnMeterAdded(Meter m)
        {
            if (MeterAdded != null)
                MeterAdded(this, m);
        }

        public delegate void MeterRemovedEventHandler(Tuner tuner, Meter m);
        public event MeterRemovedEventHandler MeterRemoved;
        private void OnMeterRemoved(Meter m)
        {
            if (MeterRemoved != null)
                MeterRemoved(this, m);
        }

        public Meter FindMeterByIndex(int index)
        {
            lock (_meters)
                return _meters.FirstOrDefault(m => m.Index == index);
        }

        public Meter FindMeterByName(string s)
        {
            lock (_meters)
                return _meters.FirstOrDefault(m => m.Name == s);
        }

        #endregion

        public void StatusUpdate(string s)
        {
            string[] words = s.Split(' ');
            //Debug.WriteLine("Tuner Status: " + s);

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Tuner::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "serial_num": SerialNumber = value; break;
                    case "version": Version = value; break;
                    case "nickname": Nickname = value; break;
                    case "model": Model = value; break;
                    case "one_by_three":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            OneByThree = Convert.ToBoolean(temp);
                        }
                        break;

                    case "dhcp":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            Dhcp = Convert.ToBoolean(temp);
                        }
                        break;

                    case "ip":
                        {
                            IPAddress temp;
                            bool b = IPAddress.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            IP = temp;
                        }
                        break;

                    case "netmask":
                        {
                            IPAddress temp;
                            bool b = IPAddress.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            Netmask = temp;
                        }
                        break;

                    case "gateway":
                        {
                            IPAddress temp;
                            bool b = IPAddress.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            Gateway = temp;
                        }
                        break;

                    case "ant": Ant = value; break;

                    case "operate":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            bool new_val = Convert.ToBoolean(temp);

                            // if the state isn't changing, don't bother with updating the object
                            if (_isOperate == new_val)
                                continue;

                            _isOperate = new_val;
                            RaisePropertyChanged(nameof(IsOperate));
                            UpdateState();
                        }
                        break;

                    case "bypass":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_isBypass == Convert.ToBoolean(temp))
                                continue;

                            _isBypass = Convert.ToBoolean(temp);
                            RaisePropertyChanged(nameof(IsBypass));
                            UpdateState();
                        }
                        break;

                    case "tuning":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_isTuning == Convert.ToBoolean(temp))
                                continue;

                            _isTuning = Convert.ToBoolean(temp);
                            RaisePropertyChanged(nameof(IsTuning));
                        }
                        break;

                    case "relayc1":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b || temp > 0xFF)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_relayC1 == temp) continue;

                            _relayC1 = temp;
                            RaisePropertyChanged(nameof(RelayC1));
                        }
                        break;

                    case "relayc2":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b || temp > 0xFF)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_relayC2 == temp) continue;

                            _relayC2 = temp;
                            RaisePropertyChanged(nameof(RelayC2));
                        }
                        break;

                    case "relayl":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b || temp > 0xFF)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_relayL == temp) continue;

                            _relayL = temp;
                            RaisePropertyChanged(nameof(RelayL));
                        }
                        break;

                    case "ptta": // note: tolower call above
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            PttA = Convert.ToBoolean(temp);
                        }
                        break;

                    case "pttb": // note: tolower call above
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Tuner::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            PttB = Convert.ToBoolean(temp);
                        }
                        break;
                    default:
                        Debug.WriteLine("Tuner::StatusUpdate: Unknown Key (" + kv + ")"); 
                        break;
                }
            }
        }
    }
}
