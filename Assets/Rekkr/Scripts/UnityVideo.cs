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
        private readonly Renderer renderer;
        private readonly byte[] frame;
        private Texture2D texture;

        public UnityVideo(Config config, GameContent content)
        {
            renderer = new Renderer(config, content);
            frame = new byte[4 * renderer.Width * renderer.Height];
            texture = new Texture2D(renderer.Height, renderer.Width, TextureFormat.RGBA32, false, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.name = "DoomFrame";
        }

        public Texture2D Texture => texture;
        public int FrameWidth => renderer.Width;
        public int FrameHeight => renderer.Height;
        public byte[] FrameData => frame;

        public void Render(Doom doom, Fixed frameFrac)
        {
            renderer.Render(doom, frame, frameFrac);
            texture.SetPixelData(frame, 0);
            texture.Apply(false, false);
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
