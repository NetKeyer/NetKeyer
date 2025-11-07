// ****************************************************************************
///*!	\file UsbBitCable.cs
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


namespace Flex.Smoothlake.FlexLib
{
    public enum UsbBitCableOutputType
    {
        Band,
        FreqRange        
    }

    public class UsbBitCable : UsbCable, IUsbBitCable
    {
        public UsbBitCable(Radio radio, string serial_number)
            : base(radio, serial_number)
        {
            _cableType = UsbCableType.Bit;
        }

        private UsbCableFreqSource[] _bitSource = new UsbCableFreqSource[8];
        public UsbCableFreqSource[] BitSource
        {
            get { return _bitSource; }
        }

        public void SetBitSource(int bit, UsbCableFreqSource source)
        {
            if (bit < 0 || bit > 7) return;
            if (_bitSource[bit] == source) return;

            _bitSource[bit] = source;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " source=" + UsbCableFreqSourceToString(_bitSource[bit]));

            RaisePropertyChanged("BitSource");
        }

        private UsbBitCableOutputType[] _bitOutput = new UsbBitCableOutputType[8];
        public UsbBitCableOutputType[] BitOutput
        {
            get { return _bitOutput; }
        }

        public void SetBitOutput(int bit, UsbBitCableOutputType output)
        {
            if (bit < 0 || bit > 7) return;
            if (_bitOutput[bit] == output) return;

            _bitOutput[bit] = output;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " output=" + UsbBitCableOutputTypeToString(_bitOutput[bit]));

            RaisePropertyChanged("BitOutput");
        }

        private bool[] _bitActiveHigh = new bool[8];
        public bool[] BitActiveHigh
        {
            get { return _bitActiveHigh; }
        }

        public void SetBitActiveHigh(int bit, bool polarity)
        {
            if (bit < 0 || bit > 7) return;
            if (_bitActiveHigh[bit] == polarity) return;

            _bitActiveHigh[bit] = polarity;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " polarity=" + (_bitActiveHigh[bit] ? "active_high" : "active_low"));

            RaisePropertyChanged("BitActiveHigh");
        }

        private bool[] _bitEnable = new bool[8];
        public bool[] BitEnable
        {
            get { return _bitEnable; }
        }

        private bool[] _bitPtt = new bool[8];
        public bool[] BitPtt
        {
            get { return _bitPtt; }
        }

        public void SetBitEnable(int bit, bool enabled)
        {
            if (bit < 0 || bit > 7) return;
            if (_bitEnable[bit] == enabled) return;

            _bitEnable[bit] = enabled;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " enable=" + (_bitEnable[bit] ? "1" : "0"));

            RaisePropertyChanged("BitEnable");
        }

        public void SetBitPtt(int bit, bool enabled)
        {
            if (bit < 0 || bit > 7) return;
            if (_bitPtt[bit] == enabled) return;

            _bitPtt[bit] = enabled;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " ptt_dependent=" + (_bitPtt[bit] ? "1" : "0"));

            RaisePropertyChanged("BitPtt");
        }

        private int[] _bitPttDelayMs = new int[8];
        public int[] BitPttDelayMs
        {
            get
            {
                return _bitPttDelayMs;
            }
        }

        public void SetBitPttDelayMs(int bit, int delay)
        {
            if (bit < 0 || bit > 7) return;
            if (_bitPttDelayMs[bit] == delay) return;

            _bitPttDelayMs[bit] = delay;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " ptt_delay=" + delay);

            RaisePropertyChanged("BitPttDelayMs");
        }

        private int[] _bitTxDelayMs = new int[8];
        public int[] BitTxDelayMs
        {
            get
            {
                return _bitTxDelayMs;
            }
        }

        public void SetBitTxDelayMs(int bit, int delay)
        {
            if (bit < 0 || bit > 7) return;
            if (_bitTxDelayMs[bit] == delay) return;

            _bitTxDelayMs[bit] = delay;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " tx_delay=" + delay);

            RaisePropertyChanged("BitTxDelayMs");
        }



        private string[] _bitOrdinalRxAnt = new string[8];
        public string[] BitOrdinalRxAnt
        {
            get { return _bitOrdinalRxAnt; }
        }

        private string[] _bitOrdinalTxAnt = new string[8];
        public string[] BitOrdinalTxAnt
        {
            get { return _bitOrdinalTxAnt; }
        }

        private string[] _bitOrdinalSlice = new string[8];
        public string[] BitOrdinalSlice
        {
            get { return _bitOrdinalSlice; }
        }

        public void SetBitSourceRxAnt(int bit, string ant)
        {
            if (bit < 0 || bit > 7) return;            
            if (_bitOrdinalRxAnt[bit] == null) return;
            if (_bitOrdinalRxAnt[bit] == ant) return;

            _bitOrdinalRxAnt[bit] = ant;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " source_rx_ant=" + _bitOrdinalRxAnt[bit]);

            RaisePropertyChanged("BitOrdinalRxAnt");
        }

        public void SetBitSourceTxAnt(int bit, string ant)
        {
            if (bit < 0 || bit > 7) return;
            if (_bitOrdinalTxAnt[bit] == null) return;
            if (_bitOrdinalTxAnt[bit] == ant) return;

            _bitOrdinalTxAnt[bit] = ant;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " source_tx_ant=" + _bitOrdinalTxAnt[bit]);

            RaisePropertyChanged("BitOrdinalTxAnt");
        }

        public void SetBitSourceSlice(int bit, string sliceIndex)
        {
            if (bit < 0 || bit > 7) return;
            if (_bitOrdinalSlice[bit] == null) return;
            if (_bitOrdinalSlice[bit] == sliceIndex) return;

            _bitOrdinalSlice[bit] = sliceIndex;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " source_slice=" + _bitOrdinalSlice[bit]);

            RaisePropertyChanged("BitOrdinalSlice");
        }

        private double[] _bitLowFreq = new double[8];
        public double[] BitLowFreq
        {
            get { return _bitLowFreq; }
        }

        private double[] _bitHighFreq = new double[8];
        public double[] BitHighFreq
        {
            get { return _bitHighFreq; }
        }

        public void SetBitFreqRange(int bit, double freqLowMHz, double freqHighMHz)
        {
            if (bit < 0 || bit > 7) return;
            if (Math.Round(_bitHighFreq[bit], 6) == Math.Round(freqHighMHz, 6) && Math.Round(_bitLowFreq[bit], 6) == Math.Round(freqLowMHz, 6)) return;

            _bitHighFreq[bit] = freqHighMHz;
            _bitLowFreq[bit] = freqLowMHz;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " low_freq=" + Math.Round(_bitLowFreq[bit], 6).ToString("0.######") + " high_freq=" + Math.Round(_bitHighFreq[bit], 6).ToString("0.######"));

            RaisePropertyChanged("BitLowFreq");
            RaisePropertyChanged("BitHighFreq");
        }

        private string[] _bitBand = new string[8];
        public string[] BitBand
        {
            get { return _bitBand; }
        }

        public void SetBitBand(int bit, string band)
        {
            if (bit < 0 || bit > 7) return;
            if (_bitBand[bit] == band) return;

            _bitBand[bit] = band;
            if (_bitBand[bit] == null) return;

            if (_radio != null)
                _radio.SendCommand("usb_cable setbit " + _serialNumber + " " + bit + " band=" + _bitBand[bit].ToLower().Replace("m", ""));

            RaisePropertyChanged("BitBand");
        }

        private string UsbBitCableOutputTypeToString(UsbBitCableOutputType output)
        {
            string ret_val = "None";

            switch(output)
            {
                case UsbBitCableOutputType.Band: ret_val = "band"; break;
                case UsbBitCableOutputType.FreqRange: ret_val = "freq_range"; break;                
            }

            return ret_val;
        }

        private UsbBitCableOutputType StringToUsbBitCableOutputType(string s)
        {
            UsbBitCableOutputType ret_val = UsbBitCableOutputType.Band;

            switch (s)
            {
                case "band": ret_val = UsbBitCableOutputType.Band; break;
                case "freq_range": ret_val = UsbBitCableOutputType.FreqRange; break;                
            }

            return ret_val;
        }

        internal override void ParseStatus(string s)
        {
            if (String.IsNullOrEmpty(s)) return;
            
            // split the status message on spaces
            string[] words = s.Split(' ');

            bool is_bit_status = (words.Length > 1 && words[0] == "bit");

            // is this a bit status?
            if (is_bit_status)
            {
                // yes -- parse the bit specific fields
                int bitNumber = -1;
                bool valid_bit = int.TryParse(words[1], out bitNumber);

                if (!valid_bit || bitNumber < 0 || bitNumber > 7)
                {
                    Debug.WriteLine("UsbBitCable::ParseStatus: Invalid bit (" + words[1] + ")");
                    return;
                }

                foreach (string kv in words)
                {
                    string[] tokens = kv.Split('=');
                    if (tokens.Length != 2)
                    {
                        Debug.WriteLine("UsbBitCable::ParseStatus: Invalid key/value pair (" + kv + ")");
                        continue;
                    }

                    string key = tokens[0];
                    string value = tokens[1];

                    ParseBitSpecificStatus(bitNumber, key, value);
                }
            }
            else
            {
                // no -- parse regular UsbCable status
                base.ParseStatus(s);
            }
        }

        private void ParseBitSpecificStatus(int bitNumber, string key, string value)
        {
            int bit = bitNumber;
            if (bit < 0 || bit > 7) return;

            switch (key)
            {
                case "enable":
                    {
                        byte temp;
                        bool b = byte.TryParse(value, out temp);

                        if (!b)
                        {
                            Debug.WriteLine("UsbBitCable::ParseStatus - enable: Invalid value (" + value + ")");
                            break;
                        }

                        _bitEnable[bit] = Convert.ToBoolean(temp);
                        RaisePropertyChanged("BitEnable");
                    }
                    break;
                case "source":
                    {
                        UsbCableFreqSource source = StringToUsbCableFreqSource(value);
                        if (source == UsbCableFreqSource.Invalid) break;

                        _bitSource[bit] = source;
                        RaisePropertyChanged("BitSource");
                    }
                    break;

                case "source_rx_ant":
                    {
                        _bitOrdinalRxAnt[bit] = value;
                        RaisePropertyChanged("BitOrdinalRxAnt");
                    }
                    break;

                case "source_tx_ant":
                    {
                        _bitOrdinalTxAnt[bit] = value;
                        RaisePropertyChanged("BitOrdinalTxAnt");
                    }
                    break;

                case "source_slice":
                    {
                        _bitOrdinalSlice[bit] = value;
                        RaisePropertyChanged("BitOrdinalSlice");
                    }
                    break;

                case "output":
                    {
                        UsbBitCableOutputType output = StringToUsbBitCableOutputType(value);
                        _bitOutput[bit] = output;
                        RaisePropertyChanged("BitOutput");
                    }
                    break;

                case "polarity":
                    {
                        bool polarity;
                        if (value == "active_high")
                            polarity = true;
                        else if (value == "active_low")
                            polarity = false;
                        else break;

                        _bitActiveHigh[bit] = polarity;
                        RaisePropertyChanged("BitActiveHigh");
                    }
                    break;

                case "ptt_dependent":
                    {
                        byte temp;
                        bool b = byte.TryParse(value, out temp);

                        if (!b)
                        {
                            Debug.WriteLine("UsbBitCable::ParseTypeSpecificStatus - ptt_dependent: Invalid value (" + temp.ToString() + ")");
                            break;
                        }

                        _bitPtt[bit] = Convert.ToBoolean(temp);
                        RaisePropertyChanged("BitPtt");
                    }
                    break;

                case "ptt_delay":
                    {
                        int temp;
                        bool b = int.TryParse(value, out temp);

                        if (!b)
                        {
                            Debug.WriteLine("UsbBitCable::ParseTypeSpecificStatus - ptt_delay: Invalid value (" + temp.ToString() + ")");
                            break;
                        }

                        _bitPttDelayMs[bit] = temp;
                        RaisePropertyChanged("BitPttDelayMs");
                    }
                    break;

                case "tx_delay":
                    {
                        int temp;
                        bool b = int.TryParse(value, out temp);

                        if (!b)
                        {
                            Debug.WriteLine("UsbBitCable::ParseTypeSpecificStatus - tx_delay: Invalid value (" + temp.ToString() + ")");
                            break;
                        }

                        _bitTxDelayMs[bit] = temp;
                        RaisePropertyChanged("BitTxDelayMs");
                    }
                    break;

                case "low_freq":
                    {
                        double temp;
                        bool b = double.TryParse(value, out temp);

                        if (!b)
                        {
                            Debug.WriteLine("UsbBitCable::ParseStatus - low_freq: Invalid value (" + temp.ToString() + ")");
                            break;
                        }

                        _bitLowFreq[bit] = temp;
                        RaisePropertyChanged("BitLowFreq");
                    }
                    break;

                case "high_freq":
                    {
                        double temp;
                        bool b = double.TryParse(value, out temp);

                        if (!b)
                        {
                            Debug.WriteLine("UsbBitCable::ParseStatus - high_freq: Invalid value (" + temp.ToString() + ")");
                            break;
                        }

                        _bitHighFreq[bit] = temp;
                        RaisePropertyChanged("BitHighFreq");
                    }
                    break;

                case "band":
                    {
                        _bitBand[bit] = value;
                        RaisePropertyChanged("BitBand");
                    }
                    break;
            }
            
        }
    }
}
