// ****************************************************************************
///*!	\file DAXMICAudioStream.cs
// *	\brief Represents a single MICAudio Stream (narrow, mono)
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Flex.UiWpfFramework.Mvvm;
using Flex.Util;
using Flex.Smoothlake.Vita;
using System.Threading;
using Flex.Smoothlake.FlexLib.Interface;

namespace Flex.Smoothlake.FlexLib
{
    public class DAXMICAudioStream : RXAudioStream, IDaxRxStream
    {
        public DAXMICAudioStream(Radio radio) :base(radio)
        {
            _radio = radio;
            _shouldApplyRxGainScalar = true;
        }

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

                if (_rxGain != new_gain)
                {
                    _rxGain = new_gain;
                    RaisePropertyChanged("RXGain");
                }
                else if (new_gain != value)
                {
                    RaisePropertyChanged("RXGain");
                }

                if (_rxGain == 0)
                {
                    _rxGainScalar = 0.0f;
                    return;
                }
                double db_min = -10.0;
                double db_max = +10.0;
                double db = db_min + (_rxGain / 100.0) * (db_max - db_min);
                _rxGainScalar = (float)Math.Pow(10.0, db / 20.0);
            }
        }
        
        public int Gain
        {
            get => RXGain;
            set
            {
                if (RXGain == value)
                    return;

                RXGain = value;
                RaisePropertyChanged("Gain");
            }
        }

        public void Close()
        {
            Debug.WriteLine("DAXMICAudioStream::Close (0x" + _streamId.ToString("X") + ")");
            _closing = true;
            _radio.SendCommand("stream remove 0x" + _streamId.ToString("X"));
            _radio.RemoveDAXMICAudioStream(_streamId);
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
                    Debug.WriteLine("DAXMICAudioStream::StatusUpdate: Invalid key/value pair (" + kv + ")");
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

                    default:
                        Debug.WriteLine("DAXMICAudioStream::StatusUpdate: Key not parsed (" + kv + ")");
                        break;
                }
            }

            if (set_radio_ack)
            {
                set_radio_ack = false;
                RadioAck = true;
                _radio.OnDAXMICAudioStreamAdded(this);

                _statsTimer.Enabled = true;
            }
        }
    }
}
