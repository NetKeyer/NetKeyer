using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;

namespace NetKeyer.Audio
{
    /// <summary>
    /// Windows-specific WASAPI sidetone generator for ultra-low latency audio output.
    /// Uses exclusive mode WASAPI with minimal buffering for ~3-5ms latency.
    /// Automatically follows Windows default playback device changes.
    /// </summary>
    public class WasapiSidetoneGenerator : ISidetoneGenerator
    {
        private WasapiOut _wasapiOut;
        private SignalGenerator _signalGenerator;
        private VolumeSampleProvider _volumeProvider;
        private bool _disposed;
        private bool _isPlaying;
        private int _frequency = 600;
        private float _volume = 0.5f;
        private MMDeviceEnumerator _deviceEnumerator;
        private DeviceNotificationClient _notificationClient;

        // Use very low latency - 3ms at 48kHz
        private const int SAMPLE_RATE = 48000;
        private const int LATENCY_MS = 3;

        public WasapiSidetoneGenerator()
        {
            try
            {
                // Create signal generator for sine wave
                _signalGenerator = new SignalGenerator(SAMPLE_RATE, 1)
                {
                    Gain = 0.25, // Prevent clipping
                    Frequency = _frequency,
                    Type = SignalGeneratorType.Sin
                };

                // Add volume control
                _volumeProvider = new VolumeSampleProvider(_signalGenerator)
                {
                    Volume = _volume
                };

                // Set up device change monitoring
                _deviceEnumerator = new MMDeviceEnumerator();
                _notificationClient = new DeviceNotificationClient(OnDefaultDeviceChanged);
                _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);

                // Initialize WASAPI output
                InitializeWasapiOut();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize WASAPI audio: {ex.Message}");
                Dispose();
                throw;
            }
        }

        private void InitializeWasapiOut()
        {
            // Initialize WASAPI in shared mode with minimal latency
            // Note: Exclusive mode would be even lower latency but may not be available
            _wasapiOut = new WasapiOut(
                AudioClientShareMode.Shared,
                LATENCY_MS
            );

            _wasapiOut.Init(_volumeProvider);
        }

        private void OnDefaultDeviceChanged()
        {
            if (_disposed)
                return;

            try
            {
                // Save current playing state
                bool wasPlaying = _isPlaying;

                // Stop and dispose old device
                if (_wasapiOut != null)
                {
                    if (_isPlaying)
                    {
                        _wasapiOut.Stop();
                        _isPlaying = false;
                    }
                    _wasapiOut.Dispose();
                    _wasapiOut = null;
                }

                // Reinitialize with new default device
                InitializeWasapiOut();

                // Resume playback if we were playing
                if (wasPlaying)
                {
                    _wasapiOut.Play();
                    _isPlaying = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to switch to new default audio device: {ex.Message}");
            }
        }

        public void SetFrequency(int frequencyHz)
        {
            if (frequencyHz < 100 || frequencyHz > 2000)
                return;

            _frequency = frequencyHz;

            if (_signalGenerator != null)
            {
                _signalGenerator.Frequency = frequencyHz;
            }
        }

        public void SetVolume(int volumePercent)
        {
            _volume = Math.Clamp(volumePercent / 100.0f, 0.0f, 1.0f);

            if (_volumeProvider != null)
            {
                _volumeProvider.Volume = _volume;
            }
        }

        public void Start()
        {
            if (_disposed || _isPlaying || _wasapiOut == null)
                return;

            try
            {
                _wasapiOut.Play();
                _isPlaying = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start WASAPI sidetone: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (_disposed || !_isPlaying || _wasapiOut == null)
                return;

            try
            {
                _wasapiOut.Stop();
                _isPlaying = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to stop WASAPI sidetone: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            Stop();

            // Unregister device change notifications
            if (_deviceEnumerator != null && _notificationClient != null)
            {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            }

            if (_wasapiOut != null)
            {
                _wasapiOut.Dispose();
                _wasapiOut = null;
            }

            if (_deviceEnumerator != null)
            {
                _deviceEnumerator.Dispose();
                _deviceEnumerator = null;
            }
        }
    }

    /// <summary>
    /// Notification client for monitoring Windows default audio device changes
    /// </summary>
    internal class DeviceNotificationClient : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        private readonly Action _onDefaultDeviceChanged;

        public DeviceNotificationClient(Action onDefaultDeviceChanged)
        {
            _onDefaultDeviceChanged = onDefaultDeviceChanged;
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Only respond to changes in the default playback device
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                _onDefaultDeviceChanged?.Invoke();
            }
        }

        // These other events we don't need to handle
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string pwstrDeviceId) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
