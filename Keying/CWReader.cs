// Ported by Chris L. White WX7V from the original RemoteKeyerInterface project
// written by Matt Murphy NQ6N. Used with his permission.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using NetKeyer.Helpers;

namespace NetKeyer.Keying;

/// <summary>
/// Holds the computed dit/dah timing statistics derived from the bimodal
/// histogram of observed key-down durations.
/// </summary>
public struct KeyingStats
{
    public int FirstPlace;
    public int SecondPlace;
    public int DitLength;
    public int DahLength;

    /// <summary>Total number of key-down elements sampled across all buckets.</summary>
    public int TotalSamples;

    /// <summary>
    /// Dah/Dit length ratio. Ideal CW is 3.0; values outside ~2.5–3.5 indicate
    /// timing inconsistency.
    /// </summary>
    public double DahDitRatio;

    /// <summary>
    /// The dit/dah pattern of the character currently being assembled,
    /// e.g. "-.-" for K.  Empty between characters.
    /// </summary>
    public string CharInProgress;
}

/// <summary>
/// Real-time adaptive Morse code (CW) decoder.
///
/// Observes raw key-down and key-up timings, builds a bimodal histogram to
/// learn the operator's actual dit and dah durations, then decodes the
/// resulting dit/dah patterns into ASCII text.
///
/// Thread safety: <see cref="OnKeyDown"/> and <see cref="OnKeyUp"/> may be
/// called from any thread. All shared state is protected by <see cref="_lock"/>.
/// </summary>
public class CWReader : INotifyPropertyChanged
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>Number of characters kept in the rolling decoded text buffer.</summary>
    private const int BufferWindowSize = 80;

    /// <summary>Minimum number of key-down elements observed before decoding begins.</summary>
    private const int MinElementsBeforeDecode = 10;

    /// <summary>Histogram bucket rounding granularity (ms).</summary>
    private const int BucketGranularity = 24;

    /// <summary>Stats auto-reset interval (30 minutes).</summary>
    private static readonly TimeSpan StatsResetInterval = TimeSpan.FromMinutes(30);

    // -------------------------------------------------------------------------
    // Morse code lookup table
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps dit/dah patterns (e.g. ".-") to their decoded characters or
    /// prosign abbreviations.
    /// </summary>
    public static readonly Dictionary<string, string> Letters =
        new Dictionary<string, string>
        {
            { ".-",      "A" },
            { "-...",    "B" },
            { "-.-.",    "C" },
            { "-..",     "D" },
            { ".",       "E" },
            { "..-.",    "F" },
            { "--.",     "G" },
            { "....",    "H" },
            { "..",      "I" },
            { ".---",    "J" },
            { "-.-",     "K" },
            { ".-..",    "L" },
            { "--",      "M" },
            { "-.",      "N" },
            { "---",     "O" },
            { ".--.",    "P" },
            { "--.-",    "Q" },
            { ".-.",     "R" },
            { "...",     "S" },
            { "-",       "T" },
            { "..-",     "U" },
            { "...-",    "V" },
            { ".--",     "W" },
            { "-..-",    "X" },
            { "-.--",    "Y" },
            { "--..",    "Z" },
            { "-----",   "0" },
            { ".----",   "1" },
            { "..---",   "2" },
            { "...--",   "3" },
            { "....-",   "4" },
            { ".....",   "5" },
            { "-....",   "6" },
            { "--...",   "7" },
            { "---..",   "8" },
            { "----.",   "9" },
            { ".-.-.-",  "." },
            { "--..--",  "," },
            { "-...-.-", "BK" },
            { ".-.-.",   "AR" },
            { "-.--.",   "KN" },
            { "-...-",   "BT" },
            { "...-.-",  "SK" },
            { "-..-.",   "/" },
            { "..--..",  "?" },
        };

    // -------------------------------------------------------------------------
    // Private fields
    // -------------------------------------------------------------------------

    private readonly object _lock = new object();

    // Timer that periodically clears the histogram so the decoder can adapt
    // to a new sending speed without waiting indefinitely.
    private readonly System.Timers.Timer _resetStatsTimer;

    // Background decoder thread
    private Thread _workerThread;
    private CancellationTokenSource _cts;
    private bool _enabled;

    // Keying state — written by OnKeyDown/OnKeyUp (any thread),
    // read by the decoder thread. Volatile ensures visibility without a lock
    // on the hot path; the decoder only needs the latest sample.
    private volatile bool _keyed;

    // Accumulates the current character's dit/dah pattern (e.g. ".-")
    // until a letter/word space is detected.
    private string _charInProgress = "";

    // Histogram: bucket key → list of raw key-down durations (ms) in that bucket.
    // Accessed under _lock.
    private readonly Dictionary<int, List<int>> _stats = new Dictionary<int, List<int>>();

    // Rolling decoded-text buffer (last BufferWindowSize characters).
    private string _buffer = "";

    // -------------------------------------------------------------------------
    // Public properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gets or sets whether the decoder is running.
    /// Setting to <c>true</c> starts the background thread; <c>false</c> stops it.
    /// </summary>
    public bool Enabled
    {
        get
        {
            lock (_lock) { return _enabled; }
        }
        set
        {
            bool current;
            lock (_lock) { current = _enabled; }

            if (value && !current) Start();
            else if (!value && current) Stop();
        }
    }

    /// <summary>
    /// Rolling decoded-text window (last <see cref="BufferWindowSize"/> characters).
    /// Raises <see cref="PropertyChanged"/> on the thread that updates it
    /// (the decoder thread — callers should marshal to the UI thread as needed).
    /// </summary>
    public string Buffer
    {
        get { lock (_lock) { return _buffer; } }
        private set
        {
            lock (_lock)
            {
                // Append and keep only the trailing BufferWindowSize characters.
                _buffer = value;
                if (_buffer.Length > BufferWindowSize)
                    _buffer = _buffer.Substring(_buffer.Length - BufferWindowSize);
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Buffer)));
        }
    }

    /// <summary>
    /// Read-only access to the raw histogram data (for diagnostics/testing).
    /// Returns a snapshot copy to avoid holding the lock during enumeration.
    /// </summary>
    public Dictionary<int, List<int>> StatsCopy
    {
        get
        {
            lock (_lock)
            {
                var copy = new Dictionary<int, List<int>>(_stats.Count);
                foreach (var kvp in _stats)
                    copy[kvp.Key] = new List<int>(kvp.Value);
                return copy;
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public CWReader()
    {
        _resetStatsTimer = new System.Timers.Timer(StatsResetInterval.TotalMilliseconds)
        {
            AutoReset = true
        };
        _resetStatsTimer.Elapsed += (_, _) => ResetStats();
        _resetStatsTimer.Start();
    }

    // -------------------------------------------------------------------------
    // Public key-state API  (called from input thread / UI thread)
    // -------------------------------------------------------------------------

    /// <summary>Called when the key is pressed down.</summary>
    public void OnKeyDown() => _keyed = true;

    /// <summary>Called when the key is released.</summary>
    public void OnKeyUp() => _keyed = false;

    /// <summary>Clears the timing histogram so the decoder re-learns from scratch.</summary>
    public void ResetStats()
    {
        DebugLogger.Log("cwreader", "CWReader: Resetting timing histogram");
        lock (_lock)
        {
            _stats.Clear();
        }
    }

    /// <summary>
    /// Clears the decoded text buffer and the character currently being assembled.
    /// </summary>
    public void ClearBuffer()
    {
        lock (_lock)
        {
            _buffer = "";
            _charInProgress = "";
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Buffer)));
        DebugLogger.Log("cwreader", "CWReader: Buffer cleared");
    }

    // -------------------------------------------------------------------------
    // Start / Stop
    // -------------------------------------------------------------------------

    public void Start()
    {
        lock (_lock)
        {
            if (_enabled) return;

            _cts = new CancellationTokenSource();
            _workerThread = new Thread(DecoderLoop)
            {
                Name = "CW Reader Thread",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _workerThread.Start();
            _enabled = true;
        }
        DebugLogger.Log("cwreader", "CWReader: Decoder started");
    }

    public void Stop()
    {
        CancellationTokenSource cts;
        Thread thread;

        lock (_lock)
        {
            if (!_enabled) return;
            cts = _cts;
            thread = _workerThread;
            _cts = null;
            _workerThread = null;
            _enabled = false;
        }

        cts?.Cancel();

        if (thread != null && thread.IsAlive)
        {
            thread.Join(1000);
            if (thread.IsAlive)
                DebugLogger.Log("cwreader", "CWReader: Warning - decoder thread did not stop gracefully");
        }

        cts?.Dispose();
        DebugLogger.Log("cwreader", "CWReader: Decoder stopped");
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rounds <paramref name="toRound"/> up to the nearest multiple of
    /// <paramref name="nearest"/>.
    /// </summary>
    private static int RoundUp(int toRound, int nearest)
    {
        if (toRound % nearest == 0) return toRound;
        return (nearest - toRound % nearest) + toRound;
    }

    /// <summary>
    /// Looks up a dit/dah pattern in the Morse table.
    /// Returns the character, or <c>#(pattern)</c> if the pattern is unknown.
    /// </summary>
    private static string LookupChar(string pattern)
    {
        return Letters.TryGetValue(pattern, out string ch) ? ch : "#(" + pattern + ")";
    }

    /// <summary>
    /// Computes the two histogram modes from the accumulated timing data and
    /// maps them to <see cref="KeyingStats.DitLength"/> and
    /// <see cref="KeyingStats.DahLength"/>.
    ///
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private KeyingStats ComputeModes()
    {
        var ks = new KeyingStats
        {
            FirstPlace     = -1,
            SecondPlace    = -1,
            DitLength      = 0,
            DahLength      = 0,
            TotalSamples   = 0,
            DahDitRatio    = 0.0,
            CharInProgress = _charInProgress
        };

        if (_stats.Count == 0) return ks;

        var averages = new Dictionary<int, double>(_stats.Count);
        var counts   = new Dictionary<int, int>(_stats.Count);

        foreach (int bucket in _stats.Keys)
        {
            List<int> samples = _stats[bucket];
            if (samples.Count == 0) continue;

            averages[bucket]  = samples.Average();
            counts[bucket]    = samples.Count;
            ks.TotalSamples  += samples.Count;

            int count = samples.Count;

            if (ks.FirstPlace == -1 || count >= counts.GetValueOrDefault(ks.FirstPlace))
            {
                ks.SecondPlace = ks.FirstPlace;
                ks.FirstPlace  = bucket;
                continue;
            }

            if (ks.SecondPlace == -1 || count >= counts.GetValueOrDefault(ks.SecondPlace))
            {
                ks.SecondPlace = bucket;
            }
        }

        if (ks.FirstPlace == -1 || ks.SecondPlace == -1) return ks;

        int[] lengths = new int[]
        {
            (int)averages[ks.FirstPlace],
            (int)averages[ks.SecondPlace]
        };
        Array.Sort(lengths);

        ks.DitLength   = lengths[0];
        ks.DahLength   = lengths[1];
        ks.DahDitRatio = ks.DitLength > 0 ? Math.Round((double)ks.DahLength / ks.DitLength, 2) : 0.0;

        return ks;
    }

    /// <summary>
    /// Classifies a key-down duration as a dit (<c>"."</c>) or dah (<c>"-"</c>).
    /// </summary>
    private static string ClassifyKeyDown(KeyingStats modes, int durationMs)
    {
        if (durationMs <= modes.DitLength * 1.5f) return ".";
        if (durationMs >  modes.DitLength * 1.5f) return "-";
        if (durationMs >= modes.DahLength * 0.8f) return "-";
        return "?";
    }

    /// <summary>
    /// Classifies a key-up (silence) duration as one of:
    /// <c>INTER_SPACE</c>, <c>LETTER_SPACE</c>, or <c>WORD_SPACE</c>.
    /// Thresholds differ by keying mode:
    /// - Straight key: inter/letter boundary at 1.3× dit (natural human timing variation)
    /// - Iambic: inter/letter boundary at 2.0× dit (machine-precise inter-element silences
    ///   sit at exactly 1 dit length and need more headroom)
    /// </summary>
    private string ClassifyKeyUp(KeyingStats modes, int durationMs)
    {
        double interLetterThreshold = _keyingMode == KeyingMode.Iambic ? 2.5 : 1.3;

        if (durationMs <= modes.DitLength * interLetterThreshold)                                      return "INTER_SPACE";
        if (durationMs >  modes.DitLength * interLetterThreshold && durationMs < modes.DitLength * 5.9) return "LETTER_SPACE";
        if (durationMs >  modes.DitLength * 5.9)                                                       return "WORD_SPACE";

        DebugLogger.Log("cwreader", $"CWReader: Unhandled key-up case: {durationMs} ms");
        return "UNKNOWN_SPACE";
    }

    // -------------------------------------------------------------------------
    // Keying mode
    // -------------------------------------------------------------------------

    /// <summary>
    /// The keying mode — affects silence classification thresholds.
    /// Iambic mode uses wider inter-element tolerance because the keyer
    /// generates machine-precise timings that sit close to the boundary.
    /// </summary>
    public enum KeyingMode { StraightKey, Iambic }

    private KeyingMode _keyingMode = KeyingMode.StraightKey;

    /// <summary>
    /// Gets or sets the current keying mode.
    /// Must be set before or after <see cref="Start"/> — safe to call at any time.
    /// </summary>
    public KeyingMode Mode
    {
        get { lock (_lock) { return _keyingMode; } }
        set { lock (_lock) { _keyingMode = value; } }
    }

    private void DecoderLoop()
    {
        DateTime keyDownAt  = DateTime.UtcNow;
        DateTime keyUpAt    = DateTime.UtcNow;
        bool isTimingKeyDown = false;
        bool isTimingKeyUp   = false;
        int  elementCount    = 0;

        CancellationToken token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            Thread.Sleep(1);

            bool keyed = _keyed; // single volatile read per loop iteration

            // ------------------------------------------------------------------
            // Key-DOWN timing
            // ------------------------------------------------------------------

            if (keyed && !isTimingKeyDown)
            {
                keyDownAt     = DateTime.UtcNow;
                isTimingKeyDown = true;
            }

            if (!keyed && isTimingKeyDown)
            {
                elementCount++;
                int durationMs  = (int)(DateTime.UtcNow - keyDownAt).TotalMilliseconds;
                isTimingKeyDown = false;

                // Map duration into a logarithmically-compressed bucket so that
                // the bimodal histogram converges quickly regardless of WPM.
                int bucket = RoundUp((int)Math.Pow(Math.Log(Math.Max(durationMs, 1), 2), 2), BucketGranularity);

                string charProgress;
                lock (_lock)
                {
                    if (!_stats.ContainsKey(bucket))
                        _stats[bucket] = new List<int>();
                    _stats[bucket].Add(durationMs);

                    if (elementCount > MinElementsBeforeDecode)
                    {
                        KeyingStats modes = ComputeModes();
                        string element = ClassifyKeyDown(modes, durationMs);
                        _charInProgress += element;
                        DebugLogger.Log("cwreader", $"CWReader: KeyDown {durationMs}ms → '{element}' | inProgress='{_charInProgress}'");
                    }
                    charProgress = _charInProgress;
                }
            }

            // ------------------------------------------------------------------
            // Key-UP timing
            // ------------------------------------------------------------------

            if (!keyed && !isTimingKeyUp)
            {
                keyUpAt      = DateTime.UtcNow;
                isTimingKeyUp = true;
            }

            // Key went down → measure silence and decide letter/word boundary.
            if (keyed && isTimingKeyUp)
            {
                int durationMs = (int)(DateTime.UtcNow - keyUpAt).TotalMilliseconds;
                isTimingKeyUp  = false;

                if (elementCount > MinElementsBeforeDecode)
                {
                    string charProgress;
                    KeyingStats modes;
                    lock (_lock)
                    {
                        charProgress = _charInProgress;
                        modes        = ComputeModes();
                    }

                    if (charProgress.Length == 0) continue;

                    string spaceType = ClassifyKeyUp(modes, durationMs);
                    string ch        = LookupChar(charProgress);

                    switch (spaceType)
                    {
                        case "INTER_SPACE":
                            // Still within the same character — do nothing.
                            break;

                        case "LETTER_SPACE":
                            lock (_lock) { _charInProgress = ""; }
                            Buffer += ch;
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Modes)));
                            DebugLogger.Log("cwreader", $"CWReader: Letter '{ch}' (letter space)");
                            break;

                        case "WORD_SPACE":
                            lock (_lock) { _charInProgress = ""; }
                            Buffer += ch;
                            Buffer += " ";
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Modes)));
                            DebugLogger.Log("cwreader", $"CWReader: Letter '{ch}' + word space");
                            break;

                        default:
                            DebugLogger.Log("cwreader",
                                $"CWReader: Unmatched key-up element — {durationMs} ms, type='{spaceType}'");
                            break;
                    }
                }
            }

            // ------------------------------------------------------------------
            // End-of-word detection: key has been up long enough to flush the
            // character in progress even though we haven't seen a new key-down.
            // ------------------------------------------------------------------

            if (!keyed && isTimingKeyUp && elementCount > MinElementsBeforeDecode)
            {
                int durationMs = (int)(DateTime.UtcNow - keyUpAt).TotalMilliseconds;

                string charProgress;
                KeyingStats modes;
                lock (_lock)
                {
                    charProgress = _charInProgress;
                    modes        = ComputeModes();
                }

                if (charProgress.Length > 0 && modes.DitLength > 0)
                {
                    string spaceType = ClassifyKeyUp(modes, durationMs);
                    if (spaceType == "WORD_SPACE")
                    {
                        string ch = LookupChar(charProgress);
                        lock (_lock) { _charInProgress = ""; }
                        Buffer += ch;
                        Buffer += " ";
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Modes)));
                        DebugLogger.Log("cwreader", $"CWReader: End-of-word flush '{ch}'");
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Diagnostic property (mirrors RKI's Modes property for compatibility)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the current computed dit/dah timing statistics.
    /// Primarily useful for diagnostics and testing.
    /// </summary>
    public KeyingStats Modes
    {
        get { lock (_lock) { return ComputeModes(); } }
    }
}
