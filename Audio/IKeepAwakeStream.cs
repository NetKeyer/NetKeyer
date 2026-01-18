using System;

namespace NetKeyer.Audio
{
    /// <summary>
    /// Interface for audio streams that keep the audio device awake by playing
    /// near-silent audio (-96dB). This prevents audio devices from going to sleep
    /// due to inactivity.
    /// </summary>
    public interface IKeepAwakeStream : IDisposable
    {
        /// <summary>
        /// Starts playing the near-silent audio stream.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops playing the near-silent audio stream.
        /// </summary>
        void Stop();

        /// <summary>
        /// Gets whether the stream is currently playing.
        /// </summary>
        bool IsPlaying { get; }
    }
}
