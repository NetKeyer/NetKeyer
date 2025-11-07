// ****************************************************************************
///*!	\file ALE4G.cs
// *	\brief Contains ALE interface
// *
// *	\copyright	Copyright 2020 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2022-06-17
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
    public class ALE4G : ObservableObject
    {
        private Radio _radio;

        public ALE4G(Radio radio)
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
                    if (value) cmd = "ale enable 4G";
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

        public void SetLink(ALE4GStation station)
        {
            if (station == null) return;

            if (Link || Linking)
            {
                return;
            }
            // TODO get link type from modem manager ALE panel
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

        public void SendMsg(ALE4GStation station, string msg, bool link)
        {
            if (link)
                _radio.SendCommand("ale link station=" + station.Name + " data \"text=" + msg + "\"");
            else
                _radio.SendCommand("ale msg station=" + station.Name + " \"text=" + msg + "\"");
        }

        public delegate void ALE4GMsgEventHandler(string from_station, string to_station, string msg);
        public event ALE4GMsgEventHandler ALE4GMsg;

        private void OnALE4GMsg(string from_station, string to_station, string msg)
        {
            if (ALE4GMsg != null)
                ALE4GMsg(from_station, to_station, msg);
        }

        private ALE4GStatus _status = null;
        public ALE4GStatus Status
        {
            get { return _status; }
        }

        private ALE4GConfig _config = null;
        public ALE4GConfig Config
        {
            get { return _config; }
        }

        private ALE4GMessage _message = null;
        public ALE4GMessage Message
        {
            get { return _message; }
        }

        private List<ALE4GStation> _stationList = new List<ALE4GStation>();
        public List<ALE4GStation> StationList
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
                foreach (ALE4GStation station in _stationList)
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

        public delegate void ALE4GStationAddedEventHandler(ALE4GStation station);
        public event ALE4GStationAddedEventHandler ALE4GStationAdded;

        private void OnALE4GStationAdded(ALE4GStation station)
        {
            if (ALE4GStationAdded != null)
                ALE4GStationAdded(station);
        }

        public delegate void ALE4GStationRemovedEventHandler(ALE4GStation station);
        public event ALE4GStationRemovedEventHandler ALE4GStationRemoved;

        private void OnALE4GStationRemoved(ALE4GStation station)
        {
            if (ALE4GStationRemoved != null)
                ALE4GStationRemoved(station);
        }

        private void RemoveStation(string name)
        {
            ALE4GStation station_to_be_removed = null;
            lock (_stationList)
            {
                foreach (ALE4GStation station in _stationList)
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
                OnALE4GStationRemoved(station_to_be_removed);
        }

        private List<ALE4GPath> _pathList = new List<ALE4GPath>();
        public List<ALE4GPath> PathList
        {
            get
            {
                if (_pathList == null) return null;
                lock (_pathList)
                    return _pathList;
            }
        }

        public delegate void ALE4GPathAddedEventHandler(ALE4GPath path);
        public event ALE4GPathAddedEventHandler ALE4GPathAdded;

        private void OnALE4GPathAdded(ALE4GPath path)
        {
            if (ALE4GPathAdded != null)
                ALE4GPathAdded(path);
        }

        public delegate void ALE4GPathRemovedEventHandler(ALE4GPath path);
        public event ALE4GPathRemovedEventHandler ALE4GPathRemoved;

        private void OnALE4GPathRemoved(ALE4GPath path)
        {
            if (ALE4GPathRemoved != null)
                ALE4GPathRemoved(path);
        }

        private void RemovePath(string id)
        {
            ALE4GPath path_to_be_removed = null;
            lock (_pathList)
            {
                foreach (ALE4GPath path in _pathList)
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
                OnALE4GPathRemoved(path_to_be_removed);
        }

        public void Remove4GPath(string id)
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
                            Debug.WriteLine("ALE4G::ParseStatus - status: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE4GStatus status = new ALE4GStatus();

                        string[] status_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "status"

                        foreach (string kv in status_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE4G::ParseStatus - status: Invalid key/value pair (" + kv + ")");
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
                                        break;
                                    }
                                case "dest":
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
                case "msg": //msg received other=<name> [purpose<purpose>] text="<message text>"
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE4G::ParseStatus - msg: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE4GMessage msg = new ALE4GMessage();

                        string[] msg_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "msg"

                        switch (msg_words[0])
                        {
                            case "received": //msg received other=<name> [purpose<purpose>] text="<message text>" 
                                {
                                    string[] received_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "received"

                                    msg.Received = true;

                                    foreach (string kv in received_words)
                                    {
                                        string[] tokens = kv.Split('=');
                                        if (tokens.Length != 2)
                                        {
                                            // if we are receiving a message with multiple words, we have already seperated the words into their own tokens
                                            // here we are appending the rest of the message to the first word
                                            if (s.Contains("text") && msg.Message != null)
                                            {
                                                msg.Message += " " + tokens[0];
                                            }
                                            Debug.WriteLine("ALE4G::ParseStatus - msg received: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }

                                        string key = tokens[0];
                                        string value = tokens[1];

                                        switch (key.ToLower())
                                        {
                                            case "other":
                                                {
                                                    msg.Sender = value;
                                                }
                                                break;
                                            case "purpose":
                                                {
                                                    msg.Purpose = value;
                                                }
                                                break;
                                            case "text":
                                                {
                                                    msg.Message = value;
                                                }
                                                break;
                                        }
                                    }
                                }
                                break;
                            case "queued":
                                {
                                    string[] queued_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "queued"

                                    msg.Queued = true;

                                    foreach (string kv in queued_words)
                                    {
                                        string[] tokens = kv.Split('=');
                                        if (tokens.Length != 2)
                                        {
                                            // if we are receiving a message with multiple words, we have already seperated the words into their own tokens
                                            // here we are appending the rest of the message to the first word
                                            if (s.Contains("text") && msg.Message != null)
                                            {
                                                msg.Message += " " + tokens[0];
                                            }
                                            Debug.WriteLine("ALE4G::ParseStatus - msg queued: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }

                                        string key = tokens[0];
                                        string value = tokens[1];

                                        switch (key.ToLower())
                                        {
                                            case "other":
                                                {
                                                    msg.Sender = value;
                                                }
                                                break;
                                            case "purpose":
                                                {
                                                    msg.Purpose = value;
                                                }
                                                break;
                                            case "text":
                                                {
                                                    msg.Message = value;
                                                }
                                                break;
                                            case "id":
                                                {
                                                    msg.ID = value;
                                                }
                                                break;
                                        }
                                    }
                                }
                                break;
                            case "dequeued":
                                {
                                    string[] dequeued_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "dequeued"

                                    msg.Dequeued = true;

                                    foreach (string kv in dequeued_words)
                                    {
                                        string[] tokens = kv.Split('=');
                                        if (tokens.Length != 2)
                                        {
                                            Debug.WriteLine("ALE4G::ParseStatus - msg dequeued: Invalid key/value pair (" + kv + ")");
                                            continue;
                                        }

                                        string key = tokens[0];
                                        string value = tokens[1];

                                        switch (key.ToLower())
                                        {
                                            case "id":
                                                {
                                                    msg.ID = value;
                                                }
                                                break;
                                            case "reason":
                                                {
                                                    msg.Reason = value;
                                                }
                                                break;
                                        }
                                    }
                                }
                                break;
                        }

                        _message = msg;
                        RaisePropertyChanged("Message");

                        if (!string.IsNullOrEmpty(msg.Message) && msg.Received)
                        {
                            string self;
                            // Do we have a good name for the local 'self' station?
                            if (!GetSelfStationName(out self))
                            {
                                // no -- we don't have the info to move forward then.  Write out a debug and drop out.
                                Debug.WriteLine("ALE4G::ParseStatus Error: No 'self' station (self=true) found.");
                            }
                            else
                            {
                                //remove quotes around received messages if needed
                                if (msg.Message.StartsWith("\""))
                                {
                                    OnALE4GMsg(msg.Sender, self, msg.Message.Substring(1, msg.Message.Length - 2));
                                }
                                else
                                {
                                    OnALE4GMsg(msg.Sender, self, msg.Message);
                                }

                            }
                        }
                    }
                    break;
                case "station":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE::ParseStatus - station: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE4GStation station = new ALE4GStation();

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
                                case "scan_mode": station.Mode = value; break;
                                case "dwell": station.Dwell = value; break;
                                case "dead": station.Dead = value; break;
                                case "react": station.React = value; break;
                                case "yield": station.Yield = value; break;
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
                                ALE4GStation oldStation = _stationList.Find(stn => stn.Name == station.Name);
                                if (oldStation != null)
                                {
                                    RemoveStation(oldStation.Name);
                                }
                                //add the new station
                                _stationList.Add(station);
                            }
                            RaisePropertyChanged("StationList");
                            OnALE4GStationAdded(station);
                        }
                    }
                    break;
                case "path":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine("ALE4G::ParseStatus - path: Too few words -- min 2 (" + words + ")");
                            return;
                        }

                        ALE4GPath path = new ALE4GPath();

                        string[] path_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "path"
                        foreach (string kv in path_words)
                        {
                            string[] tokens = kv.Split('=');
                            if (tokens.Length != 2)
                            {
                                Debug.WriteLine("ALE4G::ParseStatus - path: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            string key = tokens[0];
                            string value = tokens[1];

                            switch (key.ToLower())
                            {
                                case "id": path.PathID = value; break;
                                case "bw": path.Bandwidth = value; break;
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
                                ALE4GPath oldPath = _pathList.Find(p => p.PathID == path.PathID);
                                if (oldPath != null)
                                {
                                    RemovePath(oldPath.PathID);
                                }
                                //add the new path
                                _pathList.Add(path);
                            }
                            RaisePropertyChanged("PathList");

                            OnALE4GPathAdded(path);
                        }
                    }
                    break;
            }
        }
    }

    public class ALE4GStatus
    {
        public string State { get; set; }
        public bool Name { get; set; }
        public bool Sync { get; set; }
        public string LinkType { get; set; }
        public string TXBW { get; set; }
        public string RXBW { get; set; }
        public string Deep { get; set; }
        public string Async { get; set; }
        public string Occupy { get; set; }
    }

    public class ALE4GConfig
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
        public bool Dtm { get; set; }
        public bool Sound { get; set; }
    }

    public class ALE4GStation : ALECallee
    {
        public bool Self { get; set; }
        public string Address { get; set; }
        public string Desc { get; set; }
        public string Mode { get; set; }
        public string Dwell { get; set; }
        public string Dead { get; set; }
        public string React { get; set; }
        public string Yield { get; set; }
    }

    public class ALE4GMessage
    {
        public string Message { get; set; }
        public bool Received { get; set; }
        public bool Queued { get; set; }
        public bool Dequeued { get; set; }
        public string Sender { get; set; }
        public string Purpose { get; set; }
        public string ID { get; set; } //used only for queued and dequeued messages
        public string Reason { get; set; } //used only for dequeued messages
    }

    public class ALE4GPath
    {
        public string PathID { get; set; }
        public string Bandwidth { get; set; }
    }
}
