using System;
using System.Collections.Generic;
using System.Linq;
using Commons.Music.Midi;
using NetKeyer.Models;

namespace NetKeyer.Midi
{
    public class MidiPaddleInput : IDisposable
    {
        private const byte NOTE_ON = 0x90;
        private const byte NOTE_OFF = 0x80;
        private const bool DEBUG_MIDI = false; // Set to true to enable MIDI debug logging

        private IMidiAccess _access;
        private IMidiInput _inputPort;
        private bool _leftPaddleState = false;
        private bool _rightPaddleState = false;
        private bool _straightKeyState = false;
        private bool _pttState = false;
        private byte _lastStatusByte = 0; // Track last status byte for running status

        private List<MidiNoteMapping> _noteMappings;

        public event EventHandler<PaddleStateChangedEventArgs> PaddleStateChanged;

        public static List<string> GetAvailableDevices()
        {
            try
            {
                var access = MidiAccessManager.Default;
                return access.Inputs.Select(d => d.Name).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enumerating MIDI devices: {ex.Message}");
                return new List<string>();
            }
        }

        public void SetNoteMappings(List<MidiNoteMapping> mappings)
        {
            _noteMappings = mappings ?? MidiNoteMapping.GetDefaultMappings();
        }

        public void Open(string deviceName)
        {
            Close();

            // Ensure we have note mappings
            if (_noteMappings == null)
            {
                _noteMappings = MidiNoteMapping.GetDefaultMappings();
            }

            try
            {
                _access = MidiAccessManager.Default;
                var device = _access.Inputs.FirstOrDefault(d => d.Name == deviceName);

                if (device == null)
                {
                    throw new InvalidOperationException($"MIDI device '{deviceName}' not found");
                }

                _inputPort = _access.OpenInputAsync(device.Id).Result;
                _inputPort.MessageReceived += OnMidiMessageReceived;

                Console.WriteLine($"Opened MIDI device: {deviceName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open MIDI device: {ex.Message}");
                throw;
            }
        }

        public void Close()
        {
            if (_inputPort != null)
            {
                try
                {
                    _inputPort.MessageReceived -= OnMidiMessageReceived;
                    _inputPort.CloseAsync().Wait();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error closing MIDI device: {ex.Message}");
                }
                finally
                {
                    _inputPort = null;
                }
            }

            // Reset all states and running status
            _leftPaddleState = false;
            _rightPaddleState = false;
            _straightKeyState = false;
            _pttState = false;
            _lastStatusByte = 0;
        }

        private void OnMidiMessageReceived(object sender, MidiReceivedEventArgs e)
        {
            try
            {
                var data = e.Data;
                var start = e.Start;
                var length = e.Length;

                if (length == 0)
                    return;

                if (DEBUG_MIDI)
                {
                    var relevantBytes = data.Skip(start).Take(length);
                    Console.WriteLine($"[MIDI Raw] data.Length={data.Length} start={start} length={length} data=[{string.Join(" ", relevantBytes.Select(b => $"{b:X2}"))}]");
                }

                // Parse all MIDI messages in the buffer
                int pos = start;
                int end = start + length;

                while (pos < end)
                {
                    byte statusByte;
                    int dataStart;

                    // Check if this is a status byte or running status
                    if (data[pos] >= 0x80)
                    {
                        // New status byte
                        statusByte = data[pos];
                        _lastStatusByte = statusByte;
                        dataStart = pos + 1;
                        if (DEBUG_MIDI)
                            Console.WriteLine($"[MIDI] Status byte 0x{statusByte:X2} at pos {pos}");
                    }
                    else
                    {
                        // Running status - reuse last status byte
                        statusByte = _lastStatusByte;
                        dataStart = pos;
                        if (DEBUG_MIDI)
                            Console.WriteLine($"[MIDI] Running status, reusing 0x{statusByte:X2}");
                    }

                    // Get the number of data bytes for this message type
                    int dataSize = MidiEvent.FixedDataSize(statusByte);
                    if (DEBUG_MIDI)
                        Console.WriteLine($"[MIDI] Message requires {dataSize} data bytes");

                    // Make sure we have enough data
                    if (dataStart + dataSize > end)
                    {
                        Console.WriteLine($"[MIDI ERROR] Incomplete message: need {dataSize} bytes but only {end - dataStart} available");
                        break;
                    }

                    // Parse the message based on type
                    byte messageType = (byte)(statusByte & 0xF0);

                    if (dataSize >= 1)
                    {
                        byte note = data[dataStart];
                        byte velocity = dataSize >= 2 ? data[dataStart + 1] : (byte)0;

                        if (DEBUG_MIDI)
                            Console.WriteLine($"[MIDI Event] StatusByte=0x{statusByte:X2} Note={note} Velocity={velocity}");

                        if (messageType == NOTE_ON)
                        {
                            HandleNoteEvent(note, true);
                        }
                        else if (messageType == NOTE_OFF)
                        {
                            HandleNoteEvent(note, false);
                        }
                    }

                    // Move to next message
                    pos = dataStart + dataSize;
                }

                if (DEBUG_MIDI)
                    Console.WriteLine($"[MIDI] OnMidiMessageReceived complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MIDI ERROR] Exception in OnMidiMessageReceived: {ex.Message}");
                Console.WriteLine($"[MIDI ERROR] Stack trace: {ex.StackTrace}");
            }
        }

        private void HandleNoteEvent(int noteNumber, bool isOn)
        {
            if (DEBUG_MIDI)
                Console.WriteLine($"[MIDI] Note {noteNumber} {(isOn ? "ON" : "OFF")}");

            // Find all mappings for this note
            var mapping = _noteMappings?.FirstOrDefault(m => m.NoteNumber == noteNumber);
            if (mapping == null)
            {
                if (DEBUG_MIDI)
                    Console.WriteLine($"[MIDI] Ignoring unmapped note {noteNumber}");
                return;
            }

            bool stateChanged = false;

            // Update states based on mapped functions
            if (mapping.HasFunction(MidiNoteFunction.LeftPaddle))
            {
                // Handle note OFF when we didn't see note ON
                if (!isOn && !_leftPaddleState)
                {
                    if (DEBUG_MIDI)
                        Console.WriteLine($"[MIDI] Left paddle OFF without ON - treating as brief press/release");
                    _leftPaddleState = true;
                    stateChanged = true;
                    PaddleStateChanged?.Invoke(this, new PaddleStateChangedEventArgs
                    {
                        LeftPaddle = _leftPaddleState,
                        RightPaddle = _rightPaddleState,
                        StraightKey = _straightKeyState,
                        PTT = _pttState
                    });
                }

                if (_leftPaddleState != isOn)
                {
                    _leftPaddleState = isOn;
                    stateChanged = true;
                    if (DEBUG_MIDI)
                        Console.WriteLine($"[MIDI] Left paddle -> {isOn}");
                }
            }

            if (mapping.HasFunction(MidiNoteFunction.RightPaddle))
            {
                // Handle note OFF when we didn't see note ON
                if (!isOn && !_rightPaddleState)
                {
                    if (DEBUG_MIDI)
                        Console.WriteLine($"[MIDI] Right paddle OFF without ON - treating as brief press/release");
                    _rightPaddleState = true;
                    stateChanged = true;
                    PaddleStateChanged?.Invoke(this, new PaddleStateChangedEventArgs
                    {
                        LeftPaddle = _leftPaddleState,
                        RightPaddle = _rightPaddleState,
                        StraightKey = _straightKeyState,
                        PTT = _pttState
                    });
                }

                if (_rightPaddleState != isOn)
                {
                    _rightPaddleState = isOn;
                    stateChanged = true;
                    if (DEBUG_MIDI)
                        Console.WriteLine($"[MIDI] Right paddle -> {isOn}");
                }
            }

            if (mapping.HasFunction(MidiNoteFunction.StraightKey))
            {
                if (_straightKeyState != isOn)
                {
                    _straightKeyState = isOn;
                    stateChanged = true;
                    if (DEBUG_MIDI)
                        Console.WriteLine($"[MIDI] Straight key -> {isOn}");
                }
            }

            if (mapping.HasFunction(MidiNoteFunction.PTT))
            {
                if (_pttState != isOn)
                {
                    _pttState = isOn;
                    stateChanged = true;
                    if (DEBUG_MIDI)
                        Console.WriteLine($"[MIDI] PTT -> {isOn}");
                }
            }

            // Fire event if any state changed
            if (stateChanged)
            {
                if (DEBUG_MIDI)
                    Console.WriteLine($"[MIDI] Firing event: L={_leftPaddleState} R={_rightPaddleState} SK={_straightKeyState} PTT={_pttState}");
                PaddleStateChanged?.Invoke(this, new PaddleStateChangedEventArgs
                {
                    LeftPaddle = _leftPaddleState,
                    RightPaddle = _rightPaddleState,
                    StraightKey = _straightKeyState,
                    PTT = _pttState
                });
            }
        }

        public void Dispose()
        {
            Close();
        }
    }

    public class PaddleStateChangedEventArgs : EventArgs
    {
        public bool LeftPaddle { get; set; }
        public bool RightPaddle { get; set; }
        public bool StraightKey { get; set; }
        public bool PTT { get; set; }
    }
}
