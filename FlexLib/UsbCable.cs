// ****************************************************************************
///*!	\file UsbCable.cs
// *	\brief Base class of a single USB Cable (CAT or Bit type)
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2016-08-09
// *	\author Eric Wachsmann KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Flex.UiWpfFramework.Mvvm;
using System.Diagnostics;
using System.ComponentModel;
// using Flex.UiWpfFramework.Utils;
using Flex.Util;


namespace Flex.Smoothlake.FlexLib
{    
    public abstract class UsbCable : ObservableObject, IUsbCable
    {
        protected Radio _radio;
        public UsbCable(Radio radio, string serial_number)
        {
            _radio = radio;
            _serialNumber = serial_number;
        }

        public event EventHandler<LogMessageEventArgs> LogTextReceived;
        private void OnLogTextReceived(LogMessageEventArgs e)
        {
            if (LogTextReceived != null)
                LogTextReceived(this, e);
        }

        protected string _serialNumber;
        public string SerialNumber
        {
            get { return _serialNumber; }
        }

        protected UsbCableType _cableType = UsbCableType.Invalid;
        public UsbCableType CableType
        {
            get { return _cableType; }
            set
            {
                if (_cableType != value)
                {
                    _cableType = value;
                    _radio.RemoveUsbCable(this);
                    _radio.SendCommand("usb_cable set " + _serialNumber + " type=" + CableTypeToString(_cableType));
                    RaisePropertyChanged("CableType");
                }
            }
        }

        protected bool _enabled = false;
        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled != value)
                {
                    if (_serialNumber == null)
                    {
                        RaisePropertyChanged("Enabled");
                        return;
                    }

                    _enabled = value;

                    if(_radio != null)
                        _radio.SendCommand("usb_cable set " + _serialNumber + " enable=" + Convert.ToByte(_enabled));
                    
                    RaisePropertyChanged("Enabled");
                }
            }
        }

        protected bool _present = false;
        public bool Present
        {
            get { return _present; }            
        }

        protected string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    
                    if (_radio != null)
                        _radio.SendCommand("usb_cable set " + _serialNumber + " name=" + EncodeSpaceCharacters(_name));

                    RaisePropertyChanged("Name");
                }
            }
        }

        protected bool _loggingEnabled;
        public bool LoggingEnabled
        {
            get { return _loggingEnabled; }
            set
            {
                if (_loggingEnabled != value)
                {
                    _loggingEnabled = value;

                    if (_radio != null)
                        _radio.SendCommand("usb_cable set " + _serialNumber + " log=" + (_loggingEnabled ? "1" : "0"));
                }
            }
        }

        protected string UsbCableFreqSourceToString(UsbCableFreqSource source)
        {
            string ret_val = "";

            switch(source)
            {
                case UsbCableFreqSource.None: ret_val = "None"; break;
                case UsbCableFreqSource.TXPanadapter: ret_val = "tx_pan"; break;
                case UsbCableFreqSource.TXSlice: ret_val = "tx_slice"; break;
                case UsbCableFreqSource.ActiveSlice: ret_val = "active_slice"; break;
                case UsbCableFreqSource.TXAntenna: ret_val = "tx_ant"; break;
                case UsbCableFreqSource.RXAntenna: ret_val = "rx_ant"; break;
                case UsbCableFreqSource.Slice: ret_val = "ordinal_slice"; break;
            }

            return ret_val;
        }

        protected UsbCableFreqSource StringToUsbCableFreqSource(string s)
        {
            UsbCableFreqSource ret_val = UsbCableFreqSource.None;

            switch (s)
            {
                case "None": ret_val = UsbCableFreqSource.None; break;
                case "tx_pan": ret_val = UsbCableFreqSource.TXPanadapter; break;
                case "tx_slice": ret_val = UsbCableFreqSource.TXSlice; break;
                case "active_slice": ret_val = UsbCableFreqSource.ActiveSlice; break;
                case "tx_ant": ret_val = UsbCableFreqSource.TXAntenna; break;
                case "rx_ant": ret_val = UsbCableFreqSource.RXAntenna; break;
                case "ordinal_slice": ret_val = UsbCableFreqSource.Slice; break;
            }

            return ret_val;
        }

        private UsbCableType StringToUsbCableType(string s)
        {
            UsbCableType type = UsbCableType.Invalid;

            switch (s)
            {
                case "cat":
                    type = UsbCableType.CAT;
                    break;
                case "passthrough":
                    type = UsbCableType.Passthrough;
                    break;
                case "bcd":                    
                case "vbcd":
                case "bcd_vbcd":
                    type = UsbCableType.BCD;
                    break;
                case "bit":
                    type = UsbCableType.Bit;
                    break;
                case "ldpa":
                    type = UsbCableType.LDPA;
                    break;
                case "invalid":
                    type = UsbCableType.Invalid;
                    break;
            }

            return type;
        }

        private string CableTypeToString(UsbCableType type)
        {
            string typeString = "";

            switch (type)
            {
                case UsbCableType.CAT:
                    typeString = "cat";
                    break;
                case UsbCableType.Bit:
                    typeString = "bit";
                    break;
                case UsbCableType.BCD:
                    // If changing to BCD, we'll default the cable to a bcd type
                    // and then the user can select a specific bcd type.
                    typeString = "bcd";
                    break;
                case UsbCableType.LDPA:
                    typeString = "ldpa";
                    break;
                case UsbCableType.Passthrough:
                    typeString = "passthrough";
                    break;
                case UsbCableType.Invalid:
                    typeString = "invalid";
                    break;
                default:
                    typeString = "invalid";
                    break;
            }

            return typeString;
        }

        public void Remove()
        {
            _radio.SendCommand("usb_cable remove " + _serialNumber);
        }

        internal virtual void ParseStatus(string s)
        {
            // split the status message on spaces
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split(new char[] { '=' }, 2);
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("UsbCable::ParseStatus: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key)
                {
                    case "enable":
                        {
                            // Bit cables also have an "enable" field per bit, but these are handled in the child class
                            // and won't show up here

                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("UsbCatCable::ParseStatus - enable: Invalid value (" + kv + ")");
                                continue;
                            }

                            _enabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("Enabled");
                        }
                        break;

                    case "plugged_in":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("UsbBitCable::ParseStatus - enable: Invalid value (" + kv + ")");
                                continue;
                            }

                            _present = Convert.ToBoolean(temp);
                            RaisePropertyChanged("Present");
                        }
                        break;

                    case "name":
                        {
                            string nameWithSpaces = DecodeSpaceCharacters(value);
                            _name = nameWithSpaces;
                            RaisePropertyChanged("Name");
                        }
                        break;

                    case "log":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("UsbBitCable::ParseStatus - log: Invalid value (" + kv + ")");
                                continue;
                            }

                            _loggingEnabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("LoggingEnabled");
                        }
                        break;

                    case "log_line":
                        {
                            string messageWithSpaces = DecodeSpaceCharacters(value);
                            string messageWithNonPrintableChars = StringHelper.HandleNonPrintableChars(messageWithSpaces);
                            OnLogTextReceived(new LogMessageEventArgs(messageWithNonPrintableChars));
                        }
                        break;

                    case "type":
                        {
                            CableType = StringToUsbCableType(value);
                        }
                        break;
                }
            }
        }

        private string DecodeSpaceCharacters(string value)
        {
            return value.Replace((char)(0x7F), ' ');
        }

        private string EncodeSpaceCharacters(string value)
        {
            return value.Replace(' ', (char)(0x7F));
        }
    }

    public enum UsbCableType
    {
        Invalid,
        CAT,
        Bit,
        BCD,
        LDPA,
        Passthrough
    }

    public enum BcdCableType
    {
        Invalid,
        HF_BCD,
        VHF_BCD,
        HF_VHF_BCD
    }

    // [TypeConverter(typeof(EnumDescriptionTypeConverter))] - REMOVED FOR CROSS-PLATFORM
    public enum LdpaBand
    {
        [Description("2m")]
        LDPA_2m,
        [Description("4m")]
        LDPA_4m
    }

    public enum UsbCableFreqSource
    {
        None,
        TXPanadapter,
        TXSlice,
        ActiveSlice,
        TXAntenna,
        RXAntenna,
        Slice,
        Invalid,
    }

    public class LogMessageEventArgs : EventArgs
    {
        public LogMessageEventArgs(string s)
        {
            message = s;
        }
        private string message;

        public string Message
        {
            get { return message; }
            set { message = value; }
        }
    }
}
