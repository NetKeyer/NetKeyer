using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flex.Smoothlake.FlexLib;
using NetKeyer.Audio;
using NetKeyer.Keying;
using NetKeyer.Midi;
using NetKeyer.Models;
using NetKeyer.Services;
using NetKeyer.SmartLink;

namespace NetKeyer.ViewModels;

public enum InputDeviceType
{
    Serial,
    MIDI
}

// Debug flags
file static class DebugFlags
{
    public const bool DEBUG_KEYER = false; // Set to true to enable iambic keyer debug logging
    public const bool DEBUG_MIDI_HANDLER = false; // Set to true to enable MIDI handler debug logging
    public const bool DEBUG_SLICE_MODE = false; // Set to true to enable transmit slice mode debug logging
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

public partial class MainWindowViewModel : ViewModelBase
{
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

    // Sidetone generator
    private ISidetoneGenerator _sidetoneGenerator;

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
    private string _modeDisplay = "Disconnected";  // Combined mode string

    [ObservableProperty]
    private string _modeInstructions = "";  // Instructions for mode switching

    [ObservableProperty]
    private bool _cwSettingsVisible = true;  // Control CW settings visibility

    [ObservableProperty]
    private string _leftPaddleLabelText = "Left Paddle";  // Dynamic left label

    [ObservableProperty]
    private bool _rightPaddleVisible = true;  // Hide right paddle when appropriate

    public MainWindowViewModel()
    {
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
        _loadingSettings = false;

        // Initial discovery
        RefreshRadios();
        RefreshSerialPorts();
        RefreshMidiDevices();

        // Initialize sidetone generator (platform-specific)
        try
        {
            _sidetoneGenerator = SidetoneGeneratorFactory.Create();
            _sidetoneGenerator.SetFrequency(CwPitch);
            _sidetoneGenerator.SetVolume(SidetoneVolume);
            _sidetoneGenerator.SetWpm(CwSpeed);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not initialize sidetone generator: {ex.Message}");
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
        _keyingController.EnableDebugLogging(DebugFlags.DEBUG_KEYER);
        _keyingController.SetSpeed(CwSpeed);

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

    partial void OnSelectedRadioClientChanged(RadioClientSelection value)
    {
        if (!_loadingSettings && _settings != null && value != null)
        {
            // Check if user is explicitly selecting sidetone-only
            bool isSidetoneOnly = (value.DisplayName == SIDETONE_ONLY_OPTION);

            if (isSidetoneOnly)
            {
                // User explicitly selected sidetone-only
                _userExplicitlySelectedSidetoneOnly = true;

                // Clear persisted radio settings
                _settings.SelectedRadioSerial = null;
                _settings.SelectedGuiClientStation = null;
                _settings.Save();
            }
            else
            {
                // User selected a real radio - save it
                _userExplicitlySelectedSidetoneOnly = false;
                _settings.SelectedRadioSerial = value.Radio?.Serial;
                _settings.SelectedGuiClientStation = value.GuiClient?.Station;
                _settings.Save();
            }
        }
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
        // Clear current list
        RadioClientSelections.Clear();

        // Always add sidetone-only option first
        RadioClientSelections.Add(new RadioClientSelection
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
                        RadioClientSelections.Add(selection);
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
                    RadioClientSelections.Add(selection);
                }
            }
        }

        // If SmartLink is authenticated, ensure connection and request radio list
        if (_smartLinkManager != null && _smartLinkManager.IsAuthenticated)
        {
            // Reconnect to SmartLink server if needed (will trigger radio list refresh)
            Task.Run(async () =>
            {
                await _smartLinkManager.ConnectToServerAsync();
            });
        }

        // Restore previously selected radio/client if available
        _loadingSettings = true;

        // Try to restore saved selection
        RadioClientSelection restoredSelection = null;
        if (_settings != null && !string.IsNullOrEmpty(_settings.SelectedRadioSerial))
        {
            restoredSelection = RadioClientSelections.FirstOrDefault(s =>
                s.Radio?.Serial == _settings.SelectedRadioSerial &&
                s.GuiClient?.Station == _settings.SelectedGuiClientStation);
        }

        if (restoredSelection != null)
        {
            // Saved radio is available - select it
            SelectedRadioClient = restoredSelection;
        }
        else
        {
            // Saved radio not available OR no saved selection - default to sidetone-only
            var sidetoneOption = RadioClientSelections.FirstOrDefault(s =>
                s.DisplayName == SIDETONE_ONLY_OPTION);

            if (sidetoneOption != null)
            {
                SelectedRadioClient = sidetoneOption;

                // This is an implicit fallback, not an explicit user choice
                _userExplicitlySelectedSidetoneOnly = false;
            }
        }

        _loadingSettings = false;
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

            // Update indicators to show current state (but don't trigger keying) - only for serial
            if (InputType == InputDeviceType.Serial)
            {
                UpdateIndicatorsFromSerial();
            }
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
        // Swap is now handled in InputDeviceManager
        bool leftPaddleState = e.LeftPaddle;
        bool rightPaddleState = e.RightPaddle;
        bool straightKeyState = e.StraightKey;
        bool pttState = e.PTT;

        if (DebugFlags.DEBUG_MIDI_HANDLER)
            Console.WriteLine($"[InputDeviceManager_PaddleStateChanged] Received event: L={leftPaddleState} R={rightPaddleState} SK={straightKeyState} PTT={pttState}");

        // Update indicators
        Dispatcher.UIThread.Post(() =>
        {
            // In straight key mode, either paddle should trigger the key indicator
            bool keyState = IsIambicMode ? leftPaddleState : (leftPaddleState || rightPaddleState);

            LeftPaddleIndicatorColor = keyState ? Brushes.LimeGreen : Brushes.Black;
            LeftPaddleStateText = keyState ? "ON" : "OFF";
            RightPaddleIndicatorColor = rightPaddleState ? Brushes.LimeGreen : Brushes.Black;
            RightPaddleStateText = rightPaddleState ? "ON" : "OFF";
        });

        // Delegate keying logic to KeyingController
        _keyingController?.HandlePaddleStateChange(leftPaddleState, rightPaddleState, straightKeyState, pttState);
    }

    private void UpdateIndicatorsFromSerial()
    {
        if (_inputDeviceManager?.CurrentDeviceType != InputDeviceType.Serial)
            return;

        try
        {
            // HaliKey v1: CTS (left) + DCD (right)
            var (leftPaddleState, rightPaddleState) = _inputDeviceManager.GetCurrentSerialPinStates();

            // Apply swap if enabled
            if (SwapPaddles)
            {
                (leftPaddleState, rightPaddleState) = (rightPaddleState, leftPaddleState);
            }

            // Update indicators only
            // In straight key mode, either paddle should trigger the key indicator
            bool keyState = IsIambicMode ? leftPaddleState : (leftPaddleState || rightPaddleState);

            LeftPaddleIndicatorColor = keyState ? Brushes.LimeGreen : Brushes.Black;
            LeftPaddleStateText = keyState ? "ON" : "OFF";
            RightPaddleIndicatorColor = rightPaddleState ? Brushes.LimeGreen : Brushes.Black;
            RightPaddleStateText = rightPaddleState ? "ON" : "OFF";
        }
        catch { }
    }

    private string GetTimestamp()
    {
        // Use Environment.TickCount64 for millisecond precision timestamp
        // Reduce to 16 bits (0-65535) and format as 4-digit hex string
        long timestamp = Environment.TickCount64 % 65536;
        return timestamp.ToString("X4");
    }

    [RelayCommand]
    private void ToggleConnection()
    {
        if (_connectedRadio == null && !_isSidetoneOnlyMode)
        {
            // Check if sidetone-only mode is selected
            if (SelectedRadioClient != null && SelectedRadioClient.DisplayName == SIDETONE_ONLY_OPTION)
            {
                // Sidetone-only mode - no radio connection
                _isSidetoneOnlyMode = true;
                _connectedRadio = null;
                ConnectButtonText = "Disconnect";
                HasRadioError = false;

                // Set keying controller to sidetone-only mode
                _keyingController?.SetRadio(null, isSidetoneOnly: true);

                // Open the selected input device
                OpenInputDevice();

                // Switch to operating page
                CurrentPage = PageType.Operating;

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
            // the ClientID (UUID) field in the GUIClient objects. Wait a moment for these to arrive.
            Thread.Sleep(500);

            // Look up the updated GUIClient from the connected radio's GuiClients list
            // This will now have the ClientID (UUID) populated
            GUIClient updatedGuiClient = _connectedRadio.FindGUIClientByClientHandle(targetClientHandle);

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

            // Bind to the selected station using its UUID
            _connectedRadio.BindGUIClient(clientId);
            _boundGuiClientHandle = targetClientHandle;
            ConnectButtonText = "Disconnect";

            // Reinitialize keying controller with the correct radio client handle
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
            _keyingController.EnableDebugLogging(DebugFlags.DEBUG_KEYER);
            _keyingController.SetSpeed(CwSpeed);

            // Subscribe to radio property changes
            _connectedRadio.PropertyChanged += Radio_PropertyChanged;

            // Subscribe to transmit slice property changes and update initial mode
            _transmitSliceMonitor.AttachToRadio(_connectedRadio, _boundGuiClientHandle);

            // Attach keying controller to radio
            _keyingController?.SetRadio(_connectedRadio, isSidetoneOnly: false);
            _keyingController?.SetTransmitMode(_transmitSliceMonitor.IsTransmitModeCW);

            // Attach radio settings synchronizer and apply initial settings
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

            // Open the selected input device
            OpenInputDevice();

            // Switch to operating page
            CurrentPage = PageType.Operating;

            // Update paddle labels after connection
            UpdatePaddleLabels();
        }
        else
        {
            // Disconnect - clean up all keying state first

            // Stop keying controller (sends key-up if active)
            _keyingController?.Stop();

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

            // Update paddle labels after disconnection
            UpdatePaddleLabels();

            // Re-establish SmartLink connection if authenticated (to refresh radio list)
            if (_smartLinkManager != null && _smartLinkManager.IsAuthenticated)
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
    private void Exit()
    {
        // Clean up all keying state before exit
        _keyingController?.Stop();
        _sidetoneGenerator?.Stop();

        if (_connectedRadio != null)
        {
            _connectedRadio.Disconnect();
        }

        // Close input device
        _inputDeviceManager?.Dispose();

        // Dispose sidetone generator
        _sidetoneGenerator?.Dispose();

        API.CloseSession();
        Environment.Exit(0);
    }


    partial void OnCwSpeedChanged(int value)
    {
        // Update sidetone generator WPM for ramp calculations
        _sidetoneGenerator?.SetWpm(value);

        // Update keying controller WPM for timing calculations
        _keyingController?.SetSpeed(value);

        // Sync to radio
        _radioSettingsSynchronizer?.SyncCwSpeedToRadio(value);
    }

    partial void OnCwPitchChanged(int value)
    {
        // Update sidetone frequency
        _sidetoneGenerator?.SetFrequency(value);

        // Sync to radio
        _radioSettingsSynchronizer?.SyncCwPitchToRadio(value);
    }

    partial void OnSidetoneVolumeChanged(int value)
    {
        // Update sidetone volume
        _sidetoneGenerator?.SetVolume(value);

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
        // Refresh the radio list when a new radio is discovered
        RefreshRadios();
    }

    private void OnRadioRemoved(Radio radio)
    {
        // Refresh the radio list when a radio is removed
        RefreshRadios();

        if (_connectedRadio == radio)
        {
            _connectedRadio = null;
            RadioStatus = "Disconnected (radio removed)";
            RadioStatusColor = Brushes.Red;
            HasRadioError = true;
            ConnectButtonText = "Connect";
        }
    }


    private void TransmitSliceMonitor_ModeChanged(object sender, TransmitModeChangedEventArgs e)
    {
        // Update keying controller
        _keyingController?.SetTransmitMode(e.IsTransmitModeCW);

        // Update UI when transmit mode changes
        Dispatcher.UIThread.Post(() => UpdatePaddleLabels());
    }

    private void UpdatePaddleLabels()
    {
        // Build combined mode display string
        string modeStr;

        if (_connectedRadio == null && !_isSidetoneOnlyMode)
        {
            // Disconnected
            modeStr = "Disconnected";
            LeftPaddleLabelText = "Left Paddle";
            RightPaddleVisible = true;
            ModeInstructions = "";
            CwSettingsVisible = true;
        }
        else if (_isSidetoneOnlyMode)
        {
            // Sidetone-only mode
            modeStr = "Sidetone Only";
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
        else if (!_transmitSliceMonitor.IsTransmitModeCW)
        {
            // PTT mode (non-CW radio modes)
            var txSlice = _transmitSliceMonitor.TransmitSlice;
            string radioMode = txSlice?.DemodMode?.ToUpper() ?? "Unknown";
            modeStr = $"{radioMode} (PTT)";

            LeftPaddleLabelText = "PTT";
            RightPaddleVisible = false;
            CwSettingsVisible = false;
            ModeInstructions = $"Switch radio to CW mode to activate CW keying";
        }
        else
        {
            // CW mode
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

            CwSettingsVisible = true;
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

            // Restore previously selected radio/client if it's a SmartLink radio
            // (and not already selected)
            if (SelectedRadioClient == null || SelectedRadioClient.Radio == null)
            {
                if (_settings != null && !string.IsNullOrEmpty(_settings.SelectedRadioSerial))
                {
                    _loadingSettings = true;
                    var savedSelection = RadioClientSelections.FirstOrDefault(s =>
                        s.Radio?.Serial == _settings.SelectedRadioSerial &&
                        s.GuiClient?.Station == _settings.SelectedGuiClientStation);

                    if (savedSelection != null)
                    {
                        SelectedRadioClient = savedSelection;
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

        await loginDialog.ShowDialog(mainWindow);

        if (loginDialog.LoginSucceeded)
        {
            var username = loginDialog.Username;
            var password = loginDialog.Password;

            // Update and save the Remember Me preference
            _settings.RememberMeSmartLink = loginDialog.RememberMe;
            _settings.Save();

            // Attempt login
            SmartLinkStatus = "Authenticating...";
            var success = await _smartLinkManager.LoginAsync(username, password);

            if (!success)
            {
                SmartLinkStatus = "Login failed - check credentials";
            }
        }
        else
        {
            SmartLinkStatus = "Login cancelled";
        }
    }

    #endregion
}

