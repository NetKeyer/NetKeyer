using System.Net;

namespace Flex.Smoothlake.FlexLib
{
    public interface ICommandCommunication
    {
        bool IsConnected { get; }
        IPAddress LocalIP { set;  get; }

        event TcpCommandCommunication.TCPDataReceivedReadyEventHandler DataReceivedReady;
        event TcpCommandCommunication.IsConnectedChangedEventHandler IsConnectedChanged;

        bool Connect(IPAddress radio_ip, bool setup_reply);
        bool Connect(IPAddress radio_ip, int radioPort, int src_port = 0);
        void Disconnect();
        void Write(string msg);
    }
}