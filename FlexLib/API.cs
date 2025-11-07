// ****************************************************************************
///*!	\file API.cs
// *	\brief Core FlexLib source
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2012-03-05
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Timers;
using Flex.Util;

namespace Flex.Smoothlake.FlexLib;

public class API
{
    private const uint FLEX_OUI = 0x1C2D;
    public const double RADIOLIST_TIMEOUT_SECONDS = 17.0;

    private readonly struct RadioInfo(Radio radio)
    {
        public Radio Radio { get; } = radio;
        public Stopwatch Timer { get; } = Stopwatch.StartNew();
    }

        
    private static readonly ConcurrentDictionary<string, RadioInfo> RadioDictionary = new ();
    private static readonly ImmutableList<string> FilterSerial = ProcessFilterFile();

    /// <summary>
    /// Sets the name of the program that is using this API
    /// </summary>
    public static string ProgramName { get; set; }

    /// <summary>
    /// Sets whether the program using this API is a GUI
    /// </summary>
    public static bool IsGUI { get; set; } = false;

    private static bool _logDiscovery;
    private static bool _logDisconnect;

    private static bool _initialized;
    private static readonly object InitObj = new ();
        
    private static readonly Timer CleanupTimer = new (1000);

    /// <summary>
    /// Contains a list of discovered Radios on the network
    /// </summary>
    // TODO: This should be an immutable list, but that changes the API
    public static List<Radio> RadioList => RadioDictionary.Values.Select(ri => ri.Radio).ToList();
        
    /// <summary>
    /// Creates a UDP socket, listens for new radios on the network, and adds them to the RadioList
    /// </summary>
    public static void Init()
    {
        // ensure that the initialized variable is atomically set here (i.e. only let one instance through here)
        lock (InitObj)
        {
            if (_initialized)
                return;
                
            _initialized = true;

            var logEnableFile = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) +
                                @"\FlexRadio Systems\log_discovery.txt";
            _logDiscovery = File.Exists(logEnableFile);

            logEnableFile = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) +
                            @"\FlexRadio Systems\log_disconnect.txt";
            _logDisconnect = File.Exists(logEnableFile);

            LogDiscovery("API::Init()");

            Discovery.RadioDiscovered += Discovery_RadioDiscovered;
            Discovery.Start();

            WanServer.WanRadioRadioListRecieved += WanServer_WanRadioRadioListReceived;

            CleanupTimer.AutoReset = true;
            CleanupTimer.Elapsed += RadioListMaid;
            CleanupTimer.Enabled = true;
        }
    }

    public static void CloseSession()
    {
        Discovery.Stop();
        foreach (var radio in RadioList)
        {
            radio.Updating = false;
            RemoveRadio(radio);
            LogDisconnect($"API::CloseSession({radio})--Application is closing");
        }

        _initialized = false;
    }

    private static void RadioListMaid(object source, ElapsedEventArgs args)
    {
        var removeList = RadioDictionary.Values.Where(i =>
            i.Radio is {Updating: false, Connected: false} &&
            i.Timer.Elapsed.TotalSeconds > RADIOLIST_TIMEOUT_SECONDS).Select(i => i.Radio);

        // now loop through the remove list and take action
        foreach (var r in removeList)
        {
            RemoveRadio(r);
            LogDisconnect($"API::CleanupRadioList_ThreadFunction({r})--Timeout waiting on Discovery");
        }
    }

    private static ImmutableList<string> ProcessFilterFile()
    {
        var devFile = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\FlexRadio Systems\filter.txt";
        if (!File.Exists(devFile)) 
            return ImmutableList<string>.Empty;

        using var reader = File.OpenText(devFile);
        return reader.ReadToEnd().Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToImmutableList();
    }

    private static void RefreshRadio(Radio radio, Radio discoveredRadio)
    {
        LogDiscovery($"2 API::Discovery_RadioDiscovered({discoveredRadio}) - IP/Model/Serial match found in list");
        var versionOne = FlexVersion.Parse("1.0.0.0");
        if (radio.DiscoveryProtocolVersion <= versionOne && discoveredRadio.DiscoveryProtocolVersion > versionOne)
        {
            LogDiscovery(
                $"3 API::Discovery_RadioDiscovered({discoveredRadio}) - newer protocol, updating radio info");
            radio.DiscoveryProtocolVersion = discoveredRadio.DiscoveryProtocolVersion;
            radio.Callsign = discoveredRadio.Callsign;
            radio.Nickname = discoveredRadio.Nickname;
            radio.Serial = discoveredRadio.Serial;
        }

        if (discoveredRadio.Version != radio.Version)
        {
            LogDiscovery($"4 API::Discovery_RadioDiscovered({discoveredRadio}) - updating radio version");
            Debug.WriteLine($"Version Updated-{radio}");
            radio.Version = discoveredRadio.Version;
            radio.Updating = false;
        }

        // update the status if this is a newer discovery version
        if (discoveredRadio.DiscoveryProtocolVersion > versionOne)
        {
            if (discoveredRadio.Status == "Available" && radio.Status == "Updating")
            {
                LogDiscovery($"5 API::Discovery_RadioDiscovered({discoveredRadio}) - Radio coming out of update");
                radio.Updating = false;
            }

            if (radio.Status != discoveredRadio.Status)
            {
                LogDiscovery($"5 API::Discovery_RadioDiscovered({discoveredRadio}) - update radio status - {discoveredRadio.Status}");
                radio.Status = discoveredRadio.Status;
            }
                
            radio.GuiClientIPs = discoveredRadio.GuiClientIPs;
            radio.GuiClientHosts = discoveredRadio.GuiClientHosts;
            radio.GuiClientStations = discoveredRadio.GuiClientStations;
        }
        
        radio.IsInternetConnected = discoveredRadio.IsInternetConnected;
        radio.MaxLicensedVersion = discoveredRadio.MaxLicensedVersion;
        radio.RequiresAdditionalLicense = discoveredRadio.RequiresAdditionalLicense;
        radio.FrontPanelMacAddress = discoveredRadio.FrontPanelMacAddress;
        radio.RadioLicenseId = discoveredRadio.RadioLicenseId;
        radio.Callsign = discoveredRadio.Callsign;
        radio.Nickname = discoveredRadio.Nickname;
        radio.LicensedClients = discoveredRadio.LicensedClients;
        radio.AvailableClients = discoveredRadio.AvailableClients;
        radio.MaxPanadapters = discoveredRadio.MaxPanadapters;
        radio.AvailablePanadapters = discoveredRadio.AvailablePanadapters;
        radio.MaxSlices = discoveredRadio.MaxSlices;
        radio.AvailableSlices = discoveredRadio.AvailableSlices;
        radio.ExternalPortLink = discoveredRadio.ExternalPortLink;

        if (!radio.IP.Equals(discoveredRadio.IP))
        {
            radio.IP = discoveredRadio.IP;
            OnRadioChangedIpEventHander(discoveredRadio);
        }

        radio.UpdateGuiClientsList(newGuiClients: discoveredRadio.GuiClients);
    }

    private static void Discovery_RadioDiscovered(Radio discoveredRadio)
    {
        if (FilterSerial.Count > 0 &&
            FilterSerial.FirstOrDefault(f => discoveredRadio.Serial.Contains(f)) == null)
            return;

        // keep the radio alive in the list if it exists
        if (RadioDictionary.TryGetValue(discoveredRadio.Serial, out var radioEntry))
        {
            radioEntry.Timer.Restart();
        }

        // If we already have a radio in the list, just refresh it.
        if (RadioDictionary.TryGetValue(discoveredRadio.Serial, out var ri))
        {
            var radio = ri.Radio;
            // TODO: Should this ever really happen?
            if (radio.Model != discoveredRadio.Model || radio.Serial != discoveredRadio.Serial)
                return;

            RefreshRadio(radio, discoveredRadio);
            return;
        }

        Debug.WriteLine($"Discovered {discoveredRadio}");
        LogDiscovery($"6 API::Discovery_RadioDiscovered({discoveredRadio}) - Add radio to list");

        RadioDictionary.TryAdd(discoveredRadio.Serial, new RadioInfo(discoveredRadio));

        OnRadioAddedEventHandler(discoveredRadio);
    }

    private static void WanServer_WanRadioRadioListReceived(List<Radio> radios)
    {
        OnWanListReceivedEventHandler(radios);
    }

    public delegate void WanListReceivedEventHandler(List<Radio> radios);
    /// <summary>
    /// This event fires when a new radio on the network has been detected
    /// </summary>
    public static event WanListReceivedEventHandler WanListReceived;

    private static void OnWanListReceivedEventHandler(List<Radio> radios)
    {
        LogDiscovery($"8 API::OnWanListReceivedEventHandler({radios})");

        var filteredRadios = FilterSerial.Count > 0
            ? radios.Where(r => FilterSerial.Any(f => r.Serial.Contains(f))).ToList()
            : radios;

        WanListReceived?.Invoke(filteredRadios);
    }

    public delegate void RadioAddedEventHandler(Radio radio);
    /// <summary>
    /// This event fires when a new radio on the network has been detected
    /// </summary>
    public static event RadioAddedEventHandler RadioAdded;

    private static void OnRadioAddedEventHandler(Radio radio)
    {
        LogDiscovery($"7 API::OnRadioAddedEventHandler({radio})");
        RadioAdded?.Invoke(radio);
    }

    public delegate void RadioRemovedEventHandler(Radio radio);
    public static event RadioRemovedEventHandler RadioRemoved;

    private static void OnRadioRemovedEventHandler(Radio radio)
    {
        LogDiscovery($"8 API::OnRadioRemovedEventHandler({radio})");
        RadioRemoved?.Invoke(radio);
    }

    public delegate void RadioChangedIpEventHandler(Radio radio);

    public static event RadioChangedIpEventHandler RadioChangedIp;

    private static void OnRadioChangedIpEventHander(Radio radio)
    {
        RadioChangedIp?.Invoke(radio);
    }

    internal static void RemoveRadio(Radio radio)
    {
        LogDiscovery($"9 API::RemoveRadio({radio})");
        if (radio.Updating) 
            return; // don't remove the radio if we're just updating

        if (!RadioDictionary.TryRemove(radio.Serial, out var info))
            return;

        info.Timer.Stop();
        OnRadioRemovedEventHandler(radio);

        // disconnect the radio object
        if (radio.Connected)
            radio.Disconnect();
    }
        
    private static readonly string DisconnectLogPathName = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                                                           + @"\FlexRadio Systems\LogFiles\SSDR_Disconnect.log";
    private static readonly string DiscoveryLogPathName = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                                                          + @"\FlexRadio Systems\LogFiles\SSDR_Discovery.log";

    private static void LogToFile(string pathName, string msg)
    {
        try
        {
            using var writer = new StreamWriter(pathName, true);
            var line = $"{DateTime.Now.ToShortDateString()} {DateTime.Now:HH:mm:ss} {AppDomain.CurrentDomain.FriendlyName} {msg}";
            writer.WriteLine(line);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error writing to {pathName}: {ex}");
        }
    }

    private static void LogDiscovery(string msg)
    {
        if(!_logDiscovery) 
            return;

        LogToFile(DiscoveryLogPathName, msg);
    }

    internal static void LogDisconnect(string msg)
    {
        if (!_logDisconnect) 
            return;
            
        LogToFile(DisconnectLogPathName, msg);
    }
}