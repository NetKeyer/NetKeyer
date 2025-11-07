// ****************************************************************************
///*!	\file DAXRXAudioStream.cs
// *	\brief Represents a single Audio Stream (narrow, mono)
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2013-11-18
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************


using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Threading;
using Flex.Smoothlake.FlexLib.Interface;
using Flex.Util;

namespace Flex.Smoothlake.FlexLib
{
    public class DAXRXAudioStream : RXAudioStream, IDaxRxStream
    {
        public DAXRXAudioStream(Radio radio) : base(radio)
        {

        }

        private int _daxChannel;
        public int DAXChannel
        {
            get { return _daxChannel; }
            internal set
            {
                if (_daxChannel != value)
                {
                    _daxChannel = value;
                    RaisePropertyChanged("DAXChannel");
                }
            }
        }

        private Slice _slice;
        public Slice Slice
        {
            get { return _slice; }
            internal set
            {
                if (_slice != value)
                {
                    _slice = value;
                    _gainNeverSet = true; // force RXGain to be set again, if called
                    RaisePropertyChanged("Slice");                    
                }
            }
        }

        // ensure that not having a good Slice reference when the gain is set doesn't 
        // keep us from setting it when we do have a good reference
        private bool _gainNeverSet = true;
        
        private int _rxGain = 50;
        public int RXGain
        {
            get { return _rxGain; }
            set
            {
                int new_gain = value;

                // check limits
                if (new_gain > 100) new_gain = 100;
                if (new_gain < 0) new_gain = 0;

                if (_rxGain != new_gain || _gainNeverSet)
                {
                    _rxGain = new_gain;
                    if (_slice != null)
                    {
                        _radio.SendCommand("audio stream 0x" + _streamId.ToString("X") + " slice " + _slice.Index + " gain " + value);
                        _gainNeverSet = false;
                    }
                    RaisePropertyChanged("RXGain");
                }
                else if (new_gain != value)
                {
                    RaisePropertyChanged("RXGain");
                }
            }
        }
        
        public int Gain
        {
            get => RXGain;
            set
            {
                if (RXGain == value && !_gainNeverSet)
                    return;

                RXGain = value;
                RaisePropertyChanged("Gain");
            }
        }

        public void Close()
        {
            Debug.WriteLine("DAXRXAudioStream::Close (0x" + _streamId.ToString("X") + ")");
            _closing = true;
            _radio.SendCommand("stream remove 0x" + _streamId.ToString("X"));
            _radio.RemoveAudioStream(_streamId);
        }

        public void StatusUpdate(string s)
        {
            bool set_radio_ack = false;
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("DAXRXAudioStream::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "client_handle":
                        {
                            uint temp;
                            bool b = StringHelper.TryParseInteger(value, out temp);

                            if (!b) continue;

                            _clientHandle = temp;
                            RaisePropertyChanged("ClientHandle");

                            if (!_radioAck)
                                set_radio_ack = true;
                        }
                        break;

                    case "dax_channel":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("DAXRXAudioStream::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            DAXChannel = (int)temp;
                        }
                        break;

                    case "removed":
                        {
                            Close();
                        }
                        break;

                    case "slice":
                        {
                            Slice old_slice = _slice;
                            GUIClient gui_client = _radio.FindGUIClientByClientID(_radio.BoundClientID);

                            // do we have a good reference to the GUI Client?
                            if (gui_client == null)
                            {
                                // no -- clear the Slice reference and carry on
                                _slice = null;
                                if (_slice != old_slice)
                                    RaisePropertyChanged("Slice");
                                continue;
                            }
                            
                            // Must call the public Slice setter here since the setter has logic
                            // that syncs the RXGain of the channel, in the case that a client
                            // has added been started after DAX or a new Slice has been added.
                            Slice = _radio.FindSliceByLetter(value, gui_client.ClientHandle);

                            if(_slice != old_slice)
                                RaisePropertyChanged("Slice");
                        }
                        break;

                    default:
                        Debug.WriteLine("DAXRXAudioStream::StatusUpdate: Key not parsed (" + kv + ")");
                        break;
                }
            }

            if (set_radio_ack)
            {
                RadioAck = true;
                _radio.OnAudioStreamAdded(this);

                _statsTimer.Enabled = true;
            }
        }
    }
}
