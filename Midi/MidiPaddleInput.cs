using System;
using System.Collections.Generic;
using System.Linq;
using NetKeyer.Helpers;
using NetKeyer.Midi.LibreMidi;
using NetKeyer.Models;

namespace NetKeyer.Midi
{
    public class MidiPaddleInput : IDisposable
    {
        private const byte NOTE_ON = 0x90;
        private const byte NOTE_OFF = 0x80;

        private LibreMidiInput _libreMidi;
        private bool _leftPaddleState = false;
        private bool _rightPaddleState = false;
        private bool _straightKeyState = false;
        private bool _pttState = false;

        private List<MidiNoteMapping> _noteMappings;

        public event EventHandler<PaddleStateChangedEventArgs> PaddleStateChanged;

        public static List<string> GetAvailableDevices()
        {
            try
            {
                return LibreMidiInput.GetAvailableDevices();
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
                _libreMidi = new LibreMidiInput();
                _libreMidi.MessageReceived += OnMidiMessage;
                _libreMidi.Open(deviceName);

                Console.WriteLine($"Opened MIDI device: {deviceName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open MIDI device: {ex.Message}");
                _libreMidi?.Dispose();
                _libreMidi = null;
                throw;
            }
        }

        public void Close()
        {
            if (_libreMidi != null)
            {
                try
                {
                    _libreMidi.MessageReceived -= OnMidiMessage;
                    _libreMidi.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error closing MIDI device: {ex.Message}");
                }
                finally
                {
                    _libreMidi.Dispose();
                    _libreMidi = null;
                }
            }

            // Reset all states
            _leftPaddleState = false;
            _rightPaddleState = false;
            _straightKeyState = false;
            _pttState = false;
        }

        // libremidi delivers one complete MIDI message per callback, with SysEx,
        // timing, and active-sensing already filtered by the shim.  No manual
        // running-status or multi-packet parsing is needed here.
        private void OnMidiMessage(byte[] data)
        {
            if (data.Length < 3) return;
            byte messageType = (byte)(data[0] & 0xF0);
            byte note = data[1];
            if (messageType == NOTE_ON)
                HandleNoteEvent(note, true);  // HaliKey quirk: velocity 0 still treated as ON
            else if (messageType == NOTE_OFF)
                HandleNoteEvent(note, false);
        }

        private void HandleNoteEvent(int noteNumber, bool isOn)
        {
            DebugLogger.Log("midi", $"[MIDI] Note {noteNumber} {(isOn ? "ON" : "OFF")}");

            // Find all mappings for this note
            var mapping = _noteMappings?.FirstOrDefault(m => m.NoteNumber == noteNumber);
            if (mapping == null)
            {
                DebugLogger.Log("midi", $"[MIDI] Ignoring unmapped note {noteNumber}");
                return;
            }

            bool stateChanged = false;

            // Update states based on mapped functions
            if (mapping.HasFunction(MidiNoteFunction.LeftPaddle))
            {
                // Handle note OFF when we didn't see note ON
                if (!isOn && !_leftPaddleState)
                {
                    DebugLogger.Log("midi", $"[MIDI] Left paddle OFF without ON - treating as brief press/release");
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
                    DebugLogger.Log("midi", $"[MIDI] Left paddle -> {isOn}");
                }
            }

            if (mapping.HasFunction(MidiNoteFunction.RightPaddle))
            {
                // Handle note OFF when we didn't see note ON
                if (!isOn && !_rightPaddleState)
                {
                    DebugLogger.Log("midi", $"[MIDI] Right paddle OFF without ON - treating as brief press/release");
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
                    DebugLogger.Log("midi", $"[MIDI] Right paddle -> {isOn}");
                }
            }

            if (mapping.HasFunction(MidiNoteFunction.StraightKey))
            {
                if (_straightKeyState != isOn)
                {
                    _straightKeyState = isOn;
                    stateChanged = true;
                    DebugLogger.Log("midi", $"[MIDI] Straight key -> {isOn}");
                }
            }

            if (mapping.HasFunction(MidiNoteFunction.PTT))
            {
                if (_pttState != isOn)
                {
                    _pttState = isOn;
                    stateChanged = true;
                    DebugLogger.Log("midi", $"[MIDI] PTT -> {isOn}");
                }
            }

            // Fire event if any state changed
            if (stateChanged)
            {
                DebugLogger.Log("midi", $"[MIDI] Firing event: L={_leftPaddleState} R={_rightPaddleState} SK={_straightKeyState} PTT={_pttState}");
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
