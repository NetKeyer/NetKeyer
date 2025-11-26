using System;
using NAudio.Wave;
using NAudio.CoreAudioApi;

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

        // Use ultra-low latency - 1ms at 48kHz
        private const int SAMPLE_RATE = 48000;
        private const int LATENCY_MS = 1;

        public WasapiSidetoneGenerator()
        {
            try
            {
                // Create custom sidetone provider
                _sidetoneProvider = new SidetoneProvider();
                _sidetoneProvider.SetFrequency(_frequency);
                _sidetoneProvider.SetVolume(_volume);
                _sidetoneProvider.SetWpm(_wpm);

                // Set up device change monitoring
                _deviceEnumerator = new MMDeviceEnumerator();
                _notificationClient = new DeviceNotificationClient(OnDefaultDeviceChanged);
                _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);

                // Initialize WASAPI output
                InitializeWasapiOut();

                // Don't start the audio stream until needed - this reduces latency
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize WASAPI audio: {ex.Message}");
                Dispose();
                throw;
            }
        }

        private void InitializeWasapiOut()
        {
            // Initialize WASAPI in shared mode with minimal latency
            // Note: Exclusive mode would be even lower latency but may not be available
            _wasapiOut = new WasapiOut(
                AudioClientShareMode.Shared,
                LATENCY_MS
            );

            _wasapiOut.Init(_sidetoneProvider);
        }

        private void OnDefaultDeviceChanged()
        {
            if (_disposed)
                return;

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

                // Don't auto-start - will be started when tone is needed
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to switch to new default audio device: {ex.Message}");
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
                Console.WriteLine($"Failed to start WASAPI sidetone: {ex.Message}");
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

                // Schedule WASAPI stop after ramp-down completes
                // Poll until the provider is actually silent to avoid race conditions
                if (_isPlaying)
                {
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        // Wait for ramp-down to complete (poll every 5ms, max 50ms)
                        for (int i = 0; i < 10; i++)
                        {
                            await System.Threading.Tasks.Task.Delay(5);
                            if (_sidetoneProvider?.IsSilent == true)
                            {
                                break;
                            }
                        }

                        // Only stop if still silent (no new tone started)
                        if (_wasapiOut != null && _isPlaying && _sidetoneProvider?.IsSilent == true)
                        {
                            try
                            {
                                _wasapiOut.Stop();
                                _isPlaying = false;
                            }
                            catch { }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to stop WASAPI sidetone: {ex.Message}");
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

                // Schedule WASAPI stop after tone completes
                System.Threading.Tasks.Task.Run(async () =>
                {
                    // Wait for tone duration plus a bit extra for ramp-down
                    await System.Threading.Tasks.Task.Delay(durationMs + 20);

                    // Only stop if still silent (no new tone started)
                    if (_wasapiOut != null && _isPlaying && _sidetoneProvider?.IsSilent == true)
                    {
                        try
                        {
                            _wasapiOut.Stop();
                            _isPlaying = false;
                        }
                        catch { }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start timed WASAPI sidetone: {ex.Message}");
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

        public DeviceNotificationClient(Action onDefaultDeviceChanged)
        {
            _onDefaultDeviceChanged = onDefaultDeviceChanged;
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
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
