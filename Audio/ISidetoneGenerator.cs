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
        void StartSilenceThenTone(int silenceMs, int toneMs);
        void QueueSilence(int silenceMs, int? followingToneMs = null);
    }
}
