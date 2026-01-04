using System;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NetKeyer.Helpers;

namespace NetKeyer.Audio
{
    /// <summary>
    /// Windows-specific WASAPI sidetone generator for ultra-low latency audio output.
    /// Uses exclusive mode WASAPI with minimal buffering for ~3-5ms latency.
    /// Automatically follows Windows default playback device changes.
    /// </summary>
    public class WasapiSidetoneGenerator : ISidetoneGenerator
    {
        private WasapiOut _wasapiOut;
        private SidetoneProvider _sidetoneProvider;
        private bool _disposed;
        private bool _isPlaying;
        private int _frequency = 600;
        private float _volume = 0.5f;
        private int _wpm = 20;
        private MMDeviceEnumerator _deviceEnumerator;
        private DeviceNotificationClient _notificationClient;
        private string _selectedDeviceId = null;  // null or empty = default
        private MMDevice _selectedDevice;
        private bool _aggressiveLowLatency = true; // true = on-demand, false = keep open

        // Use absolute minimum latency - WASAPI minimum is typically 3ms in shared mode
        private const int SAMPLE_RATE = 48000;
        private const int LATENCY_MS = 0; // Let WASAPI use its minimum possible

        public event Action OnSilenceComplete;
        public event Action OnToneStart;
        public event Action OnToneComplete;
        public event Action OnBeforeSilenceEnd;
        public event Action OnBecomeIdle;

        public WasapiSidetoneGenerator(string deviceId = null, bool aggressiveLowLatency = true)
        {
            try
            {
                // Store selected device ID and aggressiveness flag
                _selectedDeviceId = deviceId;
                _aggressiveLowLatency = aggressiveLowLatency;

                // Create custom sidetone provider
                _sidetoneProvider = new SidetoneProvider();
                _sidetoneProvider.SetFrequency(_frequency);
                _sidetoneProvider.SetVolume(_volume);
                _sidetoneProvider.SetWpm(_wpm);

                // Forward events
                _sidetoneProvider.OnSilenceComplete += () => OnSilenceComplete?.Invoke();
                _sidetoneProvider.OnToneStart += () => OnToneStart?.Invoke();
                _sidetoneProvider.OnToneComplete += () => OnToneComplete?.Invoke();
                _sidetoneProvider.OnBeforeSilenceEnd += () => OnBeforeSilenceEnd?.Invoke();

                // Conditional event handling based on mode
                if (_aggressiveLowLatency)
                {
                    _sidetoneProvider.OnBecomeIdle += OnProviderBecomeIdle;
                }
                // If not aggressive, DON'T subscribe to OnBecomeIdle - let device stay open

                // Set up device change monitoring
                _deviceEnumerator = new MMDeviceEnumerator();
                _notificationClient = new DeviceNotificationClient(OnDefaultDeviceChanged, OnDeviceStateChangedCallback);
                _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);

                // Initialize WASAPI output
                InitializeWasapiOut();

                // If NOT aggressive low-latency, start the audio stream immediately (like PortAudio)
                if (!_aggressiveLowLatency)
                {
                    try
                    {
                        _wasapiOut.Play();
                        _isPlaying = true;
                        DebugLogger.Log("audio", "WASAPI initialized in always-on mode (not aggressive low-latency)");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log("audio", $"Failed to start always-on WASAPI stream: {ex.Message}");
                    }
                }
                else
                {
                    DebugLogger.Log("audio", "WASAPI initialized in aggressive low-latency mode (on-demand)");
                }
                // Don't start the audio stream until needed - this reduces latency (only in aggressive mode)
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to initialize WASAPI audio: {ex.Message}");
                Dispose();
                throw;
            }
        }

        private void InitializeWasapiOut()
        {
            MMDevice device;

            if (string.IsNullOrEmpty(_selectedDeviceId) || _selectedDeviceId == "System Default")
            {
                // Use system default
                device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            else
            {
                // Find device by friendly name
                device = FindDeviceByName(_selectedDeviceId);
                if (device == null)
                {
                    DebugLogger.Log("audio", $"Device '{_selectedDeviceId}' not found, falling back to default");
                    device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _selectedDeviceId = null; // Reset to default
                }
            }

            _selectedDevice = device;

            // Initialize WASAPI in shared mode with minimal latency
            // Note: Exclusive mode would be even lower latency but may not be available
            _wasapiOut = new WasapiOut(
                device,
                AudioClientShareMode.Shared,
                true,
                LATENCY_MS
            );

            _wasapiOut.Init(_sidetoneProvider);
        }

        private MMDevice FindDeviceByName(string friendlyName)
        {
#if WINDOWS
            using var enumerator = new MMDeviceEnumerator();
            var collection = enumerator.EnumerateAudioEndpoints(DataFlow.Render, DeviceState.Active);

            foreach (var device in collection)
            {
                if (device.FriendlyName == friendlyName)
                    return device;
            }
#endif
            return null;
        }

        private void OnProviderBecomeIdle()
        {
            if (_disposed || _wasapiOut == null)
                return;

            // Stop WASAPI asynchronously to avoid stopping it from within its own callback chain
            // This is important because OnProviderBecomeIdle is now called synchronously
            // from Read(), and we don't want to call Stop() on WASAPI while it's calling us
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // Stop WASAPI playback when fully idle to minimize latency for next tone
                    if (_isPlaying && !_disposed && _wasapiOut != null)
                    {
                        _wasapiOut.Stop();
                        _isPlaying = false;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("audio", $"Failed to stop WASAPI on idle: {ex.Message}");
                }
            });

            // Forward the event
            OnBecomeIdle?.Invoke();
        }

        private void OnDefaultDeviceChanged()
        {
            if (_disposed)
                return;

            // Only respond to default device changes if user selected "System Default"
            if (!string.IsNullOrEmpty(_selectedDeviceId) && _selectedDeviceId != "System Default")
                return; // User selected specific device, ignore default changes

            try
            {
                // Stop and dispose old device
                if (_wasapiOut != null)
                {
                    if (_isPlaying)
                    {
                        _wasapiOut.Stop();
                        _isPlaying = false;
                    }
                    _wasapiOut.Dispose();
                    _wasapiOut = null;
                }

                // Reinitialize with new default device
                InitializeWasapiOut();

                // If NOT aggressive low-latency, restart the stream immediately
                if (!_aggressiveLowLatency && _wasapiOut != null)
                {
                    try
                    {
                        _wasapiOut.Play();
                        _isPlaying = true;
                        DebugLogger.Log("audio", "WASAPI restarted in always-on mode after device change");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log("audio", $"Failed to restart always-on WASAPI stream: {ex.Message}");
                    }
                }
                // Don't auto-start in aggressive mode - will be started when tone is needed
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to switch to new default audio device: {ex.Message}");
            }
        }

        private void OnDeviceStateChangedCallback(string deviceId, DeviceState newState)
        {
            if (_disposed)
                return;

            // Check if our selected device was disconnected or disabled
            if (_selectedDevice != null && _selectedDevice.ID == deviceId)
            {
                if (newState != DeviceState.Active)
                {
                    DebugLogger.Log("audio", $"Selected device '{_selectedDeviceId}' disconnected, falling back to default");
                    _selectedDeviceId = null; // Fall back to default
                    OnDefaultDeviceChanged(); // Reinitialize with default device
                }
            }
        }

        public void SetFrequency(int frequencyHz)
        {
            if (frequencyHz < 100 || frequencyHz > 2000)
                return;

            _frequency = frequencyHz;

            if (_sidetoneProvider != null)
            {
                _sidetoneProvider.SetFrequency(frequencyHz);
            }
        }

        public void SetVolume(int volumePercent)
        {
            _volume = Math.Clamp(volumePercent / 100.0f, 0.0f, 1.0f);

            if (_sidetoneProvider != null)
            {
                _sidetoneProvider.SetVolume(_volume);
            }
        }

        public void SetWpm(int wpm)
        {
            _wpm = wpm;

            if (_sidetoneProvider != null)
            {
                _sidetoneProvider.SetWpm(wpm);
            }
        }

        public void Start()
        {
            if (_disposed || _wasapiOut == null)
                return;

            try
            {
                // Start indefinite tone (for straight-key mode)
                _sidetoneProvider?.StartIndefiniteTone();

                // Start WASAPI playback if not already playing
                if (!_isPlaying)
                {
                    _wasapiOut.Play();
                    _isPlaying = true;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to start WASAPI sidetone: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (_disposed || _wasapiOut == null)
                return;

            try
            {
                // Stop the tone (triggers ramp-down in the provider)
                _sidetoneProvider?.Stop();

                // For straight-key mode with aggressive latency:
                // Immediately stop WASAPI to minimize latency
                // The ramp-down will be cut short, but that's better than 35-45ms delay
                if (_aggressiveLowLatency && _isPlaying)
                {
                    _wasapiOut.Stop();
                    _isPlaying = false;
                }
                // If NOT aggressive, let the provider ramp down naturally and don't stop WASAPI
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to stop WASAPI sidetone: {ex.Message}");
            }
        }

        public void StartTone(int durationMs)
        {
            if (_disposed || _wasapiOut == null)
                return;

            try
            {
                // Start timed tone (for iambic mode)
                _sidetoneProvider?.StartTone(durationMs);

                // Start WASAPI playback if not already playing
                if (!_isPlaying)
                {
                    _wasapiOut.Play();
                    _isPlaying = true;
                }

                // For timed tones (iambic mode), don't stop WASAPI immediately
                // Let it keep running for the next dit/dah to minimize inter-element latency
                // WASAPI will be stopped by OnProviderBecomeIdle when truly idle (not in timed silence)
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to start timed WASAPI sidetone: {ex.Message}");
            }
        }

        public void StartSilenceThenTone(int silenceMs, int toneMs)
        {
            if (_disposed || _wasapiOut == null)
                return;

            try
            {
                _sidetoneProvider?.StartSilenceThenTone(silenceMs, toneMs);

                // Start WASAPI playback if not already playing
                if (!_isPlaying)
                {
                    _wasapiOut.Play();
                    _isPlaying = true;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to start silence+tone: {ex.Message}");
            }
        }

        public void QueueSilence(int silenceMs, int? followingToneMs = null)
        {
            if (_disposed || _wasapiOut == null)
                return;

            try
            {
                _sidetoneProvider?.QueueSilence(silenceMs, followingToneMs);
            }
            catch (Exception ex)
            {
                DebugLogger.Log("audio", $"Failed to queue silence: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Unregister device change notifications
            if (_deviceEnumerator != null && _notificationClient != null)
            {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            }

            if (_wasapiOut != null)
            {
                if (_isPlaying)
                {
                    _wasapiOut.Stop();
                    _isPlaying = false;
                }
                _wasapiOut.Dispose();
                _wasapiOut = null;
            }

            if (_deviceEnumerator != null)
            {
                _deviceEnumerator.Dispose();
                _deviceEnumerator = null;
            }
        }
    }

    /// <summary>
    /// Notification client for monitoring Windows default audio device changes
    /// </summary>
    internal class DeviceNotificationClient : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        private readonly Action _onDefaultDeviceChanged;
        private readonly Action<string, DeviceState> _onDeviceStateChanged;

        public DeviceNotificationClient(Action onDefaultDeviceChanged, Action<string, DeviceState> onDeviceStateChanged = null)
        {
            _onDefaultDeviceChanged = onDefaultDeviceChanged;
            _onDeviceStateChanged = onDeviceStateChanged;
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Only respond to changes in the default playback device
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                _onDefaultDeviceChanged?.Invoke();
            }
        }

        // These other events we don't need to handle
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string pwstrDeviceId) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            _onDeviceStateChanged?.Invoke(deviceId, newState);
        }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
