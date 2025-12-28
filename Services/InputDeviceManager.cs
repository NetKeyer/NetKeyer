using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using NetKeyer.Midi;
using NetKeyer.Models;
using NetKeyer.ViewModels;

namespace NetKeyer.Services;

public class InputDeviceManager : IDisposable
{
    private SerialPort _serialPort;
    private MidiPaddleInput _midiInput;
    private DateTime _inputDeviceOpenedTime = DateTime.MinValue;
    private const int INPUT_GRACE_PERIOD_MS = 100; // Ignore paddle events for this many ms after opening device

    private bool _swapPaddles;

    public bool IsDeviceOpen => (_serialPort != null && _serialPort.IsOpen) || _midiInput != null;
    public InputDeviceType? CurrentDeviceType { get; private set; }

    public event EventHandler<PaddleStateChangedEventArgs> PaddleStateChanged;

    public List<string> DiscoverSerialPorts()
    {
        var ports = new List<string>();

        try
        {
            var portNames = SerialPort.GetPortNames();

            // Remove duplicates (SerialPort.GetPortNames() can return duplicates on some platforms)
            var uniquePorts = portNames.Distinct().ToArray();

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

            ports.AddRange(uniquePorts);

            if (ports.Count == 0)
            {
                ports.Add("No ports found");
            }
        }
        catch (Exception ex)
        {
            ports.Add($"Error: {ex.Message}");
        }

        return ports;
    }

    public List<string> DiscoverMidiDevices()
    {
        var devices = new List<string>();

        try
        {
            var midiDevices = MidiPaddleInput.GetAvailableDevices();
            devices.AddRange(midiDevices);

            if (devices.Count == 0)
            {
                devices.Add("No MIDI devices found");
            }
        }
        catch (Exception ex)
        {
            devices.Add($"MIDI Error: {ex.Message}");
        }

        return devices;
    }

    public void OpenDevice(InputDeviceType deviceType, string deviceName, List<MidiNoteMapping> midiNoteMappings = null)
    {
        CloseDevice();

        if (deviceType == InputDeviceType.Serial)
        {
            OpenSerialPort(deviceName);
        }
        else // MIDI
        {
            OpenMidiDevice(deviceName, midiNoteMappings);
        }

        CurrentDeviceType = deviceType;
    }

    private void OpenSerialPort(string portName)
    {
        if (string.IsNullOrEmpty(portName) || portName.Contains("No ports") || portName.Contains("Error"))
        {
            throw new InvalidOperationException("No serial port selected");
        }

        try
        {
            _serialPort = new SerialPort(portName);
            _serialPort.BaudRate = 9600; // Baud rate doesn't matter for control lines
            _serialPort.PinChanged += SerialPort_PinChanged;
            _serialPort.Open();

            // Mark when we opened the device to enable grace period
            _inputDeviceOpenedTime = DateTime.UtcNow;

            // Emit initial state event with current pin states
            // This ensures indicators update immediately when device is opened
            bool leftPaddle = _serialPort.CtsHolding;
            bool rightPaddle = _serialPort.DsrHolding;

            // Apply swap if enabled
            if (_swapPaddles)
            {
                (leftPaddle, rightPaddle) = (rightPaddle, leftPaddle);
            }

            // For serial input, set StraightKey and PTT to the OR of both paddles
            bool anyPaddle = leftPaddle || rightPaddle;

            PaddleStateChanged?.Invoke(this, new PaddleStateChangedEventArgs
            {
                LeftPaddle = leftPaddle,
                RightPaddle = rightPaddle,
                StraightKey = anyPaddle,
                PTT = anyPaddle
            });
        }
        catch (Exception ex)
        {
            _serialPort = null;
            throw new InvalidOperationException($"Serial port error: {ex.Message}", ex);
        }
    }

    private void OpenMidiDevice(string deviceName, List<MidiNoteMapping> midiNoteMappings)
    {
        if (string.IsNullOrEmpty(deviceName) || deviceName.Contains("No MIDI") || deviceName.Contains("Error"))
        {
            throw new InvalidOperationException("No MIDI device selected");
        }

        try
        {
            _midiInput = new MidiPaddleInput();
            _midiInput.SetNoteMappings(midiNoteMappings);
            _midiInput.PaddleStateChanged += MidiInput_PaddleStateChanged;
            _midiInput.Open(deviceName);

            // Mark when we opened the device to enable grace period
            _inputDeviceOpenedTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _midiInput = null;
            throw new InvalidOperationException($"MIDI device error: {ex.Message}", ex);
        }
    }

    public void CloseDevice()
    {
        CloseSerialPort();
        CloseMidiDevice();
        CurrentDeviceType = null;
    }

    private void CloseSerialPort()
    {
        if (_serialPort != null)
        {
            try
            {
                if (_serialPort.IsOpen)
                    _serialPort.Close();
                _serialPort.PinChanged -= SerialPort_PinChanged;
                _serialPort.Dispose();
            }
            catch { }
            _serialPort = null;
            _inputDeviceOpenedTime = DateTime.MinValue;
        }
    }

    private void CloseMidiDevice()
    {
        if (_midiInput != null)
        {
            try
            {
                _midiInput.PaddleStateChanged -= MidiInput_PaddleStateChanged;
                _midiInput.Close();
                _midiInput.Dispose();
            }
            catch { }
            _midiInput = null;
            _inputDeviceOpenedTime = DateTime.MinValue;
        }
    }

    public void UpdateMidiNoteMappings(List<MidiNoteMapping> mappings)
    {
        _midiInput?.SetNoteMappings(mappings);
    }

    public void SetSwapPaddles(bool swap)
    {
        _swapPaddles = swap;
    }

    private void SerialPort_PinChanged(object sender, SerialPinChangedEventArgs e)
    {
        // Check if we're in the grace period after opening the input device
        bool inGracePeriod = (DateTime.UtcNow - _inputDeviceOpenedTime).TotalMilliseconds < INPUT_GRACE_PERIOD_MS;
        if (inGracePeriod)
        {
            return;
        }

        // HaliKey v1: CTS (left) + DSR (right)
        if (e.EventType == SerialPinChange.CtsChanged || e.EventType == SerialPinChange.DsrChanged)
        {
            try
            {
                // Read current pin states
                bool leftPaddle = _serialPort.CtsHolding;
                bool rightPaddle = _serialPort.DsrHolding;

                // Apply swap if enabled
                if (_swapPaddles)
                {
                    (leftPaddle, rightPaddle) = (rightPaddle, leftPaddle);
                }

                // Emit unified PaddleStateChanged event
                // For serial input, set StraightKey and PTT to the OR of both paddles
                // This allows any paddle to trigger straight key or PTT mode
                bool anyPaddle = leftPaddle || rightPaddle;

                PaddleStateChanged?.Invoke(this, new PaddleStateChangedEventArgs
                {
                    LeftPaddle = leftPaddle,
                    RightPaddle = rightPaddle,
                    StraightKey = anyPaddle,
                    PTT = anyPaddle
                });
            }
            catch { }
        }
    }

    private void MidiInput_PaddleStateChanged(object sender, PaddleStateChangedEventArgs e)
    {
        // Check if we're in the grace period after opening the input device
        bool inGracePeriod = (DateTime.UtcNow - _inputDeviceOpenedTime).TotalMilliseconds < INPUT_GRACE_PERIOD_MS;
        if (inGracePeriod)
        {
            return;
        }

        // Apply swap if enabled (only affects paddles, not straight key or PTT)
        bool leftPaddle = e.LeftPaddle;
        bool rightPaddle = e.RightPaddle;

        if (_swapPaddles)
        {
            (leftPaddle, rightPaddle) = (rightPaddle, leftPaddle);
        }

        PaddleStateChanged?.Invoke(this, new PaddleStateChangedEventArgs
        {
            LeftPaddle = leftPaddle,
            RightPaddle = rightPaddle,
            StraightKey = e.StraightKey,
            PTT = e.PTT
        });
    }

    public void Dispose()
    {
        CloseDevice();
    }
}
