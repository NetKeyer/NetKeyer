// ****************************************************************************
///*!	\file ALE3G.cs
// *	\brief Contains ALE interface
// *
// *	\copyright	Copyright 2020 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2022-06-23
// *	\author Jessica Temte
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using Flex.UiWpfFramework.Mvvm;


namespace Flex.Smoothlake.FlexLib
{
    public class ALE3G : ObservableObject
    {
        private Radio _radio;

        public ALE3G(Radio radio)
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
                    if (value) cmd = "ale enable 3G";
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

        public void SetLink(ALE3GStation station)
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

        public void SendAmd(ALE3GStation station, string msg, bool link)
        {
            if (link)
                _radio.SendCommand("ale link station=" + station.Name + " data \"text=" + msg + "\"");
            else
                _radio.SendCommand("ale amd station=" + station.Name + " \"text=" + msg + "\"");
        }

        public delegate void ALE3GAmdEventHandler(string from_station, string to_station, string msg);
        public event ALE3GAmdEventHandler ALE3GAmd;

        private void OnALE3GAmd(string from_station, string to_station, string msg)
        {
            if (ALE3GAmd != null)
                ALE3GAmd(from_station, to_station, msg);
        }


        private ALE3GStatus _status = null;
        public ALE3GStatus Status
        {
            get { return _status; }
        }

        private ALE3GConfig _config = null;
        public ALE3GConfig Config
        {
            get { return _config; }
        }

        private List<ALE3GStation> _stationList = new List<ALE3GStation>();
        public List<ALE3GStation> StationList
        {
            get
            {
                if (_stationList == null) return null;
                lock (_stationList)
                    return _stationList;
            }
        }

        public bool GetSelfStationName(out string self_name)
        {
            self_name = null;

            lock (_stationList)
            {
                foreach (ALE3GStation station in _stationList)
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

        public delegate void ALE3GStationAddedEventHandler(ALE3GStation station);
        public event ALE3GStationAddedEventHandler ALE3GStationAdded;

        private void OnALE3GStationAdded(ALE3GStation station)
        {
            if (ALE3GStationAdded != null)
                ALE3GStationAdded(station);
        }

        public delegate void ALE3GStationRemovedEventHandler(ALE3GStation station);
        public event ALE3GStationRemovedEventHandler ALE3GStationRemoved;

        private void OnALE3GStationRemoved(ALE3GStation station)
        {
            if (ALE3GStationRemoved != null)
                ALE3GStationRemoved(station);
        }

        private void RemoveStation(string name)
        {
            ALE3GStation station_to_be_removed = null;
            lock (_stationList)
            {
                foreach (ALE3GStation station in _stationList)
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
                OnALE3GStationRemoved(station_to_be_removed);
        }

        private List<ALE3GPath> _pathList = new List<ALE3GPath>();
        public List<ALE3GPath> PathList
        {
            get
            {
                if (_pathList == null) return null;
                lock (_pathList)
                    return _pathList;
            }
        }

        public delegate void ALE3GPathAddedEventHandler(ALE3GPath path);
        public event ALE3GPathAddedEventHandler ALE3GPathAdded;

        private void OnALE3GPathAdded(ALE3GPath path)
        {
            if (ALE3GPathAdded != null)
                ALE3GPathAdded(path);
        }

        public delegate void ALE3GPathRemovedEventHandler(ALE3GPath path);
        public event ALE3GPathRemovedEventHandler ALE3GPathRemoved;

        private void OnALE3GPathRemoved(ALE3GPath path)
        {
            if (ALE3GPathRemoved != null)
                ALE3GPathRemoved(path);
        }

        private void RemovePath(string id)
        {
            ALE3GPath path_to_be_removed = null;
            lock (_pathList)
            {
                foreach (ALE3GPath path in _pathList)
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
                OnALE3GPathRemoved(path_to_be_removed);
        }

        public void Remove3GPath(string id)
        {
            RemovePath(id);
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
                            Debug.WriteLine("ALE3G::ParseStatus - status: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE3GStatus status = new ALE3GStatus();

                        string[] status_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "status"
                        foreach (string kv in status_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE3G::ParseStatus - status: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
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
                                case "mode": status.Mode = value; break;
                                case "other":
                                    {
                                        if (Link)
                                            LinkedStation = value;
                                        else
                                            LinkedStation = null;
                                        RaisePropertyChanged("LinkedStation");
                                        break;
                                    }
                                case "sync":
                                    {
                                        if (!uint.TryParse(value, out uint temp))
                                        {
                                            Debug.WriteLine("ALE3G::ParseStatus - status: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }
                                        status.Sync = Convert.ToBoolean(temp);
                                        break;
                                    }
                            }
                        }

                        _status = status;
                        RaisePropertyChanged("Status");
                    }
                    break;

                case "station":
                    if (words.Length < 2)
                    {
                        Debug.WriteLine("ALE::ParseStatus - station: Too few words -- min 2 (" + words + ")");
                        return;
                    }

                    ALE3GStation station = new ALE3GStation();

                    string[] station_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "station"
                    foreach (string kv in station_words)
                    {
                        string[] tokens = kv.Split('=');
                        if (tokens.Length != 2)
                        {
                            Debug.WriteLine("ALE::ParseStatus - station: Invalid key/value pair (" + kv + ")");
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
                                        Debug.WriteLine("ALE::ParseStatus - config - self: Invalid key/value pair (" + kv + ")");
                                        continue;
                                    }
                                    station.Self = Convert.ToBoolean(temp);
                                }
                                break;
                            case "addr": station.Address = value; break;
                            case "desc": station.Desc = value; break;
                            case "tune": station.Tune = value; break;
                        }
                    }

                    // is this a remove status?
                    if (words.Length == 3 && //"station name=<name> removed"
                        words[2] == "removed" &&
                            words[1].StartsWith("name="))
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
                            ALE3GStation oldStation = _stationList.Find(stn => stn.Name == station.Name);
                            if (oldStation != null)
                            {
                                RemoveStation(oldStation.Name);
                            }
                            //add the new station
                            _stationList.Add(station);
                        }
                        RaisePropertyChanged("StationList");

                        OnALE3GStationAdded(station);
                    }
                    break;
                case "path":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE3G::ParseStatus - path: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE3GPath path = new ALE3GPath();

                        string[] path_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "path"
                        foreach (string kv in path_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE3G::ParseStatus - path: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "id": path.PathID = value; break;
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
                                ALE3GPath oldPath = _pathList.Find(p => p.PathID == path.PathID);
                                if (oldPath != null)
                                {
                                    RemovePath(oldPath.PathID);
                                }
                                //add the new path
                                _pathList.Add(path);
                            }
                            RaisePropertyChanged("PathList");

                            OnALE3GPathAdded(path);
                        }
                    }
                    break;
            }
        }
    }

    public class ALE3GStatus
    {
        public string State { get; set; }
        public string Mode { get; set; }

        public bool Sync { get; set; }
        public string Caller { get; set; }
        public string Other { get; set; }
        public string Preset { get; set; }
        public string Type { get; set; }
        public string TrafficType { get; set; }
    }

    public class ALE3GConfig
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

    public class ALE3GStation : ALECallee
    {
        public bool Self { get; set; }
        public string Address { get; set; }
        public string Desc { get; set; }
        public string Tune { get; set; }
    }
    public class ALE3GPath
    {
        public string PathID { get; set; }
    }
}
