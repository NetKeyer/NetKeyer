// ****************************************************************************
///*!	\file ByteOrder.cs
// *	\brief Byte Swapping functions
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

namespace Flex.Util
{
    public class ByteOrder
    {
        public static ushort SwapBytes(ushort x)
        {
            return (ushort)(
                (x & 0xff) << 8
                | (x & 0xff00) >> 8);
        }

        public static uint SwapBytes(uint x)
        {
            return (uint)(
                (x & 0xff) << 24
                | (x & 0xff00) << 8
                | (x & 0xff0000) >> 8
                | (x & 0xff000000) >> 24);
        }

        public static ulong SwapBytes(ulong x)
        {
            return (ulong)(
                (x & 0xff) << 56
                | (x & 0xff00) << 40
                | (x & 0xff0000) << 24
                | (x & 0xff000000) << 8
                | (x & 0xff00000000) >> 8
                | (x & 0xff0000000000) >> 24
                | (x & 0xff000000000000) >> 40
                | (x & 0xff00000000000000) >> 56);
        }
    }
}
