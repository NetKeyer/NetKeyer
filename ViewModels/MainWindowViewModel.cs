using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.WebSockets;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flex.Smoothlake.FlexLib;
using NetKeyer.Audio;
using NetKeyer.Helpers;
using NetKeyer.Keying;
using NetKeyer.Midi;
using NetKeyer.Models;
using NetKeyer.Services;
using NetKeyer.Services.Remote;
using NetKeyer.Services.Rendezvous;
using NetKeyer.SmartLink;
using PortAudioSharp;

namespace NetKeyer.ViewModels;

public enum InputDeviceType
{
    Serial,
    MIDI
}

public enum PageType
{
    Setup,
    Operating
}

public class RadioClientSelection
{
    public Radio Radio { get; set; }
    public GUIClient GuiClient { get; set; }
    public string DisplayName { get; set; }

    public override string ToString() => DisplayName;
}

public class RemoteHostClientDisplayRow
{
    public string RemoteIp { get; set; } = string.Empty;
    public string Callsign { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string LastActive { get; set; } = string.Empty;

    public IBrush StatusBrush
    {
        get
        {
            return string.Equals(Status, "Connected", StringComparison.OrdinalIgnoreCase)
                ? Brushes.Green
                : Brushes.Black;
        }
    }
}

public partial class MainWindowViewModel : ViewModelBase
{
    // On macOS, we use the native menu bar, so hide the in-window menu
    public bool IsMenuBarInWindow => !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetupPage), nameof(IsOperatingPage))]
    private PageType _currentPage = PageType.Setup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSerialInput), nameof(IsMidiInput))]
    private InputDeviceType _inputType = InputDeviceType.Serial;

    public bool IsSetupPage => CurrentPage == PageType.Setup;
    public bool IsOperatingPage => CurrentPage == PageType.Operating;

    public bool IsSerialInput
    {
        get => InputType == InputDeviceType.Serial;
        set { if (value) InputType = InputDeviceType.Serial; }
    }

    public bool IsMidiInput
    {
        get => InputType == InputDeviceType.MIDI;
        set { if (value) InputType = InputDeviceType.MIDI; }
    }

    [ObservableProperty]
    private ObservableCollection<RadioClientSelection> _radioClientSelections = new();

    [ObservableProperty]
    private RadioClientSelection _selectedRadioClient;

    [ObservableProperty]
    private ObservableCollection<string> _serialPorts = new();

    [ObservableProperty]
    private string _selectedSerialPort;

    [ObservableProperty]
    private ObservableCollection<string> _midiDevices = new();

    [ObservableProperty]
    private string _selectedMidiDevice;

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _audioDevices = new();

    [ObservableProperty]
    private AudioDeviceInfo _selectedAudioDevice;

    [ObservableProperty]
    private string _radioStatus = "";

    [ObservableProperty]
    private IBrush _radioStatusColor = Brushes.Red;

    [ObservableProperty]
    private bool _hasRadioError = false;

    [ObservableProperty]
    private string _connectButtonText = "Connect";

    [ObservableProperty]
    private int _cwSpeed = 20;

    [ObservableProperty]
    private int _sidetoneVolume = 50;

    [ObservableProperty]
    private int _cwPitch = 600;

    [ObservableProperty]
    private bool _isIambicMode = true;

    [ObservableProperty]
    private bool _isIambicModeB = true; // true = Mode B, false = Mode A

    [ObservableProperty]
    private bool _swapPaddles = false;

    [ObservableProperty]
    private IBrush _leftPaddleIndicatorColor = Brushes.Black;

    [ObservableProperty]
    private IBrush _rightPaddleIndicatorColor = Brushes.Black;

    [ObservableProperty]
    private string _leftPaddleStateText = "OFF";

    [ObservableProperty]
    private string _rightPaddleStateText = "OFF";

    private Radio _connectedRadio;
    private uint _boundGuiClientHandle = 0;
    private UserSettings _settings;
    private bool _loadingSettings = false; // Prevent saving while loading
    private bool _isSidetoneOnlyMode = false; // Track if we're in sidetone-only mode (no radio)
    private bool _userExplicitlySelectedSidetoneOnly = false; // Track if user explicitly selected sidetone-only vs. implicit fallback
    private RadioClientSelection _currentUserSelection = null; // Track user's explicit dropdown choice (ephemeral, not persisted)

    // Sidetone generator
    private ISidetoneGenerator _sidetoneGenerator;

    // Keep-awake stream (plays near-silent audio to prevent device from sleeping)
    private IKeepAwakeStream _keepAwakeStream;

    // SmartLink support
    private SmartLinkManager _smartLinkManager;

    // Transmit slice monitoring
    private TransmitSliceMonitor _transmitSliceMonitor;

    // Radio settings synchronization
    private RadioSettingsSynchronizer _radioSettingsSynchronizer;

    // Input device management
    private InputDeviceManager _inputDeviceManager;

    // Keying controller
    private KeyingController _keyingController;

    // Remote connectivity
    private IRemoteClientService _remoteClientService;
    private IRemoteHostService _remoteHostService;
    private IHostPortMapper _hostPortMapper;
    private IRendezvousControlService _rendezvousControlService;
    private RendezvousHostRegistrationSession _rendezvousHostSession;
    private RendezvousClientConnectionSession _rendezvousClientSession;
    private CancellationTokenSource _remoteCts;
    private readonly HashSet<string> _relayHostSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _relayHostSessionsLock = new();
    private readonly bool _forceRelayTransportForExperiments = IsTruthyEnvironmentValue(Environment.GetEnvironmentVariable("NETKEYER_FORCE_RELAY_TRANSPORT"));
    private readonly bool _debugInsecureOverridesEnabled;
    private readonly bool _enableSecureRemoteTransport;
    private readonly bool _requireSecureRemoteTransport;
    private readonly bool _validateRelayCiphertext;
    private string _rendezvousAccessToken = string.Empty;
    private bool _rendezvousUseLocalJwtMinting;
    private string _rendezvousJwtKeyId = string.Empty;
    private string _rendezvousJwtKeySecret = string.Empty;
    private string _rendezvousJwtIssuer = string.Empty;
    private string _rendezvousJwtAudience = string.Empty;
    private int _rendezvousJwtTokenLifetimeMinutes = 30;
    private bool _isSyncingRendezvousEndpoint;
    private bool _isExiting;
    private bool _latencyProbeKeyClosed;
    private bool _remoteHostTransmitModeCW = true;
    private string _remoteHostTransmitMode = "CW";
    private bool? _hostSidetoneEnabledForLocalSource;
    private const int DefaultRendezvousPort = 49920;

    public bool IsExiting => _isExiting;

    [ObservableProperty]
    private bool _smartLinkAvailable = false;

    [ObservableProperty]
    private bool _smartLinkAuthenticated = false;

    [ObservableProperty]
    private string _smartLinkStatus = "Not connected";

    [ObservableProperty]
    private string _smartLinkButtonText = "Login to SmartLink";

    // Mode differentiation properties
    [ObservableProperty]
    private string _connectedRadioDisplay = "";  // Shows connected radio name

    [ObservableProperty]
    private string _modeDisplay = "Disconnected";  // Combined mode string

    [ObservableProperty]
    private string _modeInstructions = "";  // Instructions for mode switching

    [ObservableProperty]
    private bool _cwSettingsVisible = true;  // Control CW settings visibility

    [ObservableProperty]
    private string _leftPaddleLabelText = "Left Paddle";  // Dynamic left label

    [ObservableProperty]
    private bool _rightPaddleVisible = true;  // Hide right paddle when appropriate

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoteModeOff), nameof(IsRemoteModeClient), nameof(IsRemoteModeHost), nameof(IsWaitingForClientConnection), nameof(RemoteStatusBarText), nameof(RemoteClientTitleStatusText), nameof(RemoteHostTitleStatusText), nameof(RemoteHostConnectionStateText), nameof(RemoteHostWaitingLineText), nameof(RemoteHostRendezvousStatusText), nameof(RemoteHostRendezvousStatusBrush))]
    private RemoteConnectionMode _remoteMode = RemoteConnectionMode.Off;

    public bool IsRemoteModeOff
    {
        get => RemoteMode == RemoteConnectionMode.Off;
        set { if (value) RemoteMode = RemoteConnectionMode.Off; }
    }

    public bool IsRemoteModeClient
    {
        get => RemoteMode == RemoteConnectionMode.Client;
        set { if (value) RemoteMode = RemoteConnectionMode.Client; }
    }

    public bool IsRemoteModeHost
    {
        get => RemoteMode == RemoteConnectionMode.Host;
        set { if (value) RemoteMode = RemoteConnectionMode.Host; }
    }

    [ObservableProperty]
    private string _remoteCallsign = "";

    [ObservableProperty]
    private string _remoteClientHost = "127.0.0.1";

    [ObservableProperty]
    private int _remoteClientPort = RemoteDefaults.DefaultPort;

    [ObservableProperty]
    private string _remoteHostName = Environment.MachineName;

    [ObservableProperty]
    private string _remoteHostBindAddress = "0.0.0.0";

    [ObservableProperty]
    private int _remoteHostPort = RemoteDefaults.DefaultPort;

    [ObservableProperty]
    private string _remoteSharedToken = "";

    [ObservableProperty]
    private int _remoteMaxClients = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoteHostRendezvousStatusText), nameof(RemoteHostRendezvousStatusBrush), nameof(RemoteHostWaitingLineText))]
    private bool _remoteUseRendezvous = false;

    [ObservableProperty]
    private string _remoteRendezvousServer = "";

    [ObservableProperty]
    private int _remoteRendezvousPort = DefaultRendezvousPort;

    [ObservableProperty]
    private string _remoteRendezvousServerUrl = "";

    [ObservableProperty]
    private string _remoteRendezvousHostId = "";

    [ObservableProperty]
    private ObservableCollection<RendezvousHostSummary> _rendezvousDiscoveredHosts = new();

    [ObservableProperty]
    private RendezvousHostSummary _selectedRendezvousHost;

    [ObservableProperty]
    private decimal _remoteClientHoldSeconds = 1.0m;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoteClientConnectionStatusText), nameof(RemoteClientConnectionStatusBrush), nameof(RemoteStatusBarText), nameof(RemoteClientTitleStatusText), nameof(RemoteHostTitleStatusText))]
    private string _remoteStatus = "Remote mode off";

    [ObservableProperty]
    private IBrush _rendezvousAuthTestButtonBrush = Brushes.Black;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingForClientConnection), nameof(RemoteHostConnectionStateText), nameof(RemoteHostWaitingLineText))]
    private int _remoteConnectedClients = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoteHostHasClients))]
    private ObservableCollection<RemoteClientStatusInfo> _remoteHostClientStatuses = new();

    [ObservableProperty]
    private ObservableCollection<RemoteHostClientDisplayRow> _remoteHostClientDisplayRows = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoteConnectedHostIpDisplay))]
    private string _remoteConnectedHostIp = "";

    [ObservableProperty]
    private string _remoteConnectedHostName = "";

    [ObservableProperty]
    private string _remoteTelemetryLabel = "Telemetry (host):";

    [ObservableProperty]
    private string _remoteTelemetryLine1 = "last --.- ms | p50 --.- ms | p95 --.- ms";

    [ObservableProperty]
    private string _remoteTelemetryLine2 = "max --.- ms | accepted 60s 0 | stale 0";

    public string RemoteConnectedHostIpDisplay
    {
        get
        {
            return NormalizeIpForDisplay(RemoteConnectedHostIp);
        }
    }

    private static string NormalizeIpForDisplay(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return string.Empty;
        }

        if (IPAddress.TryParse(ip, out var parsed))
        {
            if (parsed.IsIPv4MappedToIPv6)
            {
                return parsed.MapToIPv4().ToString();
            }

            return parsed.ToString();
        }

        const string ipv4MappedPrefix = "::ffff:";
        if (ip.StartsWith(ipv4MappedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ip.Substring(ipv4MappedPrefix.Length);
        }

        return ip;
    }

    public string RemoteClientConnectionStatusText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RemoteStatus))
            {
                return string.Empty;
            }

            if (RemoteStatus.StartsWith("Connected", StringComparison.OrdinalIgnoreCase))
            {
                return "Connected";
            }

            if (RemoteStatus.StartsWith("Connection lost", StringComparison.OrdinalIgnoreCase)
                || RemoteStatus.StartsWith("Disconnected", StringComparison.OrdinalIgnoreCase)
                || RemoteStatus.StartsWith("Host error", StringComparison.OrdinalIgnoreCase))
            {
                return "Connection lost";
            }

            return RemoteStatus;
        }
    }

    public string RemoteStatusBarText
    {
        get
        {
            // Client/host/standalone status now appears in the respective status panel title rows.
            return string.Empty;
        }
    }

    public string RemoteClientTitleStatusText
    {
        get
        {
            return IsRemoteModeClient
                ? (RemoteStatus ?? string.Empty)
                : string.Empty;
        }
    }

    public IBrush RemoteClientConnectionStatusBrush
    {
        get
        {
            return RemoteClientConnectionStatusText.Equals("Connected", StringComparison.OrdinalIgnoreCase)
                ? Brushes.Green
                : Brushes.Gray;
        }
    }

    public bool RemoteHostHasClients => RemoteHostClientStatuses.Count > 0;
    public bool IsWaitingForClientConnection => IsRemoteModeHost && RemoteConnectedClients == 0;

    public string RemoteHostRendezvousStatusText
    {
        get
        {
            if (!IsRemoteModeHost)
            {
                return string.Empty;
            }

            if (!RemoteUseRendezvous)
            {
                return "Rendezvous: Off";
            }

            return _rendezvousHostSession != null
                ? "Rendezvous: Connected"
                : "Rendezvous: Not connected";
        }
    }

    public string RemoteHostTitleStatusText
    {
        get
        {
            if (!IsRemoteModeHost)
            {
                return string.Empty;
            }

            return TryGetListeningPrefix(RemoteStatus, out string listeningPrefix)
                ? listeningPrefix
                : (RemoteStatus ?? string.Empty);
        }
    }

    public IBrush RemoteHostRendezvousStatusBrush
    {
        get
        {
            return _rendezvousHostSession != null
                ? Brushes.Green
                : Brushes.Gray;
        }
    }

    public string RemoteHostConnectionStateText
    {
        get
        {
            return IsWaitingForClientConnection
                ? "Waiting for client connection"
                : $"Connected clients: {RemoteConnectedClients}";
        }
    }

    private static bool TryGetListeningPrefix(string status, out string listeningPrefix)
    {
        listeningPrefix = string.Empty;

        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        const string prefix = "Listening on port ";
        if (!status.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int separatorIndex = status.IndexOf('.');
        listeningPrefix = separatorIndex > 0
            ? status.Substring(0, separatorIndex)
            : status;

        return true;
    }

    public string RemoteHostWaitingLineText
    {
        get
        {
            string hostState = RemoteHostConnectionStateText;

            string rendezvousState = RemoteHostRendezvousStatusText;
            if (string.IsNullOrWhiteSpace(rendezvousState))
            {
                return hostState;
            }

            return $"{hostState} | {rendezvousState}";
        }
    }

    public MainWindowViewModel()
    {
        string debugInsecureGateRaw = Environment.GetEnvironmentVariable("NETKEYER_DEBUG_ALLOW_INSECURE_OVERRIDES");
        string enableSecureRaw = Environment.GetEnvironmentVariable("NETKEYER_ENABLE_SECURE_REMOTE_TRANSPORT");
        string requireSecureRaw = Environment.GetEnvironmentVariable("NETKEYER_REQUIRE_SECURE_REMOTE_TRANSPORT");
        string validateCiphertextRaw = Environment.GetEnvironmentVariable("NETKEYER_VALIDATE_RELAY_CIPHERTEXT");

        _debugInsecureOverridesEnabled = IsDebugInsecureOverridesEnabled(debugInsecureGateRaw);
        _enableSecureRemoteTransport = IsSecureDefaultEnabled(enableSecureRaw, _debugInsecureOverridesEnabled);
        _requireSecureRemoteTransport = IsSecureDefaultEnabled(requireSecureRaw, _debugInsecureOverridesEnabled);
        _validateRelayCiphertext = IsSecureDefaultEnabled(validateCiphertextRaw, _debugInsecureOverridesEnabled);

        DebugLogger.LogAlways("system", "NetKeyer startup: debug log initialized");

        if (!_debugInsecureOverridesEnabled)
        {
            if (IsFalseyEnvironmentValue(enableSecureRaw))
            {
                DebugLogger.LogAlways("security", "Ignored NETKEYER_ENABLE_SECURE_REMOTE_TRANSPORT insecure override because debug override gate is disabled.");
            }

            if (IsFalseyEnvironmentValue(requireSecureRaw))
            {
                DebugLogger.LogAlways("security", "Ignored NETKEYER_REQUIRE_SECURE_REMOTE_TRANSPORT insecure override because debug override gate is disabled.");
            }

            if (IsFalseyEnvironmentValue(validateCiphertextRaw))
            {
                DebugLogger.LogAlways("security", "Ignored NETKEYER_VALIDATE_RELAY_CIPHERTEXT insecure override because debug override gate is disabled.");
            }
        }

        if (!IsDebugBuild && IsTruthyEnvironmentValue(debugInsecureGateRaw))
        {
            DebugLogger.LogAlways("security", "NETKEYER_DEBUG_ALLOW_INSECURE_OVERRIDES is ignored in non-debug builds.");
        }

        DebugLogger.LogAlways(
            "security",
            $"Remote security profile: enable_secure_transport={_enableSecureRemoteTransport}, require_secure_transport={_requireSecureRemoteTransport}, validate_ciphertext={_validateRelayCiphertext}, debug_insecure_overrides={_debugInsecureOverridesEnabled}");

        // Load user settings
        _settings = UserSettings.Load();

        // Initialize SmartLink support
        _smartLinkManager = new SmartLinkManager(_settings);
        _smartLinkManager.StatusChanged += SmartLinkManager_StatusChanged;
        _smartLinkManager.WanRadiosDiscovered += SmartLinkManager_WanRadiosDiscovered;
        _smartLinkManager.RegistrationInvalid += SmartLinkManager_RegistrationInvalid;
        _smartLinkManager.WanRadioConnectReady += SmartLinkManager_WanRadioConnectReady;

        SmartLinkAvailable = _smartLinkManager.IsAvailable;

        // Try to restore SmartLink session from saved refresh token
        if (_smartLinkManager.IsAvailable)
        {
            Task.Run(async () => await _smartLinkManager.TryRestoreSessionAsync());
        }

        // Initialize FlexLib API
        API.ProgramName = "NetKeyer";
        API.RadioAdded += OnRadioAdded;
        API.RadioRemoved += OnRadioRemoved;
        API.Init();

        // Initialize input device manager (must be done before RefreshSerialPorts/RefreshMidiDevices)
        _inputDeviceManager = new InputDeviceManager();
        _inputDeviceManager.PaddleStateChanged += InputDeviceManager_PaddleStateChanged;

        // Apply saved input type
        _loadingSettings = true;
        if (_settings.InputType == "MIDI")
        {
            InputType = InputDeviceType.MIDI;
        }

        RemoteMode = _settings.RemoteMode;
        RemoteCallsign = _settings.RemoteCallsign ?? "";
        RemoteClientHost = string.IsNullOrWhiteSpace(_settings.RemoteClientTargetHost)
            ? "127.0.0.1"
            : _settings.RemoteClientTargetHost;
        RemoteClientPort = _settings.RemoteClientTargetPort > 0 ? _settings.RemoteClientTargetPort : RemoteDefaults.DefaultPort;
        RemoteHostName = string.IsNullOrWhiteSpace(_settings.RemoteHostName)
            ? Environment.MachineName
            : _settings.RemoteHostName;
        RemoteHostBindAddress = string.IsNullOrWhiteSpace(_settings.RemoteHostBindAddress)
            ? "0.0.0.0"
            : _settings.RemoteHostBindAddress;
        RemoteHostPort = _settings.RemoteHostListenPort > 0 ? _settings.RemoteHostListenPort : RemoteDefaults.DefaultPort;
        RemoteSharedToken = _settings.RemoteSharedToken ?? "";
        RemoteMaxClients = _settings.RemoteHostMaxClients > 0 ? _settings.RemoteHostMaxClients : 3;
        RemoteUseRendezvous = _settings.RemoteUseRendezvous;
        ParseRendezvousEndpoint(_settings.RemoteRendezvousServerUrl ?? "", out string rendezvousServer, out int rendezvousPort);
        RemoteRendezvousServer = rendezvousServer;
        RemoteRendezvousPort = rendezvousPort;
        RemoteRendezvousServerUrl = BuildRendezvousServerUrl();
        if (string.IsNullOrWhiteSpace(_settings.RemoteHostId) && RemoteMode == RemoteConnectionMode.Host)
        {
            _settings.RemoteHostId = _settings.RemoteRendezvousHostId ?? "";
            _settings.Save();
        }
        ApplyDisplayedRemoteHostIdForMode(RemoteMode);
        _rendezvousAccessToken = _settings.RendezvousAccessToken ?? string.Empty;
        _rendezvousUseLocalJwtMinting = _settings.RendezvousUseLocalJwtMinting;
        _rendezvousJwtKeyId = _settings.RendezvousJwtKeyId ?? string.Empty;
        _rendezvousJwtKeySecret = _settings.RendezvousJwtKeySecret ?? string.Empty;
        _rendezvousJwtIssuer = _settings.RendezvousJwtIssuer ?? string.Empty;
        _rendezvousJwtAudience = _settings.RendezvousJwtAudience ?? string.Empty;
        _rendezvousJwtTokenLifetimeMinutes = _settings.RendezvousJwtTokenLifetimeMinutes > 0
            ? _settings.RendezvousJwtTokenLifetimeMinutes
            : 30;
        ApplyConfiguredRendezvousAccessTokenEnvironment();
        RemoteClientHoldSeconds = ConvertHoldMsToSeconds(_settings.RemoteHostClientHoldMs);

        if (RemoteMode == RemoteConnectionMode.Client)
        {
            CwSpeed = _settings.RemoteClientCwSpeed > 0 ? _settings.RemoteClientCwSpeed : 20;
            SidetoneVolume = Math.Max(0, Math.Min(100, _settings.RemoteClientSidetoneVolume));
            CwPitch = _settings.RemoteClientCwPitch > 0 ? _settings.RemoteClientCwPitch : 600;
        }

        SidetoneLatencyProbe.Configure(_settings.EnableDetailedTimingAnalysisLogging);

        if (_settings.ForceEnableKeepAudioDeviceAwakeAtStartup)
        {
            if (!_settings.KeepAudioDeviceAwake)
            {
                _settings.KeepAudioDeviceAwake = true;
                DebugLogger.LogAlways("audio", "[LatencyTest] KeepAudioDeviceAwake was disabled; force-enabled by startup setting.");
            }

            _settings.ForceEnableKeepAudioDeviceAwakeAtStartup = false;
            _settings.Save();
            DebugLogger.LogAlways("audio", "[LatencyTest] ForceEnableKeepAudioDeviceAwakeAtStartup applied and cleared.");
        }

        _loadingSettings = false;

        // Initial discovery
        RefreshRadios();
        RefreshSerialPorts();
        RefreshMidiDevices();

        // Initialize sidetone generator first with default device
        // This initializes PortAudio (on non-Windows) which is needed for device enumeration
        try
        {
            bool aggressiveLowLatency = _settings.WasapiAggressiveLowLatency;
            _sidetoneGenerator = SidetoneGeneratorFactory.Create(null, aggressiveLowLatency);
            _sidetoneGenerator.SetFrequency(CwPitch);
            _sidetoneGenerator.SetVolume(SidetoneVolume);
            _sidetoneGenerator.SetWpm(CwSpeed);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not initialize sidetone generator: {ex.Message}");
        }

        // Now enumerate audio devices (requires PortAudio to be initialized on non-Windows)
        RefreshAudioDevices();

        // If a non-default device was selected from settings, reinitialize with that device
        if (SelectedAudioDevice != null && !string.IsNullOrEmpty(SelectedAudioDevice.DeviceId))
        {
            ReinitializeSidetoneGenerator();
        }

        // Initialize keep-awake stream if enabled
        if (_settings.KeepAudioDeviceAwake)
        {
            try
            {
                string deviceId = SelectedAudioDevice?.DeviceId ?? "";
                _keepAwakeStream = KeepAwakeStreamFactory.Create(deviceId);
                _keepAwakeStream.Start();
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Warning: Could not initialize keep-awake stream: {ex.Message}");
            }
        }

        // Initialize keying controller
        _keyingController = new KeyingController(_sidetoneGenerator);
        _keyingController.Initialize(
            _boundGuiClientHandle,
            GetTimestamp,
            (state, timestamp, handle) =>
            {
                if (_connectedRadio != null)
                    _connectedRadio.CWKey(state, timestamp, handle);
            }
        );
        _keyingController.SetKeyingMode(IsIambicMode, IsIambicModeB);
        _keyingController.SetSpeed(CwSpeed);
        _keyingController.SetSidetoneVolume(SidetoneVolume);

        _remoteClientService = new RemoteClientService();
        _remoteClientService.ConnectionStatusChanged += RemoteClientService_ConnectionStatusChanged;
        _remoteClientService.HostIdentityChanged += RemoteClientService_HostIdentityChanged;
        _remoteClientService.HostTelemetryChanged += RemoteClientService_HostTelemetryChanged;

        _remoteHostService = new RemoteHostService();
        _remoteHostService.HostStatusChanged += RemoteHostService_HostStatusChanged;
        _remoteHostService.ConnectedClientCountChanged += RemoteHostService_ConnectedClientCountChanged;
        _remoteHostService.ClientStatusesChanged += RemoteHostService_ClientStatusesChanged;
        _remoteHostService.PaddleStateReceived += RemoteHostService_PaddleStateReceived;

        _hostPortMapper = new HostPortMapper();

        _rendezvousControlService = new RendezvousControlService();

        UpdateRemoteHostClientDisplayRows(Array.Empty<RemoteClientStatusInfo>());

        // Initialize transmit slice monitor
        _transmitSliceMonitor = new TransmitSliceMonitor();
        _transmitSliceMonitor.TransmitModeChanged += TransmitSliceMonitor_ModeChanged;

        // Initialize radio settings synchronizer
        _radioSettingsSynchronizer = new RadioSettingsSynchronizer();
        _radioSettingsSynchronizer.SettingChangedFromRadio += RadioSettingsSynchronizer_SettingChanged;
    }

    partial void OnCurrentPageChanged(PageType value)
    {
        // When returning to setup page, restore saved selections
        if (value == PageType.Setup && _settings != null)
        {
            // Refresh device lists to restore selections
            RefreshRadios();
            RefreshSerialPorts();
            RefreshMidiDevices();
            RefreshAudioDevices();
        }
    }

    partial void OnInputTypeChanged(InputDeviceType value)
    {
        if (!_loadingSettings && _settings != null)
        {
            _settings.InputType = value == InputDeviceType.MIDI ? "MIDI" : "Serial";
            _settings.Save();
        }
    }

    partial void OnRemoteModeChanged(RemoteConnectionMode value)
    {
        if (!_loadingSettings && _settings != null)
        {
            _settings.RemoteMode = value;
            _settings.Save();
        }

        ApplyDisplayedRemoteHostIdForMode(value);

        if (value == RemoteConnectionMode.Client && _settings != null)
        {
            CwSpeed = _settings.RemoteClientCwSpeed > 0 ? _settings.RemoteClientCwSpeed : CwSpeed;
            SidetoneVolume = Math.Max(0, Math.Min(100, _settings.RemoteClientSidetoneVolume));
            CwPitch = _settings.RemoteClientCwPitch > 0 ? _settings.RemoteClientCwPitch : CwPitch;
        }

        if (value == RemoteConnectionMode.Off)
        {
            RemoteStatus = "Remote mode off";
        }
        else if (value == RemoteConnectionMode.Client)
        {
            RemoteStatus = $"Client configured for {RemoteClientHost}:{RemoteClientPort}";
        }
        else
        {
            RemoteStatus = $"Host configured on {RemoteHostBindAddress}:{RemoteHostPort}";
        }
    }

    partial void OnRemoteCallsignChanged(string value)
    {
        if (!_loadingSettings && _settings != null)
        {
            _settings.RemoteCallsign = value ?? "";
            _settings.Save();
        }
    }

    partial void OnRemoteClientHostChanged(string value)
    {
        if (!_loadingSettings && _settings != null)
        {
            _settings.RemoteClientTargetHost = value;
            _settings.Save();
        }
    }

    partial void OnRemoteClientPortChanged(int value)
    {
        if (!_loadingSettings && _settings != null && value > 0)
        {
            _settings.RemoteClientTargetPort = value;
            _settings.Save();
        }
    }

    partial void OnRemoteHostBindAddressChanged(string value)
    {
        if (!_loadingSettings && _settings != null)
        {
            _settings.RemoteHostBindAddress = value;
            _settings.Save();
        }
    }

    partial void OnRemoteHostNameChanged(string value)
    {
        if (!_loadingSettings && _settings != null)
        {
            _settings.RemoteHostName = value ?? "";
            _settings.Save();
        }
    }

    partial void OnRemoteHostPortChanged(int value)
    {
        if (!_loadingSettings && _settings != null && value > 0)
        {
            _settings.RemoteHostListenPort = value;
            _settings.Save();
        }
    }

    partial void OnRemoteSharedTokenChanged(string value)
    {
        if (!_loadingSettings && _settings != null)
        {
            _settings.RemoteSharedToken = value;
            _settings.Save();
        }
    }

    partial void OnRemoteMaxClientsChanged(int value)
    {
        if (!_loadingSettings && _settings != null && value > 0)
        {
            _settings.RemoteHostMaxClients = value;
            _settings.Save();
        }
    }

    partial void OnRemoteUseRendezvousChanged(bool value)
    {
        if (!_loadingSettings && _settings != null)
        {
            _settings.RemoteUseRendezvous = value;
            _settings.Save();
        }

        OnPropertyChanged(nameof(RemoteHostRendezvousStatusText));
        OnPropertyChanged(nameof(RemoteHostRendezvousStatusBrush));
        OnPropertyChanged(nameof(RemoteHostWaitingLineText));
    }

    partial void OnRemoteRendezvousServerChanged(string value)
    {
        SyncRendezvousEndpointFromInputs();
    }

    partial void OnRemoteRendezvousPortChanged(int value)
    {
        if (value <= 0 || value > 65535)
        {
            if (!_isSyncingRendezvousEndpoint)
            {
                _isSyncingRendezvousEndpoint = true;
                RemoteRendezvousPort = DefaultRendezvousPort;
                _isSyncingRendezvousEndpoint = false;
            }
            return;
        }

        SyncRendezvousEndpointFromInputs();
    }

    partial void OnRemoteRendezvousServerUrlChanged(string value)
    {
        if (!_isSyncingRendezvousEndpoint)
        {
            ParseRendezvousEndpoint(value, out string serverName, out int serverPort);
            _isSyncingRendezvousEndpoint = true;
            RemoteRendezvousServer = serverName;
            RemoteRendezvousPort = serverPort;
            _isSyncingRendezvousEndpoint = false;
        }

        if (!_loadingSettings && _settings != null)
        {
            _settings.RemoteRendezvousServerUrl = value ?? "";
            _settings.Save();
        }
    }

    partial void OnRemoteRendezvousHostIdChanged(string value)
    {
        if (!_loadingSettings && _settings != null)
        {
            if (RemoteMode == RemoteConnectionMode.Host)
            {
                _settings.RemoteHostId = value ?? "";
            }
            else
            {
                _settings.RemoteRendezvousHostId = value ?? "";
            }
            _settings.Save();
        }
    }

    private void ApplyDisplayedRemoteHostIdForMode(RemoteConnectionMode mode)
    {
        if (_settings == null)
        {
            return;
        }

        string modeSpecificHostId;
        if (mode == RemoteConnectionMode.Host)
        {
            modeSpecificHostId = (_settings.RemoteHostId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(modeSpecificHostId))
            {
                modeSpecificHostId = string.IsNullOrWhiteSpace(_settings.RemoteHostName)
                    ? Environment.MachineName
                    : _settings.RemoteHostName;
            }
        }
        else
        {
            modeSpecificHostId = _settings.RemoteRendezvousHostId ?? string.Empty;
        }

        bool previousLoadingState = _loadingSettings;
        _loadingSettings = true;
        try
        {
            RemoteRendezvousHostId = modeSpecificHostId;
        }
        finally
        {
            _loadingSettings = previousLoadingState;
        }
    }

    partial void OnSelectedRendezvousHostChanged(RendezvousHostSummary value)
    {
        if (value == null)
        {
            return;
        }

        RemoteRendezvousHostId = value.HostId ?? "";

        string selectedHostAddress = ResolveSelectedHostAddress(value);
        if (!string.IsNullOrWhiteSpace(selectedHostAddress))
        {
            RemoteClientHost = selectedHostAddress;
        }

        if (value.PublicPort > 0)
        {
            RemoteClientPort = value.PublicPort;
        }
    }

    private static string ResolveSelectedHostAddress(RendezvousHostSummary value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(value.PublicIp))
        {
            return value.PublicIp.Trim();
        }

        string hostId = (value.HostId ?? string.Empty).Trim();
        if (IPAddress.TryParse(hostId, out _))
        {
            return hostId;
        }

        return string.Empty;
    }

    [RelayCommand]
    private async Task RefreshRendezvousHostsAsync()
    {
        if (_rendezvousControlService == null)
        {
            return;
        }

        string rendezvousUrl = BuildRendezvousServerUrl();
        if (string.IsNullOrWhiteSpace(rendezvousUrl))
        {
            RemoteStatus = "Rendezvous server is required for host discovery";
            return;
        }

        try
        {
            var hosts = await FetchRendezvousHostsAsync(rendezvousUrl, CancellationToken.None);

            RendezvousDiscoveredHosts = new ObservableCollection<RendezvousHostSummary>(hosts ?? Array.Empty<RendezvousHostSummary>());

            if (RendezvousDiscoveredHosts.Count == 0)
            {
                SelectedRendezvousHost = null;
                RemoteStatus = "No rendezvous hosts available";
                return;
            }

            var selected = RendezvousDiscoveredHosts.FirstOrDefault(h => string.Equals(h.HostId, RemoteRendezvousHostId, StringComparison.OrdinalIgnoreCase));
            SelectedRendezvousHost = selected ?? RendezvousDiscoveredHosts[0];
            RemoteStatus = $"Discovered {RendezvousDiscoveredHosts.Count} rendezvous host(s)";
        }
        catch (Exception ex)
        {
            RemoteStatus = $"Rendezvous discovery failed: {ex.Message}";
            DebugLogger.LogAlways("rendezvous", $"Host discovery failed: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<RendezvousHostSummary>> FetchRendezvousHostsAsync(string rendezvousUrl, CancellationToken ct)
    {
        string clientId = BuildRendezvousClientId();
        PrepareRendezvousAccessTokenForClient(clientId, null);
        return await _rendezvousControlService.ListHostsAsync(new RendezvousHostListRequestOptions
        {
            ServerUrl = rendezvousUrl,
            ClientId = clientId
        }, ct);
    }

    private async Task<string> ResolveRendezvousHostIdForConnectAsync(string rendezvousUrl, CancellationToken ct)
    {
        string selectedHostId = (SelectedRendezvousHost?.HostId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(selectedHostId))
        {
            return selectedHostId;
        }

        var hosts = await FetchRendezvousHostsAsync(rendezvousUrl, ct);
        RendezvousDiscoveredHosts = new ObservableCollection<RendezvousHostSummary>(hosts ?? Array.Empty<RendezvousHostSummary>());

        if (RendezvousDiscoveredHosts.Count > 0)
        {
            var matching = RendezvousDiscoveredHosts.FirstOrDefault(h => string.Equals(h.HostId, RemoteRendezvousHostId, StringComparison.OrdinalIgnoreCase));
            SelectedRendezvousHost = matching ?? RendezvousDiscoveredHosts[0];
            return (SelectedRendezvousHost?.HostId ?? string.Empty).Trim();
        }

        string manualHostId = (RemoteRendezvousHostId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(manualHostId))
        {
            DebugLogger.LogAlways("rendezvous", $"No discovered hosts; falling back to manually entered host ID '{manualHostId}'");
            return manualHostId;
        }

        throw new InvalidOperationException("No rendezvous hosts were discovered. Refresh host discovery or enter a host ID.");
    }

    partial void OnRemoteClientHoldSecondsChanged(decimal value)
    {
        decimal normalized = NormalizeHoldSeconds(value);
        if (normalized != value)
        {
            RemoteClientHoldSeconds = normalized;
            return;
        }

        if (!_loadingSettings && _settings != null)
        {
            _settings.RemoteHostClientHoldMs = ConvertHoldSecondsToMs(normalized);
            _settings.Save();
        }
    }

    partial void OnSelectedRadioClientChanged(RadioClientSelection value)
    {
        DebugLogger.Log("radio-select", $"[OnSelectedRadioClientChanged] value={(value?.DisplayName ?? "null")}, _loadingSettings={_loadingSettings}");

        if (!_loadingSettings && value != null)
        {
            // Remember user's explicit selection
            _currentUserSelection = value;

            // Track if user explicitly selected sidetone-only (vs. auto-selected as fallback)
            bool isSidetoneOnly = (value.DisplayName == SIDETONE_ONLY_OPTION);
            _userExplicitlySelectedSidetoneOnly = isSidetoneOnly;

            // DO NOT save to settings here - wait until connection
        }
        // When _loadingSettings is true, this is a programmatic change - ignore it
    }

    partial void OnSelectedSerialPortChanged(string value)
    {
        if (!_loadingSettings && _settings != null)
        {
            _settings.SelectedSerialPort = value;
            _settings.Save();
        }
    }

    partial void OnSelectedMidiDeviceChanged(string value)
    {
        if (!_loadingSettings && _settings != null)
        {
            _settings.SelectedMidiDevice = value;
            _settings.Save();
        }
    }

    partial void OnSelectedAudioDeviceChanged(AudioDeviceInfo value)
    {
        DebugLogger.Log("audio", $"[OnSelectedAudioDeviceChanged] Called with device: {value?.DisplayName ?? "null"}");
        DebugLogger.Log("audio", $"[OnSelectedAudioDeviceChanged] _loadingSettings={_loadingSettings}, _settings={(_settings != null ? "not null" : "null")}");

        if (!_loadingSettings && _settings != null && value != null)
        {
            DebugLogger.Log("audio", $"[OnSelectedAudioDeviceChanged] Saving device ID {value.DeviceId} and reinitializing");
            _settings.SelectedAudioDeviceId = value.DeviceId;
            _settings.Save();

            // Reinitialize sidetone generator with new device
            ReinitializeSidetoneGenerator();
        }
        else
        {
            DebugLogger.Log("audio", "[OnSelectedAudioDeviceChanged] Skipping due to flags or null values");
        }
    }

    private void ReinitializeSidetoneGenerator()
    {
        try
        {
            // Dispose old generator
            _sidetoneGenerator?.Dispose();

            // Create new generator with selected device and setting
            string deviceId = SelectedAudioDevice?.DeviceId ?? "";
            bool aggressiveLowLatency = _settings.WasapiAggressiveLowLatency;
            _sidetoneGenerator = SidetoneGeneratorFactory.Create(deviceId, aggressiveLowLatency);
            _sidetoneGenerator.SetFrequency(CwPitch);
            _sidetoneGenerator.SetVolume(SidetoneVolume);
            _sidetoneGenerator.SetWpm(CwSpeed);

            // Reconnect to keying controller
            _keyingController?.SetSidetoneGenerator(_sidetoneGenerator);

            DebugLogger.Log("audio", $"Sidetone generator reinitialized with device={deviceId}, aggressiveLowLatency={aggressiveLowLatency}");

            Console.WriteLine("Sidetone generator reinitialized with new audio device");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to reinitialize sidetone generator: {ex.Message}");
        }
    }

    private void ReinitializeKeepAwakeStream()
    {
        try
        {
            // Dispose old stream
            _keepAwakeStream?.Stop();
            _keepAwakeStream?.Dispose();
            _keepAwakeStream = null;

            // Create and start new stream if enabled
            if (_settings.KeepAudioDeviceAwake)
            {
                string deviceId = SelectedAudioDevice?.DeviceId ?? "";
                _keepAwakeStream = KeepAwakeStreamFactory.Create(deviceId);
                _keepAwakeStream.Start();
                DebugLogger.Log("audio", $"Keep-awake stream reinitialized with device={deviceId}");
            }
            else
            {
                DebugLogger.Log("audio", "Keep-awake stream disabled");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log("audio", $"Failed to reinitialize keep-awake stream: {ex.Message}");
        }
    }

    partial void OnIsIambicModeChanged(bool value)
    {
        // Update keying controller mode
        _keyingController?.SetKeyingMode(value, IsIambicModeB);

        // Sync to radio
        _radioSettingsSynchronizer?.SyncIambicModeToRadio(value);

        // Update paddle labels when mode changes
        UpdatePaddleLabels();
    }

    private const string SIDETONE_ONLY_OPTION = "No radio (sidetone only)";

    [RelayCommand]
    private void RefreshRadios()
    {
        DebugLogger.Log("radio-select", $"[RefreshRadios] START - current selection: {SelectedRadioClient?.DisplayName ?? "null"}");

        // Set loading flag to prevent user selection tracking during rebuild
        _loadingSettings = true;

        // Build new list of available radio/station combinations
        var newSelections = new List<RadioClientSelection>();

        // Always add sidetone-only option first
        newSelections.Add(new RadioClientSelection
        {
            Radio = null,
            GuiClient = null,
            DisplayName = SIDETONE_ONLY_OPTION
        });

        // Get discovered radios from FlexLib (local LAN radios)
        foreach (var radio in API.RadioList)
        {
            lock (radio.GuiClientsLockObj)
            {
                if (radio.GuiClients != null && radio.GuiClients.Count > 0)
                {
                    // Add an entry for each GUI client
                    foreach (var guiClient in radio.GuiClients)
                    {
                        var selection = new RadioClientSelection
                        {
                            Radio = radio,
                            GuiClient = guiClient,
                            DisplayName = $"{radio.Nickname} ({radio.Model}) - {guiClient.Station} [{guiClient.Program}]"
                        };
                        newSelections.Add(selection);
                    }
                }
                else
                {
                    // No GUI clients yet - add radio without client
                    var selection = new RadioClientSelection
                    {
                        Radio = radio,
                        GuiClient = null,
                        DisplayName = $"{radio.Nickname} ({radio.Model}) - No Stations"
                    };
                    newSelections.Add(selection);
                }
            }
        }

        // If SmartLink is authenticated, add cached WAN radios and ensure connection
        if (_smartLinkManager != null && _smartLinkManager.IsAuthenticated)
        {
            // Add cached WAN radios immediately (if available)
            var cachedRadios = _smartLinkManager.GetCachedWanRadios();
            foreach (var radio in cachedRadios)
            {
                lock (radio.GuiClientsLockObj)
                {
                    if (radio.GuiClients != null && radio.GuiClients.Count > 0)
                    {
                        foreach (var guiClient in radio.GuiClients)
                        {
                            var selection = new RadioClientSelection
                            {
                                Radio = radio,
                                GuiClient = guiClient,
                                DisplayName = $"[SmartLink] {radio.Nickname} ({radio.Model}) - {guiClient.Station} [{guiClient.Program}]"
                            };
                            newSelections.Add(selection);
                        }
                    }
                    else
                    {
                        var selection = new RadioClientSelection
                        {
                            Radio = radio,
                            GuiClient = null,
                            DisplayName = $"[SmartLink] {radio.Nickname} ({radio.Model}) - No Stations"
                        };
                        newSelections.Add(selection);
                    }
                }
            }

            // Reconnect to SmartLink server if needed (will trigger radio list refresh for updates)
            Task.Run(async () =>
            {
                await _smartLinkManager.ConnectToServerAsync();
            });
        }

        // Update the ObservableCollection in place to avoid binding issues
        // Remove items that are no longer in the new list
        for (int i = RadioClientSelections.Count - 1; i >= 0; i--)
        {
            var existing = RadioClientSelections[i];
            bool stillExists = newSelections.Any(n =>
                n.Radio?.Serial == existing.Radio?.Serial &&
                n.GuiClient?.Station == existing.GuiClient?.Station &&
                n.DisplayName == existing.DisplayName);

            if (!stillExists)
            {
                RadioClientSelections.RemoveAt(i);
            }
        }

        // Add items that are new
        foreach (var newItem in newSelections)
        {
            bool alreadyExists = RadioClientSelections.Any(e =>
                e.Radio?.Serial == newItem.Radio?.Serial &&
                e.GuiClient?.Station == newItem.GuiClient?.Station &&
                e.DisplayName == newItem.DisplayName);

            if (!alreadyExists)
            {
                RadioClientSelections.Add(newItem);
            }
        }

        // Restore previously selected radio/client if available
        RadioClientSelection defaultSelection = null;

        // PRIORITY 1: Try to maintain current user selection (if still available)
        if (_currentUserSelection != null)
        {
            // Check if current selection is still in the refreshed list
            defaultSelection = RadioClientSelections.FirstOrDefault(s =>
                s.Radio?.Serial == _currentUserSelection.Radio?.Serial &&
                s.GuiClient?.Station == _currentUserSelection.GuiClient?.Station &&
                s.DisplayName == _currentUserSelection.DisplayName);
        }

        // PRIORITY 2: Try to restore saved preference (if exists and available)
        if (defaultSelection == null && _settings != null && !string.IsNullOrEmpty(_settings.SelectedRadioSerial))
        {
            defaultSelection = RadioClientSelections.FirstOrDefault(s =>
                s.Radio?.Serial == _settings.SelectedRadioSerial &&
                s.GuiClient?.Station == _settings.SelectedGuiClientStation);
        }

        // PRIORITY 3: If saved not available, select first real radio (skip sidetone-only)
        if (defaultSelection == null)
        {
            defaultSelection = RadioClientSelections.FirstOrDefault(s =>
                s.Radio != null && s.GuiClient != null);  // First real radio with GUI client
        }

        // PRIORITY 4: If no real radios exist, fall back to sidetone-only
        if (defaultSelection == null)
        {
            defaultSelection = RadioClientSelections.FirstOrDefault(s =>
                s.DisplayName == SIDETONE_ONLY_OPTION);
        }

        // Apply the selected default (only if it changed)
        if (defaultSelection != null && SelectedRadioClient != defaultSelection)
        {
            DebugLogger.Log("radio-select", $"[RefreshRadios] Setting selection to: {defaultSelection.DisplayName}");
            SelectedRadioClient = defaultSelection;
        }
        else if (defaultSelection == null)
        {
            DebugLogger.Log("radio-select", "[RefreshRadios] No default selection found!");
        }

        _loadingSettings = false;
        DebugLogger.Log("radio-select", $"[RefreshRadios] END - final selection: {SelectedRadioClient?.DisplayName ?? "null"}");
    }

    [RelayCommand]
    private void RefreshSerialPorts()
    {
        _loadingSettings = true;
        SerialPorts.Clear();

        var ports = _inputDeviceManager.DiscoverSerialPorts();
        foreach (var port in ports)
        {
            SerialPorts.Add(port);
        }

        // Restore previously selected serial port if available
        if (_settings != null && !string.IsNullOrEmpty(_settings.SelectedSerialPort))
        {
            if (SerialPorts.Contains(_settings.SelectedSerialPort))
            {
                SelectedSerialPort = _settings.SelectedSerialPort;
            }
        }

        _loadingSettings = false;
        UpdatePaddleLabels();
    }

    [RelayCommand]
    private void RefreshMidiDevices()
    {
        _loadingSettings = true;
        MidiDevices.Clear();

        var devices = _inputDeviceManager.DiscoverMidiDevices();
        foreach (var device in devices)
        {
            MidiDevices.Add(device);
        }

        // Restore previously selected MIDI device if available (only if we have real devices)
        if (!devices[0].Contains("No MIDI") && !devices[0].Contains("Error"))
        {
            if (_settings != null && !string.IsNullOrEmpty(_settings.SelectedMidiDevice))
            {
                if (MidiDevices.Contains(_settings.SelectedMidiDevice))
                {
                    SelectedMidiDevice = _settings.SelectedMidiDevice;
                }
            }
        }

        _loadingSettings = false;
        UpdatePaddleLabels();
    }

    [RelayCommand]
    private void RefreshAudioDevices()
    {
        DebugLogger.Log("audio", "[RefreshAudioDevices] Starting...");
        _loadingSettings = true;
        AudioDevices.Clear();

        try
        {
            // Use platform-aware enumeration from factory
            var devices = SidetoneGeneratorFactory.EnumerateDevices();

            foreach (var (deviceId, name) in devices)
            {
                AudioDevices.Add(new AudioDeviceInfo { DeviceId = deviceId, Name = name });
            }

            DebugLogger.Log("audio", $"[RefreshAudioDevices] Total devices in collection: {AudioDevices.Count}");

            // Restore previously selected device if available
            if (_settings != null)
            {
                var savedDevice = AudioDevices.FirstOrDefault(d => d.DeviceId == _settings.SelectedAudioDeviceId);
                if (savedDevice != null)
                {
                    SelectedAudioDevice = savedDevice;
                    DebugLogger.Log("audio", $"[RefreshAudioDevices] Restored saved device: {savedDevice.DisplayName}");
                }
                else
                {
                    // Default to "System Default"
                    SelectedAudioDevice = AudioDevices.FirstOrDefault(d => string.IsNullOrEmpty(d.DeviceId));
                    DebugLogger.Log("audio", "[RefreshAudioDevices] Using System Default");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log("audio", $"[RefreshAudioDevices] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            DebugLogger.Log("audio", $"[RefreshAudioDevices] Stack trace: {ex.StackTrace}");
            // Add default option on error
            if (AudioDevices.Count == 0)
            {
                AudioDevices.Add(new AudioDeviceInfo
                {
                    DeviceId = "",
                    Name = "System Default"
                });
            }
            SelectedAudioDevice = AudioDevices[0];
        }

        DebugLogger.Log("audio", "[RefreshAudioDevices] Complete");
        _loadingSettings = false;
    }

    [RelayCommand]
    private async Task ConfigureMidiNotes()
    {
        var dialog = new Views.MidiConfigDialog();

        // Load current mappings
        dialog.LoadMappings(_settings.MidiNoteMappings);

        // Get the main window
        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (mainWindow == null)
        {
            return;
        }

        await dialog.ShowDialog(mainWindow);

        if (dialog.ConfigurationSaved)
        {
            // Save the new mappings
            _settings.MidiNoteMappings = dialog.Mappings;
            _settings.Save();

            // Update the MIDI input if it's currently open
            _inputDeviceManager.UpdateMidiNoteMappings(_settings.MidiNoteMappings);
        }
    }

    [RelayCommand]
    private async Task SelectAudioDevice()
    {
        var dialog = new Views.AudioDeviceDialog();

        // Set current device
        string currentDeviceId = SelectedAudioDevice?.DeviceId ?? "";
        dialog.SetCurrentDevice(currentDeviceId);

        // Get the main window
        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (mainWindow == null)
        {
            return;
        }

        await dialog.ShowDialog(mainWindow);

        if (dialog.DeviceChanged)
        {
            DebugLogger.Log("audio", $"[SelectAudioDevice] Device changed to ID: {dialog.SelectedDeviceId}");

            // Save the aggressive low-latency setting BEFORE reinitializing generator
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _settings.WasapiAggressiveLowLatency = dialog.AggressiveLowLatency;
                DebugLogger.Log("audio", $"[SelectAudioDevice] Saved AggressiveLowLatency={dialog.AggressiveLowLatency}");
            }

            // Save the keep-awake setting and update the stream
            bool keepAwakeChanged = _settings.KeepAudioDeviceAwake != dialog.KeepAudioDeviceAwake;
            bool timingLogChanged = _settings.EnableDetailedTimingAnalysisLogging != dialog.EnableDetailedTimingAnalysisLogging;
            _settings.KeepAudioDeviceAwake = dialog.KeepAudioDeviceAwake;
            _settings.EnableDetailedTimingAnalysisLogging = dialog.EnableDetailedTimingAnalysisLogging;
            _settings.Save();
            DebugLogger.Log("audio", $"[SelectAudioDevice] Saved KeepAudioDeviceAwake={dialog.KeepAudioDeviceAwake}");

            if (timingLogChanged)
            {
                SidetoneLatencyProbe.Configure(_settings.EnableDetailedTimingAnalysisLogging);
                DebugLogger.LogAlways("audio", $"[LatencyProbe] EnableDetailedTimingAnalysisLogging={_settings.EnableDetailedTimingAnalysisLogging}");
            }

            // Update the selected device - this will trigger OnSelectedAudioDeviceChanged
            // which handles saving settings and reinitializing the sidetone generator
            var newDeviceId = dialog.SelectedDeviceId;

            DebugLogger.Log("audio", $"[SelectAudioDevice] AudioDevices count: {AudioDevices.Count}");
            var deviceInfo = AudioDevices.FirstOrDefault(d => d.DeviceId == newDeviceId);

            if (deviceInfo != null)
            {
                DebugLogger.Log("audio", $"[SelectAudioDevice] Found device in collection: {deviceInfo.DisplayName}");
                SelectedAudioDevice = deviceInfo;
            }
            else
            {
                DebugLogger.Log("audio", $"[SelectAudioDevice] Device not found in AudioDevices collection!");
            }

            // Handle keep-awake stream changes
            if (keepAwakeChanged || deviceInfo != null)
            {
                ReinitializeKeepAwakeStream();
            }
        }
        else
        {
            DebugLogger.Log("audio", "[SelectAudioDevice] DeviceChanged is false");
        }
    }

    [RelayCommand]
    private async Task ConfigureAccessToken()
    {
        var dialog = new Views.AccessTokenDialog();
        dialog.SetCurrentSettings(
            _rendezvousAccessToken,
            _rendezvousUseLocalJwtMinting,
            _rendezvousJwtKeyId,
            _rendezvousJwtKeySecret,
            _rendezvousJwtIssuer,
            _rendezvousJwtAudience,
            _rendezvousJwtTokenLifetimeMinutes);

        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow == null)
        {
            return;
        }

        await dialog.ShowDialog(mainWindow);

        if (!dialog.TokenSaved)
        {
            return;
        }

        _rendezvousAccessToken = dialog.AccessToken ?? string.Empty;
        _rendezvousUseLocalJwtMinting = dialog.UseLocalJwtMinting;
        _rendezvousJwtKeyId = dialog.JwtKeyId ?? string.Empty;
        _rendezvousJwtKeySecret = dialog.JwtKeySecret ?? string.Empty;
        _rendezvousJwtIssuer = dialog.JwtIssuer ?? string.Empty;
        _rendezvousJwtAudience = dialog.JwtAudience ?? string.Empty;
        _rendezvousJwtTokenLifetimeMinutes = dialog.JwtTokenLifetimeMinutes;

        if (_settings != null)
        {
            _settings.RendezvousAccessToken = _rendezvousAccessToken;
            _settings.RendezvousUseLocalJwtMinting = _rendezvousUseLocalJwtMinting;
            _settings.RendezvousJwtKeyId = _rendezvousJwtKeyId;
            _settings.RendezvousJwtKeySecret = _rendezvousJwtKeySecret;
            _settings.RendezvousJwtIssuer = _rendezvousJwtIssuer;
            _settings.RendezvousJwtAudience = _rendezvousJwtAudience;
            _settings.RendezvousJwtTokenLifetimeMinutes = _rendezvousJwtTokenLifetimeMinutes;
            _settings.Save();
        }

        ApplyConfiguredRendezvousAccessTokenEnvironment();
        if (_rendezvousUseLocalJwtMinting)
        {
            DebugLogger.LogAlways("rendezvous", "Rendezvous local JWT minting settings updated.");
        }
        else
        {
            DebugLogger.LogAlways("rendezvous", string.IsNullOrWhiteSpace(_rendezvousAccessToken)
                ? "Rendezvous access token cleared from settings."
                : "Rendezvous access token updated from settings.");
        }
    }

    [RelayCommand]
    private async Task TestRendezvousConnectionAsync()
    {
        if (!RemoteUseRendezvous)
        {
            RemoteStatus = "Enable Use Rendezvous before running auth test";
            RendezvousAuthTestButtonBrush = Brushes.Red;
            return;
        }

        string rendezvousUrl = BuildRendezvousServerUrl();
        if (string.IsNullOrWhiteSpace(rendezvousUrl))
        {
            RemoteStatus = "Rendezvous server is required for auth test";
            RendezvousAuthTestButtonBrush = Brushes.Red;
            return;
        }

        string role;
        if (RemoteMode == RemoteConnectionMode.Host)
        {
            string hostId = GetRendezvousHostId();
            PrepareRendezvousAccessTokenForHost(hostId);
            role = "host";
        }
        else
        {
            string clientId = BuildRendezvousClientId();
            string targetHostId = (SelectedRendezvousHost?.HostId ?? RemoteRendezvousHostId ?? string.Empty).Trim();
            PrepareRendezvousAccessTokenForClient(clientId, string.IsNullOrWhiteSpace(targetHostId) ? null : targetHostId);
            role = "client";
        }

        try
        {
            Uri endpoint = BuildRendezvousWebSocketEndpoint(rendezvousUrl, role);

            using var ws = new ClientWebSocket();
            ApplyRendezvousAccessTokenHeader(ws);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            await ws.ConnectAsync(endpoint, timeout.Token);

            RemoteStatus = $"Rendezvous auth test passed ({role})";
            RendezvousAuthTestButtonBrush = Brushes.Green;
            DebugLogger.LogAlways("rendezvous", $"Auth test passed role={role} endpoint={endpoint}");

            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "auth_test_complete", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            string message = NormalizeRendezvousAuthTestError(ex);
            RemoteStatus = $"Rendezvous auth test failed ({role}): {message}";
            RendezvousAuthTestButtonBrush = Brushes.Red;
            DebugLogger.LogAlways("rendezvous", $"Auth test failed role={role}: {message}");
        }
    }

    private static void ApplyRendezvousAccessTokenHeader(ClientWebSocket ws)
    {
        string token = Environment.GetEnvironmentVariable("NETKEYER_RENDEZVOUS_ACCESS_TOKEN") ?? string.Empty;
        token = token.Trim();
        if (!string.IsNullOrWhiteSpace(token))
        {
            ws.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        }
    }

    private static Uri BuildRendezvousWebSocketEndpoint(string rendezvousUrl, string role)
    {
        if (!Uri.TryCreate(rendezvousUrl, UriKind.Absolute, out Uri uri))
        {
            throw new InvalidOperationException("Rendezvous URL is invalid.");
        }

        string scheme = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            ? "wss"
            : "ws";

        var builder = new UriBuilder(uri)
        {
            Scheme = scheme,
            Path = $"/ws/{role.Trim().ToLowerInvariant()}",
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri;
    }

    private static string NormalizeRendezvousAuthTestError(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return "request timed out";
        }

        if (ex is WebSocketException wsEx)
        {
            return wsEx.Message;
        }

        return ex.Message;
    }

    private void PrepareRendezvousAccessTokenForHost(string hostId)
    {
        if (IsLocalRendezvousJwtMintingEnabled())
        {
            string token = GenerateRendezvousAccessToken(
                subject: hostId,
                role: "host",
                scopes: new[] { $"host:{hostId}", "rendezvous:host" });
            ApplyRendezvousAccessTokenEnvironment(token);
            return;
        }

        EnsureManualAccessTokenLooksLikeJwt("host");

        ApplyConfiguredRendezvousAccessTokenEnvironment();
    }

    private void PrepareRendezvousAccessTokenForClient(string clientId, string hostId)
    {
        if (IsLocalRendezvousJwtMintingEnabled())
        {
            var scopes = new List<string>
            {
                $"client:{clientId}",
                "rendezvous:client"
            };

            if (!string.IsNullOrWhiteSpace(hostId))
            {
                scopes.Add($"target_host:{hostId}");
                scopes.Add("rendezvous:connect");
            }

            string token = GenerateRendezvousAccessToken(
                subject: clientId,
                role: "client",
                scopes: scopes);
            ApplyRendezvousAccessTokenEnvironment(token);
            return;
        }

        EnsureManualAccessTokenLooksLikeJwt("client");

        ApplyConfiguredRendezvousAccessTokenEnvironment();
    }

    private void EnsureManualAccessTokenLooksLikeJwt(string role)
    {
        if (_rendezvousUseLocalJwtMinting)
        {
            return;
        }

        string token = (_rendezvousAccessToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"Manual access token is empty for {role} mode. Enter a JWT access token or enable local JWT minting.");
        }

        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Manual access token should be the raw JWT only. Do not include the 'Bearer ' prefix.");
        }

        if (!LooksLikeJwt(token))
        {
            throw new InvalidOperationException("Manual access token is not a JWT (expected 3 segments: header.payload.signature). Do not paste RENDEZVOUS_JWT_SECRET into the app token field.");
        }
    }

    private static bool LooksLikeJwt(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        int dotCount = token.Count(ch => ch == '.');
        return dotCount == 2;
    }

    private bool IsLocalRendezvousJwtMintingEnabled()
    {
        return _rendezvousUseLocalJwtMinting && !string.IsNullOrWhiteSpace(_rendezvousJwtKeySecret);
    }

    private void ApplyConfiguredRendezvousAccessTokenEnvironment()
    {
        if (_rendezvousUseLocalJwtMinting)
        {
            if (string.IsNullOrWhiteSpace(_rendezvousJwtKeySecret))
            {
                DebugLogger.LogAlways("rendezvous", "Local JWT minting is enabled but no ID key secret is configured; falling back to manual access token mode.");
                ApplyRendezvousAccessTokenEnvironment(_rendezvousAccessToken);
                return;
            }

            // Generated tokens are operation-specific (host/client and target scope), so clear static token value.
            ApplyRendezvousAccessTokenEnvironment(string.Empty);
            return;
        }

        ApplyRendezvousAccessTokenEnvironment(_rendezvousAccessToken);
    }

    private string GenerateRendezvousAccessToken(string subject, string role, IEnumerable<string> scopes)
    {
        string normalizedSubject = (subject ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedSubject))
        {
            throw new InvalidOperationException("Cannot mint rendezvous token because subject is empty.");
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int ttlMinutes = Math.Max(1, Math.Min(1440, _rendezvousJwtTokenLifetimeMinutes));
        long exp = now + (ttlMinutes * 60L);

        string scopeValue = string.Join(" ", (scopes ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));

        var claims = new Dictionary<string, object>
        {
            ["sub"] = normalizedSubject,
            ["role"] = role,
            ["scope"] = scopeValue,
            ["iat"] = now,
            ["exp"] = exp,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["protocol_version"] = 1
        };

        if (!string.IsNullOrWhiteSpace(_rendezvousJwtIssuer))
        {
            claims["iss"] = _rendezvousJwtIssuer.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_rendezvousJwtAudience))
        {
            claims["aud"] = _rendezvousJwtAudience.Trim();
        }

        return RendezvousJwtTokenFactory.CreateHs256Token(
            _rendezvousJwtKeySecret,
            claims,
            _rendezvousJwtKeyId);
    }

    private static void ApplyRendezvousAccessTokenEnvironment(string token)
    {
        string normalized = (token ?? string.Empty).Trim();
        Environment.SetEnvironmentVariable("NETKEYER_RENDEZVOUS_ACCESS_TOKEN", string.IsNullOrWhiteSpace(normalized) ? null : normalized);
    }

    private void CloseInputDevice()
    {
        // Stop keying controller
        _keyingController?.Stop();

        // Close the device
        _inputDeviceManager?.CloseDevice();

        // Reset keying controller state
        _keyingController?.ResetState();

        // Reset indicators
        LeftPaddleIndicatorColor = Brushes.Black;
        RightPaddleIndicatorColor = Brushes.Black;
        LeftPaddleStateText = "OFF";
        RightPaddleStateText = "OFF";
    }

    private void OpenInputDevice()
    {
        string deviceName = InputType == InputDeviceType.Serial ? SelectedSerialPort : SelectedMidiDevice;

        try
        {
            _inputDeviceManager.OpenDevice(InputType, deviceName, _settings.MidiNoteMappings);

            // Reset keying controller state to ensure clean start
            _keyingController?.ResetState();

            // InputDeviceManager will emit an initial PaddleStateChanged event with current state
        }
        catch (Exception ex)
        {
            RadioStatus = ex.Message;
            RadioStatusColor = Brushes.Orange;
            HasRadioError = true;
        }
    }

    private void InputDeviceManager_PaddleStateChanged(object sender, PaddleStateChangedEventArgs e)
    {
        SetHostSidetoneForKeyingSource(isLocalInput: true);

        // Swap is now handled in InputDeviceManager
        bool leftPaddleState = e.LeftPaddle;
        bool rightPaddleState = e.RightPaddle;
        bool straightKeyState = e.StraightKey;
        bool pttState = e.PTT;
        bool pttClosureState = pttState || leftPaddleState || rightPaddleState || straightKeyState;
        bool remoteClientPttOnlyMode = ShouldUseRemoteClientPttBehavior();
        bool effectiveTransmitModeCW = IsEffectiveTransmitModeCW();

        bool closureForProbe = effectiveTransmitModeCW && !remoteClientPttOnlyMode &&
            (IsIambicMode ? (leftPaddleState || rightPaddleState) : straightKeyState);

        if (closureForProbe && !_latencyProbeKeyClosed)
        {
            bool likelyIdle = !_settings.KeepAudioDeviceAwake && _settings.WasapiAggressiveLowLatency;
            string keyerStateAtClosure = _keyingController?.GetKeyingStateForProbe() ?? "KeyerUnavailable";
            SidetoneLatencyProbe.MarkInputClosure("input:paddle_state_changed", likelyIdle, keyerStateAtClosure);
            _latencyProbeKeyClosed = true;
        }
        else if (!closureForProbe)
        {
            _latencyProbeKeyClosed = false;
        }

        DebugLogger.Log("input", $"[InputDeviceManager_PaddleStateChanged] Received event: L={leftPaddleState} R={rightPaddleState} SK={straightKeyState} PTT={pttState}");

        // Update indicators
        Dispatcher.UIThread.Post(() =>
        {
            bool leftIndicatorState;

            // Check transmit mode first (CW vs PTT), then keying mode (iambic vs straight)
            if (!effectiveTransmitModeCW)
            {
                // PTT mode (non-CW radio modes) - use PTT state
                // (InputDeviceManager sets this to OR of both paddles for serial input)
                leftIndicatorState = pttClosureState;
            }
            else if (IsIambicMode)
            {
                // CW iambic mode - left paddle indicator
                leftIndicatorState = leftPaddleState;
            }
            else
            {
                // CW straight key mode - use straight key state
                // (InputDeviceManager sets this to OR of both paddles for serial input)
                leftIndicatorState = straightKeyState;
            }

            DebugLogger.Log("input", $"[Indicator Update] IsIambic={IsIambicMode} IsCW={effectiveTransmitModeCW} HostCW={_remoteHostTransmitModeCW} RemotePttOnly={remoteClientPttOnlyMode} Sidetone={_isSidetoneOnlyMode} | L={leftPaddleState} R={rightPaddleState} SK={straightKeyState} PTT={pttState} | LeftInd={leftIndicatorState}");

            LeftPaddleIndicatorColor = leftIndicatorState ? Brushes.LimeGreen : Brushes.Black;
            LeftPaddleStateText = leftIndicatorState ? "ON" : "OFF";
            RightPaddleIndicatorColor = rightPaddleState ? Brushes.LimeGreen : Brushes.Black;
            RightPaddleStateText = rightPaddleState ? "ON" : "OFF";
        });

        // When remote host is non-CW, suppress local keyer/sidetone and send PTT-only intent remotely.
        if (!remoteClientPttOnlyMode)
        {
            SidetoneLatencyProbe.MarkBeforeControllerDispatch("input:before_keying_controller_call");
            _keyingController?.HandlePaddleStateChange(leftPaddleState, rightPaddleState, straightKeyState, pttState);
        }
        else
        {
            _keyingController?.Stop();
            _sidetoneGenerator?.Stop();
        }

        if (RemoteMode == RemoteConnectionMode.Client)
        {
            if (remoteClientPttOnlyMode)
            {
                _ = SendRemotePaddleStateAsync(false, false, false, pttClosureState);
            }
            else
            {
                _ = SendRemotePaddleStateAsync(leftPaddleState, rightPaddleState, straightKeyState, pttState);
            }
        }
    }

    private async Task SendRemotePaddleStateAsync(bool leftPaddle, bool rightPaddle, bool straightKey, bool ptt)
    {
        try
        {
            if (_remoteClientService == null || !_remoteClientService.IsConnected)
            {
                return;
            }

            await _remoteClientService.SendPaddleStateAsync(new PaddleStatePayload
            {
                LeftPaddle = leftPaddle,
                RightPaddle = rightPaddle,
                StraightKey = straightKey,
                Ptt = ptt,
                SenderTickMs = Environment.TickCount64
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            DebugLogger.Log("remote", $"Failed to send paddle state: {ex.Message}");
        }
    }

    private async Task StartRemoteClientAsync()
    {
        _remoteCts?.Cancel();
        _remoteCts?.Dispose();
        _remoteCts = new CancellationTokenSource();
        _remoteHostTransmitModeCW = true;
        _remoteHostTransmitMode = "CW";
        _keyingController?.SetTransmitMode(_transmitSliceMonitor.IsTransmitModeCW);

        RemoteConnectedHostIp = string.Empty;
        RemoteConnectedHostName = string.Empty;
        ResetTelemetryDisplay("host");

        string targetHost = RemoteClientHost;
        int targetPort = RemoteClientPort;

        if (RemoteUseRendezvous)
        {
            string rendezvousUrl = BuildRendezvousServerUrl();

            if (string.IsNullOrWhiteSpace(rendezvousUrl))
            {
                throw new InvalidOperationException("Rendezvous server is required when rendezvous mode is enabled.");
            }

            string hostId = await ResolveRendezvousHostIdForConnectAsync(rendezvousUrl, _remoteCts.Token);
            string clientId = BuildRendezvousClientId();
            PrepareRendezvousAccessTokenForClient(clientId, hostId);

            DebugLogger.LogAlways("rendezvous", $"Attempting client rendezvous connect: url={rendezvousUrl}, hostId={hostId}");

            try
            {
                _rendezvousClientSession = await _rendezvousControlService.ConnectClientAsync(new RendezvousClientConnectOptions
                {
                    ServerUrl = rendezvousUrl,
                    ClientId = clientId,
                    HostId = hostId
                }, _remoteCts.Token);
            }
            catch (Exception ex)
            {
                DebugLogger.LogAlways("rendezvous", $"Client rendezvous connect failed: {ex.Message}");
                throw;
            }

            targetHost = _rendezvousClientSession.Endpoint.HostPublicIp;
            targetPort = _rendezvousClientSession.Endpoint.HostPublicPort;
            DebugLogger.LogAlways("rendezvous", $"Resolved host endpoint via rendezvous: {targetHost}:{targetPort} (session {_rendezvousClientSession.Endpoint.SessionId})");

            if (_forceRelayTransportForExperiments)
            {
                DebugLogger.LogAlways("rendezvous", "Force relay transport is enabled; skipping direct and mapped-direct transport attempts");
                await ConnectRendezvousSessionViaRelayAsync();
                return;
            }
        }

        try
        {
            CancellationToken connectToken = _remoteCts.Token;
            CancellationTokenSource directConnectTimeoutCts = null;

            if (_rendezvousClientSession != null)
            {
                directConnectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_remoteCts.Token);
                directConnectTimeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
                connectToken = directConnectTimeoutCts.Token;
                DebugLogger.LogAlways("rendezvous", "Client direct connect attempt timeout set to 2 seconds before relay fallback");
            }

            DebugLogger.LogAlways("remote", $"Client transport connect attempt: target={targetHost}:{targetPort}");
            try
            {
                await _remoteClientService.ConnectAsync(new RemoteClientOptions
                {
                    TargetHost = targetHost,
                    TargetPort = targetPort,
                    SharedToken = RemoteSharedToken,
                    Callsign = RemoteCallsign,
                    EnableSecureTransport = _enableSecureRemoteTransport,
                    RequireSecureTransport = _requireSecureRemoteTransport,
                    ValidateRelayCiphertext = _validateRelayCiphertext
                }, connectToken);
            }
            finally
            {
                directConnectTimeoutCts?.Dispose();
            }

            if (_rendezvousClientSession != null)
            {
                await _rendezvousClientSession.ReportPunchResultAsync(success: true, _remoteCts.Token);
                _rendezvousControlService.StartClientControlMonitor(_rendezvousClientSession);
            }

            DebugLogger.LogAlways("remote", $"Client transport connected (transport=direct) endpoint={targetHost}:{targetPort}");
        }
        catch (Exception directConnectEx)
        {
            if (_rendezvousClientSession == null)
            {
                throw;
            }

            if (_remoteCts.Token.IsCancellationRequested)
            {
                throw;
            }

            if (directConnectEx is OperationCanceledException)
            {
                DebugLogger.LogAlways("rendezvous", "Client direct connect timed out; requesting host automatic port mapping");
            }
            else
            {
                DebugLogger.LogAlways("rendezvous", $"Client direct connect failed; requesting host automatic port mapping: {directConnectEx.Message}");
            }

            try
            {
                await _rendezvousClientSession.RequestPortMapAsync(_remoteCts.Token);
            }
            catch
            {
            }

            bool mappedEndpointAvailable = await _rendezvousControlService.WaitForMappedEndpointAsync(
                _rendezvousClientSession,
                TimeSpan.FromSeconds(6),
                _remoteCts.Token);

            if (mappedEndpointAvailable)
            {
                string mappedHost = _rendezvousClientSession.Endpoint.HostPublicIp;
                int mappedPort = _rendezvousClientSession.Endpoint.HostPublicPort;
                DebugLogger.LogAlways("rendezvous", $"Retrying direct connect using mapped endpoint {mappedHost}:{mappedPort}");

                try
                {
                    using var mappedConnectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_remoteCts.Token);
                    mappedConnectTimeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
                    DebugLogger.LogAlways("rendezvous", "Mapped direct connect attempt timeout set to 4 seconds before relay fallback");

                    await _remoteClientService.ConnectAsync(new RemoteClientOptions
                    {
                        TargetHost = mappedHost,
                        TargetPort = mappedPort,
                        SharedToken = RemoteSharedToken,
                        Callsign = RemoteCallsign,
                        EnableSecureTransport = _enableSecureRemoteTransport,
                        RequireSecureTransport = _requireSecureRemoteTransport,
                        ValidateRelayCiphertext = _validateRelayCiphertext
                    }, mappedConnectTimeoutCts.Token);

                    DebugLogger.LogAlways("remote", $"Client transport connected (transport=mapped-direct) endpoint={mappedHost}:{mappedPort}");

                    await _rendezvousClientSession.ReportPunchResultAsync(success: true, _remoteCts.Token);
                    _rendezvousControlService.StartClientControlMonitor(_rendezvousClientSession);
                    return;
                }
                catch (Exception mappedDirectEx)
                {
                    DebugLogger.LogAlways("rendezvous", $"Mapped direct connect failed; switching to relay fallback path: {mappedDirectEx.Message}");
                    try
                    {
                        await _rendezvousClientSession.ReportPunchResultAsync(success: false, CancellationToken.None);
                    }
                    catch
                    {
                    }
                }
            }

            bool relayAvailable = _rendezvousClientSession.HasRelayEndpoint
                || await _rendezvousControlService.WaitForRelayAsync(_rendezvousClientSession, TimeSpan.FromSeconds(4), _remoteCts.Token);

            if (!relayAvailable)
            {
                try
                {
                    await _rendezvousClientSession.DisposeAsync();
                }
                finally
                {
                    _rendezvousClientSession = null;
                }

                throw new InvalidOperationException("Direct connection failed and rendezvous relay fallback was not provided.", directConnectEx);
            }

            string relayHost = _rendezvousClientSession.RelayHost;
            int relayPort = _rendezvousClientSession.RelayPort;
            string relaySessionId = _rendezvousClientSession.Endpoint.SessionId;

            DebugLogger.LogAlways("rendezvous", $"Falling back to relay transport {relayHost}:{relayPort} (session {relaySessionId})");

            try
            {
                await _remoteClientService.ConnectAsync(new RemoteClientOptions
                {
                    TargetHost = relayHost,
                    TargetPort = relayPort,
                    SharedToken = RemoteSharedToken,
                    Callsign = RemoteCallsign,
                    RelaySessionId = relaySessionId,
                    RelayRole = "CLIENT",
                    EnableSecureTransport = _enableSecureRemoteTransport,
                    RequireSecureTransport = _requireSecureRemoteTransport,
                    ValidateRelayCiphertext = _validateRelayCiphertext
                }, _remoteCts.Token);

                await _rendezvousClientSession.ReportPunchResultAsync(success: true, _remoteCts.Token);
                _rendezvousControlService.StartClientControlMonitor(_rendezvousClientSession);

                DebugLogger.LogAlways("remote", $"Client transport connected (transport=relay) endpoint={relayHost}:{relayPort} session={relaySessionId}");
            }
            catch
            {
                try
                {
                    await _rendezvousClientSession.DisposeAsync();
                }
                finally
                {
                    _rendezvousClientSession = null;
                }

                throw;
            }
        }
    }

    private async Task ConnectRendezvousSessionViaRelayAsync()
    {
        if (_rendezvousClientSession == null)
        {
            throw new InvalidOperationException("Rendezvous session is required for relay transport.");
        }

        bool relayAvailable = _rendezvousClientSession.HasRelayEndpoint
            || await _rendezvousControlService.WaitForRelayAsync(_rendezvousClientSession, TimeSpan.FromSeconds(8), _remoteCts?.Token ?? CancellationToken.None);

        if (!relayAvailable)
        {
            throw new InvalidOperationException("Force relay transport was enabled, but rendezvous relay endpoint was not provided.");
        }

        string relayHost = _rendezvousClientSession.RelayHost;
        int relayPort = _rendezvousClientSession.RelayPort;
        string relaySessionId = _rendezvousClientSession.Endpoint.SessionId;

        DebugLogger.LogAlways("rendezvous", $"Force relay transport enabled: connecting to relay {relayHost}:{relayPort} (session {relaySessionId})");

        await _remoteClientService.ConnectAsync(new RemoteClientOptions
        {
            TargetHost = relayHost,
            TargetPort = relayPort,
            SharedToken = RemoteSharedToken,
            Callsign = RemoteCallsign,
            RelaySessionId = relaySessionId,
            RelayRole = "CLIENT",
            EnableSecureTransport = _enableSecureRemoteTransport,
            RequireSecureTransport = _requireSecureRemoteTransport,
            ValidateRelayCiphertext = _validateRelayCiphertext
        }, _remoteCts?.Token ?? CancellationToken.None);

        await _rendezvousClientSession.ReportPunchResultAsync(success: true, _remoteCts?.Token ?? CancellationToken.None);
        _rendezvousControlService.StartClientControlMonitor(_rendezvousClientSession);

        DebugLogger.LogAlways("remote", $"Client transport connected (transport=relay) endpoint={relayHost}:{relayPort} session={relaySessionId}");
    }

    private static bool IsTruthyEnvironmentValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string value = raw.Trim().ToLowerInvariant();
        return value == "1" || value == "true" || value == "yes" || value == "on";
    }

    private static bool IsFalseyEnvironmentValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string value = raw.Trim().ToLowerInvariant();
        return value == "0" || value == "false" || value == "no" || value == "off";
    }

    private static bool IsDebugInsecureOverridesEnabled(string raw)
    {
        return IsDebugBuild && IsTruthyEnvironmentValue(raw);
    }

    private static bool IsSecureDefaultEnabled(string raw, bool debugInsecureOverridesEnabled)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        string value = raw.Trim().ToLowerInvariant();
        if (value == "0" || value == "false" || value == "no" || value == "off")
        {
            return debugInsecureOverridesEnabled ? false : true;
        }

        if (value == "1" || value == "true" || value == "yes" || value == "on")
        {
            return true;
        }

        // Fail closed for unrecognized values by keeping secure defaults enabled.
        return true;
    }

        private const bool IsDebugBuild =
    #if DEBUG
        true;
    #else
        false;
    #endif

    private async Task StartRemoteHostAsync()
    {
        _remoteCts?.Cancel();
        _remoteCts?.Dispose();
        _remoteCts = new CancellationTokenSource();
        RemoteConnectedClients = 0;
        ResetTelemetryDisplay("host");

        MuteHostSidetone();

        await _remoteHostService.StartAsync(new RemoteHostOptions
        {
            HostName = RemoteHostName,
            BindAddress = RemoteHostBindAddress,
            ListenPort = RemoteHostPort,
            SharedToken = RemoteSharedToken,
            MaxClients = Math.Max(1, Math.Min(5, RemoteMaxClients)),
            ActiveClientHoldMs = ConvertHoldSecondsToMs(RemoteClientHoldSeconds),
            UseSenderTickStaleGate = _settings?.RemoteHostUseSenderTickStaleGate ?? false,
            EnableSecureTransport = _enableSecureRemoteTransport,
            RequireSecureTransport = _requireSecureRemoteTransport,
            ValidateRelayCiphertext = _validateRelayCiphertext
        }, _remoteCts.Token);

        _remoteHostService.SetTransmitMode(_transmitSliceMonitor.IsTransmitModeCW, _transmitSliceMonitor.TransmitMode);

        lock (_relayHostSessionsLock)
        {
            _relayHostSessions.Clear();
        }

        if (RemoteUseRendezvous)
        {
            string rendezvousUrl = BuildRendezvousServerUrl();
            if (string.IsNullOrWhiteSpace(rendezvousUrl))
            {
                DebugLogger.LogAlways("rendezvous", "Host rendezvous registration skipped: URL is empty while Use Rendezvous is enabled");
                throw new InvalidOperationException("Rendezvous server is required when rendezvous mode is enabled.");
            }

            string hostId = GetRendezvousHostId();
            PrepareRendezvousAccessTokenForHost(hostId);
            DebugLogger.LogAlways("rendezvous", $"Attempting host rendezvous registration: url={rendezvousUrl}, hostId={hostId}");

            try
            {
                _rendezvousHostSession = await _rendezvousControlService.RegisterHostAsync(new RendezvousHostRegistrationOptions
                {
                    ServerUrl = rendezvousUrl,
                    HostId = hostId,
                    MaxClients = Math.Max(1, Math.Min(5, RemoteMaxClients)),
                    OnUseRelayAsync = async (relayHost, relayPort, sessionId) =>
                    {
                        if (string.IsNullOrWhiteSpace(sessionId))
                        {
                            return;
                        }

                        bool alreadyActive;
                        lock (_relayHostSessionsLock)
                        {
                            alreadyActive = !_relayHostSessions.Add(sessionId);
                        }

                        if (alreadyActive)
                        {
                            return;
                        }

                        try
                        {
                            CancellationToken token = _remoteCts?.Token ?? CancellationToken.None;
                            await _remoteHostService.ConnectRelaySessionAsync(relayHost, relayPort, sessionId, token);
                        }
                        catch (Exception ex)
                        {
                            lock (_relayHostSessionsLock)
                            {
                                _relayHostSessions.Remove(sessionId);
                            }

                            DebugLogger.LogAlways("rendezvous", $"Failed to open host relay session {sessionId}: {ex.Message}");
                        }
                    },
                    OnRequestPortMapAsync = async (sessionId, internalPort) =>
                    {
                        if (_hostPortMapper == null)
                        {
                            return new RendezvousPortMapResult();
                        }

                        try
                        {
                            HostPortMapResult mapResult = await _hostPortMapper.TryMapTcpPortAsync(internalPort, _remoteCts?.Token ?? CancellationToken.None);
                            return new RendezvousPortMapResult
                            {
                                Success = mapResult.Success,
                                PublicIp = mapResult.PublicIp,
                                PublicPort = mapResult.PublicPort
                            };
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogAlways("rendezvous", $"Automatic host port mapping error for session {sessionId}: {ex.Message}");
                            return new RendezvousPortMapResult();
                        }
                    },
                    Metadata = new Dictionary<string, object>
                    {
                        ["name"] = string.IsNullOrWhiteSpace(RemoteHostName) ? Environment.MachineName : RemoteHostName,
                        ["callsign"] = RemoteCallsign ?? "",
                        ["listen_port"] = RemoteHostPort
                    }
                }, _remoteCts.Token);
            }
            catch (Exception ex)
            {
                DebugLogger.LogAlways("rendezvous", $"Host rendezvous registration failed: {ex.Message}");
                throw;
            }

            OnPropertyChanged(nameof(RemoteHostRendezvousStatusText));
            OnPropertyChanged(nameof(RemoteHostRendezvousStatusBrush));
            OnPropertyChanged(nameof(RemoteHostWaitingLineText));
            DebugLogger.LogAlways("rendezvous", $"Registered host in rendezvous as '{hostId}'");
        }
    }

    private string GetRendezvousHostId()
    {
        string configured = (_settings?.RemoteHostId ?? RemoteRendezvousHostId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string fallback = (RemoteHostName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return Environment.MachineName;
    }

    private string BuildRendezvousClientId()
    {
        string callsign = (RemoteCallsign ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(callsign))
        {
            callsign = Environment.UserName;
        }

        return $"{callsign}-{Environment.MachineName}";
    }

    private void SyncRendezvousEndpointFromInputs()
    {
        if (_isSyncingRendezvousEndpoint)
        {
            return;
        }

        string generatedUrl = BuildRendezvousServerUrl();
        _isSyncingRendezvousEndpoint = true;
        RemoteRendezvousServerUrl = generatedUrl;
        _isSyncingRendezvousEndpoint = false;
    }

    private string BuildRendezvousServerUrl()
    {
        string serverName = (RemoteRendezvousServer ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return string.Empty;
        }

        int port = RemoteRendezvousPort;
        if (port <= 0 || port > 65535)
        {
            port = DefaultRendezvousPort;
        }

        return $"http://{serverName}:{port}";
    }

    private static void ParseRendezvousEndpoint(string value, out string serverName, out int port)
    {
        serverName = string.Empty;
        port = DefaultRendezvousPort;

        string raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (Uri.TryCreate(raw, UriKind.Absolute, out var absoluteUri))
        {
            serverName = absoluteUri.Host ?? string.Empty;
            if (absoluteUri.Port > 0)
            {
                port = absoluteUri.Port;
            }
            return;
        }

        string candidate = raw;
        if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate.Substring("http://".Length);
        }
        else if (candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate.Substring("https://".Length);
        }

        int separatorIndex = candidate.LastIndexOf(':');
        if (separatorIndex > 0 && separatorIndex == candidate.IndexOf(':'))
        {
            string hostPart = candidate.Substring(0, separatorIndex).Trim();
            string portPart = candidate.Substring(separatorIndex + 1).Trim();
            if (int.TryParse(portPart, out int parsedPort) && parsedPort > 0 && parsedPort <= 65535)
            {
                serverName = hostPart;
                port = parsedPort;
                return;
            }
        }

        serverName = candidate;
    }

    private static decimal NormalizeHoldSeconds(decimal value)
    {
        decimal clamped = Math.Max(0.5m, Math.Min(30.0m, value));
        return Math.Round(clamped * 2m, MidpointRounding.AwayFromZero) / 2m;
    }

    private static decimal ConvertHoldMsToSeconds(int holdMs)
    {
        decimal seconds = holdMs / 1000m;
        return NormalizeHoldSeconds(seconds);
    }

    private static int ConvertHoldSecondsToMs(decimal holdSeconds)
    {
        decimal normalized = NormalizeHoldSeconds(holdSeconds);
        return (int)(normalized * 1000m);
    }

    private async Task StopRemoteServicesAsync()
    {
        _remoteCts?.Cancel();
        _remoteCts?.Dispose();
        _remoteCts = null;

        lock (_relayHostSessionsLock)
        {
            _relayHostSessions.Clear();
        }

        if (_rendezvousClientSession != null)
        {
            await _rendezvousClientSession.DisposeAsync();
            _rendezvousClientSession = null;
        }

        if (_rendezvousHostSession != null)
        {
            await _rendezvousHostSession.DisposeAsync();
            _rendezvousHostSession = null;
            OnPropertyChanged(nameof(RemoteHostRendezvousStatusText));
            OnPropertyChanged(nameof(RemoteHostRendezvousStatusBrush));
            OnPropertyChanged(nameof(RemoteHostWaitingLineText));
        }

        if (_remoteClientService != null)
        {
            await _remoteClientService.DisconnectAsync();
        }

        _remoteHostTransmitModeCW = true;
        _remoteHostTransmitMode = "CW";
        _keyingController?.SetTransmitMode(_transmitSliceMonitor.IsTransmitModeCW);

        if (_remoteHostService != null)
        {
            await _remoteHostService.StopAsync();
        }

        RestoreHostSidetone();

        RemoteConnectedClients = 0;
        ResetTelemetryDisplay("host");
        if (RemoteMode == RemoteConnectionMode.Off)
        {
            RemoteStatus = "Remote mode off";
        }
    }

    private void MuteHostSidetone()
    {
        _hostSidetoneEnabledForLocalSource = false;
        _sidetoneGenerator?.Stop();
        _keyingController?.SetSidetoneEnabled(false);
    }

    private void RestoreHostSidetone()
    {
        _hostSidetoneEnabledForLocalSource = null;
        _keyingController?.SetSidetoneEnabled(true);
    }

    private void RemoteClientService_ConnectionStatusChanged(object sender, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RemoteStatus = status;

            bool connected = status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase);
            if (connected)
            {
                UpdatePaddleLabels();
            }

            bool disconnected = status.StartsWith("Disconnected", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("Connection lost", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("Host error", StringComparison.OrdinalIgnoreCase);

            if (disconnected)
            {
                _remoteHostTransmitModeCW = true;
                _remoteHostTransmitMode = "CW";
                _keyingController?.SetTransmitMode(_transmitSliceMonitor.IsTransmitModeCW);
                _sidetoneGenerator?.Stop();
                UpdatePaddleLabels();
            }
        });
    }

    private void RemoteClientService_HostIdentityChanged(object sender, RemoteHostIdentityEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RemoteConnectedHostIp = e?.HostIp ?? "";
            RemoteConnectedHostName = e?.HostName ?? "";
            UpdatePaddleLabels();
        });
    }

    private void RemoteClientService_HostTelemetryChanged(object sender, RemoteHostTelemetryEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            bool previousHostIsTransmitModeCW = _remoteHostTransmitModeCW;
            string hostTransmitMode = (e?.TransmitMode ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(hostTransmitMode))
            {
                hostTransmitMode = (e?.IsTransmitModeCW ?? true) ? "CW" : "UNKNOWN";
            }

            bool hostIsTransmitModeCW = e?.IsTransmitModeCW ?? true;
            bool modeChanged = hostIsTransmitModeCW != _remoteHostTransmitModeCW
                || !string.Equals(hostTransmitMode, _remoteHostTransmitMode, StringComparison.Ordinal);

            _remoteHostTransmitModeCW = hostIsTransmitModeCW;
            _remoteHostTransmitMode = hostTransmitMode;

            if (RemoteMode == RemoteConnectionMode.Client && _remoteClientService?.IsConnected == true)
            {
                _keyingController?.SetTransmitMode(hostIsTransmitModeCW);
            }

            if (modeChanged && !hostIsTransmitModeCW && previousHostIsTransmitModeCW)
            {
                _keyingController?.Stop();
                _sidetoneGenerator?.Stop();
            }

            if (modeChanged)
            {
                UpdatePaddleLabels();
            }

            UpdateTelemetryDisplay(
                e?.LastLagMs ?? 0,
                e?.P50LagMs ?? 0,
                e?.P95LagMs ?? 0,
                e?.MaxLagMs ?? 0,
                e?.AcceptedFramesLast60s ?? 0,
                e?.DroppedStaleFrames ?? 0,
                "host");
        });
    }

    private void RemoteHostService_HostStatusChanged(object sender, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RemoteStatus = status;
        });
    }

    private void RemoteHostService_ConnectedClientCountChanged(object sender, int count)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RemoteConnectedClients = count;
        });
    }

    private void RemoteHostService_ClientStatusesChanged(object sender, IReadOnlyList<RemoteClientStatusInfo> statuses)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var safeStatuses = statuses ?? Array.Empty<RemoteClientStatusInfo>();
            RemoteHostClientStatuses = new ObservableCollection<RemoteClientStatusInfo>(safeStatuses);
            UpdateRemoteHostClientDisplayRows(safeStatuses);
            UpdateRemoteHostTelemetrySummary(safeStatuses);
        });
    }

    private void UpdateRemoteHostClientDisplayRows(IReadOnlyList<RemoteClientStatusInfo> statuses)
    {
        var rows = new List<RemoteHostClientDisplayRow>();
        var source = statuses ?? Array.Empty<RemoteClientStatusInfo>();

        foreach (var status in source.Take(3))
        {
            rows.Add(new RemoteHostClientDisplayRow
            {
                RemoteIp = NormalizeIpForDisplay(status?.RemoteIp),
                Callsign = status?.Callsign ?? string.Empty,
                Status = status?.Status.ToString() ?? string.Empty,
                LastActive = status == null
                    ? string.Empty
                    : status.LastUpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            });
        }

        while (rows.Count < 3)
        {
            rows.Add(new RemoteHostClientDisplayRow());
        }

        RemoteHostClientDisplayRows = new ObservableCollection<RemoteHostClientDisplayRow>(rows);
    }

    private void UpdateRemoteHostTelemetrySummary(IReadOnlyList<RemoteClientStatusInfo> statuses)
    {
        var source = statuses ?? Array.Empty<RemoteClientStatusInfo>();

        var selected = source
            .Where(s => s != null)
            .OrderByDescending(s => s.Status == RemoteClientSessionStatus.Connected)
            .ThenByDescending(s => s.LastUpdatedUtc)
            .FirstOrDefault();

        if (selected == null)
        {
            ResetTelemetryDisplay("host");
            return;
        }

        string identity = !string.IsNullOrWhiteSpace(selected.Callsign)
            ? selected.Callsign
            : (selected.RemoteIp ?? "client");

        UpdateTelemetryDisplay(
            selected.LastLagMs,
            selected.P50LagMs,
            selected.P95LagMs,
            selected.MaxLagMs,
            selected.AcceptedFramesLast60s,
            selected.DroppedStaleFrames,
            identity);
    }

    private void ResetTelemetryDisplay(string identity)
    {
        string who = string.IsNullOrWhiteSpace(identity) ? "host" : identity;
        RemoteTelemetryLabel = $"Telemetry ({who}):";
        RemoteTelemetryLine1 = "last --.- ms | p50 --.- ms | p95 --.- ms";
        RemoteTelemetryLine2 = "max --.- ms | accepted 60s 0 | stale 0";
    }

    private void UpdateTelemetryDisplay(
        double lastLagMs,
        double p50LagMs,
        double p95LagMs,
        double maxLagMs,
        long acceptedFramesLast60s,
        long droppedStaleFrames,
        string identity)
    {
        string who = string.IsNullOrWhiteSpace(identity) ? "host" : identity;
        RemoteTelemetryLabel = $"Telemetry ({who}):";
        RemoteTelemetryLine1 = $"last {lastLagMs:F1} ms | p50 {p50LagMs:F1} ms | p95 {p95LagMs:F1} ms";
        RemoteTelemetryLine2 = $"max {maxLagMs:F1} ms | accepted 60s {acceptedFramesLast60s} | stale {droppedStaleFrames}";
    }

    private void RemoteHostService_PaddleStateReceived(object sender, RemotePaddleStateEventArgs e)
    {
        if (RemoteMode != RemoteConnectionMode.Host)
        {
            return;
        }

        // Remote-origin keying should never generate host sidetone.
        SetHostSidetoneForKeyingSource(isLocalInput: false);

        _keyingController?.HandlePaddleStateChange(
            e.State.LeftPaddle,
            e.State.RightPaddle,
            e.State.StraightKey,
            e.State.Ptt
        );

        Dispatcher.UIThread.Post(() =>
        {
            bool isCwMode = IsEffectiveTransmitModeCW();
            bool pttClosureState = e.State.Ptt || e.State.LeftPaddle || e.State.RightPaddle || e.State.StraightKey;
            bool isPttOnlyFrame = e.State.Ptt && !e.State.LeftPaddle && !e.State.RightPaddle && !e.State.StraightKey;

            if (!isCwMode || isPttOnlyFrame)
            {
                // In non-CW mode the left indicator represents PTT assertion.
                LeftPaddleIndicatorColor = pttClosureState ? Brushes.LimeGreen : Brushes.Black;
                LeftPaddleStateText = pttClosureState ? "ON" : "OFF";
                RightPaddleIndicatorColor = Brushes.Black;
                RightPaddleStateText = "OFF";
                return;
            }

            LeftPaddleIndicatorColor = e.State.LeftPaddle ? Brushes.LimeGreen : Brushes.Black;
            LeftPaddleStateText = e.State.LeftPaddle ? "ON" : "OFF";
            RightPaddleIndicatorColor = e.State.RightPaddle ? Brushes.LimeGreen : Brushes.Black;
            RightPaddleStateText = e.State.RightPaddle ? "ON" : "OFF";
        });
    }

    private string GetTimestamp()
    {
        // Use Environment.TickCount64 for millisecond precision timestamp
        // Reduce to 16 bits (0-65535) and format as 4-digit hex string
        long timestamp = Environment.TickCount64 % 65536;
        return timestamp.ToString("X4");
    }

    [RelayCommand]
    private async Task ToggleConnection()
    {
        if (_connectedRadio == null && !_isSidetoneOnlyMode)
        {
            if (RemoteMode == RemoteConnectionMode.Client)
            {
                _isSidetoneOnlyMode = true;
                _connectedRadio = null;
                ConnectButtonText = "Disconnect";
                HasRadioError = false;

                _keyingController?.SetRadio(null, isSidetoneOnly: true);
                _keyingController?.SetSidetoneEnabled(true);

                try
                {
                    await StartRemoteClientAsync();
                }
                catch (Exception ex)
                {
                    RadioStatus = $"Remote client connect failed: {ex.Message}";
                    RadioStatusColor = Brushes.Red;
                    HasRadioError = true;
                    ConnectButtonText = "Connect";
                    _isSidetoneOnlyMode = false;
                    return;
                }

                OpenInputDevice();
                CurrentPage = PageType.Operating;
                _currentUserSelection = null;
                UpdatePaddleLabels();
                return;
            }

            // Check if sidetone-only mode is selected
            if (SelectedRadioClient != null && SelectedRadioClient.DisplayName == SIDETONE_ONLY_OPTION)
            {
                if (RemoteMode == RemoteConnectionMode.Host)
                {
                    RadioStatus = "Remote host mode requires a radio connection";
                    RadioStatusColor = Brushes.Orange;
                    HasRadioError = true;
                    return;
                }

                // Sidetone-only mode - no radio connection
                _isSidetoneOnlyMode = true;
                _connectedRadio = null;
                ConnectButtonText = "Disconnect";
                HasRadioError = false;

                // Set keying controller to sidetone-only mode
                _keyingController?.SetRadio(null, isSidetoneOnly: true);
                _keyingController?.SetSidetoneEnabled(true);

                // Open the selected input device
                OpenInputDevice();

                // Switch to operating page
                CurrentPage = PageType.Operating;

                // SAVE PERSISTENCE: Handle sidetone-only connection
                if (_userExplicitlySelectedSidetoneOnly)
                {
                    // User explicitly selected sidetone-only - clear persisted radio preference
                    _settings.SelectedRadioSerial = null;
                    _settings.SelectedGuiClientStation = null;
                    _settings.Save();
                }
                // else: Implicit fallback to sidetone-only (no radios available) - keep existing saved preference

                // Clear current selection - this is now the baseline
                _currentUserSelection = null;

                // Update paddle labels for sidetone-only mode
                UpdatePaddleLabels();
                return;
            }

            // Connect to real radio
            if (SelectedRadioClient == null || SelectedRadioClient.Radio == null)
            {
                RadioStatus = "No radio/client selected";
                RadioStatusColor = Brushes.Orange;
                HasRadioError = true;
                return;
            }

            if (SelectedRadioClient.GuiClient == null)
            {
                RadioStatus = "No station available";
                RadioStatusColor = Brushes.Orange;
                HasRadioError = true;
                return;
            }

            _connectedRadio = SelectedRadioClient.Radio;
            uint targetClientHandle = SelectedRadioClient.GuiClient.ClientHandle;
            string targetStation = SelectedRadioClient.GuiClient.Station;

            // For WAN radios, we need to request connection from SmartLinkManager first
            if (_connectedRadio.IsWan)
            {
                if (_smartLinkManager?.WanServer == null || !_smartLinkManager.WanServer.IsConnected)
                {
                    RadioStatus = "Not connected to SmartLink server";
                    RadioStatusColor = Brushes.Red;
                    HasRadioError = true;
                    _connectedRadio = null;
                    return;
                }

                // Request connection to this radio
                RadioStatus = "Requesting SmartLink connection...";
                var result = _smartLinkManager.RequestWanConnectionAsync(_connectedRadio.Serial, 10000).Result;

                if (!result.Success)
                {
                    RadioStatus = "SmartLink connection request timed out";
                    RadioStatusColor = Brushes.Red;
                    HasRadioError = true;
                    _connectedRadio = null;
                    return;
                }

                _connectedRadio.WANConnectionHandle = result.WanConnectionHandle;

                if (string.IsNullOrEmpty(_connectedRadio.WANConnectionHandle))
                {
                    RadioStatus = "Failed to get SmartLink connection handle";
                    RadioStatusColor = Brushes.Red;
                    HasRadioError = true;
                    _connectedRadio = null;
                    return;
                }

                RadioStatus = "Connecting to radio via SmartLink...";
            }

            // Now connect to the radio (works for both LAN and WAN)
            bool connectResult = _connectedRadio.Connect();

            if (!connectResult)
            {
                RadioStatus = "Failed to connect to radio";
                RadioStatusColor = Brushes.Red;
                HasRadioError = true;
                _connectedRadio = null;
                return;
            }

            // After Connect(), the radio sends "client connected" status messages that populate
            // the ClientID (UUID) field in the GUIClient objects. Wait for that event rather than
            // using a fixed delay, so proxies with higher latency also work correctly.
            RadioStatus = "Waiting for station info...";
            GUIClient updatedGuiClient = await WaitForGUIClientReadyAsync(_connectedRadio, targetClientHandle, TimeSpan.FromSeconds(10));

            if (updatedGuiClient == null)
            {
                RadioStatus = "Failed to find station after connection";
                RadioStatusColor = Brushes.Red;
                HasRadioError = true;
                _connectedRadio.Disconnect();
                _connectedRadio = null;
                return;
            }

            string clientId = updatedGuiClient.ClientID;
            if (string.IsNullOrEmpty(clientId))
            {
                RadioStatus = "Client UUID not available - binding may fail";
                RadioStatusColor = Brushes.Orange;
                HasRadioError = true;
            }
            else
            {
                // Clear any previous errors on successful connection
                HasRadioError = false;
            }

            FinishConnection(_connectedRadio, targetClientHandle, targetStation, clientId);

            if (RemoteMode == RemoteConnectionMode.Host)
            {
                try
                {
                    await StartRemoteHostAsync();
                }
                catch (Exception ex)
                {
                    RestoreHostSidetone();
                    RadioStatus = $"Remote host start failed: {ex.Message}";
                    RadioStatusColor = Brushes.Red;
                    HasRadioError = true;
                }
            }
            else
            {
                _keyingController?.SetSidetoneEnabled(true);
            }
        }
        else
        {
            // Disconnect - clean up all keying state first

            await StopRemoteServicesAsync();

            // Stop keying controller (sends key-up if active)
            _keyingController?.Stop();
            _keyingController?.SetSidetoneEnabled(true);

            // Ensure sidetone is stopped
            _sidetoneGenerator?.Stop();

            // Reset paddle indicators to OFF state
            LeftPaddleIndicatorColor = Brushes.Black;
            RightPaddleIndicatorColor = Brushes.Black;
            LeftPaddleStateText = "OFF";
            RightPaddleStateText = "OFF";

            // Unsubscribe from radio property changes
            if (_connectedRadio != null)
            {
                _connectedRadio.PropertyChanged -= Radio_PropertyChanged;

                // Detach from transmit slice monitor
                _transmitSliceMonitor.Detach();

                // Detach from radio settings synchronizer
                _radioSettingsSynchronizer.DetachFromRadio();

                _connectedRadio.Disconnect();
                _connectedRadio = null;
            }

            // Close input device
            CloseInputDevice();

            _boundGuiClientHandle = 0;
            _isSidetoneOnlyMode = false;

            // Clear any error status on manual disconnect
            HasRadioError = false;
            ConnectButtonText = "Connect";

            // Reset selection state
            _currentUserSelection = null;

            // Update paddle labels after disconnection
            UpdatePaddleLabels();

            // Re-establish SmartLink connection if authenticated (to refresh radio list)
            if (!_isExiting && _smartLinkManager != null && _smartLinkManager.IsAuthenticated)
            {
                Task.Run(async () =>
                {
                    await _smartLinkManager.ConnectToServerAsync();
                    // Refresh radio list after SmartLink reconnects
                    Dispatcher.UIThread.Post(() => RefreshRadios());
                });
            }
            else
            {
                // Not using SmartLink, just refresh radio list immediately
                RefreshRadios();
            }

            // Switch back to setup page
            CurrentPage = PageType.Setup;
        }
    }

    [RelayCommand]
    private async Task Exit()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;

        try
        {
            Task stopRemoteTask = StopRemoteServicesAsync();
            Task completedTask = await Task.WhenAny(stopRemoteTask, Task.Delay(TimeSpan.FromSeconds(5)));
            if (completedTask != stopRemoteTask)
            {
                DebugLogger.LogAlways("remote", "Exit requested: remote service shutdown timed out after 5 seconds; continuing app shutdown");
            }
            else
            {
                await stopRemoteTask;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogAlways("remote", $"Exit requested: remote teardown failed: {ex.Message}");
        }

        // Run potentially blocking teardown work off the UI thread with a hard timeout.
        try
        {
            Task teardownTask = Task.Run(() =>
            {
                try { _keyingController?.Stop(); } catch { }
                try { _sidetoneGenerator?.Stop(); } catch { }

                if (_connectedRadio != null)
                {
                    try { _connectedRadio.PropertyChanged -= Radio_PropertyChanged; } catch { }
                    try { _transmitSliceMonitor.Detach(); } catch { }
                    try { _radioSettingsSynchronizer.DetachFromRadio(); } catch { }
                    try { _connectedRadio.Disconnect(); } catch { }
                    _connectedRadio = null;
                }

                try { _inputDeviceManager?.Dispose(); } catch { }

                try { _keepAwakeStream?.Stop(); } catch { }
                try { _keepAwakeStream?.Dispose(); } catch { }

                try { _sidetoneGenerator?.Dispose(); } catch { }
                try { _hostPortMapper?.Dispose(); } catch { }

                try
                {
                    _smartLinkManager?.CancelLogin();
                    _smartLinkManager?.WanServer?.Disconnect();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogAlways("system", $"Exit requested: SmartLink disconnect failed: {ex.Message}");
                }

                try
                {
                    API.CloseSession();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogAlways("system", $"Exit requested: API.CloseSession failed: {ex.Message}");
                }
            });

            Task teardownCompleted = await Task.WhenAny(teardownTask, Task.Delay(TimeSpan.FromSeconds(5)));
            if (teardownCompleted != teardownTask)
            {
                DebugLogger.LogAlways("system", "Exit requested: local teardown timed out after 5 seconds; forcing app shutdown");
            }
            else
            {
                await teardownTask;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogAlways("system", $"Exit requested: local teardown failed: {ex.Message}");
        }

        var desktopLifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (desktopLifetime != null)
        {
            // Fail-safe: force process exit if the UI lifetime shutdown path stalls.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                Environment.Exit(0);
            });

            desktopLifetime.Shutdown(0);
            return;
        }

        Environment.Exit(0);
    }

    [RelayCommand]
    private void OpenDocumentation()
    {
        UrlHelper.OpenUrl("https://github.com/NetKeyer/NetKeyer#usage");
    }

    [RelayCommand]
    private async Task ShowAbout()
    {
        var aboutWindow = new Views.AboutWindow();

        // Get the main window
        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (mainWindow != null)
        {
            await aboutWindow.ShowDialog(mainWindow);
        }
    }

    [RelayCommand]
    private void OpenDebugLog()
    {
        var logFilePath = Helpers.DebugLogger.LogFilePath;
        var logFolder = System.IO.Path.GetDirectoryName(logFilePath);

        if (!string.IsNullOrEmpty(logFolder))
        {
            UrlHelper.OpenFolder(logFolder);
        }
    }


    partial void OnCwSpeedChanged(int value)
    {
        if (!_loadingSettings && _settings != null && RemoteMode == RemoteConnectionMode.Client)
        {
            _settings.RemoteClientCwSpeed = value;
            _settings.Save();
        }

        // Update sidetone generator WPM for ramp calculations
        _sidetoneGenerator?.SetWpm(value);

        // Update keying controller WPM for timing calculations
        _keyingController?.SetSpeed(value);

        // Sync to radio
        _radioSettingsSynchronizer?.SyncCwSpeedToRadio(value);
    }

    partial void OnCwPitchChanged(int value)
    {
        if (!_loadingSettings && _settings != null && RemoteMode == RemoteConnectionMode.Client)
        {
            _settings.RemoteClientCwPitch = value;
            _settings.Save();
        }

        // Update sidetone frequency
        _sidetoneGenerator?.SetFrequency(value);

        // Sync to radio
        _radioSettingsSynchronizer?.SyncCwPitchToRadio(value);
    }

    partial void OnSidetoneVolumeChanged(int value)
    {
        if (!_loadingSettings && _settings != null && RemoteMode == RemoteConnectionMode.Client)
        {
            _settings.RemoteClientSidetoneVolume = value;
            _settings.Save();
        }

        // Update sidetone volume
        if (_keyingController != null)
        {
            _keyingController.SetSidetoneVolume(value);
        }
        else
        {
            _sidetoneGenerator?.SetVolume(value);
        }

        // Sync to radio
        _radioSettingsSynchronizer?.SyncSidetoneVolumeToRadio(value);
    }

    partial void OnIsIambicModeBChanged(bool value)
    {
        // Update keying controller mode
        _keyingController?.SetKeyingMode(IsIambicMode, value);

        // Sync to radio
        _radioSettingsSynchronizer?.SyncIambicModeBToRadio(value);

        // Update mode display when iambic type changes
        UpdatePaddleLabels();
    }

    partial void OnSwapPaddlesChanged(bool value)
    {
        // Update input device manager
        _inputDeviceManager?.SetSwapPaddles(value);

        // Sync to radio
        _radioSettingsSynchronizer?.SyncSwapPaddlesToRadio(value);
    }

    private void OnRadioAdded(Radio radio)
    {
        // Subscribe to GUIClientAdded event for LAN radios to handle delayed GUI client population
        if (!radio.IsWan)
        {
            radio.GUIClientAdded += Radio_GUIClientAdded;
        }

        // Refresh the radio list when a new radio is discovered
        RefreshRadios();
    }

    private void OnRadioRemoved(Radio radio)
    {
        // Unsubscribe from GUIClientAdded event
        if (!radio.IsWan)
        {
            radio.GUIClientAdded -= Radio_GUIClientAdded;
        }

        // Refresh the radio list when a radio is removed
        RefreshRadios();

        if (_connectedRadio == radio)
        {
            _ = StopRemoteServicesAsync();
            _connectedRadio = null;
            RadioStatus = "Disconnected (radio removed)";
            RadioStatusColor = Brushes.Red;
            HasRadioError = true;
            ConnectButtonText = "Connect";
        }
    }

    private void FinishConnection(Radio radio, uint clientHandle, string targetStation, string clientId)
    {
        _connectedRadio = radio;
        _connectedRadio.BindGUIClient(clientId);
        _boundGuiClientHandle = clientHandle;
        ConnectButtonText = "Disconnect";

        _keyingController?.Dispose();
        _keyingController = new KeyingController(_sidetoneGenerator);
        _keyingController.Initialize(
            _boundGuiClientHandle,
            GetTimestamp,
            (state, timestamp, handle) =>
            {
                if (_connectedRadio != null)
                    _connectedRadio.CWKey(state, timestamp, handle);
            }
        );
        _keyingController.SetKeyingMode(IsIambicMode, IsIambicModeB);
        _keyingController.SetSpeed(CwSpeed);
        _keyingController.SetSidetoneVolume(SidetoneVolume);

        _connectedRadio.PropertyChanged += Radio_PropertyChanged;
        _transmitSliceMonitor.AttachToRadio(_connectedRadio, _boundGuiClientHandle);
        _keyingController?.SetRadio(_connectedRadio, isSidetoneOnly: false);
        _keyingController?.SetTransmitMode(_transmitSliceMonitor.IsTransmitModeCW);

        _radioSettingsSynchronizer.AttachToRadio(_connectedRadio);
        try
        {
            _radioSettingsSynchronizer.ApplyInitialSettingsFromRadio();
        }
        catch (Exception ex)
        {
            RadioStatus = ex.Message;
            RadioStatusColor = Brushes.Orange;
            HasRadioError = true;
        }

        _settings.SelectedRadioSerial = _connectedRadio.Serial;
        _settings.SelectedGuiClientStation = targetStation;
        _settings.Save();

        _currentUserSelection = null;
        OpenInputDevice();
        CurrentPage = PageType.Operating;
        UpdatePaddleLabels();
    }

    [RelayCommand]
    private async Task ConnectByIp()
    {
        var dialog = new Views.ConnectByIpDialog();
        dialog.SetInitialIp(_settings.LastManualRadioIp ?? "");

        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow == null) return;

        var showTask = dialog.ShowDialog(mainWindow);

        // Phase 1: wait for user to enter IP and click Connect
        string ipStr = await dialog.WaitForConnectAsync();
        if (ipStr == null) { await showTask; return; }

        if (!IPAddress.TryParse(ipStr, out var ip))
        {
            dialog.ShowError("Invalid IP address");
            await showTask;
            return;
        }

        _settings.LastManualRadioIp = ipStr;
        _settings.Save();

        var radio = API.CreateManualRadio(ip);

        // Subscribe to all three client events before Connect() so nothing is missed
        Radio.GUIClientAddedEventHandler onClientAdded = client => dialog.AddGuiClient(client);
        Radio.GUIClientUpdatedEventHandler onClientUpdated = client => dialog.UpdateGuiClient(client);
        Radio.GUIClientRemovedEventHandler onClientRemoved = client => dialog.RemoveGuiClient(client);
        radio.GUIClientAdded += onClientAdded;
        radio.GUIClientUpdated += onClientUpdated;
        radio.GUIClientRemoved += onClientRemoved;

        dialog.UpdateStatus("Connecting...");
        bool connectResult = radio.Connect();

        if (!connectResult)
        {
            radio.GUIClientAdded -= onClientAdded;
            radio.GUIClientUpdated -= onClientUpdated;
            radio.GUIClientRemoved -= onClientRemoved;
            dialog.ShowError("Failed to connect to radio");
            await showTask;
            return;
        }

        // Transition to Phase 2 immediately — no fixed delay.
        // Event handlers stay active and keep the list current until the user selects.
        dialog.TransitionToPhase2();

        // Phase 2: user picks a station (list grows/updates dynamically as clients arrive)
        GUIClient selectedClient = await dialog.WaitForClientSelectionAsync();
        radio.GUIClientAdded -= onClientAdded;
        radio.GUIClientUpdated -= onClientUpdated;
        radio.GUIClientRemoved -= onClientRemoved;
        await showTask;

        if (selectedClient == null)
        {
            radio.Disconnect();
            return;
        }

        // Wait for ClientID to be populated (likely already done; returns immediately if so)
        RadioStatus = "Waiting for station info...";
        HasRadioError = false;
        GUIClient updatedClient = await WaitForGUIClientReadyAsync(
            radio, selectedClient.ClientHandle, TimeSpan.FromSeconds(10));

        if (updatedClient == null || string.IsNullOrEmpty(updatedClient.ClientID))
        {
            RadioStatus = "Client UUID not available";
            RadioStatusColor = Brushes.Red;
            HasRadioError = true;
            radio.Disconnect();
            return;
        }

        FinishConnection(radio, updatedClient.ClientHandle, selectedClient.Station, updatedClient.ClientID);

        if (RemoteMode == RemoteConnectionMode.Host)
        {
            try
            {
                await StartRemoteHostAsync();
            }
            catch (Exception ex)
            {
                    RestoreHostSidetone();
                RadioStatus = $"Remote host start failed: {ex.Message}";
                RadioStatusColor = Brushes.Red;
                HasRadioError = true;
            }
        }
    }

    // Waits until the GUIClient for clientHandle has a non-empty ClientID, or the timeout elapses.
    // Returns the client (possibly with empty ClientID on timeout) or null if not found at all.
    private static async Task<GUIClient?> WaitForGUIClientReadyAsync(Radio radio, uint clientHandle, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<GUIClient?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Radio.GUIClientUpdatedEventHandler onUpdated = g =>
        {
            if (g.ClientHandle == clientHandle && !string.IsNullOrEmpty(g.ClientID))
                tcs.TrySetResult(g);
        };
        Radio.GUIClientAddedEventHandler onAdded = g =>
        {
            if (g.ClientHandle == clientHandle && !string.IsNullOrEmpty(g.ClientID))
                tcs.TrySetResult(g);
        };

        radio.GUIClientUpdated += onUpdated;
        radio.GUIClientAdded += onAdded;
        try
        {
            // Check immediately in case the event fired before we subscribed.
            var existing = radio.FindGUIClientByClientHandle(clientHandle);
            if (existing != null && !string.IsNullOrEmpty(existing.ClientID))
                tcs.TrySetResult(existing);

            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() => tcs.TrySetResult(radio.FindGUIClientByClientHandle(clientHandle)));

            return await tcs.Task;
        }
        finally
        {
            radio.GUIClientUpdated -= onUpdated;
            radio.GUIClientAdded -= onAdded;
        }
    }

    private void Radio_GUIClientAdded(GUIClient guiClient)
    {
        // When a GUI client is added to a LAN radio, refresh the radio list
        // and force restoration of saved preference if we're not on the right station
        Dispatcher.UIThread.Post(() =>
        {
            RefreshRadios();

            // After refresh, explicitly check if we should restore saved preference
            // This handles the case where Priority 1 might have selected something else
            if (_settings != null && !string.IsNullOrEmpty(_settings.SelectedRadioSerial))
            {
                // Only restore if we're currently on sidetone-only or a different station
                bool shouldRestore = SelectedRadioClient == null ||
                                   SelectedRadioClient.DisplayName == SIDETONE_ONLY_OPTION ||
                                   SelectedRadioClient.Radio?.Serial != _settings.SelectedRadioSerial ||
                                   SelectedRadioClient.GuiClient?.Station != _settings.SelectedGuiClientStation;

                if (shouldRestore)
                {
                    _loadingSettings = true;
                    var savedSelection = RadioClientSelections.FirstOrDefault(s =>
                        s.Radio?.Serial == _settings.SelectedRadioSerial &&
                        s.GuiClient?.Station == _settings.SelectedGuiClientStation);

                    if (savedSelection != null)
                    {
                        SelectedRadioClient = savedSelection;
                        // Don't clear current selection here - this is still a programmatic change
                    }
                    _loadingSettings = false;
                }
            }
        });
    }


    private void TransmitSliceMonitor_ModeChanged(object sender, TransmitModeChangedEventArgs e)
    {
        if (RemoteMode == RemoteConnectionMode.Host)
        {
            _remoteHostService?.SetTransmitMode(e.IsTransmitModeCW, e.TransmitMode);
        }

        // In remote-client mode, host telemetry controls the effective transmit mode when connected.
        if (!(RemoteMode == RemoteConnectionMode.Client && _remoteClientService?.IsConnected == true))
        {
            _keyingController?.SetTransmitMode(e.IsTransmitModeCW);
        }

        // Update UI when transmit mode changes
        Dispatcher.UIThread.Post(() => UpdatePaddleLabels());
    }

    private bool ShouldUseRemoteClientPttBehavior()
    {
        return RemoteMode == RemoteConnectionMode.Client
            && _remoteClientService?.IsConnected == true
            && !_remoteHostTransmitModeCW;
    }

    private bool IsEffectiveTransmitModeCW()
    {
        if (RemoteMode == RemoteConnectionMode.Client && _remoteClientService?.IsConnected == true)
        {
            return _remoteHostTransmitModeCW;
        }

        if (_isSidetoneOnlyMode)
        {
            return true;
        }

        return _transmitSliceMonitor.IsTransmitModeCW;
    }

    private void SetHostSidetoneForKeyingSource(bool isLocalInput)
    {
        if (RemoteMode != RemoteConnectionMode.Host || _connectedRadio == null)
        {
            _hostSidetoneEnabledForLocalSource = null;
            return;
        }

        bool localDeviceOpen = _inputDeviceManager?.IsDeviceOpen == true;
        bool enableSidetone = isLocalInput && localDeviceOpen;

        if (_hostSidetoneEnabledForLocalSource.HasValue
            && _hostSidetoneEnabledForLocalSource.Value == enableSidetone)
        {
            return;
        }

        _hostSidetoneEnabledForLocalSource = enableSidetone;

        _keyingController?.SetSidetoneEnabled(enableSidetone);
        if (!enableSidetone)
        {
            _sidetoneGenerator?.Stop();
        }
    }

    private bool ShouldShowHostCwSettings()
    {
        if (RemoteMode != RemoteConnectionMode.Host || _connectedRadio == null)
        {
            return true;
        }

        // In host mode, CW settings are only useful when a local keying input is actively connected.
        return _inputDeviceManager?.IsDeviceOpen == true;
    }

    private void UpdatePaddleLabels()
    {
        // Build combined mode display string
        string modeStr;

        if (_connectedRadio == null && !_isSidetoneOnlyMode)
        {
            // Disconnected
            modeStr = "Disconnected";
            ConnectedRadioDisplay = "";
            LeftPaddleLabelText = "Left Paddle";
            RightPaddleVisible = true;
            ModeInstructions = "";
            CwSettingsVisible = true;
        }
        else if (_isSidetoneOnlyMode)
        {
            bool isRemoteClientConnected = RemoteMode == RemoteConnectionMode.Client
                && _remoteClientService?.IsConnected == true;

            if (isRemoteClientConnected)
            {
                // Remote client uses sidetone-only locally, but the mode display should reflect host mode.
                string hostIdentity = !string.IsNullOrWhiteSpace(RemoteConnectedHostName)
                    ? RemoteConnectedHostName
                    : (!string.IsNullOrWhiteSpace(RemoteConnectedHostIp)
                        ? RemoteConnectedHostIp
                        : (!string.IsNullOrWhiteSpace(RemoteClientHost) ? RemoteClientHost : "Remote Host"));
                ConnectedRadioDisplay = hostIdentity;

                if (!_remoteHostTransmitModeCW)
                {
                    string radioMode = string.IsNullOrWhiteSpace(_remoteHostTransmitMode) ? "Unknown" : _remoteHostTransmitMode;
                    modeStr = $"{radioMode} (PTT)";
                    LeftPaddleLabelText = "PTT";
                    RightPaddleVisible = false;
                    CwSettingsVisible = false;
                    ModeInstructions = "Remote host is not in CW mode; key input is sent as PTT";
                }
                else
                {
                    CwSettingsVisible = true;
                    ModeInstructions = "";

                    if (IsIambicMode)
                    {
                        string iambicType = IsIambicModeB ? "Mode B" : "Mode A";
                        modeStr = $"CW (Iambic {iambicType})";
                        LeftPaddleLabelText = "Left Paddle";
                        RightPaddleVisible = true;
                    }
                    else
                    {
                        modeStr = "CW (Straight Key)";
                        LeftPaddleLabelText = "Key";
                        RightPaddleVisible = false;
                    }
                }
            }
            else
            {
                // Local sidetone-only mode (no remote connection)
                bool isRemoteClientMode = RemoteMode == RemoteConnectionMode.Client;
                modeStr = isRemoteClientMode ? "Remote Mode" : "Sidetone Only";
                ConnectedRadioDisplay = isRemoteClientMode ? "Remote Mode" : "";
                CwSettingsVisible = true;
                ModeInstructions = "";

                if (IsIambicMode)
                {
                    LeftPaddleLabelText = "Left Paddle";
                    RightPaddleVisible = true;
                }
                else
                {
                    LeftPaddleLabelText = "Key";
                    RightPaddleVisible = false;
                }
            }
        }
        else if (!IsEffectiveTransmitModeCW())
        {
            // PTT mode (non-CW radio modes)
            string radioMode;
            if (RemoteMode == RemoteConnectionMode.Client && _remoteClientService?.IsConnected == true)
            {
                radioMode = string.IsNullOrWhiteSpace(_remoteHostTransmitMode) ? "Unknown" : _remoteHostTransmitMode;
            }
            else
            {
                var txSlice = _transmitSliceMonitor.TransmitSlice;
                radioMode = txSlice?.DemodMode?.ToUpper() ?? "Unknown";
            }
            modeStr = $"{radioMode} (PTT)";

            ConnectedRadioDisplay = $"{_connectedRadio.Nickname} ({_connectedRadio.Model})";
            LeftPaddleLabelText = "PTT";
            RightPaddleVisible = false;
            CwSettingsVisible = false;
            ModeInstructions = RemoteMode == RemoteConnectionMode.Client
                ? "Remote host is not in CW mode; key input is sent as PTT"
                : "Switch radio to CW mode to activate CW keying";
        }
        else
        {
            // CW mode
            ConnectedRadioDisplay = $"{_connectedRadio.Nickname} ({_connectedRadio.Model})";

            if (IsIambicMode)
            {
                string iambicType = IsIambicModeB ? "Mode B" : "Mode A";
                modeStr = $"CW (Iambic {iambicType})";
                LeftPaddleLabelText = "Left Paddle";
                RightPaddleVisible = true;
            }
            else
            {
                modeStr = "CW (Straight Key)";
                LeftPaddleLabelText = "Key";
                RightPaddleVisible = false;
            }

            CwSettingsVisible = ShouldShowHostCwSettings();
            ModeInstructions = "";
        }

        ModeDisplay = modeStr;
    }

    private void Radio_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        // This is now mainly handled by RadioSettingsSynchronizer
        // Keep this for any non-settings radio property changes if needed in the future
    }

    private void RadioSettingsSynchronizer_SettingChanged(object sender, RadioSettingChangedEventArgs e)
    {
        // Update UI properties from radio settings changes
        switch (e.PropertyName)
        {
            case "CWSpeed":
                if (e.Value is int cwSpeed && CwSpeed != cwSpeed)
                    CwSpeed = cwSpeed;
                break;

            case "CWPitch":
                if (e.Value is int cwPitch && CwPitch != cwPitch)
                    CwPitch = cwPitch;
                break;

            case "TXCWMonitorGain":
                if (e.Value is int sidetoneVolume && SidetoneVolume != sidetoneVolume)
                    SidetoneVolume = sidetoneVolume;
                break;

            case "CWIambic":
                if (e.Value is bool cwIambic && IsIambicMode != cwIambic)
                    IsIambicMode = cwIambic;
                break;

            case "CWIambicModeB":
                if (e.Value is bool cwIambicModeB && IsIambicModeB != cwIambicModeB)
                    IsIambicModeB = cwIambicModeB;
                break;

            case "CWSwapPaddles":
                if (e.Value is bool swapPaddles && SwapPaddles != swapPaddles)
                    SwapPaddles = swapPaddles;
                break;
        }
    }

    #region SmartLink Event Handlers

    private void SmartLinkManager_StatusChanged(object sender, SmartLinkStatusChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SmartLinkStatus = e.Status;
            SmartLinkAuthenticated = e.IsAuthenticated;
            SmartLinkButtonText = e.ButtonText;
        });
    }

    private void SmartLinkManager_WanRadiosDiscovered(object sender, WanRadiosDiscoveredEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Add SmartLink radios to the radio list
            // They will be marked with IsWan = true
            foreach (var radio in e.Radios)
            {
                lock (radio.GuiClientsLockObj)
                {
                    if (radio.GuiClients != null && radio.GuiClients.Count > 0)
                    {
                        foreach (var guiClient in radio.GuiClients)
                        {
                            var selection = new RadioClientSelection
                            {
                                Radio = radio,
                                GuiClient = guiClient,
                                DisplayName = $"[SmartLink] {radio.Nickname} ({radio.Model}) - {guiClient.Station} [{guiClient.Program}]"
                            };

                            // Check if already in list
                            var existing = RadioClientSelections.FirstOrDefault(s =>
                                s.Radio?.Serial == radio.Serial &&
                                s.GuiClient?.Station == guiClient.Station);

                            if (existing == null)
                            {
                                RadioClientSelections.Add(selection);
                            }
                        }
                    }
                    else
                    {
                        var selection = new RadioClientSelection
                        {
                            Radio = radio,
                            GuiClient = null,
                            DisplayName = $"[SmartLink] {radio.Nickname} ({radio.Model}) - No Stations"
                        };

                        var existing = RadioClientSelections.FirstOrDefault(s =>
                            s.Radio?.Serial == radio.Serial && s.GuiClient == null);

                        if (existing == null)
                        {
                            RadioClientSelections.Add(selection);
                        }
                    }
                }
            }

            // Refresh to include new SmartLink radios, then explicitly restore saved preference
            RefreshRadios();

            // After refresh, explicitly check if we should restore saved preference
            // This handles the case where Priority 1 might have selected something else
            if (_settings != null && !string.IsNullOrEmpty(_settings.SelectedRadioSerial))
            {
                // Only restore if we're currently on sidetone-only or a different station
                bool shouldRestore = SelectedRadioClient == null ||
                                   SelectedRadioClient.DisplayName == SIDETONE_ONLY_OPTION ||
                                   SelectedRadioClient.Radio?.Serial != _settings.SelectedRadioSerial ||
                                   SelectedRadioClient.GuiClient?.Station != _settings.SelectedGuiClientStation;

                if (shouldRestore)
                {
                    _loadingSettings = true;
                    var savedSelection = RadioClientSelections.FirstOrDefault(s =>
                        s.Radio?.Serial == _settings.SelectedRadioSerial &&
                        s.GuiClient?.Station == _settings.SelectedGuiClientStation);

                    if (savedSelection != null)
                    {
                        SelectedRadioClient = savedSelection;
                        // Don't clear current selection here - this is still a programmatic change
                    }
                    _loadingSettings = false;
                }
            }
        });
    }

    private void SmartLinkManager_RegistrationInvalid(object sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SmartLinkStatus = "Registration invalid - please log in again";
        });
    }

    private void SmartLinkManager_WanRadioConnectReady(object sender, WanConnectionReadyEventArgs e)
    {
        // This event is handled internally by SmartLinkManager
        // We don't need to do anything here in the ViewModel
    }

    [RelayCommand]
    private async Task ToggleSmartLink()
    {
        if (!SmartLinkAvailable)
        {
            SmartLinkStatus = "SmartLink not available - no client_id configured";
            return;
        }

        if (SmartLinkAuthenticated)
        {
            // Logout
            _smartLinkManager?.Logout();

            // Clear SmartLink radios from list
            var smartLinkRadios = RadioClientSelections.Where(s => s.Radio?.IsWan == true).ToList();
            foreach (var radio in smartLinkRadios)
            {
                RadioClientSelections.Remove(radio);
            }
        }
        else
        {
            // Show login dialog
            await ShowSmartLinkLoginDialog();
        }
    }

    private async Task ShowSmartLinkLoginDialog()
    {
        var loginDialog = new Views.SmartLinkLoginDialog();

        // Set the Remember Me checkbox to the current setting value
        loginDialog.SetRememberMe(_settings.RememberMeSmartLink);

        // Get the main window
        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (mainWindow == null)
        {
            SmartLinkStatus = "Failed to show login dialog";
            return;
        }

        // Start the login task before showing dialog (it will open browser)
        SmartLinkStatus = "Authenticating...";
        var loginTask = _smartLinkManager.LoginAsync(loginDialog.CancellationToken);

        // Show dialog (blocks until user cancels or login completes)
        _ = loginTask.ContinueWith(t =>
        {
            // When login completes (success or failure), close the dialog
            if (t.IsCompletedSuccessfully && t.Result)
            {
                loginDialog.CompleteSuccessfully();
            }
            else if (t.IsFaulted)
            {
                loginDialog.ShowError(t.Exception?.InnerException?.Message ?? "Login failed");
            }
            // If cancelled, dialog will close via cancel button
        }, System.Threading.Tasks.TaskScheduler.Default);

        await loginDialog.ShowDialog(mainWindow);

        // Update and save the Remember Me preference
        _settings.RememberMeSmartLink = loginDialog.RememberMe;
        _settings.Save();

        if (loginDialog.WasCancelled)
        {
            _smartLinkManager.CancelLogin();
            SmartLinkStatus = "Login cancelled";
        }
        else
        {
            // Wait for the login task to finish if not already
            try
            {
                var success = await loginTask;
                if (!success)
                {
                    SmartLinkStatus = "Login failed";
                }
            }
            catch (OperationCanceledException)
            {
                SmartLinkStatus = "Login cancelled";
            }
            catch (Exception ex)
            {
                SmartLinkStatus = $"Login failed: {ex.Message}";
            }
        }
    }

    #endregion
}

