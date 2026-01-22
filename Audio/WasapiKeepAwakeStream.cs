using System;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NetKeyer.Helpers;

namespace NetKeyer.Audio
{
    /// <summary>
    /// Windows WASAPI implementation of keep-awake stream.
    /// Plays a -96dB square wave to keep the audio device from sleeping.
    /// Uses shared mode with higher latency since responsiveness is not needed.
    /// </summary>
    public class WasapiKeepAwakeStream : IKeepAwakeStream
    {
        private WasapiOut _wasapiOut;
        private KeepAwakeSampleProvider _sampleProvider;
        private MMDeviceEnumerator _deviceEnumerator;
        private bool _disposed;
        private bool _isPlaying;
        private string _selectedDeviceId;

        private const int SAMPLE_RATE = 48000;
        private const int LATENCY_MS = 200; // Higher latency is fine for keep-awake

        public bool IsPlaying => _isPlaying;

        public WasapiKeepAwakeStream(string deviceId = null)
        {
            _selectedDeviceId = deviceId;
            _deviceEnumerator = new MMDeviceEnumerator();
            _sampleProvider = new KeepAwakeSampleProvider();
            InitializeWasapiOut();
        }

        private void InitializeWasapiOut()
        {
            MMDevice device;

            if (string.IsNullOrEmpty(_selectedDeviceId) || _selectedDeviceId == "System Default")
            {
                device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            else
            {
                device = FindDeviceByName(_selectedDeviceId);
                if (device == null)
                {
                    DebugLogger.Log("audio", $"Keep-awake: Device '{_selectedDeviceId}' not found, using default");
                    device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
            }

            _wasapiOut = new WasapiOut(
                device,
                AudioClientShareMode.Shared,
                true,
                LATENCY_MS
            );

            _wasapiOut.Init(_sampleProvider);
            DebugLogger.Log("audio", $"Keep-awake WASAPI initialized: device={device.FriendlyName}");
        }

        private MMDevice FindDeviceByName(string friendlyName)
        {
            var collection = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in collection)
            {
                if (device.FriendlyName == friendlyName)
                    return device;
            }
            return null;
        }

        public void Start()
        {
            if (_disposed || _wasapiOut == null || _isPlaying)
                return;

            try
            {
                _wasapiOut.Play();
                _isPlaying = true;
                DebugLogger.Log("audio", "Keep-awake stream started");
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to start keep-awake stream: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (_disposed || _wasapiOut == null || !_isPlaying)
                return;

            try
            {
                _wasapiOut.Stop();
                _isPlaying = false;
                DebugLogger.Log("audio", "Keep-awake stream stopped");
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to stop keep-awake stream: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

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

            _deviceEnumerator?.Dispose();
            _deviceEnumerator = null;
        }
    }

    /// <summary>
    /// Sample provider that generates a near-silent square wave at 2^-15 amplitude.
    /// </summary>
    internal class KeepAwakeSampleProvider : ISampleProvider
    {
        private const int SAMPLE_RATE = 48000;
        private const float AMPLITUDE = 1f / 32768f; // 2^-15
        private const int FREQUENCY = 100; // Low frequency square wave
        private int _sampleIndex;
        private readonly int _samplesPerHalfPeriod;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SAMPLE_RATE, 1);

        public KeepAwakeSampleProvider()
        {
            _samplesPerHalfPeriod = SAMPLE_RATE / (FREQUENCY * 2);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                // Generate square wave: alternating +AMPLITUDE and -AMPLITUDE
                int periodPosition = _sampleIndex % (2 * _samplesPerHalfPeriod);
                buffer[offset + i] = periodPosition < _samplesPerHalfPeriod ? AMPLITUDE : -AMPLITUDE;
                _sampleIndex++;
            }
            return count;
        }
    }
}
