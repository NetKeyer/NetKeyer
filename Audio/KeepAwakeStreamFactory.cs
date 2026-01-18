using System;
using System.Runtime.InteropServices;
using NetKeyer.Helpers;

namespace NetKeyer.Audio
{
    /// <summary>
    /// Factory for creating platform-appropriate keep-awake audio streams.
    /// </summary>
    public static class KeepAwakeStreamFactory
    {
        /// <summary>
        /// Creates a keep-awake stream for the current platform.
        /// </summary>
        /// <param name="deviceId">Audio device name to use, or null/empty for system default</param>
        public static IKeepAwakeStream Create(string deviceId = null)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    DebugLogger.Log("audio", $"Creating WASAPI keep-awake stream for device: {deviceId ?? "default"}");
                    return new WasapiKeepAwakeStream(deviceId);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("audio", $"WASAPI keep-awake failed, falling back to PortAudio: {ex.Message}");
                    return new PortAudioKeepAwakeStream(deviceId);
                }
            }

            DebugLogger.Log("audio", "Creating PortAudio keep-awake stream");
            return new PortAudioKeepAwakeStream(deviceId);
        }
    }
}
