using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flex.Smoothlake.FlexLib;
using NetKeyer.Audio;
using NetKeyer.Midi;
using NetKeyer.Models;

namespace NetKeyer.ViewModels;

public enum HaliKeyVersion
{
    V1,  // CTS (left) + DCD (right)
    V2   // RI (left) + DCD (right)
}

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
    private ObservableCollection<string> _haliKeyVersions = new() { "HaliKey v1", "HaliKey v2" };

    [ObservableProperty]
    private string _selectedHaliKeyVersion = "HaliKey v1";

    private HaliKeyVersion CurrentHaliKeyVersion =>
        SelectedHaliKeyVersion == "HaliKey v2" ? HaliKeyVersion.V2 : HaliKeyVersion.V1;

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
    private bool _riState = false; // Track RI state for HaliKey v2
    private bool _previousLeftPaddleState = false;
    private bool _previousRightPaddleState = false;
    private uint _boundGuiClientHandle = 0;
    private bool _updatingFromRadio = false; // Prevent feedback loops
    private UserSettings _settings;
    private bool _loadingSettings = false; // Prevent saving while loading

    // Iambic keyer state
    private Timer _iambicTimer;
    private readonly object _iambicLock = new object();
    private bool _iambicKeyDown = false;
    private bool _iambicSendingDit = false; // true = dit, false = dah
    private bool _iambicDitLatched = false;
    private bool _iambicDahLatched = false;
    private bool _currentLeftPaddleState = false;  // Current physical paddle state
    private bool _currentRightPaddleState = false; // Current physical paddle state
    private int _ditLength = 60; // milliseconds, calculated from WPM

    // Sidetone generator
    private SidetoneGenerator _sidetoneGenerator;

    public MainWindowViewModel()
    {
        // Load user settings
        _settings = UserSettings.Load();

        // Initialize FlexLib API
        API.ProgramName = "NetKeyer";
        API.RadioAdded += OnRadioAdded;
        API.RadioRemoved += OnRadioRemoved;
        API.Init();

        // Apply saved input type and HaliKey version
        _loadingSettings = true;
        if (_settings.InputType == "MIDI")
        {
            InputType = InputDeviceType.MIDI;
        }
        if (!string.IsNullOrEmpty(_settings.HaliKeyVersion))
        {
            SelectedHaliKeyVersion = _settings.HaliKeyVersion;
        }
        _loadingSettings = false;

        // Initial discovery
        RefreshRadios();
        RefreshSerialPorts();
        RefreshMidiDevices();

        // Calculate initial dit length from CW speed
        UpdateDitLength();

        // Initialize sidetone generator
        try
        {
            _sidetoneGenerator = new SidetoneGenerator();
            _sidetoneGenerator.SetFrequency(CwPitch);
            _sidetoneGenerator.SetVolume(SidetoneVolume);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not initialize sidetone generator: {ex.Message}");
        }
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

    partial void OnSelectedHaliKeyVersionChanged(string value)
    {
        // If a serial port is open, update the pin states
        if (_serialPort != null && _serialPort.IsOpen)
        {
            UpdateSerialPinStates();
        }

        // Save setting
        if (!_loadingSettings && _settings != null)
        {
            _settings.HaliKeyVersion = value;
            _settings.Save();
        }
    }

    partial void OnSelectedRadioClientChanged(RadioClientSelection value)
    {
        if (!_loadingSettings && _settings != null && value != null)
        {
            _settings.SelectedRadioSerial = value.Radio?.Serial;
            _settings.SelectedGuiClientStation = value.GuiClient?.Station;
            _settings.Save();
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
            StopIambicKeyer();
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
    }

    [RelayCommand]
    private void RefreshRadios()
    {
        // Clear current list
        RadioClientSelections.Clear();

        // Get discovered radios from FlexLib and their GUI clients
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

        if (RadioClientSelections.Count == 0)
        {
            RadioClientSelections.Add(new RadioClientSelection
            {
                Radio = null,
                GuiClient = null,
                DisplayName = "No radios found"
            });
        }

        // Restore previously selected radio/client if available
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

    [RelayCommand]
    private void RefreshSerialPorts()
    {
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
                _loadingSettings = true;
                if (SerialPorts.Contains(_settings.SelectedSerialPort))
                {
                    SelectedSerialPort = _settings.SelectedSerialPort;
                }
                _loadingSettings = false;
            }
        }
        catch (Exception ex)
        {
            SerialPorts.Add($"Error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RefreshMidiDevices()
    {
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
                    _loadingSettings = true;
                    if (MidiDevices.Contains(_settings.SelectedMidiDevice))
                    {
                        SelectedMidiDevice = _settings.SelectedMidiDevice;
                    }
                    _loadingSettings = false;
                }
            }
        }
        catch (Exception ex)
        {
            MidiDevices.Add($"MIDI Error: {ex.Message}");
        }
    }

    private void CloseSerialPort()
    {
        if (_serialPort != null)
        {
            // Stop iambic keyer if running
            StopIambicKeyer();

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
            _riState = false;
            _previousLeftPaddleState = false;
            _previousRightPaddleState = false;
            LeftPaddleIndicatorColor = Brushes.Black;
            RightPaddleIndicatorColor = Brushes.Black;
            LeftPaddleStateText = "OFF";
            RightPaddleStateText = "OFF";
        }
    }

    private void CloseMidiDevice()
    {
        if (_midiInput != null)
        {
            // Stop iambic keyer if running
            StopIambicKeyer();

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
            LeftPaddleIndicatorColor = Brushes.Black;
            RightPaddleIndicatorColor = Brushes.Black;
            LeftPaddleStateText = "OFF";
            RightPaddleStateText = "OFF";
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

                // Initialize previous states without sending commands
                InitializeSerialPinStates();

                // Now update to show current state
                UpdateSerialPinStates();
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
                _midiInput.PaddleStateChanged += MidiInput_PaddleStateChanged;
                _midiInput.Open(SelectedMidiDevice);

                // MIDI devices start with both paddles off, so initialize previous states
                _previousLeftPaddleState = false;
                _previousRightPaddleState = false;
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
        // Update indicators
        Dispatcher.UIThread.Post(() =>
        {
            LeftPaddleIndicatorColor = e.LeftPaddle ? Brushes.LimeGreen : Brushes.Black;
            LeftPaddleStateText = e.LeftPaddle ? "ON" : "OFF";
            RightPaddleIndicatorColor = e.RightPaddle ? Brushes.LimeGreen : Brushes.Black;
            RightPaddleStateText = e.RightPaddle ? "ON" : "OFF";
        });

        // Handle keying based on mode
        if (_connectedRadio != null && _boundGuiClientHandle != 0)
        {
            if (IsIambicMode)
            {
                // Iambic mode - call keyer logic
                UpdateIambicKeyer(e.LeftPaddle, e.RightPaddle);
            }
            else
            {
                // Straight key mode - left paddle directly controls CW key
                if (e.LeftPaddle != _previousLeftPaddleState)
                {
                    string timestamp = GetTimestamp();
                    _connectedRadio.CWKey(e.LeftPaddle, timestamp, _boundGuiClientHandle);
                }
            }
        }

        // Update previous states
        _previousLeftPaddleState = e.LeftPaddle;
        _previousRightPaddleState = e.RightPaddle;
    }

    private void SerialPort_PinChanged(object sender, SerialPinChangedEventArgs e)
    {
        // Check which pins to monitor based on HaliKey version
        if (CurrentHaliKeyVersion == HaliKeyVersion.V1)
        {
            // V1: CTS (left) + DCD (right)
            if (e.EventType == SerialPinChange.CtsChanged || e.EventType == SerialPinChange.CDChanged)
            {
                UpdateSerialPinStates();
            }
        }
        else // V2
        {
            // V2: RI (left) + DCD (right)
            if (e.EventType == SerialPinChange.Ring)
            {
                // RI toggled - flip the state
                _riState = !_riState;
                UpdateSerialPinStates();
            }
            else if (e.EventType == SerialPinChange.CDChanged)
            {
                UpdateSerialPinStates();
            }
        }
    }

    private void InitializeSerialPinStates()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        try
        {
            // Read initial pin states and store as "previous" without sending commands
            if (CurrentHaliKeyVersion == HaliKeyVersion.V1)
            {
                _previousLeftPaddleState = _serialPort.CtsHolding;
                _previousRightPaddleState = _serialPort.CDHolding;
            }
            else // V2
            {
                _previousLeftPaddleState = _riState;
                _previousRightPaddleState = _serialPort.CDHolding;
            }
        }
        catch { }
    }

    private void UpdateSerialPinStates()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        try
        {
            bool leftPaddleState;
            bool rightPaddleState;

            if (CurrentHaliKeyVersion == HaliKeyVersion.V1)
            {
                // V1: CTS (left) + DCD (right)
                leftPaddleState = _serialPort.CtsHolding;
                rightPaddleState = _serialPort.CDHolding;
            }
            else // V2
            {
                // V2: RI (left) + DCD (right)
                // RI state is tracked via the Ring event toggling _riState
                leftPaddleState = _riState;
                rightPaddleState = _serialPort.CDHolding;
            }

            // Update left paddle indicator
            LeftPaddleIndicatorColor = leftPaddleState ? Brushes.LimeGreen : Brushes.Black;
            LeftPaddleStateText = leftPaddleState ? "ON" : "OFF";

            // Update right paddle indicator
            RightPaddleIndicatorColor = rightPaddleState ? Brushes.LimeGreen : Brushes.Black;
            RightPaddleStateText = rightPaddleState ? "ON" : "OFF";

            // Handle keying based on mode
            if (_connectedRadio != null && _boundGuiClientHandle != 0)
            {
                if (IsIambicMode)
                {
                    // Iambic mode - call keyer logic
                    UpdateIambicKeyer(leftPaddleState, rightPaddleState);
                }
                else
                {
                    // Straight key mode - left paddle directly controls CW key
                    if (leftPaddleState != _previousLeftPaddleState)
                    {
                        string timestamp = GetTimestamp();
                        _connectedRadio.CWKey(leftPaddleState, timestamp, _boundGuiClientHandle);
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
        if (_connectedRadio == null)
        {
            // Connect
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

            _connectedRadio.Connect();

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

            // Subscribe to radio property changes
            _connectedRadio.PropertyChanged += Radio_PropertyChanged;

            // Apply initial CW settings
            ApplyCwSettings();

            // Open the selected input device
            OpenInputDevice();

            // Switch to operating page
            CurrentPage = PageType.Operating;
        }
        else
        {
            // Disconnect
            StopIambicKeyer();

            // Unsubscribe from radio property changes
            if (_connectedRadio != null)
            {
                _connectedRadio.PropertyChanged -= Radio_PropertyChanged;
            }

            // Close input devices
            CloseSerialPort();
            CloseMidiDevice();

            _connectedRadio.Disconnect();
            _connectedRadio = null;
            _boundGuiClientHandle = 0;
            _previousLeftPaddleState = false;
            _previousRightPaddleState = false;

            // Clear any error status on manual disconnect
            HasRadioError = false;
            ConnectButtonText = "Connect";

            // Switch back to setup page
            CurrentPage = PageType.Setup;
        }
    }

    [RelayCommand]
    private void Exit()
    {
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
        UpdateDitLength();

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

    private void UpdateDitLength()
    {
        // Calculate dit length in milliseconds from WPM
        // Standard formula: dit_length = 1200 / WPM
        if (CwSpeed > 0)
        {
            _ditLength = 1200 / CwSpeed;
        }
        else
        {
            // Default to 20 WPM if speed is invalid
            _ditLength = 60;
        }
    }

    private void UpdateIambicKeyer(bool leftPaddle, bool rightPaddle)
    {
        lock (_iambicLock)
        {
            // Store current physical paddle states
            _currentLeftPaddleState = leftPaddle;
            _currentRightPaddleState = rightPaddle;

            // Latch paddle states (Mode B behavior)
            if (leftPaddle)
                _iambicDitLatched = true;
            if (rightPaddle)
                _iambicDahLatched = true;

            // Clear latches when paddles are released
            if (!leftPaddle)
                _iambicDitLatched = false;
            if (!rightPaddle)
                _iambicDahLatched = false;

            // If no paddles pressed and not currently sending, stop
            if (!leftPaddle && !rightPaddle && !_iambicKeyDown)
            {
                StopIambicKeyer();
                return;
            }

            // If timer isn't running and we have paddles pressed, start it
            if (_iambicTimer == null && (leftPaddle || rightPaddle))
            {
                // Determine which element to send first
                if (leftPaddle && rightPaddle)
                {
                    // Both pressed - start with dit
                    _iambicSendingDit = true;
                }
                else if (leftPaddle)
                {
                    _iambicSendingDit = true;
                }
                else
                {
                    _iambicSendingDit = false;
                }

                // Start keying
                SendCWKey(true);
                _iambicKeyDown = true;

                // Calculate element duration
                int elementDuration = _iambicSendingDit ? _ditLength : (_ditLength * 3);

                // Start timer for element duration
                _iambicTimer = new Timer(IambicTimerCallback, null, elementDuration, Timeout.Infinite);
            }
        }
    }

    private void IambicTimerCallback(object state)
    {
        lock (_iambicLock)
        {
            if (_iambicKeyDown)
            {
                // End of element - send key up
                SendCWKey(false);
                _iambicKeyDown = false;

                // Schedule inter-element space
                _iambicTimer?.Change(_ditLength, Timeout.Infinite);
            }
            else
            {
                // End of inter-element space - check if we should send another element
                // Check the latches (which reflect the physical paddle states)
                bool shouldSendDit = _iambicDitLatched;
                bool shouldSendDah = _iambicDahLatched;

                // Decide what to send next
                if (shouldSendDit && shouldSendDah)
                {
                    // Both latched - alternate (Mode B behavior)
                    _iambicSendingDit = !_iambicSendingDit;
                }
                else if (shouldSendDit)
                {
                    _iambicSendingDit = true;
                }
                else if (shouldSendDah)
                {
                    _iambicSendingDit = false;
                }
                else
                {
                    // No more elements to send - stop
                    StopIambicKeyer();
                    return;
                }

                // Send the next element
                SendCWKey(true);
                _iambicKeyDown = true;

                int elementDuration = _iambicSendingDit ? _ditLength : (_ditLength * 3);
                _iambicTimer?.Change(elementDuration, Timeout.Infinite);
            }
        }
    }

    private void StopIambicKeyer()
    {
        lock (_iambicLock)
        {
            if (_iambicTimer != null)
            {
                _iambicTimer.Dispose();
                _iambicTimer = null;
            }

            if (_iambicKeyDown)
            {
                SendCWKey(false);
                _iambicKeyDown = false;
            }

            _iambicDitLatched = false;
            _iambicDahLatched = false;
            _currentLeftPaddleState = false;
            _currentRightPaddleState = false;
        }
    }

    private void SendCWKey(bool state)
    {
        // Control local sidetone
        if (state)
            _sidetoneGenerator?.Start();
        else
            _sidetoneGenerator?.Stop();

        // Send to radio
        if (_connectedRadio != null && _boundGuiClientHandle != 0)
        {
            string timestamp = GetTimestamp();
            _connectedRadio.CWKey(state, timestamp, _boundGuiClientHandle);
        }
    }

    private void Radio_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
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
}
