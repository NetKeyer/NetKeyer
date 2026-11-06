using System;
using System.Runtime.InteropServices;
using NetKeyer.Helpers;
using PortAudioSharp;

namespace NetKeyer.Audio
{
    /// <summary>
    /// PortAudio-based sidetone generator using streaming audio with SidetoneProvider.
    /// Used on Linux/macOS for low-latency audio output with shaped waveforms.
    /// Uses ALSA backend on Linux and CoreAudio on macOS for optimal latency.
    /// </summary>
    public class SidetoneGenerator : ISidetoneGenerator
    {
        private Stream _stream;
        private const int DEFAULT_SAMPLE_RATE = 48000;
        private int _sampleRate = DEFAULT_SAMPLE_RATE;
        // Frames per callback (NETKEYER_PA_FRAMES, default 128 = 2.7 ms at 48 kHz; 0 = PortAudio's choice)
        private readonly uint _framesPerBuffer = LatencyOptions.PortAudioFramesPerBuffer;
        private bool _disposed;
        private SidetoneProvider _sidetoneProvider;
        private float[] _readBuffer;
        private readonly object _lock = new object();
        private string _selectedDeviceName;

        public event Action OnSilenceComplete;
        public event Action OnToneStart;
        public event Action OnToneComplete;
        public event Action OnBeforeSilenceEnd;
        public event Action OnBecomeIdle;

        public SidetoneGenerator(string deviceId = null)
        {
            try
            {
                // Initialize PortAudio
                PortAudio.Initialize();

                // Store device name (null/empty means use default)
                _selectedDeviceName = deviceId;

                // Pick the stream sample rate before creating the provider so that
                // patches are generated at the rate the device actually runs at.
                _sampleRate = ChooseSampleRate(FindPortAudioDeviceIndex(_selectedDeviceName));

                // Create the sidetone provider
                _sidetoneProvider = new SidetoneProvider(_sampleRate);

                // Forward events
                _sidetoneProvider.OnSilenceComplete += () => OnSilenceComplete?.Invoke();
                _sidetoneProvider.OnToneStart += () => OnToneStart?.Invoke();
                _sidetoneProvider.OnToneComplete += () => OnToneComplete?.Invoke();
                _sidetoneProvider.OnBeforeSilenceEnd += () => OnBeforeSilenceEnd?.Invoke();
                _sidetoneProvider.OnBecomeIdle += () => OnBecomeIdle?.Invoke();

                // Allocate read buffer for callback (grown in the callback if PortAudio asks for more)
                _readBuffer = new float[Math.Max(_framesPerBuffer, 1024u)];

                // Initialize the audio stream
                InitializeStream();
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to initialize PortAudio: {ex.Message}");
                Dispose();
                throw;
            }
        }

        private void InitializeStream()
        {
            // Use specified device or default
            int device = FindPortAudioDeviceIndex(_selectedDeviceName);
            var deviceInfo = PortAudio.GetDeviceInfo(device);

            double suggestedLatency = LatencyOptions.PortAudioSuggestedLatencyMs is double ms
                ? ms / 1000.0
                : deviceInfo.defaultLowOutputLatency;

            // Fewer ALSA periods = less queued audio (Linux ALSA host API only)
            if (LatencyOptions.PortAudioAlsaPeriods is int periods && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    PaAlsa_SetNumPeriods(periods);
                    DebugLogger.Log("audio", $"PortAudio: PaAlsa_SetNumPeriods({periods})");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("audio", $"PortAudio: PaAlsa_SetNumPeriods failed: {ex.Message}");
                }
            }

            // Configure stream parameters for output
            var streamParams = new StreamParameters
            {
                device = device,
                channelCount = 1, // Mono
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = suggestedLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            // Open stream with callback
            _stream = new Stream(
                inParams: null,
                outParams: streamParams,
                sampleRate: _sampleRate,
                framesPerBuffer: _framesPerBuffer,
                streamFlags: StreamFlags.ClipOff,
                callback: StreamCallback,
                userData: null
            );

            // Start the stream
            _stream.Start();

            DebugLogger.Log("audio", $"PortAudio initialized: device={deviceInfo.name}, hostApi={GetHostApiName(deviceInfo.hostApi)}, " +
                              $"sampleRate={_sampleRate} (device default {deviceInfo.defaultSampleRate}), " +
                              $"framesPerBuffer={(_framesPerBuffer == 0 ? "unspecified" : _framesPerBuffer.ToString())}, " +
                              $"suggestedLatency={suggestedLatency * 1000:F1}ms (device low {deviceInfo.defaultLowOutputLatency * 1000:F1}ms), " +
                              $"actual outputLatency={GetActualOutputLatencyMs(_stream)}");
        }

        private int ChooseSampleRate(int device)
        {
            string setting = LatencyOptions.PortAudioSampleRate;
            if (setting == "device")
            {
                try
                {
                    double rate = PortAudio.GetDeviceInfo(device).defaultSampleRate;
                    if (rate >= 8000 && rate <= 384000)
                        return (int)Math.Round(rate);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("audio", $"PortAudio: could not read device sample rate: {ex.Message}");
                }
                return DEFAULT_SAMPLE_RATE;
            }

            return int.TryParse(setting, out int fixedRate) && fixedRate >= 8000 && fixedRate <= 384000
                ? fixedRate
                : DEFAULT_SAMPLE_RATE;
        }

        // ---- Native PortAudio calls not exposed by PortAudioSharp (diagnostics / tuning) ----

        [StructLayout(LayoutKind.Sequential)]
        private struct PaStreamInfo
        {
            public int structVersion;
            public double inputLatency;
            public double outputLatency;
            public double sampleRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PaHostApiInfo
        {
            public int structVersion;
            public int type;
            public IntPtr name;
            public int deviceCount;
            public int defaultInputDevice;
            public int defaultOutputDevice;
        }

        [DllImport("portaudio")]
        private static extern IntPtr Pa_GetStreamInfo(IntPtr stream);

        [DllImport("portaudio")]
        private static extern IntPtr Pa_GetHostApiInfo(int hostApi);

        [DllImport("portaudio")]
        private static extern void PaAlsa_SetNumPeriods(int numPeriods);

        private static string GetActualOutputLatencyMs(Stream stream)
        {
            try
            {
                var field = typeof(Stream).GetField("streamPtr",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(stream) is IntPtr ptr && ptr != IntPtr.Zero)
                {
                    IntPtr infoPtr = Pa_GetStreamInfo(ptr);
                    if (infoPtr != IntPtr.Zero)
                    {
                        var info = Marshal.PtrToStructure<PaStreamInfo>(infoPtr);
                        return $"{info.outputLatency * 1000:F1}ms @ {info.sampleRate}Hz";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"unknown ({ex.GetType().Name})";
            }
            return "unknown";
        }

        private static string GetHostApiName(int hostApi)
        {
            try
            {
                IntPtr p = Pa_GetHostApiInfo(hostApi);
                if (p != IntPtr.Zero)
                    return Marshal.PtrToStringUTF8(Marshal.PtrToStructure<PaHostApiInfo>(p).name) ?? hostApi.ToString();
            }
            catch { }
            return hostApi.ToString();
        }

        private int FindPortAudioDeviceIndex(string deviceName)
        {
            // If null/empty or "System Default", use default
            if (string.IsNullOrEmpty(deviceName) || deviceName == "System Default")
                return PortAudio.DefaultOutputDevice;

            // Search for device by name
            int deviceCount = PortAudio.DeviceCount;
            for (int i = 0; i < deviceCount; i++)
            {
                var deviceInfo = PortAudio.GetDeviceInfo(i);
                if (deviceInfo.maxOutputChannels > 0 && deviceInfo.name == deviceName)
                    return i;
            }

            // Device not found, fall back to default
            DebugLogger.Log("audio", $"PortAudio device '{deviceName}' not found, using default");
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
                    // Ensure buffer is large enough for requested frames
                    int frames = (int)frameCount;
                    if (_readBuffer == null || _readBuffer.Length < frames)
                    {
                        _readBuffer = new float[frames];
                    }

                    // Read samples from SidetoneProvider
                    _sidetoneProvider.Read(_readBuffer, 0, frames);

                    // Copy to output buffer
                    Marshal.Copy(_readBuffer, 0, output, frames);

                    return StreamCallbackResult.Continue;
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("audio", $"PortAudio callback error: {ex.Message}");
                    return StreamCallbackResult.Abort;
                }
            }
        }


        public void SetFrequency(int frequencyHz)
        {
            if (frequencyHz < 100 || frequencyHz > 2000)
                return;

            _sidetoneProvider?.SetFrequency(frequencyHz);
        }

        public void SetVolume(int volumePercent)
        {
            float volume = Math.Clamp(volumePercent / 100.0f, 0.0f, 1.0f);
            _sidetoneProvider?.SetVolume(volume);
        }

        public void SetWpm(int wpm)
        {
            _sidetoneProvider?.SetWpm(wpm);
        }

        public void Start()
        {
            _sidetoneProvider?.StartIndefiniteTone();
        }

        public void Stop()
        {
            _sidetoneProvider?.Stop();
        }

        public void StartTone(int durationMs)
        {
            _sidetoneProvider?.StartTone(durationMs);
        }

        public void StartSilenceThenTone(int silenceMs, int toneMs)
        {
            _sidetoneProvider?.StartSilenceThenTone(silenceMs, toneMs);
        }

        public void QueueSilence(int silenceMs, int? followingToneMs = null)
        {
            _sidetoneProvider?.QueueSilence(silenceMs, followingToneMs);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Stop and close stream
            if (_stream != null)
            {
                try
                {
                    if (!_stream.IsStopped)
                    {
                        _stream.Stop();
                    }
                    _stream.Close();
                    _stream.Dispose();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("audio", $"Error disposing PortAudio stream: {ex.Message}");
                }
                _stream = null;
            }

            // Terminate PortAudio when done
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
