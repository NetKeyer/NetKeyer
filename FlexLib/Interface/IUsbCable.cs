// ****************************************************************************
///*!	\file IUsbCable.cs
// *	\brief Interface for a USB Cable base class
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
using System.ComponentModel;
namespace Flex.Smoothlake.FlexLib
{
    public interface IUsbCable
    {
        UsbCableType CableType { get; set; }
        string SerialNumber { get; }
        bool Enabled { get; set; }
        bool Present { get; }
        bool LoggingEnabled { get; set; }
        string Name { get; set; }
        void Remove();

        event PropertyChangedEventHandler PropertyChanged;
        event EventHandler<LogMessageEventArgs> LogTextReceived;
    }
}
