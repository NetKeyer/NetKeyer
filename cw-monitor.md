# CW Monitor Feature Documentation

## Overview

The **CW Monitor** is an integrated feature in NetKeyer that monitors your keying and decodes Morse code in real-time. It displays the characters you're sending and provides diagnostic information about your keying timing and speed.

## Features

### Real-Time CW Decoding
- **Automatic decoding** of Morse code patterns into readable text
- **Adaptive learning** that automatically adjusts to your keying speed
- **Bimodal timing analysis** that distinguishes between dits and dahs
- **Rolling buffer** showing the last 120 characters sent

### Diagnostic Statistics
- **Dit Length** - Measured duration of dit elements (in milliseconds)
- **Dah Length** - Measured duration of dah elements (in milliseconds)
- **Measured WPM** - Your actual keying speed calculated from timing data
- **Sample Count** - Number of timing measurements collected for accuracy confidence

### User Controls
- **Enable/Disable checkbox** - Turn the CW Monitor on or off
- **Reset Stats button** - Clear learned timing data and restart analysis
- **Persistent settings** - Your enable/disable preference is saved between sessions

## How It Works

### Adaptive Timing Analysis

The CW Monitor uses a sophisticated histogram-based approach to learn your keying characteristics:

1. **Sampling**: Monitors key-down and key-up events every 1 millisecond
2. **Histogram Building**: Groups timing measurements into buckets
3. **Mode Detection**: Identifies the two most common durations (dit and dah)
4. **Classification**: Uses learned timing to decode subsequent elements
5. **Character Assembly**: Combines dits and dahs into letters and numbers

### WPM Calculation

The Measured WPM is calculated using the **PARIS standard**:
- The word "PARIS" represents exactly 50 dit units
- At 1 WPM, "PARIS" takes exactly 60 seconds
- **Formula**: `WPM = 1200 / ditLength` (where ditLength is in milliseconds)

Example:
- Dit length of 60 ms → 1200 ÷ 60 = **20 WPM**
- Dit length of 40 ms → 1200 ÷ 40 = **30 WPM**

### Supported Modes

The CW Monitor works with both keying modes:
- ✅ **Iambic Mode** (Mode A and Mode B)
- ✅ **Straight Key Mode**

## User Interface

### Location
The CW Monitor section is located on the **Operating Page** (visible after connecting to a radio or starting sidetone-only mode).

### Display Components

```
┌─────────────────────────────────────────────────┐
│ CW Monitor  [✓] Enable  [Reset Stats]          │
├─────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────┐ │
│ │ cq cq de w1abc                              │ │ ← Decoded text
│ └─────────────────────────────────────────────┘ │
│                                                 │
│ ──────────── Diagnostics ────────────          │
│ Dit: 60 ms • Dah: 180 ms • Measured: 20 WPM   │ ← Timing stats
│ Timing samples collected: 145                  │ ← Confidence
└─────────────────────────────────────────────────┘
```

### Controls

#### Enable Checkbox
- **Checked**: CW Monitor is active and decoding
- **Unchecked**: CW Monitor is disabled (saves CPU resources)
- Setting is automatically saved and restored on next launch
- **Default**: CW Monitor starts disabled (unchecked) when first launched

#### Reset Stats Button
- **Purpose**: Clear all learned timing data
- **When to use**:
  - Changing to a significantly different keying speed
  - Statistics appear inaccurate or inconsistent
  - Starting a new operating session
- **Effect**: 
  - Clears histogram data
  - Resets all displayed statistics to 0
  - Monitor begins learning timing from scratch

## Decoded Character Map

The CW Monitor recognizes standard Morse code:

### Letters
- A-Z: Standard International Morse Code

### Numbers
- 0-9: Standard Morse numerals

### Punctuation & Prosigns
- `.` (period) - `.-.-.-`
- `,` (comma) - `--..--`
- `/` (slash) - `-..-`
- `?` (question) - `..--..`
- **AR** (end of message) - `.-.-`
- **BK** (break) - `-...-.-`
- **BT** (pause) - `-...-`
- **KN** (specific station) - `-.--`
- **SK** (end of contact) - `...-.-`

### Unknown Patterns
If a pattern isn't recognized, it's displayed as: `#(pattern)`
- Example: `#(.--..)` indicates an unrecognized sequence

## Usage Tips

### Getting Started

1. **Connect to your radio** or start sidetone-only mode
2. **Navigate to Operating Page**
3. **Ensure CW Monitor is enabled** (checkbox is checked)
4. **Start keying** - after 10-12 elements, decoding begins

### Interpreting Statistics

#### Sample Count Guidance
| Sample Count | Confidence Level | Action |
|--------------|------------------|--------|
| 0-20 | Not Ready | Keep keying, learning in progress |
| 20-50 | Low Confidence | Statistics may not be stable yet |
| 50-100 | Medium Confidence | Reasonable accuracy expected |
| 100+ | High Confidence | Statistics should be accurate |

#### Dit/Dah Ratio
- **Expected ratio**: Dah should be ~3× dit length
- **Example**: If dit = 60 ms, dah should be ~180 ms
- **Deviation**: Large variations may indicate inconsistent keying

#### Measured WPM Accuracy
- **Comparison**: Compare with your radio's CW speed setting
- **Typical variance**: ±1-2 WPM is normal
- **Large differences**: May indicate:
  - Not enough samples collected yet
  - Very inconsistent keying timing
  - Need to reset statistics

### Best Practices

#### For Accurate Measurements
1. **Send at least 20-30 characters** before trusting statistics
2. **Maintain consistent speed** during measurement period
3. **Use proper keying technique** (smooth, rhythmic)
4. **Reset stats** when changing speeds significantly

#### For Best Decoding
1. **Proper spacing**: 
   - Inter-element: ~1 dit length
   - Letter space: ~3 dit lengths  
   - Word space: ~7 dit lengths
2. **Consistent timing**: Try to maintain steady dit/dah lengths
3. **Clean keying**: Avoid bounce or hesitation

### Troubleshooting

#### Problem: No characters appear
**Possible Causes:**
- CW Monitor is disabled (checkbox unchecked)
- Not enough samples collected yet (< 10 elements)
- Check that you're actually keying (paddle indicators should show activity)

**Solution:**
- Ensure checkbox is checked
- Keep keying to build up samples
- Verify input device is connected and working

#### Problem: Wrong characters decoded
**Possible Causes:**
- Insufficient timing data (low sample count)
- Inconsistent keying timing
- Spacing issues between elements/letters

**Solution:**
- Check sample count (should be > 50 for reliability)
- Click "Reset Stats" and start fresh
- Focus on consistent, rhythmic keying
- Check dit/dah ratio (should be ~1:3)

#### Problem: Statistics show 0 or seem frozen
**Possible Causes:**
- CW Monitor was just reset
- Not keying or no key-down events detected
- CW Monitor is disabled

**Solution:**
- Start keying to generate new samples
- Verify CW Monitor is enabled
- Check that your input device is working

#### Problem: Measured WPM very different from radio setting
**Possible Causes:**
- Keying at different speed than radio setting
- Insufficient samples for accurate measurement
- Very inconsistent timing

**Solution:**
- Increase sample count (key more characters)
- Reset stats and try again
- Practice maintaining consistent speed
- Remember: Measured WPM reflects YOUR actual keying, not the radio's setting

## Technical Details

### Architecture

```
┌──────────────────┐
│ Keying Events    │ (Key Down/Up from paddles or straight key)
└────────┬─────────┘
         ↓
┌──────────────────┐
│ KeyingController │ (Intercepts all CW key events)
└────────┬─────────┘
         ↓
┌──────────────────┐
│ CWMonitor        │ (Background thread, 1ms sampling)
│ - OnKeyDown()    │
│ - OnKeyUp()      │
│ - Read() loop    │
└────────┬─────────┘
         ↓
┌──────────────────┐
│ Histogram        │ (Bimodal distribution analysis)
│ Analysis         │
└────────┬─────────┘
         ↓
┌──────────────────┐
│ Character        │ (Pattern → Letter lookup)
│ Decoding         │
└────────┬─────────┘
         ↓
┌──────────────────┐
│ UI Display       │ (PropertyChanged notifications)
└──────────────────┘
```

### Performance

- **CPU Usage**: Minimal (background thread with 1ms sleep)
- **Memory**: Small fixed footprint (~few KB for histogram data)
- **Thread Safety**: Uses cancellation tokens for clean shutdown
- **Automatic Reset**: Statistics auto-reset every 30 minutes to prevent stale data

### Settings Persistence

Settings are stored in platform-specific locations:
- **Windows**: `%APPDATA%\NetKeyer\settings.json`
- **Linux/macOS**: `~/.config/NetKeyer/settings.json`

Persisted setting:
```json
{
  "CwMonitorEnabled": false
}
```

Note: The default value is `false` (disabled). Once you enable or disable the monitor, your preference is saved.

### Debug Logging

To enable detailed logging for troubleshooting:

**Windows PowerShell:**
```powershell
$env:NETKEYER_DEBUG = "cwmonitor"
.\NetKeyer.exe
```

**Linux/macOS:**
```bash
export NETKEYER_DEBUG=cwmonitor
./NetKeyer
```

**Log Output Includes:**
- CW Monitor start/stop events
- Statistics reset notifications
- Enable/disable state changes
- Timing classification issues
- Concurrency warnings

**Log Location:**
- **Windows**: `%APPDATA%\NetKeyer\debug.log`
- **Linux/macOS**: `~/.config/NetKeyer/debug.log`

## Integration History

The CW Monitor was originally developed for the **RemoteKeyerInterface** project and was successfully integrated into NetKeyer with the following adaptations:

### Changes from Original
1. **Namespace**: Changed from `RemoteKeyerInterface` to `NetKeyer.Services`
2. **Logging**: Adapted to use NetKeyer's `DebugLogger` with category-based filtering
3. **Integration**: Hooked into `KeyingController` to capture both iambic and straight key events
4. **UI**: Added Avalonia MVVM bindings with observable properties
5. **Settings**: Integrated with NetKeyer's persistent user settings system

### File Location
- `Services/CWMonitor.cs` - Core CW Monitor implementation
- `Services/KeyingController.cs` - Integration and event routing
- `ViewModels/MainWindowViewModel.cs` - UI bindings and statistics
- `Views/MainWindow.axaml` - User interface components

## Future Enhancements (Potential)

Ideas for future development:
- [ ] Confidence indicator for each decoded character
- [ ] Historical WPM graph over time
- [ ] Export/save decoded text to file
- [ ] Audio tone feedback for decoded characters
- [ ] Advanced statistics (timing variance, error rate)
- [ ] Configurable spacing thresholds
- [ ] Support for additional prosigns and special characters
- [ ] "Learning mode" indicator showing when enough samples collected

## Credits

**Original Implementation**: RemoteKeyerInterface project  
**NetKeyer Integration**: 2026  
**Algorithm**: Adaptive bimodal histogram analysis for automatic speed detection

## Related Documentation

- [Main README](README.md) - NetKeyer overview
- [INSTALLER.md](INSTALLER.md) - Installation instructions
- [Keying System Documentation](Keying/) - Iambic keyer details

---

**Last Updated**: January 2026  
**Version**: NetKeyer with integrated CW Monitor
