using System;
using NetKeyer.Audio;

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
    private readonly ISidetoneGenerator _sidetoneGenerator;
    private readonly uint _radioClientHandle;

    private bool _enableDebugLogging = true;
    private bool _iambicDitLatched = false;
    private bool _iambicDahLatched = false;
    private bool _ditPaddleAtStart = false;
    private bool _dahPaddleAtStart = false;
    private bool _currentLeftPaddleState = false;
    private bool _currentRightPaddleState = false;
    private int _ditLength = 60; // milliseconds
    private KeyerState _keyerState = KeyerState.Idle;
    private bool _lastElementWasDit = true; // Track what was actually sent last

    private enum KeyerState { Idle, Playing }

    /// <summary>
    /// Gets or sets whether to use Mode B (true) or Mode A (false).
    /// </summary>
    public bool IsModeB { get; set; } = true;

    /// <summary>
    /// Gets or sets whether debug logging is enabled.
    /// </summary>
    public bool EnableDebugLogging
    {
        get => _enableDebugLogging;
        set => _enableDebugLogging = value;
    }

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
    public void UpdatePaddleState(bool leftPaddle, bool rightPaddle)
    {
        lock (_lock)
        {
            if (_enableDebugLogging)
                Console.WriteLine($"[IambicKeyer] UpdatePaddleState: L={leftPaddle} R={rightPaddle} State={_keyerState}");

            // Update current paddle states
            _currentLeftPaddleState = leftPaddle;
            _currentRightPaddleState = rightPaddle;

            // If keyer is idle and at least one paddle is pressed, start sending
            if (_keyerState == KeyerState.Idle && (leftPaddle || rightPaddle))
            {
                StartNextElement();
            }
            // If keyer is playing, update latches for newly pressed paddles
            else if (_keyerState == KeyerState.Playing)
            {
                // A paddle is "newly pressed" if it wasn't already pressed at element start
                if (leftPaddle && !_ditPaddleAtStart && !_iambicDitLatched)
                {
                    _iambicDitLatched = true;
                    if (_enableDebugLogging)
                        Console.WriteLine($"[IambicKeyer] Setting DIT latch");
                }
                if (rightPaddle && !_dahPaddleAtStart && !_iambicDahLatched)
                {
                    _iambicDahLatched = true;
                    if (_enableDebugLogging)
                        Console.WriteLine($"[IambicKeyer] Setting DAH latch");
                }
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
            if (_enableDebugLogging)
                Console.WriteLine($"[IambicKeyer] Stop called, going to Idle");

            // Send radio key-up if needed
            SendRadioKey(false);

            // Reset state
            _keyerState = KeyerState.Idle;
            _iambicDitLatched = false;
            _iambicDahLatched = false;
            _ditPaddleAtStart = false;
            _dahPaddleAtStart = false;
        }
    }

    /// <summary>
    /// Called when a tone starts (including queued tones). Send radio key-down.
    /// </summary>
    private void OnToneStart()
    {
        lock (_lock)
        {
            if (_enableDebugLogging)
                Console.WriteLine($"[IambicKeyer] OnToneStart: Tone starting, sending radio key-down");

            // Send radio key-down
            SendRadioKey(true);
            _keyerState = KeyerState.Playing;
        }
    }

    /// <summary>
    /// Called when a tone completes. Send radio key-up and potentially queue next tone with the silence.
    /// </summary>
    private void OnToneComplete()
    {
        lock (_lock)
        {
            if (_enableDebugLogging)
                Console.WriteLine($"[IambicKeyer] OnToneComplete: Tone ended, checking if we should queue next tone");

            // Send radio key-up
            SendRadioKey(false);

            // Determine if we'll send another element after the silence
            int? nextToneDuration = DetermineNextToneDuration();

            if (nextToneDuration.HasValue)
            {
                bool isDit = (nextToneDuration.Value == _ditLength);

                if (_enableDebugLogging)
                    Console.WriteLine($"[IambicKeyer] Queueing next {(isDit ? "dit" : "dah")} ({nextToneDuration.Value}ms) to follow the silence");

                // Update paddle states for the queued element
                _ditPaddleAtStart = _currentLeftPaddleState;
                _dahPaddleAtStart = _currentRightPaddleState;
                _iambicDitLatched = false;
                _iambicDahLatched = false;

                // Track what we're queueing
                _lastElementWasDit = isDit;

                // Re-queue the silence with the next tone to follow
                _sidetoneGenerator?.QueueSilence(_ditLength, nextToneDuration.Value);
            }
        }
    }

    /// <summary>
    /// Called when silence completes with no queued tone. Determine and start next element or go idle.
    /// </summary>
    private void OnSilenceComplete()
    {
        lock (_lock)
        {
            if (_enableDebugLogging)
                Console.WriteLine($"[IambicKeyer] OnSilenceComplete: Silence ended, starting next element");

            StartNextElement();
        }
    }

    /// <summary>
    /// Determines what element to send next based on paddle states and latches,
    /// then starts it (or goes idle if nothing to send).
    /// </summary>
    private void StartNextElement()
    {
        int? nextDuration = DetermineNextToneDuration();

        if (nextDuration.HasValue)
        {
            bool isDit = (nextDuration.Value == _ditLength);

            // Record paddle states at element start
            _ditPaddleAtStart = _currentLeftPaddleState;
            _dahPaddleAtStart = _currentRightPaddleState;

            // Clear latches
            _iambicDitLatched = false;
            _iambicDahLatched = false;

            // Track what we're sending
            _lastElementWasDit = isDit;

            if (_enableDebugLogging)
                Console.WriteLine($"[IambicKeyer] Starting {(isDit ? "dit" : "dah")} ({nextDuration.Value}ms)");

            // Start tone (OnToneStart will fire and send radio key-down)
            _sidetoneGenerator?.StartTone(nextDuration.Value);
            // Queue the silence that will follow this tone
            _sidetoneGenerator?.QueueSilence(_ditLength);
        }
        else
        {
            // Nothing to send, go idle
            if (_enableDebugLogging)
                Console.WriteLine($"[IambicKeyer] No element to send, going idle");

            _keyerState = KeyerState.Idle;
            _ditPaddleAtStart = false;
            _dahPaddleAtStart = false;
        }
    }

    /// <summary>
    /// Determines what the next tone duration should be based on current paddle states.
    /// Returns null if no tone should be sent.
    /// </summary>
    private int? DetermineNextToneDuration()
    {
        bool sendDit = false;
        bool sendDah = false;

        // Determine next element using iambic logic
        if (_lastElementWasDit || _keyerState == KeyerState.Idle)
        {
            // After dit (or from idle): Priority 1 = opposite (dah), Priority 2 = same (dit), Priority 3 (ModeB) = opposite squeeze
            if (_currentRightPaddleState || _iambicDahLatched)
            {
                sendDah = true;
            }
            else if (_currentLeftPaddleState)
            {
                sendDit = true;
            }
            else if (IsModeB && _dahPaddleAtStart && !_currentRightPaddleState)
            {
                // Mode B: squeeze - dah was held at start but now released
                sendDah = true;
            }
        }
        else // was sending dah
        {
            // After dah: Priority 1 = opposite (dit), Priority 2 = same (dah), Priority 3 (ModeB) = opposite squeeze
            if (_currentLeftPaddleState || _iambicDitLatched)
            {
                sendDit = true;
            }
            else if (_currentRightPaddleState)
            {
                sendDah = true;
            }
            else if (IsModeB && _ditPaddleAtStart && !_currentLeftPaddleState)
            {
                // Mode B: squeeze - dit was held at start but now released
                sendDit = true;
            }
        }

        // Handle special case: both paddles pressed from idle = dit first
        if (_keyerState == KeyerState.Idle && _currentLeftPaddleState && _currentRightPaddleState)
        {
            sendDit = true;
            sendDah = false;
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
