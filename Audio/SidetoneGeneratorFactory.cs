using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NetKeyer.Helpers;
using PortAudioSharp;

namespace NetKeyer.Audio
{
    public static class SidetoneGeneratorFactory
    {
        /// <summary>
        /// Creates the best available sidetone generator for the current platform.
        /// On Windows, uses WASAPI for ultra-low latency (~3-5ms).
        /// On other platforms, uses PortAudio-based generator (~5-10ms).
        /// </summary>
        /// <param name="deviceId">Audio device name to use, or null/empty for system default</param>
        /// <param name="wasapiAggressiveLowLatency">Windows only: if true, use on-demand device open/close for minimum latency; if false, keep device open</param>
        public static ISidetoneGenerator Create(string deviceId = null, bool wasapiAggressiveLowLatency = true)
        {
            DebugLogger.Log("audio", "Initializing PortAudio sidetone generator");
            return new SidetoneGenerator(deviceId);
        }

        /// <summary>
        /// Enumerates available audio output devices for the current platform.
        /// Returns a list of (deviceId, name) tuples.
        /// </summary>
        public static List<(string deviceId, string name)> EnumerateDevices()
        {
            return EnumeratePortAudioDevices();
        }

        private static List<(string deviceId, string name)> EnumerateWasapiDevices()
        {
            var devices = new List<(string deviceId, string name)>();
            devices.Add(("", "System Default"));

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                foreach (var device in endpoints)
                {
                    // Use FriendlyName as both ID and display name
                    devices.Add((device.FriendlyName, device.FriendlyName));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enumerating WASAPI devices: {ex.Message}");
            }

            return devices;
        }

        private static List<(string deviceId, string name)> EnumeratePortAudioDevices()
        {
            var devices = new List<(string deviceId, string name)>();
            devices.Add(("", "System Default"));

            try
            {
                int deviceCount = PortAudio.DeviceCount;
                for (int i = 0; i < deviceCount; i++)
                {
                    var deviceInfo = PortAudio.GetDeviceInfo(i);
                    if (deviceInfo.maxOutputChannels > 0)
                    {
                        // Use device name as both ID and display name
                        devices.Add((deviceInfo.name, deviceInfo.name));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enumerating PortAudio devices: {ex.Message}");
            }

            return devices;
        }
    }
}
