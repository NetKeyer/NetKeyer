cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release

New-Item -Force -ItemType Directory "windows-x64"
Copy-Item "build\Release\netkeyer_midi_shim.dll" "windows-x64\"

Write-Host "Native shim built and copied to windows-x64\"
