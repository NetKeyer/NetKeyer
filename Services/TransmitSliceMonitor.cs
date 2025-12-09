using System;
using System.ComponentModel;
using Flex.Smoothlake.FlexLib;

namespace NetKeyer.Services;

// Debug flags
file static class TransmitSliceDebugFlags
{
    public const bool DEBUG_SLICE_MODE = false; // Set to true to enable transmit slice mode debug logging
}

public class TransmitModeChangedEventArgs : EventArgs
{
    public bool IsTransmitModeCW { get; set; }
}

public class TransmitSliceMonitor
{
    private Radio _connectedRadio;
    private uint _boundGuiClientHandle;
    private Slice _monitoredTransmitSlice;
    private bool _isTransmitModeCW = true;

    public bool IsTransmitModeCW => _isTransmitModeCW;
    public Slice TransmitSlice => _monitoredTransmitSlice;

    public event EventHandler<TransmitModeChangedEventArgs> TransmitModeChanged;

    public void AttachToRadio(Radio radio, uint clientHandle)
    {
        // Unsubscribe from old slice if present
        DetachFromSlice();

        _connectedRadio = radio;
        _boundGuiClientHandle = clientHandle;

        // Subscribe to radio's TransmitSlice property changes
        if (_connectedRadio != null)
        {
            _connectedRadio.PropertyChanged += Radio_PropertyChanged;
        }

        // Subscribe to transmit slice
        SubscribeToTransmitSlice();
    }

    public void Detach()
    {
        DetachFromSlice();

        if (_connectedRadio != null)
        {
            _connectedRadio.PropertyChanged -= Radio_PropertyChanged;
            _connectedRadio = null;
        }

        _boundGuiClientHandle = 0;
    }

    private void DetachFromSlice()
    {
        if (_monitoredTransmitSlice != null)
        {
            _monitoredTransmitSlice.PropertyChanged -= TransmitSlice_PropertyChanged;
            _monitoredTransmitSlice = null;
        }
    }

    private Slice FindOurTransmitSlice()
    {
        if (_connectedRadio == null || _boundGuiClientHandle == 0)
            return null;

        foreach (var slice in _connectedRadio.SliceList)
        {
            if (slice.IsTransmitSlice && slice.ClientHandle == _boundGuiClientHandle)
                return slice;
        }

        return null;
    }

    private void UpdateTransmitSliceMode()
    {
        var txSlice = FindOurTransmitSlice();

        if (txSlice != null)
        {
            string mode = txSlice.DemodMode?.ToUpper() ?? "USB";
            bool wasCW = _isTransmitModeCW;
            _isTransmitModeCW = (mode == "CW");

            if (TransmitSliceDebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] Slice {txSlice.Index} mode: {mode}, isCW: {_isTransmitModeCW}");

            if (wasCW != _isTransmitModeCW)
            {
                if (TransmitSliceDebugFlags.DEBUG_SLICE_MODE)
                    Console.WriteLine($"[TransmitSliceMode] Mode changed from {(wasCW ? "CW" : "non-CW")} to {(_isTransmitModeCW ? "CW" : "non-CW")}");

                // Notify listeners
                TransmitModeChanged?.Invoke(this, new TransmitModeChangedEventArgs { IsTransmitModeCW = _isTransmitModeCW });
            }
        }
        else
        {
            if (TransmitSliceDebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] No transmit slice for our client, defaulting to CW mode");

            bool wasCW = _isTransmitModeCW;
            _isTransmitModeCW = true; // Default to CW mode if no transmit slice

            if (wasCW != _isTransmitModeCW)
            {
                // Notify listeners
                TransmitModeChanged?.Invoke(this, new TransmitModeChangedEventArgs { IsTransmitModeCW = _isTransmitModeCW });
            }
        }
    }

    private void SubscribeToTransmitSlice()
    {
        // Unsubscribe from old slice if present
        DetachFromSlice();

        // Debug: Check radio and slices
        if (TransmitSliceDebugFlags.DEBUG_SLICE_MODE && _connectedRadio != null)
        {
            Console.WriteLine($"[TransmitSliceMode] Connected radio has {_connectedRadio.SliceList.Count} slices");
            Console.WriteLine($"[TransmitSliceMode] Our bound ClientHandle: {_boundGuiClientHandle}");
            Console.WriteLine($"[TransmitSliceMode] Radio's internal ClientHandle: {_connectedRadio.ClientHandle}");

            foreach (var slice in _connectedRadio.SliceList)
            {
                Console.WriteLine($"[TransmitSliceMode]   Slice {slice.Index}: IsTransmitSlice={slice.IsTransmitSlice}, ClientHandle={slice.ClientHandle}, Mode={slice.DemodMode}");
            }
        }

        // Subscribe to new slice using our own finder
        var txSlice = FindOurTransmitSlice();
        if (txSlice != null)
        {
            _monitoredTransmitSlice = txSlice;
            _monitoredTransmitSlice.PropertyChanged += TransmitSlice_PropertyChanged;

            if (TransmitSliceDebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] Subscribed to slice {_monitoredTransmitSlice.Index}");
        }
        else
        {
            if (TransmitSliceDebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] No transmit slice found for our client");
        }

        UpdateTransmitSliceMode();
    }

    private void TransmitSlice_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "DemodMode")
        {
            if (TransmitSliceDebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] DemodMode property changed");
            UpdateTransmitSliceMode();
        }
    }

    private void Radio_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        // Handle TransmitSlice changes (needs to be done outside UI thread)
        if (e.PropertyName == "TransmitSlice")
        {
            if (TransmitSliceDebugFlags.DEBUG_SLICE_MODE)
                Console.WriteLine($"[TransmitSliceMode] Radio TransmitSlice property changed");
            SubscribeToTransmitSlice();
        }
    }
}
