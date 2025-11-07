// ****************************************************************************
///*!	\file UsbOtherCable.cs
// *	\brief Represents a single unsupported USB Cable
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2016-10-03
// *	\author Abed Haque 
// */
// ****************************************************************************
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Flex.Smoothlake.FlexLib
{
    public class UsbOtherCable : UsbCable
    {
        public UsbOtherCable(Radio radio, string serial_number, UsbCableType type)
            : base(radio, serial_number)
        {
            _cableType = type;
        }
    }
}
