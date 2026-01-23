# Morse Code ML Training - Results Summary

**Generated:** January 22, 2026

## ✅ Files Generated

### Training Data
- `morse_training_data.csv` - 5,000 synthetic Morse timing samples
- `generate_morse_training_data.py` - Data generation script

### Trained Models
- `morse_dense_model.onnx` - Simple feedforward neural network (12.5 KB)
- `morse_lstm_model.onnx` - Sequence-based LSTM model (125 KB)

### Training Analytics
- `dense_model_training.png` - Dense model accuracy/loss curves
- `lstm_model_training.png` - LSTM model accuracy/loss curves

### Scripts
- `train_morse_model.py` - Complete training pipeline

---

## 📊 Model Performance

### Dense Model (Baseline)
- **Test Accuracy:** 70.60%
- **Architecture:** 4-layer feedforward network
- **Input:** Single pulse duration (normalized)

**Per-Class Performance:**
- Dit: 0% (confused with ElementSpace)
- Dah: 100% ✓
- ElementSpace: 100% ✓
- WordSpace: 100% ✓

**Issue:** The model cannot distinguish Dits from ElementSpaces because they have identical durations (~100ms) in standard Morse code. This is the fundamental limitation of single-pulse classification.

### LSTM Model (Sequence-based)
- **Test Accuracy:** 31.52%
- **Architecture:** 2-layer LSTM with dense head
- **Input:** Sequence of 10 consecutive pulses

**Per-Class Performance:**
- Dit: 0% (overfitted to Dah)
- Dah: 98.71%
- ElementSpace: 1.74%
- WordSpace: 0%

**Issue:** The LSTM model is underfitting and needs more training data and/or architecture improvements.

---

## 🔍 Key Insights

### Why Single-Pulse Classification is Limited

In standard Morse code timing:
- **Dit duration:** ~100ms
- **Dah duration:** ~300ms (3x Dit)
- **Element Space:** ~100ms (same as Dit!)
- **Word Space:** ~700ms (7x Dit)

**The Problem:** Dits and ElementSpaces are indistinguishable by duration alone. They can only be separated by context:
- A Dit is followed by a Dah or another Dit (within a character)
- An ElementSpace separates elements within a character

This is why context-aware models (LSTM/RNN) are essential for accurate Morse decoding!

### Why the LSTM Underperformed

1. **Insufficient Training Data:** 5,000 samples may not be enough for LSTM to learn sequence patterns
2. **Class Imbalance:** Only ~10% of samples are WordSpaces
3. **Training Data Issue:** The synthetic data generates individual pulses, not realistic Morse character sequences
4. **Architecture:** May need deeper LSTM or attention mechanisms

---

## 🚀 Next Steps for Improvement

### Option 1: Generate Better Training Data (Recommended)

Instead of random individual pulses, generate complete Morse **characters** and **words**:

```python
# Encode actual text to Morse sequences
text = "THE QUICK BROWN FOX"
morse_sequence = encode_to_morse(text)  # e.g., "- .... ."
timings = morse_to_timings(morse_sequence, base_dit=100, jitter=0.08)
```

This would create realistic sequences where:
- Dits and Dahs appear in proper character patterns
- Spaces naturally separate elements and words
- The LSTM can learn character-level patterns

### Option 2: Add More Features

Instead of just duration, include:
- **Previous duration ratio:** `current_duration / previous_duration`
- **Running average:** Rolling average of last 5 durations
- **Duration delta:** `current_duration - previous_duration`

These additional features help the model understand context even in single-pulse mode.

### Option 3: Increase Training Data Size

Generate 50,000+ samples to give the LSTM more examples to learn from.

---

## 🔧 Integration with CWMonitor.cs

### Prerequisites

Add NuGet package:
```bash
dotnet add package Microsoft.ML.OnnxRuntime
```

### Scaler Parameters (for normalization)

**Important:** You must normalize input data the same way it was trained:

```csharp
// From training:
float mean = 223.1473f;
float std = 189.6006f;

float NormalizeInput(float durationMs)
{
    return (durationMs - mean) / std;
}
```

### Simple Integration Example (Dense Model)

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

public class MorseNeuralClassifier
{
    private InferenceSession _session;
    private const float MEAN = 223.1473f;
    private const float STD = 189.6006f;
    
    public MorseNeuralClassifier(string modelPath)
    {
        _session = new InferenceSession(modelPath);
    }
    
    public MorseElementType Classify(float durationMs)
    {
        // Normalize input
        float normalized = (durationMs - MEAN) / STD;
        
        // Create input tensor
        var inputTensor = new DenseTensor<float>(new[] { 1, 1 });
        inputTensor[0, 0] = normalized;
        
        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor)
        };
        
        using var results = _session.Run(inputs);
        var output = results.First().AsEnumerable<float>().ToArray();
        
        // Get predicted class (0=Dit, 1=Dah, 2=ElementSpace, 3=WordSpace)
        int predictedClass = output.ToList().IndexOf(output.Max());
        
        return (MorseElementType)predictedClass;
    }
}
```

### Advanced Integration (LSTM Model)

For the LSTM model, you need to maintain a sliding window of the last 10 timings:

```csharp
private Queue<float> _timingWindow = new Queue<float>(10);

public MorseElementType ClassifySequence(float durationMs)
{
    // Add new timing
    _timingWindow.Enqueue((durationMs - MEAN) / STD);
    
    // Keep only last 10
    if (_timingWindow.Count > 10)
        _timingWindow.Dequeue();
    
    // Need full window for LSTM
    if (_timingWindow.Count < 10)
        return MorseElementType.Unknown;
    
    // Create 3D input tensor: [batch=1, sequence=10, features=1]
    var inputTensor = new DenseTensor<float>(new[] { 1, 10, 1 });
    int i = 0;
    foreach (var timing in _timingWindow)
    {
        inputTensor[0, i++, 0] = timing;
    }
    
    // Run inference (same as above)
    // ...
}
```

---

## 🎯 Recommendations

### For Immediate Use:
**Use the Dense Model** (`morse_dense_model.onnx`) as a starting point. While it has limitations, it correctly identifies Dahs and WordSpaces with 100% accuracy.

### For Production Quality:
1. **Regenerate training data** with realistic character sequences
2. **Retrain LSTM** with 50,000+ samples
3. **Add ensemble approach:** Combine model output with statistical heuristics
4. **Consider hybrid:** Use NN for initial classification, then apply timing ratio checks

### For Best Performance:
Implement a **two-stage classifier**:
1. **Stage 1 (NN):** Classify as "short" (Dit/Space) vs "long" (Dah/WordSpace)
2. **Stage 2 (Logic):** Use context and previous elements to disambiguate

---

## 📈 Training Curves Analysis

### Dense Model
- Converged after 29 epochs (early stopping)
- Validation accuracy: 70.87%
- No overfitting (train/val curves aligned)
- **Conclusion:** Model learned what it could, but data limitation prevents better accuracy

### LSTM Model
- Converged after 19 epochs
- Poor performance indicates underfitting
- **Conclusion:** Needs better training data or architecture changes

---

## 📚 References

- ML Strategy: `cw-monitor-ml-strategy.md`
- Migration Plan: `cw-monitor-ml-migration-plan.md`
- Training Template: `cw-monitor-ml-training-data-template.md`
- Original Code: `Services/CWMonitor.cs`
