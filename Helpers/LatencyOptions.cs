using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace NetKeyer.Helpers;

/// <summary>
/// Environment-variable switches for sidetone-latency experiments.
///
/// Every latency-related change that could plausibly behave differently on
/// different hardware is controlled here so it can be A/B tested on a
/// key-to-audio measurement rig without rebuilding. See LATENCY.md for the
/// full list and a suggested test plan. Effective values are logged under the
/// "audio" debug category (NETKEYER_DEBUG=audio).
/// </summary>
public static class LatencyOptions
{
    // ---- Radio command path ----

    /// <summary>
    /// NETKEYER_RADIO_SEND_SYNC=1 restores the old behaviour of sending CW key
    /// commands to the radio synchronously on the caller's thread (MIDI thread
    /// or audio callback). Default: commands go through a dedicated sender thread.
    /// </summary>
    public static bool RadioSendSync => GetBool("NETKEYER_RADIO_SEND_SYNC", false);

    // ---- Windows / WASAPI ----

    /// <summary>
    /// NETKEYER_WASAPI=lowlatency (default) uses the custom WASAPI renderer;
    /// NETKEYER_WASAPI=naudio uses the previous NAudio WasapiOut-based generator
    /// (which honours the "aggressive low latency" setting).
    /// </summary>
    public static string WasapiMode => (GetString("NETKEYER_WASAPI") ?? "lowlatency").ToLowerInvariant();

    /// <summary>
    /// NETKEYER_WASAPI_NO_AC3=1 disables IAudioClient3 (small engine periods)
    /// and uses the classic IAudioClient::Initialize path instead.
    /// </summary>
    public static bool WasapiDisableAudioClient3 => GetBool("NETKEYER_WASAPI_NO_AC3", false);

    /// <summary>
    /// NETKEYER_WASAPI_PERIOD_FRAMES=n requests a specific shared-mode engine
    /// period (IAudioClient3 only). Default: the device's minimum period.
    /// </summary>
    public static int? WasapiPeriodFrames => GetInt("NETKEYER_WASAPI_PERIOD_FRAMES");

    /// <summary>
    /// NETKEYER_WASAPI_TARGET_FRAMES=n caps how many frames are kept queued in
    /// the WASAPI buffer. Lower = less latency, more risk of dropouts.
    /// Default: chosen from the engine period (see WasapiLowLatencySidetoneGenerator).
    /// </summary>
    public static int? WasapiTargetFrames => GetInt("NETKEYER_WASAPI_TARGET_FRAMES");

    /// <summary>
    /// NETKEYER_WASAPI_MMCSS=0 disables registering the render thread with MMCSS ("Pro Audio").
    /// </summary>
    public static bool WasapiUseMmcss => GetBool("NETKEYER_WASAPI_MMCSS", true);

    // ---- PortAudio (Linux / macOS) ----

    /// <summary>
    /// NETKEYER_PA_FRAMES=n sets PortAudio framesPerBuffer. 0 = let PortAudio choose
    /// (paFramesPerBufferUnspecified). Default: 128 (was 256).
    /// </summary>
    public static uint PortAudioFramesPerBuffer => (uint)Math.Max(0, GetInt("NETKEYER_PA_FRAMES") ?? 128);

    /// <summary>
    /// NETKEYER_PA_LATENCY_MS=x sets PortAudio's suggestedLatency.
    /// Default: the device's defaultLowOutputLatency.
    /// </summary>
    public static double? PortAudioSuggestedLatencyMs => GetDouble("NETKEYER_PA_LATENCY_MS");

    /// <summary>
    /// NETKEYER_PA_ALSA_PERIODS=n calls PaAlsa_SetNumPeriods(n) before opening
    /// the stream (Linux ALSA host API only). Default: PortAudio's own default.
    /// </summary>
    public static int? PortAudioAlsaPeriods => GetInt("NETKEYER_PA_ALSA_PERIODS");

    /// <summary>
    /// NETKEYER_PA_SAMPLE_RATE=n forces the sidetone sample rate, or "device" to
    /// use the device's default rate. Default: "device" on macOS (avoids a
    /// CoreAudio sample-rate converter), 48000 elsewhere.
    /// </summary>
    public static string PortAudioSampleRate =>
        GetString("NETKEYER_PA_SAMPLE_RATE")
        ?? (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "device" : "48000");

    /// <summary>
    /// NETKEYER_AUDIO_ENV=0 disables setting PIPEWIRE_LATENCY / PULSE_LATENCY_MSEC
    /// at startup on Linux. Values the user has already set are never overridden.
    /// </summary>
    public static bool SetSoundServerLatencyEnv => GetBool("NETKEYER_AUDIO_ENV", true);

    /// <summary>
    /// NETKEYER_PIPEWIRE_LATENCY overrides the PIPEWIRE_LATENCY value we set (default "128/48000").
    /// </summary>
    public static string PipeWireLatency => GetString("NETKEYER_PIPEWIRE_LATENCY") ?? "128/48000";

    /// <summary>
    /// NETKEYER_PULSE_LATENCY_MSEC overrides the PULSE_LATENCY_MSEC value we set (default "5").
    /// </summary>
    public static string PulseLatencyMsec => GetString("NETKEYER_PULSE_LATENCY_MSEC") ?? "5";

    // ---- Sidetone waveform ----

    /// <summary>
    /// NETKEYER_SIDETONE_RAMP_MS=x sets the maximum sidetone rise/fall time
    /// (default 5 ms; still capped at 10% of a dit at high speeds). This only
    /// shapes the local sidetone, not the transmitted RF envelope.
    /// </summary>
    public static double SidetoneMaxRampMs => Math.Clamp(GetDouble("NETKEYER_SIDETONE_RAMP_MS") ?? 5.0, 0.5, 20.0);

    // ---- Runtime ----

    /// <summary>
    /// NETKEYER_GC_LOWLATENCY=0 disables GCLatencyMode.SustainedLowLatency.
    /// </summary>
    public static bool GcSustainedLowLatency => GetBool("NETKEYER_GC_LOWLATENCY", true);

    /// <summary>
    /// Logs the effective value of every switch (under the "audio" category).
    /// </summary>
    public static void LogEffectiveOptions()
    {
        if (!DebugLogger.IsEnabled("audio")) return;
        DebugLogger.Log("audio",
            "[LatencyOptions] " +
            $"RadioSendSync={RadioSendSync} " +
            $"WasapiMode={WasapiMode} WasapiNoAC3={WasapiDisableAudioClient3} " +
            $"WasapiPeriodFrames={WasapiPeriodFrames?.ToString() ?? "auto"} " +
            $"WasapiTargetFrames={WasapiTargetFrames?.ToString() ?? "auto"} WasapiMmcss={WasapiUseMmcss} " +
            $"PaFrames={PortAudioFramesPerBuffer} PaLatencyMs={PortAudioSuggestedLatencyMs?.ToString(CultureInfo.InvariantCulture) ?? "device-low"} " +
            $"PaAlsaPeriods={PortAudioAlsaPeriods?.ToString() ?? "default"} PaSampleRate={PortAudioSampleRate} " +
            $"SoundServerEnv={SetSoundServerLatencyEnv} " +
            $"PIPEWIRE_LATENCY={Environment.GetEnvironmentVariable("PIPEWIRE_LATENCY") ?? "(unset)"} " +
            $"PULSE_LATENCY_MSEC={Environment.GetEnvironmentVariable("PULSE_LATENCY_MSEC") ?? "(unset)"} " +
            $"SidetoneMaxRampMs={SidetoneMaxRampMs.ToString(CultureInfo.InvariantCulture)} " +
            $"GcSustainedLowLatency={GcSustainedLowLatency}");
    }

    /// <summary>
    /// On Linux, ask PipeWire / PulseAudio for a small buffer for our streams.
    /// Must run before any audio library connects to the sound server.
    /// Uses libc setenv() because Environment.SetEnvironmentVariable does not
    /// change the native environment seen by libpipewire / libpulse on Unix.
    /// </summary>
    public static void ApplySoundServerEnvironment()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || !SetSoundServerLatencyEnv)
            return;

        SetNativeEnvIfUnset("PIPEWIRE_LATENCY", PipeWireLatency);
        SetNativeEnvIfUnset("PULSE_LATENCY_MSEC", PulseLatencyMsec);
    }

    [DllImport("libc", EntryPoint = "setenv")]
    private static extern int native_setenv(string name, string value, int overwrite);

    private static void SetNativeEnvIfUnset(string name, string value)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            return;
        try
        {
            native_setenv(name, value, 0);
            // Keep the managed view in sync so logging reports the real value.
            Environment.SetEnvironmentVariable(name, value);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not set {name}: {ex.Message}");
        }
    }

    // ---- parsing helpers ----

    private static string GetString(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private static bool GetBool(string name, bool defaultValue)
    {
        var v = GetString(name);
        if (v == null) return defaultValue;
        v = v.ToLowerInvariant();
        if (v is "1" or "true" or "yes" or "on") return true;
        if (v is "0" or "false" or "no" or "off") return false;
        return defaultValue;
    }

    private static int? GetInt(string name)
    {
        var v = GetString(name);
        return v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : null;
    }

    private static double? GetDouble(string name)
    {
        var v = GetString(name);
        return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }
}
