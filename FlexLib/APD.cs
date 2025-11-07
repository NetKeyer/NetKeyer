// ****************************************************************************
///*!	\file APD.cs
// *	\brief APD model
// *
// *	\copyright	Copyright 2025 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// */
// ****************************************************************************

using Flex.UiWpfFramework.Mvvm;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using AsyncAwaitBestPractices;

namespace Flex.Smoothlake.FlexLib;

public record EqualizerStatus(bool enable, string ant, double freq);

public class APD(Radio radio) : ObservableObject
{
    private Radio _radio = radio;
    private static readonly Queue _statusQueue = new();
    private static System.Threading.Timer _statusApplyTimer = null;

    public void ParseStatus(string s)
    {
        bool? activeSet = null;
        double freq = double.NaN;
        string ant = null;
        string[] words = s.Split(' ');
        if (words.Length == 0) { return; }

        foreach (string kv in words)
        {
            string[] tokens = kv.Split('=');
            if (tokens[0] == "equalizer_reset") // The only key for this status which is a boolean flag - it has no value.
            {
                Debug.WriteLine("Clearing all APD equalizers!");
                EqualizerActive = false;
                continue;
            }
            else if (tokens.Length != 2)
            {
                if (!string.IsNullOrEmpty(kv)) Debug.WriteLine($"APD::ParseStatus: Invalid key/value pair ({kv})");
                continue;
            }
            string key = tokens[0];
            string value = tokens[1];
            switch (key.ToLower())
            {
                case ("ant"):
                    {
                        ant = value;
                        break;
                    }
                case ("configurable"):
                    {
                        if (!byte.TryParse(value, out var temp))
                        {
                            Debug.WriteLine($"APD::ParseStatus - enable: Invalid value ({kv})");
                            continue;
                        }
                        _configurable = Convert.ToBoolean(temp);
                        RaisePropertyChanged(nameof(Configurable));
                        break;
                    }
                case ("enable"):
                    {
                        if (!byte.TryParse(value, out var temp))
                        {
                            Debug.WriteLine($"APD::ParseStatus - enable: Invalid value ({kv})");
                            continue;
                        }
                        var enabled = Convert.ToBoolean(temp);
                        if (_enabled == enabled)
                        {
                            break;
                        }
                        _enabled = enabled;
                        RaisePropertyChanged(nameof(Enabled));
                        break;
                    }
                case ("equalizer_active"):
                    {
                        if (!byte.TryParse(value, out var temp))
                        {
                            Debug.WriteLine($"APD::ParseStatus - equalizer_active: active value ({kv})");
                            continue;
                        }
                        activeSet = Convert.ToBoolean(temp);
                        break;
                    }
                case ("freq"):
                    {
                        if (!double.TryParse(value, out freq))
                        {
                            Debug.WriteLine($"APD::ParseStatus: Invalid frequency ({value})");
                            freq = double.NaN;
                            continue;
                        }
                        break;
                    }
                
                case "sample_index":
                    if (!int.TryParse(value, out int index))
                    {
                        Debug.WriteLine($"Invalid APD Index: {value}");
                        continue;
                    }
                        
                    if (GatherApdLogs) 
                    {
                        DownloadApdLog(index).SafeFireAndForget();
                    }
                        
                    break;
            }
        }

        // If we have a change to the active status of an equalizer for a given frequency + antenna, see if it applies to one of our slices.
        // Queue APD status messages and check them after an interval, so that rapid movements of the slice don't cause loss of sync.
        if (!(activeSet is null || double.IsNaN(freq) || String.IsNullOrEmpty(ant)))
        {
            QueueEqualizerActiveStatus(activeSet.Value, ant, freq);
        }
    }

    private bool _gatherApdLogs;
    public bool GatherApdLogs
    {
        get => _gatherApdLogs;

        set
        {
            if (_gatherApdLogs == value)
            {
                return;
            }

            _gatherApdLogs = value;
            RaisePropertyChanged(nameof(GatherApdLogs));
        }
    }
    
    private readonly string _apdLogDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\FlexRadio Systems\APD Logs\";
    private async Task DownloadApdLog(int index)
    {
        if (!_radio.IsAlphaLicensed)
        {
            return;
        }
            
        Debug.WriteLine($"Downloading APD log at index {index}");

        string portString;

        try
        {
            portString = await _radio.SendCommandAsync($"file download apd_log {index}");
        }
        catch (SmartSdrCommandErrorException ex)
        {
            Debug.WriteLine($"Failed to execute APD Download Command: {ex}");
            return;
        }
            
        if (!int.TryParse(portString, out int port))
        {
            Debug.WriteLine($"Invalid Port Number: {portString}");
            return;
        }

        var server = new TcpListener(IPAddress.Any, port);
        server.Start();

        Debug.WriteLine("Waiting for the APD Log download connection from the radio");
        using TcpClient client = await server.AcceptTcpClientAsync();
            
        Debug.WriteLine("Got a TCP connection from the radio");

        using NetworkStream stream = client.GetStream();

        Debug.WriteLine($"Downloading APD index {index}");
        Directory.CreateDirectory(_apdLogDirectory);
        var apdLogFile = $@"{_apdLogDirectory}\apd_log-{index}.zip";
        using FileStream outputFile = File.Open(apdLogFile, FileMode.Create);
        await stream.CopyToAsync(outputFile);

        Debug.WriteLine($"APD index {index} complete");

        server.Stop();
    }

    public void EqualizerActiveStatusApplyTimerTaskFunction(object state)
    {
        while (_statusQueue.Count != 0)
        {
            EqualizerStatus temp = (EqualizerStatus)_statusQueue.Dequeue();
            Debug.WriteLine($"Parsing queued eq status - enable={temp.enable}, ant={temp.ant}, freq={temp.freq}.");
            ApplyEqualizerActiveStatus(temp.enable, temp.ant, temp.freq);
        }
    }

    private void ApplyEqualizerActiveStatus(bool enable, string ant, double freq)
    {
        if (ant == null || _radio == null || _radio.SliceList == null)
        {
            return;
        }

        // If we have a transmit-enabled slice, check that for a match on antenna/frequency.
        // Or just use the current active slice.
        Slice temp = _radio.SliceList.FirstOrDefault(s => s.IsTransmitSlice) ?? _radio.ActiveSlice;

        if (temp is null)
        {
            Debug.WriteLine($"APD::ApplyEqualizerActiveStatus: No slices to apply to.");
            return;
        }

        // The max precision of the APD frequency given is in Hz, but the slice frequency may be sub-Hz.
        // Round the slice frequency for this comparison.
        double roundedFreq = Math.Round(temp.Freq, 6);
        if (ant.Equals(temp.TXAnt, StringComparison.OrdinalIgnoreCase) && freq == roundedFreq)
        {
            Debug.WriteLine($"APD::ApplyEqualizerActiveStatus: Updating APD status for slice {temp.Index}, freq={freq}, ant={ant}.");
            EqualizerActive = enable;
        }
        else
        {
            Debug.WriteLine($"APD::ApplyEqualizerActiveStatus: No matching slice with freq={freq}, ant={ant}. Current freq={temp.Freq}.");
        }
    }

    private void QueueEqualizerActiveStatus(bool enable, string ant, double freq)
    {
        if (null == _statusApplyTimer)
        {
            _statusApplyTimer = new System.Threading.Timer(EqualizerActiveStatusApplyTimerTaskFunction, null, 100, 150);
        }
        EqualizerStatus temp = new(enable, ant, freq);
        _statusQueue.Enqueue(temp);
    }

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            _radio.SendCommand("apd enable=" + Convert.ToByte(_enabled));
            RaisePropertyChanged(nameof(Enabled));
        }
    }

    private bool _configurable;
    public bool Configurable
    {
        get => _configurable;
        internal set
        {
            if (_configurable == value) return;
            _configurable = value;
            RaisePropertyChanged(nameof(Configurable));
        }
    }

    private bool _equalizerActive;
    public bool EqualizerActive
    {
        get => _equalizerActive;
        set
        {
            if (_equalizerActive == value) return;
            _equalizerActive = value;
            RaisePropertyChanged(nameof(EqualizerActive));
            RaisePropertyChanged(nameof(EqualizerCalibrating));
        }
    }

    public bool EqualizerCalibrating => !EqualizerActive;

    private bool _available;
    public bool Available
    {
        get => _available;
        set
        {
            if (_available == value) return;
            _available = value;
            RaisePropertyChanged(nameof(Available));
        }
    }
}
