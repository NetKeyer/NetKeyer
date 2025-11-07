// ****************************************************************************
///*!	\file VitaFrequency.cs
// *	\brief A 64-bit fixed point type described in Vita 49 Standard Figure 7.1.5.6-1
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
    public class VitaFrequency
    {
        private long _vita;

        /// <summary>
        /// Creates a new VitaFrequency with the raw data provided
        /// </summary>
        /// <param name="freq">The 64-bit integer to use to create the VitaFrequency</param>
        /// <returns>A new VitaFrequency</returns>
        public static implicit operator VitaFrequency(long freq)
        {
            return new VitaFrequency { _vita = freq };
        }

        /// <summary>
        /// Creates a new VitaFrequency with the data provided
        /// </summary>
        /// <param name="freqMhz">The 64-bit float in MHz to use to create the VitaFrequency</param>
        /// <returns>A new VitaFrequency</returns>
        public static implicit operator VitaFrequency(double freqMhz)
        {
            return new VitaFrequency { _vita = (long)(freqMhz * 1048576E6) };
        }

        /// <summary>
        /// Pulls the raw 64-bit integer data out of the VitaFrequency.  Allows the type to be used like a built-in type
        /// </summary>
        /// <param name="vf"></param>
        /// <returns></returns>
        public static implicit operator long(VitaFrequency vf)
        {
            return vf._vita;
        }

        /// <summary>
        /// Pulls the frequency out as a double in MHz.  Allows the type to be used like a built-in type
        /// </summary>
        /// <param name="vf"></param>
        /// <returns>The frequency in MHz</returns>
        public static implicit operator double(VitaFrequency vf)
        {
            return vf.FreqMhz;
        }

        /// <summary>
        /// The Frequency in Megahertz represented by the VitaFrequency to the limit of a double (64-bit floating point)
        /// </summary>
        public double FreqMhz
        {
            get { return _vita / 1.048576E12; }
        }

        /// <summary>
        /// The frequency in Hertz represented by the VitaFrequency to the limit of a long (64-bit signed integer)
        /// </summary>
        public long FreqHz
        {
            get { return _vita >> 20; }
        }
    }
}
