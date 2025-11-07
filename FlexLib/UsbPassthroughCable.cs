// ****************************************************************************
///*!	\file UsbPassthroughCable.cs
// *	\brief Represents a single Passthrough USB Cable
// *
// *	\copyright	Copyright 2012-2024 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2024-10-14
// *	\author Maurice Smulders KF0GEO
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
    public class UsbPassthroughCable : UsbCable, IUsbPassthroughCable
    {
        public UsbPassthroughCable(Radio radio, string serial_number)
            : base(radio, serial_number)
        {
            _cableType = UsbCableType.Passthrough;
        }

        private SerialDataBits _dataBits;
        public SerialDataBits DataBits
        {
            get => _dataBits;
            set
            {
                if (_dataBits != value)
                {
                    _dataBits = value;
                    _radio.SendCommand($"usb_cable set {_serialNumber} data_bits={(_dataBits == SerialDataBits.seven ? "7" : "8")}");
                    RaisePropertyChanged(nameof(DataBits));
                }
            }
        }

        private SerialSpeed _speed;
        public SerialSpeed Speed
        {
            get => _speed;
            set
            {
                if (_speed != value)
                {
                    _speed = value;
                    _radio.SendCommand($"usb_cable set {_serialNumber} speed={SerialSpeedToString(_speed)}");
                    RaisePropertyChanged(nameof(Speed));
                }
            }
        }

        private string SerialSpeedToString(SerialSpeed speedEnum)
        {
            string speed = Enum.GetName(typeof(SerialSpeed), speedEnum).Substring(5);
            return speed;
        }

        private SerialSpeed StringToSerialSpeed(string speed)
        {
            string speedString = $"BAUD_{speed}";

            if (!Enum.TryParse(speedString, out SerialSpeed speedEnum))
            {
                // Default to 9600 Baud
                speedEnum = SerialSpeed.BAUD_9600;
            }

            return speedEnum;

        }

        public int SerialSpeedToInt(SerialSpeed speed)
        {
            string speed_str = Enum.GetName(typeof(SerialSpeed), speed).Substring(5); // chop off BAUD_
            int.TryParse(speed_str, out int ret_val);
            return ret_val;
        }

        private SerialParity _parity;
        public SerialParity Parity
        {
            get => _parity;
            set
            {
                if (_parity != value)
                {
                    _parity = value;
                    _radio.SendCommand($"usb_cable set {_serialNumber} parity={_parity.ToString()}");
                    RaisePropertyChanged(nameof(Parity));
                }
            }
        }

        private SerialParity StringToSerialParity(string parity)
        {
            SerialParity parityEnum;

            if (!Enum.TryParse(parity, out parityEnum))
            {
                parityEnum = SerialParity.none;
            }

            return parityEnum;
        }

        private SerialFlowControl StringToFlowControl(string flowControl)
        {
            SerialFlowControl flowControlEnum;

            if (!Enum.TryParse(flowControl, out flowControlEnum))
            {
                flowControlEnum = SerialFlowControl.none;
            }

            return flowControlEnum;
        }

        private SerialStopBits _stopBits;
        public SerialStopBits StopBits
        {
            get => _stopBits;
            set
            {
                if (_stopBits == value) return;

                _stopBits = value;
                // Command defaults to 1 stop bit
                _radio.SendCommand($"usb_cable set {_serialNumber} stop_bits={(_stopBits == SerialStopBits.Two ? "2" : "1")}");
                RaisePropertyChanged(nameof(StopBits));
            }
        }

        private SerialFlowControl _flowControl;
        public SerialFlowControl FlowControl
        {
            get => _flowControl;
            set
            {
                if (_flowControl == value) return;

                _flowControl = value;
                _radio.SendCommand($"usb_cable set {_serialNumber} flow_control={_flowControl.ToString()}");
                RaisePropertyChanged(nameof(FlowControl));
            }
        }

        internal override void ParseStatus(string s)
        {
            if (String.IsNullOrEmpty(s)) return;

            // split the status message on spaces
            string[] words = s.Split(' ');

            // Parse each of the words
            foreach (string kv in words)
            {
                string[] tokens = kv.Split(new char[] { '=' }, 2);
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("UsbPassthroughCable::ParseStatus: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key)
                {

                    case "speed":
                        {
                            _speed = StringToSerialSpeed(value);
                            RaisePropertyChanged(nameof(Speed));
                        }
                        break;

                    case "data_bits":
                        {
                            if (value == "7")
                                _dataBits = SerialDataBits.seven;
                            else // default
                                _dataBits = SerialDataBits.eight;

                            RaisePropertyChanged(nameof(DataBits));
                        }
                        break;

                    case "parity":
                        {
                            _parity = StringToSerialParity(value);
                            RaisePropertyChanged(nameof(Parity));
                        }
                        break;

                    case "stop_bits":
                        {
                            if (value == "2")
                                _stopBits = SerialStopBits.Two;
                            else // default
                                _stopBits = SerialStopBits.One;

                            RaisePropertyChanged(nameof(StopBits));
                            break;
                        }

                    case "flow_control":
                        {
                            _flowControl = StringToFlowControl(value);
                            RaisePropertyChanged(nameof(FlowControl));
                        }
                        break;
                    case "data:base64":
                        {
                            // Base64 Data returned
                            byte[] decodedData = Convert.FromBase64String(value);
                            OnDataReceived(decodedData);
                        }
                        break;
                }
            }

            // Send to the base class to parse.
            base.ParseStatus(s);
        }

        public delegate void DataRecivedEventHandler(UsbPassthroughCable cable, byte[] data);
        public event DataRecivedEventHandler DataReceived;
        private void OnDataReceived(byte[] data)
        {
            if (DataReceived != null)
                DataReceived(this, data);
        }

        /// <summary>
        /// Writes bytes to the Passthrough Cable
        /// </summary>
        /// <param name="bytes">The bytes to write.  Handle with care to avoid 7-bit encoding (ASCII) issues</param>
        public void WriteBase64(byte[] bytes)
        {
            string base64data = Convert.ToBase64String(bytes);
            _radio.SendCommand("usb_cable write " + _serialNumber + " base64 " + base64data);
        }

        public void WriteText(string text)
        {
            _radio.SendCommand("usb_cable write " + _serialNumber + " text " + text);
        }
    }
}
