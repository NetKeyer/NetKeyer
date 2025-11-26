using System;
using System.Threading;
using OpenTK.Audio.OpenAL;

namespace NetKeyer.Audio
{
    /// <summary>
    /// OpenAL-based sidetone generator using streaming audio with SidetoneProvider.
    /// Used on Linux/macOS for low-latency audio output with shaped waveforms.
    /// </summary>
    public class SidetoneGenerator : ISidetoneGenerator
    {
        private ALDevice _device;
        private ALContext _context;
        private int _source;
        private int[] _buffers;
        private const int NUM_BUFFERS = 3;
        private const int SAMPLE_RATE = 48000;
        private const int BUFFER_SAMPLES = 256;
        private bool _disposed;
        private Thread _streamingThread;
        private bool _shouldStop;
        private SidetoneProvider _sidetoneProvider;

        public SidetoneGenerator()
        {
            try
            {
                // Create the sidetone provider
                _sidetoneProvider = new SidetoneProvider();

                // Open default audio device
                _device = ALC.OpenDevice(null);
                if (_device == ALDevice.Null)
                {
                    throw new InvalidOperationException("Failed to open audio device");
                }

                // Create audio context
                _context = ALC.CreateContext(_device, (int[])null);
                if (_context == ALContext.Null)
                {
                    ALC.CloseDevice(_device);
                    throw new InvalidOperationException("Failed to create audio context");
                }

                ALC.MakeContextCurrent(_context);

                // Generate source and buffers
                _source = AL.GenSource();
                _buffers = AL.GenBuffers(NUM_BUFFERS);

                // Set source properties
                AL.Source(_source, ALSourcef.Gain, 1.0f);

                // Start streaming thread
                _shouldStop = false;
                _streamingThread = new Thread(StreamingThreadProc);
                _streamingThread.IsBackground = true;
                _streamingThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize OpenAL audio: {ex.Message}");
                Dispose();
                throw;
            }
        }

        private void StreamingThreadProc()
        {
            try
            {
                // Pre-fill all buffers
                float[] floatBuffer = new float[BUFFER_SAMPLES];
                short[] shortBuffer = new short[BUFFER_SAMPLES];

                for (int i = 0; i < NUM_BUFFERS; i++)
                {
                    _sidetoneProvider.Read(floatBuffer, 0, BUFFER_SAMPLES);
                    ConvertToShort(floatBuffer, shortBuffer);
                    AL.BufferData(_buffers[i], ALFormat.Mono16, shortBuffer, SAMPLE_RATE);
                }

                AL.SourceQueueBuffers(_source, NUM_BUFFERS, _buffers);
                AL.SourcePlay(_source);

                // Streaming loop
                while (!_shouldStop)
                {
                    // Check for processed buffers
                    AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processedBuffers);

                    while (processedBuffers > 0)
                    {
                        // Unqueue processed buffer
                        int buffer = AL.SourceUnqueueBuffer(_source);

                        // Fill with new data
                        _sidetoneProvider.Read(floatBuffer, 0, BUFFER_SAMPLES);
                        ConvertToShort(floatBuffer, shortBuffer);
                        AL.BufferData(buffer, ALFormat.Mono16, shortBuffer, SAMPLE_RATE);

                        // Re-queue buffer
                        AL.SourceQueueBuffers(_source, 1, new[] { buffer });

                        processedBuffers--;
                    }

                    // Make sure source is still playing
                    AL.GetSource(_source, ALGetSourcei.SourceState, out int state);
                    if ((ALSourceState)state != ALSourceState.Playing)
                    {
                        // Check if we have buffers queued before restarting
                        AL.GetSource(_source, ALGetSourcei.BuffersQueued, out int buffersQueued);
                        if (buffersQueued > 0)
                        {
                            AL.SourcePlay(_source);
                        }
                    }

                    // Sleep briefly to avoid busy-waiting
                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenAL streaming thread error: {ex.Message}");
            }
        }

        private void ConvertToShort(float[] floatSamples, short[] shortSamples)
        {
            for (int i = 0; i < floatSamples.Length; i++)
            {
                float sample = Math.Clamp(floatSamples[i], -1.0f, 1.0f);
                shortSamples[i] = (short)(sample * short.MaxValue);
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

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Stop streaming thread
            _shouldStop = true;
            if (_streamingThread != null)
            {
                _streamingThread.Join(1000);
            }

            // Clean up OpenAL resources
            if (_source != 0)
            {
                AL.SourceStop(_source);
                AL.DeleteSource(_source);
                _source = 0;
            }

            if (_buffers != null)
            {
                AL.DeleteBuffers(_buffers);
                _buffers = null;
            }

            if (_context != ALContext.Null)
            {
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(_context);
                _context = ALContext.Null;
            }

            if (_device != ALDevice.Null)
            {
                ALC.CloseDevice(_device);
                _device = ALDevice.Null;
            }
        }
    }
}
