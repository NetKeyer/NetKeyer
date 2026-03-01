using System;
using System.Runtime.InteropServices;

namespace NetKeyer.Midi.LibreMidi
{
    internal static class NativeMethods
    {
        const string Lib = "netkeyer_midi_shim";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void MessageCallback(IntPtr ctx, IntPtr data, int len);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr nkm_create_observer();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void nkm_free_observer(IntPtr obs);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int nkm_input_count(IntPtr obs);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int nkm_input_name(IntPtr obs, int index,
            byte[] buf, int bufLen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr nkm_open_input(IntPtr obs, int index,
            MessageCallback callback, IntPtr ctx);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void nkm_close_input(IntPtr handle);
    }
}
