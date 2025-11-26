using System;
using System.Threading.Tasks;
using NetKeyer.Audio;

namespace NetKeyer.Keying;

/// <summary>
/// Iambic keyer implementation supporting Mode A and Mode B.
/// Handles timing, paddle latching, and element generation for iambic keying.
/// </summary>
public class IambicKeyer
{
    private readonly object _lock = new object();
    private readonly Func<string> _getTimestamp;
    private readonly Action<bool, string, uint> _sendRadioKey;
    private readonly ISidetoneGenerator _sidetoneGenerator;
    private readonly uint _radioClientHandle;

    private bool _enableDebugLogging = false;
    private bool _iambicKeyDown = false;
    private bool _iambicDitLatched = false;
    private bool _iambicDahLatched = false;
    private bool _ditPaddleAtStart = false;
    private bool _dahPaddleAtStart = false;
    private bool _currentLeftPaddleState = false;
    private bool _currentRightPaddleState = false;
    private int _ditLength = 60; // milliseconds
    private KeyerState _keyerState = KeyerState.Idle;

    private enum KeyerState { Idle, SendingDit, SendingDah, SpaceAfterDit, SpaceAfterDah }

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
                Console.WriteLine($"[IambicKeyer] UpdatePaddleState: L={leftPaddle} R={rightPaddle} State={_keyerState} DitLatch={_iambicDitLatched} DahLatch={_iambicDahLatched}");

            // Update current paddle states
            _currentLeftPaddleState = leftPaddle;
            _currentRightPaddleState = rightPaddle;

            // If keyer is idle and at least one paddle is pressed, start sending
            if (_keyerState == KeyerState.Idle && (leftPaddle || rightPaddle))
            {
                // Determine which element to send
                if (leftPaddle && rightPaddle)
                {
                    // Both paddles pressed - send dit first (standard behavior)
                    if (_enableDebugLogging)
                        Console.WriteLine($"[IambicKeyer] Starting from idle with BOTH paddles - sending dit");
                    StartElement(KeyerState.SendingDit);
                }
                else if (leftPaddle)
                {
                    if (_enableDebugLogging)
                        Console.WriteLine($"[IambicKeyer] Starting from idle with LEFT paddle - sending dit");
                    StartElement(KeyerState.SendingDit);
                }
                else
                {
                    if (_enableDebugLogging)
                        Console.WriteLine($"[IambicKeyer] Starting from idle with RIGHT paddle - sending dah");
                    StartElement(KeyerState.SendingDah);
                }
            }
            else if (_keyerState != KeyerState.Idle)
            {
                // Keyer is running - update latches for newly pressed paddles
                // A paddle is "newly pressed" if it wasn't already pressed at element start
                if (leftPaddle && !_ditPaddleAtStart && !_iambicDitLatched)
                {
                    _iambicDitLatched = true;
                    if (_enableDebugLogging)
                        Console.WriteLine($"[IambicKeyer] Setting DIT latch (newly pressed)");

                    // Check if we should queue a dit for after the current silence
                    CheckAndQueueNextElement();
                }
                if (rightPaddle && !_dahPaddleAtStart && !_iambicDahLatched)
                {
                    _iambicDahLatched = true;
                    if (_enableDebugLogging)
                        Console.WriteLine($"[IambicKeyer] Setting DAH latch (newly pressed)");

                    // Check if we should queue a dah for after the current silence
                    CheckAndQueueNextElement();
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

            // Ensure key is up
            if (_iambicKeyDown)
            {
                SendCWKey(false);
                _iambicKeyDown = false;
            }

            // Reset state
            _keyerState = KeyerState.Idle;
            _iambicDitLatched = false;
            _iambicDahLatched = false;
            _ditPaddleAtStart = false;
            _dahPaddleAtStart = false;
        }
    }

    private void StartElement(KeyerState elementState)
    {
        bool isDit = (elementState == KeyerState.SendingDit);
        if (_enableDebugLogging)
            Console.WriteLine($"[IambicKeyer] StartElement: {(isDit ? "dit" : "dah")} - recording paddle states and clearing latches");

        // Record which paddles are currently pressed at element start
        _ditPaddleAtStart = _currentLeftPaddleState;
        _dahPaddleAtStart = _currentRightPaddleState;

        // Clear latches - we'll track NEW paddle presses during this element
        _iambicDitLatched = false;
        _iambicDahLatched = false;

        // Start sending the current element (dit or dah) with precise duration
        _keyerState = elementState;
        int elementDuration = isDit ? _ditLength : (_ditLength * 3);

        // Send the tone to both sidetone and radio
        SendCWKey(true, elementDuration);
        _iambicKeyDown = true;

        // Send key-up to radio after the tone duration
        // (Sidetone will handle its own timing)
        Task.Delay(elementDuration).ContinueWith(_ =>
        {
            lock (_lock)
            {
                // Send key-up to radio
                if (_sendRadioKey != null && _radioClientHandle != 0)
                {
                    string timestamp = _getTimestamp();
                    _sendRadioKey(false, timestamp, _radioClientHandle);
                }
                _iambicKeyDown = false;

                // Transition to space state
                _keyerState = isDit ? KeyerState.SpaceAfterDit : KeyerState.SpaceAfterDah;

                // Queue the inter-element space in the sidetone
                _sidetoneGenerator?.QueueSilence(_ditLength);

                // Schedule callback for when silence completes
                Task.Delay(_ditLength).ContinueWith(__ =>
                {
                    lock (_lock)
                    {
                        OnSilenceComplete();
                    }
                });
            }
        });
    }

    private void CheckAndQueueNextElement()
    {
        // Only queue if we're in a space state
        if (_keyerState != KeyerState.SpaceAfterDit && _keyerState != KeyerState.SpaceAfterDah)
            return;

        bool wasSendingDit = (_keyerState == KeyerState.SpaceAfterDit);
        KeyerState? nextState = DetermineNextElement(wasSendingDit);

        if (nextState.HasValue)
        {
            bool nextIsDit = (nextState.Value == KeyerState.SendingDit);
            int nextDuration = nextIsDit ? _ditLength : (_ditLength * 3);

            if (_enableDebugLogging)
                Console.WriteLine($"[IambicKeyer] CheckAndQueue: Queueing next {(nextIsDit ? "dit" : "dah")} after current silence");

            // Queue the next tone to start after the current silence
            _sidetoneGenerator?.StartTone(nextDuration);
        }
    }

    private void OnSilenceComplete()
    {
        if (_enableDebugLogging)
            Console.WriteLine($"[IambicKeyer] OnSilenceComplete: Silence ended, checking for next element");

        bool wasSendingDit = (_keyerState == KeyerState.SpaceAfterDit);
        KeyerState? nextState = DetermineNextElement(wasSendingDit);

        if (nextState.HasValue)
        {
            if (_enableDebugLogging)
                Console.WriteLine($"[IambicKeyer] OnSilenceComplete: Starting next element: {nextState.Value}");
            StartElement(nextState.Value);
        }
        else
        {
            if (_enableDebugLogging)
                Console.WriteLine($"[IambicKeyer] OnSilenceComplete: No next element, stopping");
            Stop();
        }
    }

    private KeyerState? DetermineNextElement(bool wasSendingDit)
    {
        KeyerState? nextState = null;
        string reason = "";

        if (wasSendingDit)
        {
            // Priority 1: opposite paddle pressed or latched
            if (_currentRightPaddleState || _iambicDahLatched)
            {
                nextState = KeyerState.SendingDah;
                reason = _currentRightPaddleState ? "dah pressed" : "dah latched";
            }
            // Priority 2: same paddle still pressed
            else if (_currentLeftPaddleState)
            {
                nextState = KeyerState.SendingDit;
                reason = "dit still pressed";
            }
            // Priority 3 (Mode B): opposite was held from start but now released
            else if (IsModeB && _dahPaddleAtStart && !_currentRightPaddleState)
            {
                nextState = KeyerState.SendingDah;
                reason = "ModeB: dah held then released";
            }
        }
        else // was sending dah
        {
            // Priority 1: opposite paddle pressed or latched
            if (_currentLeftPaddleState || _iambicDitLatched)
            {
                nextState = KeyerState.SendingDit;
                reason = _currentLeftPaddleState ? "dit pressed" : "dit latched";
            }
            // Priority 2: same paddle still pressed
            else if (_currentRightPaddleState)
            {
                nextState = KeyerState.SendingDah;
                reason = "dah still pressed";
            }
            // Priority 3 (Mode B): opposite was held from start but now released
            else if (IsModeB && _ditPaddleAtStart && !_currentLeftPaddleState)
            {
                nextState = KeyerState.SendingDit;
                reason = "ModeB: dit held then released";
            }
        }

        if (_enableDebugLogging && nextState.HasValue)
            Console.WriteLine($"[IambicKeyer] DetermineNext: Next={nextState.Value}, Reason={reason}");

        return nextState;
    }

    private void SendCWKey(bool state, int? durationMs = null)
    {
        // Control local sidetone
        if (state)
        {
            if (durationMs.HasValue)
                _sidetoneGenerator?.StartTone(durationMs.Value);
            else
                _sidetoneGenerator?.Start();
        }
        else
        {
            _sidetoneGenerator?.Stop();
        }

        // Send to radio (if available)
        if (_sendRadioKey != null && _radioClientHandle != 0)
        {
            string timestamp = _getTimestamp();
            _sendRadioKey(state, timestamp, _radioClientHandle);
        }
    }
}
