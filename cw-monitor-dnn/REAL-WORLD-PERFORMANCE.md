# Real-World Performance Analysis

## Summary

**Finding:** The DNN (Dense Neural Network) model performs **worse** than the STAT (Statistical bimodal) model on real-world Morse code input, despite achieving 100% accuracy on synthetic test data.

**Recommendation:** Use **STAT mode** (statistical bimodal analyzer) for production use. DNN is available for experimentation but defaults to STAT.

---

## Why DNN Underperforms

### 1. **Synthetic Training Data**
The model was trained on perfectly generated synthetic Morse code:
- Fixed timing ratios (1:3 for dit:dah)
- Gaussian jitter added artificially
- No real human fist characteristics
- No real-world noise or artifacts

### 2. **Real-World Complexities Not Modeled**
Real Morse code has characteristics not captured in training:
- **Variable timing ratios:** Human operators don't maintain perfect 1:3 ratios
- **Speed changes:** Operators speed up/slow down mid-transmission
- **Fatigue effects:** Timing drifts over longer sessions
- **Individual fist:** Each operator has unique timing characteristics
- **Equipment variations:** Different keys, paddles, and rigs behave differently

### 3. **Insufficient Features**
The model only uses 2 features:
- Normalized duration
- Key state (up/down)

Missing important contextual features:
- Recent timing history
- Running average of current operator's speed
- Variance in recent timings
- Character context
- Statistical confidence from bimodal peaks

---

## Performance Comparison

| Aspect | STAT Mode | DNN Mode |
|--------|-----------|----------|
| **Accuracy** | Good | Poor |
| **Adaptability** | Learns operator's timing naturally | Fixed to training data patterns |
| **Speed changes** | Adapts within ~10 elements | Struggles with timing variations |
| **Robustness** | Handles wide timing variations | Brittle to non-standard timing |
| **Inference time** | < 0.1ms | < 1ms |
| **Simplicity** | Simple bimodal clustering | Complex neural network |

---

## Why STAT Mode Works Better

### Adaptive Learning
The statistical model:
1. **Collects timing samples** from actual operator
2. **Finds bimodal peaks** (dit cluster and dah cluster)
3. **Adapts to operator's specific timing** without assumptions
4. **Continuously updates** as more data arrives

### Robustness
- Works with any dit:dah ratio (not just 1:3)
- Handles speed changes gracefully
- Tolerates wide jitter and variations
- No assumptions about "perfect" Morse code

---

## Potential DNN Improvements (Future Work)

To make DNN competitive or better than STAT:

### 1. **Train on Real Data**
Collect real Morse timing data from:
- Multiple operators with different skill levels
- Various speeds (5-60 WPM)
- Different keyer types (bug, paddle, straight key)
- Long sessions showing fatigue and drift

### 2. **Add Contextual Features**
Expand from 2 features to 10+ features:
- Current element duration
- Key state
- Previous 3-5 element durations
- Running average timing
- Standard deviation of recent timings
- Element count in current session
- Time since session start
- Ratio to running average

### 3. **Use Sequence Model (LSTM/Transformer)**
Instead of classifying each element independently:
- Use LSTM or Transformer architecture
- Input: sequence of recent timings
- Output: classification for entire sequence
- Captures temporal patterns and context

### 4. **Hybrid Architecture**
Combine DNN with statistical insights:
- Feed bimodal peak locations as features
- Use statistical confidence scores
- Let DNN learn when to trust stats vs. patterns

### 5. **Online Learning**
Implement model that:
- Fine-tunes during operation
- Adapts to current operator
- Updates weights based on bimodal feedback
- Requires minimal retraining

---

## Recommendations

### For Production Use
✅ **Use STAT mode** - It's proven, adaptive, and works reliably

### For Experimentation
- DNN mode is available via toggle button for testing
- Useful for collecting data to understand failure modes
- Can help identify which timing patterns confuse the model

### For Model Improvement
If you want to improve the DNN:

1. **Collect Real Data:**
   ```csharp
   // Add data collection to CWMonitor.cs
   private void LogTimingData(int durationMs, bool isKeyDown, string classified)
   {
       File.AppendAllText("real_morse_timings.csv", 
           $"{DateTime.UtcNow:O},{durationMs},{isKeyDown},{classified}\n");
   }
   ```

2. **Label with STAT predictions:**
   - Use STAT mode as "ground truth"
   - Collect thousands of real timing samples
   - Retrain DNN on this real data

3. **Test iteratively:**
   - Retrain model with real data
   - Deploy new model file
   - Compare DNN vs STAT accuracy
   - Iterate until DNN matches or exceeds STAT

---

## Data Collection Template

To collect real data for retraining:

```csv
timestamp,duration_ms,is_key_down,classified_element,operator_id,session_id
2026-01-23T10:30:00.000Z,95,true,Dit,chris,session_001
2026-01-23T10:30:00.095Z,105,false,ElementSpace,chris,session_001
2026-01-23T10:30:00.200Z,280,true,Dah,chris,session_001
...
```

Collect:
- ✅ At least 10,000 real timing samples
- ✅ Multiple operators (3-5 minimum)
- ✅ Various speeds (10-30 WPM recommended)
- ✅ Different session conditions (fresh vs. tired)
- ✅ Label using STAT mode classifications

---

## Conclusion

The DNN underperformance is expected and common in ML projects:
- **Training data mismatch:** Synthetic ≠ Real
- **Insufficient features:** 2 features too simple
- **No context:** Single-element classification is naive

**STAT mode remains the production choice** until DNN is retrained on real data with richer features.

The toggle button allows users to:
- ✅ Use proven STAT mode for operation
- ✅ Test DNN mode for comparison
- ✅ Easily switch between algorithms
- ✅ Collect data to understand failure modes

---

**Status:** DNN integration complete but not recommended for production use. STAT mode is default and recommended.
