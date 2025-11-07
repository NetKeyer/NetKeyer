// ****************************************************************************
///*!	\file Amplifier.cs
// *	\brief Represents a single hardware amplifier
// *
// *	\copyright	Copyright 2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2016-12-09
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


namespace Flex.Smoothlake.FlexLib
{
    public enum AmplifierState
    {
        PowerUp,
        SelfCheck,
        Standby,
        Idle,
        TransmitA,
        TransmitB,
        Fault,
        Unknown
    }

    public class Amplifier : ObservableObject
    {
        // Variable Declaration
        private Radio _radio;

        private string _handle;
        public string Handle
        {
            get { return _handle; }
        }

        private IPAddress _ip;
        public IPAddress IP
        {
            get { return _ip; }
            internal set { _ip = value; }
        }

        private int _port;
        public int Port
        {
            get { return _port; }
            internal set { _port = value; }
        }

        private string _model;
        public string Model
        {
            get { return _model; }
            internal set { _model = value; }
        }

        private string _serialNumber;
        public string SerialNumber
        {
            get { return _serialNumber; }
            internal set { _serialNumber = value; }
        }

        private string _ant;
        public string Ant
        {
            get { return _ant; }
            set
            {
                _ant = value;
                ParseAntennaSettings(_ant);
                RaisePropertyChanged("Ant");
            }
        }

        private Dictionary<string, string> _antennaSettingsDict = new Dictionary<string, string>();
        private void ParseAntennaSettings(string s)
        {
            Dictionary<string, string> new_ant_settings_dict = new Dictionary<string, string>();

            string[] ant_setting_pairs = s.Split(',');
            foreach (string ant_setting_pair in ant_setting_pairs)
            {
                if (!ant_setting_pair.Contains(":")) continue;

                string[] settings = ant_setting_pair.Split(':');
                
                if (settings.Length != 2) continue;

                new_ant_settings_dict.Add(settings[0], settings[1]);
            }

            _antennaSettingsDict = new_ant_settings_dict;
        }

        /// <summary>
        /// Returns the name of the output associated with the ant given the current configuration of the amplifier
        /// </summary>
        /// <param name="ant">The radio antenna port name</param>
        /// <returns>The name of the output associated with the radio antenna port, or null if not configured for that port</returns>
        public string OutputConfiguredForAntenna(string ant)
        {
            if (_antennaSettingsDict == null || !_antennaSettingsDict.Keys.Contains(ant)) return null;

            return _antennaSettingsDict[ant];
        }

        private List<Meter> _meters = new List<Meter>();

        private AmplifierState _state = AmplifierState.Unknown;
        public AmplifierState State
        {
            get { return _state; }
        }

        private void UpdateIsOperate()
        {
            bool new_val = (_state != AmplifierState.Standby);
            if (_isOperate != new_val)
            {
                _isOperate = new_val;
                RaisePropertyChanged("IsOperate");
            }
        }

        private bool _isOperate = false;
        public bool IsOperate
        {
            get { return _isOperate; }
            set
            {
                if (_isOperate != value)
                {
                    _isOperate = value;
                    _radio.SendCommand("amplifier set " + _handle + " operate=" + Convert.ToByte(_isOperate));
                    RaisePropertyChanged("IsOperate");
                }
            }
        }

        // Constructor
        public Amplifier(Radio radio, string handle)
        {
            _radio = radio;
            _handle = handle;

            foreach (Meter m in _radio.FindMetersByAmplifier(this))
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

        public delegate void MeterAddedEventHandler(Amplifier amp, Meter m);
        public event MeterAddedEventHandler MeterAdded;
        private void OnMeterAdded(Meter m)
        {
            if (MeterAdded != null)
                MeterAdded(this, m);
        }

        public delegate void MeterRemovedEventHandler(Amplifier amp, Meter m);
        public event MeterRemovedEventHandler MeterRemoved;
        private void OnMeterRemoved(Meter m)
        {
            if (MeterRemoved != null)
                MeterRemoved(this, m);
        }

        public Meter FindMeterByIndex(int index)
        {
            lock (_meters)
            {
                foreach (Meter m in _meters)
                {
                    if (m.Index == index)
                        return m;
                }
            }

            return null;
        }

        public Meter FindMeterByName(string s)
        {
            lock (_meters)
            {
                foreach (Meter m in _meters)
                {
                    if (m.Name == s)
                        return m;
                }
            }

            return null;
        }

        #endregion

        public void StatusUpdate(string s)
        {
            string[] words = s.Split(' ');
            //Debug.WriteLine("Amp Status: " + s);

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Amplifier::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "model": _model = value; break;
                    case "serial_num": _serialNumber = value; break;
                    case "ant": Ant = value; break;
                    case "state":
                        {
                            AmplifierState new_state = _state;
                            switch (value.ToUpper())
                            {
                                case "POWERUP": new_state = AmplifierState.PowerUp; break;
                                case "SELFCHECK": new_state = AmplifierState.SelfCheck; break;
                                case "STANDBY": new_state = AmplifierState.Standby; break;
                                case "IDLE": new_state = AmplifierState.Idle; break;
                                case "TRANSMIT_A": new_state = AmplifierState.TransmitA; break;
                                case "TRANSMIT_B": new_state = AmplifierState.TransmitB; break;
                                case "FAULT": new_state = AmplifierState.Fault; break;
                                    
                                default:
                                {
                                    new_state = AmplifierState.Unknown;
                                    Debug.WriteLine("Amplifier::StatusUpdate: Unrecognized State (" + kv + ")"); break;
                                }
                            }

                            if (_state != new_state)
                            {
                                _state = new_state;
                                RaisePropertyChanged("State");

                                UpdateIsOperate();
                            }
                        }
                        break;
                    case "ip":
                        {
                            if (!IPAddress.TryParse(value, out var temp))
                            {
                                Debug.WriteLine("Amp::StatusUpdate: Invalid value ({kv})");
                                continue;
                            }

                            IP = temp;
                        }
                        break;
                }
            }
        }
    }
}
