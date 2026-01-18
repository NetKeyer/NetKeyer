using System;
using System.Runtime.InteropServices;
using NetKeyer.Helpers;
using PortAudioSharp;

namespace NetKeyer.Audio
{
    /// <summary>
    /// PortAudio implementation of keep-awake stream for Linux/macOS.
    /// Plays a 2^-15 amplitude square wave to keep the audio device from sleeping.
    /// </summary>
    public class PortAudioKeepAwakeStream : IKeepAwakeStream
    {
        private Stream _stream;
        private bool _disposed;
        private bool _isPlaying;
        private string _selectedDeviceName;
        private readonly object _lock = new object();

        private const int SAMPLE_RATE = 48000;
        private const int BUFFER_SAMPLES = 1024; // Larger buffer is fine for keep-awake
        private const float AMPLITUDE = 1f / 32768f; // 2^-15
        private const int FREQUENCY = 100; // Low frequency square wave

        private int _sampleIndex;
        private readonly int _samplesPerHalfPeriod;
        private float[] _outputBuffer;

        public bool IsPlaying => _isPlaying;

        public PortAudioKeepAwakeStream(string deviceId = null)
        {
            _selectedDeviceName = deviceId;
            _samplesPerHalfPeriod = SAMPLE_RATE / (FREQUENCY * 2);
            _outputBuffer = new float[BUFFER_SAMPLES];

            try
            {
                PortAudio.Initialize();
                InitializeStream();
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to initialize PortAudio keep-awake: {ex.Message}");
                Dispose();
                throw;
            }
        }

        private void InitializeStream()
        {
            int device = FindPortAudioDeviceIndex(_selectedDeviceName);
            var deviceInfo = PortAudio.GetDeviceInfo(device);

            var streamParams = new StreamParameters
            {
                device = device,
                channelCount = 1,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = deviceInfo.defaultHighOutputLatency, // Higher latency is fine
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            _stream = new Stream(
                inParams: null,
                outParams: streamParams,
                sampleRate: SAMPLE_RATE,
                framesPerBuffer: BUFFER_SAMPLES,
                streamFlags: StreamFlags.ClipOff,
                callback: StreamCallback,
                userData: null
            );

            DebugLogger.Log("audio", $"Keep-awake PortAudio initialized: device={deviceInfo.name}");
        }

        private int FindPortAudioDeviceIndex(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName) || deviceName == "System Default")
                return PortAudio.DefaultOutputDevice;

            int deviceCount = PortAudio.DeviceCount;
            for (int i = 0; i < deviceCount; i++)
            {
                var deviceInfo = PortAudio.GetDeviceInfo(i);
                if (deviceInfo.maxOutputChannels > 0 && deviceInfo.name == deviceName)
                    return i;
            }

            DebugLogger.Log("audio", $"Keep-awake: PortAudio device '{deviceName}' not found, using default");
            return PortAudio.DefaultOutputDevice;
        }

        private StreamCallbackResult StreamCallback(
            IntPtr input,
            IntPtr output,
            uint frameCount,
            ref StreamCallbackTimeInfo timeInfo,
            StreamCallbackFlags statusFlags,
            IntPtr userData)
        {
            lock (_lock)
            {
                try
                {
                    int frames = (int)frameCount;
                    if (_outputBuffer == null || _outputBuffer.Length < frames)
                    {
                        _outputBuffer = new float[frames];
                    }

                    // Generate square wave
                    for (int i = 0; i < frames; i++)
                    {
                        int periodPosition = _sampleIndex % (2 * _samplesPerHalfPeriod);
                        _outputBuffer[i] = periodPosition < _samplesPerHalfPeriod ? AMPLITUDE : -AMPLITUDE;
                        _sampleIndex++;
                    }

                    Marshal.Copy(_outputBuffer, 0, output, frames);
                    return StreamCallbackResult.Continue;
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("audio", $"Keep-awake PortAudio callback error: {ex.Message}");
                    return StreamCallbackResult.Abort;
                }
            }
        }

        public void Start()
        {
            if (_disposed || _stream == null || _isPlaying)
                return;

            try
            {
                _stream.Start();
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
            if (_disposed || _stream == null || !_isPlaying)
                return;

            try
            {
                _stream.Stop();
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

            if (_stream != null)
            {
                try
                {
                    if (_isPlaying && !_stream.IsStopped)
                    {
                        _stream.Stop();
                    }
                    _stream.Close();
                    _stream.Dispose();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("audio", $"Error disposing keep-awake PortAudio stream: {ex.Message}");
                }
                _stream = null;
            }

            try
            {
                PortAudio.Terminate();
            }
            catch
            {
                // Ignore termination errors
            }
        }
    }
}
