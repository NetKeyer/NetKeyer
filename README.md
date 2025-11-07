# NetKeyer - FlexRadio CW Keyer

A cross-platform GUI application for CW (Morse code) keying with FlexRadio devices using serial port input.

## Features

- **Cross-Platform**: Runs on Linux and Windows using Avalonia UI
- **Radio Discovery**: Automatic discovery of FlexRadio devices on the network
- **Serial Port Support**: Cross-platform serial port enumeration and selection
- **CW Controls**:
  - Speed adjustment (5-60 WPM)
  - Sidetone volume control (0-100)
  - Pitch control (300-1000 Hz)
  - Iambic vs. Straight Key mode selection
- **Real-time Connection Status**: Visual feedback with color-coded status indicators

## Requirements

- .NET 8.0 SDK
- FlexRadio device on the network
- Serial port device (for CW keying input)

## Building

```bash
cd NetKeyer
dotnet build
```

## Running

```bash
cd NetKeyer
dotnet run
```

## Usage

1. **Select Radio**: Click "Refresh" to discover FlexRadio devices, then select one from the dropdown
2. **Connect**: Click "Connect" to establish connection with the selected radio
3. **Select Serial Port**: Choose the serial port connected to your keyer/paddle
4. **Adjust Settings**: Use the sliders to adjust CW speed, sidetone volume, and pitch
5. **Choose Mode**: Select between Iambic or Straight Key mode

## Project Structure

```
NetKeyer/
├── Views/                  # XAML UI layouts
├── ViewModels/             # Application logic and data binding
├── FlexLib/                # Modified FlexRadio API library
├── Util/                   # Utility classes
├── Vita/                   # VITA packet handling
└── Flex.UiWpfFramework/    # Minimal MVVM framework stub
```

## Implementation Notes

### Current Status
- ✅ Radio discovery and connection
- ✅ Serial port discovery and selection
- ✅ CW settings UI (speed, volume, pitch, mode)
- ✅ Real-time settings updates
- ⚠️ CW keying implementation (monitoring CTS/DSR and calling CWPTT) - **TO BE IMPLEMENTED**

### Next Steps
1. Monitor serial port CTS/DSR lines for key state changes
2. Call `radio.CWPTT(state, timestamp, guiClientHandle)` when key state changes
3. Implement sidetone volume control (map to actual FlexLib property)
4. Implement Iambic/Straight Key mode switching
5. Add settings persistence (save/restore user preferences)

### Cross-Platform Modifications
The FlexLib API was modified to remove WPF dependencies:
- Created minimal `Flex.UiWpfFramework` with just MVVM classes
- Removed WPF-specific code (System.Windows references)
- Changed from multi-targeting to .NET 8.0 only
- Commented out non-essential TypeConverter attributes

## License

FlexLib components are Copyright © 2018-2024 FlexRadio Systems. All rights reserved.
