// ****************************************************************************
///*!	\file UsbCatCable.cs
// *	\brief Represents a single CAT USB Cable
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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.ComponentModel;
// using Flex.UiWpfFramework.Utils;


namespace Flex.Smoothlake.FlexLib
{
    public class UsbCatCable : UsbCable, IUsbCatCable
    {
        public UsbCatCable(Radio radio, string serial_number)
            : base(radio, serial_number)
        {
            _cableType = UsbCableType.CAT;
        }

        private SerialDataBits _dataBits;
        public SerialDataBits DataBits
        {
            get { return _dataBits; }
            set
            {
                if (_dataBits != value)
                {
                    _dataBits = value;
                    _radio.SendCommand("usb_cable set " + _serialNumber + " data_bits=" + (_dataBits == SerialDataBits.seven ? "7" : "8"));
                    RaisePropertyChanged("DataBits");
                }
            }
        }

        private SerialSpeed _speed;
        public SerialSpeed Speed
        {
            get { return _speed; }
            set
            {
                if (_speed != value)
                {
                    _speed = value;
                    _radio.SendCommand("usb_cable set " + _serialNumber + " speed=" + SerialSpeedToString(_speed));
                    RaisePropertyChanged("Speed");
                }
            }
        }

        private string SerialSpeedToString(SerialSpeed speedEnum)
        {
            string speed = "9600";
            switch (speedEnum)
            {
                case SerialSpeed.BAUD_300: speed = "300"; break;
                case SerialSpeed.BAUD_600: speed = "600"; break;
                case SerialSpeed.BAUD_1200: speed = "1200"; break;
                case SerialSpeed.BAUD_2400: speed = "2400"; break;
                case SerialSpeed.BAUD_4800: speed = "4800"; break;
                case SerialSpeed.BAUD_9600: speed = "9600"; break;
                case SerialSpeed.BAUD_14400: speed = "14400"; break;
                case SerialSpeed.BAUD_19200: speed = "19200"; break;
                case SerialSpeed.BAUD_38400: speed = "38400"; break;
                case SerialSpeed.BAUD_57600: speed = "57600"; break;
                case SerialSpeed.BAUD_115200: speed = "115200"; break;
                case SerialSpeed.BAUD_230400: speed = "230400"; break;
                case SerialSpeed.BAUD_460800: speed = "460800"; break;
                case SerialSpeed.BAUD_921600: speed = "921600"; break;
            }

            return speed;
        }

        private SerialSpeed StringToSerialSpeed(string speed)
        {
            SerialSpeed speedEnum = SerialSpeed.BAUD_9600;

            switch (speed)
            {
                case "300": speedEnum = SerialSpeed.BAUD_300; break;
                case "600": speedEnum = SerialSpeed.BAUD_600; break;
                case "1200": speedEnum = SerialSpeed.BAUD_1200; break;
                case "2400": speedEnum = SerialSpeed.BAUD_2400; break;
                case "4800": speedEnum = SerialSpeed.BAUD_4800; break;
                case "9600": speedEnum = SerialSpeed.BAUD_9600; break;
                case "14400": speedEnum = SerialSpeed.BAUD_14400; break;
                case "19200": speedEnum = SerialSpeed.BAUD_19200; break;
                case "38400": speedEnum = SerialSpeed.BAUD_38400; break;
                case "57600": speedEnum = SerialSpeed.BAUD_57600; break;
                case "115200": speedEnum = SerialSpeed.BAUD_115200; break;
                case "230400": speedEnum = SerialSpeed.BAUD_230400; break;
                case "460800": speedEnum = SerialSpeed.BAUD_460800; break;
                case "921600": speedEnum = SerialSpeed.BAUD_921600; break;
            }

            return speedEnum;

        }

        private SerialParity _parity;
        public SerialParity Parity
        {
            get { return _parity; }
            set
            {
                if (_parity != value)
                {
                    _parity = value;
                    _radio.SendCommand("usb_cable set " + _serialNumber + " parity=" + _parity.ToString());
                    RaisePropertyChanged("Parity");
                }
            }
        }

        private SerialParity StringToSerialParity(string parity)
        {
            SerialParity parityEnum = SerialParity.none;

            switch (parity)
            {
                case "none":
                    parityEnum = SerialParity.none;
                    break;
                case "odd":
                    parityEnum = SerialParity.odd;
                    break;
                case "even":
                    parityEnum = SerialParity.even;
                    break;
                case "mark":
                    parityEnum = SerialParity.mark;
                    break;
                case "space":
                    parityEnum = SerialParity.space;
                    break;
            }

            return parityEnum;
        }

        private SerialFlowControl StringToFlowControl(string flowControl)
        {
            SerialFlowControl flowControlEnum = SerialFlowControl.none;

            switch (flowControl)
            {
                case ("none"):
                    flowControlEnum = SerialFlowControl.none;
                    break;
                case ("rts_cts"):
                    flowControlEnum = SerialFlowControl.rts_cts;
                    break;
                case ("dtr_dsr"):
                    flowControlEnum = SerialFlowControl.dtr_dsr;
                    break;
                case ("xon_xoff"):
                    flowControlEnum = SerialFlowControl.xon_xoff;
                    break;
            }

            return flowControlEnum;
        }

        private SerialStopBits _stopBits;
        public SerialStopBits StopBits
        {
            get { return _stopBits; }
            set
            {
                if (_stopBits != value)
                {
                    _stopBits = value;
                    _radio.SendCommand("usb_cable set " + _serialNumber + " stop_bits=" + (_stopBits == SerialStopBits.One ? "1" : "2"));
                    RaisePropertyChanged("StopBits");
                }
            }
        }

        private SerialFlowControl _flowControl;
        public SerialFlowControl FlowControl
        {
            get { return _flowControl; }
            set
            {
                if (_flowControl != value)
                {
                    _flowControl = value;
                    _radio.SendCommand("usb_cable set " + _serialNumber + " flow_control=" + _flowControl.ToString());
                    RaisePropertyChanged("FlowControl");
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
                if (_selectedRxAnt != value)
                {
                    _selectedRxAnt = value;

                    if (_radio != null)
                        _radio.SendCommand("usb_cable set " + _serialNumber + " source_rx_ant=" + _selectedRxAnt);

                    RaisePropertyChanged("SelectedRxAnt");
                }
            }
        }

        private string _selectedTxAnt;
        public string SelectedTxAnt
        {
            get { return _selectedTxAnt; }
            set
            {
                if (_selectedTxAnt != value)
                {
                    _selectedTxAnt = value;
                    if (_radio != null)
                        _radio.SendCommand("usb_cable set " + _serialNumber + " source_tx_ant=" + _selectedTxAnt);

                    RaisePropertyChanged("SelectedTxAnt");
                }
            }
        }

        private string _selectedSlice;
        public string SelectedSlice
        {
            get { return _selectedSlice; }
            set
            {
                if (_selectedSlice != value)
                {
                    _selectedSlice = value;
                    if (_radio != null)
                        _radio.SendCommand("usb_cable set " + _serialNumber + " source_slice=" + _selectedSlice);

                    RaisePropertyChanged("SelectedSlice");
                }
            }
        }

        private bool _autoReport;
        public bool AutoReport
        {
            get { return _autoReport; }
            set
            {
                if (_autoReport != value)
                {
                    _autoReport = value;
                    if (_radio != null)
                        _radio.SendCommand("usb_cable set " + _serialNumber + " auto_report=" + Convert.ToByte(_autoReport));
                }
            }
        }

        internal override void ParseStatus(string s)
        {
            if (String.IsNullOrEmpty(s)) return;

            // split the status message on spaces
            string[] words = s.Split(' ');

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
                    case "auto_report":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("UsbCatCable::ParseStatus - enable: Invalid value (" + temp.ToString() + ")");
                                break;
                            }

                            _autoReport = Convert.ToBoolean(temp);
                            RaisePropertyChanged("AutoReport");
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

                    case "speed":
                        {
                            _speed = StringToSerialSpeed(value);
                            RaisePropertyChanged("Speed");
                        }
                        break;

                    case "data_bits":
                        {
                            if (value == "7")
                                _dataBits = SerialDataBits.seven;
                            else if (value == "8")
                                _dataBits = SerialDataBits.eight;

                            RaisePropertyChanged("DataBits");
                        }
                        break;

                    case "parity":
                        {
                            _parity = StringToSerialParity(value);
                            RaisePropertyChanged("Parity");
                        }
                        break;

                    case "stop_bits":
                        {
                            if (value == "1")
                                _stopBits = SerialStopBits.One;
                            else if (value == "2")
                                _stopBits = SerialStopBits.Two;

                            RaisePropertyChanged("StopBits");
                            break;
                        }

                    case "flow_control":
                        {
                            _flowControl = StringToFlowControl(value);
                            RaisePropertyChanged("FlowControl");
                        }
                        break;
                }
            }

            // Send to the base class to parse.
            base.ParseStatus(s);
        }
    }

    // [TypeConverter(typeof(EnumDescriptionTypeConverter))] - REMOVED FOR CROSS-PLATFORM
    public enum SerialDataBits
    {
        [Description("7")]
        seven,
        [Description("8")]
        eight
    }

    // [TypeConverter(typeof(EnumDescriptionTypeConverter))] - REMOVED FOR CROSS-PLATFORM
    public enum SerialSpeed
    {
        [Description("300")]
        BAUD_300,
        [Description("600")]
        BAUD_600,
        [Description("1200")]
        BAUD_1200,
        [Description("2400")]
        BAUD_2400,
        [Description("4800")]
        BAUD_4800,
        [Description("9600")]
        BAUD_9600,
        [Description("14400")]
        BAUD_14400,
        [Description("19200")]
        BAUD_19200,
        [Description("38400")]
        BAUD_38400,
        [Description("57600")]
        BAUD_57600,
        [Description("115200")]
        BAUD_115200,
        [Description("230400")]
        BAUD_230400,
        [Description("460800")]
        BAUD_460800,
        [Description("921600")]
        BAUD_921600
    }

    // [TypeConverter(typeof(EnumDescriptionTypeConverter))] - REMOVED FOR CROSS-PLATFORM
    public enum SerialParity
    {
        [Description("None")]
        none,
        [Description("Odd")]
        odd,
        [Description("Even")]
        even,
        [Description("Mark")]
        mark,
        [Description("Space")]
        space
    }

    // [TypeConverter(typeof(EnumDescriptionTypeConverter))] - REMOVED FOR CROSS-PLATFORM
    public enum SerialFlowControl
    {
        [Description("None")]
        none,
        [Description("RTS/CTS")]
        rts_cts,
        [Description("DSR/DTR")]
        dtr_dsr,
        [Description("XON/XOFF")]
        xon_xoff
    }

    // [TypeConverter(typeof(EnumDescriptionTypeConverter))] - REMOVED FOR CROSS-PLATFORM
    public enum SerialStopBits
    {
        [Description("1")]
        One,
        [Description("2")]
        Two
    }
}
