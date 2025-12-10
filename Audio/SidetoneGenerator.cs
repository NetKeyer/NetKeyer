using System;
using System.Runtime.InteropServices;
using PortAudioSharp;

namespace NetKeyer.Audio
{
    /// <summary>
    /// PortAudio-based sidetone generator using streaming audio with SidetoneProvider.
    /// Used on Linux/macOS for low-latency audio output with shaped waveforms.
    /// Uses ALSA backend on Linux and CoreAudio on macOS for optimal latency.
    /// Automatically follows default audio device changes on macOS.
    /// </summary>
    public class SidetoneGenerator : ISidetoneGenerator
    {
        private Stream _stream;
        private const int SAMPLE_RATE = 48000;
        private const int BUFFER_SAMPLES = 256; // ~5.3 ms buffer at 48 kHz for low latency
        private bool _disposed;
        private SidetoneProvider _sidetoneProvider;
        private float[] _readBuffer;
        private readonly object _lock = new object();
        private CoreAudioDeviceMonitor _deviceMonitor;

        public event Action OnSilenceComplete;
        public event Action OnToneStart;
        public event Action OnToneComplete;
        public event Action OnBecomeIdle;

        public SidetoneGenerator()
        {
            try
            {
                // Initialize PortAudio
                PortAudio.Initialize();

                // Create the sidetone provider
                _sidetoneProvider = new SidetoneProvider();

                // Forward events
                _sidetoneProvider.OnSilenceComplete += () => OnSilenceComplete?.Invoke();
                _sidetoneProvider.OnToneStart += () => OnToneStart?.Invoke();
                _sidetoneProvider.OnToneComplete += () => OnToneComplete?.Invoke();
                _sidetoneProvider.OnBecomeIdle += () => OnBecomeIdle?.Invoke();

                // Allocate read buffer for callback
                _readBuffer = new float[BUFFER_SAMPLES];

                // Initialize the audio stream
                InitializeStream();

                // Set up device change monitoring on macOS
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    _deviceMonitor = new CoreAudioDeviceMonitor(OnDefaultDeviceChanged);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize PortAudio: {ex.Message}");
                Dispose();
                throw;
            }
        }

        private void InitializeStream()
        {
            // Get default output device info for latency
            var deviceInfo = PortAudio.GetDeviceInfo(PortAudio.DefaultOutputDevice);

            // Configure stream parameters for output
            var streamParams = new StreamParameters
            {
                device = PortAudio.DefaultOutputDevice,
                channelCount = 1, // Mono
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = deviceInfo.defaultLowOutputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            // Open stream with callback
            _stream = new Stream(
                inParams: null,
                outParams: streamParams,
                sampleRate: SAMPLE_RATE,
                framesPerBuffer: BUFFER_SAMPLES,
                streamFlags: StreamFlags.ClipOff,
                callback: StreamCallback,
                userData: null
            );

            // Start the stream
            _stream.Start();

            Console.WriteLine($"PortAudio initialized: device={deviceInfo.name}, " +
                              $"latency={deviceInfo.defaultLowOutputLatency * 1000:F1}ms, bufferSize={BUFFER_SAMPLES}");
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
                    // Read samples from SidetoneProvider
                    _sidetoneProvider.Read(_readBuffer, 0, (int)frameCount);

                    // Copy to output buffer
                    Marshal.Copy(_readBuffer, 0, output, (int)frameCount);

                    return StreamCallbackResult.Continue;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"PortAudio callback error: {ex.Message}");
                    return StreamCallbackResult.Abort;
                }
            }
        }

        private void OnDefaultDeviceChanged()
        {
            if (_disposed)
                return;

            try
            {
                Console.WriteLine("Default audio device changed, reinitializing PortAudio...");

                lock (_lock)
                {
                    // Stop and close the old stream
                    if (_stream != null)
                    {
                        if (!_stream.IsStopped)
                        {
                            _stream.Stop();
                        }
                        _stream.Close();
                        _stream.Dispose();
                        _stream = null;
                    }

                    // Reinitialize with the new default device
                    InitializeStream();
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

            // Dispose device monitor on macOS
            if (_deviceMonitor != null)
            {
                _deviceMonitor.Dispose();
                _deviceMonitor = null;
            }

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
                    Console.WriteLine($"Error disposing PortAudio stream: {ex.Message}");
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
