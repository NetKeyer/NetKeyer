# Sidetone latency: switches and test plan

Sidetone latency is the time from a paddle or key closing to the start of the tone at the
computer's audio output. Measure it with a scope: one trace on the paddle contact and one on
the audio output, measuring from the key edge to the first visible cycle of the tone.

Earlier measurements (`Measurements/timings.ods`, master, HaliKey): Windows 10 about 34–45 ms,
Linux about 64–73 ms.

Every change that might behave differently on different hardware can be switched with an
environment variable, so you can A/B test without rebuilding. To see what is in effect, run with
`NETKEYER_DEBUG=audio` and check `debug.log` (`%APPDATA%\NetKeyer\debug.log` or
`~/.config/NetKeyer/debug.log`). On startup it logs a `[LatencyOptions]` line, plus a line
showing the negotiated stream: period, buffer and queue sizes on Windows, or host API and actual
output latency on PortAudio.

## Changes that are always on

These fix bugs or remove work from the time-critical path. They have no tuning knobs, but some
can be reverted for comparison.

| Change | Platforms | Revert for A/B |
|---|---|---|
| Radio `CWKey` network sends moved to a dedicated thread. Before, they ran inside the sidetone lock before the audio device was started, and on the real-time audio thread for every iambic element after the first. | all | `NETKEYER_RADIO_SEND_SYNC=1` |
| Keying runs before the UI indicator update is posted. MIDI messages are parsed with no allocations and an O(1) note lookup. | all | – |
| `GCLatencyMode.SustainedLowLatency`, which avoids blocking garbage-collector pauses that stall the audio/MIDI threads. | all | `NETKEYER_GC_LOWLATENCY=0` |
| NAudio WASAPI generator: fixed a start/stop race. Before, a key press during the background idle-stop could leave the stream stopped, giving no sidetone until a 1 s timeout. Straight-key key-up no longer blocks the MIDI thread while WASAPI stops. | Windows, `NETKEYER_WASAPI=naudio` | – |

## Windows (WASAPI)

| Variable | Default | Meaning |
|---|---|---|
| `NETKEYER_WASAPI` | `lowlatency` | `lowlatency` = new custom WASAPI renderer. `naudio` = previous NAudio `WasapiOut` generator, which still honours the "aggressive low latency" setting. If the new renderer fails to start, the old one is used automatically. |
| `NETKEYER_WASAPI_NO_AC3` | off | `1` = don't use `IAudioClient3` small engine periods; use classic 10 ms periods instead. |
| `NETKEYER_WASAPI_PERIOD_FRAMES` | device minimum | Request a specific engine period in frames (`IAudioClient3` only). |
| `NETKEYER_WASAPI_TARGET_FRAMES` | 1 period if the period is ≥ 5 ms, else 2 periods | How much audio is kept queued. Lower = less latency. Raise it if you hear crackles. With `NETKEYER_DEBUG=audio`, underruns are logged as "Buffer ran empty". |
| `NETKEYER_WASAPI_MMCSS` | on | `0` = don't register the render thread with MMCSS "Pro Audio". |

**Why the new renderer:** NAudio's `WasapiOut` refills the *whole* WASAPI buffer on every
period, so a new tone queues behind roughly 10–20 ms of silence that has already been written.
"Aggressive" mode avoided that queue by stopping the stream when idle. But then every key-down
paid for creating a thread, starting the stream and waiting for the next 10 ms engine period.

The new renderer:
- runs continuously,
- asks for the smallest engine period the driver allows (often 128 frames, 2.7 ms, with
  Windows 10+ in-box drivers),
- keeps only one or two periods queued,
- renders in the engine's native mix format.

## Linux / macOS (PortAudio)

| Variable | Default | Meaning |
|---|---|---|
| `NETKEYER_PA_FRAMES` | `128` (was 256) | PortAudio frames per callback. `0` lets PortAudio choose. |
| `NETKEYER_PA_LATENCY_MS` | device "low" latency | PortAudio `suggestedLatency`, in ms. |
| `NETKEYER_PA_ALSA_PERIODS` | PortAudio default | Linux ALSA only: number of ALSA periods (try `2`). |
| `NETKEYER_PA_SAMPLE_RATE` | `device` on macOS, `48000` elsewhere | Stream sample rate, or `device` to use the device's default rate (avoids a resampler). |
| `NETKEYER_AUDIO_ENV` | on (Linux) | At startup, sets `PIPEWIRE_LATENCY` and `PULSE_LATENCY_MSEC` if they aren't already set. `0` disables this. |
| `NETKEYER_PIPEWIRE_LATENCY` | `128/48000` | Value used for `PIPEWIRE_LATENCY`. |
| `NETKEYER_PULSE_LATENCY_MSEC` | `5` | Value used for `PULSE_LATENCY_MSEC`. |

On most Linux desktops the "default" ALSA device goes through PipeWire or PulseAudio. Those
default to a large processing cycle (about 1024 frames, 21 ms), which is the most likely reason
for the ~65 ms Linux measurement. You can also set `PIPEWIRE_LATENCY` yourself, for example
`PIPEWIRE_LATENCY=64/48000`. The bundled PortAudio also includes JACK, so on a PipeWire system
the JACK devices in the audio device list are another option to try.

## Sidetone waveform (all platforms)

| Variable | Default | Meaning |
|---|---|---|
| `NETKEYER_SIDETONE_RAMP_MS` | `5` | Maximum sidetone rise/fall time, still capped at 10% of a dit. A shorter attack makes the tone audible sooner, at the cost of a slightly harder key click. This only affects the local sidetone, not the transmitted signal. |

## Suggested test order

Take about 10 measurements for each setting and record the mean and standard deviation, as in
`timings.ods`.

**Windows**
1. Default settings, compared with `NETKEYER_WASAPI=naudio`, which is equivalent to the previous
   build plus the race fix.
2. Default, compared with `NETKEYER_WASAPI_NO_AC3=1`. This shows whether the small engine
   period is being used on this hardware; check the `[WASAPI-LL] Initialised` log line.
3. Try lowering `NETKEYER_WASAPI_TARGET_FRAMES`, for example to the period size, until crackles
   appear, then back off.

**Linux**
1. Default settings, compared with `NETKEYER_AUDIO_ENV=0 NETKEYER_PA_FRAMES=256`, which is
   roughly the old behaviour.
2. Try `NETKEYER_PIPEWIRE_LATENCY` values `256/48000`, `128/48000` and `64/48000`.
3. Try `NETKEYER_PA_ALSA_PERIODS=2`, and a JACK output device if one is listed.

**macOS**
1. Default settings, compared with `NETKEYER_PA_SAMPLE_RATE=48000 NETKEYER_PA_FRAMES=256`.
2. Try `NETKEYER_PA_FRAMES` values of `64` and `32`.

**All platforms**
- `NETKEYER_RADIO_SEND_SYNC=1` while connected to a radio, to measure the cost of the old
  synchronous send.
- `NETKEYER_SIDETONE_RAMP_MS=2`.
