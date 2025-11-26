using System;
using NAudio.Wave;

namespace NetKeyer.Audio
{
    /// <summary>
    /// Custom sample provider for CW sidetone generation with optimized patch-based playback.
    /// Generates sine wave tones with raised cosine ramps for click-free keying.
    /// </summary>
    public class SidetoneProvider : ISampleProvider
    {
        private const int SAMPLE_RATE = 48000;

        private float[] _rampUpPatch;
        private float[] _singleCyclePatch;
        private float[] _rampDownPatch;

        private int _frequency = 600;
        private float _volume = 0.5f;
        private int _wpm = 20;

        private PlaybackState _state = PlaybackState.Silent;
        private int _patchPosition = 0;
        private int _remainingCycles = 0;
        private bool _indefiniteTone = false; // true for straight-key mode

        private readonly object _lockObject = new object();

        public bool IsSilent => _state == PlaybackState.Silent;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SAMPLE_RATE, 1);

        private enum PlaybackState
        {
            Silent,
            RampUp,
            Sustain,
            RampDown
        }

        public SidetoneProvider()
        {
            RegeneratePatches();
        }

        /// <summary>
        /// Sets the sidetone frequency in Hz. Will be rounded to the nearest frequency
        /// that produces a whole number of samples per cycle at 48kHz.
        /// </summary>
        public void SetFrequency(int frequencyHz)
        {
            lock (_lockObject)
            {
                if (frequencyHz < 100 || frequencyHz > 2000)
                    return;

                _frequency = frequencyHz;
                RegeneratePatches();
            }
        }

        /// <summary>
        /// Sets the sidetone volume (0.0 to 1.0).
        /// </summary>
        public void SetVolume(float volume)
        {
            lock (_lockObject)
            {
                _volume = Math.Clamp(volume, 0.0f, 1.0f);
                RegeneratePatches();
            }
        }

        /// <summary>
        /// Sets the CW speed in WPM, used to calculate ramp duration.
        /// </summary>
        public void SetWpm(int wpm)
        {
            lock (_lockObject)
            {
                _wpm = Math.Clamp(wpm, 5, 60);
                RegeneratePatches();
            }
        }

        /// <summary>
        /// Start playing a tone for a specific duration in milliseconds.
        /// Used for iambic mode where duration is known in advance.
        /// </summary>
        public void StartTone(int durationMs)
        {
            lock (_lockObject)
            {
                if (_state != PlaybackState.Silent)
                    Stop(); // Stop any ongoing tone

                int totalSamples = (int)(durationMs * SAMPLE_RATE / 1000.0);
                int rampSamples = _rampUpPatch.Length + _rampDownPatch.Length;
                int sustainSamples = totalSamples - rampSamples;

                if (sustainSamples < 0)
                {
                    // Tone too short for full ramps - just play what we can
                    sustainSamples = 0;
                }

                _remainingCycles = sustainSamples / _singleCyclePatch.Length;
                _indefiniteTone = false;
                _patchPosition = 0;
                _state = PlaybackState.RampUp;
            }
        }

        /// <summary>
        /// Start playing a tone indefinitely (for straight-key mode).
        /// Call Stop() to end the tone.
        /// </summary>
        public void StartIndefiniteTone()
        {
            lock (_lockObject)
            {
                // If already playing or ramping up, ignore
                if (_state == PlaybackState.RampUp || _state == PlaybackState.Sustain)
                {
                    Console.WriteLine($"[SidetoneProvider] StartIndefiniteTone called but already playing (state={_state}), ignoring");
                    return;
                }

                // If in ramp-down or silent, start a new tone immediately
                Console.WriteLine($"[SidetoneProvider] Starting indefinite tone (was {_state}), freq={_frequency}Hz vol={_volume} wpm={_wpm}");
                _indefiniteTone = true;
                _patchPosition = 0;
                _state = PlaybackState.RampUp;
            }
        }

        /// <summary>
        /// Stop the currently playing tone (triggers ramp-down if playing).
        /// </summary>
        public void Stop()
        {
            lock (_lockObject)
            {
                if (_state == PlaybackState.Silent)
                {
                    Console.WriteLine($"[SidetoneProvider] Stop called but already silent");
                    return;
                }

                Console.WriteLine($"[SidetoneProvider] Stopping tone, transitioning to ramp-down");
                // Immediate transition to ramp-down
                _state = PlaybackState.RampDown;
                _patchPosition = 0;
            }
        }

        private static int _readCallCount = 0;

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_lockObject)
            {
                _readCallCount++;
                if (_readCallCount % 1000 == 0)
                {
                    Console.WriteLine($"[SidetoneProvider] Read called {_readCallCount} times, state={_state}");
                }

                int samplesWritten = 0;

                while (samplesWritten < count)
                {
                    switch (_state)
                    {
                        case PlaybackState.Silent:
                            // Fill remainder with silence
                            for (int i = samplesWritten; i < count; i++)
                            {
                                buffer[offset + i] = 0.0f;
                            }
                            return count;

                        case PlaybackState.RampUp:
                            samplesWritten += CopyFromPatch(_rampUpPatch, buffer, offset + samplesWritten, count - samplesWritten);
                            if (_patchPosition >= _rampUpPatch.Length)
                            {
                                _state = PlaybackState.Sustain;
                                _patchPosition = 0;
                            }
                            break;

                        case PlaybackState.Sustain:
                            if (_indefiniteTone)
                            {
                                // Keep playing single cycles indefinitely
                                samplesWritten += CopyFromPatch(_singleCyclePatch, buffer, offset + samplesWritten, count - samplesWritten);
                                if (_patchPosition >= _singleCyclePatch.Length)
                                {
                                    _patchPosition = 0; // Loop the single cycle
                                }
                            }
                            else if (_remainingCycles > 0)
                            {
                                samplesWritten += CopyFromPatch(_singleCyclePatch, buffer, offset + samplesWritten, count - samplesWritten);
                                if (_patchPosition >= _singleCyclePatch.Length)
                                {
                                    _patchPosition = 0;
                                    _remainingCycles--;
                                    if (_remainingCycles == 0)
                                    {
                                        _state = PlaybackState.RampDown;
                                    }
                                }
                            }
                            else
                            {
                                // No more cycles to play, go to ramp-down
                                _state = PlaybackState.RampDown;
                                _patchPosition = 0;
                            }
                            break;

                        case PlaybackState.RampDown:
                            samplesWritten += CopyFromPatch(_rampDownPatch, buffer, offset + samplesWritten, count - samplesWritten);
                            if (_patchPosition >= _rampDownPatch.Length)
                            {
                                _state = PlaybackState.Silent;
                                _patchPosition = 0;
                                // Fill any remaining buffer with silence
                                for (int i = samplesWritten; i < count; i++)
                                {
                                    buffer[offset + i] = 0.0f;
                                }
                                return count;
                            }
                            break;
                    }
                }

                return count;
            }
        }

        private int CopyFromPatch(float[] patch, float[] destBuffer, int destOffset, int maxSamples)
        {
            int samplesToCopy = Math.Min(maxSamples, patch.Length - _patchPosition);

            // Use manual copy to avoid Array.Copy type issues
            for (int i = 0; i < samplesToCopy; i++)
            {
                destBuffer[destOffset + i] = patch[_patchPosition + i];
            }

            _patchPosition += samplesToCopy;
            return samplesToCopy;
        }

        private void RegeneratePatches()
        {
            // Round frequency to one that gives whole number of samples per cycle
            int samplesPerCycle = (int)Math.Round((double)SAMPLE_RATE / _frequency);
            double actualFrequency = (double)SAMPLE_RATE / samplesPerCycle;

            // Calculate dit length in milliseconds: dit_length = 1200 / WPM
            double ditLengthMs = 1200.0 / _wpm;

            // Determine ramp duration
            double rampDurationMs;
            if (ditLengthMs >= 50.0) // <= 24 WPM
            {
                rampDurationMs = 5.0;
            }
            else
            {
                rampDurationMs = ditLengthMs * 0.1;
            }

            // Convert ramp duration to samples
            int rampSamples = (int)Math.Round(rampDurationMs * SAMPLE_RATE / 1000.0);

            // Round ramp to whole number of cycles
            int rampCycles = Math.Max(1, (int)Math.Round((double)rampSamples / samplesPerCycle));
            int actualRampSamples = rampCycles * samplesPerCycle;

            Console.WriteLine($"[SidetoneProvider] RegeneratePatches: freq={_frequency}Hz->actual={actualFrequency:F1}Hz, vol={_volume}, wpm={_wpm}, samplesPerCycle={samplesPerCycle}, rampSamples={actualRampSamples}");

            // Generate single cycle patch
            _singleCyclePatch = new float[samplesPerCycle];
            for (int i = 0; i < samplesPerCycle; i++)
            {
                double phase = 2.0 * Math.PI * i / samplesPerCycle;
                _singleCyclePatch[i] = (float)(Math.Sin(phase) * _volume);
            }

            // Generate ramp-up patch (raised cosine envelope)
            _rampUpPatch = new float[actualRampSamples];
            for (int i = 0; i < actualRampSamples; i++)
            {
                double phase = 2.0 * Math.PI * i / samplesPerCycle;
                double sineValue = Math.Sin(phase);

                // Raised cosine envelope: 0.5 * (1 - cos(pi * t / rampDuration))
                double rampProgress = (double)i / actualRampSamples;
                double envelope = 0.5 * (1.0 - Math.Cos(Math.PI * rampProgress));

                _rampUpPatch[i] = (float)(sineValue * _volume * envelope);
            }

            // Generate ramp-down patch (same phase progression, reversed envelope)
            _rampDownPatch = new float[actualRampSamples];
            for (int i = 0; i < actualRampSamples; i++)
            {
                double phase = 2.0 * Math.PI * i / samplesPerCycle;
                double sineValue = Math.Sin(phase);

                // Reversed raised cosine envelope: 0.5 * (1 + cos(pi * t / rampDuration))
                double rampProgress = (double)i / actualRampSamples;
                double envelope = 0.5 * (1.0 + Math.Cos(Math.PI * rampProgress));

                _rampDownPatch[i] = (float)(sineValue * _volume * envelope);
            }
        }
    }
}
