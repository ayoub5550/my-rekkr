// my-rekkr dev6 "Remaster" — the GPU 3D renderer (Unity side). Draws the same game state as the
// software renderer, as real 3D meshes on the GPU: level geometry (LevelGeometry), things
// (ThingBuilder billboards / extruded meshes), the animated sky. Output goes into a render texture of
// the software view window, then a compositor puts it into the software frame wherever the software
// renderer left G-buffer code 250 ("GPU here"): the HUD, status bar, menus, messages and the weapon
// stay the original software-drawn pixels. The GPU writes the dev5 G-buffer code (depth / material)
// into alpha, so WorldFx (water, fog, weather, lights, AO, rays) and PostFx run on top unchanged.
//
// Camera parity: the projection is built from ThreeDRenderer.LastView (centre with the free-look shear,
// projection in frame pixels) so edges land on the same pixels as the software renderer.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using ManagedDoom.Remaster;
using ManagedDoom.Video;
using UnityEngine;
using UnityEngine.Rendering;

namespace ManagedDoom.UnityPort
{
    using UVec = UnityEngine.Vector3;
    using Vector4 = UnityEngine.Vector4;
    using Texture = UnityEngine.Texture;
    using Renderer = ManagedDoom.Video.Renderer;

    public sealed class GpuRenderer : IDisposable
    {
        private readonly GameContent content;
        private TextureAtlas atlas;
        private Texture2D atlasTex, lookupTex, sectorTex, litPalTex;
        private readonly Material worldMat, compMat;
        private Mesh level, things;
        private World curWorld;
        private LevelGeometry geo;
        private ThingBuilder thingBuilder;
        private RenderTexture rt, comp;
        private readonly CommandBuffer cmd = new CommandBuffer { name = "Rekkr Remaster" };
        private int skyTexture;
        private int levelIndicesVersion = -1;
        private uint[] litPalFor;
        private Color32[] sectorPixels;
        private float[] lookupData;

        public int Triangles => geo?.Triangles ?? 0;
        public int ThingCount => thingBuilder?.Count ?? 0;
        public int DrawCalls { get; private set; }
        public float LastCpuMs { get; private set; }
        public static bool FlipCull;   // debug: if the winding convention were inverted

        public GpuRenderer(GameContent content)
        {
            this.content = content;
            worldMat = new Material(Resources.Load<Shader>("Rekkr/Remaster/RemasterWorld"));
            compMat = new Material(Resources.Load<Shader>("Rekkr/Remaster/RemasterComposite"));
            BuildAtlas();
        }

        private void BuildAtlas()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            GBuffer.ClassifyFlats(content);
            atlas = new TextureAtlas(content);
            atlasTex = new Texture2D(atlas.Width, atlas.Height, TextureFormat.RG16, false, true)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "RemasterAtlas",
            };
            atlasTex.LoadRawTextureData(atlas.Data);
            atlasTex.Apply(false, true);   // no CPU copy kept
            var lw = 256; var lh = (atlas.SlotCount + lw - 1) / lw;
            lookupTex = new Texture2D(lw, lh, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "RemasterLookup" };
            lookupData = new float[lw * lh * 4];
            litPalTex = new Texture2D(256, 34, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "RemasterLitPal" };
            worldMat.SetTexture("_Atlas", atlasTex);
            worldMat.SetVector("_AtlasSize", new Vector4(atlas.Width, atlas.Height, 1F / atlas.Width, 1F / atlas.Height));
            worldMat.SetTexture("_Lookup", lookupTex);
            worldMat.SetVector("_LookupSize", new Vector4(lw, lh, 1F / lw, 1F / lh));
            worldMat.SetTexture("_LitPal", litPalTex);
            Debug.Log($"[REKKR] remaster atlas {atlas.Width}x{atlas.Height} slots={atlas.SlotCount} {sw.ElapsedMilliseconds} ms");
        }

        private void LoadLevel(World world)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            curWorld = world;
            skyTexture = 0;
            for (var i = 0; i < content.Textures.Count; i++) if (ReferenceEquals(content.Textures[i], world.Map.SkyTexture)) { skyTexture = i; break; }
            geo = new LevelGeometry(world.Map, content.Textures, content.Flats, atlas, skyTexture);
            thingBuilder ??= new ThingBuilder(atlas, content.Sprites);
            if (level == null) { level = new Mesh { name = "RemasterLevel" }; level.MarkDynamic(); }
            level.Clear();
            level.SetVertexBufferParams(geo.Vertices.Length, Layout);
            level.SetVertexBufferData(geo.Vertices, 0, 0, geo.Vertices.Length, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            SetLevelIndices();
            level.bounds = new Bounds(UVec.zero, UVec.one * 200000F);
            var sc = world.Map.Sectors.Length;
            var sh = (sc + 255) / 256;
            if (sectorTex == null || sectorTex.height != sh)
            {
                if (sectorTex != null) UnityEngine.Object.Destroy(sectorTex);
                sectorTex = new Texture2D(256, sh, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "RemasterSectors" };
                sectorPixels = new Color32[256 * sh];
            }
            worldMat.SetTexture("_Sectors", sectorTex);
            worldMat.SetVector("_SectorsSize", new Vector4(256, sh, 1F / 256, 1F / sh));
            Debug.Log($"[REKKR] remaster level sectors={sc} traced={geo.TracedSectors} fallback={geo.FallbackSectors} empty={geo.EmptySectors} tris={geo.Triangles} verts={geo.Vertices.Length} {sw.ElapsedMilliseconds} ms");
        }

        private static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
        };

        private void SetLevelIndices()
        {
            var total = geo.Indices[0].Length + geo.Indices[1].Length + geo.Indices[2].Length;
            level.SetIndexBufferParams(total, IndexFormat.UInt32);
            var all = new int[total];
            var o = 0;
            level.subMeshCount = 3;
            for (var g = 0; g < 3; g++)
            {
                Array.Copy(geo.Indices[g], 0, all, o, geo.Indices[g].Length);
                o += geo.Indices[g].Length;
            }
            level.SetIndexBufferData(all, 0, 0, total, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            o = 0;
            for (var g = 0; g < 3; g++)
            {
                level.SetSubMesh(g, new SubMeshDescriptor(o, geo.Indices[g].Length), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                o += geo.Indices[g].Length;
            }
            levelIndicesVersion = geo.IndicesVersion;
        }

        /// <summary>The game whose level is on screen (live game, demo playback, title demo), or null.</summary>
        public static DoomGame LevelGame(Doom doom)
        {
            if (doom == null) return null;
            DoomGame g = null;
            if (doom.State == DoomState.Game) g = doom.Game;
            else if (doom.State == DoomState.DemoPlayback) g = doom.DemoPlayback.Game;
            else if (doom.State == DoomState.Opening && doom.Opening.State == OpeningSequenceState.Demo) g = doom.Opening.DemoGame;
            return g != null && g.State == GameState.Level && !g.World.AutoMap.Visible ? g : null;
        }

        /// <summary>Renders the 3D view of <paramref name="game"/> and composites it into the software frame.
        /// Returns the composited frame texture (same layout as video.Texture).</summary>
        public Texture Render(UnityVideo video, DoomGame game, Fixed frac)
        {
            var t0 = Time.realtimeSinceStartup;
            var world = game.World;
            if (world != curWorld) LoadLevel(world);
            if (game.Paused) frac = Fixed.One;
            var v = ThreeDRenderer.LastView;
            if (v.WindowW <= 0 || v.WindowH <= 0) return null;
            EnsureTargets(video, v);

            // ---- dynamic state
            geo.Update(world, frac);
            foreach (var (start, count) in geo.DirtyRanges)
                level.SetVertexBufferData(geo.Vertices, start, start, count, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            if (geo.IndicesVersion != levelIndicesVersion) SetLevelIndices();
            UpdateLookup(world);
            UpdateSectors(world);
            var player = world.DisplayPlayer;
            UpdateLitPal(player);

            var viewer = player.Mobj;
            thingBuilder.Build(world, frac, v.X, v.Y, v.Angle, viewer);
            things ??= NewDynamicMesh("RemasterThings");
            UploadThings();

            // ---- camera: view matrix in Unity world space (doom x, z, y), projection in window pixels
            float ca = Mathf.Cos(v.Angle), sa = Mathf.Sin(v.Angle);
            var fwd = new UVec(ca, 0, sa); var right = new UVec(sa, 0, -ca); var up = UVec.up;
            var eye = new UVec(v.X, v.Z, v.Y);
            var view = Matrix4x4.identity;
            view.SetRow(0, new Vector4(right.x, right.y, right.z, -UVec.Dot(right, eye)));
            view.SetRow(1, new Vector4(up.x, up.y, up.z, -UVec.Dot(up, eye)));
            view.SetRow(2, new Vector4(-fwd.x, -fwd.y, -fwd.z, UVec.Dot(fwd, eye)));
            view.SetRow(3, new Vector4(0, 0, 0, 1));
            float W = v.WindowW, H = v.WindowH, n = 2F, f = 40000F;
            var proj = Matrix4x4.zero;
            proj[0, 0] = 2F * v.Projection / W; proj[0, 2] = 1F - 2F * v.CenterX / W;
            proj[1, 1] = 2F * v.Projection / H; proj[1, 2] = 2F * v.CenterY / H - 1F;
            proj[2, 2] = -(f + n) / (f - n); proj[2, 3] = -2F * f * n / (f - n);
            proj[3, 2] = -1F;

            worldMat.SetVector("_View", new Vector4(v.X, v.Y, v.Z, v.Angle));
            worldMat.SetVector("_Proj", new Vector4(v.CenterX, v.CenterY, v.Projection, H));
            worldMat.SetVector("_RTSize", new Vector4(W, H, 1F / W, 1F / H));
            var nonWide = Mathf.Min(W, 2F * v.Projection);
            var skyStep = 320F / Mathf.Max(1F, nonWide) * (ThreeDRenderer.FreeLookSky ? 100F / (100F + TicCmdExt.MaxPitch) : 1F);
            worldMat.SetVector("_Sky", new Vector4(skyTexture, skyStep, ThreeDRenderer.SkyDriftBam / 4294967296F * Mathf.PI * 2F, ThreeDRenderer.FreeLookSky || v.CenterY != H / 2 ? 1 : 0));
            worldMat.SetVector("_Light", new Vector4(player.ExtraLight, player.FixedColorMap, 1, Time.time));
            worldMat.SetFloat("_MaskCull", (float)(FlipCull ? CullMode.Front : CullMode.Back));

            cmd.Clear();
            calls0 = 0;
            RenderShadows(game, v);
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 1));
            cmd.SetViewProjectionMatrices(view, proj);
            var calls = calls0;
            cmd.DrawMesh(level, Matrix4x4.identity, worldMat, 0, 0); calls++;   // opaque walls + flats
            cmd.DrawMesh(level, Matrix4x4.identity, worldMat, 2, 2); calls++;   // sky
            cmd.DrawMesh(level, Matrix4x4.identity, worldMat, 1, 1); calls++;   // masked mid textures
            if (thingBuilder.FuzzIndexStart > 0) { cmd.DrawMesh(things, Matrix4x4.identity, worldMat, 0, 1); calls++; }
            if (thingBuilder.IndexCount > thingBuilder.FuzzIndexStart) { cmd.DrawMesh(things, Matrix4x4.identity, worldMat, 1, 4); calls++; }
            Graphics.ExecuteCommandBuffer(cmd);
            DrawCalls = calls;

            // ---- composite into the software frame (transposed layout)
            compMat.SetTexture("_GpuTex", rt);
            compMat.SetVector("_Win", new Vector4(v.WindowX, v.WindowY, v.WindowW, v.WindowH));
            compMat.SetVector("_Frame", new Vector4(video.FrameWidth, video.FrameHeight, 1F / video.FrameWidth, 1F / video.FrameHeight));
            Graphics.Blit(video.Texture, comp, compMat, 0);
            LastCpuMs = (Time.realtimeSinceStartup - t0) * 1000F;
            return comp;
        }

        // ---------------------------------------------------------------- stage 5: sun shadow map
        private RenderTexture shadowRt;
        private int calls0;
        private MaterialPropertyBlock opaqueCaster, maskedCaster;
        public const int ShadowSize = 2048;
        public const float ShadowRadius = 2048F;   // map units covered around the player (2 units per texel)
        public bool ShadowsOn { get; private set; }

        private void RenderShadows(DoomGame game, ThreeDRenderer.ViewInfo v)
        {
            var (yaw, elev, strength, col) = WorldFx.SunOf(game.Options.Episode);
            ShadowsOn = RekkrSettings.RemasterShadows && strength > 0;
            if (!ShadowsOn) { worldMat.SetVector("_SunDir", Vector4.zero); return; }
            if (shadowRt == null)
            {
                shadowRt = new RenderTexture(ShadowSize, ShadowSize, 16, RenderTextureFormat.RFloat) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "RemasterShadow" };
                shadowRt.Create();
                opaqueCaster = new MaterialPropertyBlock(); opaqueCaster.SetVector("_SunCasterMask", Vector4.zero);
                maskedCaster = new MaterialPropertyBlock(); maskedCaster.SetVector("_SunCasterMask", new Vector4(1, 0, 0, 0));
            }
            // the shadow sun is never lower than 40 degrees (dusk suns of E2/E4 would give endless shadows)
            float y = yaw * Mathf.Deg2Rad, e = Mathf.Max(elev, 40F) * Mathf.Deg2Rad;
            var sun = new UVec(Mathf.Cos(e) * Mathf.Cos(y), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(y)).normalized;
            var fwd = -sun;
            var right = UVec.Cross(UVec.up, fwd).normalized;
            var up = UVec.Cross(fwd, right);
            // centre snapped to the texel grid (no shimmering while walking)
            var texel = 2F * ShadowRadius / ShadowSize;
            var c = new UVec(v.X, v.Z, v.Y);
            var cr = Mathf.Round(UVec.Dot(c, right) / texel) * texel; var cu = Mathf.Round(UVec.Dot(c, up) / texel) * texel;
            var cf = UVec.Dot(c, fwd);
            c = right * cr + up * cu + fwd * cf;
            var eye = c - fwd * 8000F;
            var view = Matrix4x4.identity;
            view.SetRow(0, new Vector4(right.x, right.y, right.z, -UVec.Dot(right, eye)));
            view.SetRow(1, new Vector4(up.x, up.y, up.z, -UVec.Dot(up, eye)));
            view.SetRow(2, new Vector4(-fwd.x, -fwd.y, -fwd.z, UVec.Dot(fwd, eye)));
            var proj = Matrix4x4.Ortho(-ShadowRadius, ShadowRadius, -ShadowRadius, ShadowRadius, 1F, 20000F);
            var svp = Matrix4x4.identity;
            svp.SetRow(0, new Vector4(right.x, right.y, right.z, -UVec.Dot(right, c)) / ShadowRadius);
            svp.SetRow(1, new Vector4(up.x, up.y, up.z, -UVec.Dot(up, c)) / ShadowRadius);
            svp.SetRow(2, new Vector4(fwd.x, fwd.y, fwd.z, -UVec.Dot(fwd, eye)));
            svp.SetRow(3, new Vector4(0, 0, 0, 1));
            worldMat.SetMatrix("_ShadowVP", svp);
            worldMat.SetVector("_SunDir", new Vector4(sun.x, sun.y, sun.z, 1));
            worldMat.SetVector("_SunTint", new Vector4(col.r, col.g, col.b, 0.38F));
            worldMat.SetVector("_ShadowParams", new Vector4(1F / ShadowSize, texel * 1.2F, 0, 0));
            worldMat.SetTexture("_ShadowMap", shadowRt);
            cmd.SetRenderTarget(shadowRt);
            cmd.ClearRenderTarget(true, true, new Color(1e7F, 0, 0, 0));
            cmd.SetViewProjectionMatrices(view, proj);
            cmd.DrawMesh(level, Matrix4x4.identity, worldMat, 0, 3, opaqueCaster);
            cmd.DrawMesh(level, Matrix4x4.identity, worldMat, 1, 3, maskedCaster);
            if (thingBuilder.FuzzIndexStart > 0) cmd.DrawMesh(things, Matrix4x4.identity, worldMat, 0, 3, maskedCaster);
            calls0 = 3;
        }

        private void EnsureTargets(UnityVideo video, ThreeDRenderer.ViewInfo v)
        {
            if (rt == null || rt.width != v.WindowW || rt.height != v.WindowH)
            {
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                rt = new RenderTexture(v.WindowW, v.WindowH, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "RemasterView" };
                rt.Create();
            }
            // composite: same (transposed) size as the frame texture
            int cw = video.FrameHeight, ch = video.FrameWidth;
            if (comp == null || comp.width != cw || comp.height != ch)
            {
                if (comp != null) { comp.Release(); UnityEngine.Object.Destroy(comp); }
                comp = new RenderTexture(cw, ch, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "RemasterFrame" };
                comp.Create();
            }
        }

        private static Mesh NewDynamicMesh(string name)
        {
            var m = new Mesh { name = name };
            m.MarkDynamic();
            m.bounds = new Bounds(UVec.zero, UVec.one * 200000F);
            return m;
        }

        private int thingVertCap, thingIdxCap;

        private void UploadThings()
        {
            var tb = thingBuilder;
            var vc = Math.Max(4, tb.VertexCount); var ic = Math.Max(6, tb.IndexCount);
            if (vc > thingVertCap || thingVertCap == 0)
            {
                thingVertCap = Math.Max(1024, Mathf.NextPowerOfTwo(vc));
                things.SetVertexBufferParams(thingVertCap, Layout);
            }
            if (ic > thingIdxCap || thingIdxCap == 0)
            {
                thingIdxCap = Math.Max(1536, Mathf.NextPowerOfTwo(ic));
                things.SetIndexBufferParams(thingIdxCap, IndexFormat.UInt32);
            }
            const MeshUpdateFlags fl = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
            if (tb.VertexCount > 0) things.SetVertexBufferData(tb.Vertices, 0, 0, tb.VertexCount, 0, fl);
            if (tb.IndexCount > 0) things.SetIndexBufferData(tb.Indices, 0, 0, tb.IndexCount, fl);
            things.subMeshCount = 2;
            things.SetSubMesh(0, new SubMeshDescriptor(0, tb.FuzzIndexStart), fl);
            things.SetSubMesh(1, new SubMeshDescriptor(tb.FuzzIndexStart, tb.IndexCount - tb.FuzzIndexStart), fl);
            things.bounds = new Bounds(UVec.zero, UVec.one * 200000F);
        }

        // slot -> atlas rect of the current (animated) picture
        private void UpdateLookup(World world)
        {
            var tt = world.Specials.TextureTranslation; var ft = world.Specials.FlatTranslation;
            var r = atlas.Rects; var d = lookupData;
            for (var i = 0; i < atlas.SlotCount; i++)
            {
                var s = i;
                if (i < atlas.TextureCount) s = tt[i];
                else if (i < atlas.TextureCount + atlas.FlatCount) s = atlas.FlatSlot(ft[i - atlas.TextureCount]);
                var rc = r[s];
                d[i * 4] = rc.X; d[i * 4 + 1] = rc.Y; d[i * 4 + 2] = rc.W; d[i * 4 + 3] = rc.H;
            }
            lookupTex.SetPixelData(d, 0);
            lookupTex.Apply(false, false);
        }

        private void UpdateSectors(World world)
        {
            var secs = world.Map.Sectors;
            for (var i = 0; i < secs.Length; i++)
            {
                var l = secs[i].LightLevel;
                var outdoor = secs[i].CeilingFlat == content.Flats.SkyFlatNumber;
                sectorPixels[i] = new Color32((byte)Mathf.Clamp(l, 0, 255), outdoor ? (byte)255 : (byte)0, 0, 255);
            }
            sectorTex.SetPixels32(sectorPixels);
            sectorTex.Apply(false, false);
        }

        private readonly Color32[] litPix = new Color32[256 * 34];

        private void UpdateLitPal(Player player)
        {
            var pal = content.Palette[Renderer.GetPaletteNumber(player)];
            if (ReferenceEquals(pal, litPalFor)) return;
            litPalFor = pal;
            var cm = content.ColorMap;
            for (var row = 0; row < 34; row++)
            {
                var map = cm[Math.Min(row, cm.Count - 1)];
                for (var i = 0; i < 256; i++)
                {
                    var c = pal[map[i]];
                    litPix[row * 256 + i] = new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), 255);
                }
            }
            litPalTex.SetPixels32(litPix);
            litPalTex.Apply(false, false);
        }

        public void Dispose()
        {
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            if (shadowRt != null) { shadowRt.Release(); UnityEngine.Object.Destroy(shadowRt); }
            if (comp != null) { comp.Release(); UnityEngine.Object.Destroy(comp); }
            foreach (var t in new UnityEngine.Object[] { atlasTex, lookupTex, sectorTex, litPalTex, level, things, worldMat, compMat })
                if (t != null) UnityEngine.Object.Destroy(t);
            cmd.Release();
        }
    }
}
