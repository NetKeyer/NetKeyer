# CW Monitor Deep Neural Network (DNN) Files

This directory contains all neural network-related files for the CW Monitor Morse code timing classifier.

## 📁 Directory Structure

### 🎯 Production Models (Ready to Use)
| File | Size | Accuracy | Status |
|------|------|----------|--------|
| `morse_dense_model_v3.onnx` | 13.1 KB | **87.35%** (5 classes) | ✅ **RECOMMENDED** |
| `morse_dense_model_v2.onnx` | 12.8 KB | 100% (4 classes) | ⚠️ Legacy (missing LetterSpace) |
| `morse_lstm_model_v3.onnx` | 127 KB | 28.33% | ⚠️ Not recommended |
| `morse_lstm_model_v2.onnx` | 126 KB | 24.8% | Legacy (V2) |
| `morse_dense_model.onnx` | 12.5 KB | 70.6% | Legacy (V1) |
| `morse_lstm_model.onnx` | 125 KB | 31.5% | Legacy (V1) |

### 📊 Training Data
| File | Samples | Features | Description |
|------|---------|----------|-------------|
| `morse_training_data_v3.csv` | 20,000 | 2 | ✅ Current (duration + is_key_down, 5 classes) |
| `morse_sequence_data_v3.csv` | 21,844 | 2 | ✅ Sequence-based V3 (5 classes) |
| `morse_training_data_v3_combined.csv` | 41,844 | 2 | ✅ Combined V3 dataset (used for training) |
| `morse_training_data_v2.csv` | 10,000 | 2 | Legacy V2 (4 classes) |
| `morse_sequence_data.csv` | 14,638 | 2 | Legacy V2 sequence data |
| `morse_training_data.csv` | 5,000 | 1 | Legacy V1 (duration only) |

### 🐍 Training Scripts
| File | Purpose |
|------|---------|
| `generate_morse_training_data_v3.py` | ✅ Generate V3 training data (5 classes, validated timing) |
| `train_morse_model_v3.py` | ✅ Train V3 models (PyTorch, 5 classes) |
| `generate_morse_training_data_v2.py` | Legacy V2 data generator (4 classes) |
| `train_morse_model_v2.py` | Legacy V2 training script |
| `generate_morse_training_data.py` | Legacy V1 data generator |
| `train_morse_model.py` | Legacy V1 training script |

### 📈 Training Visualizations
| File | Shows |
|------|-------|
| `dense_model_v3_training_v3.png` | ✅ V3 Dense model (87.35% accuracy, 5 classes) |
| `lstm_model_v3_training_v3.png` | V3 LSTM model performance |
| `dense_model_v2_training_v2.png` | V2 Dense model (100% accuracy, 4 classes) |
| `lstm_model_v2_training_v2.png` | V2 LSTM model performance |
| `dense_model_training.png` | V1 Dense model (70.6% accuracy) |
| `lstm_model_training.png` | V1 LSTM model performance |

### 📚 Documentation

#### Results & Implementation Guides
| File | Content |
|------|---------|
| `RESULTS-V3-SUMMARY.md` | ✅ **START HERE** - V3 executive summary & results |
| `RESULTS-V2-SUMMARY.md` | V2 results (4 classes, legacy) |
| `csharp-integration-guide.md` | Complete C# implementation guide |
| `training-results-summary.md` | V1 analysis and migration strategy |

#### Planning & Strategy Documents
| File | Content |
|------|---------|
| `cw-monitor-ml-strategy.md` | ML strategy and architecture overview |
| `cw-monitor-ml-migration-plan.md` | Migration plan from bimodal to neural network |
| `cw-monitor-ml-training-data-template.md` | Training data generation template and guidelines |

## 🚀 Quick Start

### For C# Integration (Production Use)
1. Read: `RESULTS-V3-SUMMARY.md`
2. Follow: `csharp-integration-guide.md` (update for V3 parameters)
3. Use model: `morse_dense_model_v3.onnx`

### For Understanding the Approach (Background Reading)
1. Strategy overview: `cw-monitor-ml-strategy.md`
2. Migration plan: `cw-monitor-ml-migration-plan.md`
3. Data generation approach: `cw-monitor-ml-training-data-template.md`

### For Retraining
1. Generate data: `python generate_morse_training_data_v3.py`
2. Train model: `python train_morse_model_v3.py`
3. Review: Check generated PNG files for training curves

## 📊 Model Performance Summary

### V3 Dense Model (Recommended) ✅
```
Test Accuracy: 87.35%
- Dit:          87.01%
- Dah:          96.69%
- ElementSpace: 86.37%
- LetterSpace:  73.81%
- WordSpace:    95.36%

Classes:       5 (Dit, Dah, ElementSpace, LetterSpace, WordSpace)
Input Features: 2 (duration_ms, is_key_down)
Model Size:     13.1 KB
Inference Time: < 1ms
Training Data:  41,844 samples
Timing Validation: ✅ Compliant with morse-timings.mdc (1:3:7 ratios)
```

### V2 Dense Model (Legacy)
```
Test Accuracy: 100%
- Dit:          100%
- Dah:          100%
- ElementSpace: 100%
- WordSpace:    100%

Classes:       4 (No LetterSpace distinction)
Input Features: 2 (duration_ms, is_key_down)
Issue:         Cannot distinguish LetterSpace from WordSpace
```

### V1 Dense Model (Legacy)
```
Test Accuracy: 70.6%
- Dit:          0%    ❌ (confused with ElementSpace)
- Dah:          100%
- ElementSpace: 100%
- WordSpace:    100%

Input Features: 1 (duration_ms only)
Issue:          Cannot distinguish Dit from ElementSpace
```

## 🔑 Key Improvements V1 → V2 → V3

| Aspect | V1 | V2 | V3 | Latest Improvement |
|--------|----|----|----|--------------------|
| Input Features | 1 (duration) | 2 (duration + key state) | 2 (duration + key state) | - |
| Classes | 4 | 4 | **5** | +LetterSpace class |
| Training Samples | 5,000 | 10,000 | **41,844** | +31,844 samples |
| Overall Accuracy | 70.6% | 100% | **87.35%** | Realistic for 5 classes |
| Dit Accuracy | 0% | 100% | **87.01%** | Maintained |
| LetterSpace | N/A | N/A (merged) | **73.81%** | New distinction |
| Timing Validation | ❌ | ❌ | **✅** | 1:3:7 ratios validated |
| Model Size | 12.5 KB | 12.8 KB | **13.1 KB** | +0.3 KB |

**Key Insights:** 
- V2 added `is_key_down` feature, resolving Dit/ElementSpace ambiguity
- V3 adds LetterSpace class for proper Morse timing (3-unit vs 7-unit gaps)
- V3 validates all data against International Morse Code standards (morse-timings.mdc)

## 📦 File Sizes

```
Total Size: ~2.1 MB

Models:           ~406 KB
Training Data:    ~1,480 KB
Visualizations:   ~339 KB
Scripts:          ~68 KB
Documentation:    ~86 KB
```

**Total Files: 34**

## 🔧 Dependencies

### For Training (Python)
```bash
pip install torch scikit-learn matplotlib pandas numpy
```

### For Inference (C#)
```bash
dotnet add package Microsoft.ML.OnnxRuntime
```

## 📖 Version History

### V3 (Current) - January 24, 2026
- ✅ Added LetterSpace class (5 classes total)
- ✅ Achieved 87.35% accuracy on 5-class problem
- ✅ 41,844 training samples (combined dataset)
- ✅ Strict timing validation against morse-timings.mdc
- ✅ Proper 1:3:7 ratio compliance (Dit:Letter:Word)
- ✅ WPM formula: T_dit = 1.2 / WPM
- ✅ Production ready

### V2 (Legacy) - January 22, 2026
- ✅ Added `is_key_down` feature
- ✅ Achieved 100% accuracy (4 classes)
- ✅ 10,000 training samples
- ⚠️ No LetterSpace distinction
- ⚠️ No timing validation

### V1 (Legacy) - January 22, 2026
- ⚠️ Single feature (duration only)
- ⚠️ 70.6% accuracy
- ⚠️ Dit/ElementSpace confusion
- ❌ Not recommended

## 🎯 Recommended Files for Production

**Essential:**
1. `morse_dense_model_v3.onnx` - The trained model (5 classes)
2. `RESULTS-V3-SUMMARY.md` - Performance details and C# integration
3. `csharp-integration-guide.md` - How to integrate (needs V3 update)

**Optional:**
- Training scripts (for future retraining)
- Training data (for model updates)
- Visualization PNGs (for analysis)
- `morse-timings.mdc` (timing standards reference)

## 📝 Notes

- All V1 and V2 files are kept for reference but V3 should be used
- V3 properly implements International Morse Code timing standards
- V3 distinguishes 5 classes: Dit, Dah, ElementSpace, LetterSpace, WordSpace
- LSTM models need sequence-based training improvements for better performance
- V3 Dense model is production-ready with 87.35% accuracy on 5-class problem
- Model was trained on synthetic data with realistic jitter (σ = 0.08)
- All V3 data validated against 1:3:7 timing ratios from morse-timings.mdc

## 🔗 Integration

See `RESULTS-V3-SUMMARY.md` for:
- V3 C# implementation code
- Normalization parameters (mean: 133.4177, std: 126.2867)
- 5-class output interpretation
- Timing unit mapping

See `csharp-integration-guide.md` for:
- Complete C# implementation patterns
- Unit tests
- Integration strategies (replace/hybrid/ensemble)
- Troubleshooting guide
- Performance considerations

**Note:** Integration guide needs updating for V3 parameters and 5-class output.

## 📧 Support

For questions:
1. Check documentation in this directory
2. Review training curves (PNG files)
3. Examine training scripts for parameter details

---

**Status:** ✅ Production Ready  
**Recommended Model:** `morse_dense_model_v3.onnx`  
**Accuracy:** 87.35% (5 classes)  
**Classes:** Dit, Dah, ElementSpace, LetterSpace, WordSpace  
**Timing Standards:** ✅ Validated against morse-timings.mdc (1:3:7 ratios)  
**Last Updated:** January 24, 2026
