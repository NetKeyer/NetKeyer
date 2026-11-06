using System;
using System.Collections.Generic;
using System.Threading;
using NetKeyer.Helpers;

namespace NetKeyer.Services;

/// <summary>
/// Sends CW key commands to the radio from a dedicated thread.
///
/// Key-down/key-up are triggered from latency-critical places: the MIDI
/// callback thread (first element, straight key) and the real-time audio
/// callback (every subsequent iambic element). Radio.CWKey does network I/O
/// inside FlexLib, so calling it inline delays the start of the sidetone
/// (it ran inside SidetoneProvider's lock, before the audio device was
/// started) and risks audio dropouts. Commands carry their own timestamps,
/// so handing them to another thread does not change radio-side timing.
///
/// Commands are delivered strictly in FIFO order.
/// Set NETKEYER_RADIO_SEND_SYNC=1 to send synchronously (old behaviour) for A/B testing.
/// </summary>
public sealed class RadioKeySender : IDisposable
{
    private readonly struct Command
    {
        public readonly Action<bool, string, uint> Send;
        public readonly bool State;
        public readonly string Timestamp;
        public readonly uint Handle;

        public Command(Action<bool, string, uint> send, bool state, string timestamp, uint handle)
        {
            Send = send;
            State = state;
            Timestamp = timestamp;
            Handle = handle;
        }
    }

    private readonly Queue<Command> _queue = new Queue<Command>(16);
    private readonly object _lock = new object();
    private readonly bool _sync;
    private Thread _thread;
    private bool _stopping;

    public RadioKeySender()
    {
        _sync = LatencyOptions.RadioSendSync;
        if (_sync)
        {
            DebugLogger.Log("keyer", "[RadioKeySender] Synchronous mode (NETKEYER_RADIO_SEND_SYNC=1)");
            return;
        }

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "NetKeyer radio key sender",
            Priority = ThreadPriority.Highest
        };
        _thread.Start();
    }

    /// <summary>
    /// Queues a key command. Never blocks on network I/O (unless synchronous mode is enabled).
    /// </summary>
    public void Enqueue(Action<bool, string, uint> send, bool state, string timestamp, uint handle)
    {
        if (send == null) return;

        if (_sync)
        {
            Invoke(new Command(send, state, timestamp, handle));
            return;
        }

        lock (_lock)
        {
            if (_stopping) return;
            _queue.Enqueue(new Command(send, state, timestamp, handle));
            Monitor.Pulse(_lock);
        }
    }

    private void Run()
    {
        while (true)
        {
            Command cmd;
            lock (_lock)
            {
                while (_queue.Count == 0 && !_stopping)
                    Monitor.Wait(_lock);

                if (_queue.Count == 0 && _stopping)
                    return;

                cmd = _queue.Dequeue();
            }

            Invoke(cmd);
        }
    }

    private static void Invoke(in Command cmd)
    {
        try
        {
            cmd.Send(cmd.State, cmd.Timestamp, cmd.Handle);
        }
        catch (Exception ex)
        {
            DebugLogger.Log("keyer", $"[RadioKeySender] Error sending CW key: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the sender thread after flushing any queued commands (so a pending
    /// key-up is still delivered).
    /// </summary>
    public void Dispose()
    {
        Thread t;
        lock (_lock)
        {
            if (_stopping) return;
            _stopping = true;
            Monitor.PulseAll(_lock);
            t = _thread;
        }
        t?.Join(1000);
    }
}
