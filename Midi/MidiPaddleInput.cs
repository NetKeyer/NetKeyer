using System;
using System.Collections.Generic;
using System.Linq;
using Commons.Music.Midi;

namespace NetKeyer.Midi
{
    public class MidiPaddleInput : IDisposable
    {
        private const int LEFT_PADDLE_NOTE = 20;
        private const int RIGHT_PADDLE_NOTE = 21;
        private const byte NOTE_ON = 0x90;
        private const byte NOTE_OFF = 0x80;
        private const bool DEBUG_MIDI = false; // Set to true to enable MIDI debug logging

        private IMidiAccess _access;
        private IMidiInput _inputPort;
        private bool _leftPaddleState = false;
        private bool _rightPaddleState = false;
        private byte _lastStatusByte = 0; // Track last status byte for running status

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

        public void Open(string deviceName)
        {
            Close();

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

            // Reset paddle states and running status
            _leftPaddleState = false;
            _rightPaddleState = false;
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

            if (noteNumber == LEFT_PADDLE_NOTE)
            {
                // Handle note OFF when we didn't see note ON - this means the paddle
                // was pressed while the other paddle was already held (MIDI device limitation)
                if (!isOn && !_leftPaddleState)
                {
                    if (DEBUG_MIDI)
                        Console.WriteLine($"[MIDI] Left paddle OFF without ON - treating as brief press/release");
                    // Fire event for both ON and OFF to simulate the missed press
                    _leftPaddleState = true;
                    PaddleStateChanged?.Invoke(this, new PaddleStateChangedEventArgs
                    {
                        LeftPaddle = _leftPaddleState,
                        RightPaddle = _rightPaddleState
                    });
                }

                _leftPaddleState = isOn;
                if (DEBUG_MIDI)
                    Console.WriteLine($"[MIDI] Left paddle -> {isOn}");
            }
            else if (noteNumber == RIGHT_PADDLE_NOTE)
            {
                // Handle note OFF when we didn't see note ON
                if (!isOn && !_rightPaddleState)
                {
                    if (DEBUG_MIDI)
                        Console.WriteLine($"[MIDI] Right paddle OFF without ON - treating as brief press/release");
                    _rightPaddleState = true;
                    PaddleStateChanged?.Invoke(this, new PaddleStateChangedEventArgs
                    {
                        LeftPaddle = _leftPaddleState,
                        RightPaddle = _rightPaddleState
                    });
                }

                _rightPaddleState = isOn;
                if (DEBUG_MIDI)
                    Console.WriteLine($"[MIDI] Right paddle -> {isOn}");
            }
            else
            {
                if (DEBUG_MIDI)
                    Console.WriteLine($"[MIDI] Ignoring unknown note {noteNumber}");
                return; // Don't fire event for unknown notes
            }

            if (DEBUG_MIDI)
                Console.WriteLine($"[MIDI] Firing event: L={_leftPaddleState} R={_rightPaddleState}");
            PaddleStateChanged?.Invoke(this, new PaddleStateChangedEventArgs
            {
                LeftPaddle = _leftPaddleState,
                RightPaddle = _rightPaddleState
            });
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
    }
}
