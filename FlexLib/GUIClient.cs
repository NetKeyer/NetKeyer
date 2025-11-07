// ****************************************************************************
///*!	\file GUIClient.cs
// *	\brief Represents a single GUI Client for the radio
// *
// *	\copyright	Copyright 2018 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2018-10-18
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using Flex.UiWpfFramework.Mvvm;


namespace Flex.Smoothlake.FlexLib
{
    public class GUIClient : ObservableObject
    {
        public GUIClient(uint handle, string client_id, string program, string station, bool is_local_ptt, bool is_available=true)
        {
            _clientHandle = handle;
            _clientID = client_id;
            _program = program;
            _station = station;
            IsAvailable = is_available;
            _isLocalPtt = is_local_ptt;
        }

        private bool _isThisClient;
        public bool IsThisClient
        {
            get { return _isThisClient; }
            internal set
            {
                if (_isThisClient != value)
                {
                    _isThisClient = value;
                    RaisePropertyChanged("IsThisClient");
                }
            }
        }


        private uint _clientHandle;
        public uint ClientHandle
        {
            get { return _clientHandle; }
            set
            {
                if (_clientHandle != value)
                {
                    _clientHandle = value;
                    RaisePropertyChanged("ClientHandle");
                }
            }
        }

        private string _clientID;
        public string ClientID
        {
            get { return _clientID; }
            set
            {
                if (_clientID != value)
                {
                    _clientID = value;
                    RaisePropertyChanged("ClientID");
                }
            }
        }

        private string _program;
        public string Program
        {
            get { return _program; }
            set
            {
                if (_program != value)
                {
                    _program = value;
                    RaisePropertyChanged("Program");
                }
            }
        }

        private string _station;
        public string Station
        {
            get { return _station; }
            set
            {
                if (_station != value)
                {
                    _station = value;
                    RaisePropertyChanged("Station");
                }
            }
        }

        private bool _isLocalPtt;
        public bool IsLocalPtt
        {
            get { return _isLocalPtt; }
            set
            {
                if (_isLocalPtt != value)
                {
                    _isLocalPtt = value;
                    RaisePropertyChanged("IsLocalPtt");
                }
            }
        }

        public bool IsAvailable { get; }

        private Slice _transmitSlice;
        public Slice TransmitSlice
        {
            get { return _transmitSlice; }
            set
            {
                if (_transmitSlice != value)
                {
                    _transmitSlice = value;
                    RaisePropertyChanged("TransmitSlice");
                }
            }
        }

    }
}
