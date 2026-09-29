// my-rekkr — Unity replacements for the DrippyAL types used by Managed Doom's sound code,
// so UnitySound.cs keeps the original channel/priority logic line for line.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;

namespace ManagedDoom.UnityPort
{
    public enum PlaybackState { Stopped, Playing, Paused }

    public struct Vector3
    {
        public float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    /// <summary>An 8-bit unsigned DMX sound converted to a Unity clip.</summary>
    public sealed class AudioClip : IDisposable
    {
        public readonly UnityEngine.AudioClip Clip;
        public readonly TimeSpan Duration;

        public AudioClip(string name, int sampleRate, Span<byte> samples)
        {
            var data = new float[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                data[i] = (samples[i] - 128) / 128F;
            }
            Clip = UnityEngine.AudioClip.Create(name, Math.Max(1, data.Length), 1, sampleRate, false);
            Clip.SetData(data, 0);
            Duration = TimeSpan.FromSeconds((double)data.Length / sampleRate);
        }

        public void Dispose()
        {
            if (Clip != null) UnityEngine.Object.Destroy(Clip);
        }
    }

    /// <summary>One Doom mixing channel = one 2D AudioSource with stereo panning.</summary>
    public sealed class AudioChannel : IDisposable
    {
        private readonly UnityEngine.AudioSource source;
        private bool paused;
        private AudioClip clip;

        public AudioChannel(UnityEngine.GameObject host)
        {
            source = host.AddComponent<UnityEngine.AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0F;
            source.loop = false;
        }

        public AudioClip AudioClip
        {
            get => clip;
            set { clip = value; source.clip = value?.Clip; }
        }

        public float Volume { set => source.volume = Math.Max(0F, Math.Min(1F, value)); }
        public float Pitch { set => source.pitch = value; }

        // Managed Doom places sources on a unit circle around the listener (-x = left).
        public Vector3 Position { set => source.panStereo = Math.Max(-1F, Math.Min(1F, value.X * 0.8F)); }

        public PlaybackState State
        {
            get
            {
                if (paused) return PlaybackState.Paused;
                return source.isPlaying ? PlaybackState.Playing : PlaybackState.Stopped;
            }
        }

        public TimeSpan PlayingOffset => TimeSpan.FromSeconds(source.time);

        public void Play()
        {
            if (paused) { source.UnPause(); paused = false; return; }
            source.Play();
        }

        public void Pause() { source.Pause(); paused = true; }

        public void Stop() { source.Stop(); paused = false; }

        public void Dispose()
        {
            if (source != null) UnityEngine.Object.Destroy(source);
        }
    }
}
