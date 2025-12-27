# NetKeyer - FlexRadio CW Keyer

A cross-platform GUI application for CW (Morse code) keying with FlexRadio devices, supporting both serial port and MIDI input devices.

## Features

- **Cross-Platform**: Runs on Linux, Windows, and macOS using Avalonia UI
- **Radio Discovery**: Automatic discovery of FlexRadio devices on the network
  - Local network discovery
  - SmartLink remote connection support
  - Sidetone-only practice mode (no radio required)
- **Multiple Input Device Types**:
  - Serial port (HaliKey v1/v2, custom hardware)
  - MIDI devices (HaliKey MIDI, CTR2, and other MIDI controllers)
  - Configurable MIDI note mappings for paddles, straight key, and PTT
- **CW Controls**:
  - Speed adjustment (5-60 WPM)
  - Sidetone volume control (0-100)
  - Pitch control (300-1000 Hz)
  - Iambic Mode A/B selection
  - Straight Key mode
  - Paddle swap option
- **Local Sidetone Generation**:
  - Low latency audio using platform-optimized backends
  - OpenAL for cross-platform compatibility
  - WASAPI for Windows
- **PTT Support**:
  - Supports PTT keying for non-CW modes

## Requirements

- .NET 8.0 SDK
- FlexRadio device on the network (or use sidetone-only mode for practice)
- Input device:
  - Serial port device (e.g., HaliKey v1/v2), OR
  - MIDI controller (e.g., HaliKey MIDI, CTR2)

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

### Setup Page

1. **SmartLink (Optional)**: Click "Enable SmartLink" to connect to remote radios via FlexRadio SmartLink
2. **Select Radio**:
   - Click "Refresh" to discover FlexRadio devices
   - Select a radio and GUI client station from the dropdown, OR
   - Select "No radio (sidetone only)" for practice mode
3. **Select Input Device Type**: Choose between:
   - Serial Port (HaliKey v1) - uses CTS (left) and DCD (right) pins
   - MIDI (HaliKey MIDI, CTR2) - uses configurable MIDI note mappings
4. **Choose Input Device**:
   - For Serial: Select the serial port connected to your keyer/paddle
   - For MIDI: Select the MIDI device, then optionally click "Configure MIDI Notes..." to customize mappings
5. **Connect**: Click "Connect" to begin operating

### Operating Page

1. **Monitor Paddle Status**: Visual indicators show left/right paddle state in real-time
2. **Adjust CW Settings**:
   - Speed (WPM): Controls dit/dah timing
   - Sidetone: Volume of local audio feedback
   - Pitch: Frequency of sidetone tone
3. **Select Keyer Mode**:
   - Iambic: Automatic dit/dah generation with Mode A or Mode B
   - Straight Key: Direct on/off control
4. **Swap Paddles**: Reverse left/right paddle assignment if needed
5. **Disconnect**: Return to setup page to change settings

## MIDI Configuration

The MIDI note configuration dialog allows you to assign any MIDI note (0-127) to one or more functions:
- **Left Paddle**: Generates dits in iambic mode
- **Right Paddle**: Generates dahs in iambic mode
- **Straight Key**: Direct key on/off control
- **PTT**: Push-to-talk for non-CW modes

Default mappings (compatible with HaliKey MIDI and CTR2):
- Note 20: Left Paddle + Straight Key + PTT
- Note 21: Right Paddle + Straight Key + PTT
- Note 30: Straight Key only
- Note 31: PTT only

## Troubleshooting

### Connection Issues

**Radio not found**:
- Ensure radio is on the same network
- Check firewall settings
- Try SmartLink if local discovery fails

**GUI client binding fails**:
- Radio needs SmartSDR or another GUI client running
- Wait a moment after connecting before binding

### Audio Issues

**No sidetone**:
- Check sidetone volume slider
- Verify system audio is not muted
- Check audio output device in your system mixer

**High latency**:
- Windows: Ensure WASAPI backend is being used
- Linux: Check PulseAudio/PipeWire configuration
- Adjust buffer size if needed

### Input Device Issues

**Serial port not found**:
- Check device permissions (Linux: add user to `dialout` group)
- Verify device is connected
- Click "Refresh" to rescan

**MIDI device not responding**:
- Verify MIDI device is connected and powered
- Check MIDI note mappings match your device
- Use "Configure MIDI Notes..." to adjust mappings

### Debug Logging

NetKeyer supports detailed debug logging controlled by the `NETKEYER_DEBUG` environment variable. This can help diagnose issues with specific subsystems.

**Available Debug Categories**:

| Category | Description |
|----------|-------------|
| `keyer` | Iambic keyer state machine (paddle state, element timing, mode transitions) |
| `midi` | MIDI input parsing and raw event processing |
| `input` | Input abstraction layer (paddle state changes, indicator updates) |
| `slice` | Transmit slice mode monitoring (CW vs PTT mode detection) |
| `sidetone` | Audio sidetone provider (tone/silence state machine, timing) |
| `audio` | Audio device management (initialization, enumeration, selection) |

**Usage Examples**:

```bash
# Enable all debug output
NETKEYER_DEBUG=all dotnet run

# Enable specific categories
NETKEYER_DEBUG=keyer,midi dotnet run

# Enable audio device debugging
NETKEYER_DEBUG=audio dotnet run

# Enable all MIDI-related categories using wildcard
NETKEYER_DEBUG=midi* dotnet run

# Multiple categories and wildcards
NETKEYER_DEBUG=keyer,audio,sidetone dotnet run

# No debug output (default)
dotnet run
```

**Common Debugging Scenarios**:

- **Paddle not working**: Use `NETKEYER_DEBUG=input,keyer` to see paddle state changes and keyer logic
- **MIDI issues**: Use `NETKEYER_DEBUG=midi,input` to see raw MIDI events and parsed paddle states
- **Audio problems**: Use `NETKEYER_DEBUG=audio,sidetone` to see device initialization and tone generation
- **Radio connection issues**: Use `NETKEYER_DEBUG=slice` to see transmit mode detection

---

## Developer Information

### Project Structure

```
NetKeyer/
├── Views/                  # XAML UI layouts
│   ├── MainWindow.axaml
│   └── MidiConfigDialog.axaml
├── ViewModels/             # Application logic and data binding
│   ├── MainWindowViewModel.cs
│   └── MidiConfigDialogViewModel.cs
├── Models/                 # Data models
│   ├── UserSettings.cs
│   └── MidiNoteMapping.cs
├── Audio/                  # Sidetone generation
│   ├── SidetoneGenerator.cs (OpenAL)
│   └── WasapiSidetoneGenerator.cs (Windows)
├── Midi/                   # MIDI input handling
│   └── MidiPaddleInput.cs
├── FlexLib/                # Modified FlexRadio API library
├── Util/                   # Utility classes
├── Vita/                   # VITA packet handling
└── Flex.UiWpfFramework/    # Minimal MVVM framework stub
```

### Input Device Support

**Serial Port (HaliKey v1/v2)**:
- HaliKey v1: CTS (left paddle) + DCD (right paddle)
- HaliKey v2: RI (left paddle, toggle-based) + DCD (right paddle)
- Pin state monitoring with debouncing

**MIDI Devices**:
- Supports any MIDI controller with configurable note mappings
- Note On/Off events trigger paddle/key/PTT state changes
- Mappings stored in user settings for persistence

### Iambic Keyer Implementation

- Software-based iambic keyer with Mode A and Mode B support
- Timer-based state machine for accurate dit/dah timing
- Calculates timing from WPM setting: dit_length = 1200 / WPM
- Paddle latching for Mode B alternating behavior

### Audio Sidetone

**OpenAL Backend** (Linux, macOS, Windows fallback):
- Cross-platform compatibility
- Buffer caching for minimal latency
- Pre-generated waveforms for instant response

**WASAPI Backend** (Windows preferred):
- 3-5ms latency using shared mode
- Real-time sine wave generation

### Settings Persistence

User settings are stored in:
- Linux: `~/.config/NetKeyer/settings.json`
- Windows: `%APPDATA%\NetKeyer\settings.json`
- macOS: `~/Library/Application Support/NetKeyer/settings.json`

Stored settings include:
- Selected radio (serial number and GUI client station)
- Input device type and selection
- MIDI note mappings
- SmartLink credentials (encrypted)

## License

FlexLib components are Copyright © 2018-2024 FlexRadio Systems. All rights reserved.
