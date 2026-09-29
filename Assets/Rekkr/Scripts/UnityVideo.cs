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

        public UnityVideo(Config config, GameContent content, int wideWidth = 0, int lines = 400)
        {
            this.config = config;
            this.content = content;
            Create(wideWidth, lines);
        }

        // dev3 stage 5: one renderer + texture per frame size, kept alive so dynamic resolution can
        // switch between levels without rebuilding tables or restarting render threads.
        private readonly System.Collections.Generic.Dictionary<long, (Renderer r, Texture2D t, byte[] f)> cache =
            new System.Collections.Generic.Dictionary<long, (Renderer, Texture2D, byte[])>();
        private int lines = 400;

        public int Lines => lines;

        private void Create(int wide, int lines)
        {
            var old = renderer;
            this.lines = lines;
            wideWidth = wide;
            var key = ((long)lines << 32) | (uint)(System.Math.Max(lines * 8 / 5, wide & ~1));
            if (!cache.TryGetValue(key, out var entry))
            {
                var r = new Renderer(config, content, wide, lines);
                var t = new Texture2D(r.Height, r.Width, TextureFormat.RGBA32, false, false);
                t.filterMode = FilterMode.Bilinear;
                t.wrapMode = TextureWrapMode.Clamp;
                t.name = "DoomFrame" + r.Width + "x" + r.Height;
                entry = (r, t, new byte[4 * r.Width * r.Height]);
                cache[key] = entry;
            }
            renderer = entry.r; texture = entry.t; frame = entry.f;
            if (old != null && old != renderer)
            {
                // Carry the per-renderer settings over (screen size, messages, gamma).
                renderer.WindowSize = old.WindowSize;
                renderer.DisplayMessage = old.DisplayMessage;
                renderer.GammaCorrectionLevel = old.GammaCorrectionLevel;
            }
        }

        /// <summary>dev3: drop all cached renderers (e.g. render-thread count changed) and recreate.</summary>
        public void Rebuild()
        {
            var old = renderer;
            var ws = old.WindowSize; var dm = old.DisplayMessage; var gamma = old.GammaCorrectionLevel;
            foreach (var e in cache.Values) { e.r.Dispose(); UnityEngine.Object.Destroy(e.t); }
            cache.Clear();
            renderer = null;
            Create(wideWidth, lines);
            renderer.WindowSize = ws; renderer.DisplayMessage = dm; renderer.GammaCorrectionLevel = gamma;
        }

        /// <summary>Change the frame width (widescreen on/off, rotation). Returns true if it changed.</summary>
        public bool SetWideWidth(int wide) => SetFrame(wide, lines);

        /// <summary>dev3: change frame width and height (resolution level). Returns true if it changed.</summary>
        public bool SetFrame(int wide, int lines)
        {
            var target = System.Math.Max(lines * 8 / 5, wide & ~1);
            if (target == renderer.Width && lines == renderer.Height) return false;
            Create(wide, lines);
            return true;
        }

        /// <summary>dev3: render directly into the texture memory (default). False = v0.2.0 path.</summary>
        public static bool ZeroCopy = true;

        public Texture2D Texture => texture;
        public int RenderThreads => renderer.RenderThreads;
        public bool Centred => renderer.LastFrameCentred;
        public float CentredX0 => renderer.CentredX0;
        public float CentredWidth => renderer.CentredWidth;
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

        /// <summary>See Renderer.LocalViewPitch (dev4 free look). Set by RekkrApp before Render.</summary>
        public int LocalViewPitch { get => renderer.LocalViewPitch; set => renderer.LocalViewPitch = value; }

        public (int x, int y, int w, int h) ViewWindow => renderer.ViewWindow;

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
            foreach (var e in cache.Values) { e.r.Dispose(); UnityEngine.Object.Destroy(e.t); }
            cache.Clear();
            texture = null;
        }
    }
}
