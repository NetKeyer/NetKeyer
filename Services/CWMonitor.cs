using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetKeyer.Helpers;

namespace NetKeyer.Services
{
    /// <summary>
    /// Represents statistical information about Morse code keying patterns
    /// </summary>
    public class KeyingStatistics : INotifyPropertyChanged
    {
        private int _ditLengthMs;
        private int _dahLengthMs;
        private string _debugInfo;
        private int _sampleCount;

        public int DitLengthMs
        {
            get => _ditLengthMs;
            set
            {
                if (_ditLengthMs != value)
                {
                    _ditLengthMs = value;
                    OnPropertyChanged(nameof(DitLengthMs));
                }
            }
        }

        public int DahLengthMs
        {
            get => _dahLengthMs;
            set
            {
                if (_dahLengthMs != value)
                {
                    _dahLengthMs = value;
                    OnPropertyChanged(nameof(DahLengthMs));
                }
            }
        }

        public string DebugInfo
        {
            get => _debugInfo;
            set
            {
                if (_debugInfo != value)
                {
                    _debugInfo = value;
                    OnPropertyChanged(nameof(DebugInfo));
                }
            }
        }

        public int SampleCount
        {
            get => _sampleCount;
            set
            {
                if (_sampleCount != value)
                {
                    _sampleCount = value;
                    OnPropertyChanged(nameof(SampleCount));
                }
            }
        }

        public bool IsValid => DitLengthMs > 0 && DahLengthMs > 0 && DahLengthMs > DitLengthMs;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Morse code decoder and pattern recognition service
    /// </summary>
    public class MorseCodeDecoder
    {
        private static readonly Dictionary<string, string> MorsePatterns = new Dictionary<string, string>
        {
            // Letters
            {".-", "A"}, {"-...", "B"}, {"-.-.", "C"}, {"-..", "D"},
            {".", "E"}, {"..-.", "F"}, {"--.", "G"}, {"....", "H"},
            {"..", "I"}, {".---", "J"}, {"-.-", "K"}, {".-..", "L"},
            {"--", "M"}, {"-.", "N"}, {"---", "O"}, {".--.", "P"},
            {"--.-", "Q"}, {".-.", "R"}, {"...", "S"}, {"-", "T"},
            {"..-", "U"}, {"...-", "V"}, {".--", "W"}, {"-..-", "X"},
            {"-.--", "Y"}, {"--..", "Z"},
            
            // Numbers
            {"-----", "0"}, {".----", "1"}, {"..---", "2"}, {"...--", "3"},
            {"....-", "4"}, {".....", "5"}, {"-....", "6"}, {"--...", "7"},
            {"---..", "8"}, {"----.", "9"},
            
            // Punctuation and prosigns
            {".-.-.-", "."}, {"--..--", ","}, {"..--..", "?"},
            {"-..-.", "/"}, {".-.-.", "AR"}, {"-...-", "BT"},
            {"-...-.-", "BK"}, {"-.--.", "KN"}, {"...-.-", "SK"}
        };

        public string Decode(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return string.Empty;

            if (MorsePatterns.TryGetValue(pattern, out string character))
            {
                return character;
            }

            return $"#({pattern})";
        }
    }

    /// <summary>
    /// Analyzes timing patterns to determine dit and dah lengths using bimodal distribution
    /// </summary>
    public class TimingAnalyzer
    {
        private readonly Dictionary<int, List<int>> _timingBuckets = new Dictionary<int, List<int>>();
        private readonly object _lockObject = new object();
        private const int BucketRounding = 12;
        private const int MinSampleCount = 10;

        public void RecordTiming(int durationMs)
        {
            int bucket = CalculateBucket(durationMs);

            lock (_lockObject)
            {
                if (!_timingBuckets.ContainsKey(bucket))
                {
                    _timingBuckets[bucket] = new List<int>();
                }

                _timingBuckets[bucket].Add(durationMs);
            }
        }

        public KeyingStatistics AnalyzeStatistics(bool includeDebugInfo = false)
        {
            var stats = new KeyingStatistics();

            lock (_lockObject)
            {
                if (_timingBuckets.Count < 2)
                    return stats;

                // Find the two most populated buckets (bimodal distribution)
                var sortedBuckets = _timingBuckets
                    .Where(kvp => kvp.Value.Count >= 2)
                    .OrderByDescending(kvp => kvp.Value.Count)
                    .Take(2)
                    .ToList();

                if (sortedBuckets.Count < 2)
                    return stats;

                // Calculate average timing for each mode
                var averages = sortedBuckets
                    .Select(kvp => (int)kvp.Value.Average())
                    .OrderBy(avg => avg)
                    .ToArray();

                stats.DitLengthMs = averages[0];
                stats.DahLengthMs = averages[1];

                if (includeDebugInfo)
                {
                    stats.DebugInfo = GenerateDebugInfo();
                }
            }

            return stats;
        }

        public void Reset()
        {
            lock (_lockObject)
            {
                _timingBuckets.Clear();
            }
        }

        private int CalculateBucket(int durationMs)
        {
            // Logarithmic bucketing for better distribution
            int value = (int)Math.Pow(Math.Log(durationMs, 2), 2);
            return RoundUpToNearest(value, BucketRounding);
        }

        private int RoundUpToNearest(int value, int nearest)
        {
            if (value % nearest == 0)
                return value;

            return ((nearest - value % nearest) + value);
        }

        private string GenerateDebugInfo()
        {
            var debugParts = new List<string>();

            foreach (var kvp in _timingBuckets.OrderBy(kvp => kvp.Key))
            {
                if (kvp.Value.Count > 1)
                {
                    double average = kvp.Value.Average();
                    debugParts.Add($"Bucket {kvp.Key}: avg={average:F1}ms count={kvp.Value.Count}");
                }
            }

            return string.Join(" | ", debugParts);
        }
    }

    /// <summary>
    /// Classifies Morse code elements based on timing patterns
    /// </summary>
    public class ElementClassifier
    {
        private const float DitTolerance = 1.5f;
        private const float InterElementTolerance = 1.3f;
        private const float LetterSpaceThreshold = 5.0f;

        public enum KeyElement
        {
            Dit,
            Dah,
            Unknown
        }

        public enum SpaceElement
        {
            InterElement,
            LetterSpace,
            WordSpace,
            Unknown
        }

        public KeyElement ClassifyKeyDown(int durationMs, KeyingStatistics stats)
        {
            if (!stats.IsValid)
                return KeyElement.Unknown;

            if (durationMs <= stats.DitLengthMs * DitTolerance)
                return KeyElement.Dit;

            if (durationMs >= stats.DahLengthMs * 0.8f)
                return KeyElement.Dah;

            return KeyElement.Dah; // Default to dah for longer durations
        }

        public SpaceElement ClassifyKeyUp(int durationMs, KeyingStatistics stats)
        {
            if (!stats.IsValid)
                return SpaceElement.Unknown;

            if (durationMs <= stats.DitLengthMs * InterElementTolerance)
                return SpaceElement.InterElement;

            if (durationMs <= stats.DitLengthMs * LetterSpaceThreshold)
                return SpaceElement.LetterSpace;

            return SpaceElement.WordSpace;
        }

        public string KeyElementToString(KeyElement element)
        {
            return element switch
            {
                KeyElement.Dit => ".",
                KeyElement.Dah => "-",
                _ => "?"
            };
        }
    }

    /// <summary>
    /// Algorithm mode for CW classification
    /// </summary>
    public enum CWAlgorithmMode
    {
        /// <summary>Dense Neural Network (100% accuracy on training data)</summary>
        DNN,
        /// <summary>Statistical bimodal distribution analyzer</summary>
        STAT
    }

    /// <summary>
    /// Main CW (Morse Code) monitoring service
    /// </summary>
    public class CWMonitor : INotifyPropertyChanged, IDisposable
    {
        private readonly TimingAnalyzer _timingAnalyzer;
        private readonly ElementClassifier _classifier;
        private readonly MorseCodeDecoder _decoder;
        private readonly Timer _statsResetTimer;
        private readonly object _lockObject = new object();
        private readonly MorseNeuralClassifier _neuralClassifier;
        private readonly bool _neuralNetworkAvailable;
        private CWAlgorithmMode _algorithmMode = CWAlgorithmMode.DNN;

        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private bool _enabled;
        private volatile bool _isKeyDown;
        private string _decodedBuffer = string.Empty;
        private string _currentPattern = string.Empty;
        private int _elementCount;

        private const int BufferMaxLength = 120;
        private const int MinElementsBeforeDecoding = 10;
        private const int StatsResetIntervalMinutes = 30;
        private const float NeuralNetworkConfidenceThreshold = 0.85f;

        /// <summary>
        /// Gets or sets the algorithm mode for classification
        /// </summary>
        public CWAlgorithmMode AlgorithmMode
        {
            get => _algorithmMode;
            set
            {
                if (_algorithmMode != value)
                {
                    _algorithmMode = value;
                    DebugLogger.Log("cwmonitor", $"Algorithm mode changed to: {value}");
                    OnPropertyChanged(nameof(AlgorithmMode));
                }
            }
        }

        /// <summary>
        /// Gets whether the neural network is available
        /// </summary>
        public bool IsNeuralNetworkAvailable => _neuralNetworkAvailable;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;

                DebugLogger.Log("cwmonitor", $"Enabled changing from {_enabled} to {value}");

                if (value)
                    Start();
                else
                    Stop();
            }
        }

        public string DecodedBuffer
        {
            get
            {
                lock (_lockObject)
                {
                    return _decodedBuffer;
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _decodedBuffer = value;

                    // Trim to maximum length
                    if (_decodedBuffer.Length > BufferMaxLength)
                    {
                        _decodedBuffer = _decodedBuffer.Substring(_decodedBuffer.Length - BufferMaxLength);
                    }
                }

                DebugLogger.Log("cwmonitor", $"Buffer updated: '{value}'");
                OnPropertyChanged(nameof(DecodedBuffer));
            }
        }

        public KeyingStatistics CurrentStatistics
        {
            get
            {
                var stats = _timingAnalyzer.AnalyzeStatistics();
                stats.SampleCount = _elementCount;
                return stats;
            }
        }

        public CWMonitor()
        {
            _timingAnalyzer = new TimingAnalyzer();
            _classifier = new ElementClassifier();
            _decoder = new MorseCodeDecoder();

            // Try to load neural network model
            try
            {
                string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
                                               "Models", "morse_dense_model_v2.onnx");
                _neuralClassifier = new MorseNeuralClassifier(modelPath);
                _neuralNetworkAvailable = true;
                _algorithmMode = CWAlgorithmMode.STAT; // Default to STAT (better real-world performance)
                DebugLogger.Log("cwmonitor", "Neural network classifier loaded (DNN available but defaulting to STAT mode)");
            }
            catch (Exception ex)
            {
                _neuralNetworkAvailable = false;
                _algorithmMode = CWAlgorithmMode.STAT; // Fall back to STAT mode
                DebugLogger.Log("cwmonitor", $"Neural network not available, using STAT mode only: {ex.Message}");
            }

            _statsResetTimer = new Timer(
                callback: _ => ResetStatistics(),
                state: null,
                dueTime: TimeSpan.FromMinutes(StatsResetIntervalMinutes),
                period: TimeSpan.FromMinutes(StatsResetIntervalMinutes));

            DebugLogger.Log("cwmonitor", "CWMonitor initialized");
        }

        public void OnKeyDown()
        {
            _isKeyDown = true;
        }

        public void OnKeyUp()
        {
            _isKeyDown = false;
        }

        public void ResetStatistics()
        {
            DebugLogger.Log("cwmonitor", "Resetting statistics");
            _timingAnalyzer.Reset();
            _elementCount = 0;
        }

        public void ClearBuffer()
        {
            DebugLogger.Log("cwmonitor", "Clearing decoded buffer");
            lock (_lockObject)
            {
                _decodedBuffer = string.Empty;
                _currentPattern = string.Empty;
            }
            OnPropertyChanged(nameof(DecodedBuffer));
        }

        private void Start()
        {
            if (_enabled)
                return;

            DebugLogger.Log("cwmonitor", "Starting CW Monitor");

            _cancellationTokenSource = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitoringLoop(_cancellationTokenSource.Token));
            _enabled = true;

            DebugLogger.Log("cwmonitor", "CW Monitor started");
        }

        private void Stop()
        {
            if (!_enabled)
                return;

            DebugLogger.Log("cwmonitor", "Stopping CW Monitor");

            _cancellationTokenSource?.Cancel();

            try
            {
                _monitoringTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("cwmonitor", $"Error waiting for monitoring task: {ex.Message}");
            }

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _enabled = false;

            DebugLogger.Log("cwmonitor", "CW Monitor stopped");
        }

        private async Task MonitoringLoop(CancellationToken cancellationToken)
        {
            var keyDownTracker = new StateTracker();
            var keyUpTracker = new StateTracker();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1, cancellationToken);

                    ProcessKeyDownTiming(keyDownTracker);
                    ProcessKeyUpTiming(keyUpTracker);
                }
            }
            catch (OperationCanceledException)
            {
                DebugLogger.Log("cwmonitor", "Monitoring loop cancelled");
            }
            catch (Exception ex)
            {
                DebugLogger.Log("cwmonitor", $"Error in monitoring loop: {ex.Message}");
            }
        }

        private void ProcessKeyDownTiming(StateTracker tracker)
        {
            if (_isKeyDown && !tracker.IsActive)
            {
                tracker.Start();
            }
            else if (!_isKeyDown && tracker.IsActive)
            {
                int durationMs = tracker.Stop();
                _elementCount++;

                _timingAnalyzer.RecordTiming(durationMs);

                if (_elementCount > MinElementsBeforeDecoding)
                {
                    var stats = _timingAnalyzer.AnalyzeStatistics();
                    var element = ClassifyKeyDownHybrid(durationMs, stats);
                    string elementStr = _classifier.KeyElementToString(element);

                    _currentPattern += elementStr;
                    DebugLogger.Log("cwmonitor", $"Element: {elementStr}, Pattern: {_currentPattern}");
                }
            }
        }
        
        /// <summary>
        /// Hybrid classification for key-down events respecting algorithm mode
        /// </summary>
        private ElementClassifier.KeyElement ClassifyKeyDownHybrid(int durationMs, KeyingStatistics stats)
        {
            // If mode is STAT or NN not available, use bimodal only
            if (_algorithmMode == CWAlgorithmMode.STAT || !_neuralNetworkAvailable)
            {
                return _classifier.ClassifyKeyDown(durationMs, stats);
            }

            // DNN mode - try neural network with bimodal fallback
            if (stats.IsValid)
            {
                try
                {
                    var (prediction, probabilities) = _neuralClassifier.Classify(durationMs, isKeyDown: true);
                    float confidence = probabilities[(int)prediction];
                    
                    // If confidence is high, trust the neural network
                    if (confidence >= NeuralNetworkConfidenceThreshold)
                    {
                        var nnResult = ConvertNeuralToKeyElement(prediction);
                        DebugLogger.Log("cwmonitor", $"DNN KeyDown: {prediction} ({confidence:P1} confidence) -> {nnResult}");
                        return nnResult;
                    }
                    
                    // Low confidence, use bimodal fallback
                    DebugLogger.Log("cwmonitor", $"Low DNN confidence ({confidence:P1}), using STAT fallback");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("cwmonitor", $"DNN error: {ex.Message}, using STAT fallback");
                }
            }
            
            // Fall back to bimodal classifier
            return _classifier.ClassifyKeyDown(durationMs, stats);
        }
        
        /// <summary>
        /// Converts neural network prediction to KeyElement type
        /// </summary>
        private ElementClassifier.KeyElement ConvertNeuralToKeyElement(MorseNeuralClassifier.MorseElementType neuralType)
        {
            return neuralType switch
            {
                MorseNeuralClassifier.MorseElementType.Dit => ElementClassifier.KeyElement.Dit,
                MorseNeuralClassifier.MorseElementType.Dah => ElementClassifier.KeyElement.Dah,
                _ => ElementClassifier.KeyElement.Unknown
            };
        }
        
        /// <summary>
        /// Converts neural network prediction to SpaceElement type
        /// </summary>
        private ElementClassifier.SpaceElement ConvertNeuralToSpaceElement(MorseNeuralClassifier.MorseElementType neuralType)
        {
            return neuralType switch
            {
                MorseNeuralClassifier.MorseElementType.ElementSpace => ElementClassifier.SpaceElement.InterElement,
                MorseNeuralClassifier.MorseElementType.WordSpace => ElementClassifier.SpaceElement.WordSpace,
                _ => ElementClassifier.SpaceElement.Unknown
            };
        }

        private void ProcessKeyUpTiming(StateTracker tracker)
        {
            if (!_isKeyDown && !tracker.IsActive)
            {
                tracker.Start();
            }
            else if (_isKeyDown && tracker.IsActive)
            {
                int durationMs = tracker.Stop();

                if (_elementCount > MinElementsBeforeDecoding && !string.IsNullOrEmpty(_currentPattern))
                {
                    ProcessSpacing(durationMs);
                }
            }
            else if (!_isKeyDown && tracker.IsActive && !string.IsNullOrEmpty(_currentPattern))
            {
                // Check for timeout (word space)
                int currentDuration = tracker.GetCurrentDuration();
                if (currentDuration > 0 && _elementCount > MinElementsBeforeDecoding)
                {
                    var stats = _timingAnalyzer.AnalyzeStatistics();
                    var spaceType = ClassifyKeyUpHybrid(currentDuration, stats);

                    if (spaceType == ElementClassifier.SpaceElement.WordSpace)
                    {
                        CompleteCharacter();
                        AppendToBuffer(" ");
                        OnPropertyChanged(nameof(CurrentStatistics));
                    }
                }
            }
        }

        private void ProcessSpacing(int durationMs)
        {
            var stats = _timingAnalyzer.AnalyzeStatistics();
            var spaceType = ClassifyKeyUpHybrid(durationMs, stats);

            switch (spaceType)
            {
                case ElementClassifier.SpaceElement.InterElement:
                    DebugLogger.Log("cwmonitor", "Inter-element space");
                    break;

                case ElementClassifier.SpaceElement.LetterSpace:
                    DebugLogger.Log("cwmonitor", $"Letter space - completing pattern: {_currentPattern}");
                    CompleteCharacter();
                    OnPropertyChanged(nameof(CurrentStatistics));
                    break;

                case ElementClassifier.SpaceElement.WordSpace:
                    DebugLogger.Log("cwmonitor", $"Word space - completing pattern: {_currentPattern}");
                    CompleteCharacter();
                    AppendToBuffer(" ");
                    OnPropertyChanged(nameof(CurrentStatistics));
                    break;

                default:
                    DebugLogger.Log("cwmonitor", $"Unknown space type: {durationMs}ms");
                    break;
            }
        }
        
        /// <summary>
        /// Hybrid classification for key-up events respecting algorithm mode
        /// </summary>
        private ElementClassifier.SpaceElement ClassifyKeyUpHybrid(int durationMs, KeyingStatistics stats)
        {
            // If mode is STAT or NN not available, use bimodal only
            if (_algorithmMode == CWAlgorithmMode.STAT || !_neuralNetworkAvailable)
            {
                return _classifier.ClassifyKeyUp(durationMs, stats);
            }

            // DNN mode - try neural network with bimodal fallback
            if (stats.IsValid)
            {
                try
                {
                    var (prediction, probabilities) = _neuralClassifier.Classify(durationMs, isKeyDown: false);
                    float confidence = probabilities[(int)prediction];
                    
                    // If confidence is high, trust the neural network
                    if (confidence >= NeuralNetworkConfidenceThreshold)
                    {
                        var nnResult = ConvertNeuralToSpaceElement(prediction);
                        DebugLogger.Log("cwmonitor", $"DNN KeyUp: {prediction} ({confidence:P1} confidence) -> {nnResult}");
                        return nnResult;
                    }
                    
                    // Low confidence, use bimodal fallback
                    DebugLogger.Log("cwmonitor", $"Low DNN confidence ({confidence:P1}), using STAT fallback");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("cwmonitor", $"DNN error: {ex.Message}, using STAT fallback");
                }
            }
            
            // Fall back to bimodal classifier
            return _classifier.ClassifyKeyUp(durationMs, stats);
        }

        private void CompleteCharacter()
        {
            if (string.IsNullOrEmpty(_currentPattern))
                return;

            string decodedChar = _decoder.Decode(_currentPattern);
            DebugLogger.Log("cwmonitor", $"Decoded '{_currentPattern}' -> '{decodedChar}'");

            AppendToBuffer(decodedChar);
            _currentPattern = string.Empty;
        }

        private void AppendToBuffer(string text)
        {
            lock (_lockObject)
            {
                _decodedBuffer += text;

                if (_decodedBuffer.Length > BufferMaxLength)
                {
                    _decodedBuffer = _decodedBuffer.Substring(_decodedBuffer.Length - BufferMaxLength);
                }
            }

            OnPropertyChanged(nameof(DecodedBuffer));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Disposes resources used by the CWMonitor
        /// </summary>
        public void Dispose()
        {
            Stop();
            _statsResetTimer?.Dispose();
            _neuralClassifier?.Dispose();
        }

        /// <summary>
        /// Helper class to track timing states
        /// </summary>
        private class StateTracker
        {
            private DateTime _startTime;
            private bool _isActive;

            public bool IsActive => _isActive;

            public void Start()
            {
                _startTime = DateTime.UtcNow;
                _isActive = true;
            }

            public int Stop()
            {
                if (!_isActive)
                    return 0;

                int duration = (int)(DateTime.UtcNow - _startTime).TotalMilliseconds;
                _isActive = false;
                return duration;
            }

            public int GetCurrentDuration()
            {
                if (!_isActive)
                    return 0;

                return (int)(DateTime.UtcNow - _startTime).TotalMilliseconds;
            }
        }
    }
}
