// ****************************************************************************
///*!	\file ALE.cs
// *	\brief Contains ALE interface
// *
// *	\copyright	Copyright 2020 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2020-11-16
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Flex.UiWpfFramework.Mvvm;


namespace Flex.Smoothlake.FlexLib
{
    public class ALE2G : ObservableObject
    {
        private Radio _radio;
        private const int SOUND_START_MAX = 86399;
        private const int SOUND_INTERVAL_MAX = 3600;

        public ALE2G(Radio radio)
        {
            _radio = radio;
        }

        public void SendConfigCommand(string cmd)
        {
            _radio.SendCommand(cmd);
        }

        private bool _enable = false;
        public bool Enable
        {
            get { return _enable; }
            set
            {
                if (_enable != value)
                {
                    _enable = value;

                    string cmd;
                    if (value) cmd = "ale enable 2g=scan";
                    else cmd = "ale disable";
                    _radio.SendCommand(cmd);

                    RaisePropertyChanged("Enable");
                }
            }
        }

        private bool _link = false;
        public bool Link
        {
            get { return _link; }
            private set
            {
                if (_link != value)
                {
                    _link = value;
                    RaisePropertyChanged("Link");
                }
            }
        }

        private bool _linking;
        public bool Linking
        {
            get => _linking;
            private set
            {
                if (value == _linking) return;
                _linking = value;
                RaisePropertyChanged(nameof(Linking));
            }
        }

        private string _linkedStation = null;
        public string LinkedStation
        {
            get { return _linkedStation; }
            set
            {
                if (_linkedStation != value)
                {
                    _linkedStation = value;
                    RaisePropertyChanged("LinkedStation");
                }
            }
        }

        public void SetLink(ALE2GStation station)
        {
            if (station == null) return;
            if (Link || Linking)
            {
                return;
            }
            _radio.SendCommand($"ale link station={station.Name} data");
        }

        public void Unlink()
        {
            if (_link == false) return;

            _radio.SendCommand("ale unlink");
        }

        private bool _sound = false;
        public bool Sound
        {
            get { return _sound; }
            set
            {
                if (_sound != value)
                {
                    _sound = value;
                    _radio.SendCommand("ale sound=" + Convert.ToByte(_sound));
                    RaisePropertyChanged("Sound");
                }
            }
        }

        public void SendAmd(ALE2GStation station, string msg, bool link)
        {
            if (link)
                _radio.SendCommand("ale link station=" + station.Name + " data \"text=" + msg + "\"");
            else
                _radio.SendCommand("ale msg station=" + station.Name + " \"text=" + msg + "\"");
        }

        public delegate void ALE2GAmdEventHandler(string from_station, string to_station, string msg);
        public event ALE2GAmdEventHandler ALE2GAmd;

        private void OnALE2GAmd(string from_station, string to_station, string msg)
        {
            if (ALE2GAmd != null)
                ALE2GAmd(from_station, to_station, msg);
        }


        private ALE2GStatus _status = null;
        public ALE2GStatus Status
        {
            get { return _status; }
        }

        private ALE2GConfig _config = null;
        public ALE2GConfig Config
        {
            get { return _config; }
        }

        private ALE2GMessage _message = null;
        public ALE2GMessage Message
        {
            get { return _message; }
        }

        private List<ALE2GStation> _stationList = new List<ALE2GStation>();
        public List<ALE2GStation> StationList
        {
            get
            {
                if (_stationList == null) return null;
                lock (_stationList)
                    return _stationList;
            }
        }

        private List<ALE2GNetwork> _networkList = new List<ALE2GNetwork>();
        public List<ALE2GNetwork> NetworkList => _networkList;

        public bool GetSelfStationName(out string self_name)
        {
            self_name = null;

            lock (_stationList)
            {
                foreach (ALE2GStation station in _stationList)
                {
                    if (station.Self)
                    {
                        self_name = station.Name;
                        return true;
                    }
                }
            }

            return false;
        }

        public delegate void ALE2GStationAddedEventHandler(ALE2GStation station);
        public event ALE2GStationAddedEventHandler ALE2GStationAdded;

        private void OnALE2GStationAdded(ALE2GStation station)
        {
            if (ALE2GStationAdded != null)
            {
                ALE2GStationAdded(station);
            }
        }

        public delegate void ALE2GNetworkAddedEventHandler(ALE2GNetwork network);
        public event ALE2GNetworkAddedEventHandler ALE2GNetworkAdded;

        private void OnALE2GNetworkAdded(ALE2GNetwork network)
        {
            ALE2GNetworkAdded?.Invoke(network);
        }

        public delegate void ALE2GStationRemovedEventHandler(ALE2GStation station);
        public event ALE2GStationRemovedEventHandler ALE2GStationRemoved;

        private void OnALE2GStationRemoved(ALE2GStation station)
        {
            ALE2GStationRemoved?.Invoke(station);
        }

        private void RemoveStation(string name)
        {
            ALE2GStation station_to_be_removed = null;
            lock (_stationList)
            {
                foreach (ALE2GStation station in _stationList)
                {
                    if (station.Name == name)
                    {
                        station_to_be_removed = station;
                        break;
                    }
                }

                if (station_to_be_removed != null)
                    _stationList.Remove(station_to_be_removed);
            }

            if (station_to_be_removed != null)
                OnALE2GStationRemoved(station_to_be_removed);
        }


        public delegate void ALE2GNetworkRemovedEventHandler(ALE2GNetwork network);
        public event ALE2GNetworkRemovedEventHandler ALE2GNetworkRemoved;

        private void OnALE2GNetworkRemoved(ALE2GNetwork network)
        {
            ALE2GNetworkRemoved?.Invoke(network);
        }

        private void RemoveNetwork(string name)
        {
            ALE2GNetwork networkToBeRemoved = null;
            lock (_networkList)
            {
                networkToBeRemoved = _networkList.FirstOrDefault(n => n.Name == name);
                if (networkToBeRemoved != null)
                {
                    _networkList.Remove(networkToBeRemoved);
                }
            }

            if (networkToBeRemoved != null)
            {
                OnALE2GNetworkRemoved(networkToBeRemoved);
            }
        }

        private List<ALE2GPath> _pathList = new List<ALE2GPath>();
        public List<ALE2GPath> PathList => _pathList;

        public delegate void ALE2GPathAddedEventHandler(ALE2GPath path);
        public event ALE2GPathAddedEventHandler ALE2GPathAdded;

        private void OnALE2GPathAdded(ALE2GPath path)
        {
            ALE2GPathAdded?.Invoke(path);
        }

        public delegate void ALE2GPathRemovedEventHandler(ALE2GPath path);
        public event ALE2GPathRemovedEventHandler ALE2GPathRemoved;

        private void OnALE2GPathRemoved(ALE2GPath path)
        {
            ALE2GPathRemoved?.Invoke(path);
        }

        private void RemovePath(string id)
        {
            ALE2GPath path_to_be_removed = null;
            lock (_pathList)
            {
                foreach (ALE2GPath path in _pathList)
                {
                    if (path.PathID == id)
                    {
                        path_to_be_removed = path;
                        break;
                    }
                }

                if (path_to_be_removed != null)
                    _pathList.Remove(path_to_be_removed);
            }

            if (path_to_be_removed != null)
                OnALE2GPathRemoved(path_to_be_removed);
        }

        public void Remove2GPath(string id)
        {
            RemovePath(id);
        }

        internal void ParseNetwork(string[] words)
        {
            // expected: ale 2g network name=<network name> desc=<description> scan_list=N/A slot_pad=<slot pad units 0-99>
            ALE2GNetwork network = new();
            bool creating = true;
            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine($"ALE::ParseStatus - station: Too few words -- min 2 ({kv})");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];
                switch (key)
                {
                    case "name":
                        {
                            network.Name = value;
                            break;
                        }
                    case "desc":
                        {
                            network.Description = value;
                            break;
                        }
                    case "addr":
                        {
                            network.Address = value;
                            break;
                        }
                    case "scan_list":
                        {
                            network.ScanList = value;
                            break;
                        }
                    case "slot_pad":
                        {
                            if (int.TryParse(value, out int temp))
                            {
                                network.SlotPad = temp;
                            }
                            break;
                        }
                    case "removed":
                        {
                            creating = false;
                            break;
                        }
                }
            }

            if (creating)
            {
                lock (_stationList)
                {
                    // If station already exists, delete the old one to replace with new station object.
                    ALE2GNetwork oldNetwork = _networkList.Find(x => x.Name == network.Name);
                    if (oldNetwork != null)
                    {
                        RemoveNetwork(oldNetwork.Name);
                    }
                    _networkList.Add(network);
                }
                RaisePropertyChanged(nameof(NetworkList));
                OnALE2GNetworkAdded(network);
            }
            else // remove
            {
                RemoveNetwork(network.Name);
                RaisePropertyChanged(nameof(NetworkList));
            }
        }

        internal void ParseStation(string[] stationWords)
        {
            ALE2GStation station = new ALE2GStation();
            foreach (string kv in stationWords)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("ALE::ParseStation - station: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "name": station.Name = value; break;
                    case "self":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("ALE::ParseStation - config - self: Invalid key/value pair (" + kv + ")");
                                continue;
                            }
                            station.Self = Convert.ToBoolean(temp);
                        }
                        break;
                    case "addr": station.Address = value; break;
                    case "reply": station.Reply = value; break;
                    case "timeout": station.Timeout = value; break;
                    case "tune": station.Tune = value; break;
                    case "slot": station.Slot = value; break;
                }
            }

            // is this a remove status?
            if (stationWords.Length == 2 && //"station name=<name> removed"
                stationWords[1] == "removed" &&
                stationWords[0].StartsWith("name="))
            {
                // yes -- remove the station
                RemoveStation(station.Name);
                RaisePropertyChanged(nameof(StationList));
            }
            else
            {
                // no -- add the station
                lock (_stationList)
                {
                    //if station  already exists, delete the old one to replace with new station object
                    ALE2GStation oldStation = _stationList.Find(stn => stn.Name == station.Name);
                    if (oldStation != null)
                    {
                        RemoveStation(oldStation.Name);
                    }
                    //add the new station
                    _stationList.Add(station);
                }
                RaisePropertyChanged("StationList");
                OnALE2GStationAdded(station);
            }
        }

        internal void ParseStatus(string s)
        {
            string[] words = s.Split(' ');

            switch (words[0])
            {
                case "status":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE::ParseStatus - status: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE2GStatus status = new ALE2GStatus();

                        string[] status_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "status"
                        foreach (string kv in status_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE::ParseStatus - status: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "mode": status.Mode = value; break;
                                case "state":
                                    {
                                        status.State = value;
                                        switch (status.State.ToLower())
                                        {
                                            case "linking":
                                                Link = false;
                                                Linking = true;
                                                break;
                                            case "linked":
                                                Link = true;
                                                Linking = false;
                                                break;
                                            default:
                                                Link = false;
                                                Linking = false;
                                                break;
                                        }
                                        RaisePropertyChanged(nameof(Link));
                                        RaisePropertyChanged(nameof(Linking));
                                        break;
                                    }

                                case "caller":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - status - caller: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        status.Caller = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "other":
                                    {
                                        status.Other = value;
                                        LinkedStation = value;
                                    }
                                    break;
                                case "type": status.Type = value; break;
                                case "to_sinad": status.ToSinad = value; break;
                                case "from_sinad": status.FromSinad = value; break;
                                case "self": status.Self = value; break;
                            }
                        }

                        _status = status;
                        RaisePropertyChanged("Status");
                    }
                    break;
                case "config":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE::ParseStatus - config: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE2GConfig config = new ALE2GConfig();

                        string[] config_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "config"
                        foreach (string kv in config_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE::ParseStatus - config: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "name": config.Name = value; break;
                                case "desc": config.Description = value; break;
                                case "rate": config.Rate = value; break;
                                case "timeout": config.Timeout = value; break;
                                case "lbt": config.ListenBeforeTalk = value; break;
                                case "scan_retries": config.ScanRetries = value; break;
                                case "call_retries": config.CallRetries = value; break;
                                case "all":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - all: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.All = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "any":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - any: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.Any = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "wild":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - wild: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.Wild = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "amd":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - amd: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.Amd = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "dtm":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE::ParseStatus - config - dtm: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        config.Dtm = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "sound":
                                    {
                                        uint temp;
                                        if ("Global" == value)
                                        {
                                            temp = 1;
                                        }
                                        else
                                        {
                                            bool b = uint.TryParse(value, out temp);
                                            if (!b || temp > 1)
                                            {
                                                Debug.WriteLine("ALE::ParseStatus - config - sound: Invalid key/value pair (" + kv + ")");
                                                continue;
                                            }
                                        }
                                        config.Sound = Convert.ToBoolean(temp);
                                    }
                                    break;
                            }
                        }

                        _config = config;
                        RaisePropertyChanged("Config");
                    }
                    break;
                case "station":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine($"ALE::ParseStatus - station: Too few words -- min 2 ({words})");
                            return;
                        }
                        ParseStation(words.Skip(1).Take(words.Length - 1).ToArray());
                        break;
                    }
                case "network":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine($"ALE::ParseStatus - network: Too few words -- min 2 ({words})");
                            return;
                        }
                        ParseNetwork(words.Skip(1).Take(words.Length - 1).ToArray());
                        break;
                    }
                case "amd":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE::ParseStatus - amd: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE2GMessage amd = new ALE2GMessage();

                        string[] amd_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "amd"
                        foreach (string kv in amd_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                // if we are receiving a message with multiple words, we have already seperated the words into their own tokens
                                // here we are appending the rest of the message to the first word
                                if (s.Contains("text") && amd.Message != null)
                                {
                                    amd.Message += " " + tokens[0];
                                }
                                Debug.WriteLine("ALE::ParseStatus - amd: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "other": amd.Sender = value; break;
                                case "text": amd.Message = value; break;
                            }
                        }

                        if (!string.IsNullOrEmpty(amd.Message))
                        {
                            string self;
                            // Do we have a good name for the local 'self' station?
                            if (!GetSelfStationName(out self))
                            {
                                // no -- we don't have the info to move forward then.  Write out a debug and drop out.
                                Debug.WriteLine("ALE2G::ParseStatus Error: No 'self' station (self=true) found.");
                            }
                            else
                            {

                                //remove quotes around received messages if needed
                                if (amd.Message.StartsWith("\""))
                                {
                                    OnALE2GAmd(amd.Sender, self, amd.Message.Substring(1, amd.Message.Length - 2));
                                }
                                else
                                {
                                    OnALE2GAmd(amd.Sender, self, amd.Message);
                                }
                            }
                        }

                        _message = amd;
                        RaisePropertyChanged("Message");
                    }
                    break;
                case "auto":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE::ParseStatus - auto: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE2GStation station = new ALE2GStation();

                        string[] auto_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "auto"
                        foreach (string kv in auto_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE::ParseStatus - auto: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "addr":
                                    {
                                        station.Name = value;
                                        station.Self = false;
                                        station.Address = value;
                                    }
                                    break;
                            }
                        }

                        // is this a remove status?
                        if (words.Length == 3 && //"auto addr=<name> removed"
                            words[2] == "removed" &&
                                words[1].StartsWith("addr="))
                        {
                            // yes -- remove the station
                            RemoveStation(station.Name);
                            RaisePropertyChanged("StationList");
                        }
                        else
                        {
                            // no -- add the station
                            lock (_stationList)
                            {
                                //if station  already exists, delete the old one to replace with new station object
                                ALE2GStation oldStation = _stationList.Find(stn => stn.Name == station.Name);
                                if (oldStation != null)
                                {
                                    RemoveStation(oldStation.Name);
                                }
                                //add the new station
                                _stationList.Add(station);
                            }
                            RaisePropertyChanged("StationList");

                            OnALE2GStationAdded(station);
                        }
                    }
                    break;
                case "path":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE2G::ParseStatus - path: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE2GPath path = new ALE2GPath();

                        string[] path_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "path"
                        foreach (string kv in path_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE2G::ParseStatus - path: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "id": path.PathID = value; break;
                                case "tx_block":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE2G::ParseStatus - path - tx_block: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        path.TxBlock = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "sound":
                                    {
                                        uint temp;
                                        bool b = uint.TryParse(value, out temp);
                                        if (!b)
                                        {
                                            Debug.WriteLine("ALE2G::ParseStatus - path - sound: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        path.Sound = Convert.ToBoolean(temp);
                                    }
                                    break;
                                case "sound_start":
                                    {
                                        //convert string value to int temp
                                        if (!Int32.TryParse(value, out int temp))
                                        {
                                            Debug.WriteLine("ALE2G::ParseStatus - path - sound_start: Invalid integer (" + value + ")");
                                            continue;
                                        }

                                        //if temp is in legal range, continue
                                        if (temp > 0 && temp < SOUND_START_MAX)
                                        {
                                            path.SoundStart = temp;
                                        }
                                    }
                                    break;
                                case "sound_int":
                                    {
                                        //convert string value to int temp
                                        if (!Int32.TryParse(value, out int temp))
                                        {
                                            Debug.WriteLine("ALE2G::ParseStatus - path - sound_int: Invalid integer (" + value + ")");
                                            continue;
                                        }

                                        //if temp is in legal range, continue
                                        if (temp > 0 && temp < SOUND_INTERVAL_MAX)
                                        {
                                            path.SoundInt = temp;
                                        }
                                    }
                                    break;
                            }
                        }

                        // is this a remove status?
                        if (words.Length == 3 && //"path id=<path id> removed"
                            words[2] == "removed" &&
                                words[1].StartsWith("id="))
                        {
                            // yes -- remove the path
                            RemovePath(path.PathID);
                            RaisePropertyChanged("PathList");
                        }
                        else
                        {
                            // no -- add the path
                            lock (_pathList)
                            {
                                //if path already exists, delete the old one to replace with new path object
                                ALE2GPath oldPath = _pathList.Find(p => p.PathID == path.PathID);
                                if (oldPath != null)
                                {
                                    RemovePath(oldPath.PathID);
                                }
                                //add the new path
                                _pathList.Add(path);
                            }
                            RaisePropertyChanged("PathList");

                            OnALE2GPathAdded(path);
                        }
                    }
                    break;
            }
        }
    }

    public class ALE2GStatus
    {
        public string Mode { get; set; }
        public string State { get; set; }
        public bool Caller { get; set; }
        public string Other { get; set; }
        public string Type { get; set; }
        public string ToSinad { get; set; }
        public string FromSinad { get; set; }
        public string Amd { get; set; }
        public string Self { get; set; }
    }

    public class ALE2GConfig
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Rate { get; set; }
        public string Timeout { get; set; }
        public string ListenBeforeTalk { get; set; }
        public string ScanRetries { get; set; }
        public string CallRetries { get; set; }
        public bool All { get; set; }
        public bool Any { get; set; }
        public bool Wild { get; set; }
        public bool Amd { get; set; }
        public bool Dtm { get; set; }
        public bool Sound { get; set; }
    }

    public class ALE2GStation : ALECallee
    {
        public bool Self { get; set; }
        public string Address { get; set; }
        public string Reply { get; set; }
        public string Timeout { get; set; }
        public string Tune { get; set; }
        public string Slot { get; set; }
    }
    public class ALE2GMessage
    {
        public string Message { get; set; }
        public string Sender { get; set; }
    }
    public class ALE2GPath
    {
        public string PathID { get; set; }
        public bool TxBlock { get; set; }
        public bool Sound { get; set; }
        public int SoundStart { get; set; }
        public int SoundInt { get; set; }
    }
    public class ALE2GNetwork : ALECallee
    {
        public string Description { get; set; }
        public string Address { get; set; }
        public string ScanList { get; set; }
        public int SlotPad { get; set; }
    }
}