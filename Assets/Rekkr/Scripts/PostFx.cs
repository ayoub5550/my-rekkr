// my-rekkr dev3 stages 7+8 — GPU post-processing of the game frame: sharp upscale into a screen-size
// target, bloom pyramid, colour grade, sharpening, vignette, optional CRT, blurred side-fill for
// centred 4:3 screens. Off (Classic) = the v0.2.0 direct draw. UI buttons are drawn afterwards.
// SPDX-License-Identifier: GPL-2.0-or-later
using UnityEngine;

namespace ManagedDoom.UnityPort
{
    public sealed class PostFx
    {
        private readonly Material post;
        private readonly Material screen;
        private RenderTexture scene;
        private readonly RenderTexture[] bloom = new RenderTexture[5];
        private readonly RenderTexture[] blur = new RenderTexture[4];
        private int w, h;

        public static readonly float[] BloomLevels = { 0F, 0.35F, 0.6F, 0.9F };

        public PostFx(Material screenMat)
        {
            screen = screenMat;
            post = new Material(Resources.Load<Shader>("Rekkr/RekkrPost"));
        }

        public static bool Active =>
            RekkrSettings.Bloom > 0 || RekkrSettings.Vignette > 0 || RekkrSettings.ColorGrade > 0 ||
            RekkrSettings.Sharpen || RekkrSettings.Crt || RekkrSettings.SideFill;

        private static RenderTexture Make(int width, int height)
        {
            var rt = new RenderTexture(Mathf.Max(1, width), Mathf.Max(1, height), 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.Create();
            return rt;
        }

        private void Ensure(int width, int height)
        {
            if (scene != null && width == w && height == h) return;
            Release();
            w = width; h = height;
            scene = Make(w, h);
            for (var i = 0; i < bloom.Length; i++) bloom[i] = Make(w >> (i + 1), h >> (i + 1));
            for (var i = 0; i < blur.Length; i++) blur[i] = Make(w >> (i + 1), h >> (i + 1));
        }

        public void Release()
        {
            if (scene != null) { scene.Release(); Object.Destroy(scene); scene = null; }
            for (var i = 0; i < bloom.Length; i++) if (bloom[i] != null) { bloom[i].Release(); Object.Destroy(bloom[i]); bloom[i] = null; }
            for (var i = 0; i < blur.Length; i++) if (blur[i] != null) { blur[i].Release(); Object.Destroy(blur[i]); blur[i] = null; }
        }

        /// <summary>Runs the chain for this frame. Call once per frame before OnGUI draws.</summary>
        public void Process(UnityVideo video, Rect gameRect)
        {
            Ensure(Mathf.RoundToInt(gameRect.width), Mathf.RoundToInt(gameRect.height));
            Graphics.Blit(video.Texture, scene, screen);

            var bloomOn = RekkrSettings.Bloom > 0;
            if (bloomOn)
            {
                post.SetFloat("_Threshold", 0.72F);
                post.SetFloat("_Knee", 0.2F);
                Graphics.Blit(scene, bloom[0], post, 0);
                for (var i = 1; i < bloom.Length; i++) Graphics.Blit(bloom[i - 1], bloom[i], post, 1);
                for (var i = bloom.Length - 1; i > 0; i--) Graphics.Blit(bloom[i], bloom[i - 1], post, 2);
            }
            var side = RekkrSettings.SideFill && video.Centred;
            if (side)
            {
                Graphics.Blit(scene, blur[0], post, 3);
                for (var i = 1; i < blur.Length; i++) Graphics.Blit(blur[i - 1], blur[i], post, 3);
            }

            post.SetTexture("_BloomTex", bloomOn ? (UnityEngine.Texture)bloom[0] : Texture2D.blackTexture);
            post.SetTexture("_BlurTex", side ? (UnityEngine.Texture)blur[blur.Length - 1] : Texture2D.blackTexture);
            post.SetFloat("_Bloom", BloomLevels[Mathf.Clamp(RekkrSettings.Bloom, 0, 3)]);
            post.SetFloat("_Vignette", RekkrSettings.Vignette / 100F);
            var grade = RekkrSettings.ColorGrade;   // 0 neutral, 1 vivid, 2 warm
            post.SetFloat("_Contrast", grade == 0 ? 1F : 1.08F);
            post.SetFloat("_Saturation", grade == 0 ? 1F : grade == 1 ? 1.15F : 1.05F);
            post.SetFloat("_Warmth", grade == 2 ? 0.05F : 0F);
            // Sharpen only helps when the frame is upscaled (fewer frame lines than screen lines).
            post.SetFloat("_Sharpen", RekkrSettings.Sharpen && video.FrameHeight < gameRect.height * 0.95F ? 1F : 0F);
            post.SetFloat("_Crt", RekkrSettings.Crt ? 1F : 0F);
            post.SetFloat("_Scan", Mathf.Max(2F, gameRect.height / video.FrameHeight * 1.2F));
            post.SetVector("_Content", side ? new Vector4(video.CentredX0, video.CentredWidth, 0, 0) : Vector4.zero);
            post.SetVector("_ScreenPx", new Vector4(gameRect.width, gameRect.height, 0, 0));
        }

        /// <summary>Draws the processed frame (inside OnGUI, Repaint).</summary>
        public void Draw(Rect gameRect) => Graphics.DrawTexture(gameRect, scene, post, 4);
    }
}
