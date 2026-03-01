#!/bin/bash
set -e

PLATFORM=$(uname -s | tr '[:upper:]' '[:lower:]')
ARCH=$(uname -m)
DEST="./${PLATFORM}-${ARCH}"

cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release -j$(nproc 2>/dev/null || sysctl -n hw.ncpu)

mkdir -p "$DEST"
cp build/libnetkeyer_midi_shim.* "$DEST/"

echo "Native shim built and copied to $DEST"
