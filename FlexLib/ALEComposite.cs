// ****************************************************************************
///*!	\file ALEComposite.cs
// *	\brief Contains ALE composite interface
// *
// *	\copyright	Copyright 2020 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2023-07-12
// *	\author Jessica Temte
// */
// ****************************************************************************

using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Flex.UiWpfFramework.Mvvm;

namespace Flex.Smoothlake.FlexLib
{
    public record ALECompositeCurrentStatus(string globalStatusString,bool is2GActive, bool is3GActive, bool is4GActive);

    public enum ALELinkType
    {
        StationToStation,
        Network,
        Group,
        AutoAddress,
        Multipoint,
        Allcall,
        Anycall,
    };

    public enum ALEGen
    {
        Gen2G,
        Gen3G,
        Gen4G,
    }

    public class ALEComposite : ObservableObject
    {
        private Radio _radio;
        private ALE2G _ale2g;
        private ALE3G _ale3g;
        private ALE4G _ale4g;

        public ALEComposite(Radio radio)
        {
            _radio = radio;
            _radio.PropertyChanged += _radio_PropertyChanged;
            RegisterSlicePropertyChangedHandlers();
            _ale2g = _radio.ALE2G;
            _ale2g.PropertyChanged += _2g_PropertyChanged;
            _ale3g = _radio.ALE3G;
            _ale3g.PropertyChanged += _3g_PropertyChanged;
            _ale4g = _radio.ALE4G;
            _ale4g.PropertyChanged += _4g_PropertyChanged;
        }

        private void DisableALEControls()
        {
            ALECompositeCurrentStatusLabel = _defaultALECompositeStatus;
            ALE2GControlsActive = ALE3GControlsActive = ALE4GControlsActive = false;
        }

        private static readonly ImmutableDictionary<string, ALECompositeCurrentStatus> sliceModeAleStatuses = new Dictionary<string, ALECompositeCurrentStatus>{
            { "2ACH", new("2ACH (2G ALE Channel)", true, false, false) },
            { "2ALE", new ALECompositeCurrentStatus("2ALE (2G ALE Scan)", true, false, false)},
            { "3ACH", new ALECompositeCurrentStatus("3ACH (3G ALE Channel)", false, true, false)},
            { "4ALE", new ALECompositeCurrentStatus("4ALE (4G ALE)", false, false, true)},
            { "DACH", new ALECompositeCurrentStatus("DACH (Dual ALE Channel: 2G + 3G)", true, true, false)},
            { "DALE", new ALECompositeCurrentStatus("DALE (Dual ALE Scan: 2G + 3G)", true, true, false)},
            { "PALE", new ALECompositeCurrentStatus("PALE (Parallel ALE: 2G + 3G + 4G)", true, true, true)},
        }.ToImmutableDictionary();

        private void SetALEControlsFromStatus(ALECompositeCurrentStatus status)
        {
            if (status is null)
            {
                DisableALEControls();
            }
            else
            {
                ALECompositeCurrentStatusLabel = string.IsNullOrEmpty(status.globalStatusString) ? _defaultALECompositeStatus : status.globalStatusString;
                ALE2GControlsActive = status.is2GActive;
                ALE3GControlsActive = status.is3GActive;
                ALE4GControlsActive = status.is4GActive;
            }
        }

        private void UnregisterSlicePropertyChangedHandlers()
        {
            if (_radio is null || _radio.SliceList is null) return;
            _radio.SliceList.ForEach(x => x.PropertyChanged -= _slice_PropertyChanged);
        }

        private void RegisterSlicePropertyChangedHandlers()
        {
            if (_radio is null || _radio.SliceList is null) return;
            _radio.SliceList.ForEach(x => x.PropertyChanged += _slice_PropertyChanged);
        }

        private void SetALEControlsFromSliceList(List<Slice> slices)
        {
            if (slices == null || slices.Count == 0) return;

            // If any slices exist, check for whether there is an ALE slice - if found, update the client.
            foreach (var slice in slices)
            {
                if (sliceModeAleStatuses.TryGetValue(slice.DemodMode, out var temp))
                {
                    SetALEControlsFromStatus(temp);
                    return;
                }
            }
            // If no matching slices found, ALE is disabled.
            DisableALEControls();
        }

        private void _radio_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Radio.SliceList):
                    {
                        if (_radio.SliceList is not null)
                        {
                            UnregisterSlicePropertyChangedHandlers();
                        }

                        // When ANY slice changes, update listener for the mode changes.
                        var list = _radio.SliceList;

                        // If no slices, obviously ALE is disabled.
                        if (list is null || list.Count == 0)
                        {
                            DisableALEControls();
                            return;
                        }
                        SetALEControlsFromSliceList(list);

                        // Add listener to all slices in case new slices were added.
                        RegisterSlicePropertyChangedHandlers();
                        break;
                    }
            }
        }

        private void _slice_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not Slice slc)
            {
                DisableALEControls();
                return;
            }
            switch (e.PropertyName)
            {
                case (nameof(Slice.DemodMode)):
                    {
                        // If this slice that has changed mode now supports ALE, we can enable ALE controls now.
                        if (sliceModeAleStatuses.TryGetValue(slc.DemodMode, out var temp))
                        {
                            SetALEControlsFromStatus(temp);
                            return;
                        }
                        // If this slice mode does not support ALE, check all other slices to see if we have just disabled it.
                        else
                        {
                            SetALEControlsFromSliceList(_radio.SliceList);
                        }
                        break;
                    }
            }
        }

        private void _2g_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ALE2G.Link):
                    {
                        RaisePropertyChanged(nameof(ALEGlobalLinkStatus));
                        break;
                    }
                case nameof(ALE2G.Linking):
                    {
                        RaisePropertyChanged(nameof(ALEGlobalLinkStatus));
                        break;
                    }
            }
        }


        private void _3g_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ALE3G.Link):
                    {
                        RaisePropertyChanged(nameof(ALEGlobalLinkStatus));
                        break;
                    }
                case nameof(ALE3G.Linking):
                    {
                        RaisePropertyChanged(nameof(ALEGlobalLinkStatus));
                        break;
                    }
            }
        }

        private void _4g_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ALE4G.Link):
                    {
                        RaisePropertyChanged(nameof(ALEGlobalLinkStatus));
                        break;
                    }
                case nameof(ALE4G.Linking):
                    {
                        RaisePropertyChanged(nameof(ALEGlobalLinkStatus));
                        break;
                    }
            }
        }

        public async Task SendConfigCommand(string cmd)
        {
            try
            {
                await _radio.SendCommandAsync(cmd);
            }
            catch (SmartSdrCommandErrorException ex)
            {
                Debug.WriteLine($"Command failed - '{cmd}', error = '{ex.Message}'");
            }
        }

        #region Composite Paths

        private List<ALECompositePath> _compositePathList = new List<ALECompositePath>();
        public List<ALECompositePath> CompositePathList
        {
            get
            {
                if (_compositePathList == null) return null;
                lock (_compositePathList)
                    return _compositePathList;
            }
        }

        public delegate void ALECompositePathAddedEventHandler(ALECompositePath composite_path);
        public event ALECompositePathAddedEventHandler ALECompositePathAdded;

        private void OnALECompositePathAdded(ALECompositePath composite_path)
        {
            if (ALECompositePathAdded != null)
                ALECompositePathAdded(composite_path);
        }

        public delegate void ALECompositePathRemovedEventHandler(ALECompositePath composite_path);
        public event ALECompositePathRemovedEventHandler ALECompositePathRemoved;

        private void OnALECompositePathRemoved(ALECompositePath composite_path)
        {
            if (ALECompositePathRemoved != null)
                ALECompositePathRemoved(composite_path);
        }

        private void RemoveCompositePath(string id)
        {
            ALECompositePath composite_path_to_be_removed = null;
            lock (_compositePathList)
            {
                foreach (ALECompositePath composite_path in _compositePathList)
                {
                    if (composite_path.ID == id)
                    {
                        composite_path_to_be_removed = composite_path;
                        break;
                    }
                }

                if (composite_path_to_be_removed != null)
                    _compositePathList.Remove(composite_path_to_be_removed);
            }

            if (composite_path_to_be_removed != null)
                OnALECompositePathRemoved(composite_path_to_be_removed);
        }

        private void RemoveSubPaths(string id)
        {
            _ale2g.Remove2GPath(id);
            _ale3g.Remove3GPath(id);
            _ale4g.Remove4GPath(id);
        }

        internal void ParsePathStatus(string[] words)
        {
            string[] pathWords = words.Skip(2).Take(words.Length - 2).ToArray(); // skip "composite path"
            ALECompositePath compositePath = new ALECompositePath();

            foreach (string kv in pathWords)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    if (!String.IsNullOrEmpty(kv))
                    {
                        Debug.WriteLine("ALEComposite::ParseStatus - composite path: Invalid key/value pair (" + kv + ")");
                    }
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "id": compositePath.ID = value; break;
                    case "freq": compositePath.Frequency = value; break;
                    case "2g_path":
                        {
                            bool b = uint.TryParse(value, out uint temp);
                            if (!b)
                            {
                                Debug.WriteLine("ALEComposite::ParseStatus - composite path - 2g_path: Invalid key/value pair (" + kv + ")");
                                continue;
                            }
                            compositePath.Is2GPath = Convert.ToBoolean(temp);
                        }
                        break;
                    case "3g_path":
                        {
                            bool b = uint.TryParse(value, out uint temp);
                            if (!b)
                            {
                                Debug.WriteLine("ALEComposite::ParseStatus - composite path - 3g_path: Invalid key/value pair (" + kv + ")");
                                continue;
                            }
                            compositePath.Is3GPath = Convert.ToBoolean(temp);
                        }
                        break;
                    case "4g_path":
                        {
                            bool b = uint.TryParse(value, out uint temp);
                            if (!b)
                            {
                                Debug.WriteLine("ALEComposite::ParseStatus - composite path - 4g_path: Invalid key/value pair (" + kv + ")");
                                continue;
                            }
                            compositePath.Is4GPath = Convert.ToBoolean(temp);
                        }
                        break;
                }
            }

            // is this a remove status?

            if (words.Length == 4 && //composite path id=<ID> removed
                words[3] == "removed" &&
                    words[2].StartsWith("id="))
            {
                // yes -- remove the path
                RemoveCompositePath(compositePath.ID);
                RemoveSubPaths(compositePath.ID);
                RaisePropertyChanged("CompositePathList");
            }
            else
            {
                // no -- add the path
                lock (_compositePathList)
                {
                    //if a path already exists, delete the old one to replace with new path object
                    ALECompositePath oldCompositePath = _compositePathList.Find(cp => cp.ID == compositePath.ID);
                    if (oldCompositePath != null)
                    {
                        RemoveCompositePath(oldCompositePath.ID);
                    }
                    //add the new path
                    _compositePathList.Add(compositePath);
                }
                RaisePropertyChanged(nameof(CompositePathList));
                OnALECompositePathAdded(compositePath);
            }
        }

        #endregion

        #region Scan Lists

        /// <summary>
        /// List of all scan lists (scan list objects have a name and a list object of type ALECompositePath)
        /// </summary>
        private List<ALEScanList> _scanLists = new List<ALEScanList>();
        public List<ALEScanList> ScanLists
        {
            get
            {
                if (_scanLists == null) return null;
                lock (_scanLists)
                    return _scanLists;
            }
        }

        public delegate void ALEScanListAddedEventHandler(ALEScanList scan_list);
        public event ALEScanListAddedEventHandler ALEScanListAdded;

        private void OnALEScanListAdded(ALEScanList scan_list)
        {
            if (ALEScanListAdded != null)
                ALEScanListAdded(scan_list);
        }

        public delegate void ALEScanListRemovedEventHandler(ALEScanList scan_list);
        public event ALEScanListRemovedEventHandler ALEScanListRemoved;

        private void OnALEScanListRemoved(ALEScanList scan_list)
        {
            if (ALEScanListRemoved != null)
                ALEScanListRemoved(scan_list);
        }

        private void RemoveScanList(string name)
        {
            ALEScanList scan_list_to_be_removed = null;
            lock (_scanLists)
            {
                foreach (ALEScanList scan_list in _scanLists)
                {
                    if (scan_list.Name == name)
                    {
                        scan_list_to_be_removed = scan_list;
                        break;
                    }
                }

                if (scan_list_to_be_removed != null)
                    _scanLists.Remove(scan_list_to_be_removed);
            }

            if (scan_list_to_be_removed != null)
                OnALEScanListRemoved(scan_list_to_be_removed);
        }

        internal void ParseScanListStatus(string[] words)
        {
            if (words.Length < 2)
            {
                Debug.WriteLine($"ALE::ParseStatus - station: Too few words -- min 2 ('{words}')");
                return;
            }

            ALEScanList scan_list = new ALEScanList();

            string[] scanListWords = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "scan_list"

            foreach (string kv in scanListWords)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("ALEComposite::ParseStatus - scan_list: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "name": scan_list.Name = value; break;
                    case "path":
                        {
                            //We need a composite path object to add to this scan list.

                            //Is there already a composite path in CompositePathList that matches
                            //the path id coming in?

                            ALECompositePath found_composite_path = CompositePathList.Find(x => x.ID == value);

                            if (found_composite_path != null)
                            {
                                //Yes, a composite path with a matching ID exists.

                                //Now- Does this scan list already exist?  We check this by
                                //looking in the list of scan lists (ScanLists) for a scan list with
                                //the name coming into the parser.

                                ALEScanList found_scan_list = _scanLists.Find(x => x.Name == scan_list.Name);

                                if (found_scan_list != null)
                                {
                                    //Yes, this scan list already exists in ScanLists

                                    //Next- Does the path id coming in already exist in the found scan list?  We
                                    //check this by looking in the scan list's list of composite paths.  (See
                                    //the structure of object type ALEScanList at the bottom of this document.)

                                    ALECompositePath found_path_in_list = found_scan_list.Paths.Find(x => x.ID == value);

                                    if (found_path_in_list != null)
                                    {
                                        //Yes, this path is already in the scan list's list of paths.

                                        //Set the new scan list to the found scan list.  We will
                                        //remove/re-add it to the ScanLists later.  (This process is
                                        //redudant, but consistent with other lists made from ALE parsers.)

                                        scan_list = found_scan_list;
                                    }
                                    else
                                    {
                                        //No, this composite path is not aleady in the scan list.

                                        //Add the path to the scan_list object.  The scan list object
                                        //will be added to ScanLists later.

                                        found_scan_list.Paths.Add(found_composite_path);
                                        scan_list = found_scan_list;
                                    }
                                }
                                else
                                {
                                    //No, this scan list does not exist in list ScanLists yet.

                                    //Add this path to the object scan_list, which will be added
                                    //to ScanLists later.

                                    scan_list.Paths.Add(found_composite_path);
                                }
                            }
                            else
                            {
                                //No, a composite path with a matching id does not exist in CompositePathList.

                                //Give error message and do nothing.

                                Debug.WriteLine("ALEComposite::ParseStatus - scan_list: Composite Path Does Not Exist.");
                            }

                        }
                        break;
                }
            }

            // is this a remove status?
            if (words.Length == 3 && //"scan_list name=<name> removed"
                words[2] == "removed" &&
                    words[1].StartsWith("name="))
            {
                // yes -- remove the scan list
                RemoveScanList(scan_list.Name);
                RaisePropertyChanged("ScanLists");
            }
            else
            {
                // no -- add the scan list
                lock (_scanLists)
                {
                    //if scan list already exists, delete the old one to replace with new scan list object
                    ALEScanList oldScanList = _scanLists.Find(sl => sl.Name == scan_list.Name);
                    if (oldScanList != null)
                    {
                        RemoveScanList(oldScanList.Name);
                    }
                    //add the new scan list
                    _scanLists.Add(scan_list);
                }
                RaisePropertyChanged("ScanLists");

                OnALEScanListAdded(scan_list);
            }
        }

        #endregion

        #region Multipoints
        private List<ALEMultipoint> _multipointList = [];
        public List<ALEMultipoint> MultipointList
        {
            get
            {
                if (_multipointList == null) return null;
                lock (_multipointList)
                    return _multipointList;
            }
        }

        internal void ParseMultipointPairing(string[] words)
        {
            // expected name=<mp name> config=<config name> paired/unpaired
            string mpName = string.Empty;
            string configName = string.Empty;
            bool tryPairing = true;
            foreach (string word in words)
            {
                string[] tokens = word.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine($"ALEComposite::ParseMultipointPairing: Invalid key/value pair ({word})");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];
                switch (key)
                {
                    case "name":
                        {
                            mpName = value;
                            break;
                        }
                    case "config":
                        {
                            configName = value;
                            break;
                        }
                    case "paired":
                        {
                            tryPairing = true;
                            break;
                        }
                    case "unpaired":
                        {
                            tryPairing = false;
                            break;
                        }
                }
            }
            var config = ConfigurationList.FirstOrDefault(c => c.Name == configName);
            if (config is null)
            {
                Debug.WriteLine($"ALEComposite::ParseMultipointPairing - unknown config {configName}");
                return;
            }
            bool isCurrentConfig = (config == SelectedALEConfig);
            var mp = MultipointList.FirstOrDefault(m => m.Name == mpName);
            if (mp is null)
            {
                Debug.WriteLine($"ALEComposite::ParseMultipointPairing - unknown multipoint {mpName}");
                return;
            }
            var pairedMultipoint = config.Multipoints.FirstOrDefault(m => m.Name == mpName);

            if (tryPairing && pairedMultipoint is null)
            {
                config.Multipoints.Add(mp);
            }
            else if (!tryPairing && pairedMultipoint is not null)
            {
                config.Multipoints.Remove(pairedMultipoint);
            }
            RaisePropertyChanged(nameof(ConfigurationList));
            if (isCurrentConfig)
            {
                RaisePropertyChanged(nameof(SelectedALEConfig));
            }
        }

        internal void ParseMultipointStatus(string s)
        {
            ALEMultipoint mp = new();
            bool valid = true;
            bool stationsListSet = false;
            string[] words = s.Split(' ');

            if (words[0] == "pair")
            {
                ParseMultipointPairing(words.Skip(1).ToArray());
                return;
            }

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine($"ALEComposite::ParseMultipointStatus: Invalid key/value pair ({kv})");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];
                switch (key)
                {
                    case "name":
                        {
                            mp.Name = value;
                            break;
                        }
                    case "addr":
                        {
                            mp.Address = value;
                            break;
                        }
                    case "3g_stations":
                        {
                            if (stationsListSet)
                            {
                                // There is some parsing error, so do not try constructing the multipoint.
                                Debug.WriteLine($"ALEComposite::ParseMultipointStatus - encountered multiple station lists '{s}'");
                                return;
                            }
                            stationsListSet = true;
                            break;
                        }
                    case "4g_stations":
                        {
                            if (stationsListSet)
                            {
                                // There is some parsing error, so do not try constructing the multipoint.
                                Debug.WriteLine($"ALEComposite::ParseMultipointStatus - encountered multiple station lists '{s}'");
                                return;
                            }
                            stationsListSet = true;

                            string[] stationNames = value.Split(',');
                            foreach (string stationName in stationNames)
                            {
                                if (string.IsNullOrEmpty(stationName)) continue;
                                var stn = _ale4g?.StationList.FirstOrDefault(x => x.Name == stationNames[0]);
                                if (stn is not null) { mp.Stations4G.Add(stn); }
                            }
                            break;
                        }
                    case "valid":
                        {
                            if (uint.TryParse(value, out uint temp))
                            {
                                valid = Convert.ToBoolean(temp);
                            }
                            break;
                        }
                }
            }
            // The multipoint is valid if it did not explicit say it wasn't, and if it had 3G or 4G stations.
            mp.IsValid = valid || mp.Stations4G?.Count > 0 || mp.Stations3G?.Count > 0;

            // Add to our current list. This may later be paired to one or more specific configurations.
            // TODO: Handle removal of multipoints at runtime.
            if (MultipointList.Contains(mp)) return;
            MultipointList.Add(mp);
            RaisePropertyChanged(nameof(MultipointList));
        }
        #endregion

        #region Station Groups
        private List<ALEStationGroup> _stationGroupList = [];
        public List<ALEStationGroup> StationGroupList
        {
            get
            {
                if (_stationGroupList == null) return null;
                lock (_stationGroupList)
                    return _stationGroupList;
            }
        }

        internal void ParseStationGroupPairing(string[] words)
        {
            // expected name=<sg name> config=<config name> paired/unpaired
            string sgName = string.Empty;
            string configName = string.Empty;
            bool tryPairing = true;
            foreach (string word in words)
            {
                string[] tokens = word.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine($"ALEComposite::ParseStationGroupPairing: Invalid key/value pair ({word})");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];
                switch (key)
                {
                    case "name":
                        {
                            sgName = value;
                            break;
                        }
                    case "config":
                        {
                            configName = value;
                            break;
                        }
                    case "paired":
                        {
                            tryPairing = true;
                            break;
                        }
                    case "unpaired":
                        {
                            tryPairing = false;
                            break;
                        }
                }
            }
            var config = ConfigurationList.FirstOrDefault(c => c.Name == configName);
            if (config is null)
            {
                Debug.WriteLine($"ALEComposite::ParseStationGroupPairing - unknown config {configName}");
                return;
            }
            bool isCurrentConfig = (config == SelectedALEConfig);
            var sg = StationGroupList.FirstOrDefault(s => s.Name == sgName);
            if (sg is null)
            {
                Debug.WriteLine($"ALEComposite::ParseStationGroupPairing - unknown station group {sgName}");
                return;
            }
            var pairedStationGroup = config.StationGroups.FirstOrDefault(s => s.Name == sgName);

            if (tryPairing && pairedStationGroup is null)
            {
                config.StationGroups.Add(sg);
            }
            else if (!tryPairing && pairedStationGroup is not null)
            {
                config.StationGroups.Remove(pairedStationGroup);
            }
            RaisePropertyChanged(nameof(ConfigurationList));
            if (isCurrentConfig)
            {
                RaisePropertyChanged(nameof(SelectedALEConfig));
            }
        }

        internal void ParseStationGroupStatus(string s)
        {
            ALEStationGroup sg = new();
            bool hasStations = false;
            string[] words = s.Split(' ');

            if (words[0] == "pair")
            {
                ParseStationGroupPairing(words.Skip(1).ToArray());
                return;
            }

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine($"ALEComposite::ParseStationGroupStatus: Invalid key/value pair ({kv})");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];
                switch (key)
                {
                    case "name":
                        {
                            sg.Name = value;
                            break;
                        }
                    case "supports_2g":
                    case "supports_3g":
                    case "supports_4g":
                        {
                            if (uint.TryParse(value, out uint temp))
                            {
                                hasStations |= Convert.ToBoolean(temp);
                            }
                            break;
                        }
                    case "2g_stations":
                        {
                            string[] stationNames = value.Split(',');
                            foreach (string stationName in stationNames)
                            {
                                if (string.IsNullOrEmpty(stationName)) continue;
                                var stn = _ale2g?.StationList.FirstOrDefault(x => x.Name == stationNames[0]);
                                if (stn is not null)
                                {
                                    sg.Stations2G.Add(stn);
                                }
                            }
                            break;
                        }
                    case "3g_stations":
                        {
                            string[] stationNames = value.Split(',');
                            foreach (string stationName in stationNames)
                            {
                                if (string.IsNullOrEmpty(stationName)) continue;
                                var stn = _ale3g?.StationList.FirstOrDefault(x => x.Name == stationNames[0]);
                                if (stn is not null)
                                {
                                    sg.Stations3G.Add(stn);
                                }
                            }
                            break;
                        }
                    case "4g_stations":
                        {
                            string[] stationNames = value.Split(',');
                            foreach (string stationName in stationNames)
                            {
                                if (string.IsNullOrEmpty(stationName)) continue;
                                var stn = _ale4g?.StationList.FirstOrDefault(x => x.Name == stationNames[0]);
                                if (stn is not null)
                                {
                                    sg.Stations4G.Add(stn);
                                }
                            }
                            break;
                        }
                }
            }

            // The station group is valid if it claims to support any of 2G/3G/4G and if there were provided stations that were already created.
            sg.IsValid = hasStations && (sg.Stations2G?.Count > 0 || sg.Stations3G?.Count > 0 || sg.Stations4G?.Count > 0);
            // Add to our current list. This may later be paired to one or more specific configurations.
            // TODO: Handle removal of station groups at runtime.
            if (StationGroupList.Contains(sg)) return;
            StationGroupList.Add(sg);
            RaisePropertyChanged(nameof(StationGroupList));
        }
        #endregion

        #region Configuration

        /// <summary>
        /// List of all ale configurations
        /// </summary>
        private List<ALEConfiguration> _configurationList = new List<ALEConfiguration>();
        public List<ALEConfiguration> ConfigurationList
        {
            get
            {
                if (_configurationList == null) return null;
                lock (_configurationList)
                    return _configurationList;
            }
        }

        public delegate void ALEConfigurationAddedEventHandler(ALEConfiguration configuration);
        public event ALEConfigurationAddedEventHandler ALEConfigurationAdded;

        private void OnALEConfigurationAdded(ALEConfiguration configuration)
        {
            if (ALEConfigurationAdded != null)
                ALEConfigurationAdded(configuration);
        }

        public delegate void ALEConfigurationRemovedEventHandler(ALEConfiguration configuration);
        public event ALEConfigurationRemovedEventHandler ALEConfigurationRemoved;

        private void OnALEConfigurationRemoved(ALEConfiguration configuration)
        {
            if (ALEConfigurationRemoved != null)
                ALEConfigurationRemoved(configuration);
        }

        private void RemoveConfiguration(string type)
        {
            ALEConfiguration configuration_to_be_removed = null;
            lock (_configurationList)
            {
                foreach (ALEConfiguration configuration in _configurationList)
                {
                    if (configuration.Type == type)
                    {
                        configuration_to_be_removed = configuration;
                        break;
                    }
                }

                if (configuration_to_be_removed != null)
                    _configurationList.Remove(configuration_to_be_removed);
            }

            if (configuration_to_be_removed != null)
                OnALEConfigurationRemoved(configuration_to_be_removed);
        }

        internal void ParseConfigStatus(string[] configWords)
        {
            ALEConfiguration config = new ALEConfiguration();
            foreach (string kv in configWords)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    if (!String.IsNullOrEmpty(kv))
                    {
                        Debug.WriteLine("ALEComposite::ParseConfigStatus - composite path: Invalid key/value pair (" + kv + ")");
                    }
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "name":
                        {
                            config.Name = value;
                            break;
                        }
                    case "2g_config":
                        {
                            config.Has2GConfig = Convert.ToBoolean(Convert.ToInt32(value));
                            break;
                        }
                    case "2g_config_valid":
                        {
                            config.Valid2GConfig = Convert.ToBoolean(Convert.ToInt32(value));
                            break;
                        }
                    case "3g_config":
                        {
                            config.Has3GConfig = Convert.ToBoolean(Convert.ToInt32(value));
                            break;
                        }
                    case "3g_config_valid":
                        {
                            config.Valid3GConfig = Convert.ToBoolean(Convert.ToInt32(value));
                            break;
                        }
                    case "4g_config":
                        {
                            config.Has4GConfig = Convert.ToBoolean(Convert.ToInt32(value));
                            break;
                        }
                    case "4g_config_valid":
                        {
                            config.Valid4GConfig = Convert.ToBoolean(Convert.ToInt32(value));
                            break;
                        }
                    default:
                        {
                            Debug.WriteLine($"ALEComposite::ParseConfigStatus - unknown key '{key}'");
                            break;
                        }
                }
            }
            if (String.IsNullOrEmpty(config.Name))
            {
                Debug.WriteLine("ALEComposite::ParseConfigStatus - Failed to parse configuration.");
                return;
            }
            Debug.WriteLine($"Parsed new config {config.Name}, valid = {config.IsValid}");
            ConfigurationList.Add(config);
            RaisePropertyChanged(nameof(ConfigurationList));
        }

        internal void ParseConfigurationTypeStatus(string[] words)
        {
            if (words.Length < 2)
            {
                Debug.WriteLine($"ALEComposite::ParseConfigurationTypeStatus: Too few words -- min 2 ({words})");
                return;
            }

            ALEConfiguration configuration = new ALEConfiguration();

            string[] config_words = words.Skip(1).Take(words.Length - 1).ToArray(); // skip the "configuration"

            foreach (string kv in config_words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine($"ALEComposite::ParseConfigurationTypeStatus: Invalid key/value pair ({kv})");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "type": configuration.Type = value; break;
                    case "name": configuration.Name = value; break;
                }
            }

            // is this a remove status?
            if (words.Length == 3 && //"configuration type=<type> removed"
                words[2] == "removed" &&
                    words[1].StartsWith("type="))
            {
                // yes -- remove the configuration
                RemoveConfiguration(configuration.Type);
                RaisePropertyChanged(nameof(ConfigurationList));
            }
            else
            {
                // no -- this status is to indicate that the "type" of a config is being updated.
                lock (_configurationList)
                {
                    ALEConfiguration existingConfig = _configurationList.Find(c => c.Name == configuration.Name);
                    if (existingConfig is null)
                    {
                        // We will take no action if this status references a configuration not yet defined.
                        return;
                    }
                    // Type is currently only used to indicate which is the current selected one.
                    // The type string is expected to be "CURRENT_COMPOSITE". If it's anything else, don't do anything.
                    // Might be used in the future.
                    //if (configuration.Type == "CURRENT_COMPOSITE" && SelectedALEConfig?.Name != configuration.Name)
                    if (configuration.Type == "CURRENT_COMPOSITE")
                    {
                        existingConfig.Type = configuration.Type;
                        SelectedALEConfig = existingConfig;
                    }
                }
                //RaisePropertyChanged(nameof(ConfigurationList));
                OnALEConfigurationAdded(configuration);
            }
        }

        #endregion

        #region Pairings to Configurations

        private void ParseStationPairing(string s, ALEGen gen)
        {
            // expected "ale 2g/3g/4g pair config=<config name> station=<station name> scan_list=<scan list name> paired/unpaired
            // The "ale 2g/3g/4g pair " portion of the string is already skipped.
            string stationName = string.Empty;
            string configName = string.Empty;
            string scanListName = string.Empty;
            bool tryPairing = true;
            string[] words = s.Split(' ');
            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    switch(tokens[0])
                    {
                        case "paired":
                            {
                                tryPairing = true;
                                break;
                            }
                        case "unpaired":
                            {
                                tryPairing = false;
                                break;
                            }
                        default:
                            {
                                Debug.WriteLine($"ALEComposite::ParseStationPairing: Invalid key/value pair ({kv})");
                                break;
                            }
                    }
                    continue;
                }
                string key = tokens[0];
                string value = tokens[1];
                switch (key)
                {
                    case "config":
                        {
                            configName = value;
                            break;
                        }
                    case "station":
                        {
                            stationName = value;
                            break;
                        }
                    case "scan_list":
                        {
                            scanListName = value;
                            break;
                        }
                }
            }

            var config = ConfigurationList.FirstOrDefault(c => c.Name == configName);
            if (config is null)
            {
                Debug.WriteLine($"ALEComposite::ParseStationPairing - unknown config {configName}");
                return;
            }
            bool isCurrentConfig = (config == SelectedALEConfig);
            var scanList = ScanLists.FirstOrDefault(s => s.Name == scanListName);
            if (scanList is null)
            {
                Debug.WriteLine($"ALEComposite::ParseStationPairing - unknown scan list {scanListName}");
                return;
            }

            switch (gen)
            {
                case (ALEGen.Gen2G):
                    {
                        var station = _ale2g.StationList.FirstOrDefault(s => s.Name == stationName);
                        if (station is null)
                        {
                            Debug.WriteLine($"ALEComposite::ParseStationPairing - unknown 2G station {stationName}");
                            return;
                        }
                        var pairedStation = config.Stations2G.FirstOrDefault(s => s.Name == stationName);
                        if (tryPairing && pairedStation is null)
                        {
                            Debug.WriteLine($"ALEComposite::ParseStationPairing - pairing 2G station {stationName} to config {configName}");
                            config.Stations2G.Add(station);
                        }
                        else if (!tryPairing && pairedStation is not null)
                        {
                            Debug.WriteLine($"ALEComposite::ParseStationPairing - unpairing 2G station {stationName} from config {configName}");
                            config.Stations2G.Remove(pairedStation);
                        }
                        break;
                    }
                case (ALEGen.Gen3G):
                    {
                        var station = _ale3g.StationList.FirstOrDefault(s => s.Name == stationName);
                        if (station is null)
                        {
                            Debug.WriteLine($"ALEComposite::ParseStationPairing - unknown 3G station {stationName}");
                            return;
                        }
                        var pairedStation = config.Stations3G.FirstOrDefault(s => s.Name == stationName);
                        if (tryPairing && pairedStation is null)
                        {
                            Debug.WriteLine($"ALEComposite::ParseStationPairing - pairing 3G station {stationName} to config {configName}");
                            config.Stations3G.Add(station);
                        }
                        else if (!tryPairing && pairedStation is not null)
                        {
                            Debug.WriteLine($"ALEComposite::ParseStationPairing - unpairing 3G station {stationName} from config {configName}");
                            config.Stations3G.Remove(pairedStation);
                        }
                        break;
                    }
                case (ALEGen.Gen4G):
                    {
                        var station = _ale4g.StationList.FirstOrDefault(s => s.Name == stationName);
                        if (station is null)
                        {
                            Debug.WriteLine($"ALEComposite::ParseStationPairing - unknown 4G station {stationName}");
                            return;
                        }
                        var pairedStation = config.Stations4G.FirstOrDefault(s => s.Name == stationName);
                        if (tryPairing && pairedStation is null)
                        {
                            Debug.WriteLine($"ALEComposite::ParseStationPairing - pairing 4G station {stationName} to config {configName}");
                            config.Stations4G.Add(station);
                        }
                        else if (!tryPairing && pairedStation is not null)
                        {
                            Debug.WriteLine($"ALEComposite::ParseStationPairing - unpairing 4G station {stationName} from config {configName}");
                            config.Stations4G.Remove(pairedStation);
                        }
                        break;
                    }
            }

            var pairedScanList = config.ScanLists.FirstOrDefault(s => s.Name == scanListName);
            if (pairedScanList is null)
            {
                Debug.WriteLine($"ALEComposite::ParseStationPairing - pairing scan list {scanListName} to config {configName}");
                config.ScanLists.Add(scanList);
            }
        }

        internal void Parse2GStationPairing(string s)
        {
            ParseStationPairing(s, ALEGen.Gen2G);
        }

        internal void Parse3GStationPairing(string s)
        {
            ParseStationPairing(s, ALEGen.Gen3G);
        }

        internal void Parse4GStationPairing(string s)
        {
            ParseStationPairing(s, ALEGen.Gen4G);
        }

        #endregion

        internal void ParseStatus(string s)
        {
            string[] words = s.Split(' ');

            switch (words[0])
            {
                case "composite":
                    {
                        if (words.Length < 2)
                        {
                            Debug.WriteLine($"ALEComposite::ParseStatus - Too few words -- min 2 ({words})");
                            return;
                        }

                        // ale composite config name=<..> 2g_config=<0|1> 2g_config_valid=<0|1> 3g_config=<0|1> 3g_config_valid=<0|1> 4g_config=<0|1> 4g_config_valid=<0|1>
                        if (words[1] == "config")
                        {
                            ParseConfigStatus(words.Skip(2).Take(words.Length - 2).ToArray()); // skip "composite config"
                            break;
                        }
                        //ale composite path id=<ID> frequency=<frequency> 2g_path=<1|0> 3g_path=<1|0> 4g_path=<1|0>
                        else if (words[1] == "path")
                        {
                            ParsePathStatus(words);
                            break;
                        }
                        break;
                    }
                case "scan_list": //scan_list name=<name> path=<path>
                    {
                        ParseScanListStatus(words);
                        break;
                    }
                case "configuration": //configuration type=<type> name=<name>
                    {
                        ParseConfigurationTypeStatus(words);
                        break;
                    }
                default:
                    {
                        Debug.WriteLine($"ALEComposite::ParseStatus - unexpected field {words[0]}");
                        break;
                    }
            }
        }

        private ALEConfiguration _selectedALEConfig;
        public ALEConfiguration SelectedALEConfig
        {
            get => _selectedALEConfig;
            set
            {
                if (value == _selectedALEConfig) return;
                _selectedALEConfig = value;
                SendConfigCommand($"ale_config apply name={_selectedALEConfig.Name}");
                RaisePropertyChanged(nameof(SelectedALEConfig));
            }
        }

        private string _ALECompositeCurrentStatusLabel = _defaultALECompositeStatus;
        public string ALECompositeCurrentStatusLabel
        {
            get => _ALECompositeCurrentStatusLabel;
            set
            {
                _ALECompositeCurrentStatusLabel = value;
                RaisePropertyChanged(nameof(ALECompositeCurrentStatusLabel));
            }
        }

        private bool _ALE2GControlsActive;
        public bool ALE2GControlsActive
        {
            get => _ALE2GControlsActive;
            set
            {
                if (value == _ALE2GControlsActive) return;
                _ALE2GControlsActive = value;
                RaisePropertyChanged(nameof(ALE2GControlsActive));
            }
        }

        private bool _ALE3GControlsActive;
        public bool ALE3GControlsActive
        {
            get => _ALE3GControlsActive;
            set
            {
                if (value == _ALE3GControlsActive) return;
                _ALE3GControlsActive = value;
                RaisePropertyChanged(nameof(ALE3GControlsActive));
            }
        }

        private bool _ALE4GControlsActive;
        public bool ALE4GControlsActive
        {
            get => _ALE4GControlsActive;
            set
            {
                if (value == _ALE4GControlsActive) return;
                _ALE4GControlsActive = value;
                RaisePropertyChanged(nameof(ALE4GControlsActive));
            }
        }

        public string ALEGlobalLinkStatus
        {
            get
            {
                if (_ale2g is not null)
                {
                    if (_ale2g.Link) return "Linked - 2G";
                    if (_ale2g.Linking) return "Linking - 2G";
                }
                if (_ale3g is not null)
                {
                    if (_ale3g.Link) return "Linked - 3G";
                    if (_ale3g.Linking) return "Linking - 3G";
                }
                if (_ale4g is not null)
                {
                    if (_ale4g.Link) return "Linked - 4G";
                    if (_ale4g.Linking) return "Linking - 4G";
                }
                return "Not Linked";
            }
        }

        public readonly static Dictionary<ALEGen, List<ALELinkType>> SupportedLinkTypesByGen = new Dictionary<ALEGen, List<ALELinkType>>{
            {ALEGen.Gen2G, [ALELinkType.StationToStation, ALELinkType.Network, ALELinkType.Group, ALELinkType.Multipoint, ALELinkType.AutoAddress, ALELinkType.Allcall, ALELinkType.Anycall]},
            {ALEGen.Gen3G, [ALELinkType.StationToStation, ALELinkType.Multipoint, ALELinkType.AutoAddress] },
            {ALEGen.Gen4G, [ALELinkType.StationToStation, ALELinkType.Multipoint, ALELinkType.AutoAddress] },
        };

        public readonly string AllcallAddress = "@?@";
        public readonly string AnycallAddress = "@@?";
        private static readonly string _defaultALECompositeStatus = "Not Active";
    }

    public class ALECompositePath
    {
        public string ID { get; set; }
        public string Frequency { get; set; }
        public bool Is2GPath { get; set; }
        public bool Is3GPath { get; set; }
        public bool Is4GPath { get; set; }
    }

    public class ALEMultipoint : ALECallee
    {
        public string Address { get; set; }
        public bool IsValid { get; set; }
        public ALEGen Gen { get; set; } = ALEGen.Gen3G;
        // TODO - consume which stations are paired to the multipoint to show the user.
        // This is a nice-to-have and not necessary to make calls using multipoint.
        public List<ALE3GStation> Stations3G { get; set; } = null;
        public List<ALE4GStation> Stations4G { get; set; } = null;
    }

    public class ALEStationGroup : ALECallee
    {
        public ALEStationGroup()
        {
            Stations2G = [];
            Stations3G = [];
            Stations4G = [];
        }
        public bool IsValid { get; set; }
        // TODO - consume which stations are paired to the station group to show the user.
        // This is a nice-to-have and not necessary to make calls using auto-addressing aka station groups.
        public List<ALE2GStation> Stations2G { get; set; }
        public List<ALE3GStation> Stations3G { get; set; }
        public List<ALE4GStation> Stations4G { get; set; }
    }

    public class ALEScanList
    {
        public string Name { get; set; }
        public List<ALECompositePath> Paths = new List<ALECompositePath>();
    }

    public class ALECallee
    {
        public string Name { get; set; }
    }

    public class ALEConfiguration
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public bool Has2GConfig {  get; set; }
        public bool Valid2GConfig { get; set; }
        public bool Has3GConfig { get; set; }
        public bool Valid3GConfig { get; set; }
        public bool Has4GConfig { get; set; }
        public bool Valid4GConfig { get; set; }
        // Valid configuration has a name, and at least one valid generation of ALE. Type is optional.
        public bool IsValid => ((Has2GConfig && Valid2GConfig) || (Has3GConfig && Valid3GConfig) || (Has4GConfig && Valid4GConfig));

        // Coomponents in a configuration: paths, stations, scan lists, networks (2G only), multipoints (3G and 4G), station groups
        public List<ALE2GStation> Stations2G { get; set; } = [];
        public List<ALE2GNetwork> Networks2G { get; set; } = [];
        public List<ALE3GStation> Stations3G { get; set; } = [];
        public List<ALE4GStation> Stations4G { get; set; } = [];
        public List<ALEMultipoint> Multipoints { get; set; } = [];
        public List<ALEScanList> ScanLists { get; set; } = [];
        public List<ALECompositePath> Paths { get; set; } = [];
        public List<ALEStationGroup> StationGroups { get; set; } = [];
    }
}
