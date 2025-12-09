using System;
using System.ComponentModel;
using Avalonia.Threading;
using Flex.Smoothlake.FlexLib;

namespace NetKeyer.Services;

public class RadioSettingChangedEventArgs : EventArgs
{
    public string PropertyName { get; set; }
    public object Value { get; set; }
}

public class RadioSettingsSynchronizer
{
    private Radio _connectedRadio;
    private bool _updatingFromRadio = false;

    public event EventHandler<RadioSettingChangedEventArgs> SettingChangedFromRadio;

    public void AttachToRadio(Radio radio)
    {
        DetachFromRadio();

        _connectedRadio = radio;

        if (_connectedRadio != null)
        {
            _connectedRadio.PropertyChanged += Radio_PropertyChanged;
        }
    }

    public void DetachFromRadio()
    {
        if (_connectedRadio != null)
        {
            _connectedRadio.PropertyChanged -= Radio_PropertyChanged;
            _connectedRadio = null;
        }
    }

    public void ApplyInitialSettingsFromRadio()
    {
        if (_connectedRadio == null)
            return;

        try
        {
            _updatingFromRadio = true;
            try
            {
                RaiseSettingChanged("CWSpeed", _connectedRadio.CWSpeed);
                RaiseSettingChanged("CWPitch", _connectedRadio.CWPitch);
                RaiseSettingChanged("TXCWMonitorGain", _connectedRadio.TXCWMonitorGain);
            }
            finally
            {
                _updatingFromRadio = false;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Settings error: {ex.Message}", ex);
        }
    }

    public void SyncCwSpeedToRadio(int value)
    {
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                _connectedRadio.CWSpeed = value;
            }
            catch { }
        }
    }

    public void SyncCwPitchToRadio(int value)
    {
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                _connectedRadio.CWPitch = value;
            }
            catch { }
        }
    }

    public void SyncSidetoneVolumeToRadio(int value)
    {
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                _connectedRadio.TXCWMonitorGain = value;
            }
            catch { }
        }
    }

    public void SyncIambicModeToRadio(bool isIambic)
    {
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                _connectedRadio.CWIambic = isIambic;
            }
            catch { }
        }
    }

    public void SyncIambicModeBToRadio(bool isModeB)
    {
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                if (isModeB)
                {
                    // Set Mode B - this sends "cw mode 1"
                    _connectedRadio.CWIambicModeB = true;
                    // Also explicitly clear Mode A
                    _connectedRadio.CWIambicModeA = false;
                }
                else
                {
                    // Set Mode A - this sends "cw mode 0"
                    _connectedRadio.CWIambicModeA = true;
                    // Also explicitly clear Mode B
                    _connectedRadio.CWIambicModeB = false;
                }
            }
            catch { }
        }
    }

    public void SyncSwapPaddlesToRadio(bool swap)
    {
        if (_connectedRadio != null && !_updatingFromRadio)
        {
            try
            {
                _connectedRadio.CWSwapPaddles = swap;
            }
            catch { }
        }
    }

    private void Radio_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        // Dispatch all UI updates to the UI thread
        Dispatcher.UIThread.Post(() =>
        {
            if (_connectedRadio == null)
                return;

            _updatingFromRadio = true;
            try
            {
                switch (e.PropertyName)
                {
                    case "CWSpeed":
                        RaiseSettingChanged("CWSpeed", _connectedRadio.CWSpeed);
                        break;

                    case "CWPitch":
                        RaiseSettingChanged("CWPitch", _connectedRadio.CWPitch);
                        break;

                    case "TXCWMonitorGain":
                        RaiseSettingChanged("TXCWMonitorGain", _connectedRadio.TXCWMonitorGain);
                        break;

                    case "CWIambic":
                        RaiseSettingChanged("CWIambic", _connectedRadio.CWIambic);
                        break;

                    case "CWIambicModeB":
                    case "CWIambicModeA":
                        RaiseSettingChanged("CWIambicModeB", _connectedRadio.CWIambicModeB);
                        break;

                    case "CWSwapPaddles":
                        RaiseSettingChanged("CWSwapPaddles", _connectedRadio.CWSwapPaddles);
                        break;
                }
            }
            finally
            {
                _updatingFromRadio = false;
            }
        });
    }

    private void RaiseSettingChanged(string propertyName, object value)
    {
        SettingChangedFromRadio?.Invoke(this, new RadioSettingChangedEventArgs
        {
            PropertyName = propertyName,
            Value = value
        });
    }
}
