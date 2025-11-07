using System;
using OpenTK.Audio.OpenAL;

namespace NetKeyer.Audio
{
    public class SidetoneGenerator : IDisposable
    {
        private ALDevice _device;
        private ALContext _context;
        private int _source;
        private int _buffer;
        private bool _isPlaying;
        private int _frequency = 600; // Hz
        private float _volume = 0.5f; // 0.0 to 1.0
        private const int SAMPLE_RATE = 44100;
        private bool _disposed;

        public SidetoneGenerator()
        {
            try
            {
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

                // Generate source and buffer
                _source = AL.GenSource();
                _buffer = AL.GenBuffer();

                // Set source properties for low latency
                AL.Source(_source, ALSourcef.Gain, _volume);
                AL.Source(_source, ALSourceb.Looping, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize audio: {ex.Message}");
                Dispose();
                throw;
            }
        }

        public void SetFrequency(int frequencyHz)
        {
            if (frequencyHz < 100 || frequencyHz > 2000)
                return;

            _frequency = frequencyHz;

            // If currently playing, restart with new frequency
            if (_isPlaying)
            {
                Stop();
                Start();
            }
        }

        public void SetVolume(int volumePercent)
        {
            // Convert 0-100 to 0.0-1.0
            _volume = Math.Clamp(volumePercent / 100.0f, 0.0f, 1.0f);

            if (_source != 0)
            {
                AL.Source(_source, ALSourcef.Gain, _volume);
            }
        }

        public void Start()
        {
            if (_disposed || _isPlaying || _source == 0 || _buffer == 0)
                return;

            try
            {
                // Generate one second of sine wave tone
                int bufferSize = SAMPLE_RATE;
                short[] samples = new short[bufferSize];

                for (int i = 0; i < bufferSize; i++)
                {
                    double t = (double)i / SAMPLE_RATE;
                    double sample = Math.Sin(2.0 * Math.PI * _frequency * t);
                    samples[i] = (short)(sample * short.MaxValue * 0.5); // Scale to prevent clipping
                }

                // Upload buffer data
                AL.BufferData(_buffer, ALFormat.Mono16, samples, SAMPLE_RATE);

                // Attach buffer to source and play
                AL.Source(_source, ALSourcei.Buffer, _buffer);
                AL.SourcePlay(_source);

                _isPlaying = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start sidetone: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (_disposed || !_isPlaying || _source == 0)
                return;

            try
            {
                AL.SourceStop(_source);
                _isPlaying = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to stop sidetone: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();

            if (_source != 0)
            {
                AL.DeleteSource(_source);
                _source = 0;
            }

            if (_buffer != 0)
            {
                AL.DeleteBuffer(_buffer);
                _buffer = 0;
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

            _disposed = true;
        }
    }
}
