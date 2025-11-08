# NetKeyer Installer Setup

This project uses [Velopack](https://velopack.io) to create cross-platform installers with automatic update support for Windows, Linux, and macOS.

## Quick Start

### Building Locally

**Build all platforms (Windows, Linux x64/ARM64):**
```bash
# On Linux/macOS:
./build-installer.sh 1.0.0

# On Windows (PowerShell):
./build-installer.ps1 -Version "1.0.0"
```

**Build specific platform:**
```bash
# Linux/macOS:
./build-installer.sh 1.0.0 windows     # Windows only
./build-installer.sh 1.0.0 linux       # Linux x64 + ARM64

# Windows (PowerShell):
./build-installer.ps1 -Version "1.0.0" -Platform windows
./build-installer.ps1 -Version "1.0.0" -Platform linux
```

### Output Files

Installers are created in `NetKeyer/Releases/<platform>/`:

**Windows (`win-x64/`):**
- `NetKeyer-1.0.0-Setup.exe` - Windows installer
- `NetKeyer-1.0.0-full.nupkg` - Full release package
- `NetKeyer-1.0.0-delta.nupkg` - Delta update
- `RELEASES` - Update manifest

**Linux (`linux-x64/` and `linux-arm64/`):**
- `NetKeyer-1.0.0.AppImage` - Portable Linux executable
- `NetKeyer-1.0.0-full.nupkg` - Full release package
- `NetKeyer-1.0.0-delta.nupkg` - Delta update
- `RELEASES` - Update manifest

**macOS (`osx-x64/` and `osx-arm64/` - must build on Mac):**
- `NetKeyer-1.0.0.dmg` - macOS installer (Intel x64 or Apple Silicon ARM64)
- Package files and manifest

## GitHub Actions

The project includes automated multi-platform builds via GitHub Actions.

### Automatic Builds on Version Tags

Create and push a version tag to trigger builds for all platforms:
```bash
git tag v1.0.0
git push origin v1.0.0
```

This will:
1. Build installers for Windows x64, Linux x64/ARM64, and macOS x64/ARM64 in parallel
2. Create a GitHub Release with all installers
3. Upload all platform installers as release assets

### Manual Builds

Go to the Actions tab in GitHub and run the "Build Multi-Platform Installers" workflow manually, specifying the version number. This will build all platforms.

## Distribution

### Option 1: GitHub Releases (Recommended)

Upload all platform files to a GitHub Release:
1. Create a new release on GitHub
2. Upload all files from `NetKeyer/Releases/*/` (all platform subdirectories)
3. Distribute platform-specific installers to users:
   - Windows: `NetKeyer-X.Y.Z-Setup.exe` (x64)
   - Linux: `NetKeyer-X.Y.Z-x64.AppImage` or `NetKeyer-X.Y.Z-arm64.AppImage`
   - macOS: `NetKeyer-X.Y.Z-x64.dmg` (Intel) or `NetKeyer-X.Y.Z-arm64.dmg` (Apple Silicon)

Auto-updates will work automatically since Velopack can read from GitHub Releases.

### Option 2: Custom Web Server

Upload all files from `NetKeyer/Releases/` to a web-accessible directory:
```
https://yoursite.com/releases/
  ├── NetKeyer-1.0.0-Setup.exe
  ├── NetKeyer-1.0.0-full.nupkg
  ├── NetKeyer-1.0.0-delta.nupkg
  └── RELEASES
```

Update the application code to point to your update URL (see below).

## Implementing Auto-Updates

To add a "Check for Updates" feature to your app, add this code to your MainWindowViewModel:

```csharp
using Velopack;

[RelayCommand]
private async Task CheckForUpdatesAsync()
{
    try
    {
        var mgr = new UpdateManager("https://github.com/yourname/netkeyer/releases");

        var updateInfo = await mgr.CheckForUpdatesAsync();
        if (updateInfo == null)
        {
            // No updates available
            return;
        }

        // Download the update
        await mgr.DownloadUpdatesAsync(updateInfo);

        // Ask user if they want to restart and install
        // (Show a dialog here)

        // Restart and apply update
        mgr.ApplyUpdatesAndRestart(updateInfo);
    }
    catch (Exception ex)
    {
        // Handle errors (no internet, etc.)
        Console.WriteLine($"Update check failed: {ex.Message}");
    }
}
```

## Cross-Platform Building

**Yes!** Velopack supports extensive cross-compilation:

- **From Linux/macOS**: Can build Windows and Linux installers
- **From Windows**: Can build Windows and Linux installers
- **From macOS**: Can build macOS, Windows, and Linux installers
- **Note**: macOS installers require building on macOS due to code signing requirements

The build scripts automatically handle cross-compilation:
- Bash script (`build-installer.sh`) works on Linux/macOS
- PowerShell script (`build-installer.ps1`) works on Windows/Linux/macOS
- GitHub Actions uses platform-specific runners for optimal results

## Versioning

Version numbers should follow [Semantic Versioning](https://semver.org/):
- **Major.Minor.Patch** (e.g., `1.0.0`)
- Increment **Major** for breaking changes
- Increment **Minor** for new features
- Increment **Patch** for bug fixes

## Prerequisites

- .NET 8.0 SDK
- `vpk` tool (installed automatically by build scripts)

## Native Dependencies

The application includes native library dependencies that are automatically bundled:
- **OpenAL** (audio library) - Included via `OpenAL.Soft` NuGet package for Windows x64/x86/ARM64
- The build automatically copies the correct OpenAL DLL for the target platform

## Troubleshooting

### Build Fails: "vpk command not found"

Run: `dotnet tool install -g vpk`

### Updates Not Working

Ensure all files from `NetKeyer/Releases/` are uploaded together, including the `RELEASES` manifest file.

### Icon Not Showing

Make sure `NetKeyer/Assets/icon.ico` exists. Update the `--icon` parameter in build scripts if your icon is elsewhere.

## Learn More

- [Velopack Documentation](https://docs.velopack.io)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
