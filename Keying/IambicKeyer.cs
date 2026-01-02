using System;
using NetKeyer.Audio;
using NetKeyer.Helpers;

namespace NetKeyer.Keying;

/// <summary>
/// Iambic keyer implementation supporting Mode A and Mode B.
/// Handles timing, paddle latching, and element generation for iambic keying.
/// Uses event-driven architecture based on audio sample timing.
/// </summary>
public class IambicKeyer
{
    private readonly object _lock = new object();
    private readonly Func<string> _getTimestamp;
    private readonly Action<bool, string, uint> _sendRadioKey;
    private ISidetoneGenerator _sidetoneGenerator;
    private readonly uint _radioClientHandle;

    private bool _iambicDitLatched = false;
    private bool _iambicDahLatched = false;
    private bool _ditPaddleAtStart = false;
    private bool _dahPaddleAtStart = false;
    private bool _ditPaddleAtSilenceStart = false;
    private bool _dahPaddleAtSilenceStart = false;
    private bool _currentDitPaddleState = false;
    private bool _currentDahPaddleState = false;
    private int _ditLength = 60; // milliseconds
    private KeyerState _keyerState = KeyerState.Idle;
    private bool _lastElementWasDit = true; // Track what was actually sent last
    private DateTime _lastStateChange = DateTime.UtcNow;

    private enum KeyerState
    {
        Idle,              // Nothing playing, nothing queued
        TonePlaying,       // Tone actively playing (between OnToneStart and OnToneComplete)
        InterElementSpace  // Silence between elements
    }

    /// <summary>
    /// Gets or sets whether to use Mode B (true) or Mode A (false).
    /// </summary>
    public bool IsModeB { get; set; } = true;

    /// <summary>
    /// Creates a new iambic keyer instance.
    /// </summary>
    /// <param name="sidetoneGenerator">Sidetone generator for local audio feedback</param>
    /// <param name="radioClientHandle">Radio client handle for sending commands (0 for sidetone-only)</param>
    /// <param name="getTimestamp">Function to get current timestamp for radio commands</param>
    /// <param name="sendRadioKey">Action to send CW key command to radio (can be null for sidetone-only)</param>
    public IambicKeyer(
        ISidetoneGenerator sidetoneGenerator,
        uint radioClientHandle,
        Func<string> getTimestamp,
        Action<bool, string, uint> sendRadioKey)
    {
        _sidetoneGenerator = sidetoneGenerator ?? throw new ArgumentNullException(nameof(sidetoneGenerator));
        _radioClientHandle = radioClientHandle;
        _getTimestamp = getTimestamp ?? throw new ArgumentNullException(nameof(getTimestamp));
        _sendRadioKey = sendRadioKey; // Can be null for sidetone-only mode

        // Subscribe to sidetone events
        _sidetoneGenerator.OnSilenceComplete += OnSilenceComplete;
        _sidetoneGenerator.OnToneStart += OnToneStart;
        _sidetoneGenerator.OnToneComplete += OnToneComplete;
        _sidetoneGenerator.OnBeforeSilenceEnd += OnBeforeSilenceEnd;
    }

    /// <summary>
    /// Sets the CW speed in WPM and recalculates timing.
    /// </summary>
    public void SetWpm(int wpm)
    {
        lock (_lock)
        {
            if (wpm > 0)
            {
                _ditLength = 1200 / wpm;
            }
            else
            {
                _ditLength = 60; // Default to 20 WPM
            }
        }
    }

    /// <summary>
    /// Updates the keyer with current paddle states.
    /// Call this whenever paddle state changes.
    /// </summary>
    public void UpdatePaddleState(bool ditPaddle, bool dahPaddle)
    {
        lock (_lock)
        {
            DebugLogger.Log("keyer", $"[IambicKeyer] UpdatePaddleState: L={ditPaddle} R={dahPaddle} State={_keyerState}");

            // Safety check: if state machine has been stuck for >1 second, force reset
            if (_keyerState != KeyerState.Idle)
            {
                var elapsed = (DateTime.UtcNow - _lastStateChange).TotalMilliseconds;
                if (elapsed > 1000)
                {
                    DebugLogger.Log("keyer", $"[IambicKeyer] State timeout detected - forcing reset from {_keyerState}");
                    Stop();
                }
            }

            // Update current paddle states
            _currentDitPaddleState = ditPaddle;
            _currentDahPaddleState = dahPaddle;

            // If keyer is idle and at least one paddle is pressed, start sending
            if (_keyerState == KeyerState.Idle && (ditPaddle || dahPaddle))
            {
                StartNextElement();
            }
            // If keyer is playing, update alternation latches for opposite paddle
            else if (_keyerState == KeyerState.TonePlaying)
            {
                // Set alternation latch for opposite paddle if it's pressed during tone
                // (paddle is "newly pressed" if it wasn't already pressed at element start)
                if (_lastElementWasDit && dahPaddle && !_dahPaddleAtStart && !_iambicDahLatched)
                {
                    _iambicDahLatched = true;
                    DebugLogger.Log("keyer", $"[IambicKeyer] Setting DAH latch (opposite paddle during dit tone)");
                }
                if (!_lastElementWasDit && ditPaddle && !_ditPaddleAtStart && !_iambicDitLatched)
                {
                    _iambicDitLatched = true;
                    DebugLogger.Log("keyer", $"[IambicKeyer] Setting DIT latch (opposite paddle during dah tone)");
                }
            }
            // If in inter-element space, update alternation latches for opposite paddle
            // Decision about next element happens in OnBeforeSilenceEnd
            else if (_keyerState == KeyerState.InterElementSpace)
            {
                // Set alternation latches for opposite paddle pressed during silence
                if (_lastElementWasDit && dahPaddle && !_dahPaddleAtSilenceStart && !_iambicDahLatched)
                {
                    _iambicDahLatched = true;
                    DebugLogger.Log("keyer", $"[IambicKeyer] Setting DAH latch (opposite paddle during silence after dit)");
                }
                if (!_lastElementWasDit && ditPaddle && !_ditPaddleAtSilenceStart && !_iambicDitLatched)
                {
                    _iambicDitLatched = true;
                    DebugLogger.Log("keyer", $"[IambicKeyer] Setting DIT latch (opposite paddle during silence after dah)");
                }
                // Don't call Stop() here - decision happens in OnBeforeSilenceEnd
            }
        }
    }

    /// <summary>
    /// Stops the keyer immediately and resets to idle state.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            DebugLogger.Log("keyer", $"[IambicKeyer] Stop called, going to Idle");

            // Send radio key-up if needed
            SendRadioKey(false);

            // Reset state
            _keyerState = KeyerState.Idle;
            _lastStateChange = DateTime.UtcNow;
            _iambicDitLatched = false;
            _iambicDahLatched = false;
            _ditPaddleAtStart = false;
            _dahPaddleAtStart = false;
            _ditPaddleAtSilenceStart = false;
            _dahPaddleAtSilenceStart = false;
        }
    }

    /// <summary>
    /// Updates the sidetone generator. Useful when changing audio output device.
    /// </summary>
    public void UpdateSidetoneGenerator(ISidetoneGenerator sidetoneGenerator)
    {
        lock (_lock)
        {
            if (sidetoneGenerator == null)
                throw new ArgumentNullException(nameof(sidetoneGenerator));

            // Unsubscribe from old generator events
            if (_sidetoneGenerator != null)
            {
                _sidetoneGenerator.OnSilenceComplete -= OnSilenceComplete;
                _sidetoneGenerator.OnToneStart -= OnToneStart;
                _sidetoneGenerator.OnToneComplete -= OnToneComplete;
                _sidetoneGenerator.OnBeforeSilenceEnd -= OnBeforeSilenceEnd;
            }

            // Update to new generator
            _sidetoneGenerator = sidetoneGenerator;

            // Subscribe to new generator events
            _sidetoneGenerator.OnSilenceComplete += OnSilenceComplete;
            _sidetoneGenerator.OnToneStart += OnToneStart;
            _sidetoneGenerator.OnToneComplete += OnToneComplete;
            _sidetoneGenerator.OnBeforeSilenceEnd += OnBeforeSilenceEnd;

            DebugLogger.Log("keyer", $"[IambicKeyer] Sidetone generator updated");
        }
    }

    /// <summary>
    /// Called when a tone starts (including queued tones). Send radio key-down.
    /// </summary>
    private void OnToneStart()
    {
        lock (_lock)
        {
            DebugLogger.Log("keyer", $"[IambicKeyer] OnToneStart: Tone starting, sending radio key-down");

            // Capture paddle states at ACTUAL element start time (not decision time)
            // This is critical for Mode B completion logic to work correctly
            _ditPaddleAtStart = _currentDitPaddleState;
            _dahPaddleAtStart = _currentDahPaddleState;

            // Send radio key-down
            SendRadioKey(true);
            _keyerState = KeyerState.TonePlaying;
            _lastStateChange = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Called when a tone completes. Send radio key-up, capture paddle states at silence start,
    /// reset latches, and queue the inter-element silence.
    /// </summary>
    private void OnToneComplete()
    {
        lock (_lock)
        {
            DebugLogger.Log("keyer", $"[IambicKeyer] OnToneComplete: Tone ended, capturing paddle states at silence start");

            // Send radio key-up
            SendRadioKey(false);

            // Set state to InterElementSpace
            _keyerState = KeyerState.InterElementSpace;
            _lastStateChange = DateTime.UtcNow;

            // Capture paddle states at START of silence (for repetition logic)
            _ditPaddleAtSilenceStart = _currentDitPaddleState;
            _dahPaddleAtSilenceStart = _currentDahPaddleState;

            DebugLogger.Log("keyer", $"[IambicKeyer] Paddle states at silence start: dit={_ditPaddleAtSilenceStart}, dah={_dahPaddleAtSilenceStart}, ditLatch={_iambicDitLatched}, dahLatch={_iambicDahLatched}");

            // Queue just the silence (decision about next element happens in OnBeforeSilenceEnd)
            // Note: Alternation latches remain set and will be checked in OnBeforeSilenceEnd
            _sidetoneGenerator?.QueueSilence(_ditLength);
        }
    }

    /// <summary>
    /// Starts or queues a tone with the specified duration.
    /// Clears latches and tracks element type.
    /// </summary>
    private void StartOrQueueTone(int toneDurationMs)
    {
        bool isDit = (toneDurationMs == _ditLength);

        DebugLogger.Log("keyer", $"[IambicKeyer] Starting/queueing {(isDit ? "dit" : "dah")} ({toneDurationMs}ms)");

        // Start tone (will queue if in silence, start immediately if idle)
        _sidetoneGenerator?.StartTone(toneDurationMs);

        // Clear latches and track element
        _iambicDitLatched = false;
        _iambicDahLatched = false;
        _lastElementWasDit = isDit;
    }

    /// <summary>
    /// Called when silence is about to end. Make late decision about next element.
    /// This is the critical decision point where we determine what to send next based on
    /// paddle states during both the tone and the silence period.
    /// </summary>
    private void OnBeforeSilenceEnd()
    {
        lock (_lock)
        {
            DebugLogger.Log("keyer", $"[IambicKeyer] OnBeforeSilenceEnd: Making decision about next element");

            // Decide what to send next based on current state and latches
            int? nextToneDuration = DetermineNextToneDuration();

            if (nextToneDuration.HasValue)
            {
                StartOrQueueTone(nextToneDuration.Value);
            }
            else
            {
                DebugLogger.Log("keyer", $"[IambicKeyer] No element to send, silence will complete and go idle");
                // If no tone, silence will complete and OnSilenceComplete will handle going idle
            }
        }
    }

    /// <summary>
    /// Called when silence completes with no queued tone. Transition to idle and reset state.
    /// Note: Decision about whether to send another element was already made in OnBeforeSilenceEnd.
    /// If we reach here, it means no tone was queued, so we're done.
    /// </summary>
    private void OnSilenceComplete()
    {
        lock (_lock)
        {
            DebugLogger.Log("keyer", $"[IambicKeyer] OnSilenceComplete: Silence ended with no queued tone, going idle");

            _keyerState = KeyerState.Idle;
            _lastStateChange = DateTime.UtcNow;

            // Reset all state
            _iambicDitLatched = false;
            _iambicDahLatched = false;
            _ditPaddleAtStart = false;
            _dahPaddleAtStart = false;
            _ditPaddleAtSilenceStart = false;
            _dahPaddleAtSilenceStart = false;
        }
    }

    /// <summary>
    /// Determines what element to send next based on paddle states and latches, then starts it.
    /// Called from Idle state when at least one paddle is pressed.
    /// Note: Will always send an element (never returns to idle) since at least one paddle is pressed.
    /// </summary>
    private void StartNextElement()
    {
        int? nextDuration = DetermineNextToneDuration();

        // When called from Idle with at least one paddle pressed, DetermineNextToneDuration
        // will always return a tone duration, so this should never be null.
        // The null check is kept for safety but the else branch is unreachable.
        if (nextDuration.HasValue)
        {
            StartOrQueueTone(nextDuration.Value);
        }
    }

    /// <summary>
    /// Determines what the next tone duration should be based on current paddle states and latches.
    /// Uses correct alternation/repetition logic:
    /// - Alternation: opposite paddle pressed during tone OR silence
    /// - Repetition: same paddle held from silence start OR pressed during silence
    /// Returns null if no tone should be sent.
    /// </summary>
    private int? DetermineNextToneDuration()
    {
        bool sendDit = false;
        bool sendDah = false;

        if (_lastElementWasDit || _keyerState == KeyerState.Idle)
        {
            // Just sent dit (or starting from idle)
            // Priority 1: Alternation - opposite paddle pressed during tone or silence
            if (_iambicDahLatched || _currentDahPaddleState)
            {
                sendDah = true;
                DebugLogger.Log("keyer", $"[IambicKeyer] Alternation: sending dah (latch={_iambicDahLatched}, current={_currentDahPaddleState})");
            }
            // Priority 2: Repetition - same paddle held from silence start OR pressed during silence
            else if (_ditPaddleAtSilenceStart || _currentDitPaddleState)
            {
                sendDit = true;
                DebugLogger.Log("keyer", $"[IambicKeyer] Repetition: sending dit (atSilenceStart={_ditPaddleAtSilenceStart}, current={_currentDitPaddleState})");
            }
            // Priority 3 (Mode B only): Squeeze - both held at tone start, both now released
            else if (IsModeB && _dahPaddleAtStart && !_currentDahPaddleState && !_currentDitPaddleState)
            {
                sendDah = true;
                DebugLogger.Log("keyer", $"[IambicKeyer] Mode B squeeze: sending dah (both were held at tone start, both now released)");
            }
        }
        else // was sending dah
        {
            // Just sent dah
            // Priority 1: Alternation - opposite paddle pressed during tone or silence
            if (_iambicDitLatched || _currentDitPaddleState)
            {
                sendDit = true;
                DebugLogger.Log("keyer", $"[IambicKeyer] Alternation: sending dit (latch={_iambicDitLatched}, current={_currentDitPaddleState})");
            }
            // Priority 2: Repetition - same paddle held from silence start OR pressed during silence
            else if (_dahPaddleAtSilenceStart || _currentDahPaddleState)
            {
                sendDah = true;
                DebugLogger.Log("keyer", $"[IambicKeyer] Repetition: sending dah (atSilenceStart={_dahPaddleAtSilenceStart}, current={_currentDahPaddleState})");
            }
            // Priority 3 (Mode B only): Squeeze - both held at tone start, both now released
            else if (IsModeB && _ditPaddleAtStart && !_currentDitPaddleState && !_currentDahPaddleState)
            {
                sendDit = true;
                DebugLogger.Log("keyer", $"[IambicKeyer] Mode B squeeze: sending dit (both were held at tone start, both now released)");
            }
        }

        // Handle special case: both paddles pressed from idle = dit first
        if (_keyerState == KeyerState.Idle && _currentDitPaddleState && _currentDahPaddleState)
        {
            sendDit = true;
            sendDah = false;
            DebugLogger.Log("keyer", $"[IambicKeyer] Both paddles from idle: sending dit first");
        }

        if (sendDit)
            return _ditLength;
        else if (sendDah)
            return _ditLength * 3;
        else
            return null;
    }

    /// <summary>
    /// Sends radio key command (if radio is configured).
    /// </summary>
    private void SendRadioKey(bool state)
    {
        if (_sendRadioKey != null && _radioClientHandle != 0)
        {
            string timestamp = _getTimestamp();
            _sendRadioKey(state, timestamp, _radioClientHandle);
        }
    }
}
