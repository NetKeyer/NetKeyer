// ****************************************************************************
///*!	\file IUsbCatCable.cs
// *	\brief Interface for a single CAT USB Cable
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2016-08-10
// *	\author Eric Wachsmann KE5DTO
// */
// ****************************************************************************

using System;

namespace Flex.Smoothlake.FlexLib
{
    public interface IUsbCatCable : IUsbCable
    {
        UsbCableFreqSource Source { get; set; }
        string SelectedRxAnt { get; set; }
        string SelectedTxAnt { get; set; }
        string SelectedSlice { get; set; }
        bool AutoReport { get; set; }
        SerialDataBits DataBits { get; set; }
        SerialSpeed Speed { get; set; }
        SerialParity Parity { get; set; }
        SerialFlowControl FlowControl { get; set; }
        SerialStopBits StopBits { get; set; }
    }
}
