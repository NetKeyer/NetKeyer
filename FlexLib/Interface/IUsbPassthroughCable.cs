// ****************************************************************************
///*!	\file IUsbPassthroughCable.cs
// *	\brief Interface for a single Passthrough USB Cable
// *
// *	\copyright	Copyright 2012-2024 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2024-10-14
// *	\author Maurice Smulders KF0GEO
// */
// ****************************************************************************

using System;
// using System.Windows.Media.Imaging;

namespace Flex.Smoothlake.FlexLib
{
    public interface IUsbPassthroughCable : IUsbCable
    {
        SerialDataBits DataBits { get; set; }
        SerialSpeed Speed { get; set; }
        SerialParity Parity { get; set; }
        SerialFlowControl FlowControl { get; set; }
        SerialStopBits StopBits { get; set; }

    }
}
