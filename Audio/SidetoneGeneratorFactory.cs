using System;
using System.Runtime.InteropServices;

namespace NetKeyer.Audio
{
    public static class SidetoneGeneratorFactory
    {
        /// <summary>
        /// Creates the best available sidetone generator for the current platform.
        /// On Windows, uses WASAPI for ultra-low latency (~3-5ms).
        /// On other platforms, uses OpenAL-based generator (~8-10ms).
        /// </summary>
        public static ISidetoneGenerator Create()
        {
            // On Windows, prefer WASAPI for lowest latency
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    Console.WriteLine("Initializing WASAPI sidetone generator (Windows low-latency mode)");
                    return new WasapiSidetoneGenerator();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WASAPI initialization failed, falling back to OpenAL: {ex.Message}");
                    // Fall back to OpenAL if WASAPI fails
                    return new SidetoneGenerator();
                }
            }

            // On Linux/macOS, use OpenAL
            Console.WriteLine("Initializing OpenAL sidetone generator");
            return new SidetoneGenerator();
        }
    }
}
