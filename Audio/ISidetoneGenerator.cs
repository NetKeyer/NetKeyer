using System;

namespace NetKeyer.Audio
{
    public interface ISidetoneGenerator : IDisposable
    {
        void SetFrequency(int frequencyHz);
        void SetVolume(int volumePercent);
        void Start();
        void Stop();
    }
}
