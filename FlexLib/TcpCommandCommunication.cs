// ****************************************************************************
///*!	\file CommandCommunication.cs
// *	\brief Handles the command pipe to the radio
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2017-01-12
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using Flex.UiWpfFramework.Mvvm;


namespace Flex.Smoothlake.FlexLib
{
    public class TcpCommandCommunication : ICommandCommunication
    {
        private TcpClient _tcpClient;
        private bool _isConnected = false;

        public bool IsConnected
        {
            get
            {
                return (_tcpClient != null && _isConnected);
            }
        }

        private IPAddress _localIP = IPAddress.Parse("0.0.0.0");
        /// <summary>
        /// The local client IP address
        /// </summary>
        public IPAddress LocalIP
        {
            set
            {
                _localIP = value;
            }
            get
            {
                // We are no longer looking up the IP Address by the IPEndPoint of this TCPClient
                // since if someone is connected directly to the radio remotely, the IP address
                // could also be the public IP address.  This value should now be obtained by
                // requesting it from the radio.
                return _localIP;
            }
        }

        private const int COMMAND_PORT = 4992;
        private const int TCP_READ_BUFFER_SIZE = 1024;

        public bool Connect(IPAddress radio_ip, bool setup_reply)
        {
            lock (this)
            {
                if (_tcpClient != null) return true;

                int count = 0;
                while (count++ <= 10)
                {
                    try
                    {
                        // create tcp client object and connect to the radio
                        _tcpClient = new TcpClient();
                        //_tcp_client.NoDelay = true; // hopefully minimize round trip command latency
                        _tcpClient.Connect(new IPEndPoint(radio_ip, COMMAND_PORT));
                        count = 20;
                    }
                    catch (Exception ex)
                    {
                        string s = "Radio::Connect() -- Error creating TCP client\n";
                        s += ex.Message;
                        if (ex.InnerException != null)
                            s += "\n\n" + ex.InnerException.Message;
                        Debug.WriteLine(s);

                        // clean up tcp client object
                        if (_tcpClient != null)
                            _tcpClient = null;

                        // this is likely due to trying to reconnect too quickly -- lets try again after waiting
                        Thread.Sleep(1000);
                    }
                }

                if (count < 20)
                    return false;

                _isConnected = true;
                OnIsConnectedChanged(_isConnected);

                if(setup_reply)
                {
                    // setup for reading messages coming from the radio
                    lock (_tcpReadSyncObj)
                        _tcpClient.GetStream().BeginRead(_tcpReadByteBuffer, 0, TCP_READ_BUFFER_SIZE, new AsyncCallback(TCPReadCallback), null);
                }

                return true;
            }
        }

        private object _tcpReadSyncObj = new object();
        private byte[] _tcpReadByteBuffer = new byte[TCP_READ_BUFFER_SIZE];
        private string _tcpReadStringBuffer = "";
        private void TCPReadCallback(IAsyncResult ar)
        {
            // keep more than one caller from entering the callback at once to
            // prevent issues with the string buffering
            lock (_tcpReadSyncObj)
            {
                if (_tcpClient == null)
                {
                    Disconnect();
                    API.LogDisconnect("CommandCommunication::TCPReadCallback: tcpClient is null");
                    return;
                }

                NetworkStream tcp_stream;

                try
                {
                    tcp_stream = _tcpClient.GetStream();
                }
                catch (Exception)
                {
                    Disconnect();
                    API.LogDisconnect("CommandCommunication::TCPReadCallback: Exception in _tcpClient.GetStream()");
                    return;
                }

                if(tcp_stream == null)
                {
                    Disconnect();
                    API.LogDisconnect("CommandCommunication::TCPReadCallback: tcp_stream is null");
                    return;
                }

                // Retrieve Read Bytes
                int num_bytes;
                try
                {
                    num_bytes = tcp_stream.EndRead(ar);
                }
                catch (Exception)
                {
                    // if the stream is somehow closed, we should exit gracefully
                    Disconnect();
                    API.LogDisconnect("CommandCommunication::TCPReadCallback: Exception in _tcp_stream.EndRead(ar)");
                    return;
                }

                if (num_bytes == 0) // stream closed? -- need to handle disconnect
                {
                    Disconnect();
                    API.LogDisconnect("CommandCommunication::TCPReadCallback: 0 bytes read from _tcp_stream.EndRead(ar)");
                    return;
                }

                // Convert byte array to a string
                string new_data = Encoding.UTF8.GetString(_tcpReadByteBuffer, 0, num_bytes);

                // add this string to the buffer
                _tcpReadStringBuffer += new_data;

                // now process the string buffer

                bool processing = true;

                while (processing)
                {
                    // look for end of message token
                    int eom = _tcpReadStringBuffer.IndexOf('\n');

                    // handle end of message token not found
                    if (eom < 0) processing = false;
                    else // process this message
                    {
                        // strip message out of larger buffer
                        string s = _tcpReadStringBuffer.Substring(0, eom).Trim('\0');

                        // remove the processed message from the buffer -- ensure modification of string is safe
                        _tcpReadStringBuffer = _tcpReadStringBuffer.Substring(eom + 1);

                        // fire the event that signals new data is ready
                        try // ensure that any exceptions are caught so we don't have silent failures that kill this socket
                        {
                            OnDataReceivedReady(s);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Exception while processing TCP Data: " + s);
                        }
                    }
                }

                // Setup next Read
                try
                {
                    tcp_stream.BeginRead(_tcpReadByteBuffer, 0, TCP_READ_BUFFER_SIZE, new AsyncCallback(TCPReadCallback), null);
                }
                catch (Exception)
                {
                    Disconnect();
                    API.LogDisconnect("CommandCommunication::TCPReadCallback: Exception in _tcp_stream.BeginRead()");
                    return;
                }
            }
        }

        public void Disconnect()
        {
            if (!_isConnected) 
                return;

            if (_tcpClient != null && _tcpClient.Connected)
            {
                // We don't use the Write method here because it could call us if there's an exception, causing
                // a loop
                try
                {
                    _tcpClient.GetStream()?.Write(new byte[] {0x04}, 0, 1);
                    _tcpClient.Close();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Exception disconnecting from radio: {ex}");
                }
            }

            _tcpClient = null;
            _isConnected = false;
            OnIsConnectedChanged(_isConnected);

            lock (_tcpReadSyncObj)
            {
                _tcpReadStringBuffer = "";
            }
        }

        public void Write(string msg)
        {
            if (_tcpClient == null) return;

            NetworkStream tcp_stream;

            try
            {
                tcp_stream = _tcpClient.GetStream();

                if (tcp_stream == null) return;

                byte[] buf = Encoding.UTF8.GetBytes(msg);
                tcp_stream.Write(buf, 0, buf.Length);
            }
            catch(Exception)
            {
                Disconnect();
            }
        }

        /// <summary>
        /// Delegate event handler for the IsConnectedChanged event
        /// </summary>
        public delegate void IsConnectedChangedEventHandler(bool connected);
        /// <summary>
        /// This event is raised when the radio connects or disconnects from the client
        /// </summary>
        public event IsConnectedChangedEventHandler IsConnectedChanged;

        private void OnIsConnectedChanged(bool connected)
        {
            if (IsConnectedChanged != null)
                IsConnectedChanged(connected);
        }

        /// <summary>
        /// Delegate event handler for the DataReceivedReady event
        /// </summary>
        public delegate void TCPDataReceivedReadyEventHandler(string msg);
        /// <summary>
        /// This event is raised when the client receives data from the radio (each message terminated by '\n')
        /// </summary>
        public event TCPDataReceivedReadyEventHandler DataReceivedReady;

        private void OnDataReceivedReady(string msg)
        {
            if (DataReceivedReady != null)
                DataReceivedReady(msg);
        }

        public bool Connect(IPAddress radio_ip, int radioPort, int src_port = 0)
        {
            throw new NotImplementedException();
        }
    }
}
