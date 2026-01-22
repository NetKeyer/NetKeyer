using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private const float LetterSpaceThreshold = 5.9f;

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
    /// Main CW (Morse Code) monitoring service
    /// </summary>
    public class CWMonitor : INotifyPropertyChanged
    {
        private readonly TimingAnalyzer _timingAnalyzer;
        private readonly ElementClassifier _classifier;
        private readonly MorseCodeDecoder _decoder;
        private readonly Timer _statsResetTimer;
        private readonly object _lockObject = new object();

        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private bool _enabled;
        private volatile bool _isKeyDown;
        private string _decodedBuffer = string.Empty;
        private string _currentPattern = string.Empty;
        private int _elementCount;

        private const int BufferMaxLength = 20;
        private const int MinElementsBeforeDecoding = 10;
        private const int StatsResetIntervalMinutes = 30;

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

        public KeyingStatistics CurrentStatistics => _timingAnalyzer.AnalyzeStatistics();

        public CWMonitor()
        {
            _timingAnalyzer = new TimingAnalyzer();
            _classifier = new ElementClassifier();
            _decoder = new MorseCodeDecoder();

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
                    var element = _classifier.ClassifyKeyDown(durationMs, stats);
                    string elementStr = _classifier.KeyElementToString(element);

                    _currentPattern += elementStr;
                    DebugLogger.Log("cwmonitor", $"Element: {elementStr}, Pattern: {_currentPattern}");
                }
            }
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
                    var spaceType = _classifier.ClassifyKeyUp(currentDuration, stats);

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
            var spaceType = _classifier.ClassifyKeyUp(durationMs, stats);

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
