// ****************************************************************************
///*!	\file Discovery.cs
// *	\brief Facilitates reception of Discovery packets
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2012-03-05
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Flex.Smoothlake.Vita;
using Flex.Util;

namespace Flex.Smoothlake.FlexLib
{
    public delegate void RadioDiscoveredEventHandler(Radio radio);

    class Discovery
    {
        private const int DISCOVERY_PORT = 4992;
        private static UdpClient udp;

        private static CancellationTokenSource _loopCts;

        public static void Start()
        {
            bool done = false;
            int error_count = 0;
            while (!done)
            {
                try
                {
                    udp = new UdpClient();
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, DISCOVERY_PORT));
                    done = true;
                }
                catch (SocketException ex)
                {
                    // do this up to 60 times (60 sec)
                    if (error_count++ > 60) // after 60, give up and rethrow the exception
                        throw new SocketException(ex.ErrorCode);
                    else Thread.Sleep(1000);
                }
            }

            _loopCts = new CancellationTokenSource();

            Task.Run(Receive);
        }

        public static void Stop()
        {
            _loopCts.Cancel();
        }

        private static async void Receive()
        {
            //Stopwatch watch = new Stopwatch();
            var token = _loopCts.Token;
            
            while (!token.IsCancellationRequested)
            {
                // TODO: Pass the cancellation token here when we move to .NET 6/8
                var packet = await udp.ReceiveAsync();
                //watch.Restart();

                // since the call above is blocking, we need to check active again here
                if (token.IsCancellationRequested) 
                    break;

                // ensure that the packet is at least long enough to inspect for VITA info
                if (packet.Buffer.Length < 16)
                    continue;
                
                var vita = new VitaPacketPreamble(packet.Buffer);

                // Check for a valid discovery packet
                if (vita.class_id.OUI != VitaFlex.FLEX_OUI ||vita.header.pkt_type != VitaPacketType.ExtDataWithStream ||
                    vita.class_id.PacketClassCode != VitaFlex.SL_VITA_DISCOVERY_CLASS)
                    continue;

                Radio radio = ProcessVitaDiscoveryDataPacket(new VitaDiscoveryPacket(packet.Buffer, packet.Buffer.Length));
                OnRadioDiscoveredEventHandler(radio);

                //watch.Stop();
                //if(radio.Serial == "3424-1213-8601-4043")
                //    Debug.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff")+": Discovery watch stop (" + watch.ElapsedMilliseconds + " ms)");
            }

            udp.Close();
            udp = null;
        }

        private static Radio ProcessVitaDiscoveryDataPacket(VitaDiscoveryPacket packet)
        {
            Radio radio = new Radio();
            string guiClientProgramsCsv = null;
            string guiClientHandlesCsv = null;

            string[] words = packet.payload.Trim().Split(' ');

            foreach (string kv in words)
            {
                string[] tokens = kv.Split('=');
                if (tokens.Length != 2)
                {
                    //Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid key/value pair (" + kv + ")");
                    continue;
                }

                string key = tokens[0].Trim();
                string value = tokens[1].Trim();
                value = value.Replace("\0", "");

                switch (key.ToLower())
                {
                    case "available_clients":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                //Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            radio.AvailableClients = temp;
                        }
                        break;
                    case "available_panadapters":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                //.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            radio.AvailablePanadapters = temp;
                        }
                        break;
                    case "available_slices":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                //Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            radio.AvailableSlices = temp;
                        }
                        break;
                    case "callsign":
                        radio.Callsign = value;
                        break;
                    case "discovery_protocol_version":
                        {
                            ulong temp;
                            bool b = FlexVersion.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Error converting version string (" + value + ")");
                                continue;
                            }

                            radio.DiscoveryProtocolVersion = temp;
                        }
                        break;
                    case "fpc_mac":
                        radio.FrontPanelMacAddress = value.Replace('-', ':').Trim();
                        break;
                    case "gui_client_ips":
                        radio.GuiClientIPs = value;
                        break;
                    case "gui_client_hosts":
                        radio.GuiClientHosts = value;
                        break;
                    case "gui_client_programs":
                        guiClientProgramsCsv = value;
                        break;
                    case "gui_client_stations":
                        radio.GuiClientStations = value.Replace('\u007f', ' ');
                        break;
                    case "gui_client_handles":
                        guiClientHandlesCsv = value;
                        break;
                    case "inuse_host":
                        radio.InUseHost = value;
                        break;
                    case "inuse_ip":
                        radio.InUseIP = value;
                        break;
                    case "ip":
                        {
                            IPAddress temp;
                            bool b = IPAddress.TryParse(value, out temp);
                            if (!b)
                            {
                                //Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            radio.IP = temp;
                        }
                        break;
                    case "licensed_clients":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                //Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            radio.LicensedClients = temp;
                        }
                        break;
                    case "max_licensed_version":
                        radio.MaxLicensedVersion = StringHelper.Sanitize(value);
                        break;
                    case "max_panadapters":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                //Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            radio.MaxPanadapters = temp;
                        }
                        break;
                    case "max_slices":
                        {
                            int temp;
                            bool b = int.TryParse(value, out temp);
                            if (!b)
                            {
                                //Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            radio.MaxSlices = temp;
                        }
                        break;
                    case "model":
                        radio.Model = value;
                        break;
                    case "nickname":
                        radio.Nickname = value;
                        break;
                    case "port":
                        {
                            ushort temp;
                            bool b = ushort.TryParse(value, out temp);
                            if (!b)
                            {
                                //Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid key/value pair (" + kv + ")");
                                continue;
                            }

                            radio.CommandPort = temp;
                        }
                        break;
                    case "radio_license_id":
                        radio.RadioLicenseId = StringHelper.Sanitize(value);
                        break;
                    case "requires_additional_license":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid value (" + kv + ")");
                                continue;
                            }

                            radio.RequiresAdditionalLicense = Convert.ToBoolean(temp);
                        }
                        break;
                    case "serial":
                        radio.Serial = StringHelper.Sanitize(value);
                        break;
                    case "status":
                        radio.Status = value;
                        break;
                    case "version":
                        {
                            ulong temp;
                            bool b = FlexVersion.TryParse(value, out temp);
                            if (!b)
                            {
                                Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Error converting version string (" + value + ")");
                                continue;
                            }

                            radio.Version = temp;
                        }
                        break;                    
                    case "wan_connected":
                        {
                            uint temp;
                            bool b = uint.TryParse(value, out temp);
                            if (!b || temp > 1)
                            {
                                Debug.WriteLine("FlexLib::Discovery::ProcessVitaDiscoveryDataPacket: Invalid value (" + kv + ")");
                                continue;
                            }

                            radio.IsInternetConnected = Convert.ToBoolean(temp);
                        }
                        break;
                    case "external_port_link":
                    {
                        if (uint.TryParse(value, out var link))
                        {
                            radio.ExternalPortLink = link == 1;
                        }

                        break;
                    }
                }
            }

            List<GUIClient> guiClients = ParseGuiClientsFromDiscovery(guiClientProgramsCsv, radio.GuiClientStations, guiClientHandlesCsv);
            lock (radio.GuiClientsLockObj)
            {
                radio.GuiClients = guiClients;
            }

            return radio;
        }

        public static List<GUIClient> ParseGuiClientsFromDiscovery(string guiClientProgramsCsv, string guiClientStationCsv, string guiClientHandlesCsv)
        {
            if (string.IsNullOrEmpty(guiClientProgramsCsv) || string.IsNullOrEmpty(guiClientStationCsv) || string.IsNullOrEmpty(guiClientHandlesCsv))
            {
                return new List<GUIClient>();
            }

            var programs = guiClientProgramsCsv.Split(',');
            var stations = guiClientStationCsv.Split(',');
            var handles = guiClientHandlesCsv.Split(',');

            if (programs.Length != stations.Length ||
                programs.Length != handles.Length ||
                stations.Length != handles.Length)
            {
                // The lengths of these lists must match.
                return new List<GUIClient>();
            }

            List<GUIClient> guiClients = new List<GUIClient>();
            for (int i = 0; i < programs.Length; i++)
            {
                uint handle_uint;
                StringHelper.TryParseInteger(handles[i], out handle_uint);

                string station = stations[i].Replace('\u007f', ' ');
                GUIClient newGuiClient = new GUIClient(handle: handle_uint, client_id: null, program: programs[i], station: station, is_local_ptt: false);
                guiClients.Add(newGuiClient);
            }

            return guiClients;
        }

        public static event RadioDiscoveredEventHandler RadioDiscovered;

        public static void OnRadioDiscoveredEventHandler(Radio radio)
        {
            if (RadioDiscovered == null) return;
            RadioDiscovered(radio);
        }
    }
}