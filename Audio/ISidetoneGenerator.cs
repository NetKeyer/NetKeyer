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

        /// <summary>
        /// Event fired when a timed silence completes and no next tone was queued.
        /// Used by the iambic keyer to drive the state machine.
        /// </summary>
        event Action OnSilenceComplete;

        /// <summary>
        /// Event fired when any timed tone completes (whether silence follows or not).
        /// Used by the iambic keyer to coordinate radio key-up and silence queueing.
        /// </summary>
        event Action OnToneComplete;

        /// <summary>
        /// Event fired when entering non-timed Silent state (fully idle).
        /// Used by WASAPI to stop playback for minimum latency.
        /// </summary>
        event Action OnBecomeIdle;
    }
}
