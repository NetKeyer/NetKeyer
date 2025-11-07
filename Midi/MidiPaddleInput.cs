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

        private IMidiAccess _access;
        private IMidiInput _inputPort;
        private bool _leftPaddleState = false;
        private bool _rightPaddleState = false;

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

            // Reset paddle states
            _leftPaddleState = false;
            _rightPaddleState = false;
        }

        private void OnMidiMessageReceived(object sender, MidiReceivedEventArgs e)
        {
            var data = e.Data;

            if (data.Length < 3)
                return;

            byte status = data[0];
            byte messageType = (byte)(status & 0xF0);
            byte note = data[1];
            byte velocity = data[2];

            if (messageType == NOTE_ON && velocity > 0)
            {
                HandleNoteEvent(note, true);
            }
            else if (messageType == NOTE_OFF || (messageType == NOTE_ON && velocity == 0))
            {
                HandleNoteEvent(note, false);
            }
        }

        private void HandleNoteEvent(int noteNumber, bool isOn)
        {
            bool stateChanged = false;

            if (noteNumber == LEFT_PADDLE_NOTE && _leftPaddleState != isOn)
            {
                _leftPaddleState = isOn;
                stateChanged = true;
            }
            else if (noteNumber == RIGHT_PADDLE_NOTE && _rightPaddleState != isOn)
            {
                _rightPaddleState = isOn;
                stateChanged = true;
            }

            if (stateChanged)
            {
                PaddleStateChanged?.Invoke(this, new PaddleStateChangedEventArgs
                {
                    LeftPaddle = _leftPaddleState,
                    RightPaddle = _rightPaddleState
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
    }
}
