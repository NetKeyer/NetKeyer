# Bug Fix: CW Speed Stuck at 30 WPM

## Problem Description

When using NetKeyer with FlexRadio SmartSDR, CW speed settings would get stuck at 30 WPM despite user adjustments. The speed slider could be moved, but when the user made any changes in SmartSDR (like changing frequency), the speed would snap back to 30 WPM even within the same NetKeyer session.

## Root Cause

When the user makes changes in SmartSDR (like changing frequency), SmartSDR reloads profile settings and broadcasts property change events via FlexLib's Radio object. These PropertyChanged events for `CWSpeed`, `CWPitch`, and `TXCWMonitorGain` contain values from the radio's saved profile (typically 30 WPM). Even worse, SmartSDR actually changes the radio's internal values to match the profile, overriding any temporary changes made by NetKeyer.

## Solution

Implemented a two-part solution:

### 1. Persist User Preferences Across Sessions
Store the user's CW speed, pitch, and sidetone volume preferences in the UserSettings file. When connecting to a radio, push these saved preferences TO the radio instead of loading FROM it. This ensures the user's preferences persist across sessions and take precedence over radio profile defaults.

### 2. Active Enforcement During Session
When SmartSDR tries to override user settings during an active session, actively push the user's value back to the radio. This uses a 30-second enforcement window that detects conflicting radio updates and immediately re-applies the user's desired value.

## Implementation Details

### Modified `Models/UserSettings.cs`:
- Added persistent storage fields: `CwSpeed`, `CwPitch`, `SidetoneVolume` (nullable ints)
- These values are saved to the settings.json file whenever the user changes them

### Modified `ViewModels/MainWindowViewModel.cs`:
1. **Load saved preferences on startup**: In constructor, load saved CW settings from UserSettings
2. **Save on every change**: In `OnCwSpeedChanged()`, `OnCwPitchChanged()`, `OnSidetoneVolumeChanged()` - save to UserSettings immediately
3. **Push to radio on connect**: Call `ApplyUserSettingsToRadio()` instead of `ApplyInitialSettingsFromRadio()` to push user preferences to radio

### Modified `Services/RadioSettingsSynchronizer.cs`:
1. **Added new method** `ApplyUserSettingsToRadio()`: Pushes user's saved preferences to the radio and sets up enforcement tracking
2. **Enhanced conflict detection**: In `Radio_PropertyChanged()`, when a conflicting value is detected within the 30-second window:
   - Push the user's desired value back to the radio immediately
   - Don't propagate the conflicting value to the UI
3. **Enforcement window**: Still uses 30-second window for enforcement, allowing eventual synchronization if user changes settings in SmartSDR after the window expires

## Files Changed

- `Models/UserSettings.cs`
  - Added `CwSpeed`, `CwPitch`, `SidetoneVolume` properties for persistence

- `ViewModels/MainWindowViewModel.cs`
  - Load saved CW settings in constructor
  - Save settings to file in `OnCwSpeedChanged()`, `OnCwPitchChanged()`, `OnSidetoneVolumeChanged()`
  - Call `ApplyUserSettingsToRadio()` on connection instead of `ApplyInitialSettingsFromRadio()`

- `Services/RadioSettingsSynchronizer.cs`
  - Added `ApplyUserSettingsToRadio()` method to push user preferences to radio
  - Modified `Radio_PropertyChanged()` to actively push back user values when conflicts detected
  - Enhanced enforcement logic to re-apply settings, not just ignore events

## Testing Scenario

1. Connect to radio via SmartSDR ✓
2. Change WPM from 30 to 15 in NetKeyer ✓
3. Change frequency in SmartSDR (triggers profile reload) ✓
4. Press paddle key in NetKeyer ✓
5. Speed remains at 15 (previously snapped back to 30) ✓
6. Close NetKeyer ✓
7. Reopen NetKeyer and reconnect ✓
8. Speed is still 15 (loaded from saved preferences) ✓

## Notes

- The 30-second enforcement window provides active protection during typical operating sessions
- User preferences are now the source of truth, not the radio profile
- Settings persist across NetKeyer sessions
- If the user intentionally changes settings in SmartSDR after 30 seconds, NetKeyer will respect those changes
- Backwards compatible with existing FlexLib integration
