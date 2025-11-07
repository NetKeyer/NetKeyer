// ****************************************************************************
///*!	\file DAXIQStream.cs
// *	\brief Represents a single IQ Stream
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2013-11-18
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Diagnostics;
using Flex.Smoothlake.FlexLib.Interface;
using Flex.Util;

namespace Flex.Smoothlake.FlexLib
{
    public class DAXIQStream : RXAudioStream, IDaxRxStream
    {
        public DAXIQStream(Radio radio) : base(radio)
        {
            _radio = radio;            
        }
        
        // TODO: This is not ideal.  This is only to satisfy the interface
        //       We might want to refactor to see if I can work it out.
        public int Gain { get; set; }

        private int _daxIQChannel = 1;
        public int DAXIQChannel
        {
            get { return _daxIQChannel; }
        }

        private Panadapter _pan;
        public Panadapter Pan
        {
            get { return _pan; }
        }

        private int _sampleRate;
        public int SampleRate
        {
            get { return _sampleRate; }
            set
            {
                if (_sampleRate != value)
                {
                    _sampleRate = value;
                    if (_radio != null)
                        _radio.SendCommand("stream set 0x" + _streamId.ToString("X") + " daxiq_rate=" + _sampleRate);
                    RaisePropertyChanged(()=>SampleRate);
                }
            }
        }

        private bool _isActive;
        /// <summary>
        /// Indicates that data is expected to stream for this IQ Client.  This may not be the case when there is another
        /// DAX client connected to the same radio and bound to the same GUI Client with this channel enabled.
        /// </summary>
        public bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    RaisePropertyChanged(()=>IsActive);
                }
            }
        }

        public void Close()
        {
            _closing = true;
            Debug.WriteLine("DAXIQStream::Close (0x" + _streamId.ToString("X") + ")");
            _radio.SendCommand("stream remove 0x" + _streamId.ToString("X"));
            _radio.RemoveDAXIQStream(_streamId);
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
                    Debug.WriteLine("DAXIQStream::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "active":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Radio::ParseRadioStatus - active: Invalid value (" + kv + ")");
                                continue;
                            }

                            IsActive = Convert.ToBoolean(temp);
                        }
                        break;

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

                    case "pan":
                        {
                            uint stream_id;
                            bool b = StringHelper.TryParseInteger(value, out stream_id);
                            if (!b)
                            {
                                Debug.WriteLine("DAXIQStream::StatusUpdate: Invalid pan stream_id (" + s + ")");
                                return;
                            }

                            Panadapter old_pan = _pan;                            
                            _pan = _radio.FindPanadapterByStreamID(stream_id);
                            if (_pan != old_pan)
                                RaisePropertyChanged("Pan");
                        }
                        break;

                    case "daxiq_rate":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("DAXIQStream::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _sampleRate = temp;
                            RaisePropertyChanged("SampleRate");
                        }
                        break;

                    case "daxiq_channel":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("DAXIQStream::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_daxIQChannel != (int)temp)
                            {
                                _daxIQChannel = (int)temp;
                                RaisePropertyChanged("DAXIQChannel");
                            }
                        }
                        break;

                    default:
                        Debug.WriteLine("DAXIQStream::StatusUpdate: Key not parsed (" + kv + ")");
                        break;
                }
            }

            if (set_radio_ack)
            {
                RadioAck = true;
                _radio.OnDAXIQStreamAdded(this);

                _statsTimer.Enabled = true;
            }
        }
    }
}
