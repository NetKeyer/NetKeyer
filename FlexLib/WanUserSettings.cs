// ****************************************************************************
///*!	\file WanUserSettings.cs
// *	\brief Helper class that contains all user-settable WAN settings for
// *            a user's account
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2017-04-20
// *	\author Abed Haque AB5ED
// */
// ****************************************************************************

using Flex.UiWpfFramework.Mvvm;
using System;

namespace Flex.Smoothlake.FlexLib
{
    public class WanUserSettings : ObservableObject, ICloneable
    {
        private string _callsign;

        public string Callsign
        {
            get { return _callsign; }
            set
            {
                _callsign = value;
                RaisePropertyChanged("Callsign");
            }
        }

        private string _firstName;

        public string FirstName
        {
            get { return _firstName; }
            set
            {
                _firstName = value;
                RaisePropertyChanged("FirstName");
            }
        }

        private string _lastName;

        public string LastName
        {
            get { return _lastName; }
            set
            {
                _lastName = value;
                RaisePropertyChanged("LastName");
            }
        }

        public void Clear()
        {
            FirstName = null;
            LastName = "";
            Callsign = "";
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}