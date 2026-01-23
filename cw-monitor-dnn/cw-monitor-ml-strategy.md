This is a structured plan to migrate the cw decode logic from a Bimodal/Statistical algorithm to a Neural Network-based approach.

Migrating from a **Bimodal/Statistical** system to a **Neural Network (NN)** is a shift from *calculating* the rules to *learning* them from examples. While the Bimodal logic in `CWMonitor` is excellent for clean signals, a Neural Network can handle "QRM" (interference), "QRN" (static), and inconsistent human "fists" that statistics alone might struggle with.

Here is a structured plan to migrate your C# logic to a Neural Network-based approach.

---

## 1. Define the Data Representation (Featurization)

In your current code, the input is `durationMs`. A Neural Network needs a more consistent "view" of the data. You have two choices for your architecture:

* **Option A: Timing-Based (RNN/LSTM):** Feed the network a sequence of durations (e.g., the last 20 timings). The network learns the relationship between them.
* **Option B: Spectrogram-Based (CNN):** Feed the network raw audio snapshots (20ms windows converted via FFT). This is more powerful because it can "see" through noise.

> **Recommendation:** Start with **Option A** as it’s a smaller leap from your current code. Instead of bucketing, you pass an array of the last  durations into the model.

---

## 2. The Training Strategy

You need a dataset. Since labeled Morse datasets are rare, you can use your existing **Bimodal logic to "auto-label" data.**

1. **Synthetic Generation:** Write a script to generate random text and convert it to Morse timings with "human-like" errors (slight randomness in dit/dah length).
2. **Collection:** Use your current `CWMonitor` to record real-world keying sessions, saving the timings and the resulting (verified) text as "Ground Truth."
3. **The "Teacher" Method:** Use `CWMonitor` as a "Teacher." If `CWMonitor` is 95% sure of a character, use that to label the raw data for the NN to learn from.

---

## 3. Proposed Neural Network Architecture

For a C# environment (using **ML.NET** or **ONNX**), a **Recurrent Neural Network (RNN)** with **LSTM** (Long Short-Term Memory) layers is ideal because Morse is a time-series problem.

| Layer | Type | Purpose |
| --- | --- | --- |
| **Input** | Input Layer | Array of  (recent timings) |
| **Feature Extraction** | LSTM | Remembers if the previous pulse was a "Dit" to help context. |
| **Classification** | Dense (Softmax) | Outputs probabilities for: `[Dit, Dah, ElementSpace, WordSpace]` |
| **Output** | Character Lookup | Map the sequence to your existing Morse Dictionary. |

---

## 4. Implementation Steps (The Migration)

### Phase 1: Data Collection (Modify `CWMonitor`)

Add a "Logging" mode to your current `TimingAnalyzer`. Every time a pulse is received, save the `durationMs` and the label your current code assigns it to a CSV file.

```csv
duration, label
120, dit
360, dah
115, dit
...

```

### Phase 2: Model Training

Use **Python (TensorFlow/PyTorch)** or **ML.NET** to train a simple classifier.

* **Goal:** Predict "Label" based on the "Duration" and the "Average Dit Length" (from your current stats).
* **Validation:** Ensure the NN outperforms the Bimodal logic on "noisy" data.

### Phase 3: Integration (The Swap)

1. **Export the Model:** Save your trained model as an `.onnx` file.
2. **Replace the Logic:** In `CWMonitor.cs`, replace the `ElementClassifier` class logic.
3. **Inference:** Instead of the `if (duration < dit * 1.5)` logic, call the ONNX model:
```csharp
var prediction = _nnModel.Predict(new { Duration = durationMs, AvgDit = currentDitAvg });
var type = prediction.Label; // "Dit" or "Dah"

```



---

## Why this is better

A Neural Network doesn't just look at one "Bucket." It looks at the **context**. If it sees a pulse that is exactly between a Dit and a Dah, the LSTM layer will look at the speed of the *previous* five characters to make an "educated guess."
