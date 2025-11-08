#!/bin/bash
# Build script for creating NetKeyer cross-platform installers with Velopack
# Usage: ./build-installer.sh <version> [platform]
# Examples:
#   ./build-installer.sh 1.0.0           # Build all platforms
#   ./build-installer.sh 1.0.0 windows   # Build Windows only
#   ./build-installer.sh 1.0.0 linux     # Build Linux only

set -e

PATH=~/.dotnet/tools:$PATH

VERSION="${1:-1.0.0}"
PLATFORM="${2:-all}"

echo -e "\033[1;36mBuilding NetKeyer installer(s) v$VERSION\033[0m"

# Step 1: Install vpk tool if not already installed
echo -e "\n\033[1;33m[1/5] Checking vpk tool...\033[0m"
if ! dotnet tool list -g | grep -q "vpk"; then
    echo "Installing vpk tool..."
    dotnet tool install -g vpk
fi

# Step 2: Clean previous builds
echo -e "\n\033[1;33m[2/5] Cleaning previous builds...\033[0m"
dotnet clean -c Release
rm -rf publish-* Releases

# Function to build for a specific platform
build_platform() {
    local platform_name=$1
    local runtime_id=$2
    local vpk_target=$3
    local main_exe=$4

    echo -e "\n\033[1;34m=== Building for $platform_name ($runtime_id) ===\033[0m"

    # Step 3: Publish the application
    echo -e "\n\033[1;33m[3/5] Publishing $platform_name application...\033[0m"
    dotnet publish NetKeyer.csproj \
        -c Release \
        --self-contained true \
        -r "$runtime_id" \
        -p:PublishSingleFile=false \
        -p:Version="$VERSION" \
        -o "publish-$platform_name"

    # Step 4: Package with Velopack
    echo -e "\n\033[1;33m[4/5] Creating $platform_name installer package...\033[0m"

    # Determine channel based on platform and architecture
    local channel="$platform_name"

    if [ "$vpk_target" == "windows" ]; then
        # Windows requires [win] directive for cross-compilation
        vpk "[win]" pack \
            --packId "NetKeyer" \
            --packVersion "$VERSION" \
            --packDir "publish-$platform_name" \
            --mainExe "$main_exe" \
            --packTitle "NetKeyer" \
            --packAuthors "NetKeyer Contributors" \
            --runtime "$runtime_id" \
            --channel "$channel" \
            --outputDir "Releases/$platform_name"
    else
        # Linux/macOS uses regular pack command
        vpk pack \
            --packId "NetKeyer" \
            --packVersion "$VERSION" \
            --packDir "publish-$platform_name" \
            --mainExe "$main_exe" \
            --packTitle "NetKeyer" \
            --packAuthors "NetKeyer Contributors" \
            --runtime "$runtime_id" \
            --channel "$channel" \
            --outputDir "Releases/$platform_name"
    fi

    # Step 5: Display results for this platform
    echo -e "\n\033[1;32m=== $platform_name Build Complete ===\033[0m"
    echo -e "\033[1;36mInstaller files located in: Releases/$platform_name\033[0m"
    echo -e "\n\033[1;33mGenerated files:\033[0m"
    ls -lh "Releases/$platform_name/" 2>/dev/null || echo "No files generated"
}

# Build based on platform selection
case "$PLATFORM" in
    windows)
        build_platform "win-x64" "win-x64" "windows" "NetKeyer.exe"
        ;;
    linux)
        build_platform "linux-x64" "linux-x64" "linux" "NetKeyer"
        build_platform "linux-arm64" "linux-arm64" "linux" "NetKeyer"
        ;;
    macos|osx)
        echo -e "\033[1;31mNote: macOS builds must be created on a Mac due to code signing requirements.\033[0m"
        echo -e "\033[1;33mTo build for macOS, run this script on a Mac with Xcode installed.\033[0m"
        build_platform "osx-x64" "osx-x64" "macos" "NetKeyer"
        build_platform "osx-arm64" "osx-arm64" "macos" "NetKeyer"
        ;;
    all)
        build_platform "win-x64" "win-x64" "windows" "NetKeyer.exe"
        build_platform "linux-x64" "linux-x64" "linux" "NetKeyer"
        build_platform "linux-arm64" "linux-arm64" "linux" "NetKeyer"
        echo -e "\n\033[1;33mNote: macOS builds must be created on a Mac (see GitHub Actions workflow).\033[0m"
        ;;
    *)
        echo -e "\033[1;31mUnknown platform: $PLATFORM\033[0m"
        echo "Valid options: windows, linux, macos, all"
        exit 1
        ;;
esac

echo -e "\n\033[1;32m=== All Builds Complete ===\033[0m"
echo -e "\n\033[1;33mTo distribute:\033[0m"
echo "  1. Upload all files in Releases/* to your hosting service"
echo "  2. Windows: NetKeyer-$VERSION-Setup.exe"
echo "  3. Linux: NetKeyer-$VERSION.AppImage (x64/arm64)"
echo "  4. macOS: NetKeyer-$VERSION.dmg (x64/arm64)"
