using System;
using Flex.Smoothlake.FlexLib;
using NetKeyer.Audio;
using NetKeyer.Keying;
using NetKeyer.Helpers;

namespace NetKeyer.Services;

public class KeyingController
{
    private Radio _connectedRadio;
    private uint _boundGuiClientHandle;
    private ISidetoneGenerator _sidetoneGenerator;
    private IambicKeyer _iambicKeyer;
    private CWMonitor _cwMonitor;
    private bool _isTransmitModeCW = true;
    private bool _isSidetoneOnlyMode = false;
    private bool _isIambicMode = true;

    // Initialization parameters
    private Func<string> _timestampGenerator;
    private Action<bool, string, uint> _cwKeyCallback;

    // Track previous paddle states for edge detection
    private bool _previousLeftPaddleState = false;
    private bool _previousRightPaddleState = false;
    private bool _previousStraightKeyState = false;
    private bool _previousPttState = false;

    public KeyingController(ISidetoneGenerator sidetoneGenerator)
    {
        _sidetoneGenerator = sidetoneGenerator;
        _cwMonitor = new CWMonitor();
        DebugLogger.Log("cwmonitor", "CWMonitor instance created in KeyingController");
    }

    public void Initialize(uint guiClientHandle, Func<string> timestampGenerator, Action<bool, string, uint> cwKeyCallback)
    {
        _boundGuiClientHandle = guiClientHandle;
        _timestampGenerator = timestampGenerator;
        _cwKeyCallback = cwKeyCallback;

        // Create a wrapper callback that notifies CW Monitor
        Action<bool, string, uint> cwKeyCallbackWithMonitor = (state, timestamp, handle) =>
        {
            // Notify CW Monitor (iambic mode)
            if (_cwMonitor != null && _cwMonitor.Enabled)
            {
                if (state)
                {
                    _cwMonitor.OnKeyDown();
                    DebugLogger.Log("cwmonitor", "Iambic: OnKeyDown");
                }
                else
                {
                    _cwMonitor.OnKeyUp();
                    DebugLogger.Log("cwmonitor", "Iambic: OnKeyUp");
                }
            }

            // Call original callback
            cwKeyCallback?.Invoke(state, timestamp, handle);
        };

        // Initialize iambic keyer with wrapped callback
        _iambicKeyer = new IambicKeyer(
            _sidetoneGenerator,
            _boundGuiClientHandle,
            timestampGenerator,
            cwKeyCallbackWithMonitor
        );

        // Note: CW Monitor will be enabled by the ViewModel based on saved settings
        DebugLogger.Log("cwmonitor", "KeyingController initialized, CWMonitor ready");
    }

    public void SetRadio(Radio radio, bool isSidetoneOnly = false)
    {
        _connectedRadio = radio;
        _isSidetoneOnlyMode = isSidetoneOnly;
    }

    public void SetSidetoneGenerator(ISidetoneGenerator sidetoneGenerator)
    {
        _sidetoneGenerator = sidetoneGenerator;

        // Update iambic keyer's sidetone generator without recreating the keyer
        _iambicKeyer?.UpdateSidetoneGenerator(_sidetoneGenerator);
    }

    public void SetTransmitMode(bool isCW)
    {
        _isTransmitModeCW = isCW;
    }

    public void SetKeyingMode(bool isIambic, bool isModeB)
    {
        _isIambicMode = isIambic;

        if (_iambicKeyer != null)
        {
            _iambicKeyer.IsModeB = isModeB;
        }

        // Stop keyer when switching to straight key mode
        if (!isIambic)
        {
            _iambicKeyer?.Stop();
        }
    }

    public void SetSpeed(int wpm)
    {
        _iambicKeyer?.SetWpm(wpm);
    }

    public void HandlePaddleStateChange(bool leftPaddle, bool rightPaddle, bool straightKey, bool ptt)
    {
        // Handle keying based on mode and transmit slice mode
        if (_connectedRadio != null && _boundGuiClientHandle != 0)
        {
            if (_isTransmitModeCW)
            {
                // CW mode - use paddle/straight key keying
                if (_isIambicMode)
                {
                    // Iambic mode - use paddle inputs
                    _iambicKeyer?.UpdatePaddleState(leftPaddle, rightPaddle);
                }
                else
                {
                    // Straight key mode - use straight key input
                    // (InputDeviceManager sets this to OR of both paddles for serial input)
                    if (straightKey != _previousStraightKeyState)
                    {
                        SendCWKey(straightKey);
                    }
                }
            }
            else
            {
                // Non-CW mode - use PTT keying
                if (ptt != _previousPttState)
                {
                    SendPTT(ptt);
                }
            }
        }
        else if (_isSidetoneOnlyMode)
        {
            // Sidetone-only mode - still run keyer logic, just no radio commands
            if (_isIambicMode)
            {
                _iambicKeyer?.UpdatePaddleState(leftPaddle, rightPaddle);
            }
            else
            {
                // Straight key mode - use straight key input
                // (InputDeviceManager sets this to OR of both paddles for serial input)
                if (straightKey != _previousStraightKeyState)
                {
                    SendCWKey(straightKey);
                }
            }
        }

        // Update previous states
        _previousLeftPaddleState = leftPaddle;
        _previousRightPaddleState = rightPaddle;
        _previousStraightKeyState = straightKey;
        _previousPttState = ptt;
    }

    public void Stop()
    {
        _iambicKeyer?.Stop();
        if (_cwMonitor != null)
        {
            _cwMonitor.Enabled = false;
        }
    }

    // Expose CWMonitor properties
    public CWMonitor CWMonitor => _cwMonitor;

    public string DecodedCW => _cwMonitor?.DecodedBuffer ?? "";

    public void ResetCWMonitorStats()
    {
        _cwMonitor?.ResetStatistics();
    }

    private void SendCWKey(bool state)
    {
        // Notify CW Monitor (straight key mode)
        if (_cwMonitor != null && _cwMonitor.Enabled)
        {
            if (state)
            {
                _cwMonitor.OnKeyDown();
                DebugLogger.Log("cwmonitor", "Straight key: OnKeyDown");
            }
            else
            {
                _cwMonitor.OnKeyUp();
                DebugLogger.Log("cwmonitor", "Straight key: OnKeyUp");
            }
        }

        // Control sidetone
        if (state)
        {
            _sidetoneGenerator?.Start();
        }
        else
        {
            _sidetoneGenerator?.Stop();
        }

        // Send to radio if connected (not in sidetone-only mode)
        if (_connectedRadio != null && _boundGuiClientHandle != 0)
        {
            try
            {
                // Generate timestamp
                long timestamp = Environment.TickCount64 % 65536;
                string timestampStr = timestamp.ToString("X4");

                _connectedRadio.CWKey(state, timestampStr, _boundGuiClientHandle);
            }
            catch { }
        }
    }

    private void SendPTT(bool state)
    {
        if (_connectedRadio != null)
        {
            try
            {
                _connectedRadio.Mox = state;
            }
            catch { }
        }
    }

    public void ResetState()
    {
        _previousLeftPaddleState = false;
        _previousRightPaddleState = false;
        _previousStraightKeyState = false;
        _previousPttState = false;
    }
}
