using System;
using System.Runtime.InteropServices;

namespace NetKeyer.Audio
{
    /// <summary>
    /// Monitors default audio device changes on macOS using CoreAudio APIs.
    /// Similar to Windows IMMNotificationClient but for macOS CoreAudio.
    /// </summary>
    internal class CoreAudioDeviceMonitor : IDisposable
    {
        private readonly Action _onDefaultDeviceChanged;
        private AudioObjectPropertyListenerProc _listenerProc;
        private bool _disposed;

        // CoreAudio constants
        private const uint kAudioObjectSystemObject = 1;
        private const uint kAudioObjectPropertyScopeGlobal = 0x676C6F62; // 'glob'
        private const uint kAudioObjectPropertyElementMain = 0;
        private const uint kAudioHardwarePropertyDefaultOutputDevice = 0x646F7574; // 'dout'

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioObjectPropertyAddress
        {
            public uint mSelector;
            public uint mScope;
            public uint mElement;
        }

        // CoreAudio callback delegate
        private delegate int AudioObjectPropertyListenerProc(
            uint inObjectID,
            uint inNumberAddresses,
            ref AudioObjectPropertyAddress inAddresses,
            IntPtr inClientData);

        // CoreAudio P/Invoke declarations
        [DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio", EntryPoint = "AudioObjectAddPropertyListener")]
        private static extern int AudioObjectAddPropertyListener(
            uint inObjectID,
            ref AudioObjectPropertyAddress inAddress,
            AudioObjectPropertyListenerProc inListener,
            IntPtr inClientData);

        [DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio", EntryPoint = "AudioObjectRemovePropertyListener")]
        private static extern int AudioObjectRemovePropertyListener(
            uint inObjectID,
            ref AudioObjectPropertyAddress inAddress,
            AudioObjectPropertyListenerProc inListener,
            IntPtr inClientData);

        public CoreAudioDeviceMonitor(Action onDefaultDeviceChanged)
        {
            _onDefaultDeviceChanged = onDefaultDeviceChanged;

            try
            {
                // Create the listener delegate and keep a reference to prevent GC
                _listenerProc = OnPropertyChanged;

                // Set up the property address for the default output device
                var propertyAddress = new AudioObjectPropertyAddress
                {
                    mSelector = kAudioHardwarePropertyDefaultOutputDevice,
                    mScope = kAudioObjectPropertyScopeGlobal,
                    mElement = kAudioObjectPropertyElementMain
                };

                // Register the listener
                int result = AudioObjectAddPropertyListener(
                    kAudioObjectSystemObject,
                    ref propertyAddress,
                    _listenerProc,
                    IntPtr.Zero
                );

                if (result != 0)
                {
                    Console.WriteLine($"Failed to register CoreAudio device listener: {result}");
                }
                else
                {
                    Console.WriteLine("CoreAudio device change monitoring enabled");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize CoreAudio device monitoring: {ex.Message}");
            }
        }

        private int OnPropertyChanged(
            uint inObjectID,
            uint inNumberAddresses,
            ref AudioObjectPropertyAddress inAddresses,
            IntPtr inClientData)
        {
            if (_disposed)
                return 0;

            try
            {
                // Notify on the thread pool to avoid blocking CoreAudio
                System.Threading.Tasks.Task.Run(() =>
                {
                    _onDefaultDeviceChanged?.Invoke();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CoreAudio property change callback: {ex.Message}");
            }

            return 0; // noErr
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (_listenerProc != null)
                {
                    var propertyAddress = new AudioObjectPropertyAddress
                    {
                        mSelector = kAudioHardwarePropertyDefaultOutputDevice,
                        mScope = kAudioObjectPropertyScopeGlobal,
                        mElement = kAudioObjectPropertyElementMain
                    };

                    AudioObjectRemovePropertyListener(
                        kAudioObjectSystemObject,
                        ref propertyAddress,
                        _listenerProc,
                        IntPtr.Zero
                    );

                    Console.WriteLine("CoreAudio device change monitoring disabled");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing CoreAudio listener: {ex.Message}");
            }
        }
    }
}
