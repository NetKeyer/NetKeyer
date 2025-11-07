// ****************************************************************************
///*!	\file CWX.cs
// *	\brief Character based CW system
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2014-07-03
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Flex.UiWpfFramework.Mvvm;


namespace Flex.Smoothlake.FlexLib
{
    public class CWX : ObservableObject
    {
        private Radio _radio;
        private const int MAX_NUM_MACROS = 12;
        private const int MAX_CWX_DELAY_MS = 2000;
        private const int MIN_CWX_SPEED = 5;
        private const int MAX_CWX_SPEED = 100;

        internal CWX(Radio radio)
        {
            _radio = radio;
            _macros = new string[MAX_NUM_MACROS];
        }

        /// <summary>
        /// Saves a macro for sending later
        /// </summary>
        /// <param name="index">A number from 0-11</param>
        /// <param name="msg">The string to save in the macro</param>
        private void SaveMacro(int index, string msg)
        {
            _radio.SendCommand("cwx macro save " + (index + 1) + " \"" + msg + "\"");
        }

        // send a macro
        public int SendMacro(int index)
        {
            return _radio.SendReplyCommand(new ReplyHandler(QueueMessageReply), "cwx macro send " + index);
        }

        private void QueueMessageReply(int seq, uint resp_val, string reply)
        {
            if (resp_val != 0) return;

            if (reply.IndexOf(",") < 0)
            {
                Debug.WriteLine("CWX::QueueMessageReply: Expected a block in the reply, but did not see one (Reply: \""+reply+"\")");
                return;
            }

            string[] words = reply.Split(',');

            int radio_index, block;
            bool b = int.TryParse(words[0], out radio_index);

            if (!b)
            {
                Debug.WriteLine("CWX::QueueMessageReply: Error parsing character position (" + reply + ")");
                return;
            }

            b = int.TryParse(words[1], out block);
            if (!b)
            {
                Debug.WriteLine("CWX::QueueMessageReply: Error parsing block (" + reply + ")");
                return;
            }

            OnMessageQueued(block, radio_index);
        }

        public int Send(string s)
        {
            //Debug.WriteLine("CWX.Send(" + s + ")");
            string msg = s.Replace(' ', '\u007f');
            return _radio.SendCommand("cwx send \"" + msg + "\"");
        }

        public int Send(string s, int block)
        {
            //Debug.WriteLine("CWX.Send(" + s + ", " + block + ")");
            
            // handle spaces carefully
            string msg = s.Replace(' ', '\u007f');
            return _radio.SendReplyCommand(new ReplyHandler(QueueMessageReply), "cwx send \"" + msg + "\" " + block);
        }

        public void Erase(int num_chars)
        {
            //Debug.WriteLine("CWX.Erase(" + num_chars + ")");
            // Note that the reply need not be handled here as the async status will take care of it
            _radio.SendCommand("cwx erase " + num_chars);
        }

        public void Erase(int num_chars, int radio_index)
        {
            //Debug.WriteLine("CWX.Erase(" + num_chars + ", " + radio_index + ")");
            //_radio.SendReplyCommand(new ReplyHandler(EraseReply), "cwx erase " + num_chars + " " + radio_index); // use this when we stop using status messages for CWX
            _radio.SendCommand("cwx erase " + num_chars + " " + radio_index);
        }

        /*private void EraseReply(int seq, uint resp_val, string reply)
        {
            if (resp_val != 0) return;

            if (reply.IndexOf(",") < 0)
            {
                Debug.WriteLine("CWX::EraseReply: Expected a block in the reply, but did not see one (Reply: \"" + reply + "\")");
                return;
            }

            string[] words = reply.Split(',');

            if (words.Length != 2) return;

            int erase_start_index, erase_stop_index;
            bool b = int.TryParse(words[0], out erase_start_index);
            if (!b)
            {
                Debug.WriteLine("CWX::EraseReply: Error parsing erase_start_index (" + reply + ")");
                return;
            }

            b = int.TryParse(words[1], out erase_stop_index);
            if (!b)
            {
                Debug.WriteLine("CWX::EraseReply: Error parsing erase_stop_index (" + reply + ")");
                return;
            }

            OnEraseSent(erase_start_index, erase_stop_index);
        }*/

        public void Insert(int radio_index, string s, int block)
        {
            Debug.WriteLine("CWX.Insert(" + radio_index + ", " + s + ", " + block + ")");
            string msg = s.Replace(' ', '\u007f');
            _radio.SendReplyCommand(new ReplyHandler(InsertReply), "cwx insert " + radio_index + " \"" + msg + "\" " + block);
        }

        private void InsertReply(int seq, uint resp_val, string reply)
        {
            if (resp_val != 0) return;

            if (reply.IndexOf(",") < 0)
            {
                Debug.WriteLine("CWX::InsertReply: Expected a block in the reply, but did not see one (Reply: \"" + reply + "\")");
                return;
            }

            string[] words = reply.Split(',');

            if (words.Length != 2) return;

            int radio_index, block;
            bool b = int.TryParse(words[0], out radio_index);
            if (!b)
            {
                Debug.WriteLine("CWX::InsertReply: Error parsing radio_index (" + reply + ")");
                return;
            }
                   
            b = int.TryParse(words[1], out block);
            if (!b)
            {
                Debug.WriteLine("CWX::InsertReply: Error parsing block (" + reply + ")");
                return;
            }

            OnInsertQueued(block, radio_index);
        }

        public double getTXFrequency()
        {
            lock (_radio.SliceList)
            {
                foreach (Slice s in _radio.SliceList)
                {
                    if (s.IsTransmitSlice && s.ClientHandle == _radio.ClientHandle)
                    {
                        return s.Freq;
                    }
                }
            }
            return 0.0;
        }

        public void ClearBuffer()
        {
            // Note that the reply need not be handled here as the async status will take care of it
            _radio.SendCommand("cwx clear");
        }

        // replace special strings with their replacement
        // ensure that this happens properly even at the end of the string
        /*private string HandleSpecialStrings(string s)
        {
            string ret_val = s;
            ret_val = ret_val.Replace(" BT ", " = ");
            if (ret_val.EndsWith(" BT"))
                ret_val = ret_val.Substring(0, ret_val.Length - 3) + " =";

            ret_val = ret_val.Replace(" AR ", " + ");
            if (ret_val.EndsWith(" AR"))
                ret_val = ret_val.Substring(0, ret_val.Length - 3) + " +";
            
            ret_val = ret_val.Replace(" KN ", " ( ");
            if (ret_val.EndsWith(" KN"))
                ret_val = ret_val.Substring(0, ret_val.Length - 3) + " (";
            
            ret_val = ret_val.Replace(" BK ", " & ");
            if (ret_val.EndsWith(" BK"))
                ret_val = ret_val.Substring(0, ret_val.Length - 3) + " &";
            
            ret_val = ret_val.Replace(" SK ", " $ ");
            if (ret_val.EndsWith(" SK"))
                ret_val = ret_val.Substring(0, ret_val.Length - 3) + " $";

            return ret_val;
        }*/

        private string[] _macros;
        public string[] Macros
        {
            get { return _macros; }
        }

        public bool SetMacro(int index, string s)
        {
            if (index < 0 || index > MAX_NUM_MACROS-1) return false;
            //string temp = HandleSpecialStrings(s);
            SaveMacro(index, s);
            return true;
        }

        public bool GetMacro(int index, out string s)
        {
            s = "";
            if (index < 0 || index > MAX_NUM_MACROS-1) return false;
            s = _macros[index];
            return true;
        }


        private int _delay;
        /// <summary>
        /// The CWX breakin delay in milliseconds (ms) from 0 ms to 2000 ms
        /// </summary>
        public int Delay
        {
            get { return _delay; }
            set
            {
                int new_delay = value;

                if (new_delay < 0) new_delay = 0;
                if (new_delay > MAX_CWX_DELAY_MS) new_delay = MAX_CWX_DELAY_MS;

                if (_delay != new_delay)
                {
                    _delay = new_delay;
                    _radio.SendCommand("cwx delay " + _delay);
                    RaisePropertyChanged("Delay");
                }
                else if (new_delay != value)
                {
                    RaisePropertyChanged("Delay");
                }
            }
        }

        private int _speed;
        /// <summary>
        /// The CWX speed in words per minute (wpm) from 5 to 100
        /// </summary>
        public int Speed
        {
            get { return _speed; }
            set
            {
                int new_speed = value;

                if (new_speed < MIN_CWX_SPEED) new_speed = MIN_CWX_SPEED;
                if (new_speed > MAX_CWX_SPEED) new_speed = MAX_CWX_SPEED;

                if (_speed != new_speed)
                {
                    _speed = new_speed;
                    _radio.SendCommand("cwx wpm " + _speed);
                    RaisePropertyChanged("Speed");
                }
                else if (new_speed != value)
                {
                    RaisePropertyChanged("Speed");
                }
            }
        }

        private bool _qskEnabled;
        public bool QskEnabled
        {
            get { return _qskEnabled; }
            set
            {
                if (_qskEnabled != value)
                {
                    _qskEnabled = value;
                    _radio.SendCommand("cwx qsk_enabled " + (_qskEnabled ? "1" : "0"));
                    RaisePropertyChanged("QskEnabled");
                }
            }
        }

        public delegate void MessageQueuedEventHandler(int block, int radio_index);
        public event MessageQueuedEventHandler MessageQueued;

        private void OnMessageQueued(int block, int radio_index)
        {
            if (MessageQueued != null)
                MessageQueued(block, radio_index);
        }

        public delegate void CharSentEventHandler(int radio_index);
        public event CharSentEventHandler CharSent;

        private void OnCharSent(int radio_index)
        {
            if (CharSent != null)
                CharSent(radio_index);
        }

        public delegate void EraseSentEventHandler(int start, int stop);
        public event EraseSentEventHandler EraseSent;

        private void OnEraseSent(int start, int stop)
        {
            if (EraseSent != null)
                EraseSent(start, stop);
        }

        public delegate void InsertQueuedEventHandler(int block, int radio_index);
        public event InsertQueuedEventHandler InsertQueued;

        private void OnInsertQueued(int block, int radio_index)
        {
            if (InsertQueued != null)
                InsertQueued(block, radio_index);
        }
        
        internal void StatusUpdate(string s)
        {
            // We could have spaces inside quotes, so we have to convert them to something else for the split.
            // We could also have an equal sign '=' (for Porosign BT) inside the quotes, so we're converting to a '*' so that the split on "="
            // will still work.  This will prevent the character '*' from being stored in a macro.  Using the ascii byte for '=' will not work.
            StringBuilder sb = new StringBuilder();
            StringBuilder sbv = new StringBuilder();

            int i;
            bool quotes = false;
            for (i = 0; i < s.Length; i++)
            {
                string c = s.Substring(i,1);
                if (c == "\"")
                    quotes = !quotes;
                else if (c == " " && quotes)
                    sb.Append('\u007f');
                else if (c == "=" && quotes)
                    sb.Append('*');
                else
                    sb.Append(c);
            }

            string[] words = sb.ToString().Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    Debug.WriteLine("CWX::StatusUpdate: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0];
                string value = tokens[1];

                switch (key.ToLower())
                {
                    case "break_in_delay":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("CWX::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }
                            
                            _delay = (int)temp;
                            RaisePropertyChanged("Delay");
                        }
                        break;

                        // handled below in the default case
                    //case "macro":
                    //    {

                    //    }
                    //    break;

                     case "sent":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("CWX::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            OnCharSent((int)temp);
                        }
                        break;

                    case "wpm": // speed
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("CWX::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _speed = (int)temp;
                            RaisePropertyChanged("Speed");
                        }
                        break;

                    case "erase": // erasing characters previously sent to radio
                        {                            
                            string[] str = value.Split(',');
                            if (str.Length != 2)
                            {
                                Debug.WriteLine("CWX::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            uint start;
                            bool b = uint.TryParse(str[0], out start);

                            if (!b)
                            {
                                Debug.WriteLine("CWX::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            uint stop;
                            b = uint.TryParse(str[1], out stop);

                            if (!b)
                            {
                                Debug.WriteLine("CWX::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            OnEraseSent((int)start, (int)stop);
                        }
                        break;

                    case "qsk_enabled":
                        {
                            byte temp;
                            bool b = byte.TryParse(value, out temp);

                            if (!b)
                            {
                                Debug.WriteLine("CWX::StatusUpdate: Invalid value (" + kv + ")");
                                continue;
                            }

                            _qskEnabled = Convert.ToBoolean(temp);
                            RaisePropertyChanged("QskEnabled");
                        }
                        break;

                    default:
                        // handled here since you can't do wildcards in a regular case
                        if (key.StartsWith("macro") && key.Length > 5)
                        {
                            // get the macro index
                            uint index;
                            bool b = uint.TryParse(key.Substring(5), out index);

                            if(!b || index > MAX_NUM_MACROS)
                            {
                                Debug.WriteLine("CWX::StatusUpdate: Invalid macro value (" + kv + ")");
                                continue;
                            }

                            for (i = 0; i < value.Length; i++)
                            {
                                string c = value.Substring(i, 1);
                                if (c == "\u007f")
                                    sbv.Append(' ');
                                else if (c == "*")
                                    sbv.Append('=');
                                else
                                    sbv.Append(c);
                            }


                            _macros[index - 1] = sbv.ToString();
                            sbv.Clear();

                            RaisePropertyChanged("Macros");
                        }
                        else
                        {
                            Debug.WriteLine("CWX::StatusUpdate: Key not parsed (" + kv + ")");
                        }
                        break;
                }
            }
        }
    }
}
