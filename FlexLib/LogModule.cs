// ****************************************************************************
///*!	\file LogModule.cs
// *	\brief Represents a single log module
// *
// *	\copyright	Copyright 2012-2022 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2022-02-01
// *	\author Jessica Temte
// */
// ****************************************************************************



using Flex.UiWpfFramework.Mvvm;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Smoothlake.FlexLib
{
    public class LogModule : ObservableObject
    {
        private Radio _radio;
        public Radio Radio
        {
            get { return _radio; }
        }

        public LogModule(Radio radio)
        {
            _radio = radio;
        }

        private string _moduleName;
        /// <summary>
        /// Name of log module
        /// </summary>
        public string ModuleName
        {
            get { return _moduleName; }
            set
            {
                if (_moduleName != value)
                {
                    _moduleName = value;
                    RaisePropertyChanged("ModuleName");
                }
            }
        }

        private string _logLevel;
        /// <summary>
        /// Log level currently active for this module
        /// </summary>
        public string LogLevel
        {
            get { return _logLevel; }
            set
            {
                if (_logLevel != value)
                {
                    _logLevel = value;
                    _radio.SendCommand("log module=" + _moduleName + " level=" + _logLevel); ;
                    RaisePropertyChanged("LogLevel");
                }
            }
        }

        private string[] _logLevels;
        /// <summary>
        /// List of available log levels for this module
        /// </summary>
        public string[] LogLevels
        {
            get { return _logLevels; }
            set
            {
                if (_logLevels != value)
                {
                    _logLevels = value;
                    RaisePropertyChanged("LogLevels");
                }
            }
        }

        public void StatusUpdate(string s)
        {
            //"module=<module> level=<level>"
            string[] words = s.Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("LogModule::ParseStatus - status: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "module":
                        {
                            _moduleName = value;
                            RaisePropertyChanged("ModuleName");
                        }
                        break;
                    case "level":
                        {
                            //create list
                            _logLevels = _radio.LogLevels;
                            RaisePropertyChanged("LogLevels");

                            //assign level
                            _logLevel = value;
                            RaisePropertyChanged("LogLevel");
                        }
                        break;
                }
            }
        }
    }
}
