# CW Monitor Feature Documentation

## Overview

The **CW Monitor** is an integrated feature in NetKeyer that monitors your keying and decodes Morse code in real-time using a **dense neural network**. It displays the characters you're sending and provides diagnostic information about your keying timing and speed.

### Revolutionary DNN-Based Approach

This implementation uses a **production-ready dense neural network** trained on International Morse Code timing standards. Unlike traditional histogram-based approaches, the neural network:

- ✅ **87.35% accuracy** on all 5 timing classes
- ✅ Follows strict **1:3:7 timing ratios** (Dit:Letter:Word)
- ✅ Trained on **41,844 samples** with realistic human variation
- ✅ Uses proper **WPM formula**: `T_dit = 1.2 / WPM` seconds
- ✅ Distinguishes **5 timing elements**: Dit, Dah, ElementSpace, LetterSpace, WordSpace
- ✅ Lightweight **11,333 parameters** for fast inference (< 1ms)

## Features

### Real-Time CW Decoding
- **Neural network classification** of Morse timing elements
- **2-feature input**: duration (ms) + key state (down/up)
- **5-class output**: Dit, Dah, ElementSpace, LetterSpace, WordSpace
- **International timing standards** following morse-timings.mdc specification
- **Rolling buffer** showing the last 120 characters sent

### Diagnostic Statistics
- **Dit Length** - Measured duration of dit elements (in milliseconds)
- **Dah Length** - Measured duration of dah elements (in milliseconds)
- **Measured WPM** - Your actual keying speed calculated from timing data
- **Sample Count** - Number of timing measurements collected for accuracy confidence
- **Neural Network Confidence** - Prediction confidence for each classified element

### User Controls
- **Enable/Disable checkbox** - Turn the CW Monitor on or off
- **Reset Stats button** - Clear learned timing data and restart analysis
- **Persistent settings** - Your enable/disable preference is saved between sessions

## How It Works

### Dense Neural Network Architecture

The CW Monitor uses a sophisticated 6-layer dense neural network:

```
Input Layer:    2 features [normalized_duration, is_key_down]
Hidden Layer 1: 128 neurons (ReLU + 30% Dropout)
Hidden Layer 2: 64 neurons (ReLU + 30% Dropout)
Hidden Layer 3: 32 neurons (ReLU + 20% Dropout)
Hidden Layer 4: 16 neurons (ReLU)
Output Layer:   5 neurons (Softmax)
```

**Training Details:**
- Dataset: 41,844 samples (20k random + 21k sequence-based)
- Training split: 80% train / 20% test
- Epochs: 75 (early stopping with patience=20)
- WPM Range: 10-40 WPM
- Jitter: σ = 0.08 (8% human timing variation)

### Neural Network Classification Process

1. **Key Event Capture**: Monitors key-down and key-up events with 1ms precision
2. **Feature Extraction**: 
   - Duration in milliseconds
   - Key state (1 = down/signal, 0 = up/silence)
3. **Normalization**: `normalized_duration = (duration_ms - 133.4177) / 126.2867`
4. **Neural Network Inference**: Classifies into one of 5 timing elements
5. **Character Assembly**: Combines dits and dahs into letters and numbers using decoded spacing

### Timing Elements (International Standards)

| Element | Timing Units | Key State | Examples |
|---------|--------------|-----------|----------|
| **Dit** | 1 unit | Down | Short pulse (60ms @ 20 WPM) |
| **Dah** | 3 units | Down | Long pulse (180ms @ 20 WPM) |
| **ElementSpace** | 1 unit | Up | Gap between dits/dahs in letter |
| **LetterSpace** | 3 units | Up | Gap between letters |
| **WordSpace** | 7 units | Up | Gap between words |

### Model Performance

**Overall Accuracy: 87.35%**

| Class | Accuracy | Notes |
|-------|----------|-------|
| Dit | 87.01% | Minor confusion with Dah (13%) |
| Dah | 96.69% | Excellent performance |
| ElementSpace | 86.37% | Some confusion with LetterSpace |
| LetterSpace | 73.81% | Moderate confusion with WordSpace (24%) |
| WordSpace | 95.36% | Excellent performance |

**Key Insight:** LetterSpace/WordSpace confusion is expected due to the timing proximity (3u vs 7u) with human jitter. The system uses character completion context to resolve ambiguities.

### WPM Calculation

The Measured WPM is calculated using the **PARIS standard**:
- The word "PARIS" represents exactly 50 dit units
- At 1 WPM, "PARIS" takes exactly 60 seconds
- **Formula**: `WPM = 1200 / ditLength` (where ditLength is in milliseconds)
- **Derived from**: `T_dit = 1.2 / WPM` seconds

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
│ Neural Network: 87% confidence                 │ ← NN confidence
└─────────────────────────────────────────────────┘
```

### Controls

#### Enable Checkbox
- **Checked**: CW Monitor is active and decoding
- **Unchecked**: CW Monitor is disabled (saves CPU resources)
- Setting is automatically saved and restored on next launch
- **Default**: CW Monitor starts disabled (unchecked) when first launched

#### Reset Stats Button
- **Purpose**: Clear all learned timing data and restart neural network
- **When to use**:
  - Changing to a significantly different keying speed
  - Statistics appear inaccurate or inconsistent
  - Starting a new operating session
- **Effect**: 
  - Clears timing statistics
  - Resets all displayed statistics to 0
  - Monitor begins fresh classification

## Decoded Character Map

The CW Monitor recognizes standard Morse code:

### Letters
- A-Z: Standard International Morse Code

### Numbers
- 0-9: Standard Morse numerals

### Punctuation & Prosigns
- `.` (period) - `.-.-.-`
- `,` (comma) - `--..--`
- `/` (slash) - `-..-.`
- `?` (question) - `..--..`
- **AR** (end of message) - `.-.-.`
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
4. **Start keying** - the neural network classifies each element immediately

### Interpreting Statistics

#### Sample Count Guidance
| Sample Count | Confidence Level | Action |
|--------------|------------------|--------|
| 0-20 | Not Ready | Keep keying, statistics building |
| 20-50 | Low Confidence | Statistics stabilizing |
| 50-100 | Medium Confidence | Reasonable accuracy expected |
| 100+ | High Confidence | Statistics should be accurate |

#### Dit/Dah Ratio
- **Expected ratio**: Dah should be ~3× dit length (per International standards)
- **Example**: If dit = 60 ms, dah should be ~180 ms
- **Deviation**: Large variations may indicate inconsistent keying

#### Neural Network Confidence
- **High confidence (>85%)**: Neural network is certain about classification
- **Medium confidence (70-85%)**: Reasonable certainty
- **Low confidence (<70%)**: May benefit from more consistent keying

#### Measured WPM Accuracy
- **Comparison**: Compare with your radio's CW speed setting
- **Typical variance**: ±1-2 WPM is normal
- **Large differences**: May indicate:
  - Not enough samples collected yet
  - Very inconsistent keying timing
  - Speed outside training range (10-40 WPM)
  - Need to reset statistics

### Best Practices

#### For Accurate Measurements
1. **Send at least 20-30 characters** before trusting statistics
2. **Maintain consistent speed** during measurement period
3. **Use proper keying technique** (smooth, rhythmic)
4. **Reset stats** when changing speeds significantly
5. **Keep speed in training range** (10-40 WPM for optimal accuracy)

#### For Best Decoding
1. **Proper spacing**: 
   - Inter-element: ~1 dit length
   - Letter space: ~3 dit lengths  
   - Word space: ~7 dit lengths
2. **Consistent timing**: Try to maintain steady dit/dah lengths
3. **Clean keying**: Avoid bounce or hesitation
4. **Standard ratios**: The neural network is trained on International timing standards

### Troubleshooting

#### Problem: No characters appear
**Possible Causes:**
- CW Monitor is disabled (checkbox unchecked)
- Neural network model file not found
- Check that you're actually keying (paddle indicators should show activity)

**Solution:**
- Ensure checkbox is checked
- Verify `morse_dense_model_v3.onnx` exists in Models folder
- Verify input device is connected and working

#### Problem: Wrong characters decoded
**Possible Causes:**
- Keying speed outside training range (< 10 WPM or > 40 WPM)
- Inconsistent keying timing
- Spacing issues between elements/letters

**Solution:**
- Check sample count (should be > 50 for reliability)
- Click "Reset Stats" and start fresh
- Focus on consistent, rhythmic keying
- Check dit/dah ratio (should be ~1:3)
- Verify speed is in 10-40 WPM range

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
- Speed outside training range

**Solution:**
- Increase sample count (key more characters)
- Reset stats and try again
- Practice maintaining consistent speed
- Remember: Measured WPM reflects YOUR actual keying, not the radio's setting
- Stay within 10-40 WPM range for best results

#### Problem: Low neural network confidence
**Possible Causes:**
- Timing between standard classes (e.g., between 1u and 3u)
- Non-standard "fist" or keying style
- Speed outside optimal training range

**Solution:**
- Practice more consistent timing
- Stay closer to International timing ratios
- Fine-tune the model with your own keying data (see Fine-Tuning section)

## Training Your Own Model

The CW Monitor uses machine learning, which means you can create custom training data and train your own model to improve accuracy for your specific keying style.

### Prerequisites

**Python Environment:**
```bash
pip install pandas numpy torch matplotlib scikit-learn onnx
```

**Training Scripts:**
- `cw-monitor-dnn/generate_morse_training_data_v3.py` - Data generation
- `cw-monitor-dnn/train_morse_model_v3.py` - Model training

### Step 1: Generate Training Data

The data generator creates synthetic Morse timing data following International standards:

```bash
cd cw-monitor-dnn
python generate_morse_training_data_v3.py
```

**What This Creates:**
- `morse_training_data_v3.csv` - 20,000 random samples
- `morse_sequence_data_v3.csv` - 21,844 sequence-based samples  
- `morse_training_data_v3_combined.csv` - 41,844 combined samples (recommended)

**Customizing Data Generation:**

Edit `generate_morse_training_data_v3.py` to adjust parameters:

```python
# Timing constants (International standards - DO NOT CHANGE unless you know what you're doing)
DIT_UNITS = 1
DAH_UNITS = 3
ELEMENT_GAP = 1
LETTER_GAP = 3
WORD_GAP = 7

# Human jitter (increase for more variation)
JITTER_SIGMA = 0.08  # 8% variation (try 0.10 for 10%, or 0.05 for 5%)

# WPM range (adjust to your typical operating speeds)
WPM_MIN = 10  # Minimum WPM
WPM_MAX = 40  # Maximum WPM

# Sample count (more samples = better training, but slower)
NUM_SAMPLES = 20000  # Try 50000 for more robust training
NUM_CHARACTERS = 3000  # Try 5000 for more sequence data
```

**Advanced: Using Real Keying Data**

To train on your actual keying:

1. Enable debug logging (see Debug Logging section)
2. Send practice text for 5-10 minutes
3. Extract timing data from logs:
   - Look for key-down/key-up events with durations
   - Create CSV with columns: `duration_ms`, `is_key_down`, `label`
4. Manually label samples (0=Dit, 1=Dah, 2=ElementSpace, 3=LetterSpace, 4=WordSpace)
5. Append to `morse_training_data_v3_combined.csv`

### Step 2: Train the Model

Run the training script:

```bash
python train_morse_model_v3.py
```

**What This Does:**
1. Loads training data (41,844 samples)
2. Splits into 80% train / 20% test
3. Normalizes duration feature (preserves key state)
4. Trains dense neural network (150 epochs max with early stopping)
5. Evaluates on test set
6. Exports to `morse_dense_model_v3.onnx`

**Training Output:**
```
Morse Code Neural Network Training V3 (PyTorch)
============================================================
Following International Morse Code Timing Standards
  - Dit:     1 unit
  - Dah:     3 units
  - Element: 1 unit
  - Letter:  3 units
  - Word:    7 units
  - Classes: 5

Using device: cpu
Loading training data...
Dataset: morse_training_data_v3_combined.csv
Total samples: 41844
Features: duration_ms, is_key_down
Classes: 5

Training samples: 33475
Test samples: 8369

Epoch 10/150 - Loss: 0.3421, Acc: 0.8624, Val Loss: 0.3198, Val Acc: 0.8712
...
Early stopping at epoch 75

============================================================
Dense Model V3 - Evaluation Results
============================================================
Test Accuracy: 87.35%

Per-Class Accuracy:
  Dit            : 87.01%
  Dah            : 96.69%
  ElementSpace   : 86.37%
  LetterSpace    : 73.81%
  WordSpace      : 95.36%

[OK] ONNX model exported: morse_dense_model_v3.onnx
```

**Key Files Generated:**
- `morse_dense_model_v3.onnx` - Production model for C# integration
- `dense_model_v3_training_v3.png` - Training curves visualization
- Console output shows scaler parameters (mean/std for normalization)

### Step 3: Integrate Into NetKeyer

1. **Copy trained model:**
   ```bash
   cp morse_dense_model_v3.onnx ../Models/
   ```

2. **Update normalization parameters** (if you retrained):
   - Open `Services/CWMonitor.cs`
   - Update `DURATION_MEAN` and `DURATION_STD` constants
   - Values are printed at the end of training

3. **Rebuild NetKeyer:**
   ```bash
   dotnet build
   ```

4. **Test the new model:**
   - Run NetKeyer
   - Enable CW Monitor
   - Send test patterns
   - Verify accuracy

### Understanding Training Parameters

**Model Architecture (DenseModel in train_morse_model_v3.py):**
```python
nn.Sequential(
    nn.Linear(2, 128),      # Input layer: 2 features → 128 neurons
    nn.ReLU(),
    nn.Dropout(0.3),        # 30% dropout for regularization
    nn.Linear(128, 64),     # Hidden layer: 128 → 64
    nn.ReLU(),
    nn.Dropout(0.3),
    nn.Linear(64, 32),      # Hidden layer: 64 → 32
    nn.ReLU(),
    nn.Dropout(0.2),        # 20% dropout
    nn.Linear(32, 16),      # Hidden layer: 32 → 16
    nn.ReLU(),
    nn.Linear(16, 5)        # Output layer: 16 → 5 classes
)
```

**Key Training Hyperparameters:**
- **Learning rate**: 0.001 (Adam optimizer)
- **Batch size**: 64
- **Dropout**: 0.2-0.3 (prevents overfitting)
- **Early stopping**: Patience of 20 epochs
- **LR scheduling**: ReduceLROnPlateau (factor=0.5, patience=5)

**Adjusting for Your Needs:**

| Scenario | Adjustment |
|----------|------------|
| Overfitting (train >> test accuracy) | Increase dropout to 0.4-0.5 |
| Underfitting (both accuracies low) | Decrease dropout to 0.1-0.2, or add more layers |
| Slow convergence | Increase learning rate to 0.002 |
| Unstable training | Decrease learning rate to 0.0005 |
| Not enough data | Generate more samples (50k+) |
| Outside WPM range | Adjust WPM_MIN and WPM_MAX in data generator |

## Fine-Tuning for Your "Fist"

Every operator has a unique keying style ("fist"). You can fine-tune the model to better recognize your specific timing patterns.

### Collecting Your Keying Data

1. **Enable debug logging:**
   ```powershell
   # Windows
   $env:NETKEYER_DEBUG = "cwmonitor"
   .\NetKeyer.exe
   ```

2. **Send practice text** (5-10 minutes):
   - Mix of letters, numbers, prosigns
   - Consistent speed
   - Natural keying style

3. **Extract timing events** from `%APPDATA%\NetKeyer\debug.log`:
   - Look for "Key down duration: XXXms"
   - Look for "Key up duration: XXXms"
   - Record the duration and whether it's key-down or key-up

4. **Label your data:**
   - Listen to your own keying or use character context
   - Assign labels: 0=Dit, 1=Dah, 2=ElementSpace, 3=LetterSpace, 4=WordSpace

5. **Create CSV file** (`my_keying_data.csv`):
   ```csv
   duration_ms,is_key_down,label
   58.3,1,0
   62.1,0,2
   181.7,1,1
   59.4,0,2
   177.8,1,1
   183.2,0,3
   ...
   ```

### Fine-Tuning Process

**Method 1: Append to Existing Dataset (Easiest)**

```bash
cd cw-monitor-dnn

# Combine your data with existing training data
cat morse_training_data_v3_combined.csv my_keying_data.csv > combined_with_mine.csv

# Retrain with combined data
# Edit train_morse_model_v3.py, line 446:
# Change: load_and_prepare_data('morse_training_data_v3_combined.csv')
# To:     load_and_prepare_data('combined_with_mine.csv')

python train_morse_model_v3.py
```

**Method 2: Transfer Learning (Advanced)**

Load pre-trained weights and continue training on your data:

```python
# Edit train_morse_model_v3.py, add before training:

# Load pre-trained model
dense_model = DenseModel(input_dim=2, num_classes=5).to(device)
dense_model.load_state_dict(torch.load('morse_dense_model_v3.pt'))

# Freeze early layers (optional)
for param in dense_model.network[:4].parameters():
    param.requires_grad = False

# Train only on your data with lower learning rate
optimizer = optim.Adam(filter(lambda p: p.requires_grad, dense_model.parameters()), 
                       lr=0.0001)  # 10x lower learning rate
```

**Method 3: Class-Weighted Loss (For Imbalanced Data)**

If your keying has unusual class distribution:

```python
# Edit train_morse_model_v3.py, in train_model():

# Calculate class weights
class_counts = np.bincount(y_train)
class_weights = 1.0 / class_counts
class_weights = torch.FloatTensor(class_weights).to(device)

# Use weighted loss
criterion = nn.CrossEntropyLoss(weight=class_weights)
```

### Evaluating Fine-Tuned Models

After fine-tuning, compare performance:

```bash
# Original model
Original Model Accuracy: 87.35%

# Your fine-tuned model
Fine-Tuned Model Accuracy: 92.14%  # Example improvement

# Per-class improvements
Dit:          87.01% → 91.23%  (+4.22%)
LetterSpace:  73.81% → 88.45%  (+14.64%)  # Much better!
```

**When Fine-Tuning Helps:**
- ✅ You have non-standard timing ratios (e.g., Dah = 2.5× Dit instead of 3×)
- ✅ You operate at speeds outside 10-40 WPM
- ✅ You have a distinctive "fist" (irregular spacing, hesitation, etc.)
- ✅ You use straight key with variable timing

**When Fine-Tuning May Not Help:**
- ❌ Your keying already follows International standards closely
- ❌ You have too little data (< 500 samples)
- ❌ Your keying is highly inconsistent (high jitter)

### Best Practices for Fine-Tuning

1. **Collect sufficient data**: At least 1,000 labeled samples
2. **Maintain consistency**: Record at your normal operating speed
3. **Balance classes**: Try to have similar numbers of each class
4. **Validate carefully**: Test on NEW keying data, not training data
5. **Iterate**: Fine-tune multiple times with different data sessions
6. **Monitor overfitting**: Ensure test accuracy doesn't drop

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
│ Feature          │ (Extract duration_ms, is_key_down)
│ Extraction       │
└────────┬─────────┘
         ↓
┌──────────────────┐
│ Neural Network   │ (Dense model inference via ONNX Runtime)
│ Classification   │ (morse_dense_model_v3.onnx)
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

- **CPU Usage**: Minimal (< 1ms inference time per element)
- **Memory**: ~2 MB (model + ONNX runtime)
- **Model Size**: 46 KB (morse_dense_model_v3.onnx)
- **Parameters**: 11,333 (lightweight dense network)
- **Thread Safety**: Uses cancellation tokens for clean shutdown

### Neural Network Integration

The neural network is integrated via **Microsoft.ML.OnnxRuntime**:

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

public class MorseNeuralClassifierV3 : IDisposable
{
    private readonly InferenceSession _session;
    
    // V3 Normalization parameters from training
    private const float DURATION_MEAN = 133.4177f;
    private const float DURATION_STD = 126.2867f;
    
    public MorseNeuralClassifierV3(string modelPath)
    {
        _session = new InferenceSession(modelPath);
    }
    
    public (MorseElementType prediction, float[] probabilities) Classify(
        float durationMs, 
        bool isKeyDown)
    {
        // Normalize duration
        float normalizedDuration = (durationMs - DURATION_MEAN) / DURATION_STD;
        float keyState = isKeyDown ? 1.0f : 0.0f;
        
        // Create input tensor [batch_size=1, features=2]
        var inputTensor = new DenseTensor<float>(new[] { 1, 2 });
        inputTensor[0, 0] = normalizedDuration;
        inputTensor[0, 1] = keyState;
        
        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor)
        };
        
        using var results = _session.Run(inputs);
        var output = results.First().AsEnumerable<float>().ToArray();
        
        // Apply softmax and get prediction
        var probabilities = Softmax(output);
        int predictedClass = Array.IndexOf(probabilities, probabilities.Max());
        
        return ((MorseElementType)predictedClass, probabilities);
    }
}
```

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
- Neural network classification results with confidence
- Timing statistics
- Feature extraction data (duration_ms, is_key_down)
- Model loading success/failure
- Concurrency warnings

**Log Location:**
- **Windows**: `%APPDATA%\NetKeyer\debug.log`
- **Linux/macOS**: `~/.config/NetKeyer/debug.log`

## Model Versioning

### V3 (Current - Recommended)
- **Classes**: 5 (Dit, Dah, ElementSpace, LetterSpace, WordSpace)
- **Accuracy**: 87.35%
- **Dataset**: 41,844 samples
- **Standards**: Validated International timing (1:3:7 ratios)
- **Features**: 2 (normalized_duration, is_key_down)
- **File**: `morse_dense_model_v3.onnx`

### V2 (Legacy)
- **Classes**: 4 (combined ElementSpace and LetterSpace)
- **Accuracy**: ~90% (on simpler task)
- **Dataset**: 10,000 samples
- **Standards**: Approximate timing (not validated)
- **File**: `morse_dense_model_v2.onnx`

### V1 (Deprecated)
- **Classes**: 4
- **Accuracy**: 70.6%
- **Features**: 1 (duration only - insufficient)
- **File**: `morse_dense_model.onnx`

**Recommendation:** Always use V3 for production. It has the best timing accuracy and follows International standards.

## Future Enhancements (Potential)

Ideas for future development:
- [ ] Real-time confidence visualization per character
- [ ] Historical WPM graph over time
- [ ] Export/save decoded text to file
- [ ] Audio tone feedback for decoded characters
- [ ] Advanced statistics (timing variance, error rate)
- [ ] LSTM model integration for sequence-aware decoding
- [ ] Online learning (model updates from manual corrections)
- [ ] "Fist" profiling with automatic fine-tuning
- [ ] Multi-operator recognition (identify operator by keying style)
- [ ] Real-time training data collection mode

## Credits

**Dense Neural Network Implementation**: 2026  
**Training Framework**: PyTorch + ONNX Runtime  
**Timing Standards**: International Morse Code (morse-timings.mdc)  
**Algorithm**: Dense feedforward network with dropout regularization  
**Dataset**: 41,844 synthetic samples with realistic human variation

## Related Documentation

- [C# Integration Guide](cw-monitor-dnn/csharp-integration-guide.md) - Detailed ONNX integration
- [Training Results V3](cw-monitor-dnn/RESULTS-V3-SUMMARY.md) - Model performance analysis
- [Main README](README.md) - NetKeyer overview
- [INSTALLER.md](INSTALLER.md) - Installation instructions
- [Keying System Documentation](Keying/) - Iambic keyer details

## FAQ

### Q: How accurate is the neural network?
**A:** 87.35% overall accuracy, with 96.69% for Dah and 95.36% for WordSpace. LetterSpace has lower accuracy (73.81%) due to timing proximity with WordSpace, but context helps resolve ambiguities.

### Q: Can it handle different keying speeds?
**A:** Yes, the model is trained on 10-40 WPM. Outside this range, accuracy may decrease. Fine-tune with your own data for speeds < 10 or > 40 WPM.

### Q: Does it work with sloppy keying?
**A:** The model is trained with 8% human jitter, so it handles normal timing variation. Very sloppy keying may reduce accuracy. Practice consistent timing for best results.

### Q: How do I improve accuracy for my keying style?
**A:** Collect your own keying data and fine-tune the model (see Fine-Tuning section). This adapts the network to your specific "fist."

### Q: Is the neural network slow?
**A:** No, inference takes < 1ms per element. The model has only 11,333 parameters and runs efficiently on CPU.

### Q: Can I train on GPU?
**A:** Yes, the training script automatically uses CUDA if available. Change `device = 'cuda'` in `train_morse_model_v3.py`.

### Q: What if I want more than 5 classes?
**A:** Edit the training scripts to add new classes (e.g., prosigns, error handling). Update `num_classes=5` to your desired number and regenerate training data with new labels.

### Q: Does it replace the histogram-based approach?
**A:** The current V3 implementation uses a pure neural network approach. Previous versions used histogram analysis. The NN approach provides better accuracy with standardized timing.

---

**Last Updated**: January 2026  
**Version**: NetKeyer with Dense Neural Network CW Monitor V3  
**Model**: morse_dense_model_v3.onnx (87.35% accuracy)
