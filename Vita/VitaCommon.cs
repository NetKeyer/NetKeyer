// ****************************************************************************
///*!	\file VitaCommon.cs
// *	\brief Common Vita Utility Funcations, Enums, Definitions
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
using System.Linq;
using System.Text;

using System.Net;


namespace Flex.Smoothlake.Vita
{
    public delegate void VitaDataReceivedCallback(IPEndPoint ep, byte[] data, int num_bytes);

    /// <summary>
    /// From Vita 49 Standard Table 6.1.1-1
    /// </summary>
    public enum VitaPacketType
    {
        IFData = 0,
        IFDataWithStream = 1,
        ExtData = 2,
        ExtDataWithStream = 3,
        IFContext = 4,
        ExtContext = 5,
    }

    /// <summary>
    /// From Vita 49 Standard Table 6.1.1-2
    /// </summary>
    public enum VitaTimeStampIntegerType
    {
        None = 0,
        UTC = 1,
        GPS = 2,
        Other = 3,
    }

    /// <summary>
    /// From Vita 49 Standard Table 6.1.1-3
    /// </summary>
    public enum VitaTimeStampFractionalType
    {
        None = 0, // No Fractional Seconds Timestamp field included
        SampleCount = 1,
        RealTime = 2, // Picoseconds
        FreeRunning = 3,
    }

    /// <summary>
    /// Represents a Packet Header as defined in the Vita 49 Standard Section 6.1.1
    /// </summary>
    public struct Header
    {
        public VitaPacketType pkt_type;            // Packet Type
        public bool c;                             // Class ID Present
        public bool t;                             // Trailer Present
        public VitaTimeStampIntegerType tsi;       // Time Stamp - Integer
        public VitaTimeStampFractionalType tsf;    // Time Stamp - Fractional
        public byte packet_count;                  // Rolling 4 bit counter
        public ushort packet_size;                 // Number of 32 bit words present including any optional fields
    }

    /// <summary>
    /// From Vita 49 Standard 7.1.3
    /// </summary>
    public struct VitaClassID
    {
        public uint OUI; // Contains the 24-bit IEEE-registered Organizationally Unique Identifier
        public ushort InformationClassCode;
        public ushort PacketClassCode;
    }

    /// <summary>
    /// Represents an IF Data Packet Trailer as defined in the Vita 49 Standard Section 6.1.7
    /// </summary>
    public struct Trailer
    {
        public bool CalibratedTimeEnable;
        public bool ValidDataEnable;
        public bool ReferenceLockEnable;
        public bool AGCMGCEnable;
        public bool DetectedSignalEnable;
        public bool SpectralInversionEnable;
        public bool OverrangeEnable;
        public bool SampleLossEnable;

        public bool CalibratedTimeIndicator;
        public bool ValidDataIndicator;
        public bool ReferenceLockIndicator;
        public bool AGCMGCIndicator;
        public bool DetectedSignalIndicator;
        public bool SpectralInversionIndicator;
        public bool OverrangeIndicator;
        public bool SampleLossIndicator;

        public bool e; // AssociatedContextPacketCountEnable
        public byte AssociatedContextPacketCount; // valid 0 - 127
    }

    public class VitaCommon
    {
        public static float convertVITAtodB(int vita)
        {
	        short db = (short)(vita & 0xFFFF);
	        return ((float)db / 128.0f);
        }
    }
}
