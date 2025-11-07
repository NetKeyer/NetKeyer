// ****************************************************************************
///*!	\file IUsbBitCable.cs
// *	\brief Interface for one 8-bit I/O USB Cable
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
    public interface IUsbBitCable : IUsbCable
    {
        string[] BitBand { get; }
        double[] BitHighFreq { get; }
        double[] BitLowFreq { get; }
        string[] BitOrdinalRxAnt { get; }
        string[] BitOrdinalTxAnt { get; }
        string[] BitOrdinalSlice { get; }
        bool[] BitActiveHigh { get; }
        bool[] BitEnable { get; }
        bool[] BitPtt { get; }
        int[] BitPttDelayMs { get; }
        int[] BitTxDelayMs { get; }
        UsbBitCableOutputType[] BitOutput { get; }
        UsbCableFreqSource[] BitSource { get; }
        void SetBitActiveHigh(int bit, bool polarity);
        void SetBitPttDelayMs(int bit, int delay);
        void SetBitTxDelayMs(int bit, int delay);
        void SetBitEnable(int bit, bool enabled);
        void SetBitPtt(int bit, bool enabled);
        void SetBitBand(int bit, string band);
        void SetBitFreqRange(int bit, double freqLowMHz, double freqHighMHz);
        void SetBitSourceRxAnt(int bit, string ant);
        void SetBitSourceTxAnt(int bit, string ant);
        void SetBitSourceSlice(int _number, string sliceLetter);
        void SetBitOutput(int bit, UsbBitCableOutputType output);
        void SetBitSource(int bit, UsbCableFreqSource source);        
    }
}
