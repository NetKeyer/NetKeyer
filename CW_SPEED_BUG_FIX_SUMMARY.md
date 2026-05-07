# Bug Fix: CW Speed Stuck at 30 WPM

## Problem Description

When using NetKeyer with FlexRadio SmartSDR, CW speed settings would get stuck at 30 WPM despite user adjustments. The speed slider could be moved, but the instant the paddle key was pressed, the speed would snap back to 30 WPM.

## Root Cause

When the user presses the paddle key, SmartSDR reloads profile settings and broadcasts property change events via FlexLib's Radio object. These PropertyChanged events for `CWSpeed`, `CWPitch`, and `TXCWMonitorGain` would override the user's recent changes, forcing values back to stored profile defaults (typically 30 WPM).

## Solution

Implemented a 30-second enforcement window that ignores conflicting radio updates after the user changes a setting. This prevents SmartSDR profile reloads from overriding user preferences while still allowing long-term bidirectional synchronization.

### Implementation Details

Modified `Services/RadioSettingsSynchronizer.cs`:

1. **Track User Intent**: Store user's desired values and timestamp when they make changes
   - `_userDesiredCwSpeed`, `_userDesiredCwPitch`, `_userDesiredSidetoneVolume`
   - `_lastUserCwSpeedChange`, `_lastUserCwPitchChange`, `_lastUserSidetoneVolumeChange`

2. **Ignore Conflicting Updates**: In `Radio_PropertyChanged` event handler, check if:
   - User has recently changed the value (within 30 seconds)
   - Radio is broadcasting a different value than user desired
   - If both true: ignore the radio update and don't propagate to UI

3. **Allow Matching Updates**: If radio's value matches user's desired value, accept the update normally

## Files Changed

- `Services/RadioSettingsSynchronizer.cs`
  - Added user preference tracking fields
  - Modified `SyncCwSpeedToRadio()`, `SyncCwPitchToRadio()`, `SyncSidetoneVolumeToRadio()` to record user intent
  - Modified `Radio_PropertyChanged()` to filter conflicting updates

- `ViewModels/MainWindowViewModel.cs`
  - Added defensive null check after radio connection (line ~1041)
  - Prevents crash if radio disconnects unexpectedly during connection sequence

## Testing

Tested with FlexRadio SmartSDR:
1. Connect to radio ✓
2. Change WPM from 30 to 15 ✓
3. Press paddle key ✓
4. Speed remains at 15 (previously snapped back to 30) ✓

## Notes

- The 30-second window (`ENFORCE_USER_SETTING_MS = 30000`) provides enough time for typical operating sessions while still allowing eventual synchronization if the user changes settings in SmartSDR directly
- The fix applies to CW Speed, CW Pitch, and Sidetone Volume - all settings that exhibited this behavior
- No changes to external APIs or dependencies
- Backwards compatible with existing FlexLib integration
