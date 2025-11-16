#!/usr/bin/env pwsh
# Build script for creating NetKeyer cross-platform installers with Velopack
# Usage: ./build-installer.ps1 -Version "1.0.0" [-Platform "all|windows|linux"]
# Examples:
#   ./build-installer.ps1 -Version "1.0.0"                    # Build all platforms
#   ./build-installer.ps1 -Version "1.0.0" -Platform windows  # Build Windows only

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter(Mandatory=$false)]
    [ValidateSet("all", "windows", "linux", "macos")]
    [string]$Platform = "all"
)

$ErrorActionPreference = "Stop"

Write-Host "Building NetKeyer installer(s) v$Version" -ForegroundColor Cyan

# Step 1: Install vpk tool if not already installed
Write-Host "`n[1/5] Checking vpk tool..." -ForegroundColor Yellow
$vpkInstalled = dotnet tool list -g | Select-String "vpk"
if (-not $vpkInstalled) {
    Write-Host "Installing vpk tool..." -ForegroundColor Yellow
    dotnet tool install -g vpk
}

# Step 2: Clean previous builds
Write-Host "`n[2/5] Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean -c Release
# Remove all obj and bin directories to prevent cross-platform build contamination
Get-ChildItem -Path . -Include "obj","bin" -Recurse -Directory -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path . -Filter "publish-*" -Directory | Remove-Item -Recurse -Force
if (Test-Path "Releases") { Remove-Item -Recurse -Force "Releases" }

# Function to build for a specific platform
function Build-Platform {
    param(
        [string]$PlatformName,
        [string]$RuntimeId,
        [string]$VpkTarget,
        [string]$MainExe
    )

    Write-Host "`n=== Building for $PlatformName ($RuntimeId) ===" -ForegroundColor Blue

    # Step 3: Publish the application
    Write-Host "`n[3/5] Publishing $PlatformName application..." -ForegroundColor Yellow
    dotnet publish NetKeyer.csproj `
        -c Release `
        --self-contained true `
        -r $RuntimeId `
        -p:PublishSingleFile=false `
        -p:Version=$Version `
        -o "publish-$PlatformName"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed for $PlatformName!" -ForegroundColor Red
        exit 1
    }

    # Step 4: Package with Velopack
    Write-Host "`n[4/5] Creating $PlatformName installer package..." -ForegroundColor Yellow

    # Use platform name as channel to ensure unique filenames
    $Channel = $PlatformName

    if ($VpkTarget -eq "windows") {
        # Windows - use normal pack command when on Windows, or [win] directive for cross-compilation
        if ($IsWindows -or ($PSVersionTable.Platform -eq $null)) {
            vpk pack `
                --packId "NetKeyer" `
                --packVersion $Version `
                --packDir "publish-$PlatformName" `
                --mainExe $MainExe `
                --packTitle "NetKeyer" `
                --packAuthors "NetKeyer Contributors" `
                --runtime $RuntimeId `
                --channel $Channel `
                --outputDir "Releases/$PlatformName"
        } else {
            # Cross-compile from Linux/Mac
            vpk "[win]" pack `
                --packId "NetKeyer" `
                --packVersion $Version `
                --packDir "publish-$PlatformName" `
                --mainExe $MainExe `
                --packTitle "NetKeyer" `
                --packAuthors "NetKeyer Contributors" `
                --runtime $RuntimeId `
                --channel $Channel `
                --outputDir "Releases/$PlatformName"
        }
    } else {
        # Linux/macOS uses regular pack command
        vpk pack `
            --packId "NetKeyer" `
            --packVersion $Version `
            --packDir "publish-$PlatformName" `
            --mainExe $MainExe `
            --packTitle "NetKeyer" `
            --packAuthors "NetKeyer Contributors" `
            --runtime $RuntimeId `
            --channel $Channel `
            --outputDir "Releases/$PlatformName"
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Packaging failed for $PlatformName!" -ForegroundColor Red
        exit 1
    }

    # Step 5: Display results for this platform
    Write-Host "`n=== $PlatformName Build Complete ===" -ForegroundColor Green
    Write-Host "Installer files located in: Releases/$PlatformName" -ForegroundColor Cyan
    Write-Host "`nGenerated files:" -ForegroundColor Yellow
    Get-ChildItem "Releases/$PlatformName" -File -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  - $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor White
    }
}

# Build based on platform selection
switch ($Platform) {
    "windows" {
        Build-Platform -PlatformName "win-x64" -RuntimeId "win-x64" -VpkTarget "windows" -MainExe "NetKeyer.exe"
    }
    "linux" {
        Build-Platform -PlatformName "linux-x64" -RuntimeId "linux-x64" -VpkTarget "linux" -MainExe "NetKeyer"
        Build-Platform -PlatformName "linux-arm64" -RuntimeId "linux-arm64" -VpkTarget "linux" -MainExe "NetKeyer"
    }
    "macos" {
        Write-Host "`nNote: macOS builds must be created on a Mac due to code signing requirements." -ForegroundColor Red
        Write-Host "To build for macOS, run this script on a Mac with Xcode installed." -ForegroundColor Yellow
        Build-Platform -PlatformName "osx-x64" -RuntimeId "osx-x64" -VpkTarget "macos" -MainExe "NetKeyer"
        Build-Platform -PlatformName "osx-arm64" -RuntimeId "osx-arm64" -VpkTarget "macos" -MainExe "NetKeyer"
    }
    "all" {
        Build-Platform -PlatformName "win-x64" -RuntimeId "win-x64" -VpkTarget "windows" -MainExe "NetKeyer.exe"
        Build-Platform -PlatformName "linux-x64" -RuntimeId "linux-x64" -VpkTarget "linux" -MainExe "NetKeyer"
        Build-Platform -PlatformName "linux-arm64" -RuntimeId "linux-arm64" -VpkTarget "linux" -MainExe "NetKeyer"
        Write-Host "`nNote: macOS builds must be created on a Mac (see GitHub Actions workflow)." -ForegroundColor Yellow
    }
}

Write-Host "`n=== All Builds Complete ===" -ForegroundColor Green
Write-Host "`nTo distribute:" -ForegroundColor Yellow
Write-Host "  1. Upload all files in Releases/* to your hosting service" -ForegroundColor White
Write-Host "  2. Windows: NetKeyer-$Version-Setup.exe" -ForegroundColor White
Write-Host "  3. Linux: NetKeyer-$Version.AppImage (x64/arm64)" -ForegroundColor White
Write-Host "  4. macOS: NetKeyer-$Version.dmg (x64/arm64)" -ForegroundColor White
