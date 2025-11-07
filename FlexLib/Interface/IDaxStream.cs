using System;
using System.ComponentModel;

namespace Flex.Smoothlake.FlexLib.Interface
{
    public interface IDaxStream : INotifyPropertyChanged
    {
        int Gain { get; set; }

        void Close();
    }
}