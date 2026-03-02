#!/bin/bash
set -e

# Map uname output to .NET RID components so the output directory matches
# what NetKeyer.csproj expects (linux-x64, osx-arm64, etc.).
case "$(uname -s)" in
  Linux)  PLATFORM="linux" ;;
  Darwin) PLATFORM="osx" ;;
  *)      echo "Unsupported OS: $(uname -s)" >&2; exit 1 ;;
esac

case "$(uname -m)" in
  x86_64)          ARCH="x64" ;;
  aarch64|arm64)   ARCH="arm64" ;;
  *)               echo "Unsupported arch: $(uname -m)" >&2; exit 1 ;;
esac

DEST="./${PLATFORM}-${ARCH}"

cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release -j$(nproc 2>/dev/null || sysctl -n hw.ncpu)

mkdir -p "$DEST"
cp build/libnetkeyer_midi_shim.* "$DEST/"

echo "Native shim built and copied to $DEST"
