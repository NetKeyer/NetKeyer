# CW Monitor Deep Neural Network (DNN) Files

This directory contains all neural network-related files for the CW Monitor Morse code timing classifier.

## 📁 Directory Structure

### 🎯 Production Models (Ready to Use)
| File | Size | Accuracy | Status |
|------|------|----------|--------|
| `morse_dense_model_v2.onnx` | 12.8 KB | **100%** | ✅ **RECOMMENDED** |
| `morse_lstm_model_v2.onnx` | 126 KB | 24.8% | ⚠️ Not recommended yet |
| `morse_dense_model.onnx` | 12.5 KB | 70.6% | Legacy (V1) |
| `morse_lstm_model.onnx` | 125 KB | 31.5% | Legacy (V1) |

### 📊 Training Data
| File | Samples | Features | Description |
|------|---------|----------|-------------|
| `morse_training_data_v2.csv` | 10,000 | 2 | ✅ Current (duration + is_key_down) |
| `morse_sequence_data.csv` | 14,638 | 2 | Sequence-based (for LSTM improvements) |
| `morse_training_data.csv` | 5,000 | 1 | Legacy V1 (duration only) |

### 🐍 Training Scripts
| File | Purpose |
|------|---------|
| `generate_morse_training_data_v2.py` | ✅ Generate V2 training data (2 features) |
| `train_morse_model_v2.py` | ✅ Train V2 models (PyTorch) |
| `generate_morse_training_data.py` | Legacy V1 data generator |
| `train_morse_model.py` | Legacy V1 training script |

### 📈 Training Visualizations
| File | Shows |
|------|-------|
| `dense_model_v2_training_v2.png` | ✅ V2 Dense model (100% accuracy) |
| `lstm_model_v2_training_v2.png` | V2 LSTM model performance |
| `dense_model_training.png` | V1 Dense model (70.6% accuracy) |
| `lstm_model_training.png` | V1 LSTM model performance |

### 📚 Documentation

#### Results & Implementation Guides
| File | Content |
|------|---------|
| `RESULTS-V2-SUMMARY.md` | ✅ **START HERE** - Executive summary & results |
| `csharp-integration-guide.md` | ✅ Complete C# implementation guide |
| `training-results-summary.md` | V1 analysis and migration strategy |

#### Planning & Strategy Documents
| File | Content |
|------|---------|
| `cw-monitor-ml-strategy.md` | ML strategy and architecture overview |
| `cw-monitor-ml-migration-plan.md` | Migration plan from bimodal to neural network |
| `cw-monitor-ml-training-data-template.md` | Training data generation template and guidelines |

## 🚀 Quick Start

### For C# Integration (Production Use)
1. Read: `RESULTS-V2-SUMMARY.md`
2. Follow: `csharp-integration-guide.md`
3. Use model: `morse_dense_model_v2.onnx`

### For Understanding the Approach (Background Reading)
1. Strategy overview: `cw-monitor-ml-strategy.md`
2. Migration plan: `cw-monitor-ml-migration-plan.md`
3. Data generation approach: `cw-monitor-ml-training-data-template.md`

### For Retraining
1. Generate data: `python generate_morse_training_data_v2.py`
2. Train model: `python train_morse_model_v2.py`
3. Review: Check generated PNG files for training curves

## 📊 Model Performance Summary

### V2 Dense Model (Recommended) ✅
```
Test Accuracy: 100%
- Dit:          100%
- Dah:          100%
- ElementSpace: 100%
- WordSpace:    100%

Input Features: 2 (duration_ms, is_key_down)
Model Size:     12.8 KB
Inference Time: < 1ms
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

## 🔑 Key Improvements V1 → V2

| Aspect | V1 | V2 | Improvement |
|--------|----|----|-------------|
| Input Features | 1 (duration) | 2 (duration + key state) | +1 feature |
| Overall Accuracy | 70.6% | **100%** | +29.4% |
| Dit Accuracy | 0% | **100%** | +100% |
| Model Size | 12.5 KB | 12.8 KB | +0.3 KB |

**Key Insight:** Adding `is_key_down` feature resolved the Dit/ElementSpace ambiguity!

## 📦 File Sizes

```
Total Size: ~1.2 MB

Models:           ~276 KB
Training Data:    ~680 KB
Visualizations:   ~226 KB
Scripts:          ~41 KB
Documentation:    ~43 KB
```

**Total Files: 22**

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

### V2 (Current) - January 22, 2026
- ✅ Added `is_key_down` feature
- ✅ Achieved 100% accuracy
- ✅ 10,000 training samples
- ✅ Production ready

### V1 (Legacy) - January 22, 2026
- ⚠️ Single feature (duration only)
- ⚠️ 70.6% accuracy
- ⚠️ Dit/ElementSpace confusion
- ❌ Not recommended

## 🎯 Recommended Files for Production

**Essential:**
1. `morse_dense_model_v2.onnx` - The trained model
2. `csharp-integration-guide.md` - How to integrate
3. `RESULTS-V2-SUMMARY.md` - Performance details

**Optional:**
- Training scripts (for future retraining)
- Training data (for model updates)
- Visualization PNGs (for analysis)

## 📝 Notes

- All V1 files are kept for reference but V2 should be used
- LSTM models need sequence-based training for better performance
- V2 Dense model is production-ready with 100% accuracy
- Model was trained on synthetic data with realistic jitter

## 🔗 Integration

See `csharp-integration-guide.md` for:
- Complete C# implementation
- Unit tests
- Integration strategies (replace/hybrid/ensemble)
- Troubleshooting guide
- Performance considerations

## 📧 Support

For questions:
1. Check documentation in this directory
2. Review training curves (PNG files)
3. Examine training scripts for parameter details

---

**Status:** ✅ Production Ready  
**Recommended Model:** `morse_dense_model_v2.onnx`  
**Accuracy:** 100%  
**Last Updated:** January 22, 2026
