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
    private SerialPort _serialPort;
    private MidiPaddleInput _midiInput;
    private bool _previousLeftPaddleState = false;
    private bool _previousRightPaddleState = false;
    private bool _previousStraightKeyState = false;
    private bool _previousPttState = false;
    private uint _boundGuiClientHandle = 0;
    private bool _updatingFromRadio = false; // Prevent feedback loops
    private UserSettings _settings;
    private bool _loadingSettings = false; // Prevent saving while loading
    private bool _isTransmitModeCW = true; // Track if transmit slice is in CW mode
    private bool _isSidetoneOnlyMode = false; // Track if we're in sidetone-only mode (no radio)
    private bool _userExplicitlySelectedSidetoneOnly = false; // Track if user explicitly selected sidetone-only vs. implicit fallback
    private DateTime _inputDeviceOpenedTime = DateTime.MinValue; // Track when input device was opened
    private const int INPUT_GRACE_PERIOD_MS = 100; // Ignore paddle events for this many ms after opening device

    // Sidetone generator and iambic keyer
    private ISidetoneGenerator _sidetoneGenerator;
    private IambicKeyer _iambicKeyer;

    // SmartLink support
    private SmartLinkAuthService _smartLinkAuth;
    private WanServer _wanServer;
    private ManualResetEvent _wanConnectionReadyEvent = new ManualResetEvent(false);

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
        InitializeSmartLink();

        // Initialize FlexLib API
        API.ProgramName = "NetKeyer";
        API.RadioAdded += OnRadioAdded;
        API.RadioRemoved += OnRadioRemoved;
        API.Init();

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

        // Initialize iambic keyer
        _iambicKeyer = new IambicKeyer(
            _sidetoneGenerator,
            _boundGuiClientHandle,
            GetTimestamp,
            (state, timestamp, handle) =>
            {
                if (_connectedRadio != null)
                    _connectedRadio.CWKey(state, timestamp, handle);
            }
        );
        _iambicKeyer.IsModeB = IsIambicModeB;
        _iambicKeyer.EnableDebugLogging = DebugFlags.DEBUG_KEYER;
        _iambicKeyer.SetWpm(CwSpeed);
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
        // Stop iambic keyer when switching to straight key mode
        if (!value)
        {
            _iambicKeyer?.Stop();
        }

        // Only send to radio if not updating from radio (prevent feedback loop)
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                _connectedRadio.CWIambic = value;
            }
            catch { }
        }

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

        // If SmartLink is connected, request updated radio list
        if (_wanServer != null && _wanServer.IsConnected && _smartLinkAuth != null && _smartLinkAuth.AuthState == SmartLinkAuthState.Authenticated)
        {
            // The WanRadioRadioListRecieved event handler will add SmartLink radios to the list
            // Note: This happens automatically when WanServer is connected
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

        try
        {
            var ports = SerialPort.GetPortNames();

            // Remove duplicates (SerialPort.GetPortNames() can return duplicates on some platforms)
            var uniquePorts = ports.Distinct().ToArray();

            Array.Sort(uniquePorts, (a, b) =>
            {
                try
                {
                    // Extract just the filename from path
                    string nameA = a;
                    string nameB = b;
                    int lastSlashA = a.LastIndexOfAny(new[] { '/', '\\' });
                    int lastSlashB = b.LastIndexOfAny(new[] { '/', '\\' });
                    if (lastSlashA >= 0) nameA = a.Substring(lastSlashA + 1);
                    if (lastSlashB >= 0) nameB = b.Substring(lastSlashB + 1);

                    // Try to extract numeric part for sorting
                    string numStrA = nameA.Replace("ttyUSB", "").Replace("COM", "").Replace("ttyACM", "");
                    string numStrB = nameB.Replace("ttyUSB", "").Replace("COM", "").Replace("ttyACM", "");

                    if (int.TryParse(numStrA, out int idA) && int.TryParse(numStrB, out int idB))
                        return idA.CompareTo(idB);

                    return nameA.CompareTo(nameB);
                }
                catch
                {
                    return a.CompareTo(b);
                }
            });

            foreach (var port in uniquePorts)
            {
                SerialPorts.Add(port);
            }

            if (SerialPorts.Count == 0)
            {
                SerialPorts.Add("No ports found");
            }

            // Restore previously selected serial port if available
            if (_settings != null && !string.IsNullOrEmpty(_settings.SelectedSerialPort))
            {
                if (SerialPorts.Contains(_settings.SelectedSerialPort))
                {
                    SelectedSerialPort = _settings.SelectedSerialPort;
                }
            }
        }
        catch (Exception ex)
        {
            SerialPorts.Add($"Error: {ex.Message}");
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    [RelayCommand]
    private void RefreshMidiDevices()
    {
        _loadingSettings = true;
        MidiDevices.Clear();

        try
        {
            var devices = MidiPaddleInput.GetAvailableDevices();

            foreach (var device in devices)
            {
                MidiDevices.Add(device);
            }

            if (MidiDevices.Count == 0)
            {
                MidiDevices.Add("No MIDI devices found");
            }
            else
            {
                // Restore previously selected MIDI device if available (only if we have real devices)
                if (_settings != null && !string.IsNullOrEmpty(_settings.SelectedMidiDevice))
                {
                    if (MidiDevices.Contains(_settings.SelectedMidiDevice))
                    {
                        SelectedMidiDevice = _settings.SelectedMidiDevice;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MidiDevices.Add($"MIDI Error: {ex.Message}");
        }
        finally
        {
            _loadingSettings = false;
        }
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
            if (_midiInput != null)
            {
                _midiInput.SetNoteMappings(_settings.MidiNoteMappings);
            }
        }
    }

    private void CloseSerialPort()
    {
        if (_serialPort != null)
        {
            // Stop iambic keyer if running
            _iambicKeyer?.Stop();

            try
            {
                if (_serialPort.IsOpen)
                    _serialPort.Close();
                _serialPort.PinChanged -= SerialPort_PinChanged;
                _serialPort.Dispose();
            }
            catch { }
            _serialPort = null;

            // Reset indicators and state
            _previousLeftPaddleState = false;
            _previousRightPaddleState = false;
            _previousStraightKeyState = false;
            _previousPttState = false;
            LeftPaddleIndicatorColor = Brushes.Black;
            RightPaddleIndicatorColor = Brushes.Black;
            LeftPaddleStateText = "OFF";
            RightPaddleStateText = "OFF";
            _inputDeviceOpenedTime = DateTime.MinValue;
        }
    }

    private void CloseMidiDevice()
    {
        if (_midiInput != null)
        {
            // Stop iambic keyer if running
            _iambicKeyer?.Stop();

            try
            {
                _midiInput.PaddleStateChanged -= MidiInput_PaddleStateChanged;
                _midiInput.Close();
                _midiInput.Dispose();
            }
            catch { }
            _midiInput = null;

            // Reset indicators and state
            _previousLeftPaddleState = false;
            _previousRightPaddleState = false;
            _previousStraightKeyState = false;
            _previousPttState = false;
            LeftPaddleIndicatorColor = Brushes.Black;
            RightPaddleIndicatorColor = Brushes.Black;
            LeftPaddleStateText = "OFF";
            RightPaddleStateText = "OFF";
            _inputDeviceOpenedTime = DateTime.MinValue;
        }
    }

    private void OpenInputDevice()
    {
        if (InputType == InputDeviceType.Serial)
        {
            // Open serial port
            if (string.IsNullOrEmpty(SelectedSerialPort) || SelectedSerialPort.Contains("No ports") || SelectedSerialPort.Contains("Error"))
            {
                RadioStatus = "No serial port selected";
                RadioStatusColor = Brushes.Orange;
                HasRadioError = true;
                return;
            }

            try
            {
                _serialPort = new SerialPort(SelectedSerialPort);
                _serialPort.BaudRate = 9600; // Baud rate doesn't matter for control lines
                _serialPort.DtrEnable = true; // Enable DTR for power
                _serialPort.RtsEnable = true; // Enable RTS for power
                _serialPort.PinChanged += SerialPort_PinChanged;
                _serialPort.Open();

                // Mark when we opened the device to enable grace period
                _inputDeviceOpenedTime = DateTime.UtcNow;

                // Reset previous states to ensure clean start
                _previousLeftPaddleState = false;
                _previousRightPaddleState = false;
                _previousStraightKeyState = false;
                _previousPttState = false;

                // Update indicators to show current state (but don't trigger keying)
                UpdateIndicatorsFromSerial();
            }
            catch (Exception ex)
            {
                RadioStatus = $"Serial port error: {ex.Message}";
                RadioStatusColor = Brushes.Orange;
                HasRadioError = true;
                _serialPort = null;
            }
        }
        else // MIDI
        {
            // Open MIDI device
            if (string.IsNullOrEmpty(SelectedMidiDevice) || SelectedMidiDevice.Contains("No MIDI") || SelectedMidiDevice.Contains("Error"))
            {
                RadioStatus = "No MIDI device selected";
                RadioStatusColor = Brushes.Orange;
                HasRadioError = true;
                return;
            }

            try
            {
                _midiInput = new MidiPaddleInput();
                _midiInput.SetNoteMappings(_settings.MidiNoteMappings);
                _midiInput.PaddleStateChanged += MidiInput_PaddleStateChanged;
                _midiInput.Open(SelectedMidiDevice);

                // Mark when we opened the device to enable grace period
                _inputDeviceOpenedTime = DateTime.UtcNow;

                // Reset previous states to ensure clean start
                _previousLeftPaddleState = false;
                _previousRightPaddleState = false;
                _previousStraightKeyState = false;
                _previousPttState = false;
            }
            catch (Exception ex)
            {
                RadioStatus = $"MIDI device error: {ex.Message}";
                RadioStatusColor = Brushes.Orange;
                HasRadioError = true;
                _midiInput = null;
            }
        }
    }

    private void MidiInput_PaddleStateChanged(object sender, PaddleStateChangedEventArgs e)
    {
        // Check if we're in the grace period after opening the input device
        bool inGracePeriod = (DateTime.UtcNow - _inputDeviceOpenedTime).TotalMilliseconds < INPUT_GRACE_PERIOD_MS;
        if (inGracePeriod)
        {
            if (DebugFlags.DEBUG_MIDI_HANDLER)
                Console.WriteLine($"[MidiInput_PaddleStateChanged] In grace period - ignoring event");
            return;
        }

        bool leftPaddleState = e.LeftPaddle;
        bool rightPaddleState = e.RightPaddle;
        bool straightKeyState = e.StraightKey;
        bool pttState = e.PTT;

        if (DebugFlags.DEBUG_MIDI_HANDLER)
            Console.WriteLine($"[MidiInput_PaddleStateChanged] Received event: L={leftPaddleState} R={rightPaddleState} SK={straightKeyState} PTT={pttState}");

        // Apply swap if enabled (only affects paddles, not straight key)
        if (SwapPaddles)
        {
            (leftPaddleState, rightPaddleState) = (rightPaddleState, leftPaddleState);
            if (DebugFlags.DEBUG_MIDI_HANDLER)
                Console.WriteLine($"[MidiInput_PaddleStateChanged] After swap: L={leftPaddleState} R={rightPaddleState}");
        }

        // Update indicators
        Dispatcher.UIThread.Post(() =>
        {
            LeftPaddleIndicatorColor = leftPaddleState ? Brushes.LimeGreen : Brushes.Black;
            LeftPaddleStateText = leftPaddleState ? "ON" : "OFF";
            RightPaddleIndicatorColor = rightPaddleState ? Brushes.LimeGreen : Brushes.Black;
            RightPaddleStateText = rightPaddleState ? "ON" : "OFF";
        });

        // Handle keying based on mode and transmit slice mode
        // In sidetone-only mode, _connectedRadio is null but we still want to key locally
        if (_connectedRadio != null && _boundGuiClientHandle != 0)
        {
            if (_isTransmitModeCW)
            {
                // CW mode - use paddle/straight key keying
                if (IsIambicMode)
                {
                    if (DebugFlags.DEBUG_MIDI_HANDLER)
                        Console.WriteLine($"[MidiInput_PaddleStateChanged] CW/Iambic - calling keyer with L={leftPaddleState} R={rightPaddleState}");
                    // Iambic mode - use paddle inputs
                    _iambicKeyer?.UpdatePaddleState(leftPaddleState, rightPaddleState);
                }
                else
                {
                    // Straight key mode - use dedicated straight key input if changed,
                    // otherwise fall back to left paddle (for backward compatibility)
                    if (straightKeyState != _previousStraightKeyState)
                    {
                        SendCWKey(straightKeyState);
                    }
                    else if (leftPaddleState != _previousLeftPaddleState)
                    {
                        SendCWKey(leftPaddleState);
                    }
                }
            }
            else
            {
                // Non-CW mode - use PTT keying
                if (pttState != _previousPttState)
                {
                    if (DebugFlags.DEBUG_MIDI_HANDLER)
                        Console.WriteLine($"[MidiInput_PaddleStateChanged] Non-CW mode - sending PTT={pttState}");
                    SendPTT(pttState);
                }
            }
        }
        else if (_isSidetoneOnlyMode)
        {
            // Sidetone-only mode - still run keyer logic, just no radio commands
            if (IsIambicMode)
            {
                _iambicKeyer?.UpdatePaddleState(leftPaddleState, rightPaddleState);
            }
            else
            {
                // Straight key mode
                if (straightKeyState != _previousStraightKeyState)
                {
                    SendCWKey(straightKeyState);
                }
                else if (leftPaddleState != _previousLeftPaddleState)
                {
                    SendCWKey(leftPaddleState);
                }
            }
        }
        else
        {
            if (DebugFlags.DEBUG_MIDI_HANDLER)
                Console.WriteLine($"[MidiInput_PaddleStateChanged] Skipping keyer - radio not connected (radio={_connectedRadio != null}, handle={_boundGuiClientHandle})");
        }

        // Update previous states
        _previousLeftPaddleState = leftPaddleState;
        _previousRightPaddleState = rightPaddleState;
        _previousStraightKeyState = straightKeyState;
        _previousPttState = pttState;
    }

    private void SerialPort_PinChanged(object sender, SerialPinChangedEventArgs e)
    {
        // HaliKey v1: CTS (left) + DCD (right)
        if (e.EventType == SerialPinChange.CtsChanged || e.EventType == SerialPinChange.CDChanged)
        {
            UpdateSerialPinStates();
        }
    }

    private void UpdateIndicatorsFromSerial()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        try
        {
            // HaliKey v1: CTS (left) + DCD (right)
            bool leftPaddleState = _serialPort.CtsHolding;
            bool rightPaddleState = _serialPort.CDHolding;

            // Apply swap if enabled
            if (SwapPaddles)
            {
                (leftPaddleState, rightPaddleState) = (rightPaddleState, leftPaddleState);
            }

            // Update indicators only
            LeftPaddleIndicatorColor = leftPaddleState ? Brushes.LimeGreen : Brushes.Black;
            LeftPaddleStateText = leftPaddleState ? "ON" : "OFF";
            RightPaddleIndicatorColor = rightPaddleState ? Brushes.LimeGreen : Brushes.Black;
            RightPaddleStateText = rightPaddleState ? "ON" : "OFF";
        }
        catch { }
    }

    private void UpdateSerialPinStates()
    {
        // Check if we're in the grace period after opening the input device
        bool inGracePeriod = (DateTime.UtcNow - _inputDeviceOpenedTime).TotalMilliseconds < INPUT_GRACE_PERIOD_MS;
        if (inGracePeriod)
        {
            // Ignore all pin state changes during grace period
            return;
        }

        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        try
        {
            // HaliKey v1: CTS (left) + DCD (right)
            bool leftPaddleState = _serialPort.CtsHolding;
            bool rightPaddleState = _serialPort.CDHolding;

            // Apply swap if enabled
            if (SwapPaddles)
            {
                (leftPaddleState, rightPaddleState) = (rightPaddleState, leftPaddleState);
            }

            // Update left paddle indicator
            LeftPaddleIndicatorColor = leftPaddleState ? Brushes.LimeGreen : Brushes.Black;
            LeftPaddleStateText = leftPaddleState ? "ON" : "OFF";

            // Update right paddle indicator
            RightPaddleIndicatorColor = rightPaddleState ? Brushes.LimeGreen : Brushes.Black;
            RightPaddleStateText = rightPaddleState ? "ON" : "OFF";

            // Handle keying based on mode and transmit slice mode
            if (_connectedRadio != null && _boundGuiClientHandle != 0)
            {
                if (_isTransmitModeCW)
                {
                    // CW mode - use paddle/straight key keying
                    if (IsIambicMode)
                    {
                        // Iambic mode - call keyer logic
                        _iambicKeyer?.UpdatePaddleState(leftPaddleState, rightPaddleState);
                    }
                    else
                    {
                        // Straight key mode - left paddle directly controls CW key
                        if (leftPaddleState != _previousLeftPaddleState)
                        {
                            SendCWKey(leftPaddleState);
                        }
                    }
                }
                else
                {
                    // Non-CW mode - either paddle triggers PTT
                    bool pttState = leftPaddleState || rightPaddleState;
                    bool previousPttState = _previousLeftPaddleState || _previousRightPaddleState;

                    if (pttState != previousPttState)
                    {
                        SendPTT(pttState);
                    }
                }
            }
            else if (_isSidetoneOnlyMode)
            {
                // Sidetone-only mode - still run keyer logic, just no radio commands
                if (IsIambicMode)
                {
                    _iambicKeyer?.UpdatePaddleState(leftPaddleState, rightPaddleState);
                }
                else
                {
                    // Straight key mode
                    if (leftPaddleState != _previousLeftPaddleState)
                    {
                        SendCWKey(leftPaddleState);
                    }
                }
            }

            // Update previous states
            _previousLeftPaddleState = leftPaddleState;
            _previousRightPaddleState = rightPaddleState;
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

            // For WAN radios, we need to request connection from WanServer first
            if (_connectedRadio.IsWan)
            {
                if (_wanServer == null || !_wanServer.IsConnected)
                {
                    RadioStatus = "Not connected to SmartLink server";
                    RadioStatusColor = Brushes.Red;
                    HasRadioError = true;
                    _connectedRadio = null;
                    return;
                }

                // Reset the event and subscribe to connect_ready event
                _wanConnectionReadyEvent.Reset();
                _wanServer.WanRadioConnectReady += WanServer_RadioConnectReady;

                // Request connection to this radio
                RadioStatus = "Requesting SmartLink connection...";
                _wanServer.SendConnectMessageToRadio(_connectedRadio.Serial, HolePunchPort: 0);

                // Wait for connect_ready response (with timeout)
                if (!_wanConnectionReadyEvent.WaitOne(10000))
                {
                    RadioStatus = "SmartLink connection request timed out";
                    RadioStatusColor = Brushes.Red;
                    HasRadioError = true;
                    _wanServer.WanRadioConnectReady -= WanServer_RadioConnectReady;
                    _connectedRadio = null;
                    return;
                }

                // Unsubscribe from event
                _wanServer.WanRadioConnectReady -= WanServer_RadioConnectReady;

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

            // Reinitialize iambic keyer with the correct radio client handle
            _iambicKeyer = new IambicKeyer(
                _sidetoneGenerator,
                _boundGuiClientHandle,
                GetTimestamp,
                (state, timestamp, handle) =>
                {
                    if (_connectedRadio != null)
                        _connectedRadio.CWKey(state, timestamp, handle);
                }
            );
            _iambicKeyer.IsModeB = IsIambicModeB;
            _iambicKeyer.EnableDebugLogging = DebugFlags.DEBUG_KEYER;
            _iambicKeyer.SetWpm(CwSpeed);

            // Subscribe to radio property changes
            _connectedRadio.PropertyChanged += Radio_PropertyChanged;

            // Subscribe to transmit slice property changes and update initial mode
            SubscribeToTransmitSlice();

            // Apply initial CW settings
            ApplyCwSettings();

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

            // Stop iambic keyer (sends key-up if active)
            _iambicKeyer?.Stop();

            // If straight key was active, send key-up
            if (_previousStraightKeyState)
            {
                SendCWKey(false);
            }

            // If PTT was active, send PTT-off
            if (_previousPttState)
            {
                SendPTT(false);
            }

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

                // Unsubscribe from monitored transmit slice if present
                if (_monitoredTransmitSlice != null)
                {
                    _monitoredTransmitSlice.PropertyChanged -= TransmitSlice_PropertyChanged;
                    _monitoredTransmitSlice = null;
                }

                _connectedRadio.Disconnect();
                _connectedRadio = null;
            }

            // Close input devices
            CloseSerialPort();
            CloseMidiDevice();

            _boundGuiClientHandle = 0;
            _previousLeftPaddleState = false;
            _previousRightPaddleState = false;
            _previousStraightKeyState = false;
            _previousPttState = false;
            _isSidetoneOnlyMode = false;

            // Clear any error status on manual disconnect
            HasRadioError = false;
            ConnectButtonText = "Connect";

            // Update paddle labels after disconnection
            UpdatePaddleLabels();

            // Switch back to setup page
            CurrentPage = PageType.Setup;
        }
    }

    [RelayCommand]
    private void Exit()
    {
        // Clean up all keying state before exit
        _iambicKeyer?.Stop();

        if (_previousStraightKeyState)
        {
            SendCWKey(false);
        }

        if (_previousPttState)
        {
            SendPTT(false);
        }

        _sidetoneGenerator?.Stop();

        if (_connectedRadio != null)
        {
            _connectedRadio.Disconnect();
        }

        CloseSerialPort();
        CloseMidiDevice();

        // Dispose sidetone generator
        _sidetoneGenerator?.Dispose();

        API.CloseSession();
        Environment.Exit(0);
    }

    private void ApplyCwSettings()
    {
        if (_connectedRadio == null)
            return;

        // Read current settings from radio and update UI
        try
        {
            _updatingFromRadio = true;
            try
            {
                CwSpeed = _connectedRadio.CWSpeed;
                CwPitch = _connectedRadio.CWPitch;
                SidetoneVolume = _connectedRadio.TXCWMonitorGain;
            }
            finally
            {
                _updatingFromRadio = false;
            }
        }
        catch (Exception ex)
        {
            RadioStatus = $"Settings error: {ex.Message}";
            RadioStatusColor = Brushes.Orange;
            HasRadioError = true;
        }
    }

    partial void OnCwSpeedChanged(int value)
    {
        // Update sidetone generator WPM for ramp calculations
        _sidetoneGenerator?.SetWpm(value);

        // Update iambic keyer WPM for timing calculations
        _iambicKeyer?.SetWpm(value);

        // Only send to radio if not updating from radio (prevent feedback loop)
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                _connectedRadio.CWSpeed = value;
            }
            catch { }
        }
    }

    partial void OnCwPitchChanged(int value)
    {
        // Update sidetone frequency
        _sidetoneGenerator?.SetFrequency(value);

        // Only send to radio if not updating from radio (prevent feedback loop)
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                _connectedRadio.CWPitch = value;
            }
            catch { }
        }
    }

    partial void OnSidetoneVolumeChanged(int value)
    {
        // Update sidetone volume
        _sidetoneGenerator?.SetVolume(value);

        // Only send to radio if not updating from radio (prevent feedback loop)
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                _connectedRadio.TXCWMonitorGain = value;
            }
            catch { }
        }
    }

    partial void OnIsIambicModeBChanged(bool value)
    {
        // Update keyer mode
        if (_iambicKeyer != null)
        {
            _iambicKeyer.IsModeB = value;
        }

        // Only send to radio if not updating from radio (prevent feedback loop)
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                if (value)
                {
                    // Set Mode B - this sends "cw mode 1"
                    _connectedRadio.CWIambicModeB = true;
                    // Also explicitly clear Mode A
                    _connectedRadio.CWIambicModeA = false;
                }
                else
                {
                    // Set Mode A - this sends "cw mode 0"
                    _connectedRadio.CWIambicModeA = true;
                    // Also explicitly clear Mode B
                    _connectedRadio.CWIambicModeB = false;
                }
            }
            catch { }
        }

        // Update mode display when iambic type changes
        UpdatePaddleLabels();
    }

    partial void OnSwapPaddlesChanged(bool value)
    {
        // Only send to radio if not updating from radio (prevent feedback loop)
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                _connectedRadio.CWSwapPaddles = value;
            }
            catch { }
        }
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


    private void SendCWKey(bool state)
    {
        SendCWKey(state, null);
    }

    private void SendCWKey(bool state, int? durationMs)
    {
        // Control local sidetone
        if (state)
        {
            if (durationMs.HasValue)
                _sidetoneGenerator?.StartTone(durationMs.Value);
            else
                _sidetoneGenerator?.Start();
        }
        else
        {
            _sidetoneGenerator?.Stop();
        }

        // Send to radio
        if (_connectedRadio != null && _boundGuiClientHandle != 0)
        {
            string timestamp = GetTimestamp();
            _connectedRadio.CWKey(state, timestamp, _boundGuiClientHandle);
        }
    }

    private void SendPTT(bool state)
    {
        // PTT does not use sidetone

        // Send to radio
        if (_connectedRadio != null)
        {
            _connectedRadio.Mox = state;
        }
    }

    private Slice _monitoredTransmitSlice = null;

    private Slice FindOurTransmitSlice()
    {
        if (_connectedRadio == null || _boundGuiClientHandle == 0)
            return null;

        foreach (var slice in _connectedRadio.SliceList)
        {
            if (slice.IsTransmitSlice && slice.ClientHandle == _boundGuiClientHandle)
                return slice;
        }

        return null;
    }

    private void UpdateTransmitSliceMode()
    {
        var txSlice = FindOurTransmitSlice();

        if (txSlice != null)
        {
            string mode = txSlice.DemodMode?.ToUpper() ?? "USB";
            bool wasCW = _isTransmitModeCW;
            _isTransmitModeCW = (mode == "CW");

            if (DebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] Slice {txSlice.Index} mode: {mode}, isCW: {_isTransmitModeCW}");

            if (wasCW != _isTransmitModeCW)
            {
                if (DebugFlags.DEBUG_SLICE_MODE)
                    Console.WriteLine($"[TransmitSliceMode] Mode changed from {(wasCW ? "CW" : "non-CW")} to {(_isTransmitModeCW ? "CW" : "non-CW")}");
            }

            // Update UI elements
            UpdatePaddleLabels();
        }
        else
        {
            if (DebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] No transmit slice for our client, defaulting to CW mode");
            _isTransmitModeCW = true; // Default to CW mode if no transmit slice

            // Update UI elements
            UpdatePaddleLabels();
        }
    }

    private void SubscribeToTransmitSlice()
    {
        // Unsubscribe from old slice if present
        if (_monitoredTransmitSlice != null)
        {
            _monitoredTransmitSlice.PropertyChanged -= TransmitSlice_PropertyChanged;
            _monitoredTransmitSlice = null;
        }

        // Debug: Check radio and slices
        if (DebugFlags.DEBUG_SLICE_MODE && _connectedRadio != null)
        {
            Console.WriteLine($"[TransmitSliceMode] Connected radio has {_connectedRadio.SliceList.Count} slices");
            Console.WriteLine($"[TransmitSliceMode] Our bound ClientHandle: {_boundGuiClientHandle}");
            Console.WriteLine($"[TransmitSliceMode] Radio's internal ClientHandle: {_connectedRadio.ClientHandle}");

            foreach (var slice in _connectedRadio.SliceList)
            {
                Console.WriteLine($"[TransmitSliceMode]   Slice {slice.Index}: IsTransmitSlice={slice.IsTransmitSlice}, ClientHandle={slice.ClientHandle}, Mode={slice.DemodMode}");
            }
        }

        // Subscribe to new slice using our own finder
        var txSlice = FindOurTransmitSlice();
        if (txSlice != null)
        {
            _monitoredTransmitSlice = txSlice;
            _monitoredTransmitSlice.PropertyChanged += TransmitSlice_PropertyChanged;

            if (DebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] Subscribed to slice {_monitoredTransmitSlice.Index}");
        }
        else
        {
            if (DebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] No transmit slice found for our client");
        }

        UpdateTransmitSliceMode();
    }

    private void TransmitSlice_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "DemodMode")
        {
            if (DebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] DemodMode property changed");
            UpdateTransmitSliceMode();
        }
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
        else if (!_isTransmitModeCW)
        {
            // PTT mode (non-CW radio modes)
            var txSlice = FindOurTransmitSlice();
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
        // Handle TransmitSlice changes (needs to be done outside UI thread)
        if (e.PropertyName == "TransmitSlice")
        {
            if (DebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] Radio TransmitSlice property changed");
            SubscribeToTransmitSlice();
            return;
        }

        // Dispatch all UI updates to the UI thread
        Dispatcher.UIThread.Post(() =>
        {
            if (e.PropertyName == "CWSpeed")
            {
                // Update our local property from the radio
                if (_connectedRadio != null && CwSpeed != _connectedRadio.CWSpeed)
                {
                    _updatingFromRadio = true;
                    try
                    {
                        CwSpeed = _connectedRadio.CWSpeed;
                    }
                    finally
                    {
                        _updatingFromRadio = false;
                    }
                }
            }
            else if (e.PropertyName == "CWPitch")
            {
                // Update our local property from the radio
                if (_connectedRadio != null && CwPitch != _connectedRadio.CWPitch)
                {
                    _updatingFromRadio = true;
                    try
                    {
                        CwPitch = _connectedRadio.CWPitch;
                    }
                    finally
                    {
                        _updatingFromRadio = false;
                    }
                }
            }
            else if (e.PropertyName == "TXCWMonitorGain")
            {
                // Update our local property from the radio
                if (_connectedRadio != null && SidetoneVolume != _connectedRadio.TXCWMonitorGain)
                {
                    _updatingFromRadio = true;
                    try
                    {
                        SidetoneVolume = _connectedRadio.TXCWMonitorGain;
                    }
                    finally
                    {
                        _updatingFromRadio = false;
                    }
                }
            }
            else if (e.PropertyName == "CWIambic")
            {
                // Update our local property from the radio
                if (_connectedRadio != null && IsIambicMode != _connectedRadio.CWIambic)
                {
                    _updatingFromRadio = true;
                    try
                    {
                        IsIambicMode = _connectedRadio.CWIambic;
                    }
                    finally
                    {
                        _updatingFromRadio = false;
                    }
                }
            }
            else if (e.PropertyName == "CWIambicModeB" || e.PropertyName == "CWIambicModeA")
            {
                // Update our local property from the radio
                if (_connectedRadio != null)
                {
                    bool shouldBeModeB = _connectedRadio.CWIambicModeB;
                    if (IsIambicModeB != shouldBeModeB)
                    {
                        _updatingFromRadio = true;
                        try
                        {
                            IsIambicModeB = shouldBeModeB;
                        }
                        finally
                        {
                            _updatingFromRadio = false;
                        }
                    }
                }
            }
            else if (e.PropertyName == "CWSwapPaddles")
            {
                // Update our local property from the radio
                if (_connectedRadio != null && SwapPaddles != _connectedRadio.CWSwapPaddles)
                {
                    _updatingFromRadio = true;
                    try
                    {
                        SwapPaddles = _connectedRadio.CWSwapPaddles;
                    }
                    finally
                    {
                        _updatingFromRadio = false;
                    }
                }
            }
        });
    }

    #region SmartLink Methods

    private void InitializeSmartLink()
    {
        // Initialize SmartLink authentication service
        var clientIdProvider = new ConfigFileClientIdProvider();
        _smartLinkAuth = new SmartLinkAuthService(clientIdProvider);

        SmartLinkAvailable = _smartLinkAuth.IsAvailable;

        if (!SmartLinkAvailable)
        {
            SmartLinkStatus = "No client_id configured";
            return;
        }

        _smartLinkAuth.AuthStateChanged += SmartLinkAuth_AuthStateChanged;
        _smartLinkAuth.ErrorOccurred += SmartLinkAuth_ErrorOccurred;

        // Try to restore session from saved refresh token
        if (!string.IsNullOrEmpty(_settings.SmartLinkRefreshToken))
        {
            Task.Run(async () =>
            {
                var success = await _smartLinkAuth.RestoreSessionAsync(_settings.SmartLinkRefreshToken);
                if (success)
                {
                    await ConnectToSmartLinkServerAsync();
                }
            });
        }
    }

    private void SmartLinkAuth_AuthStateChanged(object sender, SmartLinkAuthState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SmartLinkAuthenticated = (state == SmartLinkAuthState.Authenticated);

            switch (state)
            {
                case SmartLinkAuthState.NotAuthenticated:
                    SmartLinkStatus = "Not logged in";
                    SmartLinkButtonText = "Login to SmartLink";
                    break;
                case SmartLinkAuthState.Authenticating:
                    SmartLinkStatus = "Authenticating...";
                    SmartLinkButtonText = "Authenticating...";
                    break;
                case SmartLinkAuthState.Authenticated:
                    SmartLinkStatus = "Authenticated";
                    SmartLinkButtonText = "Logout from SmartLink";

                    // Save refresh token only if Remember Me is enabled
                    if (_settings.RememberMeSmartLink)
                    {
                        var refreshToken = _smartLinkAuth.GetRefreshToken();
                        if (!string.IsNullOrEmpty(refreshToken))
                        {
                            _settings.SmartLinkRefreshToken = refreshToken;
                            _settings.Save();
                        }
                    }
                    else
                    {
                        // Clear any existing refresh token if Remember Me is disabled
                        _settings.SmartLinkRefreshToken = null;
                        _settings.Save();
                    }
                    break;
                case SmartLinkAuthState.Error:
                    SmartLinkStatus = "Authentication error";
                    SmartLinkButtonText = "Retry Login";
                    break;
            }
        });
    }

    private void SmartLinkAuth_ErrorOccurred(object sender, string error)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Console.WriteLine($"SmartLink error: {error}");
            SmartLinkStatus = $"Error: {error}";
        });
    }

    private Task ConnectToSmartLinkServerAsync()
    {
        if (_wanServer == null)
        {
            _wanServer = new WanServer();
            WanServer.WanRadioRadioListRecieved += WanServer_WanRadioListReceived;
            _wanServer.WanApplicationRegistrationInvalid += WanServer_RegistrationInvalid;
        }

        if (!_wanServer.IsConnected)
        {
            _wanServer.Connect();

            if (_wanServer.IsConnected)
            {
                var token = _smartLinkAuth.GetIdToken();
                var platform = Environment.OSVersion.Platform.ToString();
                _wanServer.SendRegisterApplicationMessageToServer("NetKeyer", platform, token);

                SmartLinkStatus = "Connected to SmartLink";
            }
            else
            {
                SmartLinkStatus = "Failed to connect to SmartLink server";
            }
        }

        return Task.CompletedTask;
    }

    private void WanServer_WanRadioListReceived(System.Collections.Generic.List<Radio> radios)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Add SmartLink radios to the radio list
            // They will be marked with IsWan = true
            foreach (var radio in radios)
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

    private void WanServer_RegistrationInvalid()
    {
        Dispatcher.UIThread.Post(() =>
        {
            SmartLinkStatus = "Registration invalid - please log in again";
            _smartLinkAuth?.Logout();

            // Clear saved refresh token
            _settings.SmartLinkRefreshToken = null;
            _settings.Save();

            // Disconnect from WAN server
            _wanServer?.Disconnect();
        });
    }

    private void WanServer_RadioConnectReady(string wan_connectionhandle, string serial)
    {
        // This is called when the SmartLink server responds with "radio connect_ready"
        // Store the handle in the radio object
        if (_connectedRadio != null && _connectedRadio.Serial == serial)
        {
            _connectedRadio.WANConnectionHandle = wan_connectionhandle;
            _wanConnectionReadyEvent.Set(); // Signal that we're ready to connect
        }
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
            _smartLinkAuth?.Logout();
            _wanServer?.Disconnect();

            // Clear saved refresh token
            _settings.SmartLinkRefreshToken = null;
            _settings.Save();

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
            var success = await _smartLinkAuth.LoginAsync(username, password);

            if (success)
            {
                // Connect to SmartLink server
                await ConnectToSmartLinkServerAsync();
            }
            else
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

