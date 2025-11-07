using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Flex.Smoothlake.FlexLib
{
    public interface IUsbLdpaCable : IUsbCable
    {
        LdpaBand Band { get; set; }
        bool IsPreampOn { get; set; }
    }
}
