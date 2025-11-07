// ****************************************************************************
///*!	\file UsbBcdCable.cs
// *	\brief Represents a single BCD USB Cable
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2016-09-13
// *	\author Abed Haque AB5ED
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace Flex.Smoothlake.FlexLib
{
    public class UsbBcdCable : UsbCable, IUsbBcdCable
    {
        public UsbBcdCable(Radio radio, string serial_number, string bcd_type)
            : base(radio, serial_number)
        {
            _cableType = UsbCableType.BCD;
            _bcdType = StringToBcdCableType(bcd_type);
        }

        private bool _isActiveHigh;
        public bool IsActiveHigh
        {
            get { return _isActiveHigh; }
            set
            {
                if (_isActiveHigh != value)
                {
                    _isActiveHigh = value;
                    if (_radio != null)
                        _radio.SendCommand("usb_cable set " + _serialNumber + " polarity=" + (_isActiveHigh ? "active_high" : "active_low"));
                    RaisePropertyChanged("IsActiveHigh");
                }
            }
        }

        private UsbCableFreqSource _source = UsbCableFreqSource.None;
        public UsbCableFreqSource Source
        {
            get { return _source; }
            set
            {
                if (_source != value)
                {
                    _source = value;

                    if (_radio != null)
                        _radio.SendCommand("usb_cable set " + _serialNumber + " source=" + UsbCableFreqSourceToString(_source));
                }
            }
        }

        private string _selectedRxAnt;
        public string SelectedRxAnt
        {
            get { return _selectedRxAnt; }
            set
            {
                _selectedRxAnt = value;

                if (_radio != null)
                    _radio.SendCommand("usb_cable set " + _serialNumber + " source_rx_ant=" + _selectedRxAnt);
                RaisePropertyChanged("SelectedRxAnt");
            }
        }

        private string _selectedTxAnt;
        public string SelectedTxAnt
        {
            get { return _selectedTxAnt; }
            set
            {
                _selectedTxAnt = value;

                if (_radio != null)
                    _radio.SendCommand("usb_cable set " + _serialNumber + " source_tx_ant=" + _selectedTxAnt);

                RaisePropertyChanged("SelectedTxAnt");
            }
        }

        private string _selectedSlice;
        public string SelectedSlice
        {
            get { return _selectedSlice; }
            set
            {
                _selectedSlice = value;

                if (_radio != null)
                    _radio.SendCommand("usb_cable set " + _serialNumber + " source_slice=" + _selectedSlice);

                RaisePropertyChanged("SelectedSlice");
            }
        }

        private BcdCableType _bcdType;
        public BcdCableType BcdType
        {
            get { return _bcdType; }
            set
            {
                _bcdType = value;

                if (_radio != null)
                    _radio.SendCommand("usb_cable set " + _serialNumber + " type=" + BcdCableTypeToString(_bcdType));
            }
        }

        private string BcdTypeToString(BcdCableType type)
        {
            string typeString = "";

            switch (type)
            {
                // bcd cable handled elsewhere
                case BcdCableType.Invalid:
                    typeString = "invalid";
                    break;
                case BcdCableType.HF_BCD:
                    typeString = "bcd";
                    break;
                case BcdCableType.VHF_BCD:
                    typeString = "vbcd";
                    break;
                case BcdCableType.HF_VHF_BCD:
                    typeString = "bcd_vbcd";
                    break;
                default:
                    typeString = "invalid";
                    break;
            }

            return typeString;
        }

        internal override void ParseStatus(string s)
        {
            if (String.IsNullOrEmpty(s)) return;

            // split the status message on spaces
            string[] words = s.Split(' ');

            // is the first word a type that matches bcd, vbcd, or vhf_bcd?
            if (words.Length > 1 && words[0] == "type=bcd" || words[0] == "type=vbcd" || words[0] == "type=bcd_vbcd")
            {
                // yes -- parse it here
                foreach (string kv in words.Skip(1)) // skip parsing the type since we have already done that
                {
                    string[] tokens = kv.Split('=');
                    if (tokens.Length != 2)
                    {
                        Debug.WriteLine("UsbBitCable::ParseStatus: Invalid key/value pair (" + kv + ")");
                        continue;
                    }

                    string key = tokens[0];
                    string value = tokens[1];

                    switch (key)
                    {
                        case "polarity":
                            {
                                bool polarity;
                                if (value == "active_high")
                                    polarity = true;
                                else if (value == "active_low")
                                    polarity = false;
                                else break;

                                _isActiveHigh = polarity;
                                RaisePropertyChanged("IsActiveHigh");
                            }
                            break;

                        case "source":
                            {
                                UsbCableFreqSource source = StringToUsbCableFreqSource(value);

                                _source = source;
                                RaisePropertyChanged("Source");
                            }
                            break;

                        case "source_rx_ant":
                            {
                                _selectedRxAnt = value;
                                RaisePropertyChanged("SelectedRxAnt");
                            }
                            break;

                        case "source_tx_ant":
                            {
                                _selectedTxAnt = value;
                                RaisePropertyChanged("SelectedTxAnt");
                            }
                            break;

                        case "source_slice":
                            {
                                _selectedSlice = value;
                                RaisePropertyChanged("SelectedSlice");
                            }
                            break;
                    }
                }
            }

            // Send to the base class to parse.
            base.ParseStatus(s);
        }

        private BcdCableType StringToBcdCableType(string s)
        {
            BcdCableType bcdType = BcdCableType.Invalid;

            switch (s)
            {
                case "bcd": bcdType = BcdCableType.HF_BCD; break;
                case "vbcd": bcdType = BcdCableType.VHF_BCD; break;
                case "bcd_vbcd": bcdType = BcdCableType.HF_VHF_BCD; break;
            }

            return bcdType;
        }

        private string BcdCableTypeToString(BcdCableType type)
        {
            string ret_val = "";

            switch (type)
            {
                case BcdCableType.HF_BCD:
                    ret_val = "bcd";
                    break;
                case BcdCableType.VHF_BCD:
                    ret_val = "vbcd";
                    break;
                case BcdCableType.HF_VHF_BCD:
                    ret_val = "bcd_vbcd";
                    break;
            }

            return ret_val;
        }
    }
}
