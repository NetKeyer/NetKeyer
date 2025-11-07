// ****************************************************************************
///*!	\file WaterfallTile.cs
// *	\brief Represents a single Waterfall Tile object
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2014-03-11
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Flex.Util
{
    public class WaterfallTile
    {
        /// <summary>
        /// The frequency represented by the first bin in the frame (note: the
        /// frame could be spread across multiple packets feeding this object)
        /// </summary>
        public VitaFrequency FrameLowFreq { get; set; }

        /// <summary>
        /// The index of the first bin in the tile.  If a full frame was split
        /// across multiple packets/tiles, this would tell you which set of
        /// data it represented in the frame.  For example, if a Frame had 1300
        /// bins in it, it could get split across 3 packets/tiles.  These tiles
        /// might have FirstBinIndex values of 0, 500, and 1000 (last packet
        /// containing only 300 bins).  Note that the Timecode and FrameLowFreq
        /// would be the same for all 3 of these packets.
        /// </summary>
        public uint FirstBinIndex;

        /// <summary>
        /// The total number of FFT points (bins) in the frame.  Note that if
        /// the data was split across multiple packets, this number may not
        /// match the length of the Data field
        /// </summary>
        public uint TotalBinsInFrame;

        /// <summary>
        /// The "width" represented by each Bin.  For example, a 1MHz waterfall
        /// display with 4096 bins would have a BinBandwidth of ~244Hz.
        /// </summary>
        public VitaFrequency BinBandwidth { get; set; }

        /// <summary>
        /// The length of time represented by the tile in milliseconds.  Faster
        /// update rates will result in lower line durations.  For example, a
        /// rate that yields 10 frames per second would have a line duration of
        /// 100ms.  A faster rate might yield 20 frames per second and would
        /// have a duration of 50ms.
        /// </summary>
        public uint LineDurationMS { get; set; }
                
        /// <summary>
        /// The number of bins wide described by the tile
        /// </summary>
        public ushort Width { get; set; }

        //private ushort _height;
        /// <summary>
        /// The number of bins tall described by the tile
        /// </summary>
        public ushort Height { get; set; }

        /// <summary>
        /// An index relating the Tile to a relative time base
        /// </summary>
        public uint Timecode { get; set; }

        /// <summary>
        /// The level to use if Auto Black is enabled (for automatically 
        /// setting the 'noise floor' of the display)
        /// </summary>
        public uint AutoBlackLevel { get; set; }

        /// <summary>
        /// If the WaterfallTile is spread across multiple packets, this 
        /// property will be false until a complete frames data has arrived
        /// </summary>
        public bool IsFrameComplete { get; set; }

        /// <summary>
        /// The waterfall bin data
        /// </summary>
        public ushort[] Data;
        
        /// <summary>
        /// The arrival timestamp of the packet (UTC)
        /// </summary>
        public DateTime DateTime { get; set; }

        public override string ToString()
        {
            return Timecode + ": " + ((double)FrameLowFreq).ToString("f6") + "  " + Width + "x" + Height + " " + LineDurationMS + "ms " + DateTime.ToString("hh:mm:ss.fff");
        }
    }
}
