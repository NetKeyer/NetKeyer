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
        private bool _enableDebugLogging = false;

        private PlaybackState _state = PlaybackState.Silent;
        private int _patchPosition = 0;
        private int _remainingCycles = 0;
        private bool _indefiniteTone = false; // true for straight-key mode
        private int _remainingSilenceSamples = 0;

        // Queued next action after current state completes
        private int? _queuedToneDurationMs = null;
        private int? _queuedSilenceDurationMs = null;

        private readonly object _lockObject = new object();

        public bool IsSilent => _state == PlaybackState.Silent || _state == PlaybackState.TimedSilence;

        // Event fired when a timed silence completes and no next tone was queued
        public event Action OnSilenceComplete;

        // Event fired when any tone starts (including queued tones)
        public event Action OnToneStart;

        // Event fired when any timed tone completes (whether silence follows or not)
        public event Action OnToneComplete;

        // Event fired when entering non-timed Silent state (fully idle)
        public event Action OnBecomeIdle;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SAMPLE_RATE, 1);

        private enum PlaybackState
        {
            Silent,
            RampUp,
            Sustain,
            RampDown,
            TimedSilence
        }

        public SidetoneProvider()
        {
            RegeneratePatches();
        }

        /// <summary>
        /// Gets or sets whether debug logging is enabled.
        /// </summary>
        public bool EnableDebugLogging
        {
            get => _enableDebugLogging;
            set => _enableDebugLogging = value;
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
        /// If called during timed silence, the tone will be queued to start after the silence ends.
        /// </summary>
        public void StartTone(int durationMs)
        {
            lock (_lockObject)
            {
                // If we're in timed silence, queue the tone instead of starting immediately
                if (_state == PlaybackState.TimedSilence)
                {
                    if (_enableDebugLogging)
                        Console.WriteLine($"[SidetoneProvider] StartTone called during silence, queuing tone: {durationMs}ms");
                    _queuedToneDurationMs = durationMs;
                    return;
                }

                // If playing another tone, stop it first
                if (_state != PlaybackState.Silent)
                    Stop();

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

                if (_enableDebugLogging)
                    Console.WriteLine($"[SidetoneProvider] Starting timed tone: {durationMs}ms, totalSamples={totalSamples}, rampSamples={rampSamples}, sustainSamples={sustainSamples}, remainingCycles={_remainingCycles}");

                // Fire event synchronously from audio thread for deterministic timing.
                // IambicKeyer handlers are designed to be fast (just state updates).
                if (OnToneStart != null)
                {
                    try
                    {
                        OnToneStart.Invoke();
                    }
                    catch (Exception ex)
                    {
                        if (_enableDebugLogging)
                            Console.WriteLine($"[SidetoneProvider] Error in OnToneStart: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// Start a timed silence period followed by a tone.
        /// Used for iambic mode to chain silence -> tone without timers.
        /// </summary>
        public void StartSilenceThenTone(int silenceMs, int toneMs)
        {
            lock (_lockObject)
            {
                // Queue the tone to play after silence
                _queuedToneDurationMs = toneMs;
                _queuedSilenceDurationMs = null;

                // Start the silence
                _remainingSilenceSamples = (int)(silenceMs * SAMPLE_RATE / 1000.0);
                _state = PlaybackState.TimedSilence;

                if (_enableDebugLogging)
                    Console.WriteLine($"[SidetoneProvider] Starting silence ({silenceMs}ms) then tone ({toneMs}ms)");
            }
        }

        /// <summary>
        /// Queue a silence period to start after the current tone completes.
        /// Can optionally chain another tone after the silence.
        /// If already silent, starts the silence immediately.
        /// </summary>
        public void QueueSilence(int silenceMs, int? followingToneMs = null)
        {
            lock (_lockObject)
            {
                _queuedSilenceDurationMs = silenceMs;
                _queuedToneDurationMs = followingToneMs;

                if (_enableDebugLogging)
                    Console.WriteLine($"[SidetoneProvider] Queued silence: {silenceMs}ms, following tone: {followingToneMs?.ToString() ?? "none"}");

                // If we're already silent (tone already completed), start the silence immediately
                if (_state == PlaybackState.Silent)
                {
                    if (_enableDebugLogging)
                        Console.WriteLine($"[SidetoneProvider] Already silent, starting queued silence immediately");
                    _remainingSilenceSamples = (int)(silenceMs * SAMPLE_RATE / 1000.0);
                    _state = PlaybackState.TimedSilence;
                    _queuedSilenceDurationMs = null; // Clear the queue since we're starting it now
                }
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
                    if (_enableDebugLogging)
                        Console.WriteLine($"[SidetoneProvider] StartIndefiniteTone called but already playing (state={_state}), ignoring");
                    return;
                }

                // If in ramp-down or silent, start a new tone immediately
                if (_enableDebugLogging)
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
                    if (_enableDebugLogging)
                        Console.WriteLine($"[SidetoneProvider] Stop called but already silent");
                    return;
                }

                if (_enableDebugLogging)
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
                if (_enableDebugLogging)
                {
                    _readCallCount++;
                    // Log EVERY call during RampUp/Sustain/RampDown, not just every 1000th
                    if (_state != PlaybackState.Silent && _state != PlaybackState.TimedSilence)
                    {
                        Console.WriteLine($"[SidetoneProvider] Read #{_readCallCount}: state={_state}, _patchPosition={_patchPosition}, _remainingCycles={_remainingCycles}, count={count}");
                    }
                    else if (_readCallCount % 1000 == 0)
                    {
                        Console.WriteLine($"[SidetoneProvider] Read called {_readCallCount} times, state={_state}");
                    }
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
                                if (_enableDebugLogging)
                                    Console.WriteLine($"[SidetoneProvider] RampUp complete, transitioning to Sustain");
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
                                        if (_enableDebugLogging)
                                            Console.WriteLine($"[SidetoneProvider] Sustain complete (all cycles done), transitioning to RampDown");
                                        _state = PlaybackState.RampDown;
                                    }
                                }
                            }
                            else
                            {
                                // No more cycles to play, go to ramp-down
                                if (_enableDebugLogging)
                                    Console.WriteLine($"[SidetoneProvider] Sustain: no cycles to play, transitioning to RampDown");
                                _state = PlaybackState.RampDown;
                                _patchPosition = 0;
                            }
                            break;

                        case PlaybackState.RampDown:
                            samplesWritten += CopyFromPatch(_rampDownPatch, buffer, offset + samplesWritten, count - samplesWritten);
                            if (_patchPosition >= _rampDownPatch.Length)
                            {
                                _patchPosition = 0;

                                // Fire tone complete event synchronously from audio thread for deterministic timing.
                                if (_enableDebugLogging)
                                    Console.WriteLine($"[SidetoneProvider] Firing OnToneComplete event");
                                if (OnToneComplete != null)
                                {
                                    try
                                    {
                                        OnToneComplete.Invoke();
                                    }
                                    catch (Exception ex)
                                    {
                                        if (_enableDebugLogging)
                                            Console.WriteLine($"[SidetoneProvider] Error in OnToneComplete: {ex}");
                                    }
                                }

                                // Check if we have a queued silence
                                if (_queuedSilenceDurationMs.HasValue)
                                {
                                    if (_enableDebugLogging)
                                        Console.WriteLine($"[SidetoneProvider] Ramp-down complete, starting queued silence: {_queuedSilenceDurationMs.Value}ms");
                                    _remainingSilenceSamples = (int)(_queuedSilenceDurationMs.Value * SAMPLE_RATE / 1000.0);
                                    _queuedSilenceDurationMs = null;
                                    _state = PlaybackState.TimedSilence;
                                }
                                else
                                {
                                    _state = PlaybackState.Silent;

                                }

                                // Fill any remaining buffer with silence
                                for (int i = samplesWritten; i < count; i++)
                                {
                                    buffer[offset + i] = 0.0f;
                                }
                                return count;
                            }
                            break;

                        case PlaybackState.TimedSilence:
                            // Output silence samples
                            int silenceSamplesToWrite = Math.Min(count - samplesWritten, _remainingSilenceSamples);
                            for (int i = 0; i < silenceSamplesToWrite; i++)
                            {
                                buffer[offset + samplesWritten + i] = 0.0f;
                            }
                            samplesWritten += silenceSamplesToWrite;
                            _remainingSilenceSamples -= silenceSamplesToWrite;

                            // Check if silence period is complete
                            if (_remainingSilenceSamples <= 0)
                            {
                                if (_enableDebugLogging)
                                    Console.WriteLine($"[SidetoneProvider] Silence complete");

                                // Check if we have a queued tone
                                if (_queuedToneDurationMs.HasValue)
                                {
                                    if (_enableDebugLogging)
                                        Console.WriteLine($"[SidetoneProvider] Starting queued tone: {_queuedToneDurationMs.Value}ms");
                                    int toneMs = _queuedToneDurationMs.Value;
                                    _queuedToneDurationMs = null;

                                    // Start the queued tone
                                    int totalSamples = (int)(toneMs * SAMPLE_RATE / 1000.0);
                                    int rampSamples = _rampUpPatch.Length + _rampDownPatch.Length;
                                    int sustainSamples = Math.Max(0, totalSamples - rampSamples);
                                    _remainingCycles = sustainSamples / _singleCyclePatch.Length;
                                    _indefiniteTone = false;
                                    _patchPosition = 0;
                                    _state = PlaybackState.RampUp;

                                    // Fire event synchronously from audio thread for deterministic timing.
                                    if (OnToneStart != null)
                                    {
                                        try
                                        {
                                            OnToneStart.Invoke();
                                        }
                                        catch (Exception ex)
                                        {
                                            if (_enableDebugLogging)
                                                Console.WriteLine($"[SidetoneProvider] Error in OnToneStart: {ex}");
                                        }
                                    }
                                }
                                else
                                {
                                    // No queued tone, transition to Silent state FIRST
                                    // This ensures handlers see the correct state when calling StartTone()
                                    if (_enableDebugLogging)
                                        Console.WriteLine($"[SidetoneProvider] No queued tone, transitioning to Silent");
                                    _state = PlaybackState.Silent;

                                    // Fire OnSilenceComplete (handler might start a new tone, changing state)
                                    if (OnSilenceComplete != null)
                                    {
                                        try
                                        {
                                            OnSilenceComplete.Invoke();
                                        }
                                        catch (Exception ex)
                                        {
                                            if (_enableDebugLogging)
                                                Console.WriteLine($"[SidetoneProvider] Error in OnSilenceComplete: {ex}");
                                        }
                                    }

                                    // Fire OnBecomeIdle only if still Silent (handler didn't start new tone)
                                    if (_state == PlaybackState.Silent && OnBecomeIdle != null)
                                    {
                                        try
                                        {
                                            OnBecomeIdle.Invoke();
                                        }
                                        catch (Exception ex)
                                        {
                                            if (_enableDebugLogging)
                                                Console.WriteLine($"[SidetoneProvider] Error in OnBecomeIdle: {ex}");
                                        }
                                    }
                                }
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

            if (_enableDebugLogging)
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
