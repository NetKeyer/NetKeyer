// ****************************************************************************
///*!	\file RapidM.cs
// *	\brief Represents a RapidM modem interface
// *
// *	\copyright	Copyright 2020 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2020-11-11
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flex.UiWpfFramework.Mvvm;


namespace Flex.Smoothlake.FlexLib
{
    public record RapidMMessage(string text, bool? usingAle = null, bool? isOneTime = null, bool? isBinary = null, string? station = null);

    public record RapidMOptionStatus(int index, string name, bool activated);
    public record RapidMPerModemConfig(string version, string serial, string mac, string ip, string subnet, string gw, List<RapidMOptionStatus> options);

    public class RapidMHelper
    {
        private static readonly IDictionary<ModemConfigurationType, string> _modemGlobalConfigLabels = new Dictionary<ModemConfigurationType, string> {
            {ModemConfigurationType.Unknown, "Unknown" },
            {ModemConfigurationType.SingleModem, "Single Modem" },
            {ModemConfigurationType.DualModemCopro, "Dual Modem Co-Processing" },
            {ModemConfigurationType.DualModemIndependent, "Dual Modem Independent" },
        };
        public static ModemConfigurationType GlobalConfigFromString(string s)
        {
            // For easier parsing, radio status will replace whitespace with underscores.
            s = s.Replace("_", " ");

            // Default value of 0 is "Unknown".
            var ret = _modemGlobalConfigLabels.FirstOrDefault(x => x.Value == s).Key;
            return ret;
        }
        public static string GlobalConfigToString(ModemConfigurationType type)
        {
            return _modemGlobalConfigLabels[type];
        }

        public static Tuple<string,int> Get4GBinaryMessageStringFromBinaryFile(string path)
        {
            Tuple<string, int> ret = new (string.Empty, 0);
            if (path == string.Empty) return ret;

            // Copy over as many characters as will fit.
            try
            {
                var fileAttr = new FileInfo(path);
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length > maxBinaryFileSize) bytes = bytes.Take(maxBinaryFileSize).ToArray();
                ret = new(BitConverter.ToString(bytes).Replace("-", string.Empty), bytes.Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to access upload file '{path}', exception = {ex.Message}");
                return ret;
            }
            return ret;
        }

        private static readonly int maxBinaryFileSize = 1024;
    }

    public class RapidM : ObservableObject
    {
        private Radio _radio;

        public RapidM(Radio radio)
        {
            _radio = radio;
        }

        #region Properties

        private string _selectedPskWaveform;
        /// <summary>
        /// The RapidM PSK Waveform Mode selected in the drop down
        /// </summary>
        public string SelectedPskWaveform
        {
            get { return _selectedPskWaveform; }
            set
            {
                if (_selectedPskWaveform != value)
                {
                    _selectedPskWaveform = value;
                    //SendCommand("rapidm psk set wf=" + _waveform);
                    RaisePropertyChanged("SelectedPskWaveform");
                }
            }
        }

        private string _selectedMs110cWaveform;
        /// <summary>
        /// The RapidM MS110C Waveform Mode selected in the drop down
        /// </summary>
        public string SelectedMs110cWaveform
        {
            get { return _selectedMs110cWaveform; }
            set
            {
                if (_selectedMs110cWaveform != value)
                {
                    _selectedMs110cWaveform = value;
                    RaisePropertyChanged("SelectedMs110cWaveform");
                }
            }
        }

        private string _selectedRate;
        /// <summary>
        /// The RapidM baud rate selected in the drop down
        /// </summary>
        public string SelectedRate
        {
            get { return _selectedRate; }
            set
            {
                if (_selectedRate != value)
                {
                    _selectedRate = value;
                    //SendCommand("rapidm psk set rate=" + _rate);
                    RaisePropertyChanged("SelectedRate");
                }
            }
        }

        private string _selectedBandwidth;
        /// <summary>
        /// The RapidM bandwidth selected in the drop down
        /// </summary>
        public string SelectedBandwidth
        {
            get { return _selectedBandwidth; }
            set
            {
                if (_selectedBandwidth != value)
                {
                    _selectedBandwidth = value;
                    //SendCommand("rapidm psk set bw=" + _bandwidth);
                    RaisePropertyChanged("SelectedBandwidth");
                }
            }
        }

        private string _selectedInterleaver;
        /// <summary>
        /// The RapidM Interleaver selected in the drop down
        /// </summary>
        public string SelectedInterleaver
        {
            get { return _selectedInterleaver; }
            set
            {
                if (_selectedInterleaver != value)
                {
                    _selectedInterleaver = value;
                    //SendCommand("rapidm psk set il=" + _interleaver);
                    RaisePropertyChanged("SelectedInterleaver");
                }
            }
        }

        private bool _selectedPSKENC;
        /// <summary>
        /// The PSK ENC selected in the drop down
        /// </summary>
        public bool SelectedPSKENC
        {
            get { return _selectedPSKENC; }
            set
            {
                if (_selectedPSKENC != value)
                {
                    _selectedPSKENC = value;
                    RaisePropertyChanged("SelectedPSKENC");
                }
            }
        }

        private bool _selectedPSKISB;
        /// <summary>
        /// The PSK ISB selected in the drop down
        /// </summary>
        public bool SelectedPSKISB
        {
            get { return _selectedPSKISB; }
            set
            {
                if (_selectedPSKISB != value)
                {
                    _selectedPSKISB = value;
                    RaisePropertyChanged("SelectedPSKISB");
                }
            }
        }

        private string _selectedMS110CInterleaver;
        /// <summary>
        /// The RapidM Interleaver for MS110C in the drop down
        /// </summary>
        public string SelectedMS110CInterleaver
        {
            get { return _selectedMS110CInterleaver; }
            set
            {
                if (_selectedMS110CInterleaver != value)
                {
                    _selectedMS110CInterleaver = value;
                    RaisePropertyChanged("SelectedMS110CInterleaver");
                }
            }
        }

        private string _selectedMS110CConstraint;
        /// <summary>
        /// The RapidM Constraint for MS110C in the drop down
        /// </summary>
        public string SelectedMS110CConstraint
        {
            get { return _selectedMS110CConstraint; }
            set
            {
                if (_selectedMS110CConstraint != value)
                {
                    _selectedMS110CConstraint = value;
                    RaisePropertyChanged("SelectedMS110CConstraint");
                }
            }
        }

        private string _selectedMS110CTLC;
        /// <summary>
        /// The MS110C TLC value (0-255) selected in the UI
        /// </summary>
        public string SelectedMS110CTLC
        {
            get { return _selectedMS110CTLC; }
            set
            {
                if (_selectedMS110CTLC != value)
                {
                    _selectedMS110CTLC = value;
                    RaisePropertyChanged("SelectedMS110CTLC");
                }
            }
        }

        private string _selectedMS110CPreamble;
        /// <summary>
        /// The MS110C preamble value (0-32) selected in the UI
        /// </summary>
        public string SelectedMS110CPreamble
        {
            get { return _selectedMS110CPreamble; }
            set
            {
                if (_selectedMS110CPreamble != value)
                {
                    _selectedMS110CPreamble = value;
                    RaisePropertyChanged("SelectedMS110CPreamble");
                }
            }
        }

        private string _snr;
        /// <summary>
        /// The RapidM SNR
        /// </summary>
        public string SNR
        {
            get { return _snr; }
        }

        private string _ber;
        /// <summary>
        /// The RapidM bit error rate
        /// </summary>
        public string BER
        {
            get { return _ber; }
        }

        private string _modulation;
        /// <summary>
        /// The RapidM current waveform mode active in the radio (psk/ms110c/etc)
        /// </summary>
        public string Modulation
        {
            get { return _modulation; }
            set
            {
                if (_modulation != value)
                {
                    _modulation = value;
                    RaisePropertyChanged("Modulation");
                }
            }
        }

        private string _currentPSKWaveform;
        /// <summary>
        /// The RapidM PSK Waveform Mode that is currently active in the radio
        /// </summary>
        public string CurrentPSKWaveform
        {
            get { return _currentPSKWaveform; }
            set
            {
                if (_currentPSKWaveform != value)
                {
                    _currentPSKWaveform = value;
                    RaisePropertyChanged("CurrentPSKWaveform");
                }
            }
        }

        private string _currentPSKRate;
        /// <summary>
        /// The RapidM PSK baud rate that is currently active in the radio
        /// </summary>
        public string CurrentPSKRate
        {
            get { return _currentPSKRate; }
            set
            {
                if (_currentPSKRate != value)
                {
                    _currentPSKRate = value;
                    RaisePropertyChanged("CurrentPSKRate");
                }
            }
        }

        private string _currentPSKInterleaver;
        /// <summary>
        /// The RapidM Interleaver that is currently active in the radio
        /// </summary>
        public string CurrentPSKInterleaver
        {
            get { return _currentPSKInterleaver; }
            set
            {
                if (_currentPSKInterleaver != value)
                {
                    _currentPSKInterleaver = value;
                    RaisePropertyChanged("CurrentPSKInterleaver");
                }
            }
        }

        private string _currentMS110CWaveform;
        /// <summary>
        /// The RapidM MS110C Waveform Mode that is currently active in the radio
        /// </summary>
        public string CurrentMS110CWaveform
        {
            get { return _currentMS110CWaveform; }
            set
            {
                if (_currentMS110CWaveform != value)
                {
                    _currentMS110CWaveform = value;
                    RaisePropertyChanged("CurrentMS110CWaveform");
                }
            }
        }

        private string _currentMS110CBandwidth;
        /// <summary>
        /// The RapidM MS110c bandwidth that is curently active in the radio
        /// </summary>
        public string CurrentMS110CBandwidth
        {
            get { return _currentMS110CBandwidth; }
            set
            {
                if (_currentMS110CBandwidth != value)
                {
                    _currentMS110CBandwidth = value;
                    RaisePropertyChanged("CurrentMS110CBandwidth");
                }
            }
        }

        private string _currentPSKENC;
        /// <summary>
        /// The PSK ENC value that is curently active in the radio
        /// </summary>
        public string CurrentPSKENC
        {
            get { return _currentPSKENC; }
            set
            {
                if (_currentPSKENC != value)
                {
                    _currentPSKENC = value;
                    RaisePropertyChanged("CurrentPSKENC");
                }
            }
        }

        private string _currentPSKISB;
        /// <summary>
        /// The PSK ISB value that is curently active in the radio
        /// </summary>
        public string CurrentPSKISB
        {
            get { return _currentPSKISB; }
            set
            {
                if (_currentPSKISB != value)
                {
                    _currentPSKISB = value;
                    RaisePropertyChanged("CurrentPSKISB");
                }
            }
        }

        private string _currentMS110CInterleaver;
        /// <summary>
        /// The MS110C interleaver value that is curently active in the radio
        /// </summary>
        public string CurrentMS110CInterleaver
        {
            get { return _currentMS110CInterleaver; }
            set
            {
                if (_currentMS110CInterleaver != value)
                {
                    _currentMS110CInterleaver = value;
                    RaisePropertyChanged("CurrentMS110CInterleaver");
                }
            }
        }

        private string _currentMS110CConstraint;
        /// <summary>
        /// The MS110C Constraint value that is curently active in the radio
        /// </summary>
        public string CurrentMS110CConstraint
        {
            get { return _currentMS110CConstraint; }
            set
            {
                if (_currentMS110CConstraint != value)
                {
                    _currentMS110CConstraint = value;
                    RaisePropertyChanged("CurrentMS110CConstraint");
                }
            }
        }

        private string _currentMS110CTLC;
        /// <summary>
        /// The MS110C TLC value (0-255) that is curently active in the radio
        /// </summary>
        public string CurrentMS110CTLC
        {
            get { return _currentMS110CTLC; }
            set
            {
                if (_currentMS110CTLC != value)
                {
                    _currentMS110CTLC = value;
                    RaisePropertyChanged("CurrentMS110CTLC");
                }
            }
        }

        private string _currentMS110CPreamble;
        /// <summary>
        /// The MS110C preamble value (0-32) that is curently active in the radio
        /// </summary>
        public string CurrentMS110CPreamble
        {
            get { return _currentMS110CPreamble; }
            set
            {
                if (_currentMS110CPreamble != value)
                {
                    _currentMS110CPreamble = value;
                    RaisePropertyChanged("CurrentMS110CPreamble");
                }
            }
        }

        private string _currentMS110CDataRate;
        /// <summary>
        /// The MS110C data rate value that is curently active in the radio
        /// There is not a matching property for setting from the UI because
        /// that feature is not currently available
        /// </summary>
        public string CurrentMS110CDataRate
        {
            get { return _currentMS110CDataRate; }
            set
            {
                if (_currentMS110CDataRate != value)
                {
                    _currentMS110CDataRate = value;
                    RaisePropertyChanged("CurrentMS110CDataRate");
                }
            }
        }

        private string _softwareVersion;
        public string SoftwareVersion
        {
            get => _softwareVersion;
            set
            {
                if (value == _softwareVersion) return;
                _softwareVersion = value;
                RaisePropertyChanged(nameof(SoftwareVersion));
            }
        }

        private int _sampleRate;
        public int SampleRate
        {
            get => _sampleRate;
            set
            {
                if (value == _sampleRate) return;
                _sampleRate = value;
                RaisePropertyChanged(nameof(SampleRate));
            }
        }

        private List<bool> _modemsTcpConnected = [false, false];
        public List<bool> ModemsTcpConnected
        {
            get => _modemsTcpConnected;
            set
            {
                if (value == _modemsTcpConnected) return;
                _modemsTcpConnected = value;
                RaisePropertyChanged(nameof(ModemsTcpConnected));
            }
        }

        private string _operationalStateLabel;
        public string OperationalStateLabel
        {
            get => _operationalStateLabel;
            set
            {
                if (value == _operationalStateLabel) return;
                _operationalStateLabel = value;
                RaisePropertyChanged(nameof(OperationalStateLabel));
            }
        }

        private ModemConfigurationType _modemGlobalConfigType = ModemConfigurationType.DualModemCopro;
        public ModemConfigurationType ModemGlobalConfigType
        {
            get => _modemGlobalConfigType;
            set
            {
                if (value == _modemGlobalConfigType) return;
                _modemGlobalConfigType = value;
                RaisePropertyChanged(nameof(ModemGlobalConfigType));
            }
        }

        private RapidMPerModemConfig _modem1Id;
        public RapidMPerModemConfig Modem1Id
        {
            get => _modem1Id;
            set
            {
                if (value == _modem1Id) return;
                _modem1Id = value;
                RaisePropertyChanged(nameof(Modem1Id));
            }
        }

        private RapidMPerModemConfig _modem2Id;
        public RapidMPerModemConfig Modem2Id
        {
            get => _modem2Id;
            set
            {
                if (value == _modem2Id) return;
                _modem2Id = value;
                RaisePropertyChanged(nameof(Modem2Id));
            }
        }

        private bool _linked;
        public bool Linked
        {
            get => _linked;
            set
            {
                if (value == _linked) return;
                _linked = value;
                RaisePropertyChanged(nameof(Linked));
            }
        }

        #endregion


        /// <summary>
        /// The delegate event handler for the RapidmMessageReceived event
        /// </summary>
        /// <param name="message">The string that was received</param>
        public delegate void MessageReceivedEventHandler(string message);
        /// <summary>
        /// This event is raised when a RapidM message is received
        /// </summary>
        public event MessageReceivedEventHandler MessageReceived;

        private void OnMessageReceived(string message)
        {
            if (MessageReceived != null)
                MessageReceived(message);
        }

        /// <summary>
        /// Sets up the RapidM waveform, rate and interleaver
        /// </summary>
        public void ConfigurePsk()
        {
            //enc and isb are usually disabled.
            //might come back to adjust this command to control what gets sent to the radio
            _radio.SendCommand("rapidm psk set wf=" + _selectedPskWaveform + " rate=" + _selectedRate + " il=" + _selectedInterleaver
                                + " enc=" + _selectedPSKENC + " isb=" + _selectedPSKISB);
        }

        public void ConfigureMs110c()
        {
            //tlc and preamble may need to: have fixed value/be set to radio defaults/not be sent to radio.
            //will revisit when decision is made
            _radio.SendCommand("rapidm ms110c set wf=" + _selectedMs110cWaveform + " bw=" + _selectedBandwidth + " il=" + _selectedMS110CInterleaver
                                + " tlc=" + _selectedMS110CTLC + " preamble=" + _selectedMS110CPreamble + " constraint=" + _selectedMS110CConstraint);
        }

        public async Task Zeroize()
        {
            // Use rapidm object to allow zeroizing radio as it mainly touches ALE configs, but it is more properly at a
            // global radio scale.
            await _radio.SendCommandAsync("radio zeroize");
        }

        public async Task SendConfigCommand(string configCommand)
        {
            // Allow sending raw commands from view model which are set by config files that are loaded by the user.
            await _radio.SendCommandAsync(configCommand);
        }

        public void SendMessage(RapidMMessage msg)
        {
            // Default to base waveform message using PSK or MS110, if ALE parameters are not set.
            if (!msg.usingAle ?? false)
            {
                _radio.SendCommand($"rapidm tx_message \"{msg.text.Replace(' ', '\u007f')}\"");
                return;
            }

            // If using ALE, all parameters must be provided to identify the right messaging settings.
            if (!msg.isBinary.HasValue || !msg.isOneTime.HasValue)
            {
                Debug.WriteLine("Cannot send ALE message without binary and/or one-time flags set.");
                return;
            }
            if (string.IsNullOrEmpty(msg.station))
            {
                Debug.WriteLine("Can't send ALE message where no station has been identified.");
                return;
            }

            // Avoid redundant string ops by pre-allocating all the space we need
            StringBuilder command = new StringBuilder("ale ", msg.text.Length + 32);
            if (msg.isOneTime.Value)
            {
                if (Linked)
                {
                    Debug.WriteLine("Can't make one-time ALE link to send message when already linked.");
                    return;
                }
                if (msg.isBinary.HasValue && true == msg.isBinary.HasValue)
                {
                    Debug.WriteLine("Can't make one-time ALE link when sending binary message.");
                    return;
                }
                command.Append("link ");
            }
            else
            {
                command.Append(msg.isBinary.Value ? "binary " : "msg ");
            }

            command.Append($"station={msg.station} \"text={msg.text}\"");
            Debug.WriteLine($"Sending RM message with '{command}'");
            _radio.SendCommand(command.ToString());
        }

        internal List<RapidMOptionStatus> ParseOptions(string s)
        {
            List<RapidMOptionStatus> ret = [];
            // Split each option grouping by tab char. Each grouping is index,name,enable separated by commas.
            s = s.Replace("\"", string.Empty);
            string[] options = s.Split('\t');
            foreach (string option in options)
            {
                if (option.Length == 0) continue;
                string[] optionFields = option.Split(',');
                RapidMOptionStatus newOption = new(int.Parse(optionFields[0]), optionFields[1], int.Parse(optionFields[2]) == 1);
                ret.Add(newOption);
            }
            return ret;
        }

        internal void ParseIdStatus(string s)
        {
            // The status should indicate if it is referring to the primary or secondary modem in this system.
            bool isPrimary = true;
            List<RapidMOptionStatus> tempOptions = [];
            const string unknownFieldStr = "Unknown";
            string version = unknownFieldStr;
            string serial = unknownFieldStr;
            string mac = unknownFieldStr;
            string ip = unknownFieldStr;
            string subnet = unknownFieldStr;
            string gw = unknownFieldStr;

            string[] words = s.Split(' ');
            foreach (string kv in words)
            {
                if (kv.Length == 0) continue;
                string[] tokens = kv.Split('=');
                string key = tokens[0];
                string value = tokens.Length > 1 ? tokens[1] : null;
                switch (key.ToLower())
                {
                    case ("primary"):
                        {
                            isPrimary = true;
                            break;
                        }
                    case ("secondary"):
                        {
                            isPrimary = false;
                            break;
                        }
                    case ("version"):
                        {
                            version = value;
                            break;
                        }
                    case ("serial"):
                        {
                            serial = value;
                            break;
                        }
                    case ("mac"):
                        {
                            mac = value;
                            break;
                        }
                    case ("ip"):
                        {
                            ip = value;
                            break;
                        }
                    case ("subnet"):
                        {
                            subnet = value;
                            break;
                        }
                    case ("gateway"):
                        {
                            gw = value;
                            break;
                        }
                    case ("options"):
                        {
                            Debug.WriteLine($"Parsing options: {kv.Substring("options=".Length)}");
                            tempOptions = ParseOptions(kv.Substring("options=".Length));
                            break;
                        }
                    default:
                        {
                            Debug.Write($"Unknown rapidm id field '{kv}'");
                            break;
                        }
                }
            }

            if (isPrimary)
            {
                Modem1Id = new(version, serial, mac, ip, subnet, gw, tempOptions);
            }
            else
            {
                Modem2Id = new(version, serial, mac, ip, subnet, gw, tempOptions);
            }
        }

        internal void ParseStatus(string s)
        {
            string[] words = s.Split(' ');

            // TODO - get dual/single status from a rapidm message. Add to API if not currently supplied.

            if (words.Length < 2)
            {
                Debug.WriteLine("RapidM::ParseStatus: Too few words -- min 2 (" + words + ")");
                return;
            }

            // handle non key/value pair type statuses
            if (words[0] == "rx_message")
            {
                string encoded_message = s.Substring("rx_message ".Length); // strip off the rx_message
                string message = encoded_message.Replace('\u007f', ' '); // decode the spaces
                OnMessageReceived(message); // fire the event
                return;
            }

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    if (tokens[0] == "id")
                    {
                        ParseIdStatus(s.Substring("id ".Length)); // "rapidm id ... "
                    }
                    else
                    {
                        Debug.WriteLine("RapidM::ParseStatus: Invalid key/value pair (" + kv + ")");
                    }
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "wf":
                        {
                            //both psk and ms110c have waveforms
                            if (words[0] == "psk")
                            {
                                if (_currentPSKWaveform == value) continue;

                                _currentPSKWaveform = value;
                                RaisePropertyChanged("CurrentPSKWaveform");
                            }
                            if (words[0] == "ms110c")
                            {
                                if (_currentMS110CWaveform == value) continue;

                                _currentMS110CWaveform = value;
                                RaisePropertyChanged("CurrentMS110CWaveform");
                            }
                        }
                        break;
                    case "rate":
                        {
                            if (_currentPSKRate == value) continue;

                            _currentPSKRate = value;
                            RaisePropertyChanged("CurrentPSKRate");
                        }
                        break;

                    case "bw":
                        {
                            if (_currentMS110CBandwidth == value) continue;

                            _currentMS110CBandwidth = value;
                            RaisePropertyChanged("CurrentMS110CBandwidth");
                        }
                        break;

                    case "il": // interleaver
                        {
                            //both psk and ms110c have interleavers
                            if (words[0] == "psk")
                            {
                                if (_currentPSKInterleaver == value) continue;

                                _currentPSKInterleaver = value;
                                RaisePropertyChanged("CurrentPSKInterleaver");
                            }
                            if (words[0] == "ms110c")
                            {
                                if (_currentMS110CInterleaver == value) continue;

                                _currentMS110CInterleaver = value;
                                RaisePropertyChanged("CurrentMS110CInterleaver");
                            }
                        }
                        break;

                    case "constraint": // ms110c only
                        {
                            if (_currentMS110CConstraint == value) continue;

                            _currentMS110CConstraint = value;
                            RaisePropertyChanged("CurrentMS110CConstraint");
                        }
                        break;
                        
                    case "snr": // signal to noise ratio
                        {
                            if (_snr == value) continue;

                            _snr = value;
                            RaisePropertyChanged("SNR");
                        }
                        break;

                    case "ber": // bit error rate
                        {
                            if (_ber == value) continue;

                            _ber = value;
                            RaisePropertyChanged("BER");
                        }
                        break;

                    case "modulation":
                        {
                            if (_modulation == value) continue;

                            _modulation = value.ToLower();
                            RaisePropertyChanged("Modulation");
                        }
                        break;

                    case "enc":
                        {
                            if (_currentPSKENC == value) continue;

                            _currentPSKENC = value;
                            RaisePropertyChanged("CurrentPSKENC");
                        }
                        break;

                    case "isb":
                        {
                            if (_currentPSKISB == value) continue;

                            _currentPSKISB = value;
                            RaisePropertyChanged("CurrentPSKISB");
                        }
                        break;

                    case "tlc":
                        {
                            if (_currentMS110CTLC == value) continue;

                            _currentMS110CTLC = value;
                            RaisePropertyChanged("CurrentMS110CTLC");
                        }
                        break;

                    case "preamble":
                        {
                            if (_currentMS110CPreamble == value) continue;

                            _currentMS110CPreamble = value;
                            RaisePropertyChanged("CurrentMS110CPreamble");
                        }
                        break;

                    case "110c_data_rate":
                        {
                            if (_currentMS110CDataRate == value) continue;

                            _currentMS110CDataRate = value;
                            RaisePropertyChanged("CurrentMS110CDataRate");
                        }
                        break;
                    case "rapidm_version":
                        {
                            SoftwareVersion = value;
                            break;
                        }
                    case "rapidm_system_sample_rate":
                        {
                            SampleRate = int.Parse(value);
                            break;
                        }
                    case "is_tcp_pri_connected":
                        {
                            ModemsTcpConnected[0] = bool.Parse(value);
                            break;
                        }
                    case "is_tcp_sec_connected":
                        {
                            ModemsTcpConnected[1] = bool.Parse(value);
                            break;
                        }
                    case "operational_state":
                        {
                            OperationalStateLabel = value;
                            break;
                        }
                    case "global_config":
                        {
                            ModemGlobalConfigType = RapidMHelper.GlobalConfigFromString(value);
                            break;
                        }
                }
            }
        }
    }
}
