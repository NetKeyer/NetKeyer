using System;
using System.Diagnostics;

namespace NetKeyer.Helpers;

/// <summary>
/// Correlates sidetone timing checkpoints across input, keying, and audio pipeline stages.
/// Logs each probe stage and computed deltas to debug.log for idle/post-idle latency analysis.
/// </summary>
public static class SidetoneLatencyProbe
{
    private sealed class Probe
    {
        public long SequenceId;
        public long InputTicks;
        public bool InputLikelyIdle;
        public string InputSource = string.Empty;
        public string KeyerStateAtClosure = string.Empty;
        public long? BeforeControllerDispatchTicks;
        public string BeforeControllerDispatchSource = string.Empty;
        public long? ControllerEntryTicks;
        public string ControllerEntrySource = string.Empty;
        public long? StartCallTicks;
        public string StartSource = string.Empty;
        public long? ToneStartCallbackTicks;
        public string ToneStartSource = string.Empty;
        public long? FirstNonSilentTicks;
        public string FirstNonSilentSource = string.Empty;
        public long? FirstAudibleTicks;
        public string FirstAudibleSource = string.Empty;
    }

    private static readonly object _lock = new();
    private static Probe _active;
    private static long _sequenceCounter;
    private static long _lastAudioIdleTicks;
    private static bool _enabled;

    public static void Configure(bool enabled)
    {
        lock (_lock)
        {
            _enabled = enabled;

            if (!_enabled)
            {
                _active = null;
            }
        }
    }

    private static double ToMs(long deltaTicks)
    {
        return deltaTicks * 1000.0 / Stopwatch.Frequency;
    }

    private static long NowTicks()
    {
        return Stopwatch.GetTimestamp();
    }

    public static void MarkAudioIdle(string source)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_lock)
        {
            _lastAudioIdleTicks = NowTicks();
            DebugLogger.LogAlways("latency", $"[SidetoneProbe] audio_idle source={source}");
        }
    }

    public static void MarkInputClosure(string source, bool likelyIdle, string keyerStateAtClosure)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_lock)
        {
            _sequenceCounter++;
            _active = new Probe
            {
                SequenceId = _sequenceCounter,
                InputTicks = NowTicks(),
                InputLikelyIdle = likelyIdle,
                InputSource = source ?? string.Empty,
                KeyerStateAtClosure = keyerStateAtClosure ?? string.Empty
            };

            string idleText = "n/a";
            if (_lastAudioIdleTicks > 0)
            {
                idleText = ToMs(_active.InputTicks - _lastAudioIdleTicks).ToString("F3");
            }

            DebugLogger.LogAlways(
                "latency",
                $"[SidetoneProbe] seq={_active.SequenceId} input_closure source={_active.InputSource} likelyIdle={_active.InputLikelyIdle} keyerStateAtClosure={_active.KeyerStateAtClosure} sinceAudioIdleMs={idleText}");
        }
    }

    public static void MarkBeforeControllerDispatch(string source)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_lock)
        {
            if (_active == null || _active.BeforeControllerDispatchTicks.HasValue)
            {
                return;
            }

            _active.BeforeControllerDispatchTicks = NowTicks();
            _active.BeforeControllerDispatchSource = source ?? string.Empty;

            var inputToDispatchMs = ToMs(_active.BeforeControllerDispatchTicks.Value - _active.InputTicks);
            DebugLogger.LogAlways(
                "latency",
                $"[SidetoneProbe] seq={_active.SequenceId} before_controller_dispatch source={_active.BeforeControllerDispatchSource} inputToDispatchMs={inputToDispatchMs:F3}");
        }
    }

    public static void MarkControllerEntry(string source)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_lock)
        {
            if (_active == null || _active.ControllerEntryTicks.HasValue)
            {
                return;
            }

            _active.ControllerEntryTicks = NowTicks();
            _active.ControllerEntrySource = source ?? string.Empty;

            var inputToControllerEntryMs = ToMs(_active.ControllerEntryTicks.Value - _active.InputTicks);
            string dispatchToEntryMs = "n/a";
            if (_active.BeforeControllerDispatchTicks.HasValue)
            {
                dispatchToEntryMs = ToMs(_active.ControllerEntryTicks.Value - _active.BeforeControllerDispatchTicks.Value).ToString("F3");
            }

            DebugLogger.LogAlways(
                "latency",
                $"[SidetoneProbe] seq={_active.SequenceId} controller_entry source={_active.ControllerEntrySource} inputToControllerEntryMs={inputToControllerEntryMs:F3} dispatchToControllerEntryMs={dispatchToEntryMs}");
        }
    }

    public static void MarkSidetoneStartCall(string source)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_lock)
        {
            if (_active == null)
            {
                _sequenceCounter++;
                _active = new Probe
                {
                    SequenceId = _sequenceCounter,
                    InputTicks = NowTicks(),
                    InputLikelyIdle = false,
                    InputSource = "synthetic"
                };
            }

            if (_active.StartCallTicks.HasValue)
            {
                return;
            }

            _active.StartCallTicks = NowTicks();
            _active.StartSource = source ?? string.Empty;

            var inputToStartMs = ToMs(_active.StartCallTicks.Value - _active.InputTicks);
            DebugLogger.LogAlways(
                "latency",
                $"[SidetoneProbe] seq={_active.SequenceId} start_call source={_active.StartSource} inputToStartCallMs={inputToStartMs:F3}");
        }
    }

    public static void MarkToneStartCallback(string source)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_lock)
        {
            if (_active == null)
            {
                return;
            }

            if (_active.ToneStartCallbackTicks.HasValue)
            {
                return;
            }

            _active.ToneStartCallbackTicks = NowTicks();
            _active.ToneStartSource = source ?? string.Empty;

            var inputToToneCbMs = ToMs(_active.ToneStartCallbackTicks.Value - _active.InputTicks);
            string startToToneCb = "n/a";
            if (_active.StartCallTicks.HasValue)
            {
                startToToneCb = ToMs(_active.ToneStartCallbackTicks.Value - _active.StartCallTicks.Value).ToString("F3");
            }

            DebugLogger.LogAlways(
                "latency",
                $"[SidetoneProbe] seq={_active.SequenceId} tone_start_callback source={_active.ToneStartSource} inputToToneStartCbMs={inputToToneCbMs:F3} startCallToToneStartCbMs={startToToneCb}");
        }
    }

    public static bool ShouldCheckForFirstNonSilentSample()
    {
        if (!_enabled)
        {
            return false;
        }

        lock (_lock)
        {
            return _active != null && !_active.FirstNonSilentTicks.HasValue;
        }
    }

    public static bool ShouldCheckForFirstAudibleSample()
    {
        if (!_enabled)
        {
            return false;
        }

        lock (_lock)
        {
            return _active != null && !_active.FirstAudibleTicks.HasValue;
        }
    }

    public static void MarkFirstNonSilentSample(string source)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_lock)
        {
            if (_active == null || _active.FirstNonSilentTicks.HasValue)
            {
                return;
            }

            _active.FirstNonSilentTicks = NowTicks();
            _active.FirstNonSilentSource = source ?? string.Empty;

            var inputToFirstAudioMs = ToMs(_active.FirstNonSilentTicks.Value - _active.InputTicks);
            string startToFirstAudioMs = "n/a";
            if (_active.StartCallTicks.HasValue)
            {
                startToFirstAudioMs = ToMs(_active.FirstNonSilentTicks.Value - _active.StartCallTicks.Value).ToString("F3");
            }

            string toneCbToFirstAudioMs = "n/a";
            if (_active.ToneStartCallbackTicks.HasValue)
            {
                toneCbToFirstAudioMs = ToMs(_active.FirstNonSilentTicks.Value - _active.ToneStartCallbackTicks.Value).ToString("F3");
            }

            DebugLogger.LogAlways(
                "latency",
                $"[SidetoneProbe] seq={_active.SequenceId} first_non_silent source={_active.FirstNonSilentSource} inputToFirstAudioMs={inputToFirstAudioMs:F3} startCallToFirstAudioMs={startToFirstAudioMs} toneStartCbToFirstAudioMs={toneCbToFirstAudioMs} likelyIdle={_active.InputLikelyIdle}");
        }
    }

    public static void MarkFirstAudibleSample(string source)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_lock)
        {
            if (_active == null || _active.FirstAudibleTicks.HasValue)
            {
                return;
            }

            _active.FirstAudibleTicks = NowTicks();
            _active.FirstAudibleSource = source ?? string.Empty;

            var inputToFirstAudibleMs = ToMs(_active.FirstAudibleTicks.Value - _active.InputTicks);
            string startToFirstAudibleMs = "n/a";
            if (_active.StartCallTicks.HasValue)
            {
                startToFirstAudibleMs = ToMs(_active.FirstAudibleTicks.Value - _active.StartCallTicks.Value).ToString("F3");
            }

            string nonSilentToAudibleMs = "n/a";
            if (_active.FirstNonSilentTicks.HasValue)
            {
                nonSilentToAudibleMs = ToMs(_active.FirstAudibleTicks.Value - _active.FirstNonSilentTicks.Value).ToString("F3");
            }

            DebugLogger.LogAlways(
                "latency",
                $"[SidetoneProbe] seq={_active.SequenceId} first_audible source={_active.FirstAudibleSource} inputToFirstAudibleMs={inputToFirstAudibleMs:F3} startCallToFirstAudibleMs={startToFirstAudibleMs} firstNonSilentToFirstAudibleMs={nonSilentToAudibleMs}");
        }
    }
}