using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using NAudio.CoreAudioApi;
using NetKeyer.Helpers;

namespace NetKeyer.Audio
{
    /// <summary>
    /// Low-latency WASAPI sidetone renderer (Windows).
    ///
    /// Differences from the NAudio WasapiOut-based <see cref="WasapiSidetoneGenerator"/>:
    ///  - Uses IAudioClient3::InitializeSharedAudioStream at the device's minimum
    ///    shared-mode engine period (often 128 frames / 2.7 ms on Windows 10+),
    ///    falling back to the classic IAudioClient (typically 10 ms periods).
    ///  - Keeps only a small, capped amount of audio queued in the WASAPI buffer
    ///    instead of topping the whole buffer up on every event, so a new tone is
    ///    never stuck behind a buffer full of already-queued silence.
    ///  - Runs continuously: no per-element stream start/stop, no thread creation
    ///    on key-down, and no start/stop races.
    ///  - Registers the render thread with MMCSS ("Pro Audio").
    ///  - Renders in the engine's mix format (no format/rate conversion).
    ///
    /// All COM calls are made through raw vtable pointers on the render thread,
    /// so no RCW/apartment marshalling is involved.
    ///
    /// Tuning: NETKEYER_WASAPI_NO_AC3, NETKEYER_WASAPI_PERIOD_FRAMES,
    /// NETKEYER_WASAPI_TARGET_FRAMES, NETKEYER_WASAPI_MMCSS (see LATENCY.md).
    /// Select the previous implementation with NETKEYER_WASAPI=naudio.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed unsafe class WasapiLowLatencySidetoneGenerator : ISidetoneGenerator
    {
        // ---- COM / Core Audio constants ----
        private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid IID_IMMDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
        private static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioClient3 = new Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42");
        private static readonly Guid IID_IAudioRenderClient = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
        private static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new Guid("00000003-0000-0010-8000-00AA00389B71");
        private static readonly Guid KSDATAFORMAT_SUBTYPE_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");

        private const uint CLSCTX_ALL = 0x17;
        private const int AUDCLNT_SHAREMODE_SHARED = 0;
        private const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
        private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
        private const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
        private const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
        private const int eRender = 0;
        private const int eMultimedia = 1;

        // vtable slots (IUnknown occupies 0-2)
        private const int VT_Enum_GetDefaultAudioEndpoint = 4;
        private const int VT_Enum_GetDevice = 5;
        private const int VT_Device_Activate = 3;
        private const int VT_AC_Initialize = 3;
        private const int VT_AC_GetBufferSize = 4;
        private const int VT_AC_GetStreamLatency = 5;
        private const int VT_AC_GetCurrentPadding = 6;
        private const int VT_AC_GetMixFormat = 8;
        private const int VT_AC_GetDevicePeriod = 9;
        private const int VT_AC_Start = 10;
        private const int VT_AC_Stop = 11;
        private const int VT_AC_Reset = 12;
        private const int VT_AC_SetEventHandle = 13;
        private const int VT_AC_GetService = 14;
        private const int VT_AC3_GetSharedModeEnginePeriod = 18;
        private const int VT_AC3_GetCurrentSharedModeEnginePeriod = 19;
        private const int VT_AC3_InitializeSharedAudioStream = 20;
        private const int VT_RC_GetBuffer = 3;
        private const int VT_RC_ReleaseBuffer = 4;

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint clsContext, in Guid iid, out IntPtr ppv);

        [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

        [DllImport("avrt.dll")]
        private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);

        private enum SampleKind { Float32, Int16 }

        // ---- State ----
        private SidetoneProvider _sidetoneProvider;
        private readonly string _requestedDeviceName; // null/empty/"System Default" = follow default
        private bool _followDefault;
        private volatile bool _disposed;

        private Thread _renderThread;
        private volatile bool _stopRequested;
        private AutoResetEvent _audioEvent;
        private readonly ManualResetEventSlim _providerReady = new ManualResetEventSlim(false);
        private readonly object _restartLock = new object();

        // Filled in by the render thread during initialisation
        private int _sampleRate;
        private string _currentDeviceId;
        private Exception _initError;

        private MMDeviceEnumerator _notificationEnumerator;
        private DeviceNotificationClient _notificationClient;

        public event Action OnSilenceComplete;
        public event Action OnToneStart;
        public event Action OnToneComplete;
        public event Action OnBeforeSilenceEnd;
        public event Action OnBecomeIdle;

        public WasapiLowLatencySidetoneGenerator(string deviceName = null)
        {
            _requestedDeviceName = deviceName;
            _followDefault = string.IsNullOrEmpty(deviceName) || deviceName == "System Default";

            try
            {
                // Initialise the audio client first: the sidetone patches must be
                // generated at the engine's sample rate.
                StartRenderThread(requiredSampleRate: null);

                _sidetoneProvider = new SidetoneProvider(_sampleRate);
                _sidetoneProvider.OnSilenceComplete += () => OnSilenceComplete?.Invoke();
                _sidetoneProvider.OnToneStart += () => OnToneStart?.Invoke();
                _sidetoneProvider.OnToneComplete += () => OnToneComplete?.Invoke();
                _sidetoneProvider.OnBeforeSilenceEnd += () => OnBeforeSilenceEnd?.Invoke();
                _sidetoneProvider.OnBecomeIdle += () => OnBecomeIdle?.Invoke();
                _providerReady.Set();

                // Follow default-device changes / device removal
                try
                {
                    _notificationEnumerator = new MMDeviceEnumerator();
                    _notificationClient = new DeviceNotificationClient(OnDefaultDeviceChanged, OnDeviceStateChanged);
                    _notificationEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("audio", $"[WASAPI-LL] Could not register device notifications: {ex.Message}");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // ---------------------------------------------------------------------
        // Render thread lifecycle
        // ---------------------------------------------------------------------

        private void StartRenderThread(int? requiredSampleRate)
        {
            _stopRequested = false;
            _initError = null;
            _audioEvent = new AutoResetEvent(false);
            // Deliberately not disposed: if initialisation times out, the render
            // thread may still signal it later.
            var initDone = new ManualResetEventSlim(false);

            _renderThread = new Thread(() => RenderThread(requiredSampleRate, initDone))
            {
                IsBackground = true,
                Name = "NetKeyer WASAPI sidetone",
                Priority = ThreadPriority.Highest
            };
            _renderThread.SetApartmentState(ApartmentState.MTA);
            _renderThread.Start();

            if (!initDone.Wait(5000))
            {
                _stopRequested = true;
                _audioEvent.Set();
                throw new TimeoutException("WASAPI low-latency initialisation timed out");
            }

            if (_initError != null)
            {
                _renderThread.Join(1000);
                _renderThread = null;
                throw new InvalidOperationException($"WASAPI low-latency initialisation failed: {_initError.Message}", _initError);
            }
        }

        private void StopRenderThread()
        {
            var t = _renderThread;
            if (t == null) return;
            _stopRequested = true;
            try { _audioEvent?.Set(); } catch { }
            if (Thread.CurrentThread != t)
                t.Join(2000);
            _renderThread = null;
            _audioEvent?.Dispose();
            _audioEvent = null;
        }

        private void Restart(bool fallBackToDefault)
        {
            if (_disposed) return;
            lock (_restartLock)
            {
                if (_disposed) return;
                if (fallBackToDefault) _followDefault = true;

                StopRenderThread();
                try
                {
                    // Keep the provider's sample rate; convert in the engine if the new device differs.
                    StartRenderThread(requiredSampleRate: _sidetoneProvider?.SampleRate);
                    DebugLogger.Log("audio", "[WASAPI-LL] Restarted on new device");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("audio", $"[WASAPI-LL] Restart failed: {ex.Message}");
                }
            }
        }

        private void OnDefaultDeviceChanged()
        {
            if (_disposed || !_followDefault) return;
            System.Threading.Tasks.Task.Run(() => Restart(fallBackToDefault: false));
        }

        private void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (_disposed || newState == DeviceState.Active) return;
            if (deviceId == _currentDeviceId)
            {
                DebugLogger.Log("audio", $"[WASAPI-LL] Device '{_requestedDeviceName}' no longer active, falling back to default");
                System.Threading.Tasks.Task.Run(() => Restart(fallBackToDefault: true));
            }
        }

        // ---------------------------------------------------------------------
        // Render thread
        // ---------------------------------------------------------------------

        private void RenderThread(int? requiredSampleRate, ManualResetEventSlim initDone)
        {
            IntPtr enumerator = IntPtr.Zero, device = IntPtr.Zero, client = IntPtr.Zero, render = IntPtr.Zero;
            IntPtr format = IntPtr.Zero;
            IntPtr mmcss = IntPtr.Zero;
            bool started = false;
            bool deviceInvalidated = false;

            try
            {
                // ---- Initialisation ----
                ThrowIfFailed(CoCreateInstance(CLSID_MMDeviceEnumerator, IntPtr.Zero, CLSCTX_ALL, IID_IMMDeviceEnumerator, out enumerator),
                    "CoCreateInstance(MMDeviceEnumerator)");

                device = OpenDevice(enumerator);

                var init = InitializeClient(device, requiredSampleRate);
                client = init.Client;
                format = init.Format;

                uint bufferFrames;
                ThrowIfFailed(AC_GetBufferSize(client, &bufferFrames), "GetBufferSize");
                ThrowIfFailed(AC_SetEventHandle(client, _audioEvent.SafeWaitHandle.DangerousGetHandle()), "SetEventHandle");
                IntPtr renderPtr;
                Guid iidRender = IID_IAudioRenderClient;
                ThrowIfFailed(AC_GetService(client, &iidRender, &renderPtr), "GetService(IAudioRenderClient)");
                render = renderPtr;

                long streamLatency = 0;
                AC_GetStreamLatency(client, &streamLatency);

                // How much audio to keep queued. One period is the minimum that can
                // avoid an underrun at the next engine pass; very small periods get
                // a second period of slack against thread wake-up jitter.
                int period = init.PeriodFrames;
                int target = LatencyOptions.WasapiTargetFrames
                    ?? (period * 1000 / init.SampleRate >= 5 ? period : period * 2);
                target = Math.Clamp(target, Math.Min(period, (int)bufferFrames), (int)bufferFrames);

                _sampleRate = init.SampleRate;
                DebugLogger.Log("audio",
                    $"[WASAPI-LL] Initialised: device={_currentDeviceId}, mode={init.Mode}, rate={init.SampleRate}, channels={init.Channels}, " +
                    $"sample={init.Kind}, period={period} frames ({period * 1000.0 / init.SampleRate:F2} ms), " +
                    $"buffer={bufferFrames} frames ({bufferFrames * 1000.0 / init.SampleRate:F2} ms), " +
                    $"target queue={target} frames ({target * 1000.0 / init.SampleRate:F2} ms), " +
                    $"streamLatency={streamLatency / 10000.0:F2} ms");

                initDone.Set();

                // Wait until the provider exists (first start only)
                while (!_providerReady.Wait(100))
                {
                    if (_stopRequested || _disposed) return;
                }

                if (LatencyOptions.WasapiUseMmcss)
                {
                    uint taskIndex = 0;
                    mmcss = AvSetMmThreadCharacteristicsW("Pro Audio", ref taskIndex);
                    if (mmcss == IntPtr.Zero)
                        DebugLogger.Log("audio", $"[WASAPI-LL] MMCSS registration failed (error {Marshal.GetLastWin32Error()})");
                }

                var mono = new float[bufferFrames];

                // Pre-fill the target amount, then start
                Fill(render, init, mono, target);
                ThrowIfFailed(AC_Start(client), "Start");
                started = true;

                long underruns = 0;
                bool logUnderruns = DebugLogger.IsEnabled("audio");

                // ---- Render loop ----
                while (!_stopRequested)
                {
                    _audioEvent.WaitOne(200);
                    if (_stopRequested) break;

                    uint padding;
                    int hr = AC_GetCurrentPadding(client, &padding);
                    if (hr == AUDCLNT_E_DEVICE_INVALIDATED) { deviceInvalidated = true; break; }
                    ThrowIfFailed(hr, "GetCurrentPadding");

                    if (padding == 0 && logUnderruns)
                    {
                        underruns++;
                        if (underruns == 1 || underruns == 10 || underruns % 100 == 0)
                            DebugLogger.Log("audio", $"[WASAPI-LL] Buffer ran empty ({underruns} times). If you hear glitches, raise NETKEYER_WASAPI_TARGET_FRAMES.");
                    }

                    int toWrite = target - (int)padding;
                    if (toWrite > 0)
                    {
                        hr = Fill(render, init, mono, toWrite);
                        if (hr == AUDCLNT_E_DEVICE_INVALIDATED) { deviceInvalidated = true; break; }
                        ThrowIfFailed(hr, "GetBuffer/ReleaseBuffer");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!initDone.IsSet)
                {
                    _initError = ex;
                    initDone.Set();
                }
                else
                {
                    DebugLogger.Log("audio", $"[WASAPI-LL] Render thread error: {ex.Message}");
                    deviceInvalidated = true;
                }
            }
            finally
            {
                if (started && client != IntPtr.Zero)
                {
                    AC_Stop(client);
                    AC_Reset(client);
                }
                if (mmcss != IntPtr.Zero) AvRevertMmThreadCharacteristics(mmcss);
                if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
                if (render != IntPtr.Zero) Marshal.Release(render);
                if (client != IntPtr.Zero) Marshal.Release(client);
                if (device != IntPtr.Zero) Marshal.Release(device);
                if (enumerator != IntPtr.Zero) Marshal.Release(enumerator);
            }

            if (deviceInvalidated && !_stopRequested && !_disposed)
            {
                DebugLogger.Log("audio", "[WASAPI-LL] Device invalidated, restarting");
                System.Threading.Tasks.Task.Run(() => Restart(fallBackToDefault: !_followDefault));
            }
        }

        private IntPtr OpenDevice(IntPtr enumerator)
        {
            IntPtr device = IntPtr.Zero;

            if (!_followDefault)
            {
                string id = FindDeviceIdByName(_requestedDeviceName);
                if (id != null)
                {
                    fixed (char* pId = id)
                    {
                        if (Enum_GetDevice(enumerator, pId, &device) >= 0 && device != IntPtr.Zero)
                        {
                            _currentDeviceId = id;
                            return device;
                        }
                    }
                }
                DebugLogger.Log("audio", $"[WASAPI-LL] Device '{_requestedDeviceName}' not found, using default");
                _followDefault = true;
            }

            ThrowIfFailed(Enum_GetDefaultAudioEndpoint(enumerator, eRender, eMultimedia, &device), "GetDefaultAudioEndpoint");
            _currentDeviceId = null;
            try
            {
                using var e = new MMDeviceEnumerator();
                _currentDeviceId = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            }
            catch { }
            return device;
        }

        private static string FindDeviceIdByName(string friendlyName)
        {
            try
            {
                using var e = new MMDeviceEnumerator();
                foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    if (d.FriendlyName == friendlyName)
                        return d.ID;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"[WASAPI-LL] Device enumeration failed: {ex.Message}");
            }
            return null;
        }

        private struct ClientInit
        {
            public IntPtr Client;
            public IntPtr Format; // CoTaskMem; freed by caller
            public int SampleRate;
            public int Channels;
            public SampleKind Kind;
            public int PeriodFrames;
            public string Mode;
        }

        private static ClientInit InitializeClient(IntPtr device, int? requiredSampleRate)
        {
            bool tryAc3 = !LatencyOptions.WasapiDisableAudioClient3;

            // Read the engine mix format
            IntPtr probe = Activate(device, IID_IAudioClient);
            IntPtr mix;
            try
            {
                ThrowIfFailed(AC_GetMixFormat(probe, &mix), "GetMixFormat");
            }
            finally
            {
                Marshal.Release(probe);
            }

            ParseFormat(mix, out int mixRate, out int mixChannels, out SampleKind? mixKind);

            IntPtr format;
            SampleKind kind;
            bool convert = mixKind == null || (requiredSampleRate.HasValue && requiredSampleRate.Value != mixRate);
            int rate;
            if (convert)
            {
                // Our own float format; the engine converts (only possible with the classic Initialize)
                rate = requiredSampleRate ?? mixRate;
                format = BuildFloatFormat(rate, mixChannels);
                Marshal.FreeCoTaskMem(mix);
                kind = SampleKind.Float32;
                tryAc3 = false;
            }
            else
            {
                format = mix;
                rate = mixRate;
                kind = mixKind.Value;
            }

            // ---- IAudioClient3: small engine periods ----
            if (tryAc3)
            {
                IntPtr ac3 = TryActivate(device, IID_IAudioClient3);
                if (ac3 != IntPtr.Zero)
                {
                    uint defP, fundP, minP, maxP;
                    int hr = AC3_GetSharedModeEnginePeriod(ac3, format, &defP, &fundP, &minP, &maxP);
                    Marshal.Release(ac3);

                    if (hr >= 0)
                    {
                        uint wanted = minP;
                        if (LatencyOptions.WasapiPeriodFrames is int req && req > 0)
                        {
                            wanted = (uint)Math.Clamp(req, (int)minP, (int)maxP);
                            if (fundP > 0) wanted = Math.Max(minP, wanted / fundP * fundP);
                        }

                        DebugLogger.Log("audio", $"[WASAPI-LL] Engine periods (frames): default={defP} fundamental={fundP} min={minP} max={maxP}; requesting {wanted}");

                        foreach (uint attempt in new[] { wanted, 0u })
                        {
                            IntPtr c = Activate(device, IID_IAudioClient3);
                            uint p = attempt;
                            if (p == 0)
                            {
                                // Engine may already be locked to another period by another stream: use that.
                                IntPtr curFmt; uint cur;
                                if (AC3_GetCurrentSharedModeEnginePeriod(c, &curFmt, &cur) >= 0)
                                {
                                    Marshal.FreeCoTaskMem(curFmt);
                                    p = cur;
                                }
                                if (p == 0 || p == wanted) { Marshal.Release(c); break; }
                            }

                            hr = AC3_InitializeSharedAudioStream(c, AUDCLNT_STREAMFLAGS_EVENTCALLBACK, p, format, IntPtr.Zero);
                            if (hr >= 0)
                            {
                                return new ClientInit
                                {
                                    Client = c, Format = format, SampleRate = rate, Channels = mixChannels,
                                    Kind = kind, PeriodFrames = (int)p, Mode = "IAudioClient3"
                                };
                            }
                            DebugLogger.Log("audio", $"[WASAPI-LL] InitializeSharedAudioStream({p}) failed: 0x{hr:X8}");
                            Marshal.Release(c);
                        }
                    }
                    else
                    {
                        DebugLogger.Log("audio", $"[WASAPI-LL] GetSharedModeEnginePeriod failed: 0x{hr:X8}");
                    }
                }
                else
                {
                    DebugLogger.Log("audio", "[WASAPI-LL] IAudioClient3 not available");
                }
            }

            // ---- Classic IAudioClient ----
            IntPtr client = Activate(device, IID_IAudioClient);
            uint flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK;
            if (convert) flags |= AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
            int hrInit = AC_Initialize(client, AUDCLNT_SHAREMODE_SHARED, flags, 0, 0, format, IntPtr.Zero);
            if (hrInit < 0)
            {
                Marshal.Release(client);
                Marshal.FreeCoTaskMem(format);
                ThrowIfFailed(hrInit, "IAudioClient::Initialize");
            }

            long defaultPeriod, minPeriod;
            int periodFrames = rate / 100; // 10 ms if the query fails
            if (AC_GetDevicePeriod(client, &defaultPeriod, &minPeriod) >= 0 && defaultPeriod > 0)
                periodFrames = (int)(defaultPeriod * rate / 10_000_000L);

            return new ClientInit
            {
                Client = client, Format = format, SampleRate = rate, Channels = mixChannels,
                Kind = kind, PeriodFrames = Math.Max(1, periodFrames),
                Mode = convert ? "IAudioClient (engine conversion)" : "IAudioClient"
            };
        }

        /// <summary>
        /// Renders <paramref name="frames"/> frames from the provider into the WASAPI buffer.
        /// Returns an HRESULT.
        /// </summary>
        private int Fill(IntPtr render, in ClientInit init, float[] mono, int frames)
        {
            if (frames <= 0) return 0;
            if (frames > mono.Length) frames = mono.Length;

            byte* data;
            int hr = RC_GetBuffer(render, (uint)frames, &data);
            if (hr < 0) return hr;

            _sidetoneProvider.Read(mono, 0, frames);

            int ch = init.Channels;
            if (init.Kind == SampleKind.Float32)
            {
                float* dst = (float*)data;
                for (int i = 0; i < frames; i++)
                {
                    float v = mono[i];
                    // Tone on the first two channels only (front L/R on surround layouts)
                    for (int c = 0; c < ch; c++) *dst++ = c < 2 ? v : 0f;
                }
            }
            else
            {
                short* dst = (short*)data;
                for (int i = 0; i < frames; i++)
                {
                    short v = (short)Math.Clamp((int)(mono[i] * 32767f), short.MinValue, short.MaxValue);
                    for (int c = 0; c < ch; c++) *dst++ = c < 2 ? v : (short)0;
                }
            }

            return RC_ReleaseBuffer(render, (uint)frames, 0);
        }

        // ---------------------------------------------------------------------
        // Format helpers
        // ---------------------------------------------------------------------

        private static void ParseFormat(IntPtr fmt, out int rate, out int channels, out SampleKind? kind)
        {
            byte* p = (byte*)fmt;
            ushort tag = *(ushort*)p;
            channels = *(ushort*)(p + 2);
            rate = (int)*(uint*)(p + 4);
            ushort bits = *(ushort*)(p + 14);
            ushort cbSize = *(ushort*)(p + 16);

            Guid sub = Guid.Empty;
            if (tag == 0xFFFE && cbSize >= 22)
                sub = *(Guid*)(p + 24);

            bool isFloat = tag == 3 || (tag == 0xFFFE && sub == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
            bool isPcm = tag == 1 || (tag == 0xFFFE && sub == KSDATAFORMAT_SUBTYPE_PCM);

            if (isFloat && bits == 32) kind = SampleKind.Float32;
            else if (isPcm && bits == 16) kind = SampleKind.Int16;
            else kind = null;
        }

        private static IntPtr BuildFloatFormat(int rate, int channels)
        {
            IntPtr fmt = Marshal.AllocCoTaskMem(40);
            byte* p = (byte*)fmt;
            *(ushort*)(p + 0) = 0xFFFE;                       // WAVE_FORMAT_EXTENSIBLE
            *(ushort*)(p + 2) = (ushort)channels;
            *(uint*)(p + 4) = (uint)rate;
            *(uint*)(p + 8) = (uint)(rate * channels * 4);    // nAvgBytesPerSec
            *(ushort*)(p + 12) = (ushort)(channels * 4);      // nBlockAlign
            *(ushort*)(p + 14) = 32;                          // wBitsPerSample
            *(ushort*)(p + 16) = 22;                          // cbSize
            *(ushort*)(p + 18) = 32;                          // wValidBitsPerSample
            *(uint*)(p + 20) = channels == 1 ? 0x4u : channels == 2 ? 0x3u : 0u; // dwChannelMask
            *(Guid*)(p + 24) = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
            return fmt;
        }

        // ---------------------------------------------------------------------
        // Raw COM vtable calls
        // ---------------------------------------------------------------------

        private static void** VT(IntPtr p) => *(void***)p;

        private static void ThrowIfFailed(int hr, string what)
        {
            if (hr < 0) throw new COMException($"{what} failed (0x{hr:X8})", hr);
        }

        private static IntPtr TryActivate(IntPtr device, Guid iid)
        {
            IntPtr ppv;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, IntPtr, IntPtr*, int>)VT(device)[VT_Device_Activate])
                (device, &iid, CLSCTX_ALL, IntPtr.Zero, &ppv);
            return hr >= 0 ? ppv : IntPtr.Zero;
        }

        private static IntPtr Activate(IntPtr device, Guid iid)
        {
            IntPtr p = TryActivate(device, iid);
            if (p == IntPtr.Zero) throw new COMException($"IMMDevice::Activate({iid}) failed");
            return p;
        }

        private static int Enum_GetDefaultAudioEndpoint(IntPtr e, int flow, int role, IntPtr* device) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int, int, IntPtr*, int>)VT(e)[VT_Enum_GetDefaultAudioEndpoint])(e, flow, role, device);

        private static int Enum_GetDevice(IntPtr e, char* id, IntPtr* device) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, char*, IntPtr*, int>)VT(e)[VT_Enum_GetDevice])(e, id, device);

        private static int AC_Initialize(IntPtr c, int shareMode, uint flags, long bufferDuration, long periodicity, IntPtr format, IntPtr session) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int, uint, long, long, IntPtr, IntPtr, int>)VT(c)[VT_AC_Initialize])(c, shareMode, flags, bufferDuration, periodicity, format, session);

        private static int AC_GetBufferSize(IntPtr c, uint* frames) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)VT(c)[VT_AC_GetBufferSize])(c, frames);

        private static int AC_GetStreamLatency(IntPtr c, long* latency) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, long*, int>)VT(c)[VT_AC_GetStreamLatency])(c, latency);

        private static int AC_GetCurrentPadding(IntPtr c, uint* padding) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)VT(c)[VT_AC_GetCurrentPadding])(c, padding);

        private static int AC_GetMixFormat(IntPtr c, IntPtr* format) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VT(c)[VT_AC_GetMixFormat])(c, format);

        private static int AC_GetDevicePeriod(IntPtr c, long* defaultPeriod, long* minPeriod) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, long*, long*, int>)VT(c)[VT_AC_GetDevicePeriod])(c, defaultPeriod, minPeriod);

        private static int AC_Start(IntPtr c) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)VT(c)[VT_AC_Start])(c);

        private static int AC_Stop(IntPtr c) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)VT(c)[VT_AC_Stop])(c);

        private static int AC_Reset(IntPtr c) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)VT(c)[VT_AC_Reset])(c);

        private static int AC_SetEventHandle(IntPtr c, IntPtr handle) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)VT(c)[VT_AC_SetEventHandle])(c, handle);

        private static int AC_GetService(IntPtr c, Guid* iid, IntPtr* service) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)VT(c)[VT_AC_GetService])(c, iid, service);

        private static int AC3_GetSharedModeEnginePeriod(IntPtr c, IntPtr format, uint* def, uint* fund, uint* min, uint* max) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, uint*, uint*, uint*, int>)VT(c)[VT_AC3_GetSharedModeEnginePeriod])(c, format, def, fund, min, max);

        private static int AC3_GetCurrentSharedModeEnginePeriod(IntPtr c, IntPtr* format, uint* period) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, uint*, int>)VT(c)[VT_AC3_GetCurrentSharedModeEnginePeriod])(c, format, period);

        private static int AC3_InitializeSharedAudioStream(IntPtr c, uint flags, uint periodFrames, IntPtr format, IntPtr session) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr, IntPtr, int>)VT(c)[VT_AC3_InitializeSharedAudioStream])(c, flags, periodFrames, format, session);

        private static int RC_GetBuffer(IntPtr r, uint frames, byte** data) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, byte**, int>)VT(r)[VT_RC_GetBuffer])(r, frames, data);

        private static int RC_ReleaseBuffer(IntPtr r, uint frames, uint flags) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)VT(r)[VT_RC_ReleaseBuffer])(r, frames, flags);

        // ---------------------------------------------------------------------
        // ISidetoneGenerator
        // ---------------------------------------------------------------------

        public void SetFrequency(int frequencyHz)
        {
            if (frequencyHz < 100 || frequencyHz > 2000) return;
            _sidetoneProvider?.SetFrequency(frequencyHz);
        }

        public void SetVolume(int volumePercent) =>
            _sidetoneProvider?.SetVolume(Math.Clamp(volumePercent / 100.0f, 0.0f, 1.0f));

        public void SetWpm(int wpm) => _sidetoneProvider?.SetWpm(wpm);

        public void Start() => _sidetoneProvider?.StartIndefiniteTone();

        public void Stop() => _sidetoneProvider?.Stop();

        public void StartTone(int durationMs) => _sidetoneProvider?.StartTone(durationMs);

        public void StartSilenceThenTone(int silenceMs, int toneMs) => _sidetoneProvider?.StartSilenceThenTone(silenceMs, toneMs);

        public void QueueSilence(int silenceMs, int? followingToneMs = null) => _sidetoneProvider?.QueueSilence(silenceMs, followingToneMs);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_notificationEnumerator != null && _notificationClient != null)
                    _notificationEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            }
            catch { }
            _notificationEnumerator?.Dispose();
            _notificationEnumerator = null;

            lock (_restartLock)
            {
                StopRenderThread();
            }
            _providerReady.Set(); // release a render thread still waiting for the provider
        }
    }
}
