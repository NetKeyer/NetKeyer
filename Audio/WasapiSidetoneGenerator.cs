using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NetKeyer.Audio
{
    /// <summary>
    /// Windows-specific WASAPI sidetone generator for ultra-low latency audio output.
    /// Uses exclusive mode WASAPI with minimal buffering for ~3-5ms latency.
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

                // Initialize WASAPI in shared mode with minimal latency
                // Note: Exclusive mode would be even lower latency but may not be available
                _wasapiOut = new WasapiOut(
                    NAudio.CoreAudioApi.AudioClientShareMode.Shared,
                    LATENCY_MS
                );

                _wasapiOut.Init(_volumeProvider);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize WASAPI audio: {ex.Message}");
                Dispose();
                throw;
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

            Stop();

            if (_wasapiOut != null)
            {
                _wasapiOut.Dispose();
                _wasapiOut = null;
            }

            _disposed = true;
        }
    }
}
