using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            _settings.KeepAudioDeviceAwake = dialog.KeepAudioDeviceAwake;
            _settings.Save();
            DebugLogger.Log("audio", $"[SelectAudioDevice] Saved KeepAudioDeviceAwake={dialog.KeepAudioDeviceAwake}");

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
        // Swap is now handled in InputDeviceManager
        bool leftPaddleState = e.LeftPaddle;
        bool rightPaddleState = e.RightPaddle;
        bool straightKeyState = e.StraightKey;
        bool pttState = e.PTT;

        DebugLogger.Log("input", $"[InputDeviceManager_PaddleStateChanged] Received event: L={leftPaddleState} R={rightPaddleState} SK={straightKeyState} PTT={pttState}");

        // Update indicators
        Dispatcher.UIThread.Post(() =>
        {
            bool leftIndicatorState;

            // Check transmit mode first (CW vs PTT), then keying mode (iambic vs straight)
            if (!(_transmitSliceMonitor.IsTransmitModeCW || _isSidetoneOnlyMode))
            {
                // PTT mode (non-CW radio modes) - use PTT state
                // (InputDeviceManager sets this to OR of both paddles for serial input)
                leftIndicatorState = pttState;
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

            DebugLogger.Log("input", $"[Indicator Update] IsIambic={IsIambicMode} IsCW={_transmitSliceMonitor.IsTransmitModeCW} Sidetone={_isSidetoneOnlyMode} | L={leftPaddleState} R={rightPaddleState} SK={straightKeyState} PTT={pttState} | LeftInd={leftIndicatorState}");

            LeftPaddleIndicatorColor = leftIndicatorState ? Brushes.LimeGreen : Brushes.Black;
            LeftPaddleStateText = leftIndicatorState ? "ON" : "OFF";
            RightPaddleIndicatorColor = rightPaddleState ? Brushes.LimeGreen : Brushes.Black;
            RightPaddleStateText = rightPaddleState ? "ON" : "OFF";
        });

        // Delegate keying logic to KeyingController
        _keyingController?.HandlePaddleStateChange(leftPaddleState, rightPaddleState, straightKeyState, pttState);
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

            // SAVE PERSISTENCE: Save connected radio to settings
            _settings.SelectedRadioSerial = _connectedRadio.Serial;
            _settings.SelectedGuiClientStation = targetStation;
            _settings.Save();

            // Clear current selection - this is now the baseline
            _currentUserSelection = null;

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

            // Reset selection state
            _currentUserSelection = null;

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

        // Dispose keep-awake stream
        _keepAwakeStream?.Stop();
        _keepAwakeStream?.Dispose();

        // Dispose sidetone generator
        _sidetoneGenerator?.Dispose();

        API.CloseSession();
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
            _connectedRadio = null;
            RadioStatus = "Disconnected (radio removed)";
            RadioStatusColor = Brushes.Red;
            HasRadioError = true;
            ConnectButtonText = "Connect";
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
            ConnectedRadioDisplay = "";
            LeftPaddleLabelText = "Left Paddle";
            RightPaddleVisible = true;
            ModeInstructions = "";
            CwSettingsVisible = true;
        }
        else if (_isSidetoneOnlyMode)
        {
            // Sidetone-only mode
            modeStr = "Sidetone Only";
            ConnectedRadioDisplay = "";
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

            ConnectedRadioDisplay = $"{_connectedRadio.Nickname} ({_connectedRadio.Model})";
            LeftPaddleLabelText = "PTT";
            RightPaddleVisible = false;
            CwSettingsVisible = false;
            ModeInstructions = $"Switch radio to CW mode to activate CW keying";
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

