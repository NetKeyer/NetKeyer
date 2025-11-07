// ****************************************************************************
///*!	\file WanTestConnectionResults.cs
// *	\brief Helper class to encapsulate Wan Test Connection Results
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2017-06-26
// *	\author Ed Gonzalez KG5FBT
// */
// ****************************************************************************



namespace Flex.Smoothlake.FlexLib
{
    public class WanTestConnectionResults
    {
        public bool upnp_tcp_port_working;
        public bool upnp_udp_port_working;
        public bool forward_tcp_port_working;
        public bool forward_udp_port_working;
        public bool nat_supports_hole_punch;
        public string radio_serial;

        public override string ToString()
        {
            return "UPNP TCP Working: " + upnp_tcp_port_working +
                "\nUPNP UDP Working: " + upnp_udp_port_working +
                "\nForwarded TCP Working: " + forward_tcp_port_working +
                "\nForwarded UDP Working: " + forward_udp_port_working +
                "\nNAT Preserves Ports: " + nat_supports_hole_punch;
        }
    }
}