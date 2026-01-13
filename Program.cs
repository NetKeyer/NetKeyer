// NetKeyer
// Copyright 2025 by Andrew Rodland and NetKeyer contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//   http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// A copy of the License is also contained in the file LICENSE
// located at the root of this source code repository.
// ------------------------------------------------------------
using Avalonia;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Velopack;

namespace NetKeyer;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Configure native library loading for ALSA on Linux before any libraries are loaded
        ConfigureNativeLibraries();

        // Velopack: Handle app installation/update events before starting the main app
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Configures native library loading for cross-platform compatibility.
    /// On Linux, this sets up a resolver to redirect ALSA library loading from
    /// "libasound.so" (dev package) to "libasound.so.2" (runtime package).
    /// </summary>
    private static void ConfigureNativeLibraries()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Set up resolver for alsa-sharp's ALSA library dependency.
            // The alsa-sharp library (used by managed-midi) uses [DllImport("asound")] which
            // looks for "libasound.so", but this symlink is only present in the ALSA development
            // package (libasound2-dev). On runtime-only systems, we need to load "libasound.so.2" instead.
            AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
            {
                if (args.LoadedAssembly.GetName().Name == "alsa-sharp")
                {
                    NativeLibrary.SetDllImportResolver(args.LoadedAssembly, AlsaLibraryResolver);
                }
            };
        }
    }

    /// <summary>
    /// Custom native library resolver for ALSA on Linux.
    /// Redirects "asound" to "libasound.so.2" (runtime library) instead of
    /// "libasound.so" (dev package symlink).
    /// </summary>
    private static IntPtr AlsaLibraryResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == "asound")
        {
            // Try to load the runtime version of the ALSA library
            if (NativeLibrary.TryLoad("libasound.so.2", assembly, searchPath, out var handle))
            {
                return handle;
            }
        }

        // Return IntPtr.Zero to let the default loading mechanism try
        return IntPtr.Zero;
    }
}
