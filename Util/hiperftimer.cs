//=================================================================
// hiperftimer.cs
//=================================================================
// Taken directly from a Code Projects article written by
// Daniel Strigl.
// http://www.codeproject.com/csharp/highperformancetimercshar.asp
//=================================================================

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Flex.Util
{
    public class HiPerfTimer
    {
        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceCounter(
            out long lpPerformanceCount);

        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceFrequency(
            out long lpFrequency);

        private long startTime, stopTime;
        private long freq;

        // Constructor
        public HiPerfTimer()
        {
            startTime = 0;
            stopTime = 0;

            if (QueryPerformanceFrequency(out freq) == false)
            {
                // high-performance counter not supported
                throw new Exception();
            }
        }

        // Start the timer
        public void Start()
        {
            // let the waiting threads do their work - start on fresh timeslice
            Thread.Sleep(0);

            QueryPerformanceCounter(out startTime);
        }

        // Stop the timer
        public void Stop()
        {
            QueryPerformanceCounter(out stopTime);
        }

        // Returns the duration of the timer (in seconds)
        public double Duration
        {
            get
            {
                return (double)(stopTime - startTime) / (double)freq;
            }
        }

        public double DurationMsec
        {
            get
            {
                return (1000.0) * (double)((stopTime - startTime)) / (double)freq;
            }
        }

        public long GetFreq()
        {
            long freq = 0;
            QueryPerformanceFrequency(out freq);
            return freq;
        }
    }
}