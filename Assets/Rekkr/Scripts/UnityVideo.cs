// my-rekkr — Managed Doom video output for Unity.
// The original software renderer draws into a byte[] exactly like vanilla Doom;
// we upload it to a texture every rendered frame and draw it with Rekkr/Screen.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using ManagedDoom.Video;
using UnityEngine;
using Renderer = ManagedDoom.Video.Renderer;

namespace ManagedDoom.UnityPort
{
    public sealed class UnityVideo : IVideo, IDisposable
    {
        private readonly Config config;
        private readonly GameContent content;
        private Renderer renderer;
        private byte[] frame;
        private Texture2D texture;
        private int wideWidth;

        public UnityVideo(Config config, GameContent content, int wideWidth = 0)
        {
            this.config = config;
            this.content = content;
            Create(wideWidth);
        }

        private void Create(int wide)
        {
            wideWidth = wide;
            renderer?.Dispose();
            renderer = new Renderer(config, content, wide);
            frame = new byte[4 * renderer.Width * renderer.Height];
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = new Texture2D(renderer.Height, renderer.Width, TextureFormat.RGBA32, false, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.name = "DoomFrame";
        }

        /// <summary>Change the frame width (widescreen on/off, rotation). Returns true if it changed.</summary>
        public bool SetWideWidth(int wide)
        {
            var target = System.Math.Max(640, wide & ~1);
            if (target == renderer.Width) return false;
            Create(wide);
            return true;
        }

        /// <summary>dev3: render directly into the texture memory (default). False = v0.2.0 path.</summary>
        public static bool ZeroCopy = true;

        public Texture2D Texture => texture;
        public int RenderThreads => renderer.RenderThreads;
        public int FrameWidth => renderer.Width;
        public int FrameHeight => renderer.Height;
        public int CenterOffset => renderer.CenterOffset;
        public int Scale => renderer.Scale;
        public byte[] FrameData => frame;

        /// <summary>CPU time of the last software render / texture upload in ms (dev3 instrumentation).</summary>
        public float LastRenderMs { get; private set; }
        public float LastUploadMs { get; private set; }
        private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

        /// <summary>See Renderer.LocalViewTurn (smooth look). Set by RekkrApp before Render.</summary>
        public Angle? LocalViewTurn { get => renderer.LocalViewTurn; set => renderer.LocalViewTurn = value; }

        public void Render(Doom doom, Fixed frameFrac)
        {
            watch.Restart();
            long t1;
            if (ZeroCopy)
            {
                // dev3: render straight into the texture's CPU memory (saves a full-frame copy).
                var raw = texture.GetRawTextureData<byte>();
                unsafe
                {
                    var span = new Span<byte>(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(raw), raw.Length);
                    renderer.Render(doom, span, frameFrac);
                }
                t1 = watch.ElapsedTicks;
            }
            else
            {
                renderer.Render(doom, frame, frameFrac);
                t1 = watch.ElapsedTicks;
                texture.SetPixelData(frame, 0);
            }
            texture.Apply(false, false);
            var t2 = watch.ElapsedTicks;
            var toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            LastRenderMs = (float)(t1 * toMs);
            LastUploadMs = (float)((t2 - t1) * toMs);
        }

        public void InitializeWipe() => renderer.InitializeWipe();
        public bool HasFocus() => true;

        public int MaxWindowSize => renderer.MaxWindowSize;
        public int WindowSize { get => renderer.WindowSize; set => renderer.WindowSize = value; }
        public bool DisplayMessage { get => renderer.DisplayMessage; set => renderer.DisplayMessage = value; }
        public int MaxGammaCorrectionLevel => renderer.MaxGammaCorrectionLevel;
        public int GammaCorrectionLevel { get => renderer.GammaCorrectionLevel; set => renderer.GammaCorrectionLevel = value; }
        public int WipeBandCount => renderer.WipeBandCount;
        public int WipeHeight => renderer.WipeHeight;

        public void Dispose()
        {
            if (texture != null) { UnityEngine.Object.Destroy(texture); texture = null; }
        }
    }
}
