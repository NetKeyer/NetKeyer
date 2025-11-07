// ****************************************************************************
///*!	\file RXRemoteAudioStream.cs
// *	\brief Represents a single remote audio recieve stream
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2018-10-17
// *	\author Abed Haque, AB5ED
// */
// ****************************************************************************

using Flex.Util;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;


namespace Flex.Smoothlake.FlexLib
{
    public class RXRemoteAudioStream : RXAudioStream
    {
        public RXRemoteAudioStream(Radio radio): base(radio)
        {
            _opusRXList.Capacity = 50;

            // For uncompressed remote rx audio, the rx gain is applied here on the client side.
            _shouldApplyRxGainScalar = true;

            // For compressed remote rx audio, the rx gain is applied later on when the opus
            // packets are being decoded.
        }      

        public void Close()
        {
            Debug.WriteLine("RXREmoteAudioStream::Close (0x" + _streamId.ToString("X") + ")");
            _radio.RemoveRXRemoteAudioStream(_streamId);
        }

        internal void Remove()
        {
            _closing = true;
            Debug.WriteLine("RXREmoteAudioStream::Remove (0x" + _streamId.ToString("X") + ")");
            _radio.SendCommand("stream remove 0x" + _streamId.ToString("X"));
        }

        private bool _isCompressed;
        public bool IsCompressed
        {
            get { return _isCompressed; }
            set
            {
                _isCompressed = value;
                RaisePropertyChanged("IsCompressed");
            }
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
                    UpdateUncompressedRxGainScalar();
                    RaisePropertyChanged("RXGain");
                }
            }
        }

        private bool _rxMute;
        public bool RxMute
        {
            get { return _rxMute; }
            set
            {
                _rxMute = value;
                UpdateUncompressedRxGainScalar();
                RaisePropertyChanged("RxMute");
            }
        }

        private void UpdateUncompressedRxGainScalar()
        {
            if (_rxGain == 0 || _rxMute)
            {
                _rxGainScalar = 0;
            }
            else
            {

                double db_min = -20.0;
                double db_max = 0.0;
                double db = db_min + (_rxGain / 100.0) * (db_max - db_min);
#if MAESTRO
                // 1.25 factor is to increase volume and in both the speakers and headphones in Maestro
                _rxGainScalar = (float)Math.Pow(10.0, db / 20.0) * 1.25f;
#else
                _rxGainScalar = (float)Math.Pow(10.0, db / 20.0);
#endif

            }
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
                    Debug.WriteLine("RXRemoteAudioStream::StatusUpdate: Invalid key/value pair (" + kv + ")");
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

                    case "compression":
                        IsCompressed = value == "OPUS" ? true : false;
                        break;

                    default:
                        Debug.WriteLine("RXRemoteAudioStream::StatusUpdate: Key not parsed (" + kv + ")");
                        break;
                }
            }

            if (set_radio_ack)
            {
                RadioAck = true;
                _radio.OnRXRemoteAudioStreamAdded(this);

                _statsTimer.Enabled = true;
            }
        }
    }
}
