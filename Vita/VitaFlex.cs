// ****************************************************************************
///*!	\file VitaFlex.cs
// *	\brief Flex specific Vita constants
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2014-02-19
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Flex.Smoothlake.Vita
{
    public static class VitaFlex
    {
        public const ushort SL_VITA_DISCOVERY_CLASS = 0xFFFF;
        public const ushort SL_VITA_METER_CLASS = 0x8002;
        public const ushort SL_VITA_FFT_CLASS = 0x8003;
        public const ushort SL_VITA_WATERFALL_CLASS = 0x8004;
        public const ushort SL_VITA_OPUS_CLASS = 0x8005;
        public const ushort SL_VITA_IF_NARROW_CLASS = 0x03E3;
        public const ushort SL_VITA_IF_NARROW_REDUCED_BW_CLASS = 0x0123;
        public const ushort SL_VITA_IF_WIDE_CLASS_24kHz = 0x02E3;
        public const ushort SL_VITA_IF_WIDE_CLASS_48kHz = 0x02E4;
        public const ushort SL_VITA_IF_WIDE_CLASS_96kHz = 0x02E5;
        public const ushort SL_VITA_IF_WIDE_CLASS_192kHz = 0x02E6;
        public const int MAX_VITA_PACKET_SIZE = 16384;
        public const uint FLEX_OUI = 0x1C2D;
    }
}
