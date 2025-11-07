// ****************************************************************************
///*!	\file TxBandSettings.cs
// *	\brief Represents the settings for a single band
// *
// *	\copyright	Copyright 2012-2019 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2019-01-17
// *	\author Abed Haque, AB5ED
// */
// ****************************************************************************
using Flex.UiWpfFramework.Mvvm;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Flex.Smoothlake.FlexLib
{
    public class TxBandSettings : ObservableObject
    {
        private Radio _radio;
        public TxBandSettings(Radio radio, int band_id)
        {
            _radio = radio;
            BandId = band_id;
        }

        public int BandId { get; private set; }

        private string _bandName;
        public string BandName
        {
            get { return _bandName; }
            private set
            {
                if (_bandName == value) return;

                _bandName = value;
                RaisePropertyChanged(() => BandName);
            }
        }


        private bool _isHwAlcEnabled;
        public bool IsHwAlcEnabled
        {
            get { return _isHwAlcEnabled; }
            set
            {
                if (_isHwAlcEnabled == value) return;

                _isHwAlcEnabled = value;
                _radio.SendCommand($"transmit bandset {BandId} hwalc_enabled={Convert.ToByte(_isHwAlcEnabled)}");
                RaisePropertyChanged(() => IsHwAlcEnabled);
            }
        }

        private int _tuneLevel;
        public int TuneLevel
        {
            get { return _tuneLevel; }
            set
            {
                if (_tuneLevel == value) return;

                _tuneLevel = value;
                _radio.SendCommand($"transmit bandset {BandId} tunepower={_tuneLevel}");
                RaisePropertyChanged(() => TuneLevel);
            }
        }

        private int _powerLevel;
        public int PowerLevel
        {
            get { return _powerLevel; }
            set
            {
                if (_powerLevel == value) return;

                _powerLevel = value;
                _radio.SendCommand($"transmit bandset {BandId} rfpower={_powerLevel}");
                RaisePropertyChanged(() => PowerLevel);
            }
        }

        private bool _isPttInhibit;
        public bool IsPttInhibit
        {
            get { return _isPttInhibit; }
            set
            {
                if (_isPttInhibit == value) return;

                _isPttInhibit = value;
                _radio.SendCommand($"transmit bandset {BandId} inhibit={Convert.ToByte(_isPttInhibit)}");
                RaisePropertyChanged(() => IsPttInhibit);
            }
        }

        private bool _isAccTxReqEnabled;
        public bool IsAccTxReqEnabled
        {
            get { return _isAccTxReqEnabled; }
            set
            {
                if (_isAccTxReqEnabled == value) return;

                _isAccTxReqEnabled = value;
                _radio.SendCommand($"interlock bandset {BandId} acc_txreq_enable={Convert.ToByte(_isAccTxReqEnabled)}");
                RaisePropertyChanged(() => IsAccTxReqEnabled);
            }
        }

        private bool _isRcaTxReqEnabled;
        public bool IsRcaTxReqEnabled
        {
            get { return _isRcaTxReqEnabled; }
            set
            {
                if (_isRcaTxReqEnabled == value) return;

                _isRcaTxReqEnabled = value;
                _radio.SendCommand($"interlock bandset {BandId} rca_txreq_enable={Convert.ToByte(_isRcaTxReqEnabled)}");
                RaisePropertyChanged(() => IsRcaTxReqEnabled);
            }
        }

        private bool _isAccTxEnabled;
        public bool IsAccTxEnabled
        {
            get { return _isAccTxEnabled; }
            set
            {
                if (_isAccTxEnabled == value) return;

                _isAccTxEnabled = value;
                _radio.SendCommand($"interlock bandset {BandId} acc_tx_enabled={Convert.ToByte(_isAccTxEnabled)}");
                RaisePropertyChanged(() => IsAccTxEnabled);
            }
        }

        private bool _isRcaTx1Enabled;
        public bool IsRcaTx1Enabled
        {
            get { return _isRcaTx1Enabled; }
            set
            {
                if (_isRcaTx1Enabled == value) return;

                _isRcaTx1Enabled = value;
                _radio.SendCommand($"interlock bandset {BandId} tx1_enabled={Convert.ToByte(_isRcaTx1Enabled)}");
                RaisePropertyChanged(() => IsRcaTx1Enabled);
            }
        }

        private bool _isRcaTx2Enabled;
        public bool IsRcaTx2Enabled
        {
            get { return _isRcaTx2Enabled; }
            set
            {
                if (_isRcaTx2Enabled == value) return;

                _isRcaTx2Enabled = value;
                _radio.SendCommand($"interlock bandset {BandId} tx2_enabled={Convert.ToByte(_isRcaTx2Enabled)}");
                RaisePropertyChanged(() => IsRcaTx2Enabled);
            }
        }

        private bool _isRcaTx3Enabled;       
        public bool IsRcaTx3Enabled
        {
            get { return _isRcaTx3Enabled; }
            set
            {
                if (_isRcaTx3Enabled == value) return;

                _isRcaTx3Enabled = value;
                _radio.SendCommand($"interlock bandset {BandId} tx3_enabled={Convert.ToByte(_isRcaTx3Enabled)}");
                RaisePropertyChanged(() => IsRcaTx3Enabled);
            }
        }

        public void ParseStatusKeyValuePairs(List<string> keyValuePairs)
        {

            foreach (string kv in keyValuePairs)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Radio::ParseTransmitStatus: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key)
                {
                    case "band_name":
                        {
                            _bandName = value;
                            RaisePropertyChanged(() => BandName);
                            break;
                        }
                    case "hwalc_enabled":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - hwalc_enabled: Invalid value (" + kv + ")");
                                continue;
                            }

                            _isHwAlcEnabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged(() => IsHwAlcEnabled);
                            break;
                        }
                    case "tunepower":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - tunepower: Invalid value (" + kv + ")");
                                continue;
                            }

                            // check limits
                            if (temp < 0) temp = 0;
                            if (temp > 100) temp = 100;

                            _tuneLevel = temp;
                            RaisePropertyChanged(() => TuneLevel);
                            break;
                        };
                    case "rfpower":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - rfpower: Invalid value (" + kv + ")");
                                continue;
                            }

                            // check limits
                            if (temp < 0) temp = 0;
                            if (temp > 100) temp = 100;

                            _powerLevel = temp;
                            RaisePropertyChanged(() => PowerLevel);
                            break;
                        }
                    case "inhibit":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - inhibit: Invalid value (" + kv + ")");
                                continue;
                            }

                            _isPttInhibit = Convert.ToBoolean(temp);
                            RaisePropertyChanged(() => IsPttInhibit);
                            break;
                        }
                    case "acc_txreq_enable":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus - acc_txreq_enable: Invalid value (" + value + ")");
                                continue;
                            }

                            _isAccTxReqEnabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged(() => IsAccTxReqEnabled);
                        }
                        break;
                    case "rca_txreq_enable":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus - rca_txreq_enable: Invalid value (" + value + ")");
                                continue;
                            }

                            _isRcaTxReqEnabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged(() => IsRcaTxReqEnabled);
                        }
                        break;
                    case "acc_tx_enabled":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("ParseInterlockStatus - acc_tx_enabled: Invalid value (" + value + ")");
                                continue;
                            }

                            _isAccTxEnabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged(() => IsAccTxEnabled);
                        }
                        break;

                    case "tx1_enabled":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("ParseInterlockStatus - tx1_enabled: Invalid value (" + value + ")");
                                continue;
                            }

                            _isRcaTx1Enabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged(() => IsRcaTx1Enabled);
                        }
                        break;

                    case "tx2_enabled":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("ParseInterlockStatus - tx2_enabled: Invalid value (" + value + ")");
                                continue;
                            }

                            _isRcaTx2Enabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged(() => IsRcaTx2Enabled);
                        }
                        break;

                    case "tx3_enabled":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("ParseInterlockStatus - tx3_enabled: Invalid value (" + value + ")");
                                continue;
                            }

                            _isRcaTx3Enabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged(() => IsRcaTx3Enabled);
                        }
                        break;
                }
            }
        }
    }
}
