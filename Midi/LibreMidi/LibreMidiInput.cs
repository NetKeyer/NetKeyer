using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using NetKeyer.Helpers;

namespace NetKeyer.Midi.LibreMidi
{
    /// <summary>
    /// Managed wrapper around the netkeyer_midi_shim native library.
    /// Enumerates MIDI input ports and opens one for receiving messages.
    /// </summary>
    internal class LibreMidiInput : IDisposable
    {
        private IntPtr _observer = IntPtr.Zero;
        private IntPtr _inputHandle = IntPtr.Zero;
        private GCHandle _callbackHandle;
        private NativeMethods.MessageCallback _callback;

        /// <summary>
        /// Fired for each complete MIDI message received from the open port.
        /// SysEx, timing, and active sensing are pre-filtered by the shim.
        /// </summary>
        public event Action<byte[]> MessageReceived;

        /// <summary>
        /// Returns the names of all currently available MIDI input ports.
        /// Creates and immediately frees a temporary observer; safe to call at any time.
        /// </summary>
        public static List<string> GetAvailableDevices()
        {
            var devices = new List<string>();
            DebugLogger.Log("midi", "[MIDI] nkm_create_observer: calling");
            IntPtr obs = NativeMethods.nkm_create_observer();
            if (obs == IntPtr.Zero)
            {
                DebugLogger.Log("midi", "[MIDI] nkm_create_observer: returned NULL — observer creation failed (check stderr for libremidi errors)");
                return devices;
            }
            try
            {
                int count = NativeMethods.nkm_input_count(obs);
                DebugLogger.Log("midi", $"[MIDI] nkm_input_count: {count} port(s) found");
                var buf = new byte[512];
                for (int i = 0; i < count; i++)
                {
                    Array.Clear(buf, 0, buf.Length);
                    if (NativeMethods.nkm_input_name(obs, i, buf, buf.Length) == 0)
                    {
                        int nul = Array.IndexOf(buf, (byte)0);
                        int len = nul >= 0 ? nul : buf.Length;
                        var name = Encoding.UTF8.GetString(buf, 0, len);
                        DebugLogger.Log("midi", $"[MIDI] port {i}: \"{name}\"");
                        devices.Add(name);
                    }
                    else
                    {
                        DebugLogger.Log("midi", $"[MIDI] nkm_input_name({i}): failed");
                    }
                }
            }
            finally
            {
                NativeMethods.nkm_free_observer(obs);
            }
            return devices;
        }

        /// <summary>
        /// Opens the named MIDI input port and begins receiving messages.
        /// Throws <see cref="InvalidOperationException"/> if the port is not found
        /// or cannot be opened.
        /// </summary>
        public void Open(string deviceName)
        {
            Close();

            _observer = NativeMethods.nkm_create_observer();
            if (_observer == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create MIDI observer");

            int targetIndex = FindPortIndex(_observer, deviceName);
            if (targetIndex < 0)
            {
                NativeMethods.nkm_free_observer(_observer);
                _observer = IntPtr.Zero;
                throw new InvalidOperationException($"MIDI device '{deviceName}' not found");
            }

            // Pin the delegate so the GC cannot move or collect it while native code holds a pointer.
            _callback = OnNativeMessage;
            _callbackHandle = GCHandle.Alloc(_callback);

            _inputHandle = NativeMethods.nkm_open_input(_observer, targetIndex, _callback, IntPtr.Zero);
            if (_inputHandle == IntPtr.Zero)
            {
                _callbackHandle.Free();
                NativeMethods.nkm_free_observer(_observer);
                _observer = IntPtr.Zero;
                throw new InvalidOperationException($"Failed to open MIDI device '{deviceName}'");
            }
        }

        /// <summary>
        /// Closes the open MIDI input port and releases all native resources.
        /// Safe to call when not open.
        /// </summary>
        public void Close()
        {
            if (_inputHandle != IntPtr.Zero)
            {
                NativeMethods.nkm_close_input(_inputHandle);
                _inputHandle = IntPtr.Zero;
            }

            if (_callbackHandle.IsAllocated)
                _callbackHandle.Free();

            _callback = null;

            if (_observer != IntPtr.Zero)
            {
                NativeMethods.nkm_free_observer(_observer);
                _observer = IntPtr.Zero;
            }
        }

        public void Dispose() => Close();

        // ---- private helpers ----

        private static int FindPortIndex(IntPtr obs, string deviceName)
        {
            int count = NativeMethods.nkm_input_count(obs);
            var buf = new byte[512];
            for (int i = 0; i < count; i++)
            {
                Array.Clear(buf, 0, buf.Length);
                if (NativeMethods.nkm_input_name(obs, i, buf, buf.Length) == 0)
                {
                    int nul = Array.IndexOf(buf, (byte)0);
                    int len = nul >= 0 ? nul : buf.Length;
                    if (Encoding.UTF8.GetString(buf, 0, len) == deviceName)
                        return i;
                }
            }
            return -1;
        }

        private void OnNativeMessage(IntPtr ctx, IntPtr data, int len)
        {
            if (len <= 0 || data == IntPtr.Zero) return;
            var bytes = new byte[len];
            Marshal.Copy(data, bytes, 0, len);
            MessageReceived?.Invoke(bytes);
        }
    }
}
