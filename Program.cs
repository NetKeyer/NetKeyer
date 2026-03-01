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
using System.IO;
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
        // Configure native library loading before any P/Invoke calls occur
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
    /// Registers a resolver for the netkeyer_midi_shim native library so that
    /// it is found in the application's base directory regardless of platform.
    /// </summary>
    private static void ConfigureNativeLibraries()
    {
        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (name, asm, path) =>
        {
            if (name != "netkeyer_midi_shim") return IntPtr.Zero;
            var dir = AppContext.BaseDirectory;
            var libName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "netkeyer_midi_shim.dll"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "libnetkeyer_midi_shim.dylib"
                : "libnetkeyer_midi_shim.so";
            NativeLibrary.TryLoad(Path.Combine(dir, libName), asm, null, out var handle);
            return handle;
        });
    }
}
