using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Flex.Smoothlake.FlexLib
{
    public class TlsCommandCommunication : ICommandCommunication
    {
        SslClient _tlsToRadio;

        public TlsCommandCommunication()
        {
            _localIP = null;
        }

        private bool _isConnected = false;
        public bool IsConnected
        {
            get
            {
                return (_tlsToRadio != null && _isConnected);
            }
        }

        private IPAddress _localIP = IPAddress.Parse("0.0.0.0");
        public IPAddress LocalIP
        {
            set
            {
                _localIP = value;
            }
            get
            {
                return _localIP;
            }
        }

        public bool Connect(IPAddress radio_ip, bool setup_reply)
        {
            throw new NotImplementedException();
        }

        public bool Connect(IPAddress radio_ip, int radioPort, int src_port = 0)
        {
            _tlsToRadio = new SslClient(radio_ip.ToString(), radioPort.ToString(), src_port, start_ping_thread: false, validate_cert: false);
            if (!_tlsToRadio.IsConnected)
            {
                _tlsToRadio = null;
                return false;
            }

            _tlsToRadio.Disconnected += _tlsToRadio_Disconnected;
            _tlsToRadio.MessageReceivedReady += _tlsToRadio_MessageReceivedReady;

            _tlsToRadio.StartReceiving();

            _isConnected = true;
            OnIsConnectedChanged(_isConnected);
            return true;
        }

        private void _tlsToRadio_Disconnected(object sender, EventArgs e)
        {
            Disconnect();
        }

        private void _tlsToRadio_MessageReceivedReady(string msg)
        {
            OnDataReceivedReady(msg);
        }

        public void Disconnect()
        {
            if (!_isConnected) 
                return;
            
            _tlsToRadio?.Write("\x04");
            _tlsToRadio?.Disconnect();
            
            _isConnected = false;
            OnIsConnectedChanged(_isConnected);
        }

        public void Write(string msg)
        {
            _tlsToRadio.Write(msg);
        }

        public delegate void TCPDataReceivedReadyEventHandler(string msg);
        public event TcpCommandCommunication.TCPDataReceivedReadyEventHandler DataReceivedReady;

        private void OnDataReceivedReady(string msg)
        {
            if (DataReceivedReady == null) return;
            DataReceivedReady(msg);
        }

        public delegate void IsConnectedChangedEventHandler(bool connected);
        public event TcpCommandCommunication.IsConnectedChangedEventHandler IsConnectedChanged;

        private void OnIsConnectedChanged(bool connected)
        {
            if (IsConnectedChanged == null) return;
            IsConnectedChanged(connected);
        }
    }
}
