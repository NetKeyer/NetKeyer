# CW Monitor Dense Neural Network - Diagnostic Recommendations

**Date:** 2026-01-24  
**Context:** NetKeyer CW Monitor using morse_dense_model_v3.onnx (87.35% accuracy)

---

## Executive Summary

The current CW Monitor diagnostics are designed for the statistical timing algorithm and don't provide adequate visibility into the Dense Neural Network (DNN) classifier that's now the default mode. This document recommends enhanced diagnostics appropriate for DNN-based classification.

---

## Current Diagnostics (Statistical-Focused)

### What's Currently Displayed:
```
Diagnostics
• Dit: [60] ms
• Dah: [180] ms  
• Measured: [20] WPM
Timing samples collected: [45]
```

### Issues:
1. ❌ No indication of which algorithm is active (DNN vs Statistical)
2. ❌ No neural network confidence scores
3. ❌ No visibility into DNN classification decisions
4. ❌ No indication when fallback to statistical timing occurs
5. ❌ No model performance metrics
6. ❌ Users don't know if DNN loaded successfully

---

## Recommended Diagnostics for Dense Neural Network

### Priority 1: Essential DNN Diagnostics

#### A. Algorithm Mode Indicator (Always Visible)
**Location:** Top of diagnostics section

```
Mode: [Dense Neural Network v3] ✓     [Switch to Statistical]
```

**Implementation:**
- Display current algorithm mode prominently
- Show model version (v3) and checkmark if loaded successfully
- Provide button/link to switch between DNN and Statistical modes
- If DNN failed to load, show: `Mode: [Statistical Timing] (DNN unavailable)`

**Code Changes Needed:**
```csharp
// In CWMonitor.cs - expose model version
public string ModelVersion => _neuralNetworkAvailable ? "v3" : "N/A";

// In MainWindowViewModel.cs - add properties
public string AlgorithmModeDisplay => _keyingController?.CWMonitor?.AlgorithmMode.ToString() ?? "Unknown";
public bool IsNeuralNetworkLoaded => _keyingController?.CWMonitor?.IsNeuralNetworkAvailable ?? false;
public string NeuralNetworkStatus => IsNeuralNetworkLoaded ? "✓" : "✗";
```

---

#### B. Real-Time Classification Display
**Location:** Below mode indicator, updates on each element classified

```
Last Classification:
  Element: [Dah (180ms)] → Confidence: 94.2%
  Method: Neural Network
```

**Alternative compact format:**
```
Last: Dah (180ms) @ 94% conf [NN]
```

**Implementation:**
- Show the last classified element (Dit, Dah, ElementSpace, LetterSpace, WordSpace)
- Display duration in milliseconds
- Show confidence percentage from softmax output
- Indicate method: "[NN]" for neural network, "[ST]" for statistical timing fallback
- Color-code by confidence: Green (>90%), Yellow (80-90%), Red (<80% = fallback)

**Code Changes Needed:**
```csharp
// In CWMonitor.cs - add tracking properties
public class ClassificationInfo
{
    public string ElementType { get; set; }  // "Dit", "Dah", "ElementSpace", etc.
    public int DurationMs { get; set; }
    public float Confidence { get; set; }  // 0.0 - 1.0
    public string Method { get; set; }  // "Neural Network" or "Statistical Timing"
}

private ClassificationInfo _lastClassification;
public ClassificationInfo LastClassification 
{
    get => _lastClassification;
    private set
    {
        _lastClassification = value;
        OnPropertyChanged(nameof(LastClassification));
    }
}

// Update ClassifyKeyDownHybrid and ClassifyKeyUpHybrid to populate this
```

---

#### C. Confidence Statistics
**Location:** Below classification display

```
Confidence Stats (last 20 classifications):
  High (>90%): 18    Medium (80-90%): 1    Low (<80%): 1
  Fallback Rate: 5%
```

**Implementation:**
- Track rolling window of last 20-50 classifications
- Count how many fall into each confidence band
- Calculate fallback rate (% using statistical timing)
- Update in real-time as user keys

**Code Changes Needed:**
```csharp
// In CWMonitor.cs
private Queue<float> _recentConfidences = new Queue<float>(50);
private const int ConfidenceWindowSize = 20;

public int HighConfidenceCount => _recentConfidences.Count(c => c >= 0.90f);
public int MediumConfidenceCount => _recentConfidences.Count(c => c >= 0.80f && c < 0.90f);
public int LowConfidenceCount => _recentConfidences.Count(c => c < 0.80f);
public float FallbackRate => _recentConfidences.Any() ? 
    (float)LowConfidenceCount / _recentConfidences.Count * 100f : 0f;
```

---

#### D. Model Information (Collapsible/Tooltip)
**Location:** Info icon or expandable section

```
ℹ Model Details:
  Version: v3 (Dense Neural Network)
  Architecture: 6-layer Dense (128→64→32→16→5)
  Training Accuracy: 87.35%
  Classes: 5 (Dit, Dah, ElementSpace, LetterSpace, WordSpace)
  Confidence Threshold: 80%
```

**Implementation:**
- Show on hover/click of info icon
- Static information about the model
- Helps users understand what they're seeing

---

### Priority 2: Advanced DNN Diagnostics (Optional)

#### E. Per-Class Confidence Breakdown
**Location:** Expandable "Advanced" section

```
Classification Probabilities:
  Dit:          92.3% ◼◼◼◼◼◼◼◼◼▢
  Dah:           5.1% ◼▢▢▢▢▢▢▢▢▢
  ElementSpace:  1.8% ▢▢▢▢▢▢▢▢▢▢
  LetterSpace:   0.6% ▢▢▢▢▢▢▢▢▢▢
  WordSpace:     0.2% ▢▢▢▢▢▢▢▢▢▢
```

**Implementation:**
- Show all 5 class probabilities from softmax output
- Visual bar graphs for quick scanning
- Helps users understand model confidence distribution
- Useful for debugging edge cases

---

#### F. Timing Validation
**Location:** Advanced diagnostics section

```
Timing Validation (last 20 classifications):
  Dit:Dah ratio:    2.98 (expected: 3.00) ✓
  Dit:Letter ratio: 3.12 (expected: 3.00) ✓
  Dit:Word ratio:   7.23 (expected: 7.00) ✓
```

**Implementation:**
- Calculate actual timing ratios from classified elements
- Compare to International Morse Code standards (1:3:7)
- Validate that classifications match expected patterns
- Helps identify if model is working correctly on real-world input

---

#### G. Confusion Matrix (Real-Time)
**Location:** Debug/Advanced section

```
Confusion Summary (Dit vs Dah only):
  True Dit:  45    Misclassified as Dah: 2  (95.7% accuracy)
  True Dah:  38    Misclassified as Dit: 1  (97.4% accuracy)
```

**Implementation:**
- Track ground truth vs predictions
- Requires knowing actual WPM setting
- Calculate expected element types based on timing
- Compare DNN output to expected classifications
- **Note:** This is complex and may not be practical for real-time use

---

### Priority 3: User Experience Enhancements

#### H. Algorithm Mode Switcher
**Location:** CW Monitor section

```
☐ Enable CW Monitor    [Algorithm: DNN v3 ▼]
```

**Implementation:**
- Dropdown to select algorithm mode
- Options: "Dense Neural Network" | "Statistical Timing"
- Persists to user settings
- Live switching without reconnection

**Code Changes Needed:**
```csharp
// In MainWindowViewModel.cs
public ObservableCollection<string> AlgorithmModes { get; } = new()
{
    "Dense Neural Network",
    "Statistical Timing"
};

[ObservableProperty]
private string _selectedAlgorithmMode = "Dense Neural Network";

partial void OnSelectedAlgorithmModeChanged(string value)
{
    if (_keyingController?.CWMonitor != null)
    {
        var mode = value == "Dense Neural Network" ? 
            CWAlgorithmMode.DenseNeuralNetwork : 
            CWAlgorithmMode.StatisticalTiming;
        _keyingController.CWMonitor.AlgorithmMode = mode;
        
        // Save to settings
        _settings.CwAlgorithmMode = mode.ToString();
        _settings.Save();
    }
}
```

---

#### I. Performance Indicator
**Location:** Visual indicator next to mode display

```
Mode: Dense Neural Network v3 ● [Good]
```

**Color Legend:**
- 🟢 Green (Good): Fallback rate <10%, high confidence >80%
- 🟡 Yellow (Fair): Fallback rate 10-30%, mixed confidence
- 🔴 Red (Poor): Fallback rate >30%, frequent low confidence

**Implementation:**
- Aggregate metric based on recent classifications
- Helps users quickly assess if DNN is working well
- May indicate need for more training samples or mode switch

---

#### J. Diagnostic Logging Toggle
**Location:** Advanced settings

```
☐ Enable Detailed DNN Logging
```

**Implementation:**
- Optional verbose logging to debug log
- Logs every classification with full details:
  - Timestamp
  - Duration
  - All 5 class probabilities
  - Chosen class
  - Confidence
  - Method (NN vs fallback)
- Useful for troubleshooting and analysis
- Already partially implemented via DebugLogger

---

## Recommended Implementation Plan

### Phase 1: Essential Visibility (MVP)
**Goal:** Users can see DNN is active and working

1. **Add Algorithm Mode Indicator** (Priority 1A)
   - Shows current mode and model version
   - Indicates if DNN loaded successfully

2. **Add Real-Time Classification Display** (Priority 1B)
   - Shows last classified element with confidence
   - Color-coded by confidence level
   - Indicates when fallback occurs

3. **Add Mode Switcher** (Priority 3H)
   - Dropdown to select DNN vs Statistical
   - Persists to settings

**Estimated Effort:** 4-6 hours
**User Benefit:** High - immediate visibility into what's happening

---

### Phase 2: Performance Monitoring
**Goal:** Users can assess DNN performance

4. **Add Confidence Statistics** (Priority 1C)
   - Rolling window of recent confidence scores
   - Fallback rate calculation
   - Visual performance indicator (Priority 3I)

5. **Add Model Information Tooltip** (Priority 1D)
   - Static info about model architecture
   - Training accuracy and parameters

**Estimated Effort:** 3-4 hours
**User Benefit:** Medium - helps users understand and trust the system

---

### Phase 3: Advanced Diagnostics (Optional)
**Goal:** Power users can debug and validate

6. **Add Per-Class Confidence Breakdown** (Priority 2E)
   - Expandable advanced section
   - Shows all 5 class probabilities

7. **Add Timing Validation** (Priority 2F)
   - Validate 1:3:7 ratios in real-time
   - Compare to expected standards

8. **Add Detailed Logging Toggle** (Priority 3J)
   - Optional verbose diagnostic logging
   - For troubleshooting edge cases

**Estimated Effort:** 6-8 hours
**User Benefit:** Low-Medium - mainly for power users and debugging

---

## Mock-Up: Recommended UI Layout

### Compact View (Default)
```
┌─────────────────────────────────────────────────────┐
│ CW Monitor                            ☑ Enable      │
│                                                      │
│ [THIS IS A CW TEXT]                                  │
│                                                      │
│ ┌────────────────────────────────────────────────┐  │
│ │ Diagnostics                                    │  │
│ │ Mode: Dense Neural Network v3 ✓ [Switch] ℹ    │  │
│ │                                                │  │
│ │ Last: Dah (180ms) @ 94% conf [NN] 🟢         │  │
│ │                                                │  │
│ │ • Dit: 60 ms  • Dah: 180 ms  • Measured: 20 WPM│  │
│ │ Samples: 45  |  Confidence: High=18 Med=1 Low=1│  │
│ │ Fallback Rate: 5%                              │  │
│ └────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
```

### Expanded View (Advanced Diagnostics)
```
┌─────────────────────────────────────────────────────┐
│ CW Monitor                            ☑ Enable      │
│                                                      │
│ [THIS IS A CW TEXT]                                  │
│                                                      │
│ ┌────────────────────────────────────────────────┐  │
│ │ Diagnostics                           [▼ Show]  │  │
│ │ Mode: Dense Neural Network v3 ✓ [Switch] ℹ    │  │
│ │                                                │  │
│ │ Last: Dah (180ms) @ 94% conf [NN] 🟢         │  │
│ │                                                │  │
│ │ Classification Probabilities:                  │  │
│ │   Dit:          92.3% ◼◼◼◼◼◼◼◼◼▢              │  │
│ │   Dah:           5.1% ◼▢▢▢▢▢▢▢▢▢              │  │
│ │   ElementSpace:  1.8% ▢▢▢▢▢▢▢▢▢▢              │  │
│ │   LetterSpace:   0.6% ▢▢▢▢▢▢▢▢▢▢              │  │
│ │   WordSpace:     0.2% ▢▢▢▢▢▢▢▢▢▢              │  │
│ │                                                │  │
│ │ Timing Validation (last 20):                   │  │
│ │   Dit:Dah ratio:    2.98 (expected: 3.00) ✓   │  │
│ │   Dit:Letter ratio: 3.12 (expected: 3.00) ✓   │  │
│ │   Dit:Word ratio:   7.23 (expected: 7.00) ✓   │  │
│ │                                                │  │
│ │ • Dit: 60 ms  • Dah: 180 ms  • Measured: 20 WPM│  │
│ │ Samples: 45  |  High=18 Med=1 Low=1           │  │
│ │ Fallback Rate: 5%                              │  │
│ └────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
```

---

## Technical Implementation Notes

### 1. New Properties in CWMonitor.cs

```csharp
// Classification tracking
public class ClassificationInfo : INotifyPropertyChanged
{
    public string ElementType { get; set; }
    public int DurationMs { get; set; }
    public float Confidence { get; set; }
    public string Method { get; set; }
    public DateTime Timestamp { get; set; }
    
    // Add INotifyPropertyChanged implementation
}

private ClassificationInfo _lastClassification;
public ClassificationInfo LastClassification { get; private set; }

// Confidence statistics
private Queue<float> _recentConfidences = new Queue<float>();
private const int ConfidenceWindowSize = 20;

public int HighConfidenceCount { get; private set; }
public int MediumConfidenceCount { get; private set; }
public int LowConfidenceCount { get; private set; }
public float FallbackRate { get; private set; }

// Model information
public string ModelVersion => "v3";
public string ModelArchitecture => "6-layer Dense (128→64→32→16→5)";
public float ModelAccuracy => 0.8735f;
```

### 2. Update Classification Methods

Modify `ClassifyKeyDownHybrid` and `ClassifyKeyUpHybrid` to:
1. Create ClassificationInfo object with full details
2. Update LastClassification property
3. Add confidence to rolling window
4. Recalculate statistics
5. Raise PropertyChanged events

```csharp
private ElementClassifier.KeyElement ClassifyKeyDownHybrid(int durationMs, KeyingStatistics stats)
{
    // Existing logic...
    
    if (stats.IsValid && _algorithmMode == CWAlgorithmMode.DenseNeuralNetwork)
    {
        try
        {
            var (prediction, probabilities) = _neuralClassifier.Classify(durationMs, isKeyDown: true);
            float confidence = probabilities[(int)prediction];
            
            // NEW: Track classification
            UpdateClassificationInfo(
                elementType: prediction.ToString(),
                durationMs: durationMs,
                confidence: confidence,
                method: confidence >= NeuralNetworkConfidenceThreshold ? 
                    "Neural Network" : "Statistical Timing"
            );
            
            if (confidence >= NeuralNetworkConfidenceThreshold)
            {
                var nnResult = ConvertNeuralToKeyElement(prediction);
                return nnResult;
            }
        }
        catch (Exception ex)
        {
            // Fallback...
        }
    }
    
    // Fallback classification...
}

private void UpdateClassificationInfo(string elementType, int durationMs, 
    float confidence, string method)
{
    LastClassification = new ClassificationInfo
    {
        ElementType = elementType,
        DurationMs = durationMs,
        Confidence = confidence,
        Method = method,
        Timestamp = DateTime.UtcNow
    };
    
    // Update confidence window
    _recentConfidences.Enqueue(confidence);
    if (_recentConfidences.Count > ConfidenceWindowSize)
        _recentConfidences.Dequeue();
    
    // Recalculate statistics
    UpdateConfidenceStatistics();
    
    OnPropertyChanged(nameof(LastClassification));
}

private void UpdateConfidenceStatistics()
{
    HighConfidenceCount = _recentConfidences.Count(c => c >= 0.90f);
    MediumConfidenceCount = _recentConfidences.Count(c => c >= 0.80f && c < 0.90f);
    LowConfidenceCount = _recentConfidences.Count(c => c < 0.80f);
    FallbackRate = _recentConfidences.Any() ? 
        (float)LowConfidenceCount / _recentConfidences.Count * 100f : 0f;
    
    OnPropertyChanged(nameof(HighConfidenceCount));
    OnPropertyChanged(nameof(MediumConfidenceCount));
    OnPropertyChanged(nameof(LowConfidenceCount));
    OnPropertyChanged(nameof(FallbackRate));
}
```

### 3. UI Bindings in MainWindowViewModel.cs

```csharp
// Algorithm mode display
public string AlgorithmModeDisplay => 
    _keyingController?.CWMonitor?.AlgorithmMode == CWAlgorithmMode.DenseNeuralNetwork ?
    "Dense Neural Network v3" : "Statistical Timing";

public bool IsDnnLoaded => 
    _keyingController?.CWMonitor?.IsNeuralNetworkAvailable ?? false;

// Classification display
public string LastClassificationDisplay
{
    get
    {
        var info = _keyingController?.CWMonitor?.LastClassification;
        if (info == null) return "Waiting...";
        
        string methodTag = info.Method == "Neural Network" ? "NN" : "ST";
        return $"{info.ElementType} ({info.DurationMs}ms) @ {info.Confidence:P0} conf [{methodTag}]";
    }
}

public IBrush LastClassificationColor
{
    get
    {
        var confidence = _keyingController?.CWMonitor?.LastClassification?.Confidence ?? 0f;
        if (confidence >= 0.90f) return Brushes.LimeGreen;
        if (confidence >= 0.80f) return Brushes.Yellow;
        return Brushes.Red;
    }
}

// Confidence statistics
public int HighConfidenceCount => 
    _keyingController?.CWMonitor?.HighConfidenceCount ?? 0;
public int MediumConfidenceCount => 
    _keyingController?.CWMonitor?.MediumConfidenceCount ?? 0;
public int LowConfidenceCount => 
    _keyingController?.CWMonitor?.LowConfidenceCount ?? 0;
public string FallbackRateDisplay => 
    $"{_keyingController?.CWMonitor?.FallbackRate ?? 0f:F1}%";
```

### 4. XAML Updates in MainWindow.axaml

Replace the existing diagnostics section (lines 341-373) with enhanced version:

```xml
<!-- Enhanced Diagnostics Section -->
<Border BorderBrush="LightGray" BorderThickness="0,1,0,0" Padding="0,6,0,0" Margin="0,4,0,0"
        IsVisible="{Binding CwMonitorEnabled}">
    <StackPanel Spacing="4">
        <!-- Header Row -->
        <StackPanel Orientation="Horizontal" Spacing="6" HorizontalAlignment="Center">
            <TextBlock Text="Diagnostics" FontSize="10" FontWeight="SemiBold" 
                       Foreground="Gray" VerticalAlignment="Center"/>
            <TextBlock Text="•" FontSize="10" Foreground="LightGray" VerticalAlignment="Center"/>
            <TextBlock Text="Mode:" FontSize="10" Foreground="Gray" VerticalAlignment="Center"/>
            <TextBlock Text="{Binding AlgorithmModeDisplay}" FontSize="10" 
                       FontWeight="Bold" VerticalAlignment="Center"/>
            <TextBlock Text="{Binding NeuralNetworkStatus}" FontSize="10" 
                       Foreground="LimeGreen" VerticalAlignment="Center"
                       IsVisible="{Binding IsDnnLoaded}"/>
        </StackPanel>
        
        <!-- Last Classification Row -->
        <Border Background="#F0F0F0" Padding="4" CornerRadius="2" Margin="0,2">
            <TextBlock Text="{Binding LastClassificationDisplay}" 
                       FontSize="10" FontFamily="Consolas,Courier New,monospace"
                       Foreground="{Binding LastClassificationColor}"
                       HorizontalAlignment="Center"/>
        </Border>
        
        <!-- Confidence Statistics Row -->
        <StackPanel Orientation="Horizontal" Spacing="10" HorizontalAlignment="Center">
            <TextBlock FontSize="9" Foreground="Gray">
                <Run Text="High: "/>
                <Run Text="{Binding HighConfidenceCount}" FontWeight="Bold"/>
            </TextBlock>
            <TextBlock FontSize="9" Foreground="Gray">
                <Run Text="Med: "/>
                <Run Text="{Binding MediumConfidenceCount}" FontWeight="Bold"/>
            </TextBlock>
            <TextBlock FontSize="9" Foreground="Gray">
                <Run Text="Low: "/>
                <Run Text="{Binding LowConfidenceCount}" FontWeight="Bold"/>
            </TextBlock>
            <TextBlock Text="•" FontSize="9" Foreground="LightGray"/>
            <TextBlock FontSize="9" Foreground="Gray">
                <Run Text="Fallback: "/>
                <Run Text="{Binding FallbackRateDisplay}" FontWeight="Bold"/>
            </TextBlock>
        </StackPanel>
        
        <!-- Traditional Stats Row (kept for compatibility) -->
        <StackPanel Orientation="Horizontal" Spacing="15" HorizontalAlignment="Center">
            <StackPanel Orientation="Horizontal" Spacing="4">
                <TextBlock Text="Dit:" FontSize="11" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding DitLength}" FontSize="11" FontWeight="Bold" VerticalAlignment="Center"/>
                <TextBlock Text="ms" FontSize="11" VerticalAlignment="Center"/>
            </StackPanel>
            <TextBlock Text="•" FontSize="11" Foreground="LightGray" VerticalAlignment="Center"/>
            <StackPanel Orientation="Horizontal" Spacing="4">
                <TextBlock Text="Dah:" FontSize="11" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding DahLength}" FontSize="11" FontWeight="Bold" VerticalAlignment="Center"/>
                <TextBlock Text="ms" FontSize="11" VerticalAlignment="Center"/>
            </StackPanel>
            <TextBlock Text="•" FontSize="11" Foreground="LightGray" VerticalAlignment="Center"/>
            <StackPanel Orientation="Horizontal" Spacing="4">
                <TextBlock Text="Measured:" FontSize="11" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding CalculatedWpm}" FontSize="11" FontWeight="Bold" VerticalAlignment="Center"/>
                <TextBlock Text="WPM" FontSize="11" VerticalAlignment="Center"/>
            </StackPanel>
        </StackPanel>
        
        <!-- Sample Count Row -->
        <StackPanel Orientation="Horizontal" Spacing="4" HorizontalAlignment="Center">
            <TextBlock Text="Timing samples collected:" FontSize="10" Foreground="Gray" VerticalAlignment="Center"/>
            <TextBlock Text="{Binding SampleCount}" FontSize="10" FontWeight="Bold" Foreground="Gray" VerticalAlignment="Center"/>
        </StackPanel>
    </StackPanel>
</Border>
```

---

## Benefits Summary

### For End Users:
1. **Transparency**: See exactly what algorithm is running
2. **Trust**: Confidence scores show how reliable classifications are
3. **Feedback**: Performance indicators show if system is working well
4. **Control**: Easy switching between DNN and statistical modes
5. **Troubleshooting**: Clear indication when fallback occurs

### For Developers/Power Users:
1. **Debugging**: Detailed per-class probabilities
2. **Validation**: Timing ratio checks confirm correct operation
3. **Analysis**: Fallback rate indicates model performance
4. **Optimization**: Identify edge cases for future training

### For Support:
1. **Diagnosis**: Users can report specific confidence/fallback issues
2. **Verification**: Confirm DNN loaded and operating correctly
3. **Education**: Model info tooltip explains what users are seeing

---

## Conclusion

The recommended diagnostics transform the CW Monitor from a "black box" neural network into a transparent, understandable system. Users gain confidence through:

1. **Visibility** - Know which algorithm is active
2. **Feedback** - See real-time classification confidence
3. **Performance** - Track fallback rates and accuracy
4. **Control** - Switch modes and understand model behavior

**Recommended Minimum Implementation:** Phase 1 (Priority 1A, 1B, 3H)  
**Estimated Effort:** 4-6 hours  
**Impact:** High - Users immediately understand and trust the system

---

## References

- Dense Model V3 Performance: 87.35% accuracy (RESULTS-V3-SUMMARY.md)
- Confidence Threshold: 80% (line 326, CWMonitor.cs)
- Timing Standards: 1:3:7 (Dit:Letter:Word) from morse-timings.mdc
- Classes: 5 (Dit, Dah, ElementSpace, LetterSpace, WordSpace)
