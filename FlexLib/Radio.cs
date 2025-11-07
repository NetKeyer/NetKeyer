// ****************************************************************************
///*!	\file Radio.cs
// *	\brief Represents a single radio
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2012-03-05
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

//#define TIMING

using System;
using System.Collections;   // for Hashtable class
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Collections.ObjectModel; // for ObservableCollection
using System.Diagnostics;
using System.Globalization; // for NumberStyles.HexNumber
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;     // for AutoResetEvent
// using System.Windows;       // for Size - REMOVED FOR CROSS-PLATFORM
using Flex.Smoothlake.Vita;
using Flex.UiWpfFramework.Mvvm;
using Flex.Util;
using Ionic.Zip;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Timers;
// using System.Windows.Automation.Peers; - REMOVED FOR CROSS-PLATFORM
// using Flex.UiWpfFramework.Utils; - REMOVED FOR CROSS-PLATFORM
using System.Reflection;
// using System.Windows.Documents; - REMOVED FOR CROSS-PLATFORM

namespace Flex.Smoothlake.FlexLib
{
    #region Enums

    public enum MessageResponse
    {
        Success = 0,
    }

    public enum MessageSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Fatal = 3
    }

    /// <summary>
    /// The interlock state of transmitter
    /// </summary>
    public enum InterlockState
    {
        None,
        Receive,
        Ready,
        NotReady,
        PTTRequested,
        Transmitting,
        TXFault,
        Timeout,
        StuckInput,
        UnkeyRequested
    }

    /// <summary>
    /// The push-to-talk input source
    /// </summary>
    public enum PTTSource
    {
        None,
        SW, // SmartSDR, CAT, etc
        Mic,
        ACC,
        RCA,
        TUNE,
    }

    /// <summary>
    /// The reason that the InterlockState is
    /// in the state that it is in
    /// </summary>
    public enum InterlockReason
    {
        None,
        RCA_TXREQ,
        ACC_TXREQ,
        BAD_MODE,
        TUNED_TOO_FAR,
        OUT_OF_BAND,
        PA_RANGE,
        CLIENT_TX_INHIBIT,
        XVTR_RX_ONLY,
        NO_TX_ASSIGNED,
        TGXL,
    }

    /// <summary>
    /// Display options for the front panel of the radio
    /// </summary>
    public enum ScreensaverMode
    {
        None,
        Model,
        Name,
        Callsign
    }

    public enum SourceInput
    {
        None = -1,
        SignalGenerator = 0,
        Microphone,
        Balanced,
        LineIn,
        ACC,
        DAX
    }

    /// <summary>
    /// The state of the automatic antenna tuning unit (ATU)
    /// </summary>
    public enum ATUTuneStatus
    {
        None = -1,
        NotStarted = 0,
        InProgress,
        Bypass,
        Successful,
        OK,
        FailBypass,
        Fail,
        Aborted,
        ManualBypass,
        TGXL_InProgress,
        TGXL_OK,
        TGXL_Aborted
    }

    public enum NetworkQuality
    {
        OFF,
        EXCELLENT,
        VERYGOOD,
        GOOD,
        FAIR,
        POOR
    }

    // [TypeConverter(typeof(EnumDescriptionTypeConverter))] - REMOVED FOR CROSS-PLATFORM
    public enum Oscillator
    {
        [Description("Auto")]
        auto,
        [Description("External 10 MHz")]
        external,
        [Description("GPSDO")]
        gpsdo,
        [Description("TCXO")]
        tcxo
    }    

    #endregion
    
    public class SmartSdrCommandErrorException : Exception
    {
        public uint responseValue;

        public SmartSdrCommandErrorException()
        {
        }

        public SmartSdrCommandErrorException(string message) : base(message)
        {
        }

        public SmartSdrCommandErrorException(string message, Exception inner) : base (message, inner)
        {
        }

        public SmartSdrCommandErrorException(string message, uint responseValue) : this(message)
        {
            this.responseValue = responseValue;
        }
    }

    public delegate void ReplyHandler(int seq, uint resp_val, string s);
    public class Radio : ObservableObject
    {
        #region Variables

        private Hashtable _replyTable;
        private const int NETWORK_LAN_PING_FAIR_THRESHOLD_MS = 50;
        private const int NETWORK_LAN_PING_POOR_THRESHOLD_MS = 100;
        private const int NETWORK_SMARTLINK_PING_FAIR_THRESHOLD_MS = 100;
        private const int NETWORK_SMARTLINK_PING_POOR_THRESHOLD_MS = 500;
        private const int LAST_PACKET_COUNT_UNINITIALIZED = -1;
        private const int UDP_HEADER_SIZE = 52;
        private const int TCP_HEADER_SIZE = 64;

        private Thread _meterProcessThread = null;
        private Thread _fftProcessThread = null;

        private System.Timers.Timer _statisticsTimer = new System.Timers.Timer(1000);

        private ConcurrentQueue<VitaMeterPacket> meterQueue = new ConcurrentQueue<VitaMeterPacket>();
        private AutoResetEvent _semNewMeterPacket = new AutoResetEvent(false);
        private AutoResetEvent _semNewFFTPacket = new AutoResetEvent(false);
        //private AutoResetEvent _semNewReadBuffer = new AutoResetEvent(false);

        private NetCWStream _netCWStream;
        public NetCWStream netCWStream
        {
            get { return _netCWStream; }
        }

        private List<Slice> _slices;
        /// <summary>
        /// A List of Slices present in this Radio instance
        /// </summary>
        public List<Slice> SliceList
        {
            get
            {
                lock (_slices)
                    return _slices;
            }
        }

        private List<Panadapter> _panadapters;
        /// <summary>
        /// A List of Panadapters present in this Radio instance
        /// </summary>
        public List<Panadapter> PanadapterList
        {
            get
            {
                lock (_panadapters)
                    return _panadapters;
            }
        }

        private List<Memory> _memoryList;
        public List<Memory> MemoryList
        {
            get
            {
                lock (_memoryList)
                    return _memoryList;
            }
        }

        private List<Waterfall> _waterfalls;
        private List<Meter> _meters;
        private List<Equalizer> _equalizers;
        private List<DAXRXAudioStream> _daxRXAudioStream;
        private List<DAXTXAudioStream> _daxTXAudioStreams;
        private List<DAXMICAudioStream> _daxMicAudioStreams;
        private List<TXRemoteAudioStream> _txRemoteAudioStream;
        private List<RXRemoteAudioStream> _rxRemoteAudioStreams;
        private List<DAXIQStream> _daxIQStreams;
        public List<DAXIQStream> DAXIQStreamList
        {
            get
            {
                lock (_daxIQStreams)
                    return _daxIQStreams;
            }
        }

        private List<TNF> _tnfs;
        public List<TNF> TNFList
        {
            get
            {
                lock (_tnfs)
                    return _tnfs;
            }
        }
        private List<Spot> _spots;
        public ImmutableList<Spot> SpotsList
        {
            get
            {
                lock (_spots)
                    return _spots.ToImmutableList();
            }
        }

        private List<TxBandSettings> _txBandSettingsList = new List<TxBandSettings>();

        private List<Xvtr> _xvtrs;

        public readonly object GuiClientsLockObj = new object();

        private List<GUIClient> _guiClients;
        public List<GUIClient> GuiClients
        {
            get
            {
                return _guiClients;
            }
            internal set
            {
                _guiClients = value;
                RaisePropertyChanged(() => GuiClients);
            }
        }

        private CWX _cwx;

        private List<UsbCable> _usbCables;
        public List<UsbCable> UsbCables
        {
            get
            {
                lock (_usbCables)
                    return _usbCables;
            }
        }

        private ICommandCommunication _commandCommunication;
        private List<Amplifier> _amplifiers;
        /// <summary>
        /// A List of Amplifiers present in this Radio instance
        /// </summary>
        public List<Amplifier> AmplifierList
        {
            get
            {
                lock (_amplifiers)
                    return _amplifiers;
            }
        }

        private List<Tuner> _tuners;
        /// <summary>
        /// A list of Tuners present in this Radio instance
        /// </summary>
        public List<Tuner> TunerList
        {
            get
            {
                lock (_tuners)
                    return _tuners;
            }
        }

        private RapidM _rapidM;
        public RapidM RapidM
        {
            get { return _rapidM; }
        }

        private ALE2G _ale2G;
        public ALE2G ALE2G
        {
            get { return _ale2G; }
        }

        private ALE3G _ale3G;
        public ALE3G ALE3G
        {
            get { return _ale3G; }
        }

        private ALE4G _ale4G;
        public ALE4G ALE4G
        {
            get { return _ale4G; }
        }

        private ALEComposite _aleComposite;
        public ALEComposite ALEComposite
        {
            get { return _aleComposite; }
        }

        private APD _apd;
        public APD APD
        {
            get => _apd;
        }

        private string[] _logLevels;
        public string[] LogLevels
        {
            get { return _logLevels; }
        }

        private List<LogModule> _logModules;
        /// <summary>
        /// A List of log modules available in this Radio instance
        /// </summary>
        public List<LogModule> LogModules
        {
            get
            {
                lock (_logModules)
                    return _logModules;
            }
            set
            {
                if (_logModules != value)
                {
                    _logModules = value;
                    RaisePropertyChanged("LogModules");
                }
            }
        }

#if TIMING
        private Hashtable cmd_time_table;

        private class CmdTime
        {
            public uint Sequence { get; set; }
            public string Command { get; set; }
            public string Reply { get; set; }
            public double Start { get; set; }
            public double Stop { get; set; }

            public double RoundTrip()
            {
                return Stop - Start;
            }

            public override string ToString()
            {
                return Sequence + ": " + Command + " | " + Reply + " | " + RoundTrip().ToString("f8");
            }
        }
#endif

        private Stopwatch t1;

        #endregion

        #region Properties
        private UInt64 _min_protocol_version = 0x0001000000000000;    //used to be 0x01000000 but changed to UInt64 version format
        private UInt64 _max_protocol_version = 0x0001040000000000;
        private UInt64 _protocol_version;
        /// <summary>
        /// The Protocol Version is a 64 bit number with the format
        /// "maj.min.v_a.v_b" where maj, min, and v_a are each 1 byte and
        /// v_b is 4 bytes. The most significant byte is not used and 
        /// must always be 0x00.
        /// Example: v1.1.0.4 would be 0x0001010000000004 (0x00 01 01 00 00000004)
        /// </summary>
        public UInt64 ProtocolVersion
        {
            get { return _protocol_version; }
        }

        private UInt64 _req_version = FirmwareRequiredVersion.RequiredVersion; //0x00000F0100000073;   //0.15.1.115
        public UInt64 ReqVersion
        {
            get { return _req_version; }
        }

        private string _branch_name = FirmwareRequiredVersion.BranchName;
        public string BranchName
        {
            get { return _branch_name; }
        }

        private ulong _version;
        public ulong Version
        {
            get => _version;
            set
            {
                if (_version == value)
                    return;

                // NOTE: this gets called when a Discovery packet is found and the resulting Radio object is created
                _version = value;
                RaisePropertyChanged(nameof(Version));

                // figure out whether the system has the smoothlake_dev file in the right place
                bool dev_file_exists = false;
                // make sure that an access exception when trying to get information about this file doesn't take down
                // the whole application
                try
                {
                    string dev_file = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) +
                                      "\\FlexRadio Systems\\smoothlake_dev";
                    dev_file_exists = File.Exists(dev_file);
                }
                catch (Exception)
                {
                    // do nothing, just assume the file doesn't exist
                }

                // was the file there?
                if (dev_file_exists)
                {
                    // yes, assume the developer knows what they are doing and will ensure
                    // the right version of firmware is running on the radio
                    _updateRequired = false;
                }
                else
                {
                    // no, does the firmware version match exactly what we are looking for?
                    if (_version != _req_version)
                    {
                        // no -- prompt to run the update process
                        _updateRequired = true;
                    }
                    else
                    {
                        // yes -- we're good to connect
                        _updateRequired = false;
                    }
                }

                // avoid unnecessary updates during Discovery where setting the Version 
                // before the Status can cause confusing/annoying debug output
                if (_status != null)
                    UpdateConnectedState();
            }
        }

        private void UpdateConnectedState()
        {
            // first, check to see if we are in the middle of an update.  Don't go showing it as inuse and changing the display around if so.
            if (_status == "Updating" || _updating || (_updateRequired && _connected))
            {
                ConnectedState = "Updating";
            }
            else if (_status == "Recovery" || _updateRequired)
            {
                ConnectedState = "Update";
            }
            // if we aren't in the middle of an update, show whether the unit is in use (this will not allow connection in SmartSDR)
            else if (_status == "In_Use")
            {
                ConnectedState = "In Use";
            }
            else if (_status == "Available")
            {
                ConnectedState = "Available";
            }
            //  If we get here, bad things have happened.  Leave us in Update so that the user can do something about it.
            //  The Windows Folks(tm)  also should figure out why we're here.
            else
            {
                Debug.WriteLine("Encountered an unknown radio status '" + _status + "'.  Setting connected state to 'Update'");
                ConnectedState = "Update";
            }
        }

        private bool _updateRequired = false;

        private ulong _discoveryProtocolVersion;
        public ulong DiscoveryProtocolVersion
        {
            get => _discoveryProtocolVersion;
            set
            {
                if (_discoveryProtocolVersion == value)
                    return;
                
                _discoveryProtocolVersion = value;
                RaisePropertyChanged(nameof(DiscoveryProtocolVersion));
            }
        }

        private string _versions;
        public string Versions
        {
            get { return _versions; }
        }

        private uint _clientHandle;
        public uint ClientHandle
        {
            get { return _clientHandle; }
        }

        private uint _txClientHandle;
        /// <summary>
        /// The ClientHandle of the client that is transmitting. This value
        /// is set to 0 when there are no transmitting clients.
        /// </summary>
        public uint TXClientHandle
        {
            get { return _txClientHandle; }
            private set
            {
                if (_txClientHandle == value)
                {
                    return;
                }

                _txClientHandle = value;
                RaisePropertyChanged(() => TXClientHandle);
            }
        }

        private string _guiClientID;
        public string GUIClientID
        {
            get { return _guiClientID; }
        }

        private string _model;
        /// <summary>
        /// The model name of the radio, i.e. "FLEX-6500" or "FLEX-6700"
        /// </summary>
        public string Model
        {
            get { return _model; }
            internal set
            {
                if (_model != value)
                {
                    _model = value;
                    RaisePropertyChanged("Model");
                }
            }
        }
        
        public bool IsBigBend => ModelInfo.GetModelInfoForModel(Model).Platform == RadioPlatform.BigBend;

        private string _serial;
        /// <summary>
        /// The serial number of the radio, including dashes
        /// </summary>
        public string Serial
        {
            get => _serial;
            internal set
            {
                if (_serial == value)
                    return;

                _serial = value;
                RaisePropertyChanged(nameof(Serial));
            }
        }

        #region WAN
        public bool IsWan { get; internal set; }

        private string _wanConnectionHandle;
        public string WANConnectionHandle
        {
            get { return _wanConnectionHandle; }
            set
            {
                if (_wanConnectionHandle != value)
                    _wanConnectionHandle = value;
            }
        }

        private int _publicTlsPort;
        public int PublicTlsPort
        {
            get { return _publicTlsPort; }
            set
            {
                _publicTlsPort = value;
                RaisePropertyChanged("PublicTlsPort");
            }
        }

        private int _publicUdpPort;
        public int PublicUdpPort
        {
            get { return _publicUdpPort; }
            set
            {
                _publicUdpPort = value;
                RaisePropertyChanged("PublicUdpPort");
            }
        }

        private bool _isPortForwardOn;
        public bool IsPortForwardOn
        {
            get { return _isPortForwardOn; }
            set
            {
                _isPortForwardOn = value;
                RaisePropertyChanged("IsPortForwardOn");
            }
        }

        private bool _requiresHolePunch;
        public bool RequiresHolePunch
        {
            get { return _requiresHolePunch; }
            set
            {
                _requiresHolePunch = value;
                RaisePropertyChanged("RequiresHolePunch");
            }
        }

        private int _negotiatedHolePunchPort;
        public int NegotiatedHolePunchPort
        {
            get { return _negotiatedHolePunchPort; }
            set
            {
                _negotiatedHolePunchPort = value;
                RaisePropertyChanged("NegotiatedHolePunchPort");
            }
        }

        private bool _lowBandwidthConnect = false;
        public bool LowBandwidthConnect
        {
            get { return _lowBandwidthConnect; }
            set
            {
                _lowBandwidthConnect = value;
                RaisePropertyChanged("LowBandwidthConnect");
            }
        }

        private string _boundClientID;
        public string BoundClientID
        {
            get { return _boundClientID; }
            set
            {
                if (_boundClientID != value)
                {
                    _boundClientID = value;
                    BindGUIClient(_boundClientID);
                    RaisePropertyChanged("BoundClientID");
                }
            }
        }

        public enum WanRadioRegistrationState
        {
            Undefined,
            WaitingOnSmartLinkConnection,
            WaitingForPTT,
            WaitingOnServerConfirmation,
            RegisterSuccess,
            UnregisterSuccess,
            FailedPTT,
            FailedServerConnection,
            FailedServerConfirmation,
            FailedNotLicensed,
            FailedUnknown
        }

        public WanRadioRegistrationState stringToWanRadioRegistrationState(string state_str)
        {
            switch (state_str)
            {
                case "undefined": return WanRadioRegistrationState.Undefined;
                case "wait_on_connection": return WanRadioRegistrationState.WaitingOnSmartLinkConnection;
                case "wait_on_ptt": return WanRadioRegistrationState.WaitingForPTT;
                case "wait_on_server_confirmation": return WanRadioRegistrationState.WaitingOnServerConfirmation;
                case "register_success": return WanRadioRegistrationState.RegisterSuccess;
                case "unregister_success": return WanRadioRegistrationState.UnregisterSuccess;

                case "failed_ptt": return WanRadioRegistrationState.FailedPTT;
                case "failed_server_connection": return WanRadioRegistrationState.FailedServerConnection;
                case "failed_server_confirmation": return WanRadioRegistrationState.FailedServerConfirmation;
                case "failed_not_licensed": return WanRadioRegistrationState.FailedNotLicensed;
                case "failed_unknown": return WanRadioRegistrationState.FailedUnknown;
            }

            return WanRadioRegistrationState.Undefined;
        }

        private WanRadioRegistrationState _wanOwnerHandshakeStatus = WanRadioRegistrationState.Undefined;

        public WanRadioRegistrationState WanOwnerHandshakeStatus
        {
            get { return _wanOwnerHandshakeStatus; }
            internal set
            {
                _wanOwnerHandshakeStatus = value;
                RaisePropertyChanged("WanOwnerHandshakeStatus");
            }
        }

        public void WanRegisterRadio(string owner_token)
        {
            bool already_connected = _commandCommunication.IsConnected;

            if (!already_connected)
            {
                // keep from causing issues with regular connection logic while disconnecting
                _ignoreConnectedEvents = true;

                // connect to the radio using a TCP connection
                _commandCommunication.Connect(_ip, setup_reply: true);
            }

            WanOwnerHandshakeStatus = WanRadioRegistrationState.WaitingOnSmartLinkConnection;

            if (!already_connected)
            {
                SendReplyCommand(new ReplyHandler(GetWanRadioRegistrationReply), "wan register owner_token=" + owner_token);
            }
            else // if we are already connected, no need to send a reply as we don't need to disconnect
            {
                SendCommand("wan register owner_token=" + owner_token);
            }
        }

        public void WanUnregisterRadio(string owner_token)
        {
            bool already_connected = _commandCommunication.IsConnected;

            if (!already_connected)
            {
                // keep from causing issues with regular connection logic while disconnecting
                _ignoreConnectedEvents = true;

                // connect to the radio using a TCP connection
                _commandCommunication.Connect(_ip, setup_reply: true);
            }

            WanOwnerHandshakeStatus = WanRadioRegistrationState.WaitingOnSmartLinkConnection;

            if (!already_connected)
            {
                SendReplyCommand(new ReplyHandler(GetWanRadioRegistrationReply), "wan unregister owner_token=" + owner_token);
            }
            else // if we are already connected, no need to send a reply as we don't need to disconnect
            {
                SendCommand("wan unregister owner_token=" + owner_token);
            }
        }

        public void WanSetForwardedPorts(bool isPortForwardOn, int tcpPort, int udpPort)
        {
            bool already_connected = _commandCommunication.IsConnected;

            if (!already_connected)
            {
                _ignoreConnectedEvents = true;
                _commandCommunication.Connect(_ip, setup_reply: false);
            }

            if (isPortForwardOn)
                SendCommand("wan set public_tls_port=" + tcpPort + " public_udp_port=" + udpPort);
            else
                SendCommand("wan set public_tls_port=-1 public_udp_port=-1");

            if (!already_connected)
            {
                _commandCommunication.Disconnect();
                _ignoreConnectedEvents = false;
            }
        }

        private void GetWanRadioRegistrationReply(int seq, uint resp_val, string s)
        {
            /* State is handled through status messages */
            _commandCommunication.Disconnect();
            _ignoreConnectedEvents = false;
        }


        #endregion


        private IPAddress _ip;
        /// <summary>
        /// The TCP IP address of the radio
        /// </summary>
        public IPAddress IP
        {
            get => _ip;
            internal set
            {
                if ((value == null && _ip == null) || (_ip != null && value != null && _ip.Equals(value))) 
                    return;

                _ip = value;
                RaisePropertyChanged(nameof(IP));
            }
        }

        private string _inUseIP;
        [Obsolete("Use GuiClientIPs")]
        public string InUseIP
        {
            get { return _inUseIP; }
            set
            {
                if (_inUseIP != value)
                {
                    _inUseIP = value;
                    RaisePropertyChanged("InUseIP");
                }
            }
        }

        private string _inUseHost;
        [Obsolete("Use GuiClientHosts")]
        public string InUseHost
        {
            get { return _inUseHost; }
            set
            {
                if (_inUseHost != value)
                {
                    _inUseHost = value;
                    RaisePropertyChanged("InUseHost");
                }
            }
        }

        private string _guiClientIPs;
        public string GuiClientIPs
        {
            get => _guiClientIPs; 
            set
            {
                if (_guiClientIPs == value)
                    return;
                
                _guiClientIPs = value;
                RaisePropertyChanged(nameof(GuiClientIPs));
            }
        }

        private string _guiClientHosts;
        public string GuiClientHosts
        {
            get => _guiClientHosts;
            set
            {
                if (_guiClientHosts == null)
                    return;
                
                _guiClientHosts = value;
                RaisePropertyChanged(nameof(GuiClientHosts));
            }
        }

        private string _guiClientStations;
        public string GuiClientStations
        {
            get => _guiClientStations;
            set
            {
                if (_guiClientStations == value)
                    return;
                
                _guiClientStations = value;
                RaisePropertyChanged(nameof(GuiClientStations));
            }
        }




        private int _countRXCommand = 0;
        private int _avgRXCommandkbps = 0;
        public int AvgRXCommandkbps
        {
            get { return _avgRXCommandkbps; }
            internal set
            {
                if (_avgRXCommandkbps != value)
                {
                    _avgRXCommandkbps = value;
                    RaisePropertyChanged("AvgRXCommandkbps");
                }
            }
        }

        private int _countTXCommand = 0;
        private int _avgTXCommandkbps = 0;
        public int AvgTXCommandkbps
        {
            get { return _avgTXCommandkbps; }
            internal set
            {
                if (_avgTXCommandkbps != value)
                {
                    _avgTXCommandkbps = value;
                    RaisePropertyChanged("AvgTXCommandkbps");
                }
            }
        }

        private int _countMeter = 0;
        private int _avgMeterkbps = 0;
        public int AvgMeterkbps
        {
            get { return _avgMeterkbps; }
            internal set
            {
                if (_avgMeterkbps != value)
                {
                    _avgMeterkbps = value;
                    RaisePropertyChanged("AvgMeterkbps");
                }
            }
        }

        private int _countRXOpus = 0;
        private int _avgRXOpuskbps = 0;
        public int AvgRXOpuskbps
        {
            get { return _avgRXOpuskbps; }
            internal set
            {
                if (_avgRXOpuskbps != value)
                {
                    _avgRXOpuskbps = value;
                    RaisePropertyChanged("AvgRXOpuskbps");
                }
            }
        }

        private int _avgTXOpuskbps = 0;
        public int AvgTXOpuskbps
        {
            get { return _avgTXOpuskbps; }
            set
            {
                if (_avgTXOpuskbps != value)
                {
                    _avgTXOpuskbps = value;
                    RaisePropertyChanged("AvgTXOpuskbps");
                }
            }
        }

        private int _avgTXNetCWkbps = 0;
        public int AvgTXNetCWkbps
        {
            get { return _avgTXNetCWkbps; }
            set
            {
                if (_avgTXNetCWkbps != value)
                {
                    _avgTXNetCWkbps = value;
                    RaisePropertyChanged("AvgTXNetCWkbps");
                }
            }
        }

        private int _countWaterfall = 0;
        private int _avgWaterfallkbps = 0;
        public int AvgWaterfallkbps
        {
            get { return _avgWaterfallkbps; }
            internal set
            {
                if (_avgWaterfallkbps != value)
                {
                    _avgWaterfallkbps = value;
                    RaisePropertyChanged("AvgWaterfallkbps");
                }
            }
        }

        private int _countDAX = 0;
        private int _avgDAXkbps = 0;
        public int AvgDAXkbps
        {
            get { return _avgDAXkbps; }
            internal set
            {
                if (_avgDAXkbps != value)
                {
                    _avgDAXkbps = value;
                    RaisePropertyChanged("AvgDAXkbps");
                }
            }
        }

        private int _countFFT = 0;
        private int _avgFFTkbps = 0;
        public int AvgFFTkbps
        {
            get { return _avgFFTkbps; }
            internal set
            {
                if (_avgFFTkbps != value)
                {
                    _avgFFTkbps = value;
                    RaisePropertyChanged("AvgFFTkbps");
                }
            }
        }

        private int _avgRXTotalkbps = 0;
        public int AvgRXTotalkbps
        {
            get { return _avgRXTotalkbps; }
            internal set
            {
                if (_avgRXTotalkbps != value)
                {
                    _avgRXTotalkbps = value;
                    RaisePropertyChanged("AvgRXTotalkbps");
                }
            }
        }

        private int _avgTXTotalkbps = 0;
        public int AvgTXTotalkbps
        {
            get { return _avgTXTotalkbps; }
            internal set
            {
                if (_avgTXTotalkbps != value)
                {
                    _avgTXTotalkbps = value;
                    RaisePropertyChanged("AvgTXTotalkbps");
                }
            }
        }

        private int _commandPort = 4992;
        /// <summary>
        /// The TCP Port number of the radio, used for commands and status messages
        /// </summary>
        public int CommandPort
        {
            get { return _commandPort; }
            internal set
            {
                if (_commandPort != value)
                {
                    _commandPort = value;
                    RaisePropertyChanged("CommandPort");
                }
            }
        }

        private IPAddress _subnetMask;
        public IPAddress SubnetMask
        {
            get { return _subnetMask; }
            internal set
            {
                if (_subnetMask != value)
                {
                    _subnetMask = value;
                    RaisePropertyChanged("SubnetMask");
                }
            }
        }

        private bool _verbose = false;
        public bool Verbose
        {
            get { return _verbose; }
        }

        private string _connectedState = "Available";
        /// <summary>
        /// The state of the radio connection, i.e. "Update", "Updating", "Available", "In Use"
        /// </summary>
        public string ConnectedState
        {
            get { return _connectedState; }
            set
            {
                if (_connectedState != value)
                {
                    _connectedState = value;
                    RaisePropertyChanged("ConnectedState");
                }
            }
        }

        private bool _connected = false;
        /// <summary>
        /// The status of the connection.  True when the radio
        /// is connected, false when the radio is disconnected
        /// </summary>
        public bool Connected
        {
            get { return _connected; }
            internal set // not intended to be used externally -- for messaging
            {
                if (_connected != value)
                {
                    _connected = value;
                    RaisePropertyChanged("Connected");
                }
            }
        }
        
        private bool _externalPortLink;
        public bool ExternalPortLink
        {
            get => _externalPortLink;
            set
            {
                if (_externalPortLink == value)
                    return;
                
                _externalPortLink = value;
                RaisePropertyChanged(nameof(ExternalPortLink));
            }
        }

        private string _status;
        public string Status
        {
            get => _status;
            set
            {
                if (_status == value) 
                    return;
                
                _status = value;
                UpdateConnectedState();
                RaisePropertyChanged(nameof(Status));
            }
        }

        private string[] _rx_ant_list;
        /// <summary>
        /// A list of the available RX Antenna ports on 
        /// the radio, i.e. "ANT1", "ANT2", "RX_A", 
        /// "RX_B", "XVTR"
        /// </summary>
        public string[] RXAntList
        {
            get { return _rx_ant_list; }
        }

        private string _radioOptions;
        public string RadioOptions
        {
            get { return _radioOptions; }
            internal set
            {
                if (_radioOptions != value)
                {
                    _radioOptions = value;
                    RaisePropertyChanged("RadioOptions");
                }
            }
        }


        private Oscillator _selectedOscillator;
        /// <summary>
        /// The selected desired 10 MHz reference oscillator
        /// for FLEX-6400, FLEX-6400M, FLEX-6600, and FLEX-6600M 
        /// models
        /// </summary>
        public Oscillator SelectedOscillator
        {
            get { return _selectedOscillator; }
            set
            {
                _selectedOscillator = value;
                SendCommand("radio oscillator " + _selectedOscillator.ToString());
                RaisePropertyChanged("SelectedOscillator");
            }
        }

        private string _oscillatorState;
        /// <summary>
        /// The current selected oscillator reported by the radio
        /// (useful when SelectedOscillator is Auto)
        /// </summary>
        public string OscillatorState
        {
            get { return _oscillatorState; }
        }

        private bool _isOscillatorLocked;
        /// <summary>
        /// Gets whether the the selected oscillator
        /// is currently locked
        /// </summary>
        public bool IsOscillatorLocked
        {
            get { return _isOscillatorLocked; }
        }

        private bool _isExternalOscillatorPresent;
        public bool IsExternalOscillatorPresent
        {
            get { return _isExternalOscillatorPresent; }
        }

        private bool _isGpsdoPresent;
        public bool IsGpsdoPresent
        {
            get { return _isGpsdoPresent; }
        }

        public bool IsGnssPresent { get; private set; }

        private bool _isTcxoPresent;
        public bool IsTcxoPresent
        {
            get { return _isTcxoPresent; }
        }

        private int _rttyMarkDefault;
        /// <summary>
        /// Gets or sets the the default RTTY Mark offset value in Hz
        /// </summary>
        public int RTTYMarkDefault
        {
            get { return _rttyMarkDefault; }
            set
            {
                if (_rttyMarkDefault != value)
                {
                    _rttyMarkDefault = value;
                    SendCommand("radio set rtty_mark_default=" + _rttyMarkDefault);
                    RaisePropertyChanged("RTTYMarkDefault");
                }
            }
        }

        private bool _showTxInWaterfall = true;
        public bool ShowTxInWaterfall
        {
            get { return _showTxInWaterfall; }
            set
            {
                if (_showTxInWaterfall != value)
                {
                    _showTxInWaterfall = value;
                    SendCommand("transmit set show_tx_in_waterfall=" + Convert.ToByte(_showTxInWaterfall));
                    RaisePropertyChanged("ShowTxInWaterfall");
                }
            }
        }

        private bool _profileAutoSave;
        public bool ProfileAutoSave
        {
            get => _profileAutoSave;
            set
            {
                if (_profileAutoSave == value)
                    return;

                _profileAutoSave = value;
                SendCommand($"profile autosave {(_profileAutoSave ? "on" : "off")}");
                RaisePropertyChanged("ProfileAutoSave");
            }
        }

        private bool _txRawIQEnabled = false;
        public bool TXRawIQEnabled
        {
            get { return _txRawIQEnabled; }
        }

        private int _backlight;
        /// <summary>
        /// The front panel Flexradio Logo backlight brightness
        /// for 6400 and 6600 radios from a value 0-100
        /// </summary>
        public int Backlight
        {
            get { return _backlight; }
            set
            {
                if (_backlight != value)
                {
                    _backlight = value;
                    SendCommand("radio backlight " + _backlight);
                    RaisePropertyChanged("Backlight");
                }
            }
        }


        private bool _binauralRX = false;
        public bool BinauralRX
        {
            get { return _binauralRX; }
            set
            {
                if (_binauralRX != value)
                {
                    _binauralRX = value;
                    SendCommand("radio set binaural_rx=" + Convert.ToByte(_binauralRX));
                    RaisePropertyChanged("BinauralRX");
                }
            }
        }

        private bool _isMuteLocalAudioWhenRemoteOn;
        public bool IsMuteLocalAudioWhenRemoteOn
        {
            get { return _isMuteLocalAudioWhenRemoteOn; }
            set
            {
                if (_isMuteLocalAudioWhenRemoteOn != value)
                {
                    _isMuteLocalAudioWhenRemoteOn = value;
                    SendCommand("radio set mute_local_audio_when_remote=" + Convert.ToByte(_isMuteLocalAudioWhenRemoteOn));
                    RaisePropertyChanged("IsMuteLocalAudioWhenRemoteOn");
                }
            }
        }

        private int _meterPacketTotalCount = 0;
        public int MeterPacketTotalCount
        {
            get { return _meterPacketTotalCount; }
            set
            {
                if (_meterPacketTotalCount != value)
                {
                    _meterPacketTotalCount = value;
                    // only raise the property change every 100 packets (performance)
                    if (_meterPacketTotalCount % 100 == 0) RaisePropertyChanged("MeterPacketTotalCount");
                }
            }
        }

        private int _meterPacketErrorCount = 0;
        public int MeterPacketErrorCount
        {
            get { return _meterPacketErrorCount; }
            set
            {
                if (_meterPacketErrorCount != value)
                {
                    _meterPacketErrorCount = value;
                    RaisePropertyChanged("MeterPacketErrorCount");
                }
            }
        }

        private const int MAX_FILTER_SHARPNESS = 3;
        private const int MIN_FILTER_SHARPNESS = 0;

        private int _filterSharpnessVoice;
        /// <summary>
        /// The sharpness of the RX and TX filters when in a voice mode
        /// from values 0 (low latency) to 3 (higher latency, sharper filters)
        /// </summary>
        public int FilterSharpnessVoice
        {
            get { return _filterSharpnessVoice; }
            set
            {
                if (_filterSharpnessVoice != value)
                {
                    int new_val = value;

                    if (new_val > MAX_FILTER_SHARPNESS)
                        new_val = MAX_FILTER_SHARPNESS;
                    else if (new_val < MIN_FILTER_SHARPNESS)
                        new_val = MIN_FILTER_SHARPNESS;

                    _filterSharpnessVoice = new_val;
                    SendCommand("radio filter_sharpness voice level=" + _filterSharpnessVoice);
                    RaisePropertyChanged("FilterSharpnessVoice");
                }
            }
        }

        private bool _filterSharpnessVoiceAuto;
        /// <summary>
        /// Automatically adjusts the filter sharpness of the RX
        /// and TX filters based on bandwidth for voice modes.
        /// Sharper filters for lower bandwidths.
        /// </summary>
        public bool FilterSharpnessVoiceAuto
        {
            get { return _filterSharpnessVoiceAuto; }
            set
            {
                if (_filterSharpnessVoiceAuto != value)
                {
                    _filterSharpnessVoiceAuto = value;
                    SendCommand("radio filter_sharpness voice auto_level=" + Convert.ToByte(_filterSharpnessVoiceAuto));
                    RaisePropertyChanged("FilterSharpnessVoiceAuto");
                }
            }
        }

        private int _filterSharpnessCW;
        /// <summary>
        /// The sharpness of the RX and TX filters when in a CW mode
        /// from values 0 (low latency) to 3 (higher latency, sharper filters)
        /// </summary>
        public int FilterSharpnessCW
        {
            get { return _filterSharpnessCW; }
            set
            {
                if (_filterSharpnessCW != value)
                {
                    int new_val = value;

                    if (new_val > MAX_FILTER_SHARPNESS)
                        new_val = MAX_FILTER_SHARPNESS;
                    else if (new_val < MIN_FILTER_SHARPNESS)
                        new_val = MIN_FILTER_SHARPNESS;

                    _filterSharpnessCW = new_val;
                    SendCommand("radio filter_sharpness cw level=" + _filterSharpnessCW);
                    RaisePropertyChanged("FilterSharpnessCW");
                }
            }
        }

        private bool _filterSharpnessCWAuto;
        /// <summary>
        /// Automatically adjusts the filter sharpness of the RX
        /// and TX filters based on bandwidth for CW mode.
        /// Sharper filters for lower bandwidths.
        /// </summary>
        public bool FilterSharpnessCWAuto
        {
            get { return _filterSharpnessCWAuto; }
            set
            {
                if (_filterSharpnessCWAuto != value)
                {
                    _filterSharpnessCWAuto = value;
                    SendCommand("radio filter_sharpness cw auto_level=" + Convert.ToByte(_filterSharpnessCWAuto));
                    RaisePropertyChanged("FilterSharpnessCWAuto");
                }
            }
        }

        private int _filterSharpnessDigital;
        /// <summary>
        /// The sharpness of the RX and TX filters when in a digital mode
        /// from values 0 (low latency) to 3 (higher latency, sharper filters)
        /// </summary>
        public int FilterSharpnessDigital
        {
            get { return _filterSharpnessDigital; }
            set
            {
                if (_filterSharpnessDigital != value)
                {
                    int new_val = value;

                    if (new_val > MAX_FILTER_SHARPNESS)
                        new_val = MAX_FILTER_SHARPNESS;
                    else if (new_val < MIN_FILTER_SHARPNESS)
                        new_val = MIN_FILTER_SHARPNESS;

                    _filterSharpnessDigital = new_val;
                    SendCommand("radio filter_sharpness digital level=" + _filterSharpnessDigital);
                    RaisePropertyChanged("FilterSharpnessDigital");
                }
            }
        }

        private bool _filterSharpnessDigitalAuto;
        /// <summary>
        /// Automatically adjusts the filter sharpness of the RX
        /// and TX filters based on bandwidth for digital modes.
        /// Sharper filters for lower bandwidths.
        /// </summary>
        public bool FilterSharpnessDigitalAuto
        {
            get { return _filterSharpnessDigitalAuto; }
            set
            {
                if (_filterSharpnessDigitalAuto != value)
                {
                    _filterSharpnessDigitalAuto = value;
                    SendCommand("radio filter_sharpness digital auto_level=" + Convert.ToByte(_filterSharpnessDigitalAuto));
                    RaisePropertyChanged("FilterSharpnessDigitalAuto");
                }
            }
        }

        private void ParseFilterSharpnessStatus(string s)
        {
            //[voice || digital || cw] level=[0-3] auto_level=[T || F]
            string[] words = s.Split(' ');

            string modes = words[0].ToLower();

            foreach (string kv in words)
            {
                // skip the mode so that it doesn't give a false positive for the key/value test below
                if (kv.ToLower() == modes) continue;

                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Radio::ParseFilterSharpnessStatus: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "level":
                        {
                            int level;

                            bool b = int.TryParse(value, out level);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseFilterSharpnessStatus: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (level > MAX_FILTER_SHARPNESS)
                                level = MAX_FILTER_SHARPNESS;
                            else if (level < MIN_FILTER_SHARPNESS)
                                level = MIN_FILTER_SHARPNESS;

                            switch (modes)
                            {
                                case "voice":
                                    _filterSharpnessVoice = level;
                                    RaisePropertyChanged("FilterSharpnessVoice");
                                    break;
                                case "cw":
                                    _filterSharpnessCW = level;
                                    RaisePropertyChanged("FilterSharpnessCW");
                                    break;
                                case "digital":
                                    _filterSharpnessDigital = level;
                                    RaisePropertyChanged("FilterSharpnessDigital");
                                    break;
                            }
                        }
                        break;

                    case "auto_level":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseFilterSharpnessStatus - auto_level: Invalid value (" + kv + ")");
                                continue;
                            }

                            bool auto = Convert.ToBoolean(temp);
                            switch (modes)
                            {
                                case "voice":
                                    _filterSharpnessVoiceAuto = auto;
                                    RaisePropertyChanged("FilterSharpnessVoiceAuto");
                                    break;
                                case "cw":
                                    _filterSharpnessCWAuto = auto;
                                    RaisePropertyChanged("FilterSharpnessCWAuto");
                                    break;
                                case "digital":
                                    _filterSharpnessDigitalAuto = auto;
                                    RaisePropertyChanged("FilterSharpnessDigitalAuto");
                                    break;
                            }
                            break;
                        }
                }
            }
        }

        private bool _isAlphaLicensed;
        public bool IsAlphaLicensed
        {
            get { return _isAlphaLicensed; }
            set
            {
                if (_isAlphaLicensed != value)
                {
                    _isAlphaLicensed = value;
                    RaisePropertyChanged("IsAlphaLicensed");
                }
            }
        }

        public bool _lowLatencyDigitalModes;
        public bool LowLatencyDigitalModes
        {
            get { return _lowLatencyDigitalModes; }
            set
            {
                if (_lowLatencyDigitalModes == value)
                    return;

                _lowLatencyDigitalModes = value;
                SendCommand("radio set low_latency_digital_modes=" + Convert.ToByte(_lowLatencyDigitalModes));
                RaisePropertyChanged("LowLatencyDigitalModes");
            }
        }

        #endregion

        #region Constructor

        private int _unique_id;
        internal Radio(bool isWan = false)
        {
            _unique_id = new Random().Next();

            InitLists();
            _rapidM = new RapidM(this);
            _ale2G = new ALE2G(this);
            _ale3G = new ALE3G(this);
            _ale4G = new ALE4G(this);
            _aleComposite = new ALEComposite(this);
            _apd = new APD(this);

            IsWan = isWan;

            if (IsWan)
            {
                _commandCommunication = new TlsCommandCommunication();
            }
            else
            {
                _commandCommunication = new TcpCommandCommunication();
            }

            _commandCommunication.IsConnectedChanged += _commandCommunication_IsConnectedChanged;
            _commandCommunication.DataReceivedReady += _commandCommunication_TCPDataReceived;

            _statisticsTimer.AutoReset = true;
            _statisticsTimer.Elapsed += StatisticsUpdater;
        }

        public void SetLocalPttForGuiClient()
        {
            SendCommand("client set local_ptt=1");
        }

        Random random = new Random();
        private double GetRandomNumber(double minimum, double maximum)
        {
            return random.NextDouble() * (maximum - minimum) + minimum;
        }

        bool _ignoreConnectedEvents = false;
        private void _commandCommunication_IsConnectedChanged(bool connected)
        {
            if (!_ignoreConnectedEvents)
            {
                Connected = connected;

                if (!connected)
                {
                    Disconnect();
                }
            }
        }

        private void _commandCommunication_TCPDataReceived(string msg)
        {
            if (msg == null)
                return;
            _countRXCommand += msg.Length + TCP_HEADER_SIZE;

            // Process the pre-processed reply string buffer
            ParseRead(msg);
        }

        internal Radio(string model, string serial, string name, IPAddress ip, string version) : this()
        {
            this._model = model;
            this._serial = serial;
            this._nickname = name;
            this._ip = ip;


            UInt64 ver;
            bool b = FlexVersion.TryParse(version, out ver);
            if (!b)
            {
                Debug.WriteLine("Radio::Constructor: Error converting version string (" + version + ")");
            }
            else Version = ver;

#if TIMING
            cmd_time_table = new Hashtable();
#endif

            t1 = new Stopwatch();
            t1.Reset();
            t1.Start();
        }

        private void InitLists()
        {
            _replyTable = new Hashtable();
            _slices = new List<Slice>();
            _panadapters = new List<Panadapter>();
            _waterfalls = new List<Waterfall>();
            _meters = new List<Meter>();
            _equalizers = new List<Equalizer>();
            _daxRXAudioStream = new List<DAXRXAudioStream>();
            _daxMicAudioStreams = new List<DAXMICAudioStream>();
            _daxTXAudioStreams = new List<DAXTXAudioStream>();
            _txRemoteAudioStream = new List<TXRemoteAudioStream>();
            _rxRemoteAudioStreams = new List<RXRemoteAudioStream>();
            _daxIQStreams = new List<DAXIQStream>();
            _micInputList = new List<string>();
            _tnfs = new List<TNF>();
            _spots = new List<Spot>();
            _xvtrs = new List<Xvtr>();
            _guiClients = new List<GUIClient>();
            _memoryList = new List<Memory>();
            _usbCables = new List<UsbCable>();
            _amplifiers = new List<Amplifier>();
            _tuners = new List<Tuner>();
            _logModules = new List<LogModule>();
        }




        /// <summary>
        /// The local client IP address
        /// </summary>
        public IPAddress LocalIP
        {
            get
            {
                return _commandCommunication.LocalIP;
            }
        }

        private AutoResetEvent WaitForIpResponseFromRadioARE = new AutoResetEvent(false);

        private object _connectSyncObj = new Object();

        /// <summary>
        /// Creates a TCP client and connects to the radio
        /// </summary>
        /// <returns>Connection status of the radio</returns>
        public bool Connect(string gui_client_id = null)
        {
            _guiClientID = gui_client_id;

            // save this so we can use it later, even if it changes due to connection
            // mainly looking for update to know if we will do persistence
            string saved_connectedState = _connectedState;
            bool connected = false;

            // ensure that only one connection can be made at a time
            lock (_connectSyncObj)
            {
                if (IsWan)
                {
                    if (RequiresHolePunch)
                    {
                        /* If we require hole punching then the radio port and the source port
                         * will both be the same
                         */
                        connected = _commandCommunication.Connect(_ip, NegotiatedHolePunchPort, NegotiatedHolePunchPort);
                    }
                    else
                    {
                        connected = _commandCommunication.Connect(_ip, PublicTlsPort);
                    }

                    if (connected)
                        SendCommand("wan validate handle=" + _wanConnectionHandle);
                }
                else
                {
                    connected = _commandCommunication.Connect(_ip, true);
                }

                // When connecting to a WAN radio, the public IP address of the connected
                // client must be obtained from the radio.  This value is used to determine
                // if audio streams fromt the radio are meant for this client.
                // (IsAudioStreamStatusForThisClient() checks for LocalIP)
                if (connected)
                {
                    SendReplyCommand(new ReplyHandler(GetClientIpReplyHandler), "client ip");
                    WaitForIpResponseFromRadioARE.WaitOne(millisecondsTimeout: 5000);
                }
            }

            if (!connected) return false;

            // send client program to radio
            if (API.ProgramName != null && API.ProgramName != "")
                SendCommand("client program " + API.ProgramName);

            // turn off persistence if about to do an update
            if (saved_connectedState == "Update")
                SendCommand("client start_persistence off");

            if (LowBandwidthConnect)
                SendCommand("client low_bw_connect");

            if (API.IsGUI)
            {
                if (string.IsNullOrEmpty(_guiClientID))
                    SendReplyCommand(GUIClientIDReplyHandler, "client gui");
                else
                    SendCommand("client gui " + _guiClientID);
            }
            else
            {
                BindGUIClient(_boundClientID);
            }

            // get info (name, etc)
            GetInfo();

            // get version info from radio
            GetVersions();

            // get the list of antennas from the radio
            GetRXAntennaList();

            // get list of Input sources
            GetMicList();

            // get list of Profiles
            GetProfileLists();

            // subscribe for status updates
            SendCommand("sub client all");
            SendCommand("sub tx all");
            SendCommand("sub atu all");
            SendCommand("sub amplifier all");
            SendCommand("sub meter all");
            SendCommand("sub pan all");
            SendCommand("sub slice all");
            SendCommand("sub gps all");
            SendCommand("sub audio_stream all");
            SendCommand("sub cwx all");
            SendCommand("sub xvtr all");
            SendCommand("sub memories all");
            SendCommand("sub daxiq all");
            SendCommand("sub dax all");
            SendCommand("sub usb_cable all");
            if (_isTNFSubscribed)
                SendCommand("sub tnf all");
            SendCommand("sub spot all");
            SendCommand("sub rapidm all");
            SendCommand("sub ale all");
            SendCommand("sub log_manager");
            SendCommand("sub radio all");
            SendCommand("sub codec all");
            SendCommand("sub apd all");

            // ensure that packets are manually fragmented to avoid network issues
            SendRadioMTUCommand(_mtu);

            // Send reduced bandwidth DAX packets
            SendCommand("client set send_reduced_bw_dax=1");

            Connected = true;

            StartUDP();

            // set the streaming UDP port for this client if we're local. Wan clients use udp_register
            if (!IsWan)
                SendReplyCommand(new ReplyHandler(ClientUDPPortReplyHandler), "client udpport " + UDPPort);

            // Create NetCWStream for CW over network functionality
            _netCWStream = new NetCWStream(this);
            _netCWStream.RequestNetCWStreamFromRadio();

            StartFFTProcessThread();
            StartMeterProcessThread();
            _statisticsTimer.Enabled = true;

            StartKeepAlive();
            MonitorNetworkQuality();

            return true;
        }

        private bool _persistenceLoaded = false;
        public bool PersistenceLoaded
        {
            get { return _persistenceLoaded; }
            set
            {
                if (_persistenceLoaded != value)
                {
                    _persistenceLoaded = value;
                    RaisePropertyChanged("PersistenceLoaded");
                }
            }
        }
        private void GUIClientIDReplyHandler(int seq, uint resp_val, string reply)
        {
            if (resp_val != 0) return;
            _guiClientID = reply;
            RaisePropertyChanged("GUIClientID");
        }

        private void ClientUDPPortReplyHandler(int seq, uint resp_val, string reply)
        {
            PersistenceLoaded = true;
        }

        private void GetClientIpReplyHandler(int seq, uint resp_val, string reply)
        {
            IPAddress clientIP;
            bool b = IPAddress.TryParse(reply, out clientIP);

            if (b)
                _commandCommunication.LocalIP = clientIP;

            WaitForIpResponseFromRadioARE.Set();
        }

        private void DisconnectReplyHandler(int seq, uint resp_val, string reply)
        {
            //if (resp_val != 0) return;

            // disconnect the client that connected just to send the disconnect GUI client command
            if (_commandCommunication != null)
                _commandCommunication.Disconnect();

            // stop ignoring connected events
            _ignoreConnectedEvents = false;
        }

        private void LicenseRefreshReplyHandler(int seq, uint resp_val, string s)
        {
            // disconnect the client that connected just to send the disconnect GUI client command
            if (_commandCommunication != null)
                _commandCommunication.Disconnect();

            // stop ignoring connected events
            _ignoreConnectedEvents = false;
            OnRefreshLicenseStateCompleted();
        }

        /// <summary>
        /// Closes the TCP client and disconnects the radio
        /// </summary>
        public void Disconnect()
        {
            //Console.WriteLine("FlexLib::Disconnect()");
            /* Unsubscribe from connected changed events so that 
             * we don't recursively loop since the Disconnect() in
             * commandCommunication will raise an event
             */

            if (_commandCommunication != null)
            {
                _commandCommunication.IsConnectedChanged -= _commandCommunication_IsConnectedChanged;
                _commandCommunication.Disconnect();
            }

            Connected = false;

            lock (_xvtrs)
            {
                for (int i = 0; i < _xvtrs.Count; i++)
                {
                    Xvtr xvtr = _xvtrs[i];
                    RemoveXvtr(xvtr);
                    i--;
                }
            }

            lock (_equalizers)
                _equalizers.Clear();

            lock (_meters)
                _meters.Clear();

            RemoveAllSlices();

            RemoveAllPanadapters();

            RemoveAllWaterfalls();

            RemoveAllTNFs();

            RemoveAllAmplifiers();

            RemoveAllGUIClients();

            lock (_daxRXAudioStream)
                _daxRXAudioStream.Clear();

            lock (_daxTXAudioStreams)
                _daxTXAudioStreams.Clear();

            lock (_daxMicAudioStreams)
                _daxMicAudioStreams.Clear();

            lock (_txRemoteAudioStream)
                _txRemoteAudioStream.Clear();

            lock (_daxIQStreams)
                _daxIQStreams.Clear();

            lock (_replyTable)
                _replyTable.Clear();

            _trxPsocVersion = 0;
            _paPsocVersion = 0;
            _fpgaVersion = 0;

            _rx_ant_list = null;

            _semNewFFTPacket.Set();
            _semNewMeterPacket.Set();

            StopUDP();

            _persistenceLoaded = false;

            API.RemoveRadio(this);

            if (_updating)
                _commandCommunication.IsConnectedChanged += _commandCommunication_IsConnectedChanged;
        }

        /// <summary>
        /// Disconnects all currently connected GUI clients from
        /// the radio
        /// </summary>
        public void DisconnectAllGuiClients()
        {
            // keep from causing issues with regular connection logic while disconnecting
            _ignoreConnectedEvents = true;

            //connect to the radio that is in use using a TCP connection
            _commandCommunication.Connect(_ip, true);

            // send the disconnect GUI client command
            SendReplyCommand(new ReplyHandler(DisconnectReplyHandler), "client disconnect");
        }

        /// <summary>
        /// Disconnects a single connected client given the client's
        /// handle
        /// </summary>
        /// <param name="handle">The handle ID of the client</param>
        public void DisconnectClientByHandle(string handle)
        {
            SendCommand("client disconnect " + handle);
        }

        /// <summary>
        /// Gets the current licensing state of the radio
        /// </summary>
        public void RefreshLicenseState()
        {
            bool already_connected = _commandCommunication.IsConnected;

            if (!already_connected)
            {
                // keep from causing issues with regular connection logic while disconnecting
                _ignoreConnectedEvents = true;

                // connect to the radio using a TCP connection
                _commandCommunication.Connect(_ip, setup_reply: true);
            }

            if (!already_connected)
            {
                SendReplyCommand(new ReplyHandler(LicenseRefreshReplyHandler), "license refresh");
            }
            else
            {
                SendCommand("license refresh");
            }
        }

        /// <summary>
        /// Reboots the radio.  This may take several minutes.
        /// </summary>
        public void RebootRadio()
        {
            SendCommand("radio reboot");
        }

        private Stopwatch _keepAliveTimer = new Stopwatch();
        private double _lastPingRTT = 0;

        /* This will just be a free running timer used to report a ms timing to 
         * the radio for jitter measurements. */
        private Stopwatch _jitterTimer = Stopwatch.StartNew();

        private System.Timers.Timer _keepaliveTimerLoop = new System.Timers.Timer(1000);

        private void StartKeepAlive()
        {
            if (!_connected || _keepaliveTimerLoop.Enabled)
                return;

            string program_name = API.ProgramName;
            if (program_name == null)
            {
                Debug.WriteLine("The API.ProgramName should be set before connecting.  Please correct and try again.");
                throw new Exception("ProgramName not set");
            }

            // tell the radio to watch for pings
#if (!DEBUG)
            /* We still want to send pings but we don't want the radio to disconnect us */
            SendCommand("keepalive enable");
#endif

            _keepaliveTimerLoop.AutoReset = true;
            _keepaliveTimerLoop.Elapsed += KeepaliveTimerLoopTask;
            _keepaliveTimerLoop.Enabled = true;
        }

        private void KeepaliveTimerLoopTask(Object source, ElapsedEventArgs e)
        {
            if (!_connected)
            {
                _keepaliveTimerLoop.Enabled = false;
                return;
            }

#if (!DEBUG)
            if ( _keepAliveTimer.ElapsedMilliseconds / 1000.0 > API.RADIOLIST_TIMEOUT_SECONDS)
            {
                /* Only disconnect if we are not in DEBUG */
                // yes -- we should disconnect
                if (_commandCommunication != null)
                    _commandCommunication.Disconnect();

                API.LogDisconnect($"Radio::KeepAlive()--{this.ToString()} ping timeout");
            }
#endif

            if (!API.ProgramName.Contains("Maestro"))
                SendReplyCommand(new ReplyHandler(GetPingReply), $"ping ms_timestamp={_jitterTimer.ElapsedMilliseconds}");
            else
                SendReplyCommand(new ReplyHandler(GetPingReply), "ping");

            if (!_keepAliveTimer.IsRunning)
                _keepAliveTimer.Restart();
        }

        private void GetPingReply(int seq, uint resp_val, string reply)
        {
            if (resp_val != 0)
                return;

            _keepAliveTimer.Stop();
            _lastPingRTT = _keepAliveTimer.ElapsedMilliseconds;
        }

        public void JitterKeepAlive(string timestamp)
        {
            /* Used for Maestro jitter calculation. Passes the Y-Pic ping command with a timestamp used for 
             * whole system jitter calculation.
             */
            SendCommand("ping ms_timestamp=0x" + timestamp);
        }

        #endregion

        #region Reply/Status Processing Routines

        private ConcurrentQueue<VitaFFTPacket> FFTPacketQueue = new ConcurrentQueue<VitaFFTPacket>();
        private void ProcessFFTDataPacket(VitaFFTPacket packet)
        {
            FFTPacketQueue.Enqueue(packet);
            _semNewFFTPacket.Set();

            //Panadapter pan = FindPanadapterByStreamID(packet.stream_id);
            //if (pan == null) return;

            //pan.AddData(packet.payload, packet.start_bin_index, packet.frame_index);
        }

        private void StatisticsUpdater(Object source, ElapsedEventArgs args)
        {
            AvgRXTotalkbps = (int)((_countDAX + _countFFT + _countMeter + _countRXOpus + _countWaterfall + _countRXCommand) * 0.008f);

            if (_netCWStream != null)
            {
                AvgTXTotalkbps = (int)(_countTXCommand * 0.008f + AvgTXOpuskbps + _netCWStream.TXCount * 0.008f);
            }
            else
            {
                AvgTXTotalkbps = (int)(_countTXCommand * 0.008f + AvgTXOpuskbps);
            }

            AvgDAXkbps = (int)(_countDAX * 0.008f);
            _countDAX = 0;
            AvgFFTkbps = (int)(_countFFT * 0.008f);
            _countFFT = 0;
            AvgMeterkbps = (int)(_countMeter * 0.008f);
            _countMeter = 0;
            AvgRXOpuskbps = (int)(_countRXOpus * 0.008f);
            _countRXOpus = 0;
            AvgWaterfallkbps = (int)(_countWaterfall * 0.008f);
            _countWaterfall = 0;
            AvgRXCommandkbps = (int)(_countRXCommand * 0.008f);
            _countRXCommand = 0;
            AvgTXCommandkbps = (int)(_countTXCommand * 0.008f);
            _countTXCommand = 0;

            if (_netCWStream != null)
            {
                AvgTXNetCWkbps = (int)(_netCWStream.TXCount * 0.008f);
                _netCWStream.TXCount = 0;
            }
            else
            {
                AvgTXNetCWkbps = 0;
            }
        }

        private void ProcessFFTDataPacket_ThreadFunction()
        {
            VitaFFTPacket packet = null;
            while (_connected)
            {
                bool try_dequeue_result = false;
                _semNewFFTPacket.WaitOne();
                if (!_connected) break;
                while (try_dequeue_result = FFTPacketQueue.TryDequeue(out packet))
                {
                    Panadapter pan = FindPanadapterByStreamID(packet.stream_id);
                    if (pan == null) continue;

                    pan.AddData(packet.payload, packet.start_bin_index, packet.frame_index, packet.header.packet_count);
                }
            }
        }

        private void ProcessWaterfallDataPacket(VitaWaterfallPacket packet)
        {
            Waterfall fall = FindWaterfallByStreamID(packet.stream_id);
            if (fall == null) return;

            fall.AddData(packet.tile, packet.header.packet_count);
        }

        private int last_packet_count = LAST_PACKET_COUNT_UNINITIALIZED;
        private void ProcessMeterDataPacket(VitaMeterPacket packet)
        {

            // queue and get out so we don't hold up the network thread
            meterQueue.Enqueue(packet);

            if (meterQueue.Count > 1000)
            {
                Debug.WriteLine("meterQueue.Count =  " + meterQueue.Count + ". This should not happen, please investigate. Flushing queue.");
                VitaMeterPacket trash = null;
                while (meterQueue.Count > 10)
                {
                    meterQueue.TryDequeue(out trash);
                }
            }

            _semNewMeterPacket.Set();

            // lost packet diagnostics
            int packet_count = packet.header.packet_count;
            MeterPacketTotalCount++;
            //normal case -- this is the next packet we are looking for, or it is the first one
            if (packet_count == (last_packet_count + 1) % 16 || last_packet_count == LAST_PACKET_COUNT_UNINITIALIZED)
            {
                // do nothing
            }
            else
            {
                Debug.WriteLine("Meter Packet: Expected " + ((last_packet_count + 1) % 16) + "  got " + packet_count);
                MeterPacketErrorCount++;
            }

            last_packet_count = packet_count;


            //for (int i = 0; i < packet.NumMeters; i++)
            //{
            //    int id = (int)packet.GetMeterID(i);
            //    Meter m = FindMeterByIndex(id);
            //    if (m != null)
            //        m.UpdateValue(packet.GetMeterValue(i));
            //}
        }

        public void ResetMeterPacketStatistics()
        {
            _meterPacketErrorCount = 0;
            _meterPacketTotalCount = 0;
            last_packet_count = LAST_PACKET_COUNT_UNINITIALIZED;
        }

        private void ProcessMeterDataPacket_ThreadFunction()
        {
            VitaMeterPacket packet = null;

            while (_connected)
            {
                _semNewMeterPacket.WaitOne();
                if (!_connected) break;
                // Contains the meter's ID and raw value
                Dictionary<int, short> meterUpdateDictionary = new Dictionary<int, short>();

                while (meterQueue.TryDequeue(out packet))
                {
                    for (int i = 0; i < packet.NumMeters; i++)
                    {
                        // Add the meter index to the update list if it doesn't already exist in the list.
                        // If it alraedy exists, update the value.
                        int id = (int)packet.GetMeterID(i);

                        if (!meterUpdateDictionary.ContainsKey(id))
                            meterUpdateDictionary.Add(id, packet.GetMeterValue(i));
                        else
                            meterUpdateDictionary[id] = packet.GetMeterValue(i);
                    }
                }

                // Update the meters to the GUI
                foreach (int meter_id in meterUpdateDictionary.Keys)
                {
                    Meter m = FindMeterByIndex(meter_id);
                    if (m != null)
                        m.UpdateValue(meterUpdateDictionary[meter_id]);
                }
            }
        }

        private void ProcessOpusDataPacket(VitaOpusDataPacket packet)
        {
            RXRemoteAudioStream remoteAudioRX = FindRXRemoteAudioStreamByStreamID(packet.stream_id);
            if (remoteAudioRX != null)
            {
                remoteAudioRX.AddRXData(packet);
                return;
            }
        }

        private void ProcessIFDataPacket(VitaIFDataPacket packet)
        {
            // Remote audio uncompressed
            RXRemoteAudioStream remoteAudioRX = FindRXRemoteAudioStreamByStreamID(packet.stream_id);
            if (remoteAudioRX != null)
            {
                remoteAudioRX.AddRXData(packet);
            }

            // DAX RX
            DAXRXAudioStream audio_stream = FindDAXRXAudioStreamByStreamID(packet.stream_id);
            if (audio_stream != null)
            {
                audio_stream.AddRXData(packet);
                return;
            }

            // DAX MIC
            DAXMICAudioStream mic_audio_stream = FindDAXMICAudioStreamByStreamID(packet.stream_id);
            if (mic_audio_stream != null)
            {
                mic_audio_stream.AddRXData(packet);
                return;
            }

            // DAX IQ
            DAXIQStream iq_stream = FindDAXIQStreamByStreamID(packet.stream_id);
            if (iq_stream == null) return;

            iq_stream.AddRXData(packet);
        }

        #endregion

        #region Parse Routines

        //private void ParseRead_ThreadFunction()
        //{
        //    string message = null;
        //    bool try_dequeue_result = false;
        //    while (_connected)
        //    {
        //        _semNewReadBuffer.WaitOne();
        //        while (try_dequeue_result = _readBufferQueue.TryDequeue(out message))
        //        {                    
        //            ParseRead(message);
        //        }
        //    }
        //}

        private void ParseRead(string s)
        {
            // bump the ping reply timer so that EXTREMELY slow computers do not timeout
            _keepAliveTimer.Stop();

            // handle empty string
            if (s.Length == 0) return;

            // decide what kind of message this is based on the first character
            switch (s[0]) // first character of message
            {
                case 'R': // reply
                    ParseReply(s);
                    break;
                case 'S': // status
                    ParseStatus(s);
                    break;
                case 'H': // handle
                    ParseHandle(s);
                    break;
                case 'V': // version
                    ParseProtocolVersion(s);
                    break;
                case 'M': // message
                    ParseMessage(s);
                    break;
            }
        }

        private void ParseReply(string s)
        {
            string[] tokens = s.Split('|');

            // handle incomplete reply -- must have at least 3 tokens
            if (tokens.Length < 3)
            {
                Debug.WriteLine("FlexLib::Radio::ParseReply: Incomplete reply -- must have at least 3 tokens (" + s + ")");
                return;
            }

            // handle first token shorter than minimum (2 characters)
            if (tokens[0].Length < 2)
            {
                Debug.WriteLine("FlexLib::Radio::ParseReply: First reply token invalid -- min 2 chars (" + s + ")");
                return;
            }

            // parse the sequence number
            int seq;
            bool b = int.TryParse(tokens[0].Substring(1), out seq);
            if (!b) // handle sequence number formatted improperly
            {
                Debug.WriteLine("FlexLib::Radio::ParseReply: Reply sequence invalid (" + s + ")");
                return;
            }

            // parse the hex response number
            uint resp;
            b = uint.TryParse(tokens[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out resp);
            if (!b) // handle response number formatted improperly
            {
                Debug.WriteLine("FlexLib::Radio::ParseReply: Reply response number invalid (" + s + ")");
                return;
            }

            // parse the message
            string msg = tokens[2];

            // parse optional debug if present
            string debug = "";
            if (tokens.Length == 4)
                debug = tokens[3];

#if TIMING
            // add timing info
            if (cmd_time_table.Contains(seq))
            {
                CmdTime cmd_time = (CmdTime)cmd_time_table[seq];
                cmd_time.Reply = msg;
                t1.Stop();
                cmd_time.Stop = t1.Elapsed.TotalSeconds;

                Debug.WriteLine(cmd_time.ToString());
            }
#endif

            //Debug.WriteLine("ParseReply: " + seq + ": " + s);

            ReplyHandler handler = null;

            // is there an entry in the reply table looking for this reply
            lock (_replyTable)
            {
                if (_replyTable.ContainsKey(seq))
                {
                    // yes -- pull the handler out of the reply table
                    handler = (ReplyHandler)_replyTable[seq];

                    // remove the handler from the table as there will only be one response from any one command
                    _replyTable.Remove(seq);
                }
            }

            // call the method to handle the reply on the object from the table
            if (handler != null)
                handler(seq, resp, msg);
        }

        public EventHandler ClientDisconnectReq;
        private void OnClientDisconnectReq()
        {
            if (ClientDisconnectReq != null)
                ClientDisconnectReq(this, null);
        }

        public EventHandler DuplicateClientIdDisconnectReq;
        private void OnDuplicateClientIdDisconnectReq()
        {
            if (DuplicateClientIdDisconnectReq != null)
                DuplicateClientIdDisconnectReq(this, null);
        }

        public EventHandler RefreshLicenseStateCompleted;
        private void OnRefreshLicenseStateCompleted()
        {
            if (RefreshLicenseStateCompleted != null)
                RefreshLicenseStateCompleted(this, null);
        }

        private void ParseStatus(string s)
        {
            string[] tokens = s.Split('|');
            // handle minimum status tokens
            if (tokens.Length < 2)
            {
                Debug.WriteLine("ParseStatus: Invalid status -- min 2 tokens (" + s + ")");
                return;
            }

            string[] words = tokens[1].Split(' ');

            switch (words[0])
            {
                case "ale":
                    if (words[1] == "2g")
                    {
                        if (words.Length > 2 && words[2] == "pair")
                        {
                            _aleComposite.Parse2GStationPairing(tokens[1].Substring("ale 2g pair ".Length));
                        }
                        else
                        {
                            _ale2G.ParseStatus(tokens[1].Substring("ale 2g ".Length));
                        }
                    }
                    else if (words[1] == "3g")
                    {
                        if (words.Length > 2 && words[2] == "pair")
                        {
                            _aleComposite.Parse3GStationPairing(tokens[1].Substring("ale 3g pair ".Length));
                        }
                        else
                        {
                            _ale3G.ParseStatus(tokens[1].Substring("ale 3g ".Length));
                        }
                    }
                    else if (words[1] == "4g")
                    {
                        if (words.Length > 2 && words[2] == "pair")
                        {
                            _aleComposite.Parse4GStationPairing(tokens[1].Substring("ale 4g pair ".Length));
                        }
                        else
                        {
                            _ale4G.ParseStatus(tokens[1].Substring("ale 4g ".Length));
                        }
                    }
                    else
                    {
                        _aleComposite.ParseStatus(tokens[1].Substring("ale ".Length));
                    }
                    break;
                case "ale_station_group":
                    {
                        _aleComposite.ParseStationGroupStatus(tokens[1].Substring("ale_station_group ".Length));
                        break;
                    }
                case "ale_multipoint":
                    {
                        _aleComposite.ParseMultipointStatus(tokens[1].Substring("ale_multipoint ".Length));
                        break;
                    }
                case "amplifier":
                    ParseAmplifierStatus(tokens[1].Substring("amplifier ".Length)); // remove the "amplifier "
                    break;

                case "apd":
                    APD.ParseStatus(tokens[1].Substring("apd ".Length));
                    break;

                case "atu":
                    ParseATUStatus(tokens[1].Substring("atu ".Length)); // remove the "atu "
                    break;

                case "client":
                    ParseClientStatus(tokens[1].Substring("client ".Length));
                    break;

                case "cwx":
                    if (_cwx == null)
                        _cwx = new CWX(this);
                    _cwx.StatusUpdate(tokens[1].Substring(4)); // "cwx "
                    break;

                case "display":
                    {
                        if (words.Length < 4)
                        {
                            Debug.WriteLine("ParseStatus: Too few words for display status -- min 3(" + s + ")");
                            return;
                        }

                        switch (words[1])
                        {
                            case "pan":
                                uint stream_id;
                                bool b = StringHelper.TryParseInteger(words[2], out stream_id);
                                if (!b)
                                {
                                    Debug.WriteLine("ParseStatus: Invalid display pan stream_id (" + s + ")");
                                    return;
                                }

                                //Debug.WriteLine("Stream Id Parsed From Status = 0x" + words[2].Substring(2) + " (" + stream_id.ToString()+")");

                                bool add_new_pan = false;
                                Panadapter pan = FindPanadapterByStreamID(stream_id);
                                if (pan == null)
                                {
                                    if (s.Contains("removed")) return;
                                    pan = new Panadapter(this);
                                    pan.StreamID = stream_id;
                                    add_new_pan = true;
                                }

                                if (s.Contains("removed"))
                                {
                                    RemovePanadapter(pan);
                                }
                                else
                                {
                                    if (add_new_pan)
                                        AddPanadapter(pan);

                                    string update = tokens[1].Substring("display pan ".Length + words[2].Length + 1); // display pan <client_handle> -- +1 for the trailing space
                                    pan.StatusUpdate(update);
                                }

                                break;

                            case "waterfall":
                                b = StringHelper.TryParseInteger(words[2], out stream_id);
                                if (!b)
                                {
                                    Debug.WriteLine("ParseStatus: Invalid waterfall pan stream_id (" + s + ")");
                                    return;
                                }

                                //Debug.WriteLine("Stream Id Parsed From Status = 0x" + words[2].Substring(2) + " (" + stream_id.ToString()+")");

                                bool add_new_fall = false;
                                Waterfall fall = FindWaterfallByStreamID(stream_id);
                                if (fall == null)
                                {
                                    if (s.Contains("removed")) return;
                                    fall = new Waterfall(this);
                                    fall.StreamID = stream_id;
                                    add_new_fall = true;
                                }

                                if (s.Contains("removed"))
                                {
                                    RemoveWaterfall(fall);
                                }
                                else
                                {
                                    if (add_new_fall)
                                    {
                                        AddWaterfall(fall);
                                    }
                                    string update = tokens[1].Substring("display waterfall ".Length + words[2].Length + 1); // display fall <client_handle> -- +1 for trailing space
                                    fall.StatusUpdate(update);
                                }

                                break;

                            case "panafall":
                                break;
                        }
                        break;
                    }

                case "eq":
                    {
                        bool eq_added = false;
                        Equalizer eq = null;

                        if (s.Contains("txsc"))
                        {
                            eq = FindEqualizerByEQSelect(EqualizerSelect.TX);
                            if (eq == null)
                            {
                                eq = new Equalizer(this, EqualizerSelect.TX);
                                eq_added = true;
                            }
                            string update = tokens[1].Substring("eq txsc".Length);
                            eq.StatusUpdate(update);
                        }
                        else if (s.Contains("rxsc"))
                        {
                            eq = FindEqualizerByEQSelect(EqualizerSelect.RX);
                            if (eq == null)
                            {
                                eq = new Equalizer(this, EqualizerSelect.RX);
                                eq_added = true;
                            }
                            string update = tokens[1].Substring("eq rxsc".Length);
                            eq.StatusUpdate(update);
                        }
                        else if (s.Contains("apf"))
                        {
                            ParseAPFStatus(tokens[1].Substring("eq apf ".Length)); // "eq apf "
                        }

                        if (eq_added && eq != null)
                            AddEqualizer(eq);

                    }
                    break;

                case "file":
                    {
                        ParseUpdateStatus(tokens[1].Substring("file update ".Length)); // "file update "
                    }
                    break;

                case "gps":
                    {
                        ParseGPSStatus(tokens[1].Substring("gps ".Length)); // "gps "
                    }
                    break;

                case "interlock":
                    ParseInterlockStatus(tokens[1].Substring("interlock ".Length)); // "interlock "
                    break;

                case "log":
                    {
                        ParseLogModuleStatus(tokens[1].Substring("log ".Length)); // remove the "log "
                    }
                    break;

                case "memory":
                    ParseMemoryStatus(tokens[1].Substring("memory ".Length));
                    break;

                case "meter":
                    ParseMeterStatus(tokens[1].Substring("meter ".Length));
                    break;

                case "mic_audio_stream":
                    {
                        if (words.Length < 3)
                        {
                            Debug.WriteLine("ParseStatus: Too few words for mic_audio_stream status -- min 3(" + s + ")");
                            return;
                        }

                        uint stream_id;
                        bool b = StringHelper.TryParseInteger(words[1], out stream_id);
                        if (!b)
                        {
                            Debug.WriteLine("ParseStatus: Invalid mic_audio_stream stream_id (" + s + ")");
                            return;
                        }

                        bool add_new = false;
                        DAXMICAudioStream mic_audio_stream = FindDAXMICAudioStreamByStreamID(stream_id);
                        if (mic_audio_stream == null)
                        {
                            if (s.Contains("in_use=0")) return;

                            if (!IsDAXRXAudioStreamStatusForThisClient(s)) return;

                            add_new = true;
                            mic_audio_stream = new DAXMICAudioStream(this);
                            mic_audio_stream.StreamID = stream_id;
                        }

                        if (s.Contains("in_use=0"))
                        {
                            lock (_daxMicAudioStreams)
                            {
                                mic_audio_stream.Closing = true;
                            }
                            RemoveDAXMICAudioStream(mic_audio_stream.StreamID);
                        }
                        else
                        {
                            string update = tokens[1].Substring(17 + words[1].Length + 1); // mic_audio_stream <client_handle>
                            mic_audio_stream.StatusUpdate(update);
                        }

                        if (add_new)
                            AddDAXMICAudioStream(mic_audio_stream);
                    }
                    break;

                case "profile":
                    ParseProfilesStatus(tokens[1].Substring("profile ".Length)); // "profile "
                    break;

                case "radio":
                    ParseRadioStatus(tokens[1].Substring("radio ".Length)); // radio 
                    break;

                case "rapidm":
                    _rapidM.ParseStatus(tokens[1].Substring("rapidm ".Length));
                    break;

                case "slice":
                    {
                        // handle minimum words
                        if (words.Length < 3 || words[1] == "")
                        {
                            Debug.WriteLine("ParseStatus: Too few words for slice status -- min 3 (" + s + ")");
                            return;
                        }

                        uint index;
                        bool b = uint.TryParse(words[1], out index);
                        if (!b)
                        {
                            Debug.WriteLine("ParseStatus: Invalid slice index (" + s + ")");
                            return;
                        }

                        bool add_slice = false;
                        Slice slc = FindSliceByIndex((int)index);
                        if (slc == null)
                        {
                            if (s.Contains("in_use=0"))
                                return;

                            slc = new Slice(this);
                            slc.Index = (int)index;

                            lock (_meters)
                            {
                                for (int i = 0; i < _meters.Count; i++)
                                {
                                    if (_meters[i].Source == Meter.SOURCE_SLICE && _meters[i].SourceIndex == index)
                                        slc.AddMeter(_meters[i]);
                                }
                            }

                            add_slice = true;
                        }

                        if (s.Contains("in_use=0"))
                        {
                            RemoveSlice(slc);
                            return;
                        }

                        if (s.Contains("in_use=1")) // EW 2014-11-03: this is happening much more than I would expect
                        {
                            lock (_meters)
                            {
                                for (int i = 0; i < _meters.Count; i++)
                                {
                                    if (_meters[i].Source == Meter.SOURCE_SLICE && _meters[i].SourceIndex == index)
                                        slc.AddMeter(_meters[i]);
                                }
                            }
                        }

                        string update = tokens[1].Substring(7 + words[1].Length); // "slice <num> "


                        if (add_slice)
                            AddSlice(slc);

                        slc.StatusUpdate(update);
                        break;
                    }

                case "spot":
                    {
                        ParseSpotStatus(tokens[1].Substring("spot ".Length));
                        break;
                    }

                case "stream":
                    {
                        // stream <streamid> type=<remote_audio_rx|remote_audio_tx> compression=<none|opus> client_handle=<handle>
                        // stream <streamid> type=dax_iq daxiq_channel=<channel> pan=<panadater> rate=<rate> client_handle=<handle>
                        // or in removal: stream <streamid> removed
                        if (words.Length < 3)
                        {
                            Debug.WriteLine("ParseStatus: Too few words for stream status -- min 3 (" + s + ")");
                            return;
                        }

                        uint stream_id;
                        bool b = StringHelper.TryParseInteger(words[1], out stream_id);
                        if (!b)
                        {
                            Debug.WriteLine("ParseStatus: Invalid stream stream_id (" + s + ")");
                            return;
                        }

                        string type;

                        if (words[2].Contains("removed"))
                        {
                            // This looks hacky but as an oversight we did not include the type of stream in the 
                            // removed commands - so we do not know what type of stream is being removed. 
                            // Each of these functions traverses lists of a particular type of stream - we shotgun
                            // all of them so that we properly cover all types.
                            RemoveAudioStream(stream_id);
                            RemoveDAXTXAudioStream(stream_id);
                            RemoveDAXMICAudioStream(stream_id);
                            RemoveDAXIQStream(stream_id);
                            RemoveRXRemoteAudioStream(stream_id);
                            RemoveTXRemoteAudioStream(stream_id);
                            return;
                        }

                        // Get the stream type that we are trying to parse
                        type = words[2].Substring("type=".Length);

                        // Pass along key value pairs for everything after "stream <streamid> type=<type>"
                        string statusUpdateKeyValuePairs = tokens[1].Substring("stream ".Length + words[1].Length + " type=".Length + type.Length); // stream <stream_id> 

                        switch (type)
                        {
                            case "dax_rx":
                                ParseDAXRXStatus(stream_id, statusUpdateKeyValuePairs);
                                break;
                            case "dax_tx":
                                ParseDAXTXStatus(stream_id, statusUpdateKeyValuePairs);
                                break;
                            case "dax_mic":
                                ParseDAXMICStatus(stream_id, statusUpdateKeyValuePairs);
                                break;
                            case "dax_iq":
                                ParseDAXIQStatus(stream_id, statusUpdateKeyValuePairs);
                                break;
                            case "remote_audio_rx":
                                ParseRXRemoteAudioStreamStatus(stream_id, statusUpdateKeyValuePairs);
                                break;
                            case "remote_audio_tx":
                                ParseRemoteAudioTXStatus(stream_id, statusUpdateKeyValuePairs);
                                break;

                        }
                    }
                    break;

                case "tnf":
                    {
                        uint tnf_id;
                        bool b = StringHelper.TryParseInteger(words[1], out tnf_id);
                        if (!b)
                        {
                            Debug.WriteLine("ParseStatus: Invalid TNF ID (" + s + ")");
                            return;
                        }

                        bool add_new_tnf = false;

                        TNF tnf = FindTNFById(tnf_id);
                        if (tnf == null)
                        {
                            if (s.Contains("removed")) return;
                            tnf = new TNF(this, tnf_id);
                            add_new_tnf = true;
                        }

                        if (s.Contains("removed"))
                        {
                            lock (_tnfs)
                            {
                                _tnfs.Remove(tnf);
                            }

                            if (tnf != null)
                                OnTNFRemoved(tnf);
                        }
                        else
                        {
                            string update = tokens[1].Substring("tnf ".Length + words[1].Length + 1); // tnf <tnf_id>
                            tnf.StatusUpdate(update);
                        }

                        if (add_new_tnf)
                            AddTNF(tnf);
                    }
                    break;

                case "transmit":
                    {
                        ParseTransmitStatus(tokens[1].Substring("transmit ".Length));
                    }
                    break;

                case "turf":
                    {
                        ParseTurfStatus(tokens[1].Substring("turf ".Length)); // "turf "
                    }
                    break;

                case "tx_audio_stream":
                    {
                        if (words.Length < 3)
                        {
                            Debug.WriteLine("ParseStatus: Too few words for tx_audio_stream status -- min 3(" + s + ")");
                            return;
                        }

                        uint stream_id;
                        bool b = StringHelper.TryParseInteger(words[1], out stream_id);
                        if (!b)
                        {
                            Debug.WriteLine("ParseStatus: Invalid tx_audio_stream stream_id (" + s + ")");
                            return;
                        }

                        bool add_new = false;
                        DAXTXAudioStream tx_audio_stream = FindDAXTXAudioStreamByStreamID(stream_id);
                        if (tx_audio_stream == null)
                        {
                            if (s.Contains("in_use=0")) return;

                            if (!IsDAXRXAudioStreamStatusForThisClient(s)) return;

                            add_new = true;
                            tx_audio_stream = new DAXTXAudioStream(this);
                            tx_audio_stream.TXStreamID = stream_id;
                        }

                        if (s.Contains("in_use=0"))
                        {
                            lock (_daxTXAudioStreams)
                            {
                                tx_audio_stream.Closing = true;
                            }
                            RemoveDAXTXAudioStream(tx_audio_stream.TXStreamID);
                        }
                        else
                        {
                            string update = tokens[1].Substring(16 + words[1].Length + 1); // tx_audio_stream <client_handle>
                            tx_audio_stream.StatusUpdate(update);
                        }

                        if (add_new)
                            AddDAXTXAudioStream(tx_audio_stream);
                    }
                    break;

                case "usb_cable":
                    {
                        ParseUsbCableStatus(tokens[1].Substring("usb_cable ".Length)); // "usb_cable "
                    }
                    break;

                case "waveform":
                    {
                        ParseWaveformStatus(tokens[1].Substring("waveform ".Length));
                    }
                    break;

                case "wan":
                    {
                        ParseWanStatus(tokens[1].Substring("wan ".Length));
                    }
                    break;
                case "xvtr":
                    {
                        // handle minimum words
                        if (words.Length < 3 || words[1] == "")
                        {
                            Debug.WriteLine("ParseStatus: Too few words for xvtr status -- min 3 (" + s + ")");
                            return;
                        }

                        uint index;
                        bool b = uint.TryParse(words[1], out index);
                        if (!b)
                        {
                            Debug.WriteLine("ParseStatus: Invalid xvtr index (" + s + ")");
                            return;
                        }

                        bool add_xvtr = false;
                        Xvtr xvtr = FindXvtrByIndex((int)index);
                        if (xvtr == null)
                        {
                            if (s.Contains("in_use=0"))
                                return;

                            xvtr = new Xvtr(this);
                            xvtr.Index = (int)index;

                            add_xvtr = true;
                        }

                        if (s.Contains("in_use=0"))
                        {
                            RemoveXvtr(xvtr);
                            return;
                        }

                        string update = tokens[1].Substring("xvtr ".Length + words[1].Length + 1); // "xvtr <num> "

                        xvtr.StatusUpdate(update);

                        if (add_xvtr)
                            AddXvtr(xvtr);
                    }
                    break;

                default:
                    Debug.WriteLine("Radio::ParseStatus: Unparsed status (" + s + ")");
                    break;
            }
        }

        private void ParseDAXTXStatus(uint stream_id, string statusUpdateKeyValuePairs)
        {
            DAXTXAudioStream txAudioStream = FindDAXTXAudioStreamByStreamID(stream_id);
            if (txAudioStream == null)
            {
                // create an audio tx stream if one has not yet been created
                txAudioStream = new DAXTXAudioStream(this);
                txAudioStream.TXStreamID = stream_id;
                AddDAXTXAudioStream(txAudioStream);
            }

            // slice=<slc> dax_clients=0 client_handle=<handle> 
            txAudioStream.StatusUpdate(statusUpdateKeyValuePairs);
        }

        private void ParseDAXRXStatus(uint stream_id, string statusUpdateKeyValuePairs)
        {
            DAXRXAudioStream audioRXStream = FindDAXRXAudioStreamByStreamID(stream_id);
            if (audioRXStream == null)
            {
                // create a audio rx stream if one has not yet been created
                audioRXStream = new DAXRXAudioStream(this);
                audioRXStream.StreamID = stream_id;

                // We have added a brand new audio rx stream, so add it to our collection.
                AddDAXRXAudioStream(audioRXStream);
            }

            // slice=<slc> dax_clients=0 client_handle=<handle> 
            audioRXStream.StatusUpdate(statusUpdateKeyValuePairs);
        }

        private void ParseDAXIQStatus(uint stream_id, string statusUpdateKeyValuePairs)
        {
            DAXIQStream daxIQStream = FindDAXIQStreamByStreamID(stream_id);
            if (daxIQStream == null)
            {
                // is it going away?
                if (statusUpdateKeyValuePairs.Contains("removed"))
                {
                    // yes -- then do nothing, don't add it
                    return;
                }

                // create a DAX IQ stream if one has not yet been created
                daxIQStream = new DAXIQStream(this);
                daxIQStream.StreamID = stream_id;

                // slice=<slc> dax_clients=0 client_handle=<handle> 
                daxIQStream.StatusUpdate(statusUpdateKeyValuePairs);

                // We have added a brand new DAX IQ stream, so add it to our collection.
                AddDAXIQStream(daxIQStream);
            }
            else
            {
                // is it going away?
                if (statusUpdateKeyValuePairs.Contains("removed"))
                {
                    // yes -- remove the object
                    RemoveDAXIQStream(stream_id);
                    return;
                }

                // slice=<slc> dax_clients=0 client_handle=<handle> 
                daxIQStream.StatusUpdate(statusUpdateKeyValuePairs);
            }
        }

        private void ParseDAXMICStatus(uint stream_id, string statusUpdateKeyValuePairs)
        {
            DAXMICAudioStream micAudioStream = FindDAXMICAudioStreamByStreamID(stream_id);
            if (micAudioStream == null)
            {
                // create a audio rx stream if one has not yet been created
                micAudioStream = new DAXMICAudioStream(this);
                micAudioStream.StreamID = stream_id;

                // We have added a brand new audio rx stream, so add it to our collection.
                AddDAXMICAudioStream(micAudioStream);
            }

            // slice=<slc> dax_clients=0 client_handle=<handle> 
            micAudioStream.StatusUpdate(statusUpdateKeyValuePairs);
        }

        private void ParseRemoteAudioTXStatus(uint stream_id, string statusUpdateKeyValuePairs)
        {
            bool addNewRemoteAudioTX = false;
            TXRemoteAudioStream remoteAudioTX = FindTXRemoteAudioStreamByStreamID(stream_id);

            if (remoteAudioTX == null)
            {
                //create an opus_stream if one has not yet been created
                addNewRemoteAudioTX = true;
                remoteAudioTX = new TXRemoteAudioStream(this);
                remoteAudioTX.StreamID = stream_id;
            }

            // compression=<none|opus> client_handle=<handle>
            remoteAudioTX.StatusUpdate(statusUpdateKeyValuePairs);
            if (addNewRemoteAudioTX)
            {
                // We have added a brand new opus stream, so add it to our collection.
                // Today, there will only be 0 or 1 items in this collection.
                AddTXRemoteAudioStream(remoteAudioTX);
            }
        }

        private void ParseRXRemoteAudioStreamStatus(uint stream_id, string statusUpdateKeyValuePairs)
        {
            bool addNewRemoteAudioRX = false;
            RXRemoteAudioStream remoteAudioRX = FindRXRemoteAudioStreamByStreamID(stream_id);
            if (remoteAudioRX == null)
            {
                // create a remote audio rx stream if one has nto yet been created
                addNewRemoteAudioRX = true;
                remoteAudioRX = new RXRemoteAudioStream(this);
                remoteAudioRX.StreamID = stream_id;
            }

            // compression=<none|opus> client_handle=<handle>
            remoteAudioRX.StatusUpdate(statusUpdateKeyValuePairs);

            if (addNewRemoteAudioRX)
            {
                // We have added a brand new remote audio rx stream, so add it to our collection.
                // Today, there will only be 0 or 1 items in this collection.
                AddRXRemoteAudioStream(remoteAudioRX);
            }
        }

        private void ParseRadioStatus(string s)
        {
            string[] words = s.Split(' ');

            if (s.Contains("filter_sharpness"))
            {
                ParseFilterSharpnessStatus(s.Substring("filter_sharpness ".Length)); // "filter_sharpness "
            }
            else if (s.Contains("static_net_params"))
            {
                ParseNetParamsStatus(s.Substring("static_net_params ".Length));
            }
            else if (s.StartsWith("oscillator"))
            {
                //radio oscillator state=%s setting=%s locked=%d ext_present=%d gpsdo_present=%d tcxo_present=%d"
                ParseOscillatorStatus(s.Substring("oscillator ".Length));
            }
            else if (s.StartsWith("xmas_tree"))
            {
                ChristmasTreeMode = !ChristmasTreeMode;
            }
            else
            {

                foreach (string kv in words)
                {
                    string[] tokens = kv.Split('=');
                    if (tokens.Length != 2)
                    {
                        if (!string.IsNullOrEmpty(kv)) Debug.WriteLine($"Radio::ParseRadioStatus: Invalid key/value pair ({kv})");
                        continue;
                    }

                    string key = tokens[0];
                    string value = tokens[1];

                    switch (key.ToLower())
                    {
                        case "backlight":
                            {
                                bool b = int.TryParse(value, out _backlight);
                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus: Invalid backlight value (" + kv + ")");
                                    continue;
                                }

                                RaisePropertyChanged("Backlight");
                            }
                            break;

                        case "callsign":
                            {
                                _callsign = value;
                                RaisePropertyChanged("Callsign");
                            }
                            break;

                        case "daxiq_available":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - daxiq_available: Invalid value (" + kv + ")");
                                    continue;
                                }

                                DAXIQAvailable = temp;
                            }
                            break;

                        case "daxiq_capacity":
                            {
                                int temp;
                                bool b = int.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - daxiq_capacity: Invalid value (" + kv + ")");
                                    continue;
                                }

                                DAXIQCapacity = temp;
                            }
                            break;

                        case "full_duplex_enabled":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - Full Duplex Enabled: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _fullDuplexEnabled = Convert.ToBoolean(temp);
                                RaisePropertyChanged("FullDuplexEnabled");
                            }
                            break;

                        case "enforce_private_ip_connections":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - Enforce Private IP Connections: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _enforcePrivateIPConnections = Convert.ToBoolean(temp);
                                RaisePropertyChanged("EnforcePrivateIPConnections");
                            }
                            break;

                        case "front_speaker_mute":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - Front Speaker Mute: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _frontSpeakerMute = Convert.ToBoolean(temp);
                                RaisePropertyChanged("FrontSpeakerMute");
                            }
                            break;

                        case "headphone_gain":
                            {
                                bool b = int.TryParse(value, out _headphoneGain);
                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus: Invalid headphone_gain value (" + kv + ")");
                                    continue;
                                }

                                RaisePropertyChanged("HeadphoneGain");
                            }
                            break;

                        case "headphone_mute":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - headphone_mute: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _headphoneMute = Convert.ToBoolean(temp);
                                RaisePropertyChanged("HeadphoneMute");
                            }
                            break;

                        case "lineout_gain":
                            {
                                bool b = int.TryParse(value, out _lineoutGain);
                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus: Invalid lineout_gain value (" + kv + ")");
                                    continue;
                                }

                                RaisePropertyChanged("LineoutGain");
                            }
                            break;

                        case "lineout_mute":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - lineout_mute: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _lineoutMute = Convert.ToBoolean(temp);
                                RaisePropertyChanged("LineoutMute");
                            }
                            break;

                        case "external_pa_allowed":
                            {
                                if (!byte.TryParse(value, out var temp))
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - external_pa_allowed: Invalid value (" + kv + ")");
                                    continue;
                                }
                                ExternalPaAllowed = Convert.ToBoolean(temp);
                                break;
                            }

                        case "max_internal_pa_power":
                            {
                                if (!int.TryParse(value, out var temp))
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - invalid key-value (" + kv + ")");
                                    continue;
                                }
                                MaxInternalPaPowerWatts = temp;
                                break;
                            }

                        case "nickname":
                            {
                                _nickname = value;
                                RaisePropertyChanged("Nickname");
                            }
                            break;

                        case "panadapters":
                            {
                                bool b = int.TryParse(value, out _panadaptersRemaining);
                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus: Invalid panadapter value (" + kv + ")");
                                    continue;
                                }

                                RaisePropertyChanged("PanadaptersRemaining");
                            }
                            break;

                        case "pll_done":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - pll_done: Invalid value (" + kv + ")");
                                    continue;
                                }

                                // enable the PLL Start button again once the pll is done
                                if (Convert.ToBoolean(temp))
                                {
                                    _startOffsetEnabled = Convert.ToBoolean(temp);
                                    RaisePropertyChanged("StartOffsetEnabled");
                                }
                            }
                            break;

                        case "remote_on_enabled":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - remote_on_enabled: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _remoteOnEnabled = Convert.ToBoolean(temp);
                                RaisePropertyChanged("RemoteOnEnabled");
                            }
                            break;

                        case "rtty_mark_default":
                            {
                                bool b = int.TryParse(value, out _rttyMarkDefault);
                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus: Invalid rtty_mark_default value (" + kv + ")");
                                    continue;
                                }

                                RaisePropertyChanged("RTTYMarkDefault");
                            }
                            break;

                        case "slices":
                            {
                                bool b = int.TryParse(value, out _slicesRemaining);
                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus: Invalid slice value (" + kv + ")");
                                    continue;
                                }

                                RaisePropertyChanged("SlicesRemaining");
                            }
                            break;

                        case "cal_freq":
                            {
                                bool b = StringHelper.TryParseDouble(value, out _calFreq);
                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus: Invalid calFreq value (" + kv + ")");
                                    continue;
                                }

                                RaisePropertyChanged("CalFreq");
                            }
                            break;

                        case "freq_error_ppb":
                            {
                                bool b = int.TryParse(value, out _freqErrorPPB);
                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus: Invalid freqErrorPPB value (" + kv + ")");
                                    continue;
                                }

                                RaisePropertyChanged("FreqErrorPPB");
                            }
                            break;

                        case "tnf_enabled":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - tnf_enabled: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _tnfEnabled = Convert.ToBoolean(temp);
                                RaisePropertyChanged("TNFEnabled");
                            }
                            break;

                        case "binaural_rx":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - binaural_rx: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _binauralRX = Convert.ToBoolean(temp);
                                RaisePropertyChanged("BinauralRX");
                            }
                            break;

                        case "mute_local_audio_when_remote":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - mute_local_audio_when_remote: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _isMuteLocalAudioWhenRemoteOn = Convert.ToBoolean(temp);
                                RaisePropertyChanged("IsMuteLocalAudioWhenRemoteOn");
                            }
                            break;

                        case "importing":
                            {
                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - importing: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _databaseImportComplete = !Convert.ToBoolean(temp);
                                RaisePropertyChanged("DatabaseImportComplete");
                            }
                            break;

                        case "unity_tests_complete":
                            UnityResultsImportComplete = true;
                            break;

                        case "alpha":
                            {
                                if (value == "1")
                                    _isAlphaLicensed = true;
                                else
                                    _isAlphaLicensed = false;
                                RaisePropertyChanged("IsAlphaLicensed");
                            }
                            break;

                        case "low_latency_digital_modes":
                            {
                                // "radio low_latency_digital_modes=1|0"

                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - low_latency_digital_modes: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _lowLatencyDigitalModes = Convert.ToBoolean(temp);
                                RaisePropertyChanged("LowLatencyDigitalModes");
                            }
                            break;
                        case "mf_enable":
                            {
                                // "radio mf_enable=1|0"

                                byte temp;
                                bool b = byte.TryParse(value, out temp);

                                if (!b)
                                {
                                    Debug.WriteLine("Radio::ParseRadioStatus - mf_enable: Invalid value (" + kv + ")");
                                    continue;
                                }

                                _multiFlexEnabled = Convert.ToBoolean(temp);
                                RaisePropertyChanged("MultiFlexEnabled");
                            }
                            break;
                        case "auto_save":
                            {
                                // "radio auto_save=1|0"
                                if(false == byte.TryParse(value, out var temp))
                                {
                                    Debug.WriteLine($"Radio::ParseRadioStatus - auto_save: Invalid value ({kv})");
                                    continue;
                                }

                                _profileAutoSave = Convert.ToBoolean(temp);
                                RaisePropertyChanged("ProfileAutoSave");
                            }
                            break;
                    }
                }
            }
        }

        private void ParseOscillatorStatus(string s)
        {
            string[] words = s.Split(' ');


            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("ParseOscillatorStatus: Invalid key/value pair (" + kv + ")");
                    continue;
                }
                string key = tokens[0];
                string value = tokens[1];

                switch (key)
                {
                    case "state":
                        _oscillatorState = value;
                        RaisePropertyChanged("OscillatorState");
                        break;
                    case "setting":
                        {
                            Oscillator temp;
                            bool b = Enum.TryParse(value.ToLower(), out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseOscillatorStatus: Invalid value (" + s + ")");
                                continue;
                            }

                            _selectedOscillator = temp;
                            RaisePropertyChanged("SelectedOscillator");
                        }
                        break;
                    case "locked":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseOscillatorStatus - locked: Invalid value (" + kv + ")");
                                continue;
                            }

                            _isOscillatorLocked = Convert.ToBoolean(temp);
                            RaisePropertyChanged("IsOscillatorLocked");
                        }
                        break;
                    case "ext_present":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseOscillatorStatus - ext_present: Invalid value (" + kv + ")");
                                continue;
                            }

                            _isExternalOscillatorPresent = Convert.ToBoolean(temp);
                            RaisePropertyChanged("IsExternalOscillatorPresent");
                        }
                        break;
                    case "gnss_present":
                        {
                            bool b = byte.TryParse(value, out byte temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseOscillatorStatus - gnss_present: Invalid value (" + kv + ")");
                                continue;
                            }

                            IsGnssPresent = Convert.ToBoolean(temp);
                            RaisePropertyChanged(nameof(IsGnssPresent));
                        }
                        break;
                    case "gpsdo_present":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseOscillatorStatus - gpsdo_present: Invalid value (" + kv + ")");
                                continue;
                            }

                            _isGpsdoPresent = Convert.ToBoolean(temp);
                            RaisePropertyChanged("IsGpsdoPresent");
                        }
                        break;
                    case "tcxo_present":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseOscillatorStatus - tcxo_present: Invalid value (" + kv + ")");
                                continue;
                            }

                            _isTcxoPresent = Convert.ToBoolean(temp);
                            RaisePropertyChanged("IsTcxoPresent");
                        }
                        break;
                }
            }
        }

        private void ParseMessage(string s)
        {
            string[] tokens = s.Split('|');
            if (tokens.Length != 2)
            {
                Debug.WriteLine("ParseMessage: Invalid message -- min 2 tokens (" + s + ")");
                return;
            }

            uint num;
            bool b = StringHelper.TryParseInteger("0x" + tokens[0].Substring(1), out num);
            if (!b)
            {
                Debug.WriteLine("ParseMessage: Invalid message number (" + s + ")");
                return;
            }

            MessageSeverity severity = (MessageSeverity)((num >> 24) & 0x3);
            OnMessageReceived(severity, tokens[1]);
        }

        /// <summary>
        /// Delegate event handler for the MessageReceived event
        /// </summary>
        /// <param name="severity">The message severity </param>
        /// <param name="msg">The message being received</param>
        public delegate void MessageReceivedEventHandler(MessageSeverity severity, string msg);
        /// <summary>
        /// This event is raised when the radio receives a message from the client
        /// </summary>
        public event MessageReceivedEventHandler MessageReceived;

        private void OnMessageReceived(MessageSeverity severity, string msg)
        {
            if (MessageReceived != null)
                MessageReceived(severity, msg);
        }

        private void ParseHandle(string s)
        {
            // typical string: HABC123DE
            if (s.Length <= 1) return;
            string handle_str = s.Substring("H".Length);

            uint handle_uint;
            bool b = uint.TryParse(handle_str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handle_uint);

            if (!b) return;

            _clientHandle = handle_uint;
            RaisePropertyChanged("ClientHandle");
        }

        private void ParseProtocolVersion(string s)
        {
            if (s.Length <= 1) return;
            FlexVersion.TryParse(s.Substring(1), out _protocol_version);
            if (_protocol_version < _min_protocol_version || _protocol_version > _max_protocol_version)
            {
                // OnMessageReceived probably was not the right thing to do here?  Need to revist this
                Debug.WriteLine("*****Protocol not supported!  _protocol_version = 0x" + _protocol_version.ToString("X") + ", _min_protocol_version = " + _min_protocol_version + ", _max_protocol_version 0x = " + _max_protocol_version.ToString("X"));
                OnMessageReceived(MessageSeverity.Fatal, "Protocol Not Supported (" + Util.FlexVersion.ToString(_protocol_version) + ")");
            }
        }

        #endregion

        #region Command Routines

        private int _cmdSequenceNumber = 0;
        private int GetNextSeqNum()
        {
            return Interlocked.Increment(ref _cmdSequenceNumber);
        }

        internal int SendCommand(int seq_num, string s)
        {
            if (!_commandCommunication.IsConnected)
            {
                // TODO: handle disconnect
                return 0;
            }

            //Debug.WriteLine("SendCommand: " + seq_num + ": " + s);

            /*if (!connected)
            {
                bool b = Connect();
                if (!b) return 0;
            }*/

            string msg_type = "C";
            if (Verbose) msg_type = "CD";

            string seq = seq_num.ToString();

            if (!s.EndsWith("\n") && !s.EndsWith("\r"))
                s = s + "\n";

            string msg = msg_type + seq + "|" + s;

#if TIMING
            CmdTime cmd_time = new CmdTime();
            cmd_time.Command = s;
            cmd_time.Sequence = seq_num;
            t1.Stop();
            cmd_time.Start = t1.Elapsed.TotalSeconds;

            cmd_time_table.Add(seq_num, cmd_time);
#endif

            _commandCommunication.Write(msg);
            _countTXCommand += msg.Length + TCP_HEADER_SIZE;

            return seq_num;
        }

        internal int SendCommand(string s)
        {
            return SendCommand(GetNextSeqNum(), s);
        }

        internal int SendReplyCommand(int seq_num, ReplyHandler handler, string s)
        {
            if (!_commandCommunication.IsConnected)
            {
                // TODO: handle disconnect
                return 0;
            }

            // add the sender to the reply queue with the sequence number
            lock (_replyTable)
                _replyTable.Add(seq_num, handler);

            return SendCommand(seq_num, s);
        }

        internal int SendReplyCommand(ReplyHandler handler, string s)
        {
            return SendReplyCommand(GetNextSeqNum(), handler, s);
        }
        
        public Task<string> SendCommandAsync(string message)
        {
            var tcs = new TaskCompletionSource<string>();

            SendReplyCommand(CommandCompleted, message);

            return tcs.Task;

            void CommandCompleted(int sequence, uint responseValue, string info)
            {
                if (responseValue != 0)
                {
                    tcs.TrySetException(new SmartSdrCommandErrorException(info, responseValue));
                    return;
                }

                tcs.TrySetResult(info);
            }
        }

        #endregion

        #region TNF Routines

        private bool _isTNFSubscribed = false;
        public bool IsTNFSubscribed
        {
            get { return _isTNFSubscribed; }
            set
            {
                if (_isTNFSubscribed != value)
                {
                    _isTNFSubscribed = value;
                    if (_connected)
                    {
                        if (_isTNFSubscribed)
                            SendCommand("sub tnf all");
                        else
                            SendCommand("unsub tnf all");

                    }
                }
            }
        }

        private bool _tnfEnabled;
        public bool TNFEnabled
        {
            get { return _tnfEnabled; }
            set
            {
                if (_tnfEnabled != value)
                {
                    _tnfEnabled = value;
                    SendCommand("radio set tnf_enabled=" + _tnfEnabled);
                    RaisePropertyChanged("TNFEnabled");
                }
            }
        }

        private TNF FindTNFById(uint id)
        {
            lock (_tnfs)
                return _tnfs.FirstOrDefault(x => x.ID == id);
        }

        internal void AddTNF(TNF tnf)
        {
            lock (_tnfs)
            {
                if (_tnfs.Contains(tnf)) return;
                _tnfs.Add(tnf);
            }

            OnTNFAdded(tnf);
        }

        internal void RemoveTNF(TNF tnf)
        {
            lock (_tnfs)
            {
                if (!_tnfs.Contains(tnf)) return;
                _tnfs.Remove(tnf);
            }

            OnTNFRemoved(tnf);
        }

        private void RemoveAllTNFs()
        {
            lock (_tnfs)
            {
                while (_tnfs.Count > 0)
                {
                    TNF tnf = _tnfs[0];
                    _tnfs.Remove(tnf);
                    OnTNFRemoved(tnf);
                }
            }
        }

        /// <summary>
        /// Removes all TNFs from the radio by closing each one
        /// </summary>
        public void CloseAllTNFs()
        {
            lock (_tnfs)
            {
                while (_tnfs.Count > 0)
                {
                    TNF tnf = _tnfs[0];
                    tnf.Close();
                }
            }
        }

        public delegate void TNFRemovedEventHandler(TNF tnf);
        public event TNFRemovedEventHandler TNFRemoved;
        private void OnTNFRemoved(TNF tnf)
        {
            if (TNFRemoved != null)
                TNFRemoved(tnf);
        }

        public delegate void TNFAddedEventHandler(TNF tnf);
        public event TNFAddedEventHandler TNFAdded;

        internal void OnTNFAdded(TNF tnf)
        {
            if (TNFAdded != null)
                TNFAdded(tnf);
        }

        public void RequestTNF(double freq, uint panID)
        {
            if (freq == 0)
            {
                Panadapter pan = FindPanadapterByStreamID(panID);
                if (pan == null)
                    return;

                Slice target_slice = null;
                double freq_diff = 1000;

                lock (_slices)
                {
                    foreach (Slice s in _slices)
                    {
                        if (s.PanadapterStreamID == panID)
                        {
                            double diff = Math.Abs(s.Freq - pan.CenterFreq);
                            if (diff < freq_diff)
                            {
                                freq_diff = diff;
                                target_slice = s;
                            }
                        }
                    }
                }

                if (target_slice == null)
                {
                    freq = pan.CenterFreq;
                }
                else
                {
                    switch (target_slice.DemodMode)
                    {
                        case "LSB":
                        case "DIGL":
                            {
                                freq = target_slice.Freq + (((target_slice.FilterLow - target_slice.FilterHigh) / 2.0) * 1e-6);
                            }
                            break;
                        case "RTTY":
                            {
                                freq = target_slice.Freq - (target_slice.RTTYShift / 2.0 * 1e-6);
                            }
                            break;
                        case "CW":
                        case "AM":
                        case "SAM":
                        case "AME":
                            {
                                freq = target_slice.Freq + (((target_slice.FilterHigh / 2.0) * 1e-6));
                            }
                            break;

                        case "USB":
                        case "DIGU":
                        case "FDV":
                        default:
                            {
                                freq = target_slice.Freq + (((target_slice.FilterHigh - target_slice.FilterLow) / 2.0) * 1e-6);
                            }
                            break;
                    }
                }
            }

            SendCommand("tnf create freq=" + StringHelper.DoubleToString(freq, "f6"));
        }

        #endregion

        #region Spot Routines

        /// <summary>
        /// Creates a new Spot on the radio
        /// </summary>
        /// <param name="spot">The Spot object to create on the radio</param>
        public void RequestSpot(Spot spot)
        {
            string cmd = "spot add callsign=" + spot.Callsign.Replace(' ', '\u007f') + " rx_freq=" + StringHelper.DoubleToString(spot.RXFrequency, "f6");
            if (spot.TXFrequency > 0.0) cmd += " tx_freq=" + StringHelper.DoubleToString(spot.TXFrequency, "f6");
            if (spot.Mode != null) cmd += " mode=" + spot.Mode;
            if (spot.Color != null) cmd += " color=" + spot.Color;
            if (spot.BackgroundColor != null) cmd += " background_color=" + spot.BackgroundColor;
            if (spot.Source != null) cmd += " source=" + spot.Source;
            if (spot.SpotterCallsign != null) cmd += " spotter_callsign=" + spot.SpotterCallsign;
            if (spot.Timestamp != null) cmd += " timestamp=" + spot.DateTimeToUnixTimestamp(spot.Timestamp);
            if (spot.LifetimeSeconds > 0) cmd += " lifetime_seconds=" + spot.LifetimeSeconds;
            if (spot.Comment != null) cmd += " comment=" + spot.Comment.Replace(' ', '\u007f');
            if (spot.Priority != 0) cmd += " priority=" + spot.Priority;
            if (spot.TriggerAction != null) cmd += " trigger_action=" + spot.TriggerAction;

            SendCommand(cmd);
        }

        private void ParseSpotStatus(string status)
        {
            string[] words = status.Split(' ');

            if (words.Length < 2)
            {
                Debug.WriteLine("Radio::ParseSpotStatus: Error parsing spot status -- too few words (" + status + ")");
                return;
            }

            // parse the index -- should be the first value with the 'spot' has already been stripped by now
            int index;
            bool b = int.TryParse(words[0], out index);
            if (!b)
            {
                Debug.WriteLine("ParseSpotStatus: Invalid index (" + words[0] + ")");
                return;
            }

            // if we make it to here, we have a good spot index

            // Find a reference to the Spot using the index (assuming it exists)
            Spot spot = FindSpotByIndex(index);
            bool spot_added = false;

            // do we need to add the spot?
            if (spot == null)
            {
                // yes -- make sure that we aren't going to add the Spot just to need to remove it
                if (words[1] == "removed") return;

                // create the spot and populate the fields
                spot = new Spot(this, index);
                spot_added = true;
            }

            // if we made it this far, we have a good reference to a Spot

            // is the spot being removed?
            if (words[1] == "removed")
            {
                // yes -- remove it
                RemoveSpot(spot);
                return;
            }

            // pass along the status message to be parsed within the Spot class
            spot.StatusUpdate(status.Substring(words[0].Length + 1));

            // was a Spot added?
            if (spot_added)
            {
                // yes -- make sure to add it to our list
                AddSpot(spot);
            }
        }

        private void AddSpot(Spot spot)
        {
            if (String.IsNullOrEmpty(spot.Callsign))    // drop a spot w/o a callsign on the floor,
                return;                                 // it's useless and causes a null pointer exception

            lock (_spots)
            {
                if (_spots.Contains(spot)) return;
                _spots.Add(spot);
            }

            OnSpotAdded(spot);
        }

        private void RemoveSpot(Spot spot)
        {
            lock (_spots)
            {
                if (!_spots.Contains(spot)) return;
                _spots.Remove(spot);
            }

            OnSpotRemoved(spot);
        }

        private Spot FindSpotByIndex(int index)
        {
            lock (_spots)
                return _spots.FirstOrDefault(x => x.Index == index);
        }

        public void RemoveSpot(string callsign, double rx_freq)
        {
            const double SPOT_REMOVAL_FREQ_TOL = 0.01; // 10 kHz

            lock (_spots)
            {
                foreach (Spot s in _spots.Where(x => x.Callsign == callsign))
                {
                    if (Math.Abs(rx_freq - s.RXFrequency) <= SPOT_REMOVAL_FREQ_TOL)
                        s.Remove();
                }
            }
        }

        public void ClearAllSpots()
        {
            SendCommand("spot clear");
        }

        /// <summary>
        /// Delegate event handler for the SpotAdded event
        /// </summary>
        /// <param name="spot"></param>
        public delegate void SpotAddedEventHandler(Spot spot);
        /// <summary>
        /// This event is raised when a new Spot has been added to the radio
        /// </summary>
        public event SpotAddedEventHandler SpotAdded;

        internal void OnSpotAdded(Spot spot)
        {
            if (SpotAdded != null)
                SpotAdded(spot);
        }

        /// <summary>
        /// Delegate event handler for the SpotRemoved event
        /// </summary>
        /// <param name="spot"></param>
        public delegate void SpotRemovedEventHandler(Spot spot);
        /// <summary>
        /// This event is raised when a Spot is removed from the radio
        /// </summary>
        public event SpotRemovedEventHandler SpotRemoved;

        private void OnSpotRemoved(Spot spot)
        {
            if (SpotRemoved != null)
                SpotRemoved(spot);
        }

        /// <summary>
        /// Delegate event handler for the SpotTriggered event
        /// </summary>
        /// <param name="spot">The Spot that was triggered</param>
        public delegate void SpotTriggeredEventHandler(Spot spot);
        /// <summary>
        /// This event is raised when a Spot is Triggered (clicked)
        /// </summary>
        public event SpotTriggeredEventHandler SpotTriggered;

        /// <summary>
        /// Delegate event handler for the SpotTriggered event
        /// </summary>
        /// <param name="spot">The Spot that was triggered</param>
        /// <param name="pan">The Panadapter on which the Spot was triggered</param>
        public delegate void SpotTriggeredWithPanEventHandler(Spot spot, Panadapter pan);
        /// <summary>
        /// This event is raised when a Spot is Triggered (clicked)
        /// </summary>
        public event SpotTriggeredWithPanEventHandler SpotTriggeredWithPan;

        internal void OnSpotTriggered(Spot spot, Panadapter pan)
        {
            if (SpotTriggered != null)
                SpotTriggered(spot);

            if (SpotTriggeredWithPan != null)
                SpotTriggeredWithPan(spot, pan);
        }

        #endregion

        #region TX Band Settings Routines
        private void ParseTxBandSettingsStatus(string status)
        {
            //band_id %d rfpower=%d tunepower=%d hwalc_enabled=%d"
            string[] words = status.Split(' ');
            if (words.Length < 2)
            {
                Debug.WriteLine($"Radio::ParseTxBandSettingsStatus: Not enough arguments: {status}");
                return;
            }

            string band_id_str = words[1];
            int bandId;
            bool b = int.TryParse(band_id_str, out bandId);

            if (!b)
            {
                Debug.WriteLine($"Radio::ParseTxBandSettingsStatus: Error parsing band_id: {status}");
                return;
            }

            if (words.Length == 3 && words[2] == "removed")
            {
                TxBandSettings txBandSettingsToRemove;
                lock (_txBandSettingsList)
                {
                    txBandSettingsToRemove = _txBandSettingsList.FirstOrDefault(s => s.BandId == bandId);
                    if (txBandSettingsToRemove != null)
                    {
                        _txBandSettingsList.Remove(txBandSettingsToRemove);
                    }
                    else
                    {
                        Debug.WriteLine($"Radio::ParseTxBandSettingsStatus: Error removing band, band_id not found: {status}");
                    }
                }

                if (txBandSettingsToRemove != null)
                {
                    OnTxBandSettingsRemoved(txBandSettingsToRemove);
                }

                return;
            }


            List<string> keyValuePairs = words.Skip(2).ToList();    //skip band_id <band_id> to get all key-value pairs

            // Find TxBandSettings that this corresponds to.  
            // If one does not exist for this band, create one.

            TxBandSettings foundTxBandSettings;

            lock (_txBandSettingsList)
            {
                foundTxBandSettings = _txBandSettingsList.FirstOrDefault(s => s.BandId == bandId);
            }

            if (foundTxBandSettings == null)
            {
                // No existing band settings have been found for this band, 
                // so create a new one and populate its elements.
                TxBandSettings newTxBandSettings = new TxBandSettings(this, bandId);
                newTxBandSettings.ParseStatusKeyValuePairs(keyValuePairs);

                lock (_txBandSettingsList)
                {
                    _txBandSettingsList.Add(newTxBandSettings);
                }

                OnTxBandSettingsAdded(newTxBandSettings);
            }
            else
            {
                // An existing TX band settings model has been found.
                // Update the properties for this model.
                foundTxBandSettings.ParseStatusKeyValuePairs(keyValuePairs);
            }
        }

        /// <summary>
        /// Delegate event handler for the TxBandSettingsAddedEventHandler event
        /// </summary>
        /// <param name="txBandSettings"></param>
        public delegate void TxBandSettingsAddedEventHandler(TxBandSettings txBandSettings);
        /// <summary>
        /// This event is raised when a new TxBandSettings has been added to the radio
        /// </summary>
        public event TxBandSettingsAddedEventHandler TxBandSettingsAdded;

        internal void OnTxBandSettingsAdded(TxBandSettings txBandSettings)
        {
            if (TxBandSettingsAdded != null)
                TxBandSettingsAdded(txBandSettings);
        }

        /// <summary>
        /// Delegate event handler for the TxBandSettingsRemovedEventHandler event
        /// </summary>
        /// <param name="txBandSettings"></param>
        public delegate void TxBandSettingsRemovedEventHandler(TxBandSettings txBandSettings);
        /// <summary>
        /// This event is raised when a new TxBandSettings has been added to the radio
        /// </summary>
        public event TxBandSettingsRemovedEventHandler TxBandSettingsRemoved;

        internal void OnTxBandSettingsRemoved(TxBandSettings txBandSettings)
        {
            if (TxBandSettingsRemoved != null)
                TxBandSettingsRemoved(txBandSettings);
        }



        #endregion

        #region USB Cable Routines

        internal void AddUsbCable(UsbCable cable)
        {
            lock (_usbCables)
            {
                if (_usbCables.Contains(cable)) return;
                _usbCables.Add(cable);
                OnUsbCableAdded(cable);
            }
        }

        internal void RemoveUsbCable(UsbCable cable)
        {
            lock (_usbCables)
            {
                if (!_usbCables.Contains(cable)) return;

                _usbCables.Remove(cable);
                OnUsbCableRemoved(cable);
            }
        }

        public delegate void UsbCableRemovedEventHandler(UsbCable cable);
        public event UsbCableRemovedEventHandler UsbCableRemoved;
        private void OnUsbCableRemoved(UsbCable cable)
        {
            if (UsbCableRemoved != null)
                UsbCableRemoved(cable);
        }

        public delegate void UsbCableAddedEventHandler(UsbCable cable);
        public event UsbCableAddedEventHandler UsbCableAdded;

        internal void OnUsbCableAdded(UsbCable cable)
        {
            if (UsbCableAdded != null)
                UsbCableAdded(cable);
        }

        private UsbCable FindUsbCableBySN(string sn)
        {
            lock (_usbCables)
            {
                foreach (UsbCable cable in _usbCables)
                {
                    if (cable.SerialNumber == sn)
                    {
                        return cable;
                    }
                }
            }
            return null;
        }

        private void ParseUsbCableStatus(string s)
        {
            // split the status message on spaces
            string[] words = s.Split(' ');

            // if there isn't at least a serial number and another piece of data, we're done
            if (words.Length < 2) return;

            // the first word should be the serial number -- use this to find a reference to the UsbCable object
            string serialNumber = words[0];
            UsbCable cable = FindUsbCableBySN(serialNumber);

            bool cable_added = false;

            if (cable != null && s.Contains("removed"))
            {
                RemoveUsbCable(cable);
                return;
            }

            // was a good reference found?
            if (cable == null)
            {
                cable_added = true;

                // what type of cable is this?
                if (s.Contains("type=bit")) //
                    cable = new UsbBitCable(this, serialNumber);
                else if (s.Contains("type=cat"))
                    cable = new UsbCatCable(this, serialNumber);
                else if (s.Contains("type=bcd_vbcd")) //This should be checked before "type=bcd" to avoid collision.
                    cable = new UsbBcdCable(this, serialNumber, "bcd_vbcd");
                else if (s.Contains("type=vbcd"))
                    cable = new UsbBcdCable(this, serialNumber, "vbcd");
                else if (s.Contains("type=bcd"))
                    cable = new UsbBcdCable(this, serialNumber, "bcd");
                else if (s.Contains("type=ldpa"))
                    cable = new UsbLdpaCable(this, serialNumber);
                else if (s.Contains("type=passthrough"))
                    cable = new UsbPassthroughCable(this, serialNumber);
                else if (s.Contains("type=invalid"))
                    cable = new UsbOtherCable(this, serialNumber, UsbCableType.Invalid);
            }

            // if we don't have a good reference by now, this status is a bust, quit now
            if (cable == null) return;

            cable.ParseStatus(s.Substring(words[0].Length + 1));

            if (cable_added)
            {
                AddUsbCable(cable);
            }
        }

        #endregion

        public void RequestPanafall()
        {
            SendCommand("display panafall create x=100 y=100");
        }

        public void RequestDAXRXAudioStream(int channel)
        {
            SendCommand("stream create type=dax_rx dax_channel=" + channel);
        }

        public void RequestDAXMICAudioStream()
        {
            SendCommand("stream create type=dax_mic");
        }

        public void RequestDAXTXAudioStream()
        {
            SendCommand("stream create type=dax_tx");
        }

        public void RequestDAXIQStream(int channel)
        {
            // stream create type=dax_iq pan=<panadapter> rate=<rate>
            SendCommand("stream create type=dax_iq daxiq_channel=" + channel);
        }

        public void RequestXvtr()
        {
            SendCommand("xvtr create");
        }

        private AutoResetEvent _requestSliceBlockingARE = new AutoResetEvent(false);
        private int _requestSliceBlockingIndex = -1;
        public Slice RequestSliceBlocking(Panadapter pan, double freq = 0.0, string rxant = "", string mode = "", bool load_persistence = false)
        {
            string cmd = "slice create ";
            if (pan != null) cmd += " pan=0x" + pan.StreamID.ToString("X");
            if (freq != 0.0) cmd += " freq=" + StringHelper.DoubleToString(freq, "f6");
            if (rxant != null && rxant != "") cmd += " rxant=" + rxant;
            if (mode != null && mode != "") cmd += " mode=" + mode;
            if (load_persistence) cmd += " load_from=PERSISTENCE";
            SendReplyCommand(new ReplyHandler(HandleRequestSliceBlockingReply), cmd);

            _requestSliceBlockingARE.WaitOne(5000);

            // store the Slice index
            int index = _requestSliceBlockingIndex;

            // reset the Slice index
            _requestSliceBlockingIndex = -1;

            // Find the related Slice and return a reference
            return FindSliceByIndex(index);
        }

        public Slice RequestCloneSliceBlocking(Slice slice)
        {
            string cmd = String.Format("slice create clone_slice={0} pan=0x{1} load_from=clone", slice.Index, slice.PanadapterStreamID.ToString("X"));
            SendReplyCommand(new ReplyHandler(HandleRequestSliceBlockingReply), cmd);

            _requestSliceBlockingARE.WaitOne(5000);

            // store the Slice index
            int index = _requestSliceBlockingIndex;

            // reset the Slice index
            _requestSliceBlockingIndex = -1;

            // Find the related Slice and return a reference
            return FindSliceByIndex(index);
        }

        private void HandleRequestSliceBlockingReply(int seq, uint resp_val, string s)
        {
            // Response should be in the format R<seq>|<response value>|<slice index>|<debug info>
            if (resp_val == 0)
            {
                // clear out any debug output.  
                if (s.Contains("|"))
                    s = s.Substring(0, s.IndexOf("|"));

                if (s.Length > 0)
                {
                    int index;
                    bool b = int.TryParse(s, out index);

                    if (b) _requestSliceBlockingIndex = index;
                }
            }

            _requestSliceBlockingARE.Set();
        }

        #region Slice Routines

        /// <summary>
        /// Creates a new slice on the radio
        /// </summary>
        /// <param name="pan">The Panadapter object on which to add the slice</param>
        /// <param name="demod_mode">The demodulation mode of this slice: "USB", "DIGU",
        /// "LSB", "DIGL", "CW", "DSB", "AM", "SAM", "FM"</param>
        /// <returns>The Slice object</returns>
        public void RequestSlice(Panadapter pan, string demod_mode = "", double freq = 0.0, string rx_ant = "", bool load_persistence = false)
        {
            string cmd = "slice create ";
            if (pan != null) cmd += " pan=0x" + pan.StreamID.ToString("X");
            if (freq != 0.0) cmd += " freq=" + StringHelper.DoubleToString(freq, "f6");
            if (rx_ant != null && rx_ant != "") cmd += " rxant=" + rx_ant;
            if (demod_mode != null && demod_mode != "") cmd += " mode=" + demod_mode;
            if (load_persistence) cmd += " load_from=PERSISTENCE";

            SendCommand(cmd);
        }

        /// <summary>
        /// Creates a new Slice on the radio on a new Pandapter
        /// </summary>
        public void RequestSlice()
        {
            SendCommand("slice create");
        }

        /// <summary>
        /// Find a Slice object by index number
        /// </summary>
        /// <param name="index">The index number for the Slice</param>
        /// <returns>The Slice object</returns>
        public Slice FindSliceByIndex(int index)
        {
            lock (_slices)
                return _slices.FirstOrDefault(slc => slc.Index == index);
        }

        public Slice FindSliceByLetter(string slice_letter, uint gui_client_handle)
        {
            if (string.IsNullOrEmpty(slice_letter)) return null;

            lock (_slices)
            {
                return _slices.FirstOrDefault(s => s.Letter == slice_letter && s.ClientHandle == gui_client_handle);
            }
        }

        /// <summary>
        /// Find a Slice by the DAX Channel number
        /// </summary>
        /// <param name="dax_channel">The DAX Channel number of the slice</param>
        /// <returns>The Slice object</returns>
        public Slice FindSliceByDAXChannel(int dax_channel)
        {
            lock (_slices)
                return _slices.FirstOrDefault(s => s.DAXChannel == dax_channel);
        }

        internal void AddSlice(Slice slc)
        {
            lock (_slices)
            {
                if (_slices.Contains(slc)) return;
                _slices.Add(slc);
                //OnSliceAdded(slc); -- this is now done in the Slice class to ensure that good status info is present before notifying the client
            }

            RaisePropertyChanged("SliceList");
        }

        internal void RemoveSlice(Slice slc)
        {
            lock (_slices)
            {
                if (!_slices.Contains(slc)) return;
                _slices.Remove(slc);
            }

            UpdateGuiClientListTXSlices();
            OnSliceRemoved(slc);
            RaisePropertyChanged("SliceList");
        }

        internal void RemoveAllSlices()
        {
            lock (_slices)
            {
                while (_slices.Count > 0)
                {
                    Slice slc = _slices[0];
                    _slices.Remove(slc);
                    OnSliceRemoved(slc);
                }
            }
        }

        /// <summary>
        /// Delegate event handler for the SliceRemoved event
        /// </summary>
        /// <param name="slc"></param>
        public delegate void SliceRemovedEventHandler(Slice slc);
        /// <summary>
        /// This event is raised when a Slice is removed from the radio
        /// </summary>
        public event SliceRemovedEventHandler SliceRemoved;

        private void OnSliceRemoved(Slice slc)
        {
            if (SliceRemoved != null)
                SliceRemoved(slc);
        }

        /// <summary>
        /// Delegate event handler for the SlicePanReferenceChange event
        /// </summary>
        /// <param name="slc"></param>
        public delegate void SlicePanReferenceChangeEventHandler(Slice slc);
        /// <summary>
        /// This event is raised when a new Slice has been added to the radio
        /// </summary>
        public event SlicePanReferenceChangeEventHandler SlicePanReferenceChange;

        internal void OnSlicePanReferenceChange(Slice slc)
        {
            if (SlicePanReferenceChange != null)
                SlicePanReferenceChange(slc);
        }

        /// <summary>
        /// Delegate event handler for the SliceAdded event
        /// </summary>
        /// <param name="slc"></param>
        public delegate void SliceAddedEventHandler(Slice slc);
        /// <summary>
        /// This event is raised when a new Slice has been added to the radio
        /// </summary>
        public event SliceAddedEventHandler SliceAdded;

        internal void OnSliceAdded(Slice slc)
        {
            if (SliceAdded != null)
                SliceAdded(slc);
        }

        public Slice ActiveSlice
        {
            get
            {
                uint client_handle = 0;

                // is this a GUI client?
                if (API.IsGUI)
                {
                    // yes -- just use this client's handle
                    client_handle = this.ClientHandle;
                }
                else
                {
                    // no -- not a GUI client
                    // is there a bound GUI client?
                    if (_boundClientID != null)
                    {
                        // yes -- find the bound clients handle and use that
                        GUIClient gui_client = FindGUIClientByClientID(_boundClientID);
                        if (gui_client != null)
                            client_handle = gui_client.ClientHandle;
                    }
                    else
                    {
                        // non-GUI client and not bound to one
                        // in this case, we will just return the first ActiveSlice we find, regardless of which GUIClient it belongs to
                        client_handle = 0;
                    }
                }

                lock (_slices)
                {
                    foreach (Slice slc in _slices)
                    {
                        if (slc.Active && // this Slice is active
                            (client_handle == 0 || slc.ClientHandle == client_handle)) // either we don't have a good client_handle or it matches
                            return slc;
                    }
                }

                return null;
            }
        }

        internal void UpdateActiveSlice()
        {
            RaisePropertyChanged("ActiveSlice");
        }

        public Slice TransmitSlice
        {
            get
            {
                lock (_slices)
                {
                    foreach (Slice slc in _slices)
                    {
                        if (slc.IsTransmitSlice && slc.ClientHandle == _clientHandle)
                            return slc;
                    }
                }

                return null;
            }
        }

        internal void UpdateTransmitSlice()
        {
            RaisePropertyChanged("TransmitSlice");
        }

        public Amplifier ActiveAmplifier
        {
            get
            {
                lock (_amplifiers)
                    return _amplifiers.FirstOrDefault();
            }
        }

        internal void UpdateActiveAmplifier()
        {
            RaisePropertyChanged("ActiveAmplifier");
        }

        public Tuner ActiveTuner
        {
            get
            {
                lock (_tuners)
                    return _tuners.FirstOrDefault();
            }
        }

        internal void UpdateActiveTuner()
        {
            RaisePropertyChanged("ActiveTuner");
        }

        private int _slicesRemaining;
        /// <summary>
        /// Gets the number of remaining Slice resources available
        /// </summary>
        public int SlicesRemaining
        {
            get { return _slicesRemaining; }

            // It looks like we are not using the setter--
            // the _sliceRemaining gets changed only by 
            // a radio command message.  I am leaving it 
            // but will make it internal. --Abed
            internal set
            {
                if (_slicesRemaining != value)
                {
                    _slicesRemaining = value;
                    RaisePropertyChanged("SlicesRemaining");
                }
            }
        }

        public string GetSliceLetterFromIndex(int index)
        {
            lock (_slices)
            {
                foreach (Slice slc in _slices)
                {
                    if (slc.Index == index)
                        return slc.Letter;
                }
            }

            return "-";
        }

        #endregion

        #region Panadapter Routines

        internal void RemovePanadapter(Panadapter pan)
        {
            if (pan == null) return;

            lock (_panadapters)
            {
                if (_panadapters.Contains(pan))
                    _panadapters.Remove(pan);
            }

            OnPanadapterRemoved(pan);
            RaisePropertyChanged("PanadapterList");
        }

        private void RemoveAllPanadapters()
        {
            lock (_panadapters)
            {
                while (_panadapters.Count > 0)
                {
                    Panadapter pan = _panadapters[0];
                    _panadapters.Remove(pan);
                    OnPanadapterRemoved(pan);
                }
            }

            RaisePropertyChanged("PanadapterList");
        }

        public delegate void PanadapterAddedEventHandler(Panadapter pan, Waterfall fall);
        public event PanadapterAddedEventHandler PanadapterAdded;

        internal void OnPanadapterAdded(Panadapter pan, Waterfall fall)
        {
            if (PanadapterAdded != null)
                PanadapterAdded(pan, fall);
        }

        /// <summary>
        /// The delegate event handler for the PanadapterRemoved event
        /// </summary>
        /// <param name="pan">The Panadapter object</param>
        public delegate void PanadapterRemovedEventHandler(Panadapter pan);
        /// <summary>
        /// This event is raised when a Panadapter is closed
        /// </summary>
        public event PanadapterRemovedEventHandler PanadapterRemoved;

        private void OnPanadapterRemoved(Panadapter pan)
        {
            if (PanadapterRemoved != null)
                PanadapterRemoved(pan);
        }

        internal void AddPanadapter(Panadapter new_pan)
        {
            Panadapter pan = FindPanadapterByStreamID(new_pan.StreamID);
            if (pan != null)
            {
                Debug.WriteLine("Attempted to Add Panadapter already in Radio _panadapters List");
                return; // already in the list
            }

            lock (_panadapters)
                _panadapters.Add(new_pan);

            lock (_slices)
            {
                foreach (Slice slc in _slices)
                {
                    if (slc.PanadapterStreamID == new_pan.StreamID && slc.Panadapter != new_pan)
                    {
                        slc.Panadapter = new_pan;
                        OnSlicePanReferenceChange(slc);
                    }
                }
            }

            //OnPanadapterAdded(new_pan); -- this is now done in the Panadapter class after receiving the necessary status info
            RaisePropertyChanged("PanadapterList");
        }

        internal Panadapter FindPanadapterByStreamID(uint stream_id)
        {
            lock (_panadapters)
                return _panadapters.FirstOrDefault(x => x.StreamID == stream_id);
        }

        internal Waterfall FindWaterfallByParentStreamID(uint stream_id)
        {
            lock (_waterfalls)
                return _waterfalls.FirstOrDefault(x => x.ParentPanadapterStreamID == stream_id);
        }

        /// <summary>
        /// Finds a Panadapter given its DAX IQ Channel
        /// </summary>
        /// <param name="client_handle">The Client Handle to match</param>
        /// <param name="daxIQChannel">The DAX IQ Channel number</param>
        /// <returns>The Panadapter object </returns>
        public Panadapter FindPanByDAXIQChannel(uint client_handle, int daxIQChannel)
        {
            lock (_panadapters)
                return _panadapters.FirstOrDefault(x => x.DAXIQChannel == daxIQChannel && x.ClientHandle == client_handle);
        }

        private int _panadaptersRemaining;
        /// <summary>
        /// The number of available Panadapter resources remaining
        /// </summary>
        public int PanadaptersRemaining
        {
            get { return _panadaptersRemaining; }

            // This is currently only set by a radio command.
            internal set
            {
                if (_panadaptersRemaining != value)
                {
                    _panadaptersRemaining = value;
                    RaisePropertyChanged("PanadaptersRemaining");
                }
            }
        }

        #endregion        

        #region Waterfall Routines

        internal void RemoveWaterfall(Waterfall fall)
        {
            if (fall == null) return;
            lock (_waterfalls)
            {
                if (!_waterfalls.Contains(fall)) return;
                _waterfalls.Remove(fall);
            }

            OnWaterfallRemoved(fall);
        }

        private void RemoveAllWaterfalls()
        {
            lock (_waterfalls)
            {
                while (_waterfalls.Count > 0)
                {
                    Waterfall fall = _waterfalls[0];
                    _waterfalls.Remove(fall);
                    OnWaterfallRemoved(fall);
                }
            }
        }

        public delegate void WaterfallAddedEventHandler(Waterfall wf);
        public event WaterfallAddedEventHandler WaterfallAdded;

        internal void OnWaterfallAdded(Waterfall fall)
        {
            if (WaterfallAdded != null)
                WaterfallAdded(fall);
        }

        public delegate void WaterfallRemovedEventHandler(Waterfall fall);
        public event WaterfallRemovedEventHandler WaterfallRemoved;

        private void OnWaterfallRemoved(Waterfall fall)
        {
            if (WaterfallRemoved != null)
                WaterfallRemoved(fall);
        }

        internal void AddWaterfall(Waterfall new_fall)
        {
            Waterfall fall = FindWaterfallByStreamID(new_fall.StreamID);
            if (fall != null)
            {
                Debug.WriteLine("Attempted to Add Waterfall already in Radio _waterfalls List");
                return; // already in the list
            }

            lock (_waterfalls)
            {
                _waterfalls.Add(new_fall);
            }

            /*foreach (Slice slc in _slices)
            {
                if (slc.PanadapterStreamID == new_pan._stream_id)
                    slc.Panadapter = new_pan;
            }*/

            //OnWaterfallAdded(new_wf); -- this is now done in the Waterfall class after receiving the necessary status info
        }

        internal Waterfall FindWaterfallByStreamID(uint stream_id)
        {
            lock (_waterfalls)
                return _waterfalls.FirstOrDefault(x => x.StreamID == stream_id);
        }

        public Waterfall FindWaterfallByDAXIQChannel(int daxIQChannel)
        {
            lock (_waterfalls)
                return _waterfalls.FirstOrDefault(x => x.DAXIQChannel == daxIQChannel);
        }

        private int _waterfallsRemaining;
        public int WaterfallsRemaining
        {
            get { return _waterfallsRemaining; }
            set
            {
                if (_waterfallsRemaining != value)
                {
                    _waterfallsRemaining = value;
                    RaisePropertyChanged("WaterfallsRemaining");
                }
            }
        }

        #endregion

        #region DAXMICAudioStream Routines

        public delegate void DAXMICAudioStreamRemovedEventHandler(DAXMICAudioStream mic_audio_stream);
        public event DAXMICAudioStreamRemovedEventHandler DAXMICAudioStreamRemoved;

        private void OnDAXMICAudioStreamRemoved(DAXMICAudioStream mic_audio_stream)
        {
            if (DAXMICAudioStreamRemoved != null)
                DAXMICAudioStreamRemoved(mic_audio_stream);
        }

        public delegate void DAXMICAudioStreamAddedEventHandler(DAXMICAudioStream mic_audio_stream);
        public event DAXMICAudioStreamAddedEventHandler DAXMICAudioStreamAdded;
        internal void OnDAXMICAudioStreamAdded(DAXMICAudioStream mic_audio_stream)
        {
            if (DAXMICAudioStreamAdded != null)
                DAXMICAudioStreamAdded(mic_audio_stream);
        }

        internal DAXMICAudioStream FindDAXMICAudioStreamByStreamID(uint stream_id)
        {
            lock (_daxMicAudioStreams)
                return _daxMicAudioStreams.FirstOrDefault(x => x.StreamID == stream_id);
        }

        public DAXMICAudioStream CreateDAXMICAudioStream()
        {
            return new DAXMICAudioStream(this);
        }

        internal void AddDAXMICAudioStream(DAXMICAudioStream new_mic_audio_stream)
        {

            DAXMICAudioStream mic_audio_stream = FindDAXMICAudioStreamByStreamID(new_mic_audio_stream.StreamID);
            if (mic_audio_stream != null)
            {
                Debug.WriteLine("Attempted to Add MICAudioStream already in Radio _micAudioStreams List");
                return; // already in the list
            }

            lock (_daxMicAudioStreams)
                _daxMicAudioStreams.Add(new_mic_audio_stream);
        }

        public void RemoveDAXMICAudioStream(uint stream_id)
        {
            DAXMICAudioStream mic_audio_stream = FindDAXMICAudioStreamByStreamID(stream_id);
            if (mic_audio_stream == null) return;

            lock (_daxMicAudioStreams)
                _daxMicAudioStreams.Remove(mic_audio_stream);

            OnDAXMICAudioStreamRemoved(mic_audio_stream);
        }

        #endregion

        #region DAXTXAudioStream Routines

        public delegate void DAXTXAudioStreamRemovedEventHandler(DAXTXAudioStream tx_audio_stream);
        public event DAXTXAudioStreamRemovedEventHandler DAXTXAudioStreamRemoved;

        private void OnDAXTXAudioStreamRemoved(DAXTXAudioStream tx_audio_stream)
        {
            if (DAXTXAudioStreamRemoved != null)
                DAXTXAudioStreamRemoved(tx_audio_stream);
        }

        public delegate void DAXTXAudioStreamAddedEventHandler(DAXTXAudioStream tx_audio_stream);
        public event DAXTXAudioStreamAddedEventHandler DAXTXAudioStreamAdded;
        internal void OnDAXTXAudioStreamAdded(DAXTXAudioStream tx_audio_stream)
        {
            if (DAXTXAudioStreamAdded != null)
                DAXTXAudioStreamAdded(tx_audio_stream);
        }

        internal DAXTXAudioStream FindDAXTXAudioStreamByStreamID(uint stream_id)
        {
            lock (_daxTXAudioStreams)
            {
                foreach (DAXTXAudioStream tx_audio_stream in _daxTXAudioStreams)
                {
                    if (tx_audio_stream.TXStreamID == stream_id)
                        return tx_audio_stream;
                }
            }

            return null;
        }

        internal void AddDAXTXAudioStream(DAXTXAudioStream new_tx_audio_stream)
        {

            DAXTXAudioStream tx_audio_stream = FindDAXTXAudioStreamByStreamID(new_tx_audio_stream.TXStreamID);
            if (tx_audio_stream != null)
            {
                Debug.WriteLine("Attempted to Add TXAudioStream already in Radio _txAudioStreams List");
                return; // already in the list
            }

            lock (_daxTXAudioStreams)
                _daxTXAudioStreams.Add(new_tx_audio_stream);
        }

        public void RemoveDAXTXAudioStream(uint stream_id)
        {
            DAXTXAudioStream tx_audio_stream = FindDAXTXAudioStreamByStreamID(stream_id);
            if (tx_audio_stream == null) return;

            lock (_daxTXAudioStreams)
                _daxTXAudioStreams.Remove(tx_audio_stream);

            OnDAXTXAudioStreamRemoved(tx_audio_stream);
        }

        #endregion

        #region DAXRXAudioStream Routines

        /// <summary>
        /// Removes a DAX Audio Stream
        /// </summary>
        /// <param name="stream_id">The stream ID of the DAX Channel</param>
        public void RemoveAudioStream(uint stream_id)
        {
            DAXRXAudioStream audio_stream = FindDAXRXAudioStreamByStreamID(stream_id);
            if (audio_stream == null) return;

            lock (_daxRXAudioStream)
                _daxRXAudioStream.Remove(audio_stream);

            OnDAXRXAudioStreamRemoved(audio_stream);
        }

        /// <summary>
        /// The delegate event handler for the DAXRXAudioStreamAdded event
        /// </summary>
        /// <param name="audio_stream">The DAXRXAudioStream object</param>
        public delegate void DAXRXAudioStreamAddedEventHandler(DAXRXAudioStream audio_stream);

        /// <summary>
        /// This event is reaised when a new DAXRXAudioStream is added
        /// </summary>
        public event DAXRXAudioStreamAddedEventHandler DAXRXAudioStreamAdded;

        internal void OnAudioStreamAdded(DAXRXAudioStream audio_stream)
        {
            if (DAXRXAudioStreamAdded != null)
                DAXRXAudioStreamAdded(audio_stream);
        }

        /// <summary>
        /// The delegate event handler for the AudioStreamRemoved event
        /// </summary>
        /// <param name="audio_stream">The DAX AudioStream object</param>
        public delegate void DAXRXAudioStreamRemovedEventHandler(DAXRXAudioStream audio_stream);
        /// <summary>
        /// This event is raised when a DAX Audio Stream has been removed
        /// </summary>
        public event DAXRXAudioStreamRemovedEventHandler DAXRXAudioStreamRemoved;

        private void OnDAXRXAudioStreamRemoved(DAXRXAudioStream audio_stream)
        {
            if (DAXRXAudioStreamRemoved != null)
                DAXRXAudioStreamRemoved(audio_stream);
        }

        internal void AddDAXRXAudioStream(DAXRXAudioStream new_audio_stream)
        {
            DAXRXAudioStream audio_stream = FindDAXRXAudioStreamByStreamID(new_audio_stream.StreamID);
            if (audio_stream != null)
            {
                Debug.WriteLine("Attempted to Add DAXRXAudioStream already in Radio _daxRXAudioStream List");
                return; // already in the list
            }

            lock (_daxRXAudioStream)
                _daxRXAudioStream.Add(new_audio_stream);
        }

        private bool IsDAXRXAudioStreamStatusForThisClient(string s)
        {
            string client_handle_str = null;

            string[] words = s.Split(' ');

            // need at least 3 words in the status message to make a good determination
            if (words.Length < 3) return false;

            // skip the first part of the status (handle and audio stream -- e.g. S81D92FC8|audio_stream 0x)
            for (int i = 2; i < words.Length; i++)
            {
                string[] tokens = words[i].Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Radio::IsDAXRXAudioStreamForThisClient: Invalid key/value pair (" + words[i] + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "client_handle": client_handle_str = value; break;
                }
            }

            if (client_handle_str == null || client_handle_str == "")
                return false;

            uint client_handle_uint;
            bool b = StringHelper.TryParseInteger(client_handle_str, out client_handle_uint);

            if (!b) return false;

            return (_clientHandle == client_handle_uint);
        }

        internal DAXRXAudioStream FindDAXRXAudioStreamByStreamID(uint stream_id)
        {
            lock (_daxRXAudioStream)
            {
                foreach (DAXRXAudioStream audio_stream in _daxRXAudioStream)
                {
                    if (audio_stream.StreamID == stream_id)
                        return audio_stream;
                }
            }

            return null;
        }

        internal DAXRXAudioStream FindDAXRXAudioStreamByDAXChannel(int daxChannel)
        {
            lock (_daxRXAudioStream)
            {
                foreach (DAXRXAudioStream audio_stream in _daxRXAudioStream)
                {
                    if (audio_stream.DAXChannel == daxChannel &&
                        audio_stream.ClientHandle == this.ClientHandle)
                        return audio_stream;
                }
            }

            return null;
        }

        #endregion

        #region RemoteAudioTX Routines

        /// <summary>
        /// Creates a new Opus Audio Stream
        /// </summary>
        /// <returns>The OpusStream object</returns>
        public TXRemoteAudioStream CreateOpusStream()
        {
            return new TXRemoteAudioStream(this);
        }

        /// <summary>
        /// Removes an Opus Audio Stream
        /// </summary>
        public void RemoveTXRemoteAudioStream(uint streamID)
        {
            TXRemoteAudioStream opus_stream = FindTXRemoteAudioStreamByStreamID(streamID);
            if (opus_stream == null) return;

            opus_stream.Remove();
            lock (_txRemoteAudioStream)
                _txRemoteAudioStream.Remove(opus_stream);

            OnTXRemoteAudioStreamRemoved(opus_stream);
        }



        /// <summary>
        /// The delegate event handler for the TXRemoteAudioStream event
        /// </summary>
        /// <param name="remoteAudioTX">The TXRemoteAudioStream object</param>
        public delegate void TXRemoteAudioStreamAddedEventHandler(TXRemoteAudioStream remoteAudioTX);

        /// <summary>
        /// This event is reaised when a new Opus Audio Stream is added
        /// </summary>
        public event TXRemoteAudioStreamAddedEventHandler TXRemoteAudioStreamAdded;

        internal void OnTXRemoteAudioStreamAdded(TXRemoteAudioStream RemoteAudioTX)
        {
            if (TXRemoteAudioStreamAdded != null)
                TXRemoteAudioStreamAdded(RemoteAudioTX);
        }

        /// <summary>
        /// The delegate event handler for the TXRemoteAudioStream event
        /// </summary>
        /// <param name="audio_stream">The TXRemoteAudioStreamStream object</param>
        public delegate void TXRemoteAudioStreamRemovedEventHandler(TXRemoteAudioStream remoteAudioTX);
        /// <summary>
        /// This event is raised when an Opus Stream has been removed
        /// </summary>
        public event TXRemoteAudioStreamRemovedEventHandler TXRemoteAudioStreamRemoved;

        private void OnTXRemoteAudioStreamRemoved(TXRemoteAudioStream remoteAudioTX)
        {
            if (TXRemoteAudioStreamRemoved != null)
                TXRemoteAudioStreamRemoved(remoteAudioTX);
        }


        public void AddTXRemoteAudioStream(TXRemoteAudioStream newRemoteAudioTX)
        {
            TXRemoteAudioStream remoteAudioTX = FindTXRemoteAudioStreamByStreamID(newRemoteAudioTX.StreamID);
            if (remoteAudioTX != null)
            {
                Debug.WriteLine("Attempted to Add OpusStream already in Radio _audioStreams List");
                return; // already in the list
            }

            lock (_txRemoteAudioStream)
                _txRemoteAudioStream.Add(newRemoteAudioTX);
        }

        internal TXRemoteAudioStream FindTXRemoteAudioStreamByStreamID(uint stream_id)
        {
            lock (_txRemoteAudioStream)
            {
                foreach (TXRemoteAudioStream remoteAudioTX in _txRemoteAudioStream)
                {
                    if (remoteAudioTX.StreamID == stream_id)
                        return remoteAudioTX;
                }
            }

            return null;
        }


        #endregion

        #region RXRemoteAudioStream Routines

        /// <summary>
        /// Removes a Remote Audio RX Stream
        /// </summary>
        public void RemoveRXRemoteAudioStream(uint streamID)
        {
            RXRemoteAudioStream remoteAudioRxStream = FindRXRemoteAudioStreamByStreamID(streamID);
            if (remoteAudioRxStream == null) return;

            remoteAudioRxStream.Remove();
            lock (_rxRemoteAudioStreams)
            {
                _rxRemoteAudioStreams.Remove(remoteAudioRxStream);
            }

            OnRXRemoteAudioStreamRemoved(remoteAudioRxStream);
        }

        /// <summary>
        /// The delegate event handler for the RXRemoteAudioStreamStreamAdded event
        /// </summary>
        /// <param name="remoteAudioRX">The RXRemoteAudioStream object</param>
        public delegate void RXRemoteAudioStreamAddedEventHandler(RXRemoteAudioStream remoteAudioRX);

        /// <summary>
        /// This event is reaised when a new Remote Audio RX Stream is added
        /// </summary>
        public event RXRemoteAudioStreamAddedEventHandler RXRemoteAudioStreamAdded;

        internal void OnRXRemoteAudioStreamAdded(RXRemoteAudioStream remoteAudioRx)
        {
            if (RXRemoteAudioStreamAdded != null)
                RXRemoteAudioStreamAdded(remoteAudioRx);
        }

        /// <summary>
        /// The delegate event handler for the RXRemoteAudioStreamStreamRemoved event
        /// </summary>
        /// <param name="remoteAudioRX">The Remote Audio RX object</param>
        public delegate void RXRemoteAudioStreamRemovedEventHandler(RXRemoteAudioStream remoteAudioRX);
        /// <summary>
        /// This event is raised when Remote Audio RX Stream has been removed
        /// </summary>
        public event RXRemoteAudioStreamRemovedEventHandler RXRemoteAudioStreamRemoved;

        private void OnRXRemoteAudioStreamRemoved(RXRemoteAudioStream remoteAudioRX)
        {
            if (RXRemoteAudioStreamRemoved != null)
                RXRemoteAudioStreamRemoved(remoteAudioRX);
        }

        public void AddRXRemoteAudioStream(RXRemoteAudioStream newRemoteAudioRX)
        {
            RXRemoteAudioStream remoteAudioRX = FindRXRemoteAudioStreamByStreamID(newRemoteAudioRX.StreamID);
            if (remoteAudioRX != null)
            {
                Debug.WriteLine("Attempted to Add RXRemoteAudioStream already in Radio _audioStreams List");
                return; // already in the list
            }

            lock (_rxRemoteAudioStreams)
                _rxRemoteAudioStreams.Add(newRemoteAudioRX);
        }

        internal RXRemoteAudioStream FindRXRemoteAudioStreamByStreamID(uint stream_id)
        {
            lock (_rxRemoteAudioStreams)
            {
                foreach (RXRemoteAudioStream remoteAudioRX in _rxRemoteAudioStreams)
                {
                    if (remoteAudioRX.StreamID == stream_id)
                        return remoteAudioRX;
                }
            }

            return null;
        }

        [Obsolete("Use RequestRXRemoteAudioStream(bool isCompressed) to explicitly specifiy whether to use compression")]
        public void RequestRXRemoteAudioStream()
        {
            SendCommand("stream create type=remote_audio_rx");
        }

        public void RequestRXRemoteAudioStream(bool isCompressed)
        {
            if (isCompressed)
            {
                SendCommand("stream create type=remote_audio_rx compression=opus");
            }
            else
            {
                SendCommand("stream create type=remote_audio_rx compression=none");
            }
        }

        public void RequestRemoteAudioTXStream()
        {
            SendCommand("stream create type=remote_audio_tx");
        }
        #endregion

        #region DAXIQStream Routines

        /// <summary>
        /// Finds a DAX IQ Stream by its Stream ID
        /// </summary>
        /// <param name="stream_id">The StreamID of the DAX IQ Stream</param>
        public void RemoveDAXIQStream(uint stream_id)
        {
            DAXIQStream iq_stream = FindDAXIQStreamByStreamID(stream_id);
            if (iq_stream == null) return;

            lock (_daxIQStreams)
                _daxIQStreams.Remove(iq_stream);

            OnDAXIQStreamRemoved(iq_stream); // good find EHR
        }

        /// <summary>
        /// The delegate event handler for the IQStreamAdded event
        /// </summary>
        /// <param name="iq_stream"></param>
        public delegate void DAXIQStreamAddedEventHandler(DAXIQStream iq_stream);
        /// <summary>
        /// This event is raised when a new DAX IQ Stream is added
        /// </summary>
        public event DAXIQStreamAddedEventHandler DAXIQStreamAdded;

        internal void OnDAXIQStreamAdded(DAXIQStream iq_stream)
        {
            if (DAXIQStreamAdded != null)
                DAXIQStreamAdded(iq_stream);
        }

        /// <summary>
        /// The delegate event handler for the IQStreamRemoved event
        /// </summary>
        /// <param name="iq_stream">The DAX IQStream object</param>
        public delegate void DAXIQStreamRemovedEventHandler(DAXIQStream iq_stream);
        /// <summary>
        ///  This event is raised when a DAX IQ Stream is removed
        /// </summary>
        public event DAXIQStreamRemovedEventHandler DAXIQStreamRemoved;

        private void OnDAXIQStreamRemoved(DAXIQStream iq_stream)
        {
            if (DAXIQStreamRemoved != null)
                DAXIQStreamRemoved(iq_stream);
        }

        internal void AddDAXIQStream(DAXIQStream new_iq_stream)
        {
            DAXIQStream iq_stream = FindDAXIQStreamByStreamID(new_iq_stream.StreamID);
            if (iq_stream != null)
            {
                Debug.WriteLine("Attempted to Add IQStream already in Radio _iqStreams List");
                return; // already in the list
            }

            // Add the new stream to the IQ Streams list
            lock (_daxIQStreams)
                _daxIQStreams.Add(new_iq_stream);

            //OnIQStreamAdded(new_iq_stream); -- this is now done in the IQStream class to ensure full info before notifying the clients
        }

        internal DAXIQStream FindDAXIQStreamByStreamID(uint stream_id)
        {
            lock (_daxIQStreams)
            {
                foreach (DAXIQStream iq_stream in _daxIQStreams)
                {
                    if (iq_stream.StreamID == stream_id)
                        return iq_stream;
                }
            }

            return null;
        }

        public DAXIQStream FindDAXIQStreamByDAXIQChannel(int daxIQChannel)
        {
            lock (_daxIQStreams)
            {
                foreach (DAXIQStream iq_stream in _daxIQStreams)
                {
                    if (iq_stream.DAXIQChannel == daxIQChannel &&
                        iq_stream.ClientHandle == this.ClientHandle) // ensure that we are only returning DAXIQStreams for this client
                        return iq_stream;
                }
            }

            return null;
        }

        private int _daxiqCapacity;
        public int DAXIQCapacity
        {
            get { return _daxiqCapacity; }
            set
            {
                if (_daxiqCapacity != value)
                {
                    _daxiqCapacity = value;
                    RaisePropertyChanged("DAXIQCapacity");
                }
            }
        }

        private int _daxiqAvailable;
        public int DAXIQAvailable
        {
            get { return _daxiqAvailable; }
            set
            {
                if (_daxiqAvailable != value)
                {
                    _daxiqAvailable = value;
                    RaisePropertyChanged("DAXIQAvailable");
                }
            }
        }

        #endregion

        #region Antenna Routines

        private void GetRXAntennaList()
        {
            SendReplyCommand(new ReplyHandler(GetRXAntennaListReply), "ant list");
        }

        private void GetRXAntennaListReply(int seq, uint resp_val, string reply)
        {
            if (resp_val != 0) return;

            _rx_ant_list = reply.Split(',');
            RaisePropertyChanged("RXAntList");
        }

        #endregion

        #region Meter Routines

        private void GetMeterList()
        {
            SendReplyCommand(new ReplyHandler(GetMeterListReply), "meter list");
        }

        private void GetMeterListReply(int seq, uint resp_val, string s)
        {
            if (resp_val != 0) return;
            ParseMeterStatus(s);
        }

        private void ParseMeterStatus(string status)
        {
            if (status == "") return;

            Meter m = null;

            // check for removal first
            if (status.Contains("removed"))
            {
                string[] words = status.Split(' ');
                if (words.Length < 2 || words[0] == "")
                {
                    Debug.WriteLine("ParseMeterStatus: Invalid removal status -- min 2 tokens (" + status + ")");
                    return;
                }

                int meter_index;
                bool b = int.TryParse(words[0], out meter_index);

                if (!b)
                {
                    Debug.WriteLine("ParseMeterStatus: Error parsing meter index in removal status (" + status + ")");
                    return;
                }

                m = FindMeterByIndex(meter_index);
                if (m == null) return;

                if (m.Source == Meter.SOURCE_SLICE)
                {
                    // get a hold of the slice in order to remove the meter from its meter list
                    Slice slc = FindSliceByIndex(m.SourceIndex);
                    if (slc != null)
                        slc.RemoveMeter(m);
                }

                if (m.Source == Meter.SOURCE_AMPLIFIER)
                {
                    Amplifier amp = FindAmplifierByHandle("0x" + m.SourceIndex.ToString("X8"));
                    if (amp != null)
                        amp.RemoveMeter(m);
                }

                RemoveMeter(m);
                return;
            }

            bool new_meter = false;

            // not a removal, do normal parsing
            string[] reply_tokens = status.Split('#');

            foreach (string s in reply_tokens)
            {
                if (s == "") break;

                // break down the message into thae 3 components: index, key, and value
                // message is typically in the format index.key=value
                // we can use the '.' and '=' to find the edges of each token
                int meter_index;
                int start = 0;
                int len = s.IndexOf(".");
                if (len < 0)
                {
                    Debug.WriteLine("Error in Meter List Reply: Expected '.', but found none (" + s + ")");
                    continue;
                }

                bool b = int.TryParse(s.Substring(start, len), out meter_index);

                if (!b)
                {
                    Debug.WriteLine("Error in Meter List Reply: Invalid Index (" + s.Substring(start, len) + ")");
                    continue;
                }

                // parse the key from the string
                start = len + 1;
                int eq_index = s.IndexOf("=");
                if (eq_index < 0)
                {
                    Debug.WriteLine("Error in Meter List Reply: Expected '=', but found none (" + s + ")");
                    continue;
                }

                len = eq_index - len - 1;
                string key = s.Substring(start, len);

                // parse the value from the string
                string value = s.Substring(eq_index + 1); // everything after the '='


                // check to see whether we have a meter object for this index
                m = FindMeterByIndex(meter_index);
                if (m == null) // if not, create one
                {
                    m = new Meter(this, meter_index);
                    lock (_meters)
                    {
                        _meters.Add(m);
                    }
                    new_meter = true;
                }

                // depending on what the key is, parse the next value appropriately
                switch (key)
                {
                    case "src":
                        {
                            if (m.Source != value)
                                m.Source = value;
                        }
                        break;
                    case "num":
                        {
                            int source_index;
                            b = StringHelper.TryParseInteger(value, out source_index); // strip the 0x on hex numbers if needed

                            if (!b)
                            {
                                Debug.WriteLine("Error in Meter List Reply: Invalid Source Index (" + value + ")");
                                continue;
                            }

                            if (m.SourceIndex != source_index)
                                m.SourceIndex = source_index;
                        }
                        break;
                    case "nam":
                        {
                            if (m.Name != value)
                                m.Name = value;
                        }
                        break;
                    case "low":
                        {
                            if (!StringHelper.TryParseDouble(value, out double low))
                            {
                                Debug.WriteLine($"Error in Meter List Reply: Invalid Low ({value})");
                                continue;
                            }

                            if (m.Low != low)
                                m.Low = low;
                        }
                        break;
                    case "hi":
                        {
                            if (!StringHelper.TryParseDouble(value, out double high))
                            {
                                Debug.WriteLine($"Error in Meter List Reply: Invalid High ({value})");
                                continue;
                            }

                            if (m.High != high)
                                m.High = high;
                        }
                        break;
                    case "desc":
                        {
                            if (m.Description != value)
                                m.Description = value;
                        }
                        break;
                    case "unit":
                        {
                            MeterUnits units = MeterUnits.None;
                            switch (value)
                            {
                                case "Volts": units = MeterUnits.Volts; break;
                                case "Amps": units = MeterUnits.Amps; break;
                                case "dB": units = MeterUnits.Db; break;
                                case "dBm": units = MeterUnits.Dbm; break;
                                case "dBFS": units = MeterUnits.Dbfs; break;
                                case "degF": units = MeterUnits.DegreesF; break;
                                case "degC": units = MeterUnits.DegreesC; break;
                                case "SWR": units = MeterUnits.SWR; break;
                                case "Watts": units = MeterUnits.Watts; break;
                                case "Percent": units = MeterUnits.Percent; break;
                            }

                            if (m.Units != units)
                                m.Units = units;
                        }
                        break;
                }
            }

            if (m != null)
            {
                if (m.Source == Meter.SOURCE_SLICE)
                {
                    Slice slc = FindSliceByIndex(m.SourceIndex);
                    if (slc != null)
                    {
                        if (slc.FindMeterByIndex(m.Index) == null)
                            slc.AddMeter(m);
                    }
                }
                else if (m.Source == Meter.SOURCE_AMPLIFIER)
                {
                    Amplifier amp = FindAmplifierByHandle("0x" + m.SourceIndex.ToString("X"));
                    if (amp != null)
                    {
                        if (amp.FindMeterByIndex(m.Index) == null)
                            amp.AddMeter(m);
                    }
                    else // see if it is maybe a Tuner Meter
                    {
                        Tuner tuner = FindTunerByHandle("0x" + m.SourceIndex.ToString("X"));
                        if (tuner != null)
                        {
                            if (tuner.FindMeterByIndex(m.Index) == null)
                                tuner.AddMeter(m);
                        }
                    }
                }

                if (new_meter)
                    AddMeter(m);
            }
        }

        private void ParseNetParamsStatus(string s)
        {
            //ip=num.num.num.num gateway=num.num.num.num netmask=num.num.num.num
            string[] words = s.Split(' ');
            string modes = words[0].ToLower();
            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');

                string key = tokens[0];
                string value = "";
                if (tokens.Length >= 2)
                    value = tokens[1];

                switch (key.ToLower())
                {
                    case "ip":
                        {
                            IPAddress ip;
                            bool b = IPAddress.TryParse(value, out ip);

                            if (!b)
                            {
                                _staticIP = null;
                            }
                            else
                            {
                                _staticIP = ip;
                            }
                            RaisePropertyChanged("StaticIP");
                        }

                        break;

                    case "gateway":
                        {
                            IPAddress gateway;

                            bool b = IPAddress.TryParse(value, out gateway);

                            if (!b)
                            {
                                _staticGateway = null;
                            }
                            else
                            {
                                _staticGateway = gateway;
                            }
                            RaisePropertyChanged("StaticGateway");
                        }
                        break;

                    case "netmask":
                        {
                            IPAddress netmask;

                            bool b = IPAddress.TryParse(value, out netmask);

                            if (!b)
                            {
                                _staticNetmask = null;
                            }
                            else
                            {
                                _staticNetmask = netmask;
                            }
                            RaisePropertyChanged("StaticNetmask");
                        }
                        break;
                }
            }
        }

        private Meter FindMeterByIndex(int index)
        {
            lock (_meters)
                return _meters.FirstOrDefault(m => m.Index == index);
        }

        /// <summary>
        /// Gets a Meter by its name
        /// </summary>
        /// <param name="s">The meter name</param>
        /// <returns>The found Meter object</returns>
        public Meter FindMeterByName(string s)
        {
            lock (_meters)
                return _meters.FirstOrDefault(m => m.Name == s);
        }

        public ImmutableList<Meter> FindMetersByAmplifier(Amplifier amp)
        {
            lock (_meters)
                return _meters.FindAll(x => (x.Source.ToUpper() == Meter.SOURCE_AMPLIFIER &&
                    $"0x{x.SourceIndex:X8}" == amp.Handle)).ToImmutableList();
        }

        public ImmutableList<Meter> FindMetersByTuner(Tuner tuner)
        {
            lock (_meters)
                return _meters.FindAll(x => (x.Source.ToUpper() == Meter.SOURCE_AMPLIFIER &&
                    $"0x{x.SourceIndex:X8}" == tuner.Handle)).ToImmutableList();
        }

        private void AddMeter(Meter m)
        {
            lock (_meters)
            {
                if (!_meters.Contains(m))
                {
                    _meters.Add(m);
                }
            }

            if (m.Name == "FWDPWR")
                m.DataReady += new Meter.DataReadyEventHandler(FWDPW_DataReady);
            else if (m.Name == "REFPWR")
                m.DataReady += new Meter.DataReadyEventHandler(REFPW_DataReady);
            else if (m.Name == "SWR")
                m.DataReady += new Meter.DataReadyEventHandler(SWR_DataReady);
            else if (m.Name == "PATEMP")
                m.DataReady += new Meter.DataReadyEventHandler(PATEMP_DataReady);
            else if (m.Name == "MIC")
                m.DataReady += new Meter.DataReadyEventHandler(MIC_DataReady);
            else if (m.Name == "MICPEAK")
                m.DataReady += new Meter.DataReadyEventHandler(MICPeak_DataReady);
            else if (m.Name == "COMPPEAK")
                m.DataReady += new Meter.DataReadyEventHandler(COMPPeak_DataReady);
            else if (m.Name == "HWALC")
                m.DataReady += new Meter.DataReadyEventHandler(HWAlc_DataReady);
            else if (m.Name == "+13.8A") // A: before the fuse
                m.DataReady += new Meter.DataReadyEventHandler(Volts_DataReady);
            else if (m.Name == "PAEFF")
                m.DataReady += new Meter.DataReadyEventHandler(PAEff_DataReady);

        }

        private void RemoveMeter(Meter m)
        {
            lock (_meters)
            {
                if (_meters.Contains(m))
                {
                    _meters.Remove(m);

                    if (m.Name == "FWDPWR")
                        m.DataReady -= FWDPW_DataReady;
                    else if (m.Name == "REFPWR")
                        m.DataReady -= REFPW_DataReady;
                    else if (m.Name == "SWR")
                        m.DataReady -= SWR_DataReady;
                    else if (m.Name == "PATEMP")
                        m.DataReady -= PATEMP_DataReady;
                    else if (m.Name == "MIC")
                        m.DataReady -= MIC_DataReady;
                    else if (m.Name == "MICPEAK")
                        m.DataReady -= MICPeak_DataReady;
                    else if (m.Name == "COMPPEAK")
                        m.DataReady -= Volts_DataReady;
                    else if (m.Name == "HWALC")
                        m.DataReady -= HWAlc_DataReady;
                    else if (m.Name == "+13.8A") // A: Before the fuse
                        m.DataReady -= Volts_DataReady;
                    else if (m.Name == "PAEFF")
                        m.DataReady -= PAEff_DataReady;
                }
            }
        }

        private void FWDPW_DataReady(Meter meter, float data)
        {
            OnForwardPowerDataReady(data);
        }

        private void REFPW_DataReady(Meter meter, float data)
        {
            OnReflectedPowerDataReady(data);
        }

        void SWR_DataReady(Meter meter, float data)
        {
            OnSWRDataReady(data);
        }

        void PATEMP_DataReady(Meter meter, float data)
        {
            OnPATempDataReady(data);
        }

        void MIC_DataReady(Meter meter, float data)
        {
            OnMicDataReady(data);
        }

        void MICPeak_DataReady(Meter meter, float data)
        {
            OnMicPeakDataReady(data);
        }

        void COMPPeak_DataReady(Meter meter, float data)
        {
            OnCompPeakDataReady(data);
        }

        void HWAlc_DataReady(Meter meter, float data)
        {
            OnHWAlcDataReady(data);  //abed change
        }

        void Volts_DataReady(Meter meter, float data)
        {
            OnVoltsDataReady(data);
        }
        
        void PAEff_DataReady(Meter meter, float data)
        {
            OnPAEffDataReady(data);
        }
        
        /// <summary>
        /// The delegate event handler for meter data events.
        /// Used with the events: ForwardPowerDataReady, ReflectedPowerDataReady,
        /// SWRDataReady, PATempDataReady, VoltsDataReady, MicDataReady, MicPeakDataReady,
        /// CompPeakDataReady, HWAlcDataReady, PAEffDataReady, etc.
        /// </summary>
        /// <param name="data">The forward RF power meter value</param>
        public delegate void MeterDataReadyEventHandler(float data);
        /// <summary>
        /// This event is raised when there is new meter data for the forward RF power.
        /// Data units are in dBm.
        /// </summary>
        public event MeterDataReadyEventHandler ForwardPowerDataReady;
        private void OnForwardPowerDataReady(float data)
        {
            if (ForwardPowerDataReady != null)
                ForwardPowerDataReady(data);
        }

        /// <summary>
        /// This event is raised when there is new meter data for the reflected RF power.
        /// Data units are in dBm.
        /// </summary>
        public event MeterDataReadyEventHandler ReflectedPowerDataReady;
        private void OnReflectedPowerDataReady(float data)
        {
            if (ReflectedPowerDataReady != null)
                ReflectedPowerDataReady(data);
        }

        /// <summary>
        /// This event is raised when there is new meter data for the SWR.
        /// Data units are in VSWR.
        /// </summary>
        public event MeterDataReadyEventHandler SWRDataReady;
        private void OnSWRDataReady(float data)
        {
            //Debug.WriteLine("SWR: " + data.ToString("f2"));
            if (SWRDataReady != null)
                SWRDataReady(data);
        }

        /// <summary>
        /// This event is raised when there is new meter data for the PA
        /// Temperature.  Data units are in degrees Celsius.
        /// </summary>
        public event MeterDataReadyEventHandler PATempDataReady;
        private void OnPATempDataReady(float data)
        {
            if (PATempDataReady != null)
                PATempDataReady(data);
        }

        public event MeterDataReadyEventHandler VoltsDataReady;
        private void OnVoltsDataReady(float data)
        {
            if (VoltsDataReady != null)
                VoltsDataReady(data);
        }

        /// <summary>
        /// This event is raised when there is new meter data for
        /// the Mic input level (use MicPeakDataReady for peak levels).
        /// </summary>
        public event MeterDataReadyEventHandler MicDataReady;
        private void OnMicDataReady(float data)
        {
            if (MicDataReady != null)
                MicDataReady(data);
        }

        /// <summary>
        /// This event is raised when there is new meter data for
        /// the Mic peak level.
        /// </summary>
        public event MeterDataReadyEventHandler MicPeakDataReady;
        private void OnMicPeakDataReady(float data)
        {
            if (MicPeakDataReady != null)
                MicPeakDataReady(data);
        }

        /// <summary>
        /// This event is raised when there is new meter data for
        /// the input Compression.  Data is in units of reduction
        /// in dB.
        /// </summary>
        public event MeterDataReadyEventHandler CompPeakDataReady;
        private void OnCompPeakDataReady(float data)
        {
            if (CompPeakDataReady != null)
                CompPeakDataReady(data);
        }

        /// <summary>
        /// This event is raised when there is new meter data for the Hardware ALC
        /// input to the radio.  The data is in units of Volts.
        /// </summary>
        public event MeterDataReadyEventHandler HWAlcDataReady;
        private void OnHWAlcDataReady(float data)
        {
            if (HWAlcDataReady != null)
                HWAlcDataReady(data);
        }

        /// <summary>
        /// This event is raised when there is new meter data for the PA
        /// Efficiency.  Data units are in degrees dBm.
        /// </summary>
        public event MeterDataReadyEventHandler PAEffDataReady;
        private void OnPAEffDataReady(float data)
        {
            if (PAEffDataReady != null)
                PAEffDataReady(data);
        }
    
        #endregion

        #region Version Routines

        private void GetVersions()
        {
            SendReplyCommand(new ReplyHandler(UpdateVersions), "version");
        }

        private void UpdateVersions(int seq, uint resp_val, string s)
        {
            if (resp_val != 0) return;

            _versions = s;
            string[] vers = s.Split('#');
            UInt64 temp;
            bool b;


            foreach (string kv in vers)
            {
                string key, value;
                string[] tokens = kv.Split('=');

                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Radio::UpdateVersions - Invalid token (" + kv + ")");
                    continue;
                }

                key = tokens[0];
                value = tokens[1];

                b = FlexVersion.TryParse(value, out temp);
                if (!b)
                {
                    Debug.WriteLine("Radio::UpdateVersions -- Invalid value (" + value + ")");
                    continue;
                }

                switch (key)
                {
                    case "PSoC-MBTRX":
                        {
                            _trxPsocVersion = temp;
                            RaisePropertyChanged("TRXPsocVersion");
                        }
                        break;
                    case "PSoC-MBPA100":
                        {
                            _paPsocVersion = temp;
                            RaisePropertyChanged("PAPsocVersion");
                        }
                        break;
                    case "FPGA-MB":
                        {
                            _fpgaVersion = temp;
                            RaisePropertyChanged("FPGAVersion");
                        }
                        break;
                }
            }

            RaisePropertyChanged("Versions");
        }

        private UInt64 _fpgaVersion;
        public UInt64 FPGAVersion
        {
            get { return _fpgaVersion; }
        }

        private UInt64 _paPsocVersion;
        public UInt64 PAPsocVersion
        {
            get { return _paPsocVersion; }
        }

        private UInt64 _trxPsocVersion;
        public UInt64 TRXPsocVersion
        {
            get { return _trxPsocVersion; }
        }

        #endregion

        #region Info Routines

        private void GetInfo()
        {
            SendReplyCommand(new ReplyHandler(UpdateInfo), "info");
        }

        private void UpdateInfo(int seq, uint resp_val, string s)
        {
            if (resp_val != 0) return;

            string[] vers = s.Split(',');

            foreach (string kv in vers)
            {
                string key, value;
                string[] tokens = kv.Split('=');

                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Radio::UpdateInfo - Invalid token (" + kv + ")");
                    continue;
                }

                key = tokens[0];
                value = tokens[1].Trim('\\', '"');

                switch (key)
                {
                    case "atu_present":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::UpdateInfo - invalid value (" + key + "=" + value + ")");
                                continue;
                            }

                            _atuPresent = Convert.ToBoolean(temp);
                            RaisePropertyChanged("ATUPresent");
                        }
                        break;

                    case "callsign":
                        {
                            _callsign = value;
                            RaisePropertyChanged("Callsign");
                        }
                        break;

                    case "gps":
                        {
                            GPSInstalled = (value != "Not Present");
                        }
                        break;

                    case "name":
                        {
                            _nickname = value;
                            RaisePropertyChanged("Nickname");
                        }
                        break;

                    case "num_tx":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::UpdateInfo - tx: Error - invalid value (" + value + ")");
                                continue;
                            }

                            _num_tx = temp;
                            RaisePropertyChanged("NumTX");
                        }
                        break;

                    case "options":
                        _radioOptions = value;
                        RaisePropertyChanged("RadioOptions");
                        break;

                    case "region":
                        {
                            RegionCode = value;
                        }
                        break;

                    case "screensaver":
                        {
                            ScreensaverMode mode = ParseScreensaverMode(value);
                            if (mode == ScreensaverMode.None) continue;

                            _screensaver = mode;
                            RaisePropertyChanged("Screensaver");
                        }
                        break;

                    case "netmask":
                        {
                            IPAddress temp;
                            bool b = IPAddress.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::UpdateInfo - subnet: Error - invalid value (" + value + ")");
                                continue;
                            }

                            _subnetMask = temp;
                            RaisePropertyChanged("SubnetMask");
                        }
                        break;
                }
            }
        }

        private ScreensaverMode ParseScreensaverMode(string s)
        {
            ScreensaverMode mode = ScreensaverMode.None;

            switch (s)
            {
                case "model": mode = ScreensaverMode.Model; break;
                case "name": mode = ScreensaverMode.Name; break;
                case "callsign": mode = ScreensaverMode.Callsign; break;
            }

            return mode;
        }

        private string ScreensaverModeToString(ScreensaverMode mode)
        {
            string s = "";

            switch (mode)
            {
                case ScreensaverMode.Model: s = "model"; break;
                case ScreensaverMode.Name: s = "name"; break;
                case ScreensaverMode.Callsign: s = "callsign"; break;
            }

            return s;
        }

        private ScreensaverMode _screensaver;
        /// <summary>
        /// Sets the screensaver mode to be shown on the front display of
        /// the radio (Model, Name, Callsign, None).
        /// </summary>
        public ScreensaverMode Screensaver
        {
            get { return _screensaver; }
            set
            {
                if (_screensaver != value)
                {
                    _screensaver = value;
                    SendCommand("radio screensaver " + ScreensaverModeToString(_screensaver));
                    RaisePropertyChanged("Screensaver");
                }
            }
        }

        private string _callsign;
        /// <summary>
        /// The Callsign string to be stored in the radio to be shown
        /// on the front display if the Callsign ScreensaverMode is
        /// selected with the Screensaver property.
        /// </summary>
        public string Callsign
        {
            get => _callsign;
            set
            {
                string new_value = StringHelper.Sanitize(value.ToUpper());

                if (_callsign != new_value)
                {
                    _callsign = new_value;
                    SendCommand("radio callsign " + _callsign);
                    RaisePropertyChanged(nameof(Callsign));
                }
                else if (new_value != value)
                {
                    RaisePropertyChanged(nameof(Callsign));
                }
            }
        }

        private string _nickname;
        /// <summary>
        /// The Nickname string to be stored in the radio to be shown
        /// on the front display if the Name ScreensaverMode is
        /// selected with the Screensaver property.
        /// </summary>
        public string Nickname
        {
            get { return _nickname; }
            set
            {
                string new_value = StringHelper.Sanitize(value);

                if (_nickname != new_value)
                {
                    _nickname = new_value;
                    SendCommand("radio name " + _nickname);
                    RaisePropertyChanged(nameof(Nickname));
                }
                else if (new_value != value)
                {
                    RaisePropertyChanged(nameof(Nickname));
                }
            }
        }

        private int _num_tx = 0;
        /// <summary>
        /// The number of transmitters that are currently available and 
        /// enabled on the radio.  Typically NumTX will be 0 if there are
        /// no transmitters enabled and will be 1 if there is a slice that is
        /// set to TX.
        /// </summary>
        public int NumTX
        {
            get { return _num_tx; }
        }

        #endregion

        #region Interlock Routines

        private int _interlockTimeout; // in ms
        /// <summary>
        /// The timeout period for the transmitter to be keyed continuously, in milliseconds.
        /// If set to 120000, the transmitter can be keyed for 2 
        /// minutes continuously before begin unkeyed automatically.
        /// </summary>
        public int InterlockTimeout
        {
            get { return _interlockTimeout; }
            set
            {
                if (_interlockTimeout != value)
                {
                    _interlockTimeout = value;
                    SendCommand("interlock timeout=" + _interlockTimeout);
                    RaisePropertyChanged("InterlockTimeout");
                }
            }
        }

        private bool _txreqRCAEnabled;
        /// <summary>
        /// Enables or disables the Transmit Request functionality
        /// via the Accessory input on the back panel of the radio.
        /// </summary>
        public bool TXReqRCAEnabled
        {
            get { return _txreqRCAEnabled; }
            set
            {
                if (_txreqRCAEnabled != value)
                {
                    _txreqRCAEnabled = value;
                    SendCommand("interlock rca_txreq_enable=" + Convert.ToByte(_txreqRCAEnabled));
                    RaisePropertyChanged("TXReqRCAEnabled");
                }
            }
        }

        private bool _txreqACCEnabled;
        /// <summary>
        /// Enables or disables the Transmit Request (TX REQ) RCA 
        /// input on the back panel of the radio.
        /// </summary>
        public bool TXReqACCEnabled
        {
            get { return _txreqACCEnabled; }
            set
            {
                if (_txreqACCEnabled != value)
                {
                    _txreqACCEnabled = value;
                    SendCommand("interlock acc_txreq_enable=" + Convert.ToByte(_txreqACCEnabled));
                    RaisePropertyChanged("TXReqACCEnabled");
                }
            }
        }

        private bool _txreqRCAPolarity;
        /// <summary>
        /// The polartiy of the Transmit Request (TX REQ) RCA input on the back panel
        /// of the radio.  When true, TX REQ is active high.
        /// When false, TX REQ is active low. The RCA port must be enabled by 
        /// the TXReqRCAEnabled property.
        /// </summary>
        public bool TXReqRCAPolarity
        {
            get { return _txreqRCAPolarity; }
            set
            {
                if (_txreqRCAPolarity != value)
                {
                    _txreqRCAPolarity = value;
                    SendCommand("interlock rca_txreq_polarity=" + Convert.ToByte(_txreqRCAPolarity));
                    RaisePropertyChanged("TXReqRCAPolarity");
                }
            }
        }

        private bool _txreqACCPolarity;
        /// <summary>
        /// The polartiy of the Transmit Request input via the Accessory port 
        /// on the back panel of the radio.  When true, TX REQ is active high.
        /// When false, TX REQ is active low. TX REQ functionality via the
        /// Accessory port must be enabled by the TXReqACCEnabled property.
        /// </summary>
        public bool TXReqACCPolarity
        {
            get { return _txreqACCPolarity; }
            set
            {
                if (_txreqACCPolarity != value)
                {
                    _txreqACCPolarity = value;
                    SendCommand("interlock acc_txreq_polarity=" + Convert.ToByte(_txreqACCPolarity));
                    RaisePropertyChanged("TXReqACCPolarity");
                }
            }
        }

        private InterlockState _interlockState = InterlockState.Ready;
        /// <summary>
        /// Gets the Interlock State of the transmitter: None, Receive,
        /// NotReady, PTTRequested, Transmitting, TXFault, Timeout,
        /// StuckInput
        /// </summary>
        public InterlockState InterlockState
        {
            get { return _interlockState; }
            internal set
            {
                if (_interlockState != value)
                {
                    _interlockState = value;
                    RaisePropertyChanged("InterlockState");

                    bool new_mox_state = IsInterlockMox(_interlockState) && ShouldUpdateMoxOrTuneState(_txClientHandle);

                    if (_mox != new_mox_state)
                    {
                        _mox = new_mox_state;
                        RaisePropertyChanged("Mox");
                    }
                }
            }
        }

        private bool IsInterlockMox(InterlockState state)
        {
            bool ret_val = false;

            switch (state)
            {
                case InterlockState.Transmitting:
                case InterlockState.PTTRequested:
                case InterlockState.UnkeyRequested:
                    ret_val = true;
                    break;
            }

            return ret_val;
        }

        private bool ShouldUpdateMoxOrTuneState(uint txClientHandle)
        {
            // does the TX Client Handle match this client's handle?
            if (_clientHandle == txClientHandle || txClientHandle == 0)
                return true;

            // are we bound to a GUIClient?
            if (_boundClientID == null)
            {
                // no
                return true;
            }
            else
            {
                // yes -- find the GUIClient
                GUIClient bound_guiClient = FindGUIClientByClientID(_boundClientID);

                // Did we find a good reference to the client?
                if (bound_guiClient != null)
                {
                    // Does the GUIClient's Client Handle match the TX Client Handle?
                    if (bound_guiClient.ClientHandle == txClientHandle || txClientHandle == 0)
                        return true;
                }
            }

            return false;
        }

        private PTTSource _pttSource;
        /// <summary>
        /// Gets the current push to talk (PTT) source of the radio:
        /// SW, Mic, ACC, RCA.
        /// </summary>
        public PTTSource PTTSource
        {
            get { return _pttSource; }
            internal set
            {
                if (_pttSource != value)
                {
                    _pttSource = value;
                    RaisePropertyChanged("PTTSource");
                }
            }
        }

        private InterlockReason _interlockReason;
        /// <summary>
        /// Gets the radio's reasoning for the current InterlockState
        /// </summary>
        public InterlockReason InterlockReason
        {
            get { return _interlockReason; }
            internal set
            {
                if (_interlockReason != value)
                {
                    _interlockReason = value;
                    RaisePropertyChanged("InterlockReason");

                    if (_interlockReason == InterlockReason.CLIENT_TX_INHIBIT)
                    {
                        _txInhibit = true;
                        RaisePropertyChanged("TXInhibit");
                    }
                }
            }
        }

        private int _delayTX;
        /// <summary>
        /// The delay duration between keying the radio and transmit in milliseconds
        /// </summary>
        public int DelayTX
        {
            get { return _delayTX; }
            set
            {
                if (_delayTX != value)
                {
                    _delayTX = value;
                    SendCommand("interlock tx_delay=" + _delayTX);
                    RaisePropertyChanged("DelayTX");
                }
            }
        }

        private bool _tx1Enabled;
        /// <summary>
        /// Enables the TX1 Transmit Relay RCA output port on the back panel of the radio
        /// </summary>
        public bool TX1Enabled
        {
            get { return _tx1Enabled; }
            set
            {
                if (_tx1Enabled != value)
                {
                    _tx1Enabled = value;
                    SendCommand("interlock tx1_enabled=" + Convert.ToByte(_tx1Enabled));
                    RaisePropertyChanged("TX1Enabled");
                }
            }
        }

        private bool _tx2Enabled;
        /// <summary>
        /// Enables the TX2 Transmit Relay RCA output port on the back panel of the radio
        /// </summary>
        public bool TX2Enabled
        {
            get { return _tx2Enabled; }
            set
            {
                if (_tx2Enabled != value)
                {
                    _tx2Enabled = value;
                    SendCommand("interlock tx2_enabled=" + Convert.ToByte(_tx2Enabled));
                    RaisePropertyChanged("TX2Enabled");
                }
            }
        }

        private bool _tx3Enabled;
        /// <summary>
        /// Enables the TX3 Transmit Relay RCA output port on the back panel of the radio
        /// </summary>
        public bool TX3Enabled
        {
            get { return _tx3Enabled; }
            set
            {
                if (_tx3Enabled != value)
                {
                    _tx3Enabled = value;
                    SendCommand("interlock tx3_enabled=" + Convert.ToByte(_tx3Enabled));
                    RaisePropertyChanged("TX3Enabled");
                }
            }
        }

        private bool _txACCEnabled;
        /// <summary>
        /// Enables the Transmit Relay output via the Accessory port
        /// on the back panel of the radio
        /// </summary>
        public bool TXACCEnabled
        {
            get { return _txACCEnabled; }
            set
            {
                if (_txACCEnabled != value)
                {
                    _txACCEnabled = value;
                    SendCommand("interlock acc_tx_enabled=" + Convert.ToByte(_txACCEnabled));
                    RaisePropertyChanged("TXACCEnabled");
                }
            }
        }

        private int _tx1Delay;
        /// <summary>
        /// The delay in milliseconds (ms) for the TX1 RCA output relay.  This
        /// port must be enabled by setting the TX1Enabled property
        /// </summary>
        public int TX1Delay
        {
            get { return _tx1Delay; }
            set
            {
                if (_tx1Delay != value)
                {
                    _tx1Delay = value;
                    SendCommand("interlock tx1_delay=" + _tx1Delay);
                    RaisePropertyChanged("TX1Delay");
                }
            }
        }

        private int _tx2Delay;
        /// <summary>
        /// The delay in milliseconds (ms) for the TX2 RCA output relay.  This
        /// port must be enabled by setting the TX2Enabled property
        /// </summary>
        public int TX2Delay
        {
            get { return _tx2Delay; }
            set
            {
                if (_tx2Delay != value)
                {
                    _tx2Delay = value;
                    SendCommand("interlock tx2_delay=" + _tx2Delay);
                    RaisePropertyChanged("TX2Delay");
                }
            }
        }

        private int _tx3Delay;
        /// <summary>
        /// The delay in milliseconds (ms) for the TX3 RCA output relay.  This
        /// port must be enabled by setting the TX3Enabled property
        /// </summary>
        public int TX3Delay
        {
            get { return _tx3Delay; }
            set
            {
                if (_tx3Delay != value)
                {
                    _tx3Delay = value;
                    SendCommand("interlock tx3_delay=" + _tx3Delay);
                    RaisePropertyChanged("TX3Delay");
                }
            }
        }

        private int _txACCDelay;
        /// <summary>
        /// The delay in milliseconds (ms) for the Transmit Relay output pin via the
        /// Accessory port on the back panel of the radio.  This
        /// pin must be enabled by setting the TXACCEnabled property
        /// </summary>
        public int TXACCDelay
        {
            get { return _txACCDelay; }
            set
            {
                if (_txACCDelay != value)
                {
                    _txACCDelay = value;
                    SendCommand("interlock acc_tx_delay=" + _txACCDelay);
                    RaisePropertyChanged("TXACCDelay");
                }
            }
        }

        private bool _remoteOnEnabled;
        /// <summary>
        /// Enables the remote on "REM ON" RCA input port on the back 
        /// panel of the radio.
        /// </summary>
        public bool RemoteOnEnabled
        {
            get { return _remoteOnEnabled; }
            set
            {
                if (_remoteOnEnabled != value)
                {
                    _remoteOnEnabled = value;
                    SendCommand("radio set remote_on_enabled=" + Convert.ToByte(_remoteOnEnabled));
                    RaisePropertyChanged("RemoteOnEnabled");
                }
            }
        }

        private InterlockReason ParseInterlockReason(string s)
        {
            InterlockReason reason = InterlockReason.None;

            switch (s)
            {
                case "RCA_TXREQ": reason = InterlockReason.RCA_TXREQ; break;
                case "ACC_TXREQ": reason = InterlockReason.ACC_TXREQ; break;
                case "BAD_MODE": reason = InterlockReason.BAD_MODE; break;
                case "TUNED_TOO_FAR": reason = InterlockReason.TUNED_TOO_FAR; break;
                case "OUT_OF_BAND": reason = InterlockReason.OUT_OF_BAND; break;
                case "OUT_OF_PA_RANGE": reason = InterlockReason.PA_RANGE; break;
                case "CLIENT_TX_INHIBIT": reason = InterlockReason.CLIENT_TX_INHIBIT; break;
                case "XVTR_RX_ONLY": reason = InterlockReason.XVTR_RX_ONLY; break;
                case "NO_TX_ASSIGNED": reason = InterlockReason.NO_TX_ASSIGNED; break;
                case "AMP:TG": reason = InterlockReason.TGXL; break;
            }

            return reason;
        }

        private InterlockState ParseInterlockState(string s)
        {
            InterlockState state = InterlockState.None;
            switch (s)
            {
                case "RECEIVE": state = InterlockState.Receive; break;
                case "READY": state = InterlockState.Ready; break;
                case "NOT_READY": state = InterlockState.NotReady; break;
                case "PTT_REQUESTED": state = InterlockState.PTTRequested; break;
                case "TRANSMITTING": state = InterlockState.Transmitting; break;
                case "TX_FAULT": state = InterlockState.TXFault; break;
                case "TIMEOUT": state = InterlockState.Timeout; break;
                case "STUCK_INPUT": state = InterlockState.StuckInput; break;
                case "UNKEY_REQUESTED": state = InterlockState.UnkeyRequested; break;
            }

            return state;
        }

        private ATUTuneStatus ParseATUTuneStatus(string s)
        {
            ATUTuneStatus status = ATUTuneStatus.None;
            // Check if external tuner is connected and valid. If it is, set TGXL status based on whether or not it is tuning.
            if (radioShouldUseExtTuner)
            {
                status = ActiveTuner.IsTuning ? ATUTuneStatus.TGXL_InProgress : ATUTuneStatus.TGXL_OK;
                return status;
            }

            switch (s)
            {
                case "NONE": status = ATUTuneStatus.None; break;
                case "TUNE_NOT_STARTED": status = ATUTuneStatus.NotStarted; break;
                case "TUNE_IN_PROGRESS": status = ATUTuneStatus.InProgress; break;
                case "TUNE_BYPASS": status = ATUTuneStatus.Bypass; break;
                case "TUNE_SUCCESSFUL": status = ATUTuneStatus.Successful; break;
                case "TUNE_OK": status = ATUTuneStatus.OK; break;
                case "TUNE_FAIL_BYPASS": status = ATUTuneStatus.FailBypass; break;
                case "TUNE_FAIL": status = ATUTuneStatus.Fail; break;
                case "TUNE_ABORTED": status = ATUTuneStatus.Aborted; break;
                case "TUNE_MANUAL_BYPASS": status = ATUTuneStatus.ManualBypass; break;
            }
            
            return status;
        }

        private PTTSource ParsePTTSource(string s)
        {
            PTTSource source = PTTSource.None;

            switch (s)
            {
                case "SW": source = PTTSource.SW; break;
                case "MIC": source = PTTSource.Mic; break;
                case "ACC": source = PTTSource.ACC; break;
                case "RCA": source = PTTSource.RCA; break;
                case "TUNE": source = PTTSource.TUNE; break;
            }
            return source;
        }

        private string _einterlockAmplifierHandlesCsv;

        public string EinterlockAmplifierHandlesCsv
        {
            get { return _einterlockAmplifierHandlesCsv; }
            set
            {
                _einterlockAmplifierHandlesCsv = value;
                RaisePropertyChanged("EinterlockAmplifierHandlesCsv");
            }
        }

        private void ParseInterlockStatus(string s)
        {
            string[] words = s.Split(' ');

            if (words.Length == 0)
            {
                return;
            }

            if (words[0] == "band")
            {
                ParseTxBandSettingsStatus(s);
                return;
            }

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    if (!string.IsNullOrEmpty(kv)) Debug.WriteLine($"Radio::ParseInterlockStatus: Invalid key/value pair ({kv})");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "amplifier":
                        {
                            // comma separated list of amplifier handles
                            EinterlockAmplifierHandlesCsv = value;
                        }
                        break;

                    case "tx_allowed":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - tx_allowed: Invalid value (" + kv + ")");
                                continue;
                            }

                            _txAallowed = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TXAllowed");
                            break;
                        }

                    case "tx_client_handle":
                        {
                            uint txClientHandle;
                            string txHandleStr = value.Substring("0x".Length);
                            bool b = uint.TryParse(txHandleStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out txClientHandle);
                            if (!b)
                            {
                                continue;
                            }

                            TXClientHandle = txClientHandle;
                        }
                        break;

                    case "state":
                        {
                            InterlockState state = ParseInterlockState(value);
                            if (state == InterlockState.None)
                            {
                                Debug.WriteLine("ParseInterlockStatus: Error - Invalid state (" + value + ")");
                                continue;
                            }

                            InterlockState = state;
                        }
                        break;

                    case "source":
                        {
                            PTTSource source = ParsePTTSource(value);
                            if (!string.IsNullOrEmpty(value) && source == PTTSource.None)
                            {
                                Debug.WriteLine("ParseInterlockStatus: Error - Invalid PTT Source (" + value + ")");
                                continue;
                            }

                            PTTSource = source;
                        }
                        break;

                    case "reason":
                        {
                            InterlockReason reason = ParseInterlockReason(value);
                            if (!string.IsNullOrEmpty(value) && reason == InterlockReason.None && !value.Contains("PG-XL"))
                            {
                                Debug.WriteLine("ParseInterlockStatus: Error - Invalid reason (" + value + ")");
                                continue;
                            }

                            InterlockReason = reason;
                        }
                        break;

                    case "timeout":
                        {
                            uint timeout;
                            bool b = uint.TryParse(value, out timeout);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus: Inavlid timeout value (" + value + ")");
                                continue;
                            }

                            _interlockTimeout = (int)timeout;
                            RaisePropertyChanged("InterlockTimeout");
                        }
                        break;

                    case "acc_txreq_enable":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus - acc_txreq_enable: Invalid value (" + value + ")");
                                continue;
                            }

                            _txreqACCEnabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TXReqACCEnabled");
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

                            _txreqRCAEnabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TXReqRCAEnabled");
                        }
                        break;

                    case "acc_txreq_polarity":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus - acc_txreq_polarity: Invalid value (" + value + ")");
                                continue;
                            }

                            _txreqACCPolarity = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TXReqACCPolarity");
                        }
                        break;

                    case "rca_txreq_polarity":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus - rca_txreq_polarity: Invalid value (" + value + ")");
                                continue;
                            }

                            _txreqRCAPolarity = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TXReqRCAPolarity");
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

                            _tx1Enabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TX1Enabled");
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

                            _tx2Enabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TX2Enabled");
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

                            _tx3Enabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TX3Enabled");
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

                            _txACCEnabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TXACCEnabled");
                        }
                        break;

                    case "tx1_delay":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus - tx1_delay: Invalid value (" + value + ")");
                                continue;
                            }

                            _tx1Delay = temp;
                            RaisePropertyChanged("TX1Delay");
                        }
                        break;

                    case "tx2_delay":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus - tx2_delay: Invalid value (" + value + ")");
                                continue;
                            }

                            _tx2Delay = temp;
                            RaisePropertyChanged("TX2Delay");
                        }
                        break;

                    case "tx3_delay":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus - tx3_delay: Invalid value (" + value + ")");
                                continue;
                            }

                            _tx3Delay = temp;
                            RaisePropertyChanged("TX3Delay");
                        }
                        break;

                    case "acc_tx_delay":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus - acc_tx_delay: Invalid value (" + value + ")");
                                continue;
                            }

                            _txACCDelay = temp;
                            RaisePropertyChanged("TXACCDelay");
                        }
                        break;

                    case "tx_delay":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("ParseInterlockStatus - tx_delay: Invalid value (" + value + ")");
                                continue;
                            }

                            _delayTX = temp;
                            RaisePropertyChanged("DelayTX");
                        }
                        break;
                }
            }
        }

        #endregion

        #region Transmit Routines

        private int _maxPowerLevel;
        /// <summary>
        /// The maximum power level (on relative scale 0-100, non-linear) that the radio will transmit when using the PA
        /// </summary>
        public int MaxPowerLevel
        {
            get { return _maxPowerLevel; }
            set
            {
                int new_power = value;

                // check limits
                if (new_power < 0) new_power = 0;
                if (new_power > 100) new_power = 100;

                if (_maxPowerLevel != new_power)
                {
                    _maxPowerLevel = new_power;
                    SendCommand("transmit set max_power_level=" + _maxPowerLevel);
                    RaisePropertyChanged("MaxPowerLevel");
                }
                else if (new_power != value)
                {
                    RaisePropertyChanged("MaxPowerLevel");
                }
            }
        }


        private int _rfPower;
        /// <summary>
        /// The transmit RF power level in Watts, from 0 to 100.
        /// </summary>
        public int RFPower
        {
            get { return _rfPower; }
            set
            {
                int new_power = value;

                // check limits
                if (new_power < 0) new_power = 0;
                if (new_power > 100) new_power = 100;

                if (_rfPower != new_power)
                {
                    _rfPower = new_power;
                    SendCommand("transmit set rfpower=" + _rfPower);
                    RaisePropertyChanged("RFPower");
                }
                else if (new_power != value)
                {
                    RaisePropertyChanged("RFPower");
                }
            }
        }

        private int _tunePower;
        /// <summary>
        /// The transmit RF power level for Tune in Watts, from 0 to 100
        /// </summary>
        public int TunePower
        {
            get { return _tunePower; }
            set
            {
                int new_power = value;

                // check limits
                if (new_power < 0) new_power = 0;
                if (new_power > 100) new_power = 100;

                if (_tunePower != new_power)
                {
                    _tunePower = new_power;
                    SendCommand("transmit set tunepower=" + _tunePower);
                    RaisePropertyChanged("TunePower");
                }
                else if (new_power != value)
                {
                    RaisePropertyChanged("TunePower");
                }
            }
        }

        private int _amCarrierLevel;
        /// <summary>
        /// The AM Carrier level in Watts, from 0 to 100
        /// </summary>
        public int AMCarrierLevel
        {
            get { return _amCarrierLevel; }
            set
            {
                int new_level = value;

                // check limits
                if (new_level < 0) new_level = 0;
                if (new_level > 100) new_level = 100;

                if (_amCarrierLevel != new_level)
                {
                    _amCarrierLevel = new_level;
                    SendCommand("transmit set am_carrier=" + _amCarrierLevel);
                    RaisePropertyChanged("AMCarrierLevel");
                }
                else if (new_level != value)
                {
                    RaisePropertyChanged("AMCarrierLevel");
                }
            }
        }

        [Obsolete("Profiles are now saved automatically with changes. Use CreateTXProfile() to create a new TX profile.", error: true)]
        public void SaveTXProfile(string profile_name)
        {
            if (profile_name != null && profile_name != "")
            {
                SendCommand("profile transmit save \"" + profile_name.Replace("*", "") + "\"");
            }
        }

        public void CreateTXProfile(string profile_name)
        {
            if (profile_name != null && profile_name != "")
            {
                SendCommand("profile transmit create \"" + profile_name.Replace("*", "") + "\"");
            }
        }

        public void ResetTXProfile(string profile_name)
        {
            if (profile_name != null && profile_name != "")
            {
                SendCommand("profile transmit reset \"" + profile_name.Replace("*", "") + "\"");
            }
        }

        public void DeleteTXProfile(string profile_name)
        {
            if (profile_name != null && profile_name != "")
            {
                SendCommand("profile transmit delete \"" + profile_name.Replace("*", "") + "\"");
            }
        }

        public void DeleteMICProfile(string profile_name)
        {
            if (profile_name != null && profile_name != "")
            {
                SendCommand("profile mic delete \"" + profile_name.Replace("*", "") + "\"");
            }
        }

        [Obsolete("Profiles are now saved automatically with changes. Use CreateMICProfile() to create a new MIC profile.", error: true)]
        public void SaveMICProfile(string profile_name)
        {
            if (profile_name != null && profile_name != "")
            {
                SendCommand("profile mic save \"" + profile_name.Replace("*", "") + "\"");
            }
        }

        public void ResetMICProfile(string profile_name)
        {
            if (profile_name != null && profile_name != "")
            {
                SendCommand("profile mic reset \"" + profile_name.Replace("*", "") + "\"");
            }
        }

        public void CreateMICProfile(string profile_name)
        {
            if (profile_name != null && profile_name != "")
            {
                SendCommand("profile mic create \"" + profile_name.Replace("*", "") + "\"");
            }
        }

        /// <summary>
        /// Send the profile autosave command to turn autosave on or off
        /// </summary>
        public void AutoSaveProfile(string state)
        {
            SendCommand($"profile autosave \"{state}\"");
        }

        public void SaveGlobalProfile(string profile_name)
        {
            if (profile_name != null && profile_name != "")
            {
                SendCommand("profile global save \"" + profile_name + "\"");
            }
        }

        public void DeleteGlobalProfile(string profile_name)
        {
            if (profile_name != null && profile_name != "")
            {
                SendCommand("profile global delete \"" + profile_name + "\"");
            }
        }

        public void UninstallWaveform(string waveform_name)
        {
            if (waveform_name != null && waveform_name != "")
            {
                SendCommand("waveform uninstall " + waveform_name);
            }
        }


        private ObservableCollection<string> _profileMICList;
        public ObservableCollection<string> ProfileMICList
        {
            get
            {
                if (_profileMICList == null)
                    return new ObservableCollection<string>();
                return new ObservableCollection<string>(_profileMICList);
            }

        }

        private string _profileMICSelection;
        public string ProfileMICSelection
        {
            get { return _profileMICSelection; }
            set
            {
                if (_profileMICSelection != value)
                {
                    _profileMICSelection = value;
                    if (_profileMICSelection != null &&
                        _profileMICSelection != "")
                    {
                        SendCommand("profile mic load \"" + _profileMICSelection + "\"");
                        RaisePropertyChanged("ProfileMICSelection");
                    }
                }
            }
        }

        private ObservableCollection<string> _profileTXList;
        public ObservableCollection<string> ProfileTXList
        {
            get
            {
                if (_profileTXList == null)
                    return new ObservableCollection<string>();
                return new ObservableCollection<string>(_profileTXList);
            }

        }

        private string _profileTXSelection;
        public string ProfileTXSelection
        {
            get { return _profileTXSelection; }
            set
            {
                _profileTXSelection = value;
                if (_profileTXSelection != null &&
                    _profileTXSelection != "")
                {
                    _profileTXSelection = _profileTXSelection.Replace("*", "");
                    SendCommand("profile tx load \"" + _profileTXSelection + "\"");
                    RaisePropertyChanged("ProfileTXSelection");
                }
            }
        }

        private ObservableCollection<string> _profileDisplayList;
        public ObservableCollection<string> ProfileDisplayList
        {
            get
            {
                if (_profileDisplayList == null)
                    return new ObservableCollection<string>();
                return _profileDisplayList;
            }
        }

        private string _profileDisplaySelection;
        public string ProfileDisplaySelection
        {
            get { return _profileDisplaySelection; }
            set
            {
                if (_profileDisplaySelection != value)
                {
                    _profileDisplaySelection = value;
                    if (_profileDisplaySelection != null &&
                        _profileDisplaySelection != "")
                    {
                        SendCommand("profile display load \"" + _profileDisplaySelection + "\"");
                        RaisePropertyChanged("ProfileDisplaySelection");
                    }
                }
            }
        }


        private ObservableCollection<string> _profileGlobalList;
        public ObservableCollection<string> ProfileGlobalList
        {
            get
            {
                if (_profileGlobalList == null)
                    return new ObservableCollection<string>();
                return new ObservableCollection<string>(_profileGlobalList);
            }
        }

        private string _profileGlobalSelection;
        public string ProfileGlobalSelection
        {
            get { return _profileGlobalSelection; }
            set
            {
                _profileGlobalSelection = value;
                if (_profileGlobalSelection != null &&
                    _profileGlobalSelection != "")
                {
                    SendCommand("profile global load \"" + _profileGlobalSelection + "\"");
                    RaisePropertyChanged("ProfileGlobalSelection");
                }
            }
        }

        private void UpdateProfileMicList(string s)
        {
            string[] inputs = s.Split('^');

            _profileMICList = new ObservableCollection<string>();
            foreach (string profile in inputs)
            {
                if (profile != "")
                {
                    _profileMICList.Add(profile);
                }
            }
            RaisePropertyChanged("ProfileMICList");
        }

        private void UpdateProfileTxList(string s)
        {
            string[] inputs = s.Split('^');

            _profileTXList = new ObservableCollection<string>();
            foreach (string profile in inputs)
            {
                if (profile != "")
                {
                    _profileTXList.Add(profile);
                }
            }
            RaisePropertyChanged("ProfileTXList");
        }

        private void UpdateProfileDisplayList(string s)
        {
            string[] inputs = s.Split('^');

            _profileDisplayList = new ObservableCollection<string>();
            foreach (string profile in inputs)
            {
                if (profile != "")
                {
                    _profileDisplayList.Add(profile);
                }
            }
            RaisePropertyChanged("ProfileDisplayList");
        }

        private void UpdateProfileGlobalList(string s)
        {
            string[] inputs = s.Split('^');

            _profileGlobalList = new ObservableCollection<string>();
            foreach (string profile in inputs)
            {
                if (profile != "")
                {
                    _profileGlobalList.Add(profile);
                }
            }

            RaisePropertyChanged("ProfileGlobalList");
        }

        private void GetProfileLists()
        {
            SendCommand("profile global info");
            SendCommand("profile tx info");
            SendCommand("profile mic info");
            SendCommand("profile display info");
        }

        private void GetMicList()
        {
            SendReplyCommand(new ReplyHandler(UpdateMicList), "mic list");
        }

        private void UpdateMicList(int seq, uint resp_val, string s)
        {
            if (resp_val != 0) return;

            string[] inputs = s.Split(',');

            _micInputList = new List<string>();
            foreach (string mic in inputs)
                _micInputList.Add(mic);

            RaisePropertyChanged("MicInputList");
            RaisePropertyChanged("MicInput");
        }

        private List<string> _micInputList;
        /// <summary>
        /// A list of the available mic inputs
        /// </summary>
        public List<string> MicInputList
        {
            get { return _micInputList; }

            /*set
            internal set
            {
                _micInputList = value;
                RaisePropertyChanged("MicInputList");
            }*/
        }

        private string _micInput;
        /// <summary>
        /// The currently selected mic input
        /// </summary>
        public string MicInput
        {
            get { return _micInput; }
            set
            {
                if (_micInput != value)
                {
                    _micInput = value;
                    if (_micInput != null)
                        SendCommand("mic input " + _micInput.ToUpper());
                    RaisePropertyChanged("MicInput");
                }
            }
        }

        System.Timers.Timer _networkQualityTimer = new System.Timers.Timer(1000);
        int packetErrorCount = 0;
        int lastPacketErrorCount = 0;
        bool packet_lost = false;
        NetworkIndicatorState currentState = NetworkIndicatorState.STATE_EXCELLENT;
        NetworkIndicatorState nextState = NetworkIndicatorState.STATE_EXCELLENT;
        int state_countdown = 0;

        public void MonitorNetworkQuality()
        {
            _networkQualityTimer.AutoReset = true;
            _networkQualityTimer.Elapsed += MonitorNetworkQualityTask;
            _networkQualityTimer.Enabled = true;
        }

        private enum NetworkIndicatorState
        {
            STATE_OFF,
            STATE_EXCELLENT,
            STATE_VERY_GOOD,
            STATE_GOOD,
            STATE_FAIR,
            STATE_POOR
        }

        private void MonitorNetworkQualityTask(object obj, ElapsedEventArgs args)
        {
            if (!_connected)
            {
                _networkQualityTimer.Enabled = false;
                return;
            }

            //Console.WriteLine("Address: {0}", reply.Address.ToString());
            //Console.WriteLine("RoundTrip time: {0}", reply.RoundtripTime);
            //Console.WriteLine("Time to live: {0}", reply.Options.Ttl);
            //Console.WriteLine("Don't fragment: {0}", reply.Options.DontFragment);
            //Console.WriteLine("Buffer size: {0}", reply.Buffer.Length);
            //Console.WriteLine("---");

            NetworkPing = Convert.ToInt32(_lastPingRTT);

            lastPacketErrorCount = packetErrorCount;
            int totalPacketCount = 0;

            packetErrorCount = _meterPacketErrorCount;
            totalPacketCount = _meterPacketTotalCount;

            if (_rxRemoteAudioStreams.Count > 0)
            {
                packetErrorCount += _rxRemoteAudioStreams[0].ErrorCount;
                totalPacketCount += _rxRemoteAudioStreams[0].TotalCount;
            }

            lock (_panadapters)
            {
                foreach (Panadapter p in _panadapters)
                {
                    packetErrorCount += p.FFTPacketErrorCount;
                    totalPacketCount += p.FFTPacketTotalCount;
                }
            }

            lock (_waterfalls)
            {
                foreach (Waterfall w in _waterfalls)
                {
                    packetErrorCount += w.FallPacketErrorCount;
                    totalPacketCount += w.FallPacketTotalCount;
                }
            }

            if (!IsWan) // LAN
            {
                packet_lost = (packetErrorCount > lastPacketErrorCount);
            }
            else // SmartLink
            {
                switch (currentState)
                {
                    case NetworkIndicatorState.STATE_EXCELLENT:
                    case NetworkIndicatorState.STATE_VERY_GOOD:
                    case NetworkIndicatorState.STATE_GOOD:
                        packet_lost = ((packetErrorCount / (double)totalPacketCount) > 0.02);
                        break;
                    case NetworkIndicatorState.STATE_FAIR:
                    case NetworkIndicatorState.STATE_POOR:
                        packet_lost = ((packetErrorCount / (double)totalPacketCount) > 0.05);
                        break;
                }
            }

            // order of operations is:
            // 1. Check to see if we need to move down in state
            // 2. Check to see if we can move up (countdown == 0).
            // 3. If yes, set the countdown and the state
            // 4. If not, decrement state_countdown


            int ping_poor_threshold_ms;
            int ping_fair_threshold_ms;

            if (!IsWan)
            {
                ping_poor_threshold_ms = NETWORK_LAN_PING_POOR_THRESHOLD_MS;
                ping_fair_threshold_ms = NETWORK_LAN_PING_FAIR_THRESHOLD_MS;
            }
            else
            {
                ping_poor_threshold_ms = NETWORK_SMARTLINK_PING_POOR_THRESHOLD_MS;
                ping_fair_threshold_ms = NETWORK_SMARTLINK_PING_FAIR_THRESHOLD_MS;
            }

            switch (currentState)
            {
                case NetworkIndicatorState.STATE_EXCELLENT:
                    // down
                    if (_networkPing >= ping_poor_threshold_ms)
                        nextState = NetworkIndicatorState.STATE_POOR;
                    else if (_networkPing >= ping_fair_threshold_ms &&
                        _networkPing < ping_poor_threshold_ms)
                        nextState = NetworkIndicatorState.STATE_GOOD;
                    else if (packet_lost)
                        nextState = NetworkIndicatorState.STATE_VERY_GOOD;
                    break;

                case NetworkIndicatorState.STATE_VERY_GOOD:
                    // down
                    if (_networkPing >= ping_poor_threshold_ms)
                    {
                        nextState = NetworkIndicatorState.STATE_POOR;
                        state_countdown = 5;
                    }
                    else if (_networkPing >= ping_fair_threshold_ms &&
                        _networkPing < ping_poor_threshold_ms ||
                        packet_lost)
                    {
                        nextState = NetworkIndicatorState.STATE_GOOD;
                        state_countdown = 5;
                    }
                    else // up
                    {
                        if (state_countdown-- == 0)
                        {
                            nextState = NetworkIndicatorState.STATE_EXCELLENT;
                            state_countdown = 5;
                        }
                    }
                    break;

                case NetworkIndicatorState.STATE_GOOD:
                    // down
                    if (_networkPing >= ping_poor_threshold_ms)
                    {
                        nextState = NetworkIndicatorState.STATE_POOR;
                        state_countdown = 5;
                    }
                    else if (packet_lost)
                    {
                        nextState = NetworkIndicatorState.STATE_FAIR;
                        state_countdown = 5;
                    }
                    else if (_networkPing < ping_fair_threshold_ms)
                    {
                        if (state_countdown-- == 0)
                        {
                            nextState = NetworkIndicatorState.STATE_VERY_GOOD;
                            state_countdown = 5;
                        }
                    }
                    break;

                case NetworkIndicatorState.STATE_FAIR:
                    if (_networkPing >= ping_poor_threshold_ms ||
                        packet_lost)
                    {
                        nextState = NetworkIndicatorState.STATE_POOR;
                        state_countdown = 5;
                    }
                    else
                    {
                        if (state_countdown-- == 0)
                        {
                            nextState = NetworkIndicatorState.STATE_GOOD;
                            state_countdown = 5;
                        }
                    }
                    break;

                case NetworkIndicatorState.STATE_POOR:
                    if (_networkPing < ping_poor_threshold_ms)
                    {
                        if (state_countdown-- == 0)
                        {
                            nextState = NetworkIndicatorState.STATE_FAIR;
                            state_countdown = 5;
                        }
                    }
                    break;

                case NetworkIndicatorState.STATE_OFF:
                    nextState = NetworkIndicatorState.STATE_POOR;
                    state_countdown = 5;
                    break;
            }

            //Debug.WriteLine("Network Indicator State: " + currentState.ToString() + " --> " + nextState.ToString());

            switch (nextState)
            {
                case NetworkIndicatorState.STATE_EXCELLENT:
                    _remoteNetworkQuality = NetworkQuality.EXCELLENT;
                    break;
                case NetworkIndicatorState.STATE_VERY_GOOD:
                    _remoteNetworkQuality = NetworkQuality.VERYGOOD;
                    break;
                case NetworkIndicatorState.STATE_GOOD:
                    _remoteNetworkQuality = NetworkQuality.GOOD;
                    break;
                case NetworkIndicatorState.STATE_FAIR:
                    _remoteNetworkQuality = NetworkQuality.FAIR;
                    break;
                case NetworkIndicatorState.STATE_POOR:
                    _remoteNetworkQuality = NetworkQuality.POOR;
                    break;
            }

            currentState = nextState;

            RaisePropertyChanged("RemoteNetworkQuality");
        }

        private NetworkQuality _remoteNetworkQuality = NetworkQuality.OFF;
        /// <summary>
        /// Gets quality of the network between the client and the radio
        /// </summary>
        public NetworkQuality RemoteNetworkQuality
        {
            get { return _remoteNetworkQuality; }
            internal set
            {
                if (_remoteNetworkQuality == value)
                    return;

                _remoteNetworkQuality = value;
                RaisePropertyChanged("RemoteNetworkQuality");
            }
        }

        private int _networkPing = -1;
        /// <summary>
        /// Gets the round-trip time (ping time) between the client and
        /// radio in milliseconds.
        /// </summary>
        public int NetworkPing
        {
            get { return _networkPing; }
            internal set
            {
                if (_networkPing == value)
                    return;

                _networkPing = value;
                RaisePropertyChanged("NetworkPing");
            }
        }

        private bool _remoteTxOn;
        public bool RemoteTxOn
        {
            get { return _remoteTxOn; }
            internal set
            {
                if (_remoteTxOn != value)
                {
                    _remoteTxOn = value;
                    RaisePropertyChanged("RemoteTxOn");
                }
            }
        }

        private int _micLevel;
        /// <summary>
        /// The currently selected mic level from 0 to 100
        /// </summary>
        public int MicLevel
        {
            get { return _micLevel; }
            set
            {
                int new_level = value;

                // check limits
                if (new_level < 0) new_level = 0;
                if (new_level > 100) new_level = 100;

                if (_micLevel != new_level)
                {
                    _micLevel = new_level;
                    SendCommand("transmit set miclevel=" + _micLevel);
                    RaisePropertyChanged("MicLevel");
                }
                else if (new_level != value)
                {
                    RaisePropertyChanged("MicLevel");
                }
            }
        }

        private bool _micBias;
        /// <summary>
        /// Enables (true) or disables (false) the mic bias
        /// </summary>
        public bool MicBias
        {
            get { return _micBias; }
            set
            {
                if (_micBias != value)
                {
                    _micBias = value;
                    SendCommand("mic bias " + Convert.ToByte(_micBias));
                    RaisePropertyChanged("MicBias");
                }
            }
        }

        private bool _micBoost;
        /// <summary>
        /// Enables (true) or disables (false) the +20 dB mic boost
        /// </summary>
        public bool MicBoost
        {
            get { return _micBoost; }
            set
            {
                if (_micBoost != value)
                {
                    _micBoost = value;
                    SendCommand("mic boost " + Convert.ToByte(_micBoost));
                    RaisePropertyChanged("MicBoost");
                }
            }
        }

        private bool _txFilterChangesAllowed;
        /// <summary>
        /// Gets whether transmit filter widths are allowed to be changed in
        /// for the current transmit mode
        /// </summary>
        public bool TXFilterChangesAllowed
        {
            get { return _txFilterChangesAllowed; }
            internal set
            {
                if (_txFilterChangesAllowed != value)
                {
                    _txFilterChangesAllowed = value;
                    RaisePropertyChanged("TXFilterChangesAllowed");
                }
            }
        }

        private bool _txRFPowerChangesAllowed;
        /// <summary>
        /// Gets whether the RF Power is allowed to be changed in
        /// for the current radio state
        /// </summary>
        public bool TXRFPowerChangesAllowed
        {
            get { return _txRFPowerChangesAllowed; }
            internal set
            {
                if (_txRFPowerChangesAllowed != value)
                {
                    _txRFPowerChangesAllowed = value;
                    RaisePropertyChanged("TXRFPowerChangesAllowed");
                }
            }
        }

        private bool _hwalcEnabled;
        /// <summary>
        /// Enables or disables the ALC RCA input on the back panel of the radio
        /// </summary>
        public bool HWAlcEnabled
        {
            get { return _hwalcEnabled; }
            set
            {
                if (_hwalcEnabled != value)
                {
                    _hwalcEnabled = value;
                    SendCommand("transmit set hwalc_enabled=" + Convert.ToByte(_hwalcEnabled));
                    RaisePropertyChanged("HWAlcEnabled");
                }
            }
        }

        private void _SetTXFilter(int low, int high)
        {
            if (low >= high) return;

            if (_txFilterLow != low || _txFilterHigh != high)
            {
                if (high > 10000)   // max 10 kHz
                    high = 10000;

                if (low < 0)        // min 0 Hz
                    low = 0;

                _txFilterLow = low;
                _txFilterHigh = high;
                SendCommand("transmit set filter_low=" + _txFilterLow + " filter_high=" + _txFilterHigh);
            }
        }

        private int _txFilterLow;
        /// <summary>
        /// The low cut frequency of the transmit filter in Hz (0 to TXFilterHigh Hz - 50 Hz)
        /// </summary>
        public int TXFilterLow
        {
            get { return _txFilterLow; }
            set
            {
                int new_cut = value;

                if (new_cut > _txFilterHigh - 50)
                    new_cut = _txFilterHigh - 50;

                if (new_cut < 0) new_cut = 0;

                if (_txFilterLow != new_cut)
                {
                    _SetTXFilter(value, _txFilterHigh);
                    RaisePropertyChanged("TXFilterLow");
                }
                else if (new_cut != value)
                {
                    RaisePropertyChanged("TXFilterLow");
                }
            }
        }

        private int _txFilterHigh;
        /// <summary>
        /// The high cut frequency of the transmit filter in Hz (TXFilterLow + 50 Hz to 10000 Hz)
        /// </summary>
        public int TXFilterHigh
        {
            get { return _txFilterHigh; }
            set
            {
                int new_cut = value;

                if (new_cut < _txFilterLow + 50)
                    new_cut = _txFilterLow + 50;

                if (new_cut > 10000) new_cut = 10000;

                if (_txFilterHigh != new_cut)
                {
                    _SetTXFilter(_txFilterLow, value);
                    RaisePropertyChanged("TXFilterHigh");
                }
                else if (new_cut != value)
                {
                    RaisePropertyChanged("TXFilterHigh");
                }
            }
        }

        private bool _txTune;
        /// <summary>
        /// Keys the transmitter with Tune
        /// </summary>
        public bool TXTune
        {
            get { return _txTune; }
            set
            {
                if (value == _txTune) return;
                _txTune = value;
                SendCommand("transmit tune " + Convert.ToByte(_txTune));
                RaisePropertyChanged(nameof(TXTune));
            }
        }

        private string _tuneMode;
        /// <summary>
        /// Sets the tuneMode
        /// </summary>
        public string TuneMode
        {
            get => _tuneMode;
            set
            {
                if (value == _tuneMode) return;
                _tuneMode = value;

                var command = _tuneMode.Equals("Two Tone", StringComparison.OrdinalIgnoreCase)
                    ? "transmit set tune_mode=two_tone"
                    : "transmit set tune_mode=single_tone";

                SendCommand(command);
                RaisePropertyChanged(nameof(TuneMode));
            }
        }

        private bool _txMonitor;
        /// <summary>
        /// Enables the transmit monitor
        /// </summary>
        public bool TXMonitor
        {
            get { return _txMonitor; }
            set
            {
                if (_txMonitor != value)
                {
                    _txMonitor = value;
                    SendCommand("transmit set mon=" + Convert.ToByte(_txMonitor));
                    RaisePropertyChanged("TXMonitor");
                }
            }
        }

        private int _txCWMonitorGain;
        /// <summary>
        /// The transmit monitor gain from 0 to 100
        /// </summary>
        public int TXCWMonitorGain
        {
            get { return _txCWMonitorGain; }
            set
            {
                int new_gain = value;

                // check limits
                if (new_gain < 0) new_gain = 0;
                if (new_gain > 100) new_gain = 100;

                if (_txCWMonitorGain != new_gain)
                {
                    _txCWMonitorGain = new_gain;
                    SendCommand("transmit set mon_gain_cw=" + _txCWMonitorGain);
                    RaisePropertyChanged("TXCWMonitorGain");
                }
                else if (new_gain != value)
                {
                    RaisePropertyChanged("TXCWMonitorGain");
                }
            }
        }

        private int _txSBMonitorGain;
        /// <summary>
        /// The transmit monitor gain from 0 to 100
        /// </summary>
        public int TXSBMonitorGain
        {
            get { return _txSBMonitorGain; }
            set
            {
                int new_gain = value;

                // check limits
                if (new_gain < 0) new_gain = 0;
                if (new_gain > 100) new_gain = 100;

                if (_txSBMonitorGain != new_gain)
                {
                    _txSBMonitorGain = new_gain;
                    SendCommand("transmit set mon_gain_sb=" + _txSBMonitorGain);
                    RaisePropertyChanged("TXSBMonitorGain");
                }
                else if (new_gain != value)
                {
                    RaisePropertyChanged("TXSBMonitorGain");
                }
            }
        }

        private int _txCWMonitorPan;
        /// <summary>
        /// Gets or sets the left-right pan for the CW monitor (sidetone) from 0 to 100.  
        /// A value of 50 pans evenly between left and right.
        /// </summary>
        public int TXCWMonitorPan
        {
            get { return _txCWMonitorPan; }
            set
            {
                int new_pan = value;

                // check limits
                if (new_pan < 0) new_pan = 0;
                if (new_pan > 100) new_pan = 100;

                if (_txCWMonitorPan != new_pan)
                {
                    _txCWMonitorPan = new_pan;
                    SendCommand("transmit set mon_pan_cw=" + _txCWMonitorPan);
                    RaisePropertyChanged("TXCWMonitorPan");
                }
                else if (new_pan != value)
                {
                    RaisePropertyChanged("TXCWMonitorPan");
                }
            }
        }

        private int _txSBMonitorPan;
        /// <summary>
        /// The transmit monitor gain from 0 to 100
        /// </summary>
        public int TXSBMonitorPan
        {
            get { return _txSBMonitorPan; }
            set
            {
                int new_gain = value;

                // check limits
                if (new_gain < 0) new_gain = 0;
                if (new_gain > 100) new_gain = 100;

                if (_txSBMonitorPan != new_gain)
                {
                    _txSBMonitorPan = new_gain;
                    SendCommand("transmit set mon_pan_sb=" + _txSBMonitorPan);
                    RaisePropertyChanged("TXSBMonitorPan");
                }
                else if (new_gain != value)
                {
                    RaisePropertyChanged("TXSBMonitorPan");
                }
            }
        }

        private bool _mox;
        /// <summary>
        /// Enables mox
        /// </summary>
        public bool Mox
        {
            get { return _mox; }
            set
            {
                if (_mox != value)
                {
                    _mox = value;
                    SendCommand("xmit " + Convert.ToByte(_mox));
                    RaisePropertyChanged("Mox");
                }
            }
        }

        private bool _txMonAvailable;
        /// <summary>
        /// True when MOX is avaialble to be used
        /// </summary>
        public bool TxMonAvailable
        {
            get { return _txMonAvailable; }
        }

        private bool _txInhibit;
        /// <summary>
        /// Enables or disables the transmit inhibit
        /// </summary>
        public bool TXInhibit
        {
            get { return _txInhibit; }
            set
            {
                if (_txInhibit != value)
                {
                    _txInhibit = value;
                    SendCommand("transmit set inhibit=" + Convert.ToByte(_txInhibit));
                    RaisePropertyChanged("TXInhibit");
                }
            }
        }

        private bool _txAallowed;
        public bool TXAllowed
        {
            get { return _txAallowed; }
            internal set
            {
                if (_txAallowed == value)
                    return;

                _txAallowed = value;
                RaisePropertyChanged("TXAllowed");
            }
        }

        private bool _met_in_rx;
        /// <summary>
        /// Enables or disables the level meter during receive
        /// </summary>
        public bool MetInRX
        {
            get { return _met_in_rx; }
            set
            {
                if (_met_in_rx != value)
                {
                    _met_in_rx = value;
                    SendCommand("transmit set met_in_rx=" + Convert.ToByte(_met_in_rx));
                    RaisePropertyChanged("MetInRX");
                }
            }
        }

        private int _cwPitch;
        /// <summary>
        /// The CW pitch from 100 Hz to 6000 Hz
        /// </summary>
        public int CWPitch
        {
            get { return _cwPitch; }
            set
            {
                int new_pitch = value;

                if (new_pitch < 100) new_pitch = 100;
                if (new_pitch > 6000) new_pitch = 6000;

                if (_cwPitch != new_pitch)
                {
                    _cwPitch = new_pitch;
                    SendCommand("cw pitch " + _cwPitch);
                    RaisePropertyChanged("CWPitch");
                }
                else if (new_pitch != value)
                {
                    RaisePropertyChanged("CWPitch");
                }
            }
        }

        public void CWPTT(bool state, string timestamp, uint guiClientHandle = 0)
        {
            if (_netCWStream != null)
            {
                string cwGuiClientHandle;

                // If the GUI Client Handle was not specified, assume that this is the GUIClient, and use it as the Client Handle.
                // Otherwise, use the passed in guiClientHandle.  This will usually be done for non-gui clients that have been
                // bound to a different GUIClient context.
                if (guiClientHandle == 0)
                {
                    cwGuiClientHandle = ClientHandle.ToString("X");
                }
                else
                {
                    cwGuiClientHandle = guiClientHandle.ToString("X");
                }

                string cmd = "cw ptt " + Convert.ToByte(state) + " time=0x" + timestamp + " index=" + _netCWStream.GetNextIndex() + " client_handle=0x" + cwGuiClientHandle;
                _netCWStream.AddTXData(cmd);
                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(5);
                    _netCWStream.AddTXData(cmd);
                });
                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(10);
                    _netCWStream.AddTXData(cmd);
                });

                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(15);
                    _netCWStream.AddTXData(cmd);
                });
                SendCommand(cmd);
            }
        }

        public void CWKey(bool state, string timestamp, uint guiClientHandle = 0)
        {
            if (_netCWStream != null)
            {
                string cwGuiClientHandle;

                // If the GUI Client Handle was not specified, assume that this is the GUIClient, and use it as the Client Handle.
                // Otherwise, use the passed in guiClientHandle.  This will usually be done for non-gui clients that have been
                // bound to a different GUIClient context.
                if (guiClientHandle == 0)
                {
                    cwGuiClientHandle = ClientHandle.ToString("X");
                }
                else
                {
                    cwGuiClientHandle = guiClientHandle.ToString("X");
                }

                string cmd = "cw key " + Convert.ToByte(state) + " time=0x" + timestamp + " index=" + _netCWStream.GetNextIndex() + " client_handle=0x" + cwGuiClientHandle;
                _netCWStream.AddTXData(cmd);
                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(5);
                    _netCWStream.AddTXData(cmd);
                });

                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(10);
                    _netCWStream.AddTXData(cmd);
                });

                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(15);
                    _netCWStream.AddTXData(cmd);
                });
                SendCommand(cmd);
            }
        }

        public void CWKeyImmediate(bool state)
        {
            SendCommand("cw key immediate " + Convert.ToByte(state));
        }

        private bool _apfMode;
        /// <summary>
        /// Enables or disables the auto-peaking filter (APF)
        /// </summary>
        public bool APFMode
        {
            get { return _apfMode; }
            set
            {
                if (_apfMode != value)
                {
                    _apfMode = value;
                    SendCommand("eq apf mode=" + _apfMode);
                    RaisePropertyChanged("APFMode");
                }
            }
        }

        private double _apfQFactor;
        /// <summary>
        /// The Q factor for the auto-peaking filter (APF) from 0 to 33
        /// </summary>
        public double APFQFactor
        {
            get { return _apfQFactor; }
            set
            {
                if (_apfQFactor != value)
                {
                    _apfQFactor = value;
                    SendCommand("eq apf qfactor=" + StringHelper.DoubleToString(_apfQFactor, "f6"));
                    RaisePropertyChanged("APFQFactor");
                }
            }
        }

        private double _apfGain;
        /// <summary>
        /// The gain of the auto-peaking filter (APF) from 0 to 100, mapped
        /// linearly from 0 dB to 14 dB
        /// </summary>
        public double APFGain
        {
            get { return _apfGain; }
            set
            {
                // TODO: Need a bounds check here.  Are the bounds really 0-100?
                // Should this property be a double or an int?
                if (_apfGain != value)
                {
                    _apfGain = value;
                    SendCommand("eq apf gain=" + StringHelper.DoubleToString(_apfGain, "f6"));
                    RaisePropertyChanged("APFGain");
                }
            }
        }

        private int _cwSpeed;
        /// <summary>
        /// The CW speed in words per minute (wpm) from 5 to 100
        /// </summary>
        public int CWSpeed
        {
            get { return _cwSpeed; }
            set
            {
                int new_speed = value;

                if (new_speed < 5) new_speed = 5;
                if (new_speed > 100) new_speed = 100;

                if (_cwSpeed != new_speed)
                {
                    _cwSpeed = new_speed;
                    SendCommand("cw wpm " + _cwSpeed);
                    RaisePropertyChanged("CWSpeed");
                }
                else if (new_speed != value)
                {
                    RaisePropertyChanged("CWSpeed");
                }
            }
        }

        private int _cwDelay;
        /// <summary>
        /// The CW breakin delay in milliseconds (ms) from 0 ms to 2000 ms
        /// </summary>
        public int CWDelay
        {
            get { return _cwDelay; }
            set
            {
                int new_delay = value;

                if (new_delay < 0) new_delay = 0;
                if (new_delay > 2000) new_delay = 2000;

                if (_cwDelay != new_delay)
                {
                    _cwDelay = new_delay;
                    SendCommand("cw break_in_delay " + _cwDelay);
                    RaisePropertyChanged("CWDelay");
                }
                else if (new_delay != value)
                {
                    RaisePropertyChanged("CWDelay");
                }
            }
        }


        private bool _cwBreakIn;
        /// <summary>
        /// Enables or disables CW breakin mode, which turns on the
        /// transmitter by a key or paddle closure rather than using PTT
        /// </summary>
        public bool CWBreakIn
        {
            get { return _cwBreakIn; }
            set
            {
                if (_cwBreakIn != value)
                {
                    _cwBreakIn = value;
                    SendCommand("cw break_in " + Convert.ToByte(_cwBreakIn));
                    RaisePropertyChanged("CWBreakIn");
                }
            }
        }

        private bool _cwSidetone;
        /// <summary>
        /// Enables or disables the CW Sidetone
        /// </summary>
        public bool CWSidetone
        {
            get { return _cwSidetone; }
            set
            {
                if (_cwSidetone != value)
                {
                    _cwSidetone = value;
                    SendCommand("cw sidetone " + Convert.ToByte(_cwSidetone));
                    RaisePropertyChanged("CWSidetone");
                }
            }
        }

        private bool _cwIambic;
        /// <summary>
        /// Enables or disables the Iambic keyer for CW
        /// </summary>
        public bool CWIambic
        {
            get { return _cwIambic; }
            set
            {
                if (_cwIambic != value)
                {
                    _cwIambic = value;
                    SendCommand("cw iambic " + Convert.ToByte(_cwIambic));
                    RaisePropertyChanged("CWIambic");
                }
            }
        }

        private bool _cwIambicModeA;
        /// <summary>
        /// Enables or disables CW Iambic Mode A
        /// </summary>
        public bool CWIambicModeA
        {
            get { return _cwIambicModeA; }
            set
            {
                if (_cwIambicModeA != value)
                {
                    _cwIambicModeA = value;
                    if (_cwIambicModeA)
                    {
                        SendCommand("cw mode 0");
                    }

                    RaisePropertyChanged("CWIambicModeA");
                }
            }
        }

        private bool _cwIambicModeB;
        /// <summary>
        /// Enables or disables CW Iambic Mode B
        /// </summary>
        public bool CWIambicModeB
        {
            get { return _cwIambicModeB; }
            set
            {
                if (_cwIambicModeB != value)
                {
                    _cwIambicModeB = value;
                    if (_cwIambicModeB)
                    {
                        SendCommand("cw mode 1");
                    }

                    RaisePropertyChanged("CWIambicModeB");
                }
            }
        }


        private bool _cwl_enabled;
        /// <summary>
        /// Enables or disables CWL. CWU (default) active when disabled.
        /// </summary>
        public bool CWL_Enabled
        {
            get { return _cwl_enabled; }
            set
            {
                if (_cwl_enabled != value)
                {
                    _cwl_enabled = value;
                    SendCommand("cw cwl_enabled " + Convert.ToByte(_cwl_enabled));

                    RaisePropertyChanged("CWL_Enabled");
                }
            }
        }

        private bool _cwSwapPaddles;
        /// <summary>
        /// Swaps the CW dot-dash paddles when true
        /// </summary>
        public bool CWSwapPaddles
        {
            get { return _cwSwapPaddles; }
            set
            {
                if (_cwSwapPaddles != value)
                {
                    _cwSwapPaddles = value;
                    SendCommand("cw swap " + Convert.ToByte(_cwSwapPaddles));
                    RaisePropertyChanged("CWSwapPaddles");
                }
            }
        }

        private bool _companderOn;
        /// <summary>
        /// Enables or disables the Compander
        /// </summary>
        public bool CompanderOn
        {
            get { return _companderOn; }
            set
            {
                if (_companderOn != value)
                {
                    _companderOn = value;
                    SendCommand("transmit set compander=" + Convert.ToByte(_companderOn));
                    RaisePropertyChanged("CompanderOn");
                }
            }
        }

        private int _companderLevel;
        /// <summary>
        /// The compander level from 0 to 100
        /// </summary>
        public int CompanderLevel
        {
            get { return _companderLevel; }
            set
            {
                int new_val = value;

                if (new_val < 0) new_val = 0;
                if (new_val > 100) new_val = 100;


                if (new_val != _companderLevel)
                {
                    _companderLevel = new_val;
                    SendCommand("transmit set compander_level=" + _companderLevel);
                    RaisePropertyChanged("CompanderLevel");
                }
                else if (new_val != value)
                {
                    RaisePropertyChanged("CompanderLevel");
                }
            }
        }

        private bool _accOn;
        /// <summary>
        /// Enables or disables mixing of an input via the accessory port on the back panel 
        /// of the radio with the currently selected Mic input
        /// </summary>
        public bool ACCOn
        {
            get { return _accOn; }
            set
            {
                if (_accOn != value)
                {
                    _accOn = value;
                    SendCommand("mic acc " + Convert.ToByte(_accOn));
                    RaisePropertyChanged("ACCOn");
                }
            }
        }


        private bool _daxOn;
        /// <summary>
        /// Enables or disables Digital Audio eXchange (DAX)
        /// </summary>
        public bool DAXOn
        {
            get { return _daxOn; }
            set
            {
                if (_daxOn != value)
                {
                    _daxOn = value;
                    SendCommand("transmit set dax=" + Convert.ToByte(_daxOn));
                    RaisePropertyChanged("DAXOn");
                }
            }
        }

        private bool _simpleVOXEnable;
        /// <summary>
        /// Enables or disables VOX
        /// </summary>
        public bool SimpleVOXEnable
        {
            get { return _simpleVOXEnable; }
            set
            {
                if (_simpleVOXEnable != value)
                {
                    _simpleVOXEnable = value;
                    SendCommand("transmit set vox_enable=" + Convert.ToByte(_simpleVOXEnable));
                    RaisePropertyChanged("SimpleVOXEnable");
                }
            }
        }

        private int _simpleVOXLevel;
        /// <summary>
        /// The vox level from 0 to 100
        /// </summary>
        public int SimpleVOXLevel
        {
            get { return _simpleVOXLevel; }
            set
            {
                int new_val = value;

                // check limits
                if (new_val < 0) new_val = 0;
                if (new_val > 100) new_val = 100;

                if (_simpleVOXLevel != new_val)
                {
                    _simpleVOXLevel = new_val;
                    SendCommand("transmit set vox_level=" + _simpleVOXLevel);
                    RaisePropertyChanged("SimpleVOXLevel");
                }
                else if (new_val != value)
                    RaisePropertyChanged("SimpleVOXLevel");
            }
        }

        private int _simpleVOXDelay;
        /// <summary>
        /// Sets the VOX delay from 0 to 100.  The delay will
        /// be (value * 20) milliseconds.  Setting this value to 
        /// 50 will result in a delay of 1000 ms.
        /// </summary>
        public int SimpleVOXDelay
        {
            get { return _simpleVOXDelay; }
            set
            {
                int new_val = value;

                // check limits
                if (new_val < 0) new_val = 0;
                if (new_val > 100) new_val = 100;

                if (_simpleVOXDelay != new_val)
                {
                    _simpleVOXDelay = new_val;
                    // _simpleVOXDelay is multiplied by 20 to set the hang time in milliseconds
                    SendCommand("transmit set vox_delay=" + _simpleVOXDelay);
                    RaisePropertyChanged("SimpleVOXDelay");
                }
                else if (new_val != value)
                    RaisePropertyChanged("SimpleVOXDelay");
            }
        }


        private bool _speechProcessorEnable;
        public bool SpeechProcessorEnable
        {
            get { return _speechProcessorEnable; }
            set
            {
                if (_speechProcessorEnable != value)
                {
                    _speechProcessorEnable = value;
                    SendCommand("transmit set speech_processor_enable=" + Convert.ToByte(_speechProcessorEnable));
                    RaisePropertyChanged("SpeechProcessorEnable");
                }
            }
        }

        private uint _speechProcessorLevel;
        public uint SpeechProcessorLevel
        {
            get { return _speechProcessorLevel; }
            set
            {
                if (_speechProcessorLevel != value)
                {
                    _speechProcessorLevel = value;
                    SendCommand("transmit set speech_processor_level=" + Convert.ToByte(_speechProcessorLevel));
                    RaisePropertyChanged("SpeechProcessorLevel");
                }
            }
        }

        private bool _fullDuplexEnabled;
        public bool FullDuplexEnabled
        {
            get { return _fullDuplexEnabled; }
            set
            {
                if (_fullDuplexEnabled != value)
                {
                    _fullDuplexEnabled = value;
                    SendCommand("radio set full_duplex_enabled=" + Convert.ToByte(_fullDuplexEnabled));
                    RaisePropertyChanged("FullDuplexEnabled");
                }
            }
        }

        private bool _unsavedProfileChangesTX;
        public bool UnsavedProfileChangesTX
        {
            get => _unsavedProfileChangesTX;
            set
            {
                if (_unsavedProfileChangesTX == value)
                    return;

                _unsavedProfileChangesTX = value;
                RaisePropertyChanged("UnsavedProfileChangesTX");
            }
        }

        private bool _unsavedProfileChangesMIC;
        public bool UnsavedProfileChangesMIC
        {
            get => _unsavedProfileChangesMIC;
            set
            {
                if (_unsavedProfileChangesMIC == value)
                    return;

                _unsavedProfileChangesMIC = value;
                RaisePropertyChanged("UnsavedProfileChangesMIC");
            }
        }

        private void ParseTransmitStatus(string s)
        {
            string[] words = s.Split(' ');

            if (words.Length == 0)
            {
                return;
            }

            if (words[0] == "band")
            {
                ParseTxBandSettingsStatus(s);
                return;
            }

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    if (!string.IsNullOrEmpty(kv)) Debug.WriteLine($"Radio::ParseTransmitStatus: Invalid key/value pair ({kv})");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "max_power_level":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - max_power_level: Invalid value (" + kv + ")");
                            }

                            if (temp < 0) temp = 0;
                            if (temp > 100) temp = 100;
                            _maxPowerLevel = temp;
                            RaisePropertyChanged("MaxPowerLevel");
                            break;
                        }
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

                            _rfPower = temp;
                            RaisePropertyChanged("RFPower");
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

                            _tunePower = temp;
                            RaisePropertyChanged("TunePower");
                            break;
                        }

                    case "lo":
                        {
                            int temp; // in Hz
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus: Invalid value (" + kv + ")");
                                continue;
                            }

                            _txFilterLow = temp;
                            RaisePropertyChanged("TXFilterLow");
                            break;
                        }

                    case "hi":
                        {
                            int temp; // in Hz
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus: Invalid value (" + kv + ")");
                                continue;
                            }

                            _txFilterHigh = temp;
                            RaisePropertyChanged("TXFilterHigh");
                            break;
                        }

                    case "tx_filter_changes_allowed":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - tx_filter_changes_allowed: Invalid value (" + kv + ")");
                                continue;
                            }

                            _txFilterChangesAllowed = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TXFilterChangesAllowed");
                            break;
                        }

                    case "tx_rf_power_changes_allowed":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - tx_rf_power_changes_allowed: Invalid value (" + kv + ")");
                                continue;
                            }

                            _txRFPowerChangesAllowed = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TXRFPowerChangesAllowed");
                            break;
                        }

                    case "am_carrier_level":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - am_carrier_level: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (temp < 0) temp = 0;
                            if (temp > 100) temp = 100;

                            _amCarrierLevel = temp;
                            RaisePropertyChanged("AMCarrierLevel");
                            break;
                        }

                    case "mic_level":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - mic_level: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (temp < 0) temp = 0;
                            if (temp > 100) temp = 100;

                            _micLevel = temp;
                            RaisePropertyChanged("MicLevel");
                            break;
                        }

                    case "mic_selection":
                        {
                            _micInput = value.ToUpper();
                            if (_micInputList.Count > 0)
                            {
                                if (_micInput == "PC" || !_micInputList.Contains(_micInput))
                                    RemoteTxOn = true;
                                else
                                    RemoteTxOn = false;
                            }

                            RaisePropertyChanged("MicInput");
                            break;
                        }

                    case "mic_boost":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - mic_boost: Invalid value (" + kv + ")");
                                continue;
                            }

                            _micBoost = Convert.ToBoolean(temp);
                            RaisePropertyChanged("MicBoost");
                            break;
                        }

                    case "mon_available":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - mon_available: Invalid value (" + kv + ")");
                                continue;
                            }

                            _txMonAvailable = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TxMonAvailable");
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

                            _hwalcEnabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("HWAlcEnabled");
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

                            _txInhibit = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TXInhibit");
                            break;
                        }

                    case "mic_bias":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - mic_bias: Invalid value (" + kv + ")");
                                continue;
                            }

                            _micBias = Convert.ToBoolean(temp);
                            RaisePropertyChanged("MicBias");
                            break;
                        }

                    case "mic_acc":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - mic_acc: Invalid value (" + kv + ")");
                                continue;
                            }

                            _accOn = Convert.ToBoolean(temp);
                            RaisePropertyChanged("ACCOn");
                            break;
                        }

                    case "dax":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - dax: Invalid value (" + kv + ")");
                                continue;
                            }

                            _daxOn = Convert.ToBoolean(temp);
                            RaisePropertyChanged("DAXOn");
                            break;
                        }

                    case "compander":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - compander: Invalid value (" + kv + ")");
                                continue;
                            }

                            _companderOn = Convert.ToBoolean(temp);
                            RaisePropertyChanged("CompanderOn");
                            break;
                        }

                    case "compander_level":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - compander_level: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (temp < 0) temp = 0;
                            if (temp > 100) temp = 100;

                            _companderLevel = temp;
                            RaisePropertyChanged("CompanderLevel");
                            break;
                        }

                    /*case "noise_gate_level":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - noise_gate_level: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (temp < 0) temp = 0;
                            if (temp > 100) temp = 100;

                            _noiseGateLevel = temp;
                            RaisePropertyChanged("NoiseGateLevel");
                            break;
                        }*/

                    case "pitch":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - pitch: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (temp < 100) temp = 100;
                            if (temp > 6000) temp = 6000;

                            _cwPitch = temp;
                            RaisePropertyChanged("CWPitch");
                            break;
                        }

                    case "speed":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - pitch: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (temp < 1) temp = 1;
                            if (temp > 100) temp = 100;

                            _cwSpeed = temp;
                            RaisePropertyChanged("CWSpeed");
                            break;
                        }

                    case "synccwx":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b || temp < 0 || temp > 1)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - synccwx: Invalid value (" + kv + ")");
                                continue;
                            }

                            _syncCWX = Convert.ToBoolean(temp);
                            RaisePropertyChanged("SyncCWX");
                            break;
                        }

                    case "iambic":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - iambic: Invalid value (" + kv + ")");
                                continue;
                            }

                            _cwIambic = Convert.ToBoolean(temp);
                            RaisePropertyChanged("CWIambic");
                            break;
                        }

                    case "iambic_mode":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - iambic_mode: Invalid value (" + kv + ")");
                                continue;
                            }

                            // Currently only Iambic Mode A and B are available in the client
                            // 0: Iambic Mode A
                            // 1: Iambic Mode B
                            // 2: Iambic Mode B Strict
                            // 3: Iambic Mode Bug

                            switch (temp)
                            {
                                case 0:
                                    _cwIambicModeA = true;
                                    RaisePropertyChanged("CWIambicModeA");
                                    break;
                                case 1:
                                    _cwIambicModeB = true;
                                    RaisePropertyChanged("CWIambicModeB");
                                    break;
                                default:
                                    _cwIambicModeA = true;
                                    RaisePropertyChanged("CWIambicModeA");
                                    break;
                            }

                            break;
                        }

                    case "swap_paddles":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - swap_paddles: Invalid value (" + kv + ")");
                                continue;
                            }

                            _cwSwapPaddles = Convert.ToBoolean(temp);
                            RaisePropertyChanged("CWSwapPaddles");
                            break;
                        }

                    case "break_in":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - break_in: Invalid value (" + kv + ")");
                                continue;
                            }

                            _cwBreakIn = Convert.ToBoolean(temp);
                            RaisePropertyChanged("CWBreakIn");
                            break;
                        }

                    case "sidetone":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - sidetone: Invalid value (" + kv + ")");
                                continue;
                            }

                            _cwSidetone = Convert.ToBoolean(temp);
                            RaisePropertyChanged("CWSidetone");
                            break;
                        }
                    case "cwl_enabled":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - cwl_enabled: Invalid value (" + kv + ")");
                                continue;
                            }

                            _cwl_enabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("CWL_Enabled");
                            break;
                        }

                    case "break_in_delay":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - break_in_delay: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (temp < 0) temp = 0;
                            if (temp > 2000) temp = 2000;

                            _cwDelay = temp;

                            RaisePropertyChanged("CWDelay");
                            break;
                        }

                    case "sb_monitor":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - sb_monitor: Invalid value (" + kv + ")");
                                continue;
                            }
                            _txMonitor = Convert.ToBoolean(temp);
                            RaisePropertyChanged("TXMonitor");
                            break;
                        }
                    case "mon_gain_cw":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - mon_gain_cw: invalid value (" + kv + ")");
                                continue;
                            }

                            _txCWMonitorGain = temp;
                            RaisePropertyChanged("TXCWMonitorGain");
                            break;
                        }
                    case "mon_gain_sb":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - mon_gain_sb: invalid value (" + kv + ")");
                                continue;
                            }

                            _txSBMonitorGain = temp;
                            RaisePropertyChanged("TXSBMonitorGain");
                            break;
                        }
                    case "mon_pan_cw":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - mon_pan_cw: invalid value (" + kv + ")");
                                continue;
                            }

                            _txCWMonitorPan = temp;
                            RaisePropertyChanged("TXCWMonitorPan");
                            break;
                        }
                    case "mon_pan_sb":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - mon_pan_sb: invalid value (" + kv + ")");
                                continue;
                            }

                            _txSBMonitorPan = temp;
                            RaisePropertyChanged("TXSBMonitorPan");
                            break;
                        }
                    case "speech_processor_enable":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - Speech Processor Enable: Invalid value (" + kv + ")");
                                continue;
                            }

                            _speechProcessorEnable = Convert.ToBoolean(temp);
                            RaisePropertyChanged("SpeechProcessorEnable");
                            break;
                        }
                    case "speech_processor_level":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - Speech Processor Level: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (temp < 0) temp = 0;
                            if (temp > 100) temp = 100;

                            _speechProcessorLevel = temp;
                            RaisePropertyChanged("SpeechProcessorLevel");
                            break;
                        }

                    case "vox_enable":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - vox_enable: Invalid value (" + kv + ")");
                                continue;
                            }

                            _simpleVOXEnable = Convert.ToBoolean(temp);
                            RaisePropertyChanged("SimpleVOXEnable");
                            break;
                        }

                    case "vox_level":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - vox_level: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (temp < 0) temp = 0;
                            if (temp > 100) temp = 100;

                            _simpleVOXLevel = temp;
                            RaisePropertyChanged("SimpleVOXLevel");
                            break;
                        }
                    case "vox_delay":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - vox_delay: Invalid value (" + kv + ")");
                                continue;
                            }
                            if (temp < 0) temp = 0;
                            if (temp > 100) temp = 100;

                            _simpleVOXDelay = temp;
                            RaisePropertyChanged("SimpleVOXDelay");
                            break;
                        }
                    case "tune":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - tune: Invalid value (" + kv + ")");
                                continue;
                            }

                            bool new_val = Convert.ToBoolean(temp);
                            if (new_val != _txTune && ShouldUpdateMoxOrTuneState(_txClientHandle))
                            {
                                _txTune = new_val;
                                RaisePropertyChanged("TXTune");
                            }
                            break;
                        }
                    case "tune_mode":
                        {
                            string parsedValue = value?.Trim().ToLowerInvariant();

                            switch (parsedValue)
                            {
                                case "two_tone":
                                    _tuneMode = "Two Tone";
                                    break;
                                case "single_tone":
                                    _tuneMode = "Single Tone";
                                    break;
                                default:
                                    Debug.WriteLine($"Radio::ParseRadioStatus - tune_mode: Unknown value ({value})");
                                    _tuneMode = "Single Tone"; // default fallback
                                    break;
                            }
                        
                            RaisePropertyChanged("TuneMode");
                            break;
                        }
                    case "met_in_rx":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - met_in_rx: Invalid value (" + kv + ")");
                                continue;
                            }

                            _met_in_rx = Convert.ToBoolean(temp);
                            RaisePropertyChanged("MetInRX");
                            break;
                        }
                    case "show_tx_in_waterfall":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - show_tx_in_waterfall: Invalid value (" + kv + ")");
                                continue;
                            }

                            _showTxInWaterfall = Convert.ToBoolean(temp);
                            RaisePropertyChanged("ShowTxInWaterfall");
                            break;
                        }
                    case "raw_iq_enable":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseTransmitStatus - raw_iq_enable: Invalid value (" + kv + ")");
                                continue;
                            }

                            bool old_val = _txRawIQEnabled;
                            bool new_val = Convert.ToBoolean(temp);

                            if (new_val != old_val)
                            {
                                _txRawIQEnabled = new_val;
                                RaisePropertyChanged("TXRawIQEnabled");
                            }
                            break;
                        }
                }
            }
        }



        #endregion

        #region receive Routines

        private bool _startOffsetEnabled = true;
        /// <summary>
        /// Allows or prevents the ability to start an automatic 
        /// frequency offset calibration routine.  This is used 
        /// to prevent the user from starting a routine while
        /// one is already in progress
        /// </summary>
        public bool StartOffsetEnabled
        {
            get { return _startOffsetEnabled; }
            set
            {
                if (_startOffsetEnabled != value)
                {
                    _startOffsetEnabled = value;

                    //when false start pll (disabled)
                    //when true pll is over (enabled, default state)
                    if (!_startOffsetEnabled)
                    {
                        SendCommand("radio pll_start");
                    }
                    RaisePropertyChanged("StartOffsetEnabled");
                }
            }
        }

        private int _freqErrorPPB;
        /// <summary>
        /// The frequency error correction value for the internal clock of
        /// radio in parts per billion
        /// </summary>
        public int FreqErrorPPB
        {
            get { return _freqErrorPPB; }
            set
            {
                if (_freqErrorPPB != value)
                {
                    _freqErrorPPB = value;
                    SendCommand("radio set freq_error_ppb=" + _freqErrorPPB);
                    RaisePropertyChanged("FreqErrorPPB");
                }
            }
        }

        private double _calFreq;
        /// <summary>
        /// The frequency, in MHz, that the automatic frequency error correction
        /// routine will use to listen for a reference tone
        /// </summary>
        public double CalFreq
        {
            get { return _calFreq; }
            set
            {
                if (_calFreq != value)
                {
                    _calFreq = value;
                    SendCommand("radio set cal_freq=" + StringHelper.DoubleToString(_calFreq, "f6"));
                    RaisePropertyChanged("CalFreq");
                }
            }
        }

        /// <summary>
        /// Returns true if Diversity is allowed on the radio model.
        /// </summary>
        public bool DiversityIsAllowed
        {
            get
            {
                bool ret_val = false;
                switch (_model)
                {
                    case "FLEX-6600":
                    case "FLEX-6600M":
                    case "FLEX-6700":
                    case "FLEX-6700R":
                    case "FLEX-8600":
                    case "FLEX-8600M":
                    case "AU-520":
                    case "AU-520M":
                        ret_val = true;
                        break;
                }

                return ret_val;
            }
        }

        #endregion

        #region ATU Routines

        private bool _atuPresent;
        /// <summary>
        /// Returns true if an automatic antenna tuning unit (ATU) is present
        /// </summary>
        public bool ATUPresent
        {
            get { return _atuPresent; }
        }

        private bool _atuEnabled;
        /// <summary>
        /// Returns true if the ATU is allowed to be used.
        /// </summary>
        public bool ATUEnabled
        {
            get { return _atuEnabled; }
            internal set
            {
                _atuEnabled = value;
                RaisePropertyChanged("ATUEnabled");
            }
        }

        private bool _atuMemoriesEnabled;
        /// <summary>
        /// Gets or sets whether ATU Memories are enabled
        /// </summary>
        public bool ATUMemoriesEnabled
        {
            get { return _atuMemoriesEnabled; }
            set
            {
                bool newAtuMemoriesEnabled = radioShouldUseExtTuner ? false : value;

                if (_atuMemoriesEnabled == newAtuMemoriesEnabled)
                {
                    if (value != newAtuMemoriesEnabled)
                    {
                        // Overriding value, notify the viewmodel so that it doesn't think everything worked
                        RaisePropertyChanged("ATUMemoriesEnabled");
                    }

                    return;
                }

                _atuMemoriesEnabled = newAtuMemoriesEnabled;
                SendCommand("atu set memories_enabled=" + Convert.ToByte(_atuMemoriesEnabled));
                RaisePropertyChanged("ATUMemoriesEnabled");
            }
        }

        private bool _atuUsingMemory;
        /// <summary>
        /// Gets whether an ATU Memory is currently being used
        /// </summary>
        public bool ATUUsingMemory
        {
            get { return _atuUsingMemory; }
        }

        /// <summary>
        /// Starts an automatic tune on the automatic antenna tuning unit (ATU)
        /// </summary>
        public void ATUTuneStart()
        {
            SendCommand("atu start");
        }

        /// <summary>
        /// Sets the automatic antenna tuning unit (ATU) to be in bypass mode
        /// </summary>
        public void ATUTuneBypass()
        {
            SendCommand("atu bypass");
        }

        /// <summary>
        /// Clears all ATU memories
        /// </summary>
        public void ATUClearMemories()
        {
            SendCommand("atu clear");
        }

        private ATUTuneStatus _atuTuneStatus = ATUTuneStatus.None;
        /// <summary>
        /// Gets the current status of the automatic antenna tuning unit (ATU)
        /// </summary>
        public ATUTuneStatus ATUTuneStatus
        {
            get { return _atuTuneStatus; }
            internal set
            {
                if (_atuTuneStatus != value)
                {
                    _atuTuneStatus = value;
                    RaisePropertyChanged("ATUTuneStatus");
                }
            }
        }

        private void ParseATUStatus(string s)
        {
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Radio::ParseATUStatus: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "status":
                        {
                            ATUTuneStatus = ParseATUTuneStatus(value);
                            break;
                        }

                    case "atu_enabled":
                        {
                            byte is_enabled = 0;
                            bool b = byte.TryParse(value, out is_enabled);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseATUStatus: Invalid value for atu_enabled(" + kv + ")");
                            }

                            ATUEnabled = Convert.ToBoolean(is_enabled);
                            break;
                        }

                    case "memories_enabled":
                        {
                            byte memeories_enabled = 0;
                            bool b = byte.TryParse(value, out memeories_enabled);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseATUStatus: Invalid value for memeories_enabled(" + kv + ")");
                            }

                            _atuMemoriesEnabled = Convert.ToBoolean(memeories_enabled);
                            RaisePropertyChanged("ATUMemoriesEnabled");
                            break;
                        }

                    case "using_mem":
                        {
                            byte using_memory = 0;
                            bool b = byte.TryParse(value, out using_memory);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseATUStatus: Invalid value for using_mem(" + kv + ")");
                            }

                            _atuUsingMemory = Convert.ToBoolean(using_memory);
                            RaisePropertyChanged("ATUUsingMemory");
                            break;
                        }
                }
            }
        }

        private ObservableCollection<string> _waveformsInstalledList;
        public ObservableCollection<string> WaveformsInstalledList
        {
            get
            {
                if (_waveformsInstalledList == null)
                    return new ObservableCollection<string>();
                return new ObservableCollection<string>(_waveformsInstalledList);
            }
        }

        private void UpdateWaveformsInstalledList(string s)
        {
            string[] inputs = s.Split(',');

            _waveformsInstalledList = new ObservableCollection<string>();
            foreach (string wave in inputs)
            {
                if (wave != "")
                {
                    _waveformsInstalledList.Add(wave.Replace('\u007f', ' '));
                }
            }

            RaisePropertyChanged("WaveformsInstalledList");
        }

        private void ParseWaveformStatus(string s)
        {
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Radio::ParseWaveformStatus: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "installed_list":
                        {
                            UpdateWaveformsInstalledList(value);
                            break;
                        }
                }
            }
        }

        private void ParseWanStatus(string s)
        {
            string[] words = s.Split(' ');

            Debug.WriteLine($"Radio::ParseWanStatus - new status {s}");
            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Console.WriteLine("Radio::ParseWanStatus: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];
                switch (key.ToLower())
                {
                    case "server_connected":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("Radio::ParseWanStatus: Invalid value (" + kv + ")");
                                continue;
                            }

                            if (_wanServerConnected == Convert.ToBoolean(temp))
                                continue;

                            _wanServerConnected = Convert.ToBoolean(temp);
                            RaisePropertyChanged("WanServerConnected");
                            break;
                        }
                    case "owner_handshake_state":
                        {
                            _wanOwnerHandshakeStatus = stringToWanRadioRegistrationState(value);

                            RaisePropertyChanged("WanOwnerHandshakeStatus");

                            break;
                        }
                }
            }
        }

        private void ParseProfilesStatus(string s)
        {
            char[] separators = new char[] { ' ' };
            Int32 count = 2;
            string[] words = s.Split(separators, count);
            string profile_type = words[0]; // global | tx | mic | displays
            //uint i;
            /* We only allow one single status key=token pair in profiles since 
             * profile names can have spaces 
             */

            if (words.Length < 2)
            {
                Debug.WriteLine("Radio::ParseProfilesStatus: Invalid profile status string (" + s + ")");
                return;
            }

            string kv = words[1];
            string[] tokens = kv.Split('=');
            if (tokens.Length != 2)
            {
                Debug.WriteLine("Radio::ParseProfilesStatus: Invalid key/value pair (" + kv + ")");
                return;
            }

            string key = tokens[0];
            string value = tokens[1];

            switch (key.ToLower())
            {
                case "list":
                    {
                        UpdateProfileList(profile_type, value);
                        break;
                    }
                case "current":
                    {
                        UpdateProfileListSelection(profile_type, value);
                        break;
                    }
                case "importing":
                    {
                        byte is_importing = 0;

                        bool b = byte.TryParse(value, out is_importing);
                        if (!b)
                        {
                            Debug.WriteLine("Radio::ParseProfilesStatus: Invalid value for importing(" + kv + ")");
                        }

                        DatabaseImportComplete = !Convert.ToBoolean(is_importing);
                        break;
                    }
                case "exporting":
                    {
                        byte is_exporting = 0;
                        bool b = byte.TryParse(value, out is_exporting);
                        if (!b)
                        {
                            Debug.WriteLine("Radio::ParseProfilesStatus: Invalid value for exporting(" + kv + ")");
                        }

                        DatabaseExportComplete = !Convert.ToBoolean(is_exporting);
                        break;
                    }
                case "unsaved_changes_tx":
                {
                    if(false == byte.TryParse(value, out var temp))
                    {
                        Debug.WriteLine($"Radio::ParseProfilesStatus - auto_save_unsaved_changes_tx: Invalid value (({kv})");
                    }

                    bool old_val = _unsavedProfileChangesTX;
                    bool new_val = Convert.ToBoolean(temp);

                    if (new_val == old_val)
                        return;

                    _unsavedProfileChangesTX = new_val;
                    RaisePropertyChanged("UnsavedProfileChangesTX");
                    break;
                }
                case "unsaved_changes_mic":
                {
                    if(false == byte.TryParse(value, out var temp))
                    {
                        Debug.WriteLine($"Radio::ParseProfilesStatus - auto_save_unsaved_changes_mic: Invalid value (({kv})");
                    }

                    bool old_val = _unsavedProfileChangesMIC;
                    bool new_val = Convert.ToBoolean(temp);

                    if (new_val == old_val)
                        return;

                    _unsavedProfileChangesMIC = new_val;
                    RaisePropertyChanged("UnsavedProfileChangesMIC");
                    break;
                }
            }

        }

        private void UpdateProfileListSelection(string profile_type, string profile_name)
        {
            switch (profile_type)
            {
                case "global":
                    if (_profileGlobalList != null && _profileGlobalList.Contains(profile_name))
                    {
                        if (_profileGlobalSelection != profile_name)
                        {
                            _profileGlobalSelection = profile_name;
                            RaisePropertyChanged("ProfileGlobalSelection");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Profile List Problem!");
                    }
                    break;
                case "tx":
                    if (_profileTXList != null && _profileTXList.Contains(profile_name))
                    {
                        if (_profileTXSelection != profile_name)
                        {
                            _profileTXSelection = profile_name;
                            RaisePropertyChanged("ProfileTXSelection");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Profile List Problem!");
                    }
                    break;
                case "mic":
                    if (_profileMICList != null && _profileMICList.Contains(profile_name))
                    {
                        if (_profileMICSelection != profile_name)
                        {
                            _profileMICSelection = profile_name;
                            RaisePropertyChanged("ProfileMICSelection");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Profile List Problem!");
                    }
                    break;
                case "displays":
                    if (_profileDisplayList != null && _profileDisplayList.Contains(profile_name))
                    {
                        if (_profileDisplaySelection != profile_name)
                        {
                            _profileDisplaySelection = profile_name;
                            RaisePropertyChanged("ProfileDisplaySelection");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Profile List Problem!");
                    }
                    break;
            }
        }

        private void UpdateProfileList(string profile_type, string list)
        {
            switch (profile_type)
            {
                case "global":
                    UpdateProfileGlobalList(list);
                    break;
                case "tx":
                    UpdateProfileTxList(list);
                    break;
                case "mic":
                    UpdateProfileMicList(list);
                    break;
                case "displays":
                    UpdateProfileDisplayList(list);
                    break;
            }
        }

        #endregion

        #region GPS Routines

        private bool _gpsInstalled;
        /// <summary>
        /// True if a GPS unit is installed in the radio
        /// </summary>
        public bool GPSInstalled
        {
            get { return _gpsInstalled; }
            internal set
            {
                if (_gpsInstalled != value)
                {
                    _gpsInstalled = value;
                    RaisePropertyChanged("GPSInstalled");
                }
            }
        }

        /// <summary>
        /// Installs the GPS unit on the radio.  Check if a GPS is
        /// installed using the property GPSInstalled
        /// </summary>
        public void GPSInstall()
        {
            SendCommand("radio gps install");
        }

        /// <summary>
        /// Uninstalls the GPS unit on the radio.  Check if a GPS is 
        /// installed using the property GPSInstalled
        /// </summary>
        public void GPSUninstall()
        {
            SendCommand("radio gps uninstall");
        }

        private string _gpsLatitude;
        /// <summary>
        /// Gets the GPS Latitude as a string
        /// </summary>
        public string GPSLatitude
        {
            get { return _gpsLatitude; }
            internal set
            {
                if (_gpsLatitude != value)
                {
                    _gpsLatitude = value;
                    RaisePropertyChanged("GPSLatitude");
                }
            }
        }

        private string _gpsLongitude;
        /// <summary>
        /// Gets the GPS Longitude as a string
        /// </summary>
        public string GPSLongitude
        {
            get { return _gpsLongitude; }
            internal set
            {
                if (_gpsLongitude != value)
                {
                    _gpsLongitude = value;
                    RaisePropertyChanged("GPSLongitude");
                }
            }
        }

        private string _gpsGrid;
        /// <summary>
        /// Gets the GPS Grid as a string
        /// </summary>
        public string GPSGrid
        {
            get { return _gpsGrid; }
            internal set
            {
                if (_gpsGrid != value)
                {
                    _gpsGrid = value;
                    RaisePropertyChanged("GPSGrid");
                }
            }
        }

        private string _gpsAltitude;
        /// <summary>
        /// Gets the GPS Altitude as a string
        /// </summary>
        public string GPSAltitude
        {
            get { return _gpsAltitude; }
            set
            {
                if (_gpsAltitude != value)
                {
                    _gpsAltitude = value;
                    RaisePropertyChanged("GPSAltitude");
                }
            }
        }

        private string _gpsSatellitesTracked;
        /// <summary>
        /// Gets the GPS satellites tracked as a string
        /// </summary>
        public string GPSSatellitesTracked
        {
            get { return _gpsSatellitesTracked; }
            internal set
            {
                if (_gpsSatellitesTracked != value)
                {
                    _gpsSatellitesTracked = value;
                    RaisePropertyChanged("GPSSatellitesTracked");
                }
            }
        }

        private string _gpsSatellitesVisible;
        /// <summary>
        /// Gets the GPS satellites visible as a string
        /// </summary>
        public string GPSSatellitesVisible
        {
            get { return _gpsSatellitesVisible; }
            internal set
            {
                if (_gpsSatellitesVisible != value)
                {
                    _gpsSatellitesVisible = value;
                    RaisePropertyChanged("GPSSatellitesVisible");
                }
            }
        }

        private string _gpsSpeed;
        /// <summary>
        /// Gets the GPS speed as a string
        /// </summary>
        public string GPSSpeed
        {
            get { return _gpsSpeed; }
            internal set
            {
                if (_gpsSpeed != value)
                {
                    _gpsSpeed = value;
                    RaisePropertyChanged("GPSSpeed");
                }
            }
        }

        private string _gpsFreqError;
        /// <summary>
        /// Gets the GPS frequency error as a string
        /// </summary>
        public string GPSFreqError
        {
            get { return _gpsFreqError; }
            internal set
            {
                if (_gpsFreqError != value)
                {
                    _gpsFreqError = value;
                    RaisePropertyChanged("GPSFreqError");
                }
            }
        }

        private string _gpsStatus;
        /// <summary>
        /// Gets the GPS status as a string
        /// </summary>
        public string GPSStatus
        {
            get { return _gpsStatus; }
            internal set
            {
                if (_gpsStatus != value)
                {
                    _gpsStatus = value;
                    RaisePropertyChanged("GPSStatus");
                }
            }
        }

        private string _gpsUtcTime;
        /// <summary>
        /// Gets the GPS UTC time as a string
        /// </summary>
        public string GPSUtcTime
        {
            get { return _gpsUtcTime; }
            internal set
            {
                if (_gpsUtcTime != value)
                {
                    _gpsUtcTime = value;
                    RaisePropertyChanged("GPSUtcTime");
                }
            }
        }

        private void ParseGPSStatus(string s)
        {
            string[] words = s.Split('#');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Radio::ParseGPSStatus: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "lat":
                        {
                            GPSLatitude = value;
                            break;
                        }

                    case "lon":
                        {
                            GPSLongitude = value;
                            break;
                        }

                    case "grid":
                        {
                            GPSGrid = value;
                            break;
                        }

                    case "altitude":
                        {
                            GPSAltitude = value;
                            break;
                        }

                    case "tracked":
                        {
                            GPSSatellitesTracked = value;
                            break;
                        }

                    case "visible":
                        {
                            GPSSatellitesVisible = value;
                            break;
                        }

                    case "speed":
                        {
                            GPSSpeed = value;
                            break;
                        }

                    case "freq_error":
                        {
                            GPSFreqError = value;
                            break;
                        }

                    case "status":
                        {
                            GPSStatus = value;
                            break;
                        }

                    case "time":
                        {
                            GPSUtcTime = value;
                            break;
                        }
                }
            }
        }

        #endregion

        #region Mixer Routines

        private int _lineoutGain;
        /// <summary>
        /// The line out gain value from 0 to 100
        /// </summary>
        public int LineoutGain
        {
            get { return _lineoutGain; }
            set
            {
                int new_gain = value;

                // check limits
                if (new_gain < 0) new_gain = 0;
                if (new_gain > 100) new_gain = 100;

                if (_lineoutGain != new_gain)
                {
                    _lineoutGain = new_gain;
                    SendCommand("mixer lineout gain " + _lineoutGain);
                    RaisePropertyChanged("LineoutGain");
                }
                else if (new_gain != value)
                {
                    RaisePropertyChanged("LineoutGain");
                }
            }
        }

        private bool _lineoutMute;
        /// <summary>
        /// Mutes or unmutes the lineout output
        /// </summary>
        public bool LineoutMute
        {
            get { return _lineoutMute; }
            set
            {
                if (_lineoutMute != value)
                {
                    _lineoutMute = value;
                    SendCommand("mixer lineout mute " + Convert.ToByte(_lineoutMute));
                    RaisePropertyChanged("LineoutMute");
                }
            }
        }

        private int _headphoneGain;
        /// <summary>
        /// The headphone gain value from 0 to 100
        /// </summary>
        public int HeadphoneGain
        {
            get { return _headphoneGain; }
            set
            {
                int new_gain = value;

                // check limits
                if (new_gain < 0) new_gain = 0;
                if (new_gain > 100) new_gain = 100;

                if (_headphoneGain != new_gain)
                {
                    _headphoneGain = new_gain;
                    SendCommand("mixer headphone gain " + _headphoneGain);
                    RaisePropertyChanged("HeadphoneGain");
                }
                else if (new_gain != value)
                {
                    RaisePropertyChanged("HeadphoneGain");
                }
            }
        }

        private bool _headphoneMute;
        /// <summary>
        /// Mutes or unmutes the headphone output
        /// </summary>
        public bool HeadphoneMute
        {
            get { return _headphoneMute; }
            set
            {
                if (_headphoneMute != value)
                {
                    _headphoneMute = value;
                    SendCommand("mixer headphone mute " + Convert.ToByte(_headphoneMute));
                    RaisePropertyChanged("HeadphoneMute");
                }
            }
        }

        // Used to control the front panel speaker on/off on M models.  Also mapped to the accessory connector audio out.
        private bool _frontSpeakerMute;
        public bool FrontSpeakerMute
        {
            get { return _frontSpeakerMute; }
            set
            {
                // only allow this to be called on M models
                if (!ModelInfo.GetModelInfoForModel(_model).IsMModel) 
                    return;

                if (_frontSpeakerMute != value)
                {
                    _frontSpeakerMute = value;
                    SendCommand("mixer front_speaker mute " + Convert.ToByte(_frontSpeakerMute));
                    RaisePropertyChanged("FrontSpeakerMute");
                }
            }
        }

        private int _maxInternalPaPowerWatts = 100; // Default to 100W, unless otherwise specified by platform.
        public int MaxInternalPaPowerWatts
        {
            get => _maxInternalPaPowerWatts;
            set
            {
                if (value == _maxInternalPaPowerWatts) return;
                _maxInternalPaPowerWatts = value;
                RaisePropertyChanged(nameof(MaxInternalPaPowerWatts));
            }
        }

        private bool _externalPaAllowed = true; // Default to true, unless otherwise specified by platform.
        public bool ExternalPaAllowed
        {
            get => _externalPaAllowed;
            set
            {
                if (value == _externalPaAllowed) return;
                _externalPaAllowed = value;
                RaisePropertyChanged(nameof(ExternalPaAllowed));
            }
        }


        #endregion

        #region Update Routines

        private bool _updateFailed = false;
        public bool UpdateFailed
        {
            get { return _updateFailed; }
        }

        /// <summary>
        /// For internal use only.
        /// </summary>
        /// <param name="update_filename"></param>

        public void SendUpdateFile(string update_filename)
        {
            Thread t = new Thread(new ParameterizedThreadStart(Private_SendUpdateFile));
            t.Name = "Update File Thread";
            t.Priority = ThreadPriority.BelowNormal;
            t.Start(update_filename);
        }

        public void SendSSDRWaveformFile(string wave_filename)
        {
            Thread t = new Thread(new ParameterizedThreadStart(Private_SendSSDRWaveformFile));
            t.Name = "Waveform File Thread";
            t.Priority = ThreadPriority.Normal;
            t.Start(wave_filename);
        }

        public void SendDBImportFile(string database_filename)
        {
            Thread t = new Thread(new ParameterizedThreadStart(Private_SendDatabaseFile));
            t.Name = "Database Import File Thread";
            t.Priority = ThreadPriority.BelowNormal;
            t.IsBackground = true;
            t.Start(database_filename);
        }

        public void SendMemoryImportFile(string memory_filename)
        {
            Thread t = new Thread(new ParameterizedThreadStart(Private_SendMemoryFile));
            t.Name = "Memory Import File Thread";
            t.Priority = ThreadPriority.BelowNormal;
            t.IsBackground = true;
            t.Start(memory_filename);
        }

        private void StartMeterProcessThread()
        {
            _meterProcessThread = new Thread(new ThreadStart(ProcessMeterDataPacket_ThreadFunction));
            _meterProcessThread.Name = "Meter Packet Processing Thread";
            _meterProcessThread.Priority = ThreadPriority.Normal;
            _meterProcessThread.IsBackground = true;
            _meterProcessThread.Start();
        }

        private void StartFFTProcessThread()
        {
            _fftProcessThread = new Thread(new ThreadStart(ProcessFFTDataPacket_ThreadFunction));
            _fftProcessThread.Name = "FFT Packet Processing Thread";
            _fftProcessThread.Priority = ThreadPriority.Normal;
            _fftProcessThread.IsBackground = true;
            _fftProcessThread.Start();
        }

        //private void StartParseReadThread()
        //{
        //    // ensure this thread only gets started once
        //    if (_parseReadThread != null)
        //        return;

        //    _parseReadThread = new Thread(new ThreadStart(ParseRead_ThreadFunction));
        //    _parseReadThread.Name = "Status Message Processing Thread";
        //    _parseReadThread.Priority = ThreadPriority.Normal;
        //    _parseReadThread.IsBackground = true;
        //    _parseReadThread.Start();
        //}

        // this function no longer is used.  We will get the profile list from the client's copy of the list.
        //public void ReceiveDBMetaFile(string file_name)
        //{
        //    Thread t = new Thread(new ParameterizedThreadStart(Private_GetDBMetaFile));
        //    t.Name = "Database Meta Data File Thread";
        //    t.Priority = ThreadPriority.BelowNormal;
        //    t.IsBackground = true;
        //    t.Start(file_name);
        //}

        public void ReceiveSSDRDatabaseFile(string meta_subset_path, string destination_path, bool memories_export_checked)
        {

            Thread t = new Thread(new ParameterizedThreadStart(Private_GetSSDRDatabaseFile));
            t.Name = "Database Database File Thread";
            t.Priority = ThreadPriority.BelowNormal;
            t.IsBackground = true;

            List<string> path_list = new List<string>();
            path_list.Add(meta_subset_path);
            path_list.Add(destination_path);
            if (memories_export_checked)
                path_list.Add("CSV");

            t.Start(path_list);
        }

        private string _databaseExportException;
        public string DatabaseExportException
        {
            get { return _databaseExportException; }
            set
            {
                _databaseExportException = value;
                RaisePropertyChanged("DatabaseExportException");
            }
        }

        private bool _databaseExportComplete = true;
        public bool DatabaseExportComplete
        {
            get { return _databaseExportComplete; }
            set
            {
                if (_databaseExportComplete != value)
                {
                    _databaseExportComplete = value;
                    RaisePropertyChanged("DatabaseExportComplete");
                }
            }
        }

        private bool _databaseImportComplete = true;
        public bool DatabaseImportComplete
        {
            get { return _databaseImportComplete; }
            set
            {
                _databaseImportComplete = value;
                RaisePropertyChanged("DatabaseImportComplete");
            }
        }

        private bool _unityResultsImportComplete = true;
        public bool UnityResultsImportComplete
        {
            get { return _unityResultsImportComplete; }
            set
            {
                _unityResultsImportComplete = value;
            }
        }

        private void Private_GetSSDRDatabaseFile(object obj)
        {
            if (obj == null)
            {
                Debug.WriteLine("Null object passed into GetSSDRDatabaseFile");
                return;
            }

            List<string> path_list = (List<string>)obj;
            DatabaseExportComplete = false;
            _metaSubsetTransferComplete = false;
            /* Index 0 contains the meta_subset path */
            Private_SendMetaSubsetFile(path_list[0]);

            int timeout = 0;
            while (_metaSubsetTransferComplete == false && timeout < 50)
            {
                Thread.Sleep(100);
                timeout++;
            }

            if (timeout >= 50)
            {
                Debug.WriteLine("Export SSDR Database File: Could not send meta_subset file");
                DatabaseExportComplete = true;
                return;
            }

            SendReplyCommand(new ReplyHandler(UpdateReceivePort), "file download db_package");

            timeout = 0;
            while (_receive_port == -1 && timeout++ < 100)
                Thread.Sleep(100);

            if (_receive_port == -1)
                _receive_port = 42607;

            string timestamp_string = "";
            string config_file = "";
            string memories_file = "";

            TcpClient client = null;
            FileStream file_stream = null;
            TcpListener server = null;

            try
            {
                /* Open meta_data file tinto a file stream */
                /* path_list[1] is the destination directory */
                DateTime timestamp = DateTime.UtcNow;

                //Filename format: SSDR_Config_08-04-14_3.16_PM_v3.0.6.75.ssdr_cfg
                timestamp_string = timestamp.ToLocalTime().ToString("MM-dd-yy_") + timestamp.ToLocalTime().ToShortTimeString().Replace(":", ".").Replace(" ", "_");
                string version = FlexVersion.ToString(_version);

                config_file = $"{path_list[1]}\\SSDR_Config_{timestamp_string}_v{version}.ssdr_cfg";
                file_stream = File.Create(config_file);


                IPAddress ip = IPAddress.Any;

                server = new TcpListener(ip, _receive_port);

                /* Start Listening */
                server.Start();

                Byte[] bytes = new Byte[1500];

                Debug.WriteLine("Listening for SSDR_Database file");

                /* Blocking call to accept requests */
                client = server.AcceptTcpClient();
                Debug.WriteLine("Connected to client! ");

                /* Get stream object */
                NetworkStream stream = client.GetStream();

                /* Loop to receive all the data sent by the client */
                int i;
                while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                {
                    /* Translate bytes to ascii string */
                    //data = System.Text.Encoding.ASCII.GetString(bytes, 0, i);
                    //Debug.WriteLine("Received : " + data);
                    file_stream.Write(bytes, 0, i);
                }
                file_stream.Close();
                /* Extract memories file */
                if (path_list.Contains("CSV"))
                {
                    /* Kind of a hacky way of doing this but I didn't feel like making a helper class just to be 
                     * able to pass in whether the Memories export is passed */

                    memories_file = $"{path_list[1]}\\SSDR_Memories_{timestamp_string}_v{version}.csv";
                    using (FileStream memories_stream = File.Create(memories_file))
                    {
                        using (ZipFile zip = ZipFile.Read(config_file))
                        {
                            ZipEntry entry = zip["memories.csv"];
                            if (entry != null)
                                entry.Extract(memories_stream);
                        }

                        memories_stream.Close();
                    }
                }

                Debug.WriteLine("Finished getting SSDR_Database file");
            }
            catch (SocketException e)
            {
                Debug.WriteLine("SocketException: {0}", e);
                DatabaseExportException = "Network connection error.  Please check your network settings and try again.";
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.WriteLine("UnauthorizedAccessException: {0}", e);
                DatabaseExportException = "Unauthorized access to export location \n\n" + path_list[1] +
                    "\n\nPlease check permissions or select a different folder.";
            }
            catch (DirectoryNotFoundException e)
            {
                Debug.WriteLine("DirectoryNotFoundException: {0}", e);
                DatabaseExportException = "Directory \n\n" + path_list[1] + "\n\n not found.  Please select a valid directory.";
            }
            catch (Exception e)
            {
                Debug.WriteLine("Caught exception: {0}", e);
                DatabaseExportException = "Configuration export failed.\n\n" + e.ToString();
            }
            finally
            {
                if (client != null)
                    client.Close();

                if (server != null)
                {
                    try
                    {
                        server.Stop();
                    }
                    catch (Exception)
                    { }
                }


                DatabaseExportComplete = true;
            }
        }

        public bool ReceiveTestingResultsFile(object obj)
        {
            string dest_file_full_path = (string)obj;

            SendReplyCommand(new ReplyHandler(UpdateReceivePort), "file download unity_test");

            int timeout = 0;
            while (_receive_port == -1 && timeout++ < 100)
                Thread.Sleep(100);

            if (_receive_port == -1)
            {
                Console.WriteLine("Was not able to Update Recieve Port, setting port to 42607.");
                _receive_port = 42607;
            }

            TcpClient client = null;
            FileStream file_stream = null;
            TcpListener server = null;

            try
            {
                file_stream = File.Create(dest_file_full_path); // the unity file
                IPAddress ip = IPAddress.Any;
                server = new TcpListener(ip, _receive_port);

                /* Start Listening */
                server.Start();

                Byte[] bytes = new Byte[1500];

                Console.WriteLine("Waiting to accept TCP Client on port " + _receive_port);

                /* Blocking call to accept requests */

                timeout = 0;
                while (timeout++ < 100 && !server.Pending())
                {
                    Thread.Sleep(100);
                }

                if (!server.Pending())
                {
                    Console.WriteLine("Error, there were no pending TCP client requests. Exiting.");
                    return false;
                }
                client = server.AcceptTcpClient();
                Console.WriteLine("Connected to client! Getting Unity Output file");

                /* Get stream object */
                NetworkStream stream = client.GetStream();

                /* Loop to receive all the data sent by the client */
                int i;
                while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                {
                    /* Translate bytes to ascii string */
                    //data = System.Text.Encoding.ASCII.GetString(bytes, 0, i);
                    //Debug.WriteLine("Received : " + data);
                    file_stream.Write(bytes, 0, i);
                }

                file_stream.Close();
                Console.WriteLine("Finished getting Unity Output file");
                UnityResultsImportComplete = true;
                return true;
            }
            catch (SocketException e)
            {
                Console.WriteLine("SocketException: {0}", e);
                DatabaseExportException = "Network connection error.  Please check your network settings and try again.";
            }
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine("UnauthorizedAccessException: {0}", e);
                DatabaseExportException = "Unauthorized access to export location \n\n" + dest_file_full_path +
                    "\n\nPlease check permissions or select a different folder.";
            }
            catch (DirectoryNotFoundException e)
            {
                Console.WriteLine("DirectoryNotFoundException: {0}", e);
                DatabaseExportException = "Directory \n\n" + dest_file_full_path + "\n\n not found.  Please select a valid directory.";
            }
            catch (Exception e)
            {
                Console.WriteLine("Caught exception: {0}", e);
                DatabaseExportException = "Configuration export failed.\n\n" + e.ToString();
            }
            finally
            {
                if (file_stream != null)
                    file_stream.Close();

                if (client != null)
                    client.Close();

                if (server != null)
                    server.Stop();
            }

            return false;
        }

        // We no longer use this function.  We get the current list
        // of profiles from the client, not from the radio

        //private void Private_GetDBMetaFile(object obj)
        //{
        //    _exportMetaData_Received = false;

        //    string file_name = (string)obj;

        //    SendReplyCommand(new ReplyHandler(UpdateReceivePort), "file download db_meta_data");

        //    int timeout = 0;
        //    while (_receive_port == -1 && timeout++ < 100)
        //        Thread.Sleep(100);

        //    if (_receive_port == -1)
        //        _receive_port = 42607;

        //    try
        //    {
        //        /* Open meta_data file tinto a file stream */
        //        FileStream file_stream = File.Create("meta_data");
        //    }
        //    catch
        //    {
        //        Debug.WriteLine("Database Meta Data Download: Error opening meta_data file for writing");
        //    }

        //    try
        //    {
        //        IPAddress ip = IPAddress.Any;

        //        TcpListener server = new TcpListener(ip, _receive_port);

        //        /* Start Listening */
        //        server.Start();

        //        Byte[] bytes = new Byte[1500];
        //        String data = null;


        //        Debug.WriteLine("Listening for meta data file");

        //        /* Blockign call to accept requests */
        //        TcpClient client = server.AcceptTcpClient();
        //        Debug.WriteLine("Connected to client! ");

        //        data = null;

        //        /* Get stream object */
        //        NetworkStream stream = client.GetStream();

        //        using (StreamWriter sw = File.CreateText(file_name))
        //        {

        //            /* Loop to receive all the data sent by the client */
        //            int i;
        //            while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
        //            {
        //                /* Translate bytes to ascii string */
        //                data = System.Text.Encoding.ASCII.GetString(bytes, 0, i);
        //                sw.WriteLine(data);
        //                Debug.WriteLine("Received : " + data);
        //            }

        //            sw.Close();
        //        }
        //        client.Close();
        //        server.Stop();
        //        ExportMetaData_Received = true;
        //    }
        //    catch (SocketException e)
        //    {
        //        Debug.WriteLine("SocketException: {0}", e);
        //    }
        //}

        private void Private_SendMetaSubsetFile(object obj)
        {
            string meta_subset_filename = (string)obj;

            // check to make sure the file exists
            if (!File.Exists(meta_subset_filename))
            {
                Debug.WriteLine("Database Import: Database file does not exist (" + meta_subset_filename + ")");
                return;
            }

            // read the file contents into a byte buffer to be sent via TCP
            byte[] file_buffer;
            FileStream stream = null;
            try
            {
                // open the file into a file stream
                stream = File.OpenRead(meta_subset_filename);

                // allocate a buffer large enough for the file
                file_buffer = new byte[stream.Length];

                // read the entire contents of the file into the buffer
                stream.Read(file_buffer, 0, (int)stream.Length);
            }
            catch (Exception)
            {
                Debug.WriteLine("Database Export: Error reading the meta_subset file");
                return;
            }
            finally
            {
                // cleanup -- close the stream
                stream.Close();
            }

            // create a TCP client to send the data to the radio
            TcpClient tcp_client = null;
            NetworkStream tcp_stream = null;

            string filename = meta_subset_filename.Substring(meta_subset_filename.LastIndexOf("\\") + 1);

            SendReplyCommand(new ReplyHandler(UpdateUpgradePort), "file upload " + file_buffer.Length + " db_meta_subset");

            int timeout = 0;
            while (_upgrade_port == -1 && timeout++ < 100)
                Thread.Sleep(100);

            if (_upgrade_port == -1)
                _upgrade_port = 4995;

            if (timeout < 2)
                Thread.Sleep(200); // wait for the server to get setup and be ready to accept the connection

            // connect to the radio's upgrade port
            try
            {
                // create tcp client object and connect to the radio
                tcp_client = new TcpClient();
                Debug.WriteLine("Opening TCP Database Export port " + _upgrade_port.ToString());
                //_tcp_client.NoDelay = true; // hopefully minimize round trip command latency
                tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                tcp_stream = tcp_client.GetStream();
            }
            catch (Exception)
            {
                // lets try again on the new known update port if radio does not reply with proper response
                _upgrade_port = 42607;
                tcp_client.Close(); // ensure the previous object is closed to prevent orphaning the resource

                tcp_client = new TcpClient();

                try
                {
                    Debug.WriteLine("Opening TCP upgrade port " + _upgrade_port.ToString());
                    tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                    tcp_stream = tcp_client.GetStream();
                }
                catch (Exception)
                {
                    Debug.WriteLine("Update: Error opening the update TCP client");
                    tcp_client.Close();
                    return;
                }
            }

            // send the data over TCP
            try
            {
                tcp_stream.Write(file_buffer, 0, file_buffer.Length);
                _countTXCommand += file_buffer.Length + TCP_HEADER_SIZE;
            }
            catch (Exception)
            {
                Debug.WriteLine("Update: Error sending the update buffer over TCP");
                return;
            }
            finally
            {
                // clean up the upgrade TCP connection
                tcp_stream.Close();
                tcp_client.Close();
            }

            // note: removing this delay causes both the radio and client to crash on the next line
            Thread.Sleep(5000); // wait 5 seconds, then disconnect

            _metaSubsetTransferComplete = true;

            // close main command channel too since the radio will reboot
            //if (_tcp_client != null)
            //{
            //    _tcp_client.Close();
            //    _tcp_client = null;
            //}

            // if we get this far, the file contents have been sent
        }

        //private void Private_GetDBMetaFile(object obj)
        //{
        //    SendReplyCommand(new ReplyHandler(UpdateReceivePort), "file download db_meta_data");

        //    int timeout = 0;
        //    while (_receive_port == -1 && timeout++ < 100)
        //        Thread.Sleep(100);

        //    if (_receive_port == -1)
        //        _receive_port = 42607;

        //    try
        //    {
        //        /* Open meta_data file tinto a file stream */
        //        FileStream file_stream = File.Create("meta_data");
        //    }
        //    catch
        //    {
        //        Debug.WriteLine("Database Meta Data Download: Error opening meta_data file for writing");
        //    }

        //    try
        //    {
        //        IPAddress ip = IPAddress.Any;

        //        TcpListener server = new TcpListener(ip, _receive_port);

        //        /* Start Listening */
        //        server.Start();

        //        Byte[] bytes = new Byte[1500];
        //        String data = null;


        //        Debug.WriteLine("Listening for meta data file");

        //        /* Blockign call to accept requests */
        //        TcpClient client = server.AcceptTcpClient();
        //        Debug.WriteLine("Connected to client! ");

        //        data = null;

        //        /* Get stream object */
        //        NetworkStream stream = client.GetStream();

        //        /* Loop to receive all the data sent by the client */
        //        int i;
        //        while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
        //        {
        //            /* Translate bytes to ascii string */
        //            data = System.Text.Encoding.ASCII.GetString(bytes, 0, i);
        //            Debug.WriteLine("Received : " + data);
        //        }
        //        client.Close();
        //        server.Stop();
        //    }
        //    catch (SocketException e)
        //    {
        //        Debug.WriteLine("SocketException: {0}", e);
        //    }

        //}

        private void Private_SendDatabaseFile(object obj)
        {
            string database_filename = (string)obj;
            //DatabaseImportComplete = false;

            // check to make sure the file exists
            if (!File.Exists(database_filename))
            {
                Debug.WriteLine("Database Import: Database file does not exist (" + database_filename + ")");
                return;
            }

            // read the file contents into a byte buffer to be sent via TCP
            byte[] update_file_buffer;
            FileStream stream = null;
            try
            {
                // open the file into a file stream
                stream = File.OpenRead(database_filename);

                // allocate a buffer large enough for the file
                update_file_buffer = new byte[stream.Length];

                // read the entire contents of the file into the buffer
                stream.Read(update_file_buffer, 0, (int)stream.Length);
            }
            catch (Exception)
            {
                Debug.WriteLine("Database Import: Error reading the database file");
                return;
            }
            finally
            {
                // cleanup -- close the stream
                stream.Close();
            }

            // create a TCP client to send the data to the radio
            TcpClient tcp_client = null;
            NetworkStream tcp_stream = null;

            string filename = database_filename.Substring(database_filename.LastIndexOf("\\") + 1);

            SendReplyCommand(new ReplyHandler(UpdateUpgradePort), "file upload " + update_file_buffer.Length + " db_import");

            int timeout = 0;
            while (_upgrade_port == -1 && timeout++ < 100)
                Thread.Sleep(100);

            if (_upgrade_port == -1)
                _upgrade_port = 4995;

            if (timeout < 2)
                Thread.Sleep(200); // wait for the server to get setup and be ready to accept the connection

            // connect to the radio's upgrade port
            try
            {
                // create tcp client object and connect to the radio
                tcp_client = new TcpClient();
                Debug.WriteLine("Opening TCP Database Import port " + _upgrade_port.ToString());
                //_tcp_client.NoDelay = true; // hopefully minimize round trip command latency
                tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                tcp_stream = tcp_client.GetStream();
            }
            catch (Exception)
            {
                // lets try again on the new known update port if radio does not reply with proper response
                _upgrade_port = 42607;
                tcp_client.Close(); // close the old object so we don't orphan the resource
                tcp_client = new TcpClient();

                try
                {
                    Debug.WriteLine("Opening TCP upgrade port " + _upgrade_port.ToString());
                    tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                    tcp_stream = tcp_client.GetStream();
                }
                catch (Exception)
                {
                    tcp_client.Close();
                    Debug.WriteLine("Update: Error opening the update TCP client");
                    return;
                }
            }

            // send the data over TCP
            try
            {
                tcp_stream.Write(update_file_buffer, 0, update_file_buffer.Length);
                _countTXCommand += update_file_buffer.Length + TCP_HEADER_SIZE;
            }
            catch (Exception)
            {
                Debug.WriteLine("Update: Error sending the update buffer over TCP");
                tcp_client.Close();
                return;
            }


            // clean up the upgrade TCP connection
            tcp_client.Close();

            // note: removing this delay causes both the radio and client to crash on the next line
            Thread.Sleep(5000); // wait 5 seconds, then disconnect

            //DatabaseImportComplete = true;

            // close main command channel too since the radio will reboot
            //if (_tcp_client != null)
            //{
            //    _tcp_client.Close();
            //    _tcp_client = null;
            //}

            // if we get this far, the file contents have been sent


        }

        private void Private_SendMemoryFile(object obj)
        {
            string memory_filename = (string)obj;
            //DatabaseImportComplete = false;

            // check to make sure the file exists
            if (!File.Exists(memory_filename))
            {
                Debug.WriteLine("Memory Import: Memory file does not exist (" + memory_filename + ")");
                return;
            }

            // read the file contents into a byte buffer to be sent via TCP
            byte[] update_file_buffer;
            FileStream stream = null;
            try
            {
                // open the file into a file stream
                stream = File.OpenRead(memory_filename);

                // allocate a buffer large enough for the file
                update_file_buffer = new byte[stream.Length];

                // read the entire contents of the file into the buffer
                stream.Read(update_file_buffer, 0, (int)stream.Length);
            }
            catch (Exception)
            {
                Debug.WriteLine("Memory Import: Error reading the memory file");
                return;
            }
            finally
            {
                // cleanup -- close the stream
                stream.Close();
            }

            // create a TCP client to send the data to the radio
            TcpClient tcp_client = null;
            NetworkStream tcp_stream = null;

            string filename = memory_filename.Substring(memory_filename.LastIndexOf("\\") + 1);

            SendReplyCommand(new ReplyHandler(UpdateUpgradePort), "file upload " + update_file_buffer.Length + " memories_csv_file");

            int timeout = 0;
            while (_upgrade_port == -1 && timeout++ < 100)
                Thread.Sleep(100);

            if (_upgrade_port == -1)
                _upgrade_port = 4995;

            if (timeout < 2)
                Thread.Sleep(200); // wait for the server to get setup and be ready to accept the connection

            // connect to the radio's upgrade port
            try
            {
                // create tcp client object and connect to the radio
                tcp_client = new TcpClient();
                Debug.WriteLine("Opening TCP Database Import port " + _upgrade_port.ToString());
                //_tcp_client.NoDelay = true; // hopefully minimize round trip command latency
                tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                tcp_stream = tcp_client.GetStream();
            }
            catch (Exception)
            {
                // lets try again on the new known update port if radio does not reply with proper response
                _upgrade_port = 42607;
                tcp_client.Close(); // close the old object so we don't orphan the resource
                tcp_client = new TcpClient();

                try
                {
                    Debug.WriteLine("Opening TCP upgrade port " + _upgrade_port.ToString());
                    tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                    tcp_stream = tcp_client.GetStream();
                }
                catch (Exception)
                {
                    tcp_client.Close();
                    Debug.WriteLine("Update: Error opening the update TCP client");
                    return;
                }
            }

            // send the data over TCP
            try
            {
                tcp_stream.Write(update_file_buffer, 0, update_file_buffer.Length);
                _countTXCommand += update_file_buffer.Length + TCP_HEADER_SIZE;
            }
            catch (Exception)
            {
                Debug.WriteLine("Update: Error sending the update buffer over TCP");
                tcp_client.Close();
                return;
            }


            // clean up the upgrade TCP connection
            tcp_client.Close();

            // note: removing this delay causes both the radio and client to crash on the next line
            Thread.Sleep(5000); // wait 5 seconds, then disconnect

            //DatabaseImportComplete = true;

            // close main command channel too since the radio will reboot
            //if (_tcp_client != null)
            //{
            //    _tcp_client.Close();
            //    _tcp_client = null;
            //}

            // if we get this far, the file contents have been sent


        }

        private void Private_SendSSDRWaveformFile(object obj)
        {
            string waveform_filename = (string)obj;

            // check to make sure the file exists
            if (!File.Exists(waveform_filename))
            {
                Debug.WriteLine("Update: Update file does not exist (" + waveform_filename + ")");
                return;
            }

            // TODO: verify file integrity

            // read the file contents into a byte buffer to be sent via TCP
            byte[] update_file_buffer;
            FileStream stream = null;
            try
            {
                // open the file into a file stream
                stream = File.OpenRead(waveform_filename);

                // allocate a buffer large enough for the file
                update_file_buffer = new byte[stream.Length];

                // read the entire contents of the file into the buffer
                stream.Read(update_file_buffer, 0, (int)stream.Length);
            }
            catch (Exception)
            {
                Debug.WriteLine("Update: Error reading the upgrade file");
                return;
            }
            finally
            {
                // cleanup -- close the stream
                stream.Close();
            }


            // create a TCP client to send the data to the radio
            TcpClient tcp_client = null;
            NetworkStream tcp_stream = null;

            string filename = waveform_filename.Substring(waveform_filename.LastIndexOf("\\") + 1);
            SendCommand("file filename " + filename);
            SendReplyCommand(new ReplyHandler(UpdateUpgradePort), "file upload " + update_file_buffer.Length + " new_waveform");

            int timeout = 0;
            while (_upgrade_port == -1 && timeout++ < 100)
                Thread.Sleep(100);

            if (_upgrade_port == -1)
                _upgrade_port = 4995;

            if (timeout < 2)
                Thread.Sleep(200); // wait for the server to get setup and be ready to accept the connection

            // connect to the radio's upgrade port
            try
            {
                // create tcp client object and connect to the radio
                tcp_client = new TcpClient();
                Debug.WriteLine("Opening TCP upgrade port " + _upgrade_port.ToString());
                //_tcp_client.NoDelay = true; // hopefully minimize round trip command latency
                tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                tcp_stream = tcp_client.GetStream();
            }
            catch (Exception)
            {
                // lets try again on the new known update port if radio does not reply with proper response
                _upgrade_port = 42607;
                tcp_client.Close(); // ensure the old object is disposed so we don't orphan it
                tcp_client = new TcpClient();

                try
                {
                    Debug.WriteLine("Opening TCP upgrade port " + _upgrade_port.ToString());
                    tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                    tcp_stream = tcp_client.GetStream();
                }
                catch (Exception)
                {
                    Debug.WriteLine("Update: Error opening the update TCP client");
                    tcp_client.Close();
                    return;
                }
            }

            // send the data over TCP
            try
            {
                tcp_stream.Write(update_file_buffer, 0, update_file_buffer.Length);
                _countTXCommand += update_file_buffer.Length + TCP_HEADER_SIZE;
            }
            catch (Exception)
            {
                Debug.WriteLine("Update: Error sending the update buffer over TCP");
                tcp_stream.Close();
                return;
            }

            // clean up the upgrade TCP connection
            tcp_client.Close();

            // note: removing this delay causes both the radio and client to crash on the next line
            Thread.Sleep(5000); // wait 5 seconds, then disconnect

            // close main command channel too since the radio will reboot
            //if (_tcp_client != null)
            //{
            //    _tcp_client.Close();
            //    _tcp_client = null;
            //}

            // if we get this far, the file contents have been sent
        }

        private void Private_SendUpdateFile(object obj)
        {
            string update_filename = (string)obj;

            // check to make sure the file exists
            if (!File.Exists(update_filename))
            {
                Debug.WriteLine("Update: Update file does not exist (" + update_filename + ")");
                return;
            }

            // TODO: verify file integrity

            // read the file contents into a byte buffer to be sent via TCP
            byte[] update_file_buffer;
            FileStream stream = null;
            try
            {
                // open the file into a file stream
                stream = File.OpenRead(update_filename);

                // allocate a buffer large enough for the file
                update_file_buffer = new byte[stream.Length];

                // read the entire contents of the file into the buffer
                stream.Read(update_file_buffer, 0, (int)stream.Length);
            }
            catch (Exception)
            {
                Debug.WriteLine("Update: Error reading the upgrade file");
                return;
            }
            finally
            {
                // cleanup -- close the stream
                stream.Close();
            }


            // create a TCP client to send the data to the radio
            TcpClient tcp_client = null;
            NetworkStream tcp_stream = null;

            string filename = update_filename.Substring(update_filename.LastIndexOf("\\") + 1);
            SendCommand("file filename " + filename);
            SendReplyCommand(new ReplyHandler(UpdateUpgradePort), "file upload " + update_file_buffer.Length + " update");

            int timeout = 0;
            while (_upgrade_port == -1 && timeout++ < 100)
                Thread.Sleep(100);

            if (_upgrade_port == -1)
                _upgrade_port = 4995;

            if (timeout < 2)
                Thread.Sleep(200); // wait for the server to get setup and be ready to accept the connection

            // connect to the radio's upgrade port
            try
            {
                // create tcp client object and connect to the radio
                tcp_client = new TcpClient();
                Debug.WriteLine("Opening TCP upgrade port " + _upgrade_port.ToString());
                //_tcp_client.NoDelay = true; // hopefully minimize round trip command latency
                tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                tcp_stream = tcp_client.GetStream();
            }
            catch (Exception)
            {
                // lets try again on the new known update port if radio does not reply with proper response
                _upgrade_port = 42607;
                tcp_client.Close(); // ensure the old object is disposed so we don't orphan it
                tcp_client = new TcpClient();

                try
                {
                    Debug.WriteLine("Opening TCP upgrade port " + _upgrade_port.ToString());
                    tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                    tcp_stream = tcp_client.GetStream();
                }
                catch (Exception)
                {
                    Debug.WriteLine("Update: Error opening the update TCP client");
                    tcp_client.Close();
                    return;
                }
            }

            _updating = true;

            // send the data over TCP
            try
            {
                tcp_stream.Write(update_file_buffer, 0, update_file_buffer.Length);
                _countTXCommand += update_file_buffer.Length + TCP_HEADER_SIZE;
            }
            catch (Exception)
            {
                Debug.WriteLine("Update: Error sending the update buffer over TCP");
                tcp_stream.Close();
                return;
            }

            // clean up the upgrade TCP connection
            tcp_client.Close();

            // note: removing this delay causes both the radio and client to crash on the next line
            Thread.Sleep(5000); // wait 5 seconds, then disconnect

            // close main command channel too since the radio will reboot
            //if (_tcp_client != null)
            //{
            //    _tcp_client.Close();
            //    _tcp_client = null;
            //}

            // if we get this far, the file contents have been sent
        }

        private bool _updating;
        internal bool Updating
        {
            get => _updating;
            set
            {
                if (_updating == value)
                    return;
                
                _updating = value;
                UpdateConnectedState();
            }
        }

        private int _receive_port = -1;
        private void UpdateReceivePort(int seq, uint resp_val, string s)
        {
            if (resp_val != 0) return;

            int temp;
            bool b = int.TryParse(s, out temp);

            if (!b)
            {
                Debug.WriteLine("Radio::UpgradeReceivePort-Error parsing Receive Port (" + s + ")");
                return;
            }
            else
            {
                _receive_port = temp;
                Debug.WriteLine("Receive Port updated to: " + s);
            }
        }

        private int _upgrade_port = -1;
        private void UpdateUpgradePort(int seq, uint resp_val, string s)
        {
            if (resp_val != 0) return;

            int temp;
            bool b = int.TryParse(s, out temp);

            if (!b)
            {
                Debug.WriteLine("Radio::UpdateUpgradePort-Error parsing Upgrade Port (" + s + ")");
                return;
            }
            else
            {
                _upgrade_port = temp;
                Debug.WriteLine("Upgrade Port updated to: " + s);
            }
        }

        private string _regionCode;
        /// <summary>
        /// Gets the region code of the radio as a string.
        /// </summary>
        public string RegionCode
        {
            get { return _regionCode; }
            internal set
            {
                if (_regionCode != value)
                {
                    _regionCode = value;
                    RaisePropertyChanged("RegionCode");
                }
            }
        }

        /// <summary>
        /// For internal use only.
        /// </summary>
        /// <param name="update_filename"></param>
        public void SendTurfFile(string update_filename)
        {
            Thread t = new Thread(new ParameterizedThreadStart(Private_SendTurfFile));
            t.Name = "Update File Thread";
            t.Priority = ThreadPriority.BelowNormal;
            t.Start(update_filename);
        }

        private void Private_SendTurfFile(object obj)
        {
            string turf_filename = (string)obj;

            // check to make sure the file exists
            if (!File.Exists(turf_filename))
            {
                Debug.WriteLine("Update: Update file does not exist (" + turf_filename + ")");
                return;
            }

            // TODO: verify file integrity

            // read the file contents into a byte buffer to be sent via TCP
            byte[] turf_file_buffer;
            try
            {
                // open the file into a file stream
                using (FileStream stream = File.OpenRead(turf_filename))
                {

                    // allocate a buffer large enough for the file
                    turf_file_buffer = new byte[stream.Length];

                    // read the entire contents of the file into the buffer
                    stream.Read(turf_file_buffer, 0, (int)stream.Length);

                    stream.Close();
                }
            }
            catch (Exception)
            {
                Debug.WriteLine("Update: Error reading the turf file");
                return;
            }

            // create a TCP client to send the data to the radio
            TcpClient tcp_client = null;
            NetworkStream tcp_stream = null;

            SendReplyCommand(new ReplyHandler(UpdateUpgradePort), "file upload " + turf_file_buffer.Length + " turf");

            int timeout = 0;
            while (_upgrade_port == -1 && timeout++ < 10)
                Thread.Sleep(100);

            if (_upgrade_port == -1)
                _upgrade_port = 4995;

            if (timeout < 2)
                Thread.Sleep(200); // wait for the server to get setup and be ready to accept the connection

            // connect to the radio's upgrade port
            try
            {
                // create tcp client object and connect to the radio
                tcp_client = new TcpClient();
                //_tcp_client.NoDelay = true; // hopefully minimize round trip command latency
                tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                tcp_stream = tcp_client.GetStream();
            }
            catch (Exception)
            {
                // lets try again on the new known update port if radio does not reply with proper response
                _upgrade_port = 42607;
                tcp_client.Close(); // ensure the old object is disposed so it will not be orphaned
                tcp_client = new TcpClient();

                try
                {
                    Debug.WriteLine("Opening TCP upgrade port " + _upgrade_port.ToString());
                    tcp_client.Connect(new IPEndPoint(IP, _upgrade_port));
                    tcp_stream = tcp_client.GetStream();
                }
                catch (Exception)
                {
                    Debug.WriteLine("Update: Error opening the update TCP client");
                    tcp_client.Close();
                    return;
                }
            }

            // send the data over TCP
            try
            {
                tcp_stream.Write(turf_file_buffer, 0, turf_file_buffer.Length);
                _countTXCommand += turf_file_buffer.Length + TCP_HEADER_SIZE;
            }
            catch (Exception)
            {
                tcp_stream.Close();
                Debug.WriteLine("Update: Error sending the turf buffer over TCP");
                return;
            }

            // clean up the turf TCP connection
            tcp_client.Close();

            // if we get this far, the file contents have been sent
        }

        private void ParseUpdateStatus(string s)
        {
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("Update::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }
                string key = tokens[0];
                string value = tokens[1];

                switch (key)
                {
                    case "failed":
                        {
                            byte fail;
                            bool b = byte.TryParse(value, out fail);
                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseUpdateStatus: Invalid value for failed(" + kv + ")");
                                continue;
                            }

                            _updateFailed = Convert.ToBoolean(fail);

                            // close main command channel too since the radio will reboot
                            _commandCommunication.Disconnect();

                            RaisePropertyChanged("UpdateFailed");
                            break;
                        }

                    case "reason":
                        {
                            Debug.WriteLine("Update faild for reason: " + value);
                            break;
                        }
                    case "transfer":
                        {
                            double temp;
                            bool b = StringHelper.TryParseDouble(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("Radio::ParseUpdateStatus - transfer: Invalid value (" + kv + ")");
                                continue;
                            }

                            UploadStatus = temp;
                            break;
                        }
                }
            }
        }

        private void ParseTurfStatus(string s)
        {
            string[] words = s.Split(' ');

            switch (words[0].ToLower())
            {
                case "success":
                    {
                        GetInfo(); // update the region code since the turf upload succeeded
                        OnMessageReceived(MessageSeverity.Info, "Region change succeeded!");
                    }
                    break;

                case "fail":
                    {
                        string failure_mode = "";
                        if (words.Length > 1)
                            failure_mode = words[1];

                        OnMessageReceived(MessageSeverity.Error, "Region change failed: " + failure_mode);
                    }
                    break;
            }
        }

        private void ParseAPFStatus(string s)
        {
            string[] words = s.Split(' ');


            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("APF::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }
                string key = tokens[0];
                string value = tokens[1];

                switch (key)
                {
                    case "mode":
                        int mode;
                        bool b = int.TryParse(value, out mode);
                        if (!b)
                        {
                            Debug.WriteLine("Radio::ParseAPFStatus: Invalid APF Mode (" + kv + ")");
                            continue;
                        }
                        if (mode == 0)
                            _apfMode = false;
                        else if (mode == 1)
                            _apfMode = true;
                        else
                        {
                            Debug.WriteLine("Radio::ParseAPFStatus: Invalid APF Mode Number (" + mode + ")");
                            continue;
                        }

                        RaisePropertyChanged("APFMode");
                        break;
                    case "gain":
                        double temp;
                        b = StringHelper.TryParseDouble(value, out temp);

                        if (!b)
                        {
                            Debug.WriteLine("Radio::ParseAPFStatus:: Invalid APFGain value (" + kv + ")");
                            continue;
                        }
                        _apfGain = temp;
                        RaisePropertyChanged("APFGain");
                        break;
                    case "qfactor":
                        b = StringHelper.TryParseDouble(value, out temp);

                        if (!b)
                        {
                            Debug.WriteLine("Radio::ParseAPFStatus:: Invalid APFGain value (" + kv + ")");
                            continue;
                        }

                        _apfQFactor = temp;
                        RaisePropertyChanged("APFQFactor");
                        break;
                }
            }
        }


        private double _uploadStatus = 0.0;
        /// <summary>
        /// For internal use only.
        /// </summary>
        public double UploadStatus
        {
            get { return _uploadStatus; }
            internal set
            {
                _uploadStatus = value;
                RaisePropertyChanged("UploadStatus");
            }
        }

        #endregion

        #region Equalizer Routines
        /// <summary>
        /// Docs not available
        /// </summary>
        /// <param name="eq_select"></param>
        /// <returns></returns>
        public Equalizer CreateEqualizer(EqualizerSelect eq_select)
        {
            if (_equalizers.Count >= 2)
            {
                Equalizer eq = FindEqualizerByEQSelect(eq_select);
                if (eq == null) return null;
                eq.RequestEqualizerInfo();
                return eq;
            }
            else
            {
                return new Equalizer(this, eq_select);
            }
        }

        ///// <summary>
        ///// Docs not available
        ///// </summary>
        ///// <param name="eq_select"></param>
        //public void RemoveEqualizer(EqualizerSelect eq_select)
        //{
        //    lock (_equalizers)
        //    {
        //        Equalizer eq = FindEqualizerByEQSelect(eq_select);
        //        if (eq == null) return;

        //        //eq.Remove();
        //        _equalizers.Remove(eq);
        //    }
        //}

        public delegate void EqualizerAddedEventHandler(Equalizer eq);
        /// <summary>
        /// Docs not available
        /// </summary>
        public event EqualizerAddedEventHandler EqualizerAdded;

        private void OnEqualizerAdded(Equalizer eq)
        {
            if (EqualizerAdded != null)
                EqualizerAdded(eq);
        }

        public delegate void EqualizerRemovedEventHandler(Equalizer eq);
        /// <summary>
        /// Docs not available
        /// </summary>
        public event EqualizerRemovedEventHandler EqualizerRemoved;
        private bool _metaSubsetTransferComplete = false;

        private void OnEqualizerRemoved(Equalizer eq)
        {
            if (EqualizerRemoved != null)
                EqualizerRemoved(eq);
        }

        internal void AddEqualizer(Equalizer new_eq)
        {
            lock (_equalizers)
            {
                Equalizer eq = FindEqualizerByEQSelect(new_eq.EQ_select);
                if (eq != null) return; // already in the list

                _equalizers.Add(new_eq);
                OnEqualizerAdded(new_eq);
            }
        }

        /// <summary>
        /// Docs not available
        /// </summary>
        /// <param name="eq_select"></param>
        /// <returns></returns>
        public Equalizer FindEqualizerByEQSelect(EqualizerSelect eq_select)
        {
            lock (_equalizers)
                return _equalizers.FirstOrDefault(x => x.EQ_select == eq_select);
        }
        #endregion

        #region Xvtr Routines

        /// <summary>
        /// Create a new XVTR object
        /// </summary>
        /// <returns>A reference to the new XVTR object</returns>
        public Xvtr CreateXvtr()
        {
            return new Xvtr(this);
        }

        /// <summary>
        /// Find a Xvtr object by index number
        /// </summary>
        /// <param name="index">The index number for the XVTR</param>
        /// <returns></returns>
        public Xvtr FindXvtrByIndex(int index)
        {
            lock (_xvtrs)
                return _xvtrs.FirstOrDefault(x => x.Index == index);
        }

        internal void AddXvtr(Xvtr xvtr)
        {
            lock (_xvtrs)
            {
                if (_xvtrs.Contains(xvtr)) return;
                _xvtrs.Add(xvtr);
            }
            OnXvtrAdded(xvtr);
        }

        internal void RemoveXvtr(Xvtr xvtr)
        {
            lock (_xvtrs)
            {
                if (!_xvtrs.Contains(xvtr)) return;
                _xvtrs.Remove(xvtr);
            }
            OnXvtrRemoved(xvtr);
        }

        /// <summary>
        /// Delegate event handler for the XvtrRemoved event
        /// </summary>
        /// <param name="xvtr">The XVTR to be removed</param>
        public delegate void XvtrRemovedEventHandler(Xvtr xvtr);
        /// <summary>
        /// This event is raised when a XVTR is removed from the radio
        /// </summary>
        public event XvtrRemovedEventHandler XvtrRemoved;

        private void OnXvtrRemoved(Xvtr xvtr)
        {
            if (XvtrRemoved != null)
                XvtrRemoved(xvtr);
        }

        /// <summary>
        /// Delegate event handler for the XVTRAdded event
        /// </summary>
        /// <param name="xvtr">The XVTR object being added</param>
        public delegate void XvtrAddedEventHandler(Xvtr xvtr);
        /// <summary>
        /// This event is raised when a new XVTR has been added to the radio
        /// </summary>
        public event XvtrAddedEventHandler XvtrAdded;

        internal void OnXvtrAdded(Xvtr xvtr)
        {
            if (XvtrAdded != null)
                XvtrAdded(xvtr);
        }

        #endregion

        #region Memory Routines

        private void ParseMemoryStatus(string status)
        {
            string[] words = status.Split(' ');

            // handle minimum words
            if (words.Length < 2 || words[0] == "")
            {
                Debug.WriteLine("ParseMemoryStatus: Too few words for Memory status -- min 2 (\"memory " + status + "\")");
                return;
            }

            uint index;
            bool b = uint.TryParse(words[0], out index);
            if (!b)
            {
                Debug.WriteLine("ParseMemoryStatus: Invalid memory index (\"memory " + status + "\")");
                return;
            }

            // if we make it to here, we have a good memory index

            // Find a reference to the Memory using the index (assuming it exists)
            Memory memory = FindMemoryByIndex((int)index);
            bool add_memory = false;

            // do we need to add the Memory?
            if (memory == null)
            {
                // yes -- make sure that we aren't going to add the Memory just to need to remove it
                if (status.Contains("removed")) return;

                // create the memory and populate the fields
                memory = new Memory(this, (int)index);
                add_memory = true;
            }

            // if we made it this far, we have a good reference to a Memory

            // is the spot being removed?
            if (status.Contains("removed"))
            {
                // yes -- remove it
                RemoveMemory(memory);
                return;
            }

            // pass along the status message to be parsed within the Memory class
            memory.StatusUpdate(status.Substring(words[0].Length + 1)); // Send everything after this: "memory <index> "

            if (add_memory)
                AddMemory(memory);
        }
        public void RequestMemory()
        {
            SendCommand("memory create");
        }

        internal void AddMemory(Memory memory)
        {
            lock (_memoryList)
            {
                if (_memoryList.Contains(memory)) return;
                _memoryList.Add(memory);
            }

            OnMemoryAdded(memory);
            RaisePropertyChanged("MemoryList");
        }

        internal void RemoveMemory(Memory mem)
        {
            lock (_memoryList)
            {
                if (!_memoryList.Contains(mem)) return;
                _memoryList.Remove(mem);
            }

            OnMemoryRemoved(mem);
            RaisePropertyChanged("MemoryList");
        }

        /// <summary>
        /// Delegate event handler for the MemoryRemoved event
        /// </summary>
        /// <param name="mem">The Memory object being removed</param>
        public delegate void MemoryRemovedEventHandler(Memory mem);
        /// <summary>
        /// This event is raised when a Memory is removed from the radio
        /// </summary>
        public event MemoryRemovedEventHandler MemoryRemoved;

        private void OnMemoryRemoved(Memory mem)
        {
            if (MemoryRemoved != null)
                MemoryRemoved(mem);
        }

        /// <summary>
        /// Delegate event handler for the MemoryAdded event
        /// </summary>
        /// <param name="mem">The Memory object being added</param>
        public delegate void MemoryAddedEventHandler(Memory mem);
        /// <summary>
        /// This event is raised when a new Memory has been added to the radio
        /// </summary>
        public event MemoryAddedEventHandler MemoryAdded;

        internal void OnMemoryAdded(Memory mem)
        {
            if (MemoryAdded != null)
                MemoryAdded(mem);
        }

        /// <summary>
        /// Find a MEmory object by index number
        /// </summary>
        /// <param name="index">The index number for the Memory</param>
        /// <returns>The Memory object</returns>
        public Memory FindMemoryByIndex(int index)
        {
            lock (_memoryList)
                return _memoryList.FirstOrDefault(x => x.Index == index);
        }

        #endregion

        #region Network Routines

        private IPAddress _staticIP;
        public IPAddress StaticIP
        {
            get { return _staticIP; }
            set
            {
                _staticIP = value;
                RaisePropertyChanged("StaticIP");
            }
        }

        private IPAddress _staticGateway;
        public IPAddress StaticGateway
        {
            get { return _staticGateway; }
            set
            {
                _staticGateway = value;
                RaisePropertyChanged("StaticGateway");
            }
        }

        private IPAddress _staticNetmask;
        public IPAddress StaticNetmask
        {
            get { return _staticNetmask; }
            set
            {
                _staticNetmask = value;
                RaisePropertyChanged("StaticNetmask");
            }
        }

        public EventHandler StaticIPSetSuccessful;
        private void OnStaticIPSetSucessful(EventArgs e)
        {
            if (StaticIPSetSuccessful != null)
                StaticIPSetSuccessful(this, e);
        }

        public EventHandler StaticIPSetFailed;
        private void OnStaticIPSetFailed(EventArgs e)
        {
            if (StaticIPSetFailed != null)
                StaticIPSetFailed(this, e);
        }

        public EventHandler DHCPSetSuccessful;
        private void OnDHCPSetSucessful(EventArgs e)
        {
            if (DHCPSetSuccessful != null)
                DHCPSetSuccessful(this, e);
        }

        public EventHandler DHCPSetFailed;
        private void OnDHCPSetFailed(EventArgs e)
        {
            if (DHCPSetFailed != null)
                DHCPSetFailed(this, e);
        }

        public void SetStaticNetworkParams()
        {
            if (_staticIP != null && _staticGateway != null && _staticNetmask != null)
            {
                SendReplyCommand(new ReplyHandler(SetStaticReplyHandler), "radio static_net_params ip=" + _staticIP.ToString() + " gateway=" + _staticGateway.ToString() + " netmask=" + _staticNetmask.ToString());
            }
        }

        private void SetStaticReplyHandler(int seq, uint resp_val, string reply)
        {
            if (resp_val != 0)
                OnStaticIPSetFailed(EventArgs.Empty);
            else
                OnStaticIPSetSucessful(EventArgs.Empty);
        }

        public void SetNetworkToDCHP()
        {
            SendReplyCommand(new ReplyHandler(SetDCHPReplyHandler), "radio static_net_params reset");
        }

        private void SetDCHPReplyHandler(int seq, uint resp_val, string reply)
        {
            if (resp_val != 0)
                OnDHCPSetFailed(EventArgs.Empty);
            else
                OnDHCPSetSucessful(EventArgs.Empty);
        }

        private bool _enforcePrivateIPConnections = true;
        public bool EnforcePrivateIPConnections
        {
            get { return _enforcePrivateIPConnections; }
            set
            {
                if (_enforcePrivateIPConnections != value)
                {
                    _enforcePrivateIPConnections = value;
                    SendCommand("radio set enforce_private_ip_connections=" + Convert.ToByte(_enforcePrivateIPConnections));
                    RaisePropertyChanged("EnforcePrivateIPConnections");
                }
            }
        }

        private int _mtu = 1500;
        public int MTU
        {
            get { return _mtu; }
            set
            {
                if (_mtu == value)
                {
                    return;
                }

                _mtu = value;

                if (_connected)
                {
                    SendRadioMTUCommand(_mtu);
                }

                RaisePropertyChanged("MTU");
            }
        }

        private void SendRadioMTUCommand(int mtu)
        {
            SendCommand($"client set enforce_network_mtu=1 network_mtu={mtu}");
        }

        private bool _wanServerConnected = false;
        public bool WanServerConnected
        {
            get { return _wanServerConnected; }
        }

        private bool _wanRadioAuthenticated = false;
        public bool WanRadioAuthenticated
        {
            get { return _wanRadioAuthenticated; }
        }

        #endregion

        #region Tuner Routines

        private void ParseTunerStatus(string s)
        {
            if (string.IsNullOrEmpty(s)) return;

            string[] words = s.Split(' ');

            if (words.Length < 2)
            {
                Debug.WriteLine("ParseTunerStatus: Too few words -- min 2 (" + words + ")");
                return;
            }

            string handle = words[0];
            Tuner tuner = FindTunerByHandle(handle);

            bool add_tuner = false;

            // is this a tuner we already knew about?
            if (tuner == null)
            {
                // no -- is it being removed?
                if (s.Contains("removed"))
                {
                    // yes -- don't bother adding it since we would just remove it
                    // then return since we are done here
                    return;
                }

                // create a new Tuner -- we will add this to the TunerList later
                tuner = new Tuner(this, handle);
                add_tuner = true;
            }

            // is the object being removed
            if (s.Contains("removed"))
            {
                // remove it and return
                RemoveTuner(tuner);
                return;
            }

            tuner.StatusUpdate(s.Substring(handle.Length + 1));

            if (add_tuner)
                AddTuner(tuner);
        }

        /// <summary>
        /// Find an Tuner object by handle (Client ID)
        /// </summary>
        /// <param name="handle">The handle for the Tuner</param>
        /// <returns>The Tuner object</returns>
        public Tuner FindTunerByHandle(string handle)
        {
            lock (_tuners)
                return _tuners.FirstOrDefault(t => t.Handle == handle);
        }

        internal void AddTuner(Tuner tuner)
        {
            lock (_tuners)
            {
                if (_tuners.Contains(tuner)) return;
                _tuners.Add(tuner);
                OnTunerAdded(tuner);
            }

            UpdateActiveTuner();
            RaisePropertyChanged("TunerList");
        }

        internal void RemoveTuner(Tuner tuner)
        {
            lock (_tuners)
            {
                if (!_tuners.Contains(tuner)) return;
                _tuners.Remove(tuner);
                OnTunerRemoved(tuner);
            }

            UpdateActiveAmplifier();
            RaisePropertyChanged("AmplifierList");
        }

        /// <summary>
        /// Delegate event handler for the TunerRemoved event
        /// </summary>
        /// <param name="tuner"></param>
        public delegate void TunerRemovedEventHandler(Tuner tuner);
        /// <summary>
        /// This event is raised when a Tuner is removed from the radio
        /// </summary>
        public event TunerRemovedEventHandler TunerRemoved;

        private void OnTunerRemoved(Tuner tuner)
        {
            if (TunerRemoved != null)
                TunerRemoved(tuner);
        }

        /// <summary>
        /// Delegate event handler for the TunerAdded event
        /// </summary>
        /// <param name="tuner"></param>
        public delegate void TunerAddedEventHandler(Tuner tuner);
        /// <summary>
        /// This event is raised when a Tuner has been added to the radio
        /// </summary>
        public event TunerAddedEventHandler TunerAdded;

        internal void OnTunerAdded(Tuner tuner)
        {
            if (TunerAdded != null)
                TunerAdded(tuner);
        }

        public bool radioShouldUseExtTuner => ActiveTuner != null &&
                                              ActiveTuner.IsOperate &&
                                              !string.IsNullOrEmpty(TransmitSlice?.TXAnt) &&
                                              ((TransmitSlice?.TXAnt == ActiveTuner.PortAAnt) ||
                                              (TransmitSlice?.TXAnt == ActiveTuner.PortBAnt));

        #endregion

        #region Amplifier Routines

        private void ParseAmplifierStatus(string s)
        {
            if (string.IsNullOrEmpty(s)) return;

            string[] words = s.Split(' ');

            if (words.Length < 2)
            {
                Debug.WriteLine("ParseAmplifierStatus: Too few words -- min 2 (" + words + ")");
                return;
            }

            string handle = words[0];

            // since the TGXL is using the amp API, we are going to catch that fact here for now.
            // At some point, we will likely want to migrate this to an accessory API (or tuner API?)
            // that is more appropriate so we don't have tuners looking like amplifiers in the radio.

            Tuner tuner = FindTunerByHandle(handle);
            // check for whether this is actually a Tuner
            if (tuner != null || s.Contains("model=TunerGeniusXL"))
            {
                ParseTunerStatus(s);
                return;
            }

            Amplifier amp = FindAmplifierByHandle(handle);

            bool add_amp = false;

            // is this an amplifier we already knew about?
            if (amp == null)
            {                
                // no -- is it being removed?
                if (s.Contains("removed"))
                {
                    // yes -- don't bother adding it since we would just remove it
                    // then return since we are done here
                    return;
                }

                // create a new Amplifier -- we will add this to the AmplifierList later
                amp = new Amplifier(this, handle);
                add_amp = true;
            }

            // is the object being removed
            if (s.Contains("removed"))
            {
                // remove it and return
                RemoveAmplifier(amp);
                return;
            }

            amp.StatusUpdate(s.Substring(handle.Length + 1));

            if (add_amp)
                AddAmplifier(amp);
        }

        /// <summary>
        /// Find an Amplifier object by handle (Client ID)
        /// </summary>
        /// <param name="handle">The handle for the Amplifier</param>
        /// <returns>The Amplifier object</returns>
        public Amplifier FindAmplifierByHandle(string handle)
        {
            lock (_amplifiers)
                return _amplifiers.FirstOrDefault(a => a.Handle == handle);
        }

        internal void AddAmplifier(Amplifier amp)
        {
            lock (_amplifiers)
            {
                if (_amplifiers.Contains(amp)) return;
                _amplifiers.Add(amp);
                OnAmplifierAdded(amp);
            }

            UpdateActiveAmplifier();
            RaisePropertyChanged("AmplifierList");
        }

        internal void RemoveAmplifier(Amplifier amp)
        {
            lock (_amplifiers)
            {
                if (!_amplifiers.Contains(amp)) return;
                _amplifiers.Remove(amp);
                OnAmplifierRemoved(amp);
            }

            UpdateActiveAmplifier();
            RaisePropertyChanged("AmplifierList");
        }

        internal void RemoveAllAmplifiers()
        {
            lock (_amplifiers)
            {
                while (_amplifiers.Count > 0)
                {
                    Amplifier amp = _amplifiers[0];
                    _amplifiers.Remove(amp);
                    OnAmplifierRemoved(amp);
                }
            }
        }

        /// <summary>
        /// Delegate event handler for the AmplifierRemoved event
        /// </summary>
        /// <param name="amp"></param>
        public delegate void AmplifierRemovedEventHandler(Amplifier amp);
        /// <summary>
        /// This event is raised when an Amplifier is removed from the radio
        /// </summary>
        public event AmplifierRemovedEventHandler AmplifierRemoved;

        private void OnAmplifierRemoved(Amplifier amp)
        {
            if (AmplifierRemoved != null)
                AmplifierRemoved(amp);
        }        

        /// <summary>
        /// Delegate event handler for the AmplifierAdded event
        /// </summary>
        /// <param name="amp"></param>
        public delegate void AmplifierAddedEventHandler(Amplifier amp);
        /// <summary>
        /// This event is raised when an Amplifier has been added to the radio
        /// </summary>
        public event AmplifierAddedEventHandler AmplifierAdded;

        internal void OnAmplifierAdded(Amplifier amp)
        {
            if (AmplifierAdded != null)
                AmplifierAdded(amp);
        }

        #endregion

        #region Client Routines

        private void ParseClientStatus(string s)
        {
            string[] words = s.Split(' ');

            if (words.Length < 3)
            {
                Debug.WriteLine("ParseStatus: Too few words for client status -- min 4 (" + s + ")");
                return;
            }

            string handle_str = words[0];
            string command = words[1];

            uint handle_uint;
            bool b = StringHelper.TryParseInteger(handle_str, out handle_uint);
            if (!b) return;

            switch (command)
            {
                case "disconnected": // <handle> disconnected forced=<0/1> wan_validation_failed=<0/1> duplicate_client_id=<0/1>
                    {
                        // start from the 3rd word (skip handle and 'disconnected' words)
                        for (int i = 2; i < words.Length; i++)
                        {
                            string kv = words[i];
                            string[] tokens = kv.Split('=');

                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ParseStatus(client disconnect): Invalid key/value pair(" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key)
                            {
                                case "forced":
                                    {

                                        uint temp;
                                        b = uint.TryParse(value, out temp);
                                        if (!b || temp > 1)
                                        {
                                            Debug.WriteLine("Radio::forced: Invalid value (" + kv + ")");
                                            continue;
                                        }

                                        bool forced = Convert.ToBoolean(temp);
                                        if (handle_uint == _clientHandle && forced)
                                        {
                                            OnClientDisconnectReq();
                                        }
                                    }
                                    break;

                                case "wan_validation_failed":
                                    break;
                                case "duplicate_client_id":
                                    {
                                        uint temp;
                                        b = uint.TryParse(value, out temp);
                                        if (!b || temp > 1)
                                        {
                                            Debug.WriteLine("Radio::duplicate_client_id: Invalid value (" + kv + ")");
                                            continue;
                                        }

                                        bool duplicate_client_id = Convert.ToBoolean(temp);
                                        if (handle_uint == _clientHandle && duplicate_client_id)
                                        {
                                            OnDuplicateClientIdDisconnectReq();
                                        }
                                    }
                                    break;
                            }
                        }
 

                        GUIClient gui_client = FindGUIClientByClientHandle(handle_uint);
                        if (gui_client != null)
                            RemoveGUIClient(gui_client);
                    }
                    break;

                case "connected": // <handle> connected client_id=<id> name=<name> station=<station>
                    {
                        if (words.Length < 5)
                        {
                            Debug.WriteLine("ParseStatus: Too few words for client connected status -- min 6 (" + s + ")");
                            return;
                        }

                        string client_id = null;
                        string program = null;
                        string station = null;
                        bool is_local_ptt = false;

                        for (int i = 2; i < words.Length; i++)
                        {
                            string kv = words[i];
                            string[] tokens = kv.Split('=');

                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ParseStatus(client connect): Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key)
                            {
                                case "client_id": client_id = value; break;
                                case "program": program = value; break;
                                case "station": station = value.Replace('\u007f', ' '); break;
                                case "local_ptt":
                                    uint temp;
                                    b = uint.TryParse(value, out temp);
                                    if (!b || temp > 1)
                                    {
                                        Debug.WriteLine("Radio::local_ptt: Invalid value (" + kv + ")");
                                        continue;
                                    }

                                    is_local_ptt = Convert.ToBoolean(temp);
                                    break;

                            }
                        }

                        if (string.IsNullOrEmpty(client_id)) return;

                        GUIClient existingGuiClient;

                        lock (GuiClientsLockObj)
                        {
                            // We must match on client handle here instead of client id because the gui client
                            // list provided by discovery provides the client handle but not the client id
                            existingGuiClient = GuiClients.FirstOrDefault(x => x.ClientHandle == handle_uint);
                            if (existingGuiClient != null)
                            {
                                existingGuiClient.ClientID = client_id;
                                existingGuiClient.Program = program;
                                existingGuiClient.Station = station;
                                existingGuiClient.IsLocalPtt = is_local_ptt;
                            }
                        }

                        if (existingGuiClient != null)
                        {
                            OnGUIClientUpdated(existingGuiClient);
                        }
                        else
                        {
                            // if we make it this far and don't have a GUI Client, we need to add one
                            GUIClient newGuiClient = new GUIClient(handle_uint, client_id, program, station, is_local_ptt);
                            newGuiClient.IsThisClient = handle_uint == _clientHandle;
                            AddGUIClient(newGuiClient);
                        }
                    }
                    break;
            }
        }

        public void BindGUIClient(string client_id)
        {
            // avoid binding a GUI client to another GUI client (!!)
            Debug.Assert(API.IsGUI == false); // make sure this is a non-GUI client

            SendCommand("client bind client_id=" + client_id);
        }

        public GUIClient FindGUIClientByClientID(string client_id)
        {
            lock (GuiClientsLockObj)
                return GuiClients.FirstOrDefault(x => x.ClientID == client_id);
        }

        public GUIClient FindGUIClientByClientHandle(uint client_handle)
        {
            lock (GuiClientsLockObj)
                return GuiClients.FirstOrDefault(x => x.ClientHandle == client_handle);
        }

        private void RemoveAllGUIClients()
        {
            lock (GuiClientsLockObj)
            {
                GuiClients.ToImmutableList().ForEach(x =>
                {   
                    GuiClients.Remove(x);
                    OnGUIClientRemoved(x);
                });
            }

            RaisePropertyChanged(() => GuiClients);
        }

        internal void AddGUIClient(GUIClient gui_client)
        {
            lock (GuiClientsLockObj)
            {
                if (GuiClients.Contains(gui_client)) return;
                GuiClients.Add(gui_client);
            }

            OnGUIClientAdded(gui_client);
            RaisePropertyChanged(() => GuiClients);
        }

        internal void RemoveGUIClient(GUIClient gui_client)
        {
            lock (GuiClientsLockObj)
            {
                if (!GuiClients.Contains(gui_client)) return;
                GuiClients.Remove(gui_client);
            }

            OnGUIClientRemoved(gui_client);
            RaisePropertyChanged(() => GuiClients);
        }

        /// <summary>
        /// Delegate event handler for the GUIClientRemoved event
        /// </summary>
        /// <param name="gui_client">The GUIClient to be removed</param>
        public delegate void GUIClientRemovedEventHandler(GUIClient gui_client);
        /// <summary>
        /// This event is raised when a GUIClient is removed from the radio
        /// </summary>
        public event GUIClientRemovedEventHandler GUIClientRemoved;

        private void OnGUIClientRemoved(GUIClient gui_client)
        {
            if (GUIClientRemoved != null)
                GUIClientRemoved(gui_client);
        }

        /// <summary>
        /// Delegate event handler for the GUIClientAdded event
        /// </summary>
        /// <param name="gui_client">The GUIClient object being added</param>
        public delegate void GUIClientAddedEventHandler(GUIClient gui_client);
        /// <summary>
        /// This event is raised when a new GUIClient has been added to the radio
        /// </summary>
        public event GUIClientAddedEventHandler GUIClientAdded;

        internal void OnGUIClientAdded(GUIClient gui_client)
        {
            if (GUIClientAdded != null)
                GUIClientAdded(gui_client);
        }

        /// <summary>
        /// Delegate event handler for the GUIClientUpdated event
        /// </summary>
        /// <param name="gui_client">The GUIClient object being updated</param>
        public delegate void GUIClientUpdatedEventHandler(GUIClient gui_client);
        /// <summary>
        /// This event is raised when an existing GUIClient has been updated
        /// </summary>
        public event GUIClientUpdatedEventHandler GUIClientUpdated;

        internal void OnGUIClientUpdated(GUIClient gui_client)
        {
            if (GUIClientUpdated != null)
                GUIClientUpdated(gui_client);
        }

        public void SetClientStationName(string station_name)
        {
            SendCommand("client station " + StringHelper.SanitizeInvalidRadioChars(station_name).Replace(' ', '\u007f'));
        }

        // This is called downstream from Discovery to update the GUI Client list based on what is sent via Discovery
        public void UpdateGuiClientsList(List<GUIClient> newGuiClients)
        {
            // Add/Update: Check if a new GUIClient should be added to the existing list, or if an existing GUIClient should be updated
            if (newGuiClients == null)
            {
                return;
            }

            foreach (GUIClient newGuiClient in newGuiClients)
            {
                GUIClient matchingExistingGuiClient;
                lock (GuiClientsLockObj)
                {
                    matchingExistingGuiClient = GuiClients.FirstOrDefault(x => x.ClientHandle == newGuiClient.ClientHandle);
                }

                if (matchingExistingGuiClient == null)
                {
                    // The new GUI client from discovery was not found in the existing list, so add it to the existing list
                    AddGUIClient(newGuiClient);
                }
                else
                {
                    // The new GUI client was found in the existing list.  Update any of the existing fields
                    // (except for local_ptt and client_id) if they are different.
                    if (matchingExistingGuiClient.Program != newGuiClient.Program)
                    {
                        matchingExistingGuiClient.Program = newGuiClient.Program;
                    }

                    if (matchingExistingGuiClient.Station != newGuiClient.Station)
                    {
                        matchingExistingGuiClient.Station = newGuiClient.Station;
                    }

                    if (matchingExistingGuiClient.ClientHandle != newGuiClient.ClientHandle)
                    {
                        matchingExistingGuiClient.ClientHandle = newGuiClient.ClientHandle;
                    }
                }
            }            

            // Remove: Check that all GUIClients in the existing list are also present in the new list-- if any are not in the new list,
            // they must be removed from the existing list
            lock (GuiClientsLockObj)
            {
                for (int i = GuiClients.Count-1; i >= 0; i--)
                {
                    GUIClient existingGuiClient = GuiClients[i];
                    bool existingClientDoesNotExistInNewList = !newGuiClients.Any(x => x.ClientHandle == existingGuiClient.ClientHandle);

                    if(existingClientDoesNotExistInNewList)
                    {
                        GuiClients.Remove(existingGuiClient);
                        OnGUIClientRemoved(existingGuiClient);
                    }                    
                }
            }

            RaisePropertyChanged(() => GuiClients);
        }

        internal void UpdateGuiClientListTXSlices()
        {
            lock(GuiClientsLockObj)
            {
                foreach (GUIClient client in _guiClients)
                {
                    lock (_slices)
                    {
                        Slice this_clients_tx_slice = null;
                        foreach (Slice slc in _slices)
                        {
                            if (slc.ClientHandle == client.ClientHandle && slc.IsTransmitSlice)
                            {
                                this_clients_tx_slice = slc;
                                break;
                            }
                        }

                        client.TransmitSlice = this_clients_tx_slice;
                    }
                }
            }
        }


        #endregion

        #region Log Modules

        private void ParseLogModuleStatus(string s)
        {
            string[] words = s.Split(' ');

            if (words.Length < 1)
            {
                Debug.WriteLine("ParseLogModuleStatus: Too few words -- min 1 (" + words + ")");
                return;
            }

            string[] tokens = words[0].Split('=');

            switch (tokens[0])
            {
                //"available_levels=level1,level2,level3"
                case "available_levels":
                    {
                        //add available levels to the array
                        _logLevels = tokens[1].Split(',');
                        RaisePropertyChanged("LogLevels");
                    }
                    break;

                //"module=<module> level=<level>"
                case "module":
                    {
                        string moduleName = tokens[1];
                        LogModule lm = FindModuleByName(moduleName);

                        bool add_lm = false;

                        // is this an module we already knew about?
                        if (lm == null)
                        {
                            // create a new LogModule - will add it to the list later
                            lm = new LogModule(this);
                            add_lm = true;
                        }

                        //update the logmodule object
                        lm.StatusUpdate(s);

                        //add it to the list if needed
                        if (add_lm)
                            AddLogModule(lm);

                        RaisePropertyChanged("LogModules");
                    }
                    break;
            }
        }

        public LogModule FindModuleByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            lock (_logModules)
                return _logModules.FirstOrDefault(x => x.ModuleName == name);
        }

        public void AddLogModule(LogModule lm)
        {
            lock (_logModules)
            {
                if (_logModules.Contains(lm)) return;
                _logModules.Add(lm);
            }
        }

        #endregion

        public const uint MinimumMajorVersionForMultiFlex = 3;

        private bool _multiFlexEnabled = true;
        public bool MultiFlexEnabled
        {
            get { return _multiFlexEnabled; }
            set
            {
                if (_multiFlexEnabled != value)
                {
                    _multiFlexEnabled = value;
                    SendCommand("radio set mf_enable=" + Convert.ToByte(_multiFlexEnabled));
                    RaisePropertyChanged("MultiFlexEnabled");
                }
            }
        }

        private bool _christmasTreeMode = false;
        public bool ChristmasTreeMode
        {
            get => _christmasTreeMode;
            set
            {
                if (value == _christmasTreeMode) return;
                _christmasTreeMode = value;
                RaisePropertyChanged(nameof(ChristmasTreeMode));
            }
        }

        /// <summary>
        /// Get a reference to the CWX object
        /// </summary>
        /// <returns>CWX object</returns>
        public CWX GetCWX()
        {
            if (_cwx == null)
                _cwx = new CWX(this);

            return _cwx;
        }

        private bool _syncCWX = true;
        public bool SyncCWX
        {
            get { return _syncCWX; }
            set
            {
                if (_syncCWX != value)
                {
                    _syncCWX = value;
                    SendCommand("cw synccwx " + Convert.ToByte(_syncCWX));
                    RaisePropertyChanged("SyncCWX");
                }
            }
        }
        
        public bool GetTXFreq(out double freq_mhz)
        {
            freq_mhz = 0.0;

            lock(_slices)
            {
                // are there any slices?
                if (_slices.Count == 0) return false;

                // for each slice...
                foreach (Slice s in _slices)
                {
                    // is this slice the Transmit slice?
                    if (s.IsTransmitSlice && s.ClientHandle == _clientHandle)
                    {
                        freq_mhz = s.Freq;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Overridden ToString() method for the Radio.cs class
        /// </summary>
        /// <returns>A string description of the radio object in the form of "{IP_Address} {Radio_model}: {Serial_number} {ID_string}"</returns>
        public override string ToString()
        {
            // to enable easy binding to ListBoxes, etc
            return _ip.ToString() + " " + _model + ": " + _serial + " ("+_nickname +" 0x" + _unique_id.ToString("X").PadLeft(8, '0') + ") fpc_mac:"+_frontPanelMacAddress;
        }
        
        #region UDP VITASocket

        public VitaSocket VitaSock = null;
        private Thread _udpProcessingThread = null;
        public int UDPPort
        {
            get
            {
                if (!Connected) return -1;
                if (VitaSock == null) return -2;

                return VitaSock.Port;
            }
        }

        private string _radioLicenseId;
        public string RadioLicenseId
        {
            get => _radioLicenseId;
            set
            {
                if (_radioLicenseId == value) 
                    return;
                
                _radioLicenseId = value;
                RaisePropertyChanged(nameof(RadioLicenseId));
            }
        }

        private string _maxLicensedVersion = "v1"; // may say "All" or "Any" in factory mode
        public string MaxLicensedVersion
        {
            get => _maxLicensedVersion;
            set
            {
                if (_maxLicensedVersion == value) 
                    return;
                
                _maxLicensedVersion = value;
                RaisePropertyChanged(nameof(MaxLicensedVersion));
            }
        }

        private int _licensedClients = 0;
        public int LicensedClients
        {
            get => _licensedClients;
            set
            {
                if (_licensedClients == value) 
                    return;
                
                _licensedClients = value;
                RaisePropertyChanged(nameof(LicensedClients));
            }
        }

        private int _availableClients = 0;
        public int AvailableClients
        {
            get => _availableClients; 
            set
            {
                if (_availableClients == value) 
                    return;
                
                _availableClients = value;
                RaisePropertyChanged(nameof(AvailableClients));
            }
        }

        private int _maxPanadapters = 0;
        public int MaxPanadapters
        {
            get => _maxPanadapters;
            set
            {
                if (_maxPanadapters == value) 
                    return;
                
                _maxPanadapters = value;
                RaisePropertyChanged(nameof(MaxPanadapters));
            }
        }

        private int _availablePanadapters = 0;
        public int AvailablePanadapters
        {
            get => _availablePanadapters;
            set
            {
                if (_availablePanadapters == value) 
                    return;
                
                _availablePanadapters = value;
                RaisePropertyChanged(nameof(AvailablePanadapters));
            }
        }

        private int _availableSlices = 0;
        public int AvailableSlices
        {
            get => _availableSlices;
            set
            {
                if (_availableSlices == value) 
                    return;
                
                _availableSlices = value;
                RaisePropertyChanged(nameof(AvailableSlices));
            }
        }

        private int _maxSlices = 0;
        public int MaxSlices
        {
            get => _maxSlices;
            set
            {
                if (_maxSlices == value) 
                    return;
                
                _maxSlices = value;
                RaisePropertyChanged(nameof(MaxSlices));
            }
        }

        private bool _isInternetConnected;
        public bool IsInternetConnected
        {
            get => _isInternetConnected;
            set
            {
                if (_isInternetConnected == value) 
                    return;
                
                _isInternetConnected = value;
                RaisePropertyChanged(nameof(IsInternetConnected));
            }
        }

        private bool _requiresAdditionalLicense = false;
        public bool RequiresAdditionalLicense
        {
            get => _requiresAdditionalLicense; 
            set
            {
                if (_requiresAdditionalLicense == value) 
                    return;
                
                _requiresAdditionalLicense = value;
                RaisePropertyChanged(nameof(RequiresAdditionalLicense));
            }
        }

        private string _frontPanelMacAddress;
        public string FrontPanelMacAddress
        {
            get => _frontPanelMacAddress;
            internal set
            {
                if (_frontPanelMacAddress == value) 
                    return;
                
                _frontPanelMacAddress = value;
                RaisePropertyChanged(nameof(FrontPanelMacAddress));
            }
        }

        public class UDPVitaPacket
        {
            public IPEndPoint Ep { get; set; }
            public byte[] Data { get; set; }
            public int Bytes { get; set; }


            public UDPVitaPacket(IPEndPoint ep, byte[] data, int bytes)
            {
                Ep = ep;
                Data = data;
                Bytes = bytes;
            }
        }

        private void ProcessVitaPacket(VitaPacketPreamble vita_preamble, byte[] data, int bytes)
        {
            try
            {
                switch (vita_preamble.header.pkt_type)
                {
                    case VitaPacketType.ExtDataWithStream:
                        switch (vita_preamble.class_id.PacketClassCode)
                        {
                            case VitaFlex.SL_VITA_FFT_CLASS:
                                _countFFT += bytes + UDP_HEADER_SIZE;
                                ProcessFFTDataPacket(new VitaFFTPacket(data));
                                break;
                            case VitaFlex.SL_VITA_OPUS_CLASS:   // Opus Encoded Audio
                                _countRXOpus += bytes + UDP_HEADER_SIZE;
                                ProcessOpusDataPacket(new VitaOpusDataPacket(data, bytes));
                                break;
                            case VitaFlex.SL_VITA_IF_NARROW_CLASS: // DAX Audio and uncompressed Remote RX Audio
                            case VitaFlex.SL_VITA_IF_NARROW_REDUCED_BW_CLASS: // DAX Audio Reduced BW
                                _countDAX += bytes + UDP_HEADER_SIZE;
                                ProcessIFDataPacket(new VitaIFDataPacket(data, bytes));
                                break;
                            case VitaFlex.SL_VITA_METER_CLASS:
                                _countMeter += bytes + UDP_HEADER_SIZE;
                                ProcessMeterDataPacket(new VitaMeterPacket(data));
                                break;
                            case VitaFlex.SL_VITA_WATERFALL_CLASS:
                                _countWaterfall += bytes + UDP_HEADER_SIZE;
                                ProcessWaterfallDataPacket(new VitaWaterfallPacket(data));
                                break;
                            default:
                                //Debug.WriteLine("Unprocessed UDP packet");
                                break;
                        }
                        break;

                    case VitaPacketType.IFDataWithStream:
                        switch (vita_preamble.class_id.PacketClassCode)
                        {
                            case VitaFlex.SL_VITA_IF_WIDE_CLASS_24kHz: // DAX IQ
                            case VitaFlex.SL_VITA_IF_WIDE_CLASS_48kHz:
                            case VitaFlex.SL_VITA_IF_WIDE_CLASS_96kHz:
                            case VitaFlex.SL_VITA_IF_WIDE_CLASS_192kHz:
                                ProcessIFDataPacket(new VitaIFDataPacket(data, bytes));
                                break;
                        }
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message + "\n\n" + ex.StackTrace);
            }
        }

        public ConcurrentQueue<UDPVitaPacket> UDPCallbackQueue = new ConcurrentQueue<UDPVitaPacket>();
        private AutoResetEvent _semNewUDPPacket = new AutoResetEvent(false);
        private void ProcessUDPPackets_ThreadFunction()
        {
            while ( Connected )
            {
                UDPVitaPacket packet;
                bool try_dequeue_result = false;

                _semNewUDPPacket.WaitOne();
                while (try_dequeue_result = UDPCallbackQueue.TryDequeue(out packet))
                {
                    // ensure that the packet is at least long enough to inspect for VITA info
                    if (packet.Data.Length < 16)
                        continue;

                    VitaPacketPreamble vita_preamble = new VitaPacketPreamble(packet.Data);

                    // ensure the packet has our OUI in it -- looks like it came from us
                    if (vita_preamble.class_id.OUI != VitaFlex.FLEX_OUI)
                        continue;

                    _udpSuccessfulRegistration = true;

                    ProcessVitaPacket(vita_preamble, packet.Data, packet.Bytes);
                }
            }

            // Clear out the queue
            UDPVitaPacket dummy;
            while (UDPCallbackQueue.TryDequeue(out dummy))
            {

            }

            VitaSock.CloseSocket();
        }

        private void UDPDataReceivedCallback(IPEndPoint ep, byte[] data, int bytes)
        {
            // if we aren't connected, we shouldn't build up a queue of unprocessed UDP data
            if (!_connected) return;

            // Keep this callback short so we that we don't hold the network thread and so that
            // we can ensure that we are keeping packets the order that they arrive over the network

            UDPCallbackQueue.Enqueue(new UDPVitaPacket(ep, data, bytes));
            _semNewUDPPacket.Set();
        }

        private void StopUDP()
        {
            _semNewUDPPacket.Set();

            // Wait for thread to finish
            if(_udpProcessingThread != null)
                _udpProcessingThread.Join();

            _udpSuccessfulRegistration = false;
        }

        private bool _udpSuccessfulRegistration = false;
        private void RegisterUDP()
        {
            try
            {
                while (VitaSock != null && !_udpSuccessfulRegistration && Connected)
                {

                    Byte[] sendBytes = Encoding.ASCII.GetBytes("client udp_register handle=0x" + ClientHandle.ToString("X"));
                    VitaSock.SendUDP(sendBytes);

                    Thread.Sleep(50);
                }

                PersistenceLoaded = true;

                while ( VitaSock != null && Connected )
                {
                    /* We must maintain the NAT rule in the local router
                     * so we have to send traffic every once in a while 
                     */
                    Byte[] sendBytes = Encoding.ASCII.GetBytes("client ping handle=0x" + ClientHandle.ToString("X"));
                    VitaSock.SendUDP(sendBytes);
                    Thread.Sleep(5000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception: " + ex.ToString());
            }
        }

        private void StartUDP( )
        {
            if (IsWan)
            {
                if (RequiresHolePunch)
                {
                    VitaSock = new VitaSocket(NegotiatedHolePunchPort, UDPDataReceivedCallback, _ip, NegotiatedHolePunchPort);
                }
                else
                {
                    VitaSock = new VitaSocket(4991, UDPDataReceivedCallback, IP, PublicUdpPort);
                }

                Task.Factory.StartNew(() => RegisterUDP(), TaskCreationOptions.LongRunning);

            }
            else
            {
                VitaSock = new VitaSocket(4991, UDPDataReceivedCallback, IP, 4991);
            }

            Thread t = new Thread(new ThreadStart(ProcessUDPPackets_ThreadFunction));
            t.Name = "UDP Packet Processing Thread";
            t.IsBackground = true;
            t.Priority = ThreadPriority.Normal;
            t.Start();

            _udpProcessingThread = t;
        }

        #endregion
    }
}
