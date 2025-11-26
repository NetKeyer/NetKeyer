using System;

namespace NetKeyer.Audio
{
    public interface ISidetoneGenerator : IDisposable
    {
        void SetFrequency(int frequencyHz);
        void SetVolume(int volumePercent);
        void SetWpm(int wpm);
        void Start();
        void Stop();
        void StartTone(int durationMs);
    }
}
