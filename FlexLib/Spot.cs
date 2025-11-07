// ****************************************************************************
///*!	\file Spot.cs
// *	\brief Represents a single DX Spot as provided by telnet, N1MM, or other Client
// *
// *	\copyright	Copyright 2018 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2018-04-24
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
// using System.Windows.Media;
using Flex.UiWpfFramework.Mvvm;
using Flex.Util;

namespace Flex.Smoothlake.FlexLib
{
    public class Spot : ObservableObject
    {
        /// <summary>
        /// This constructor should only be used by FlexLib to create Spot objects as a
        /// result of status messages from the radio (the result of calling Radio.RequestSpot
        /// or a dump of existing spots when initially connecting to the radio.
        /// </summary>
        /// <param name="radio">A reference to the Radio object that received the status message</param>
        /// <param name="index">The radios reference to the spot object</param>
        internal Spot(Radio radio, int index)
        {
            _radio = radio;
            _index = index;
        }

        /// <summary>
        /// This constructor is intended to be used to populate a Spot object prior to
        /// requesting it from the radio using the Radio.RequestSpot function.  Note that
        /// the resulting status messages from the radio will create a new Spot object
        /// that contains a Radio reference and an index. 
        /// </summary>
        public Spot()
        {
            
        }

        /// <summary>
        /// Removes a spot from the radio.  Note that only upon the resulting status
        /// message from the radio will the SpotRemoved event fire from the Radio object.
        /// </summary>
        public void Remove()
        {
            if (_radio != null && _index > 0)
                _radio.SendCommand("spot remove " + _index);

            // ensure that no additional changes are performed on this object
            _radio = null;
        }

        private Radio _radio = null;

        private int _index = -1;
        /// <summary>
        /// The radio's reference to spot object.  When this is -1, it is assumed
        /// that the FlexLib object exists only on the client side (as opposed to
        /// a Spot object about which the radio is aware.
        /// </summary>
        public int Index
        {
            get { return _index; }
        }        

        public double _rxFrequency;
        /// <summary>
        /// The frequency to be used to place the Spot.  If no TXFrequency is set, this is the assumed TX Frequency (simplex).
        /// </summary>
        public double RXFrequency
        {
            get { return _rxFrequency; }
            set
            {
                if (_rxFrequency != value)
                {
                    _rxFrequency = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " rx_freq=" + StringHelper.DoubleToString(_rxFrequency, "f6"));
                    RaisePropertyChanged("RXFrequency");
                }
            }
        }

        public double _txFrequency;
        /// <summary>
        /// (optional) This field would indicate a Split spot with a different 
        /// transmit frequency than the RX frequency.  When this field is
        /// blank, it is assumed to be a simplex Spot where the TX Frequency
        /// matches the RX Frequency.  Note that triggering a Spot with this
        /// field set does not automatically create a Split Slice as of v2.3.x.
        /// </summary>
        public double TXFrequency
        {
            get { return _txFrequency; }
            set
            {
                if (_txFrequency != value)
                {
                    _txFrequency = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " tx_freq=" + StringHelper.DoubleToString(_txFrequency, "f6"));
                    RaisePropertyChanged("TXFrequency");
                }
            }
        }

        private string _mode;
        /// <summary>
        /// The Mode specified for the Spot.  Note that this may not always be provided
        /// and may not map directly to a DSPMode (e.g. SSB, PSK31, etc)
        /// </summary>
        public string Mode
        {
            get { return _mode; }
            set
            {
                if (_mode != value)
                {
                    _mode = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " mode=" + _mode);
                    RaisePropertyChanged("Mode");
                }
            }
        }

        private string _callsign;
        /// <summary>
        /// The Callsign to display for the Spot (dxcall in N1MM spot packet)
        /// </summary>
        public string Callsign
        {
            get { return _callsign; }
            set
            {
                if (_callsign != value)
                {
                    _callsign = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " callsign=" + _callsign.Replace(' ', '\u007f'));
                    RaisePropertyChanged("Callsign");
                }
            }
        }

        private string _color;
        /// <summary>
        /// (Optional). A color represented by hex.  Typical format #AARRGGBB
        /// </summary>
        public string Color
        {
            get { return _color; }
            set
            {
                if (_color != value)
                {
                    _color = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " color=" + _color);
                    RaisePropertyChanged("Color");
                }
            }
        }

        private string _backgroundColor;
        /// <summary>
        /// (Optional). A color represented by hex.  Typical format #AARRGGBB
        /// </summary>
        public string BackgroundColor
        {
            get { return _backgroundColor; }
            set
            {
                if (_backgroundColor != value)
                {
                    _backgroundColor = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " background_color=" + _backgroundColor);
                    RaisePropertyChanged("BackgroundColor");
                }
            }
        }

        private string _source;
        /// <summary>
        /// A string used to identify from where the Spot came.  For example, the source
        /// will be N1MM-[StationName] for spots that originate from N1MMSpot Ports.
        /// </summary>
        public string Source
        {
            get { return _source; }
            set
            {
                if (_source != value)
                {
                    _source = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " source=" + _source);
                    RaisePropertyChanged("Source");
                }
            }
        }

        private string _spotterCallsign;
        /// <summary>
        /// The callsign of the spotter as is often reported on telnet.
        /// </summary>
        public string SpotterCallsign
        {
            get { return _spotterCallsign; }
            set
            {
                if (_spotterCallsign != value)
                {
                    _spotterCallsign = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " spotter_callsign=" + _spotterCallsign);
                    RaisePropertyChanged("SpotterCallsign");
                }
            }
        }

        private DateTime _timestamp;
        /// <summary>
        /// The timestamp (UTC) of the spot as reported by the original source
        /// </summary>
        public DateTime Timestamp
        {
            get { return _timestamp; }
            set
            {
                if (_timestamp != value)
                {
                    _timestamp = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " timestamp=" + DateTimeToUnixTimestamp(_timestamp));
                    RaisePropertyChanged("Timestamp");
                }
            }
        }

        private DateTime UnixTimestampToDateTime(long timestamp)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return origin.AddSeconds(timestamp);
        }

        internal long DateTimeToUnixTimestamp(DateTime date)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan diff = date.ToUniversalTime() - origin;
            return (long)diff.TotalSeconds;
        }

        private int _lifetimeSeconds;
        /// <summary>
        /// An expiration time.  After this many seconds, the Spot will automatically be
        /// removed from the radio.  Setting this property will reset the countdown timer.
        /// Warning: Because duplicate values are allowed to "reset" this timer, care should
        /// be taken in the client not to create a loop where an upstream PropertyChanged
        /// event triggers a downstream Property set call (which calls the radio, which
        /// generates a status message, which creates another PropertyChanged event...).
        /// </summary>
        public int LifetimeSeconds
        {
            get { return _lifetimeSeconds; }
            set
            {
                //if (_lifetimeSeconds != value) // allow setting the same value to go through to the radio
                {
                    _lifetimeSeconds = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " lifetime_seconds=" + _lifetimeSeconds);
                    RaisePropertyChanged("LifetimeSeconds");
                }
            }
        }

        private string _comment;
        /// <summary>
        /// The Spot comment as provided by the original Spot source.
        /// </summary>
        public string Comment
        {
            get { return _comment; }
            set
            {
                if (_comment != value)
                {
                    _comment = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " comment=" + _comment.Replace(' ', '\u007f'));
                    RaisePropertyChanged("Comment");
                }
            }
        }

        private int _priority = 5;
        /// <summary>
        /// The integer (1:higher-5:lower) priority of the Spot perhaps due to multipliers, etc.  Higher
        /// priority Spots will be shown lower on the Panadapter.
        /// </summary>
        public int Priority
        {
            get { return _priority; }
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    if (_radio != null && _index >= 0)
                        _radio.SendCommand("spot set " + _index + " priority=" + _priority);
                    RaisePropertyChanged("Priority");
                }
            }
        }

        private string _triggerAction = "tune";
        /// <summary>
        /// The action for the radio to take when a Spot is triggered (clicked in SmartSDR).
        /// The supported actions today are "tune" and "none".  The assumption is that a
        /// client that sets a Spot to "none" will likely be implementing their own
        /// functionality that tuning might interfere with.
        /// </summary>
        public string TriggerAction
        {
            get { return _triggerAction; }
            set
            {
                _triggerAction = value;
                if (_radio != null && _index >= 0)
                    _radio.SendCommand("spot set " + _index + " trigger_action=" + _triggerAction);
                RaisePropertyChanged("TriggerAction");
            }
        }

        /// <summary>
        /// A parameterless version of the Spot Trigger
        /// </summary>
        public void Trigger()
        {
            if (_radio == null || _index < 0) return;

            _radio.SendCommand("spot trigger " + _index);
        }

        /// <summary>
        /// Gets called when the Spot is interacted with (e.g. Clicked in SmartSDR) and
        /// could be called to simulate a Spot being Clicked.
        /// </summary>
        /// <param name="pan">A reference to the Panadapter to use for any actions like tuning.</param>
        public void Trigger(Panadapter pan)
        {
            // is the reference to the Panadapter null? 
            if (pan == null)
            {
                // yes -- just call the parameterless Trigger function
                Trigger();
                return;
            }

            if (_radio == null || _index < 0) return;            

            _radio.SendCommand("spot trigger " + _index + " pan=0x" + pan.StreamID.ToString("X8"));
        }

        internal void StatusUpdate(string s)
        {
            string[] words = s.Split(' ');

            if (words[0] == "triggered")
            {
                // does the status include the Panadapter StreamID?
                if (words.Length == 1)
                {
                    // no -- fire the event without it
                    _radio.OnSpotTriggered(this, null);
                }
                else if (words.Length == 2)
                {
                    // yes -- parse the StreamID and ensure it is valid
                    string[] tokens = words[1].Split('=');
                    if (tokens.Length != 2)
                    {
                        Debug.WriteLine("Spot::StatusUpdate: Invalid key/value pair (" + words[1] + ")");
                        return;
                    }

                    string key = tokens[0];
                    string value = tokens[1];
                    if (key != "pan") return; // ensure expected key

                    uint pan_stream;
                    bool b = StringHelper.TryParseInteger(value, out pan_stream);
                    if (!b) return; // ensure valid StreamID

                    Panadapter pan = _radio.FindPanadapterByStreamID(pan_stream);
                    _radio.OnSpotTriggered(this, pan);
                }
                return;
            }

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Spot::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "rx_freq":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Spot::StatusUpdate: Invalid RX Frequency (" + kv + ")");
                                continue;
                            }

                            if (_rxFrequency == temp) continue;

                            _rxFrequency = temp;
                            RaisePropertyChanged("RXFrequency");
                        }
                        break;

                    case "tx_freq":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Spot::StatusUpdate: Invalid TX Frequency (" + kv + ")");
                                continue;
                            }

                            if (_txFrequency == temp) continue;

                            _txFrequency = temp;
                            RaisePropertyChanged("TXFrequency");
                        }
                        break;

                    case "mode":
                        {
                            if (_mode == value) continue;

                            _mode = value;
                            RaisePropertyChanged("Mode");
                        }
                        break;

                    case "callsign":
                        {
                            if (_callsign == value) continue;

                            _callsign = value.Replace('\u007f', ' ');
                            RaisePropertyChanged("Callsign");
                        }
                        break;

                    case "color":
                        {
                            if (_color == value) continue;

                            _color = value;
                            RaisePropertyChanged("Color");
                        }
                        break;

                    case "background_color":
                        {
                            if (_backgroundColor == value) continue;

                            _backgroundColor = value;
                            RaisePropertyChanged("BackgroundColor");
                        }
                        break;

                    case "source":
                        {
                            if (_source == value) continue;

                            _source = value;
                            RaisePropertyChanged("Source");
                        }
                        break;

                    case "trigger_action":
                        {
                            if (_triggerAction == value) continue;

                            _triggerAction = value;
                            RaisePropertyChanged("TriggerAction");
                        }
                        break;

                    case "spotter_callsign":
                        {
                            if (_spotterCallsign == value) continue;

                            _spotterCallsign = value;
                            RaisePropertyChanged("SpotterCallsign");
                        }
                        break;

                    case "timestamp":
                        {
                            long temp;
                            bool b = long.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Spot::StatusUpdate: Invalid Timestamp (" + kv + ")");
                                continue;
                            }

                            DateTime date = UnixTimestampToDateTime(temp);

                            if (_timestamp == date) continue;

                            _timestamp = date;
                            RaisePropertyChanged("Timestamp");
                        }
                        break;

                    case "lifetime_seconds":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Spot::StatusUpdate: Invalid LifetimeSeconds (" + kv + ")");
                                continue;
                            }

                            // Note: Raise propertychanged even if the value didn't change
                            // as setting the same value can be used to reset the countdown timer
                            //if (_lifetimeSeconds == temp) continue; 

                            _lifetimeSeconds = temp;
                            RaisePropertyChanged("LifetimeSeconds");
                        }
                        break;

                    case "comment":
                        {
                            if (_comment == value) continue;

                            _comment = value.Replace('\u007f', ' ');
                            RaisePropertyChanged("Comment");
                        }
                        break;

                    case "priority":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Spot::StatusUpdate: Invalid Priority (" + kv + ")");
                                continue;
                            }

                            if (_priority == temp) continue;

                            _priority = temp;
                            RaisePropertyChanged("Priority");                           
                        }
                        break;
                }
            }
        }
    }
}
