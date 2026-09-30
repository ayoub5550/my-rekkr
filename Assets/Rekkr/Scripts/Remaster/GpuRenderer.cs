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
        private RenderTexture rt, comp, behind;
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
        public Texture Render(UnityVideo video, DoomGame game, Fixed frac, WorldFx fx = null)
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
            curFrac = frac;
            BeginExtruded(v);
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
            // dev7: "Smooth lighting" off = the original 32 COLORMAP steps in Remaster too (it was always smooth)
            worldMat.SetVector("_Light", new Vector4(player.ExtraLight, player.FixedColorMap, RekkrSettings.SmoothLighting ? 1 : 0, Time.time));
            worldMat.SetFloat("_MaskCull", (float)(FlipCull ? CullMode.Front : CullMode.Back));

            cmd.Clear();
            calls0 = 0;
            RenderShadows(game, v);
            calls0 += RenderPointLights(fx, v);   // dev7
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 1));
            cmd.SetViewProjectionMatrices(view, proj);
            var calls = calls0;
            cmd.DrawMesh(level, Matrix4x4.identity, worldMat, 0, 0); calls++;   // opaque walls + flats
            cmd.DrawMesh(level, Matrix4x4.identity, worldMat, 2, 2); calls++;   // sky
            cmd.DrawMesh(level, Matrix4x4.identity, worldMat, 1, 1); calls++;   // masked mid textures
            if (thingBuilder.FuzzIndexStart > 0) { cmd.DrawMesh(things, Matrix4x4.identity, worldMat, 0, 1); calls++; }
            calls += DrawExtruded(4, null);
            if (thingBuilder.IndexCount > thingBuilder.FuzzIndexStart)
            {
                // dev7 spectre refraction: a copy of the scene so far, sampled bent through the spectre's shape
                if (behind == null || behind.width != rt.width || behind.height != rt.height)
                {
                    if (behind != null) { behind.Release(); UnityEngine.Object.Destroy(behind); }
                    behind = new RenderTexture(rt.width, rt.height, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "RemasterBehind" };
                    behind.Create();
                }
                if (SystemInfo.copyTextureSupport != CopyTextureSupport.None) cmd.CopyTexture(rt, behind);
                else { cmd.Blit(rt, behind); cmd.SetRenderTarget(rt); }
                worldMat.SetTexture("_Behind", behind);
                cmd.DrawMesh(things, Matrix4x4.identity, worldMat, 1, 5); calls++;
            }
            calls += DrawWeapon(v, player, eye, right, up, fwd);   // dev7 3D weapon (last, own depth)
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
            calls0 = 3 + DrawExtruded(3, maskedCaster);
        }

        // ---------------------------------------------------------------- dev7: 3D weapon
        // The weapon (and its flash) as extruded meshes at the exact screen place of the original psprite, with
        // a slight turn so its thickness shows, lit by the sector light, the sun and the dynamic lights, and
        // written with G-buffer code 249 (weapon) so the world effects leave it alone. A layer whose mesh is not
        // built yet (first use) or cannot be extruded (too big) stays the software sprite for that frame.
        private const int WeaponDepthClass = 7;
        public int WeaponLayersDrawn { get; private set; }

        /// <summary>Main thread, before the software pass: which psprite layers the GPU will draw.</summary>
        public void PrepareWeapon(Player player)
        {
            for (var i = 0; i < 2; i++) ThreeDRenderer.GpuWeaponLayer[i] = false;
            if (RekkrSettings.RemasterWeapon != 1 || player == null) return;
            if (player.Powers[(int)PowerType.Invisibility] > 4 * 32 || (player.Powers[(int)PowerType.Invisibility] & 8) != 0) return;
            for (var i = 0; i < 2; i++)
            {
                var psp = player.PlayerSprites[i];
                if (psp.State == null) continue;
                var frame = content.Sprites[psp.State.Sprite].Frames[psp.State.Frame & 0x7fff];
                ThreeDRenderer.GpuWeaponLayer[i] = WeaponMesh(frame.Patches[0], frame.Flip[0]) != null;
            }
        }

        private Mesh WeaponMesh(Patch patch, bool flip)
        {
            var slot = atlas.SpriteSlot(patch);
            if (slot < 0) return null;
            var key = (patch, flip, WeaponDepthClass);
            if (!extruded.TryGetValue(key, out var e))
            {
                e = new Extruded();
                e.Task = System.Threading.Tasks.Task.Run(() => SpriteExtruder.Build(patch, slot, flip, 0.6F, 1024));
                Debug.Log($"[REKKR] remaster weapon mesh queued {patch.Name} {patch.Width}x{patch.Height}");
                pendingBuilds++;
                extruded[key] = e;
            }
            e.LastUse = frameNo + 1;
            return e.Mesh;
        }

        private readonly MaterialPropertyBlock weaponBlock = new MaterialPropertyBlock();
        private float lastViewAngleDeg, weaponSway;

        private int DrawWeapon(ThreeDRenderer.ViewInfo v, Player player, UVec eye, UVec right, UVec up, UVec fwd)
        {
            WeaponLayersDrawn = 0;
            var any = false;
            for (var i = 0; i < 2; i++) any |= ThreeDRenderer.GpuWeaponLayer[i] && ThreeDRenderer.WeaponLayers[i].Valid;
            if (!any) return 0;
            cmd.ClearRenderTarget(true, false, Color.clear);   // the weapon never clips into walls
            const float D = 48F;                               // view depth of the weapon plane (map units)
            var k = D / v.Projection;                          // map units per window pixel at that depth
            // a small turn so the thickness catches the light; REKKR weapons are up to 700 px wide (the bow spans the
            // screen), so a bigger angle would distort them in perspective. Sways a little with turning.
            var turn = Mathf.DeltaAngle(lastViewAngleDeg, v.Angle * Mathf.Rad2Deg);
            lastViewAngleDeg = v.Angle * Mathf.Rad2Deg;
            weaponSway = Mathf.Lerp(weaponSway, Mathf.Clamp(turn * 0.6F, -2.5F, 2.5F), 0.15F);
            var tilt = Quaternion.AngleAxis(-1.5F + weaponSway, UVec.up) * Quaternion.AngleAxis(2F, UVec.right);
            var calls = 0;
            for (var i = 0; i < 2; i++)
            {
                var L = ThreeDRenderer.WeaponLayers[i];
                if (!ThreeDRenderer.GpuWeaponLayer[i] || !L.Valid) continue;
                var mesh = WeaponMesh(L.Patch, L.Flip);
                if (mesh == null) continue;
                var p = L.Patch;
                var a = L.Scale * k;                                        // units per patch pixel
                // mesh x = patch column - LeftOffset, y = rows from the bottom; screen rect = (X1, Top, w*s, h*s)
                var cx = (L.X1 + p.LeftOffset * L.Scale - v.CenterX) * k;  // view x of mesh x = 0
                var by = (v.CenterY - (L.Top + p.Height * L.Scale)) * k;   // view y of mesh y = 0 (bottom)
                var midX = (p.Width * 0.5F - p.LeftOffset);                 // turn around the sprite's centre
                var local = Matrix4x4.TRS(new UVec(cx + midX * a, by, D), tilt, new UVec(a, a, a)) * Matrix4x4.Translate(new UVec(-midX, 0, 0));
                var cam = Matrix4x4.identity;
                cam.SetColumn(0, new Vector4(right.x, right.y, right.z, 0));
                cam.SetColumn(1, new Vector4(up.x, up.y, up.z, 0));
                cam.SetColumn(2, new Vector4(fwd.x, fwd.y, fwd.z, 0));
                cam.SetColumn(3, new Vector4(eye.x, eye.y, eye.z, 1));
                weaponBlock.Clear();
                weaponBlock.SetVector("_Inst", new Vector4(player.Mobj.Subsector.Sector.Number, L.FullBright ? 1F : 0F, 1F, 0));
                cmd.DrawMesh(mesh, cam * local, worldMat, 0, 4, weaponBlock);
                calls++; WeaponLayersDrawn++;
            }
            return calls;
        }

        // ---------------------------------------------------------------- dev7: point lights + shadows
        // The dynamic lights (WorldFx.PrepareLights: projectiles, explosions, torches, muzzle flash) are applied
        // here per pixel with the real surface normal, and the nearest two cast shadows: a distance "cube" map
        // per light, stored as 3x2 tiles (+X -X +Y -Y +Z -Z) of a 2D RFloat atlas (no cube render targets).
        public const int PointShadowSize = 256;
        public const int MaxPointLights = 4, MaxPointShadows = 2;
        private RenderTexture pointRt;
        private readonly Matrix4x4[] pointVP = new Matrix4x4[MaxPointShadows * 6];
        private readonly Vector4[] plPos = new Vector4[MaxPointLights], plCol = new Vector4[MaxPointLights];
        private readonly MaterialPropertyBlock[] plOpaque = new MaterialPropertyBlock[MaxPointShadows], plMasked = new MaterialPropertyBlock[MaxPointShadows];
        public int PointLights { get; private set; }
        public int PointShadows { get; private set; }

        private static readonly UVec[] FaceFwd = { UVec.right, UVec.left, UVec.up, UVec.down, UVec.forward, UVec.back };
        private static readonly UVec[] FaceUp = { UVec.up, UVec.up, UVec.forward, UVec.back, UVec.up, UVec.up };

        private int RenderPointLights(WorldFx fx, ThreeDRenderer.ViewInfo v)
        {
            var n = 0; var shadows = 0; var calls = 0;
            if (fx != null && RekkrSettings.DynLights && WorldFx.RemasterLights)
            {
                for (var i = 0; i < fx.LightsLastFrame && n < MaxPointLights; i++)
                {
                    var p = fx.WorldLightPos[i]; var c = fx.WorldLightCol[i];
                    if (p.w <= 0) continue;
                    plPos[n] = new Vector4(p.x, p.z, p.y, p.w);   // unity (doom x, height, doom y), radius
                    // shadows: the first two sources that are not the muzzle flash / puffs (radius >= 120)
                    var shadowed = RekkrSettings.RemasterLightShadows && shadows < MaxPointShadows && p.w >= 120F && !(i == fx.LightsLastFrame - 1 && fx.MuzzleLast);
                    plCol[n] = new Vector4(c.x, c.y, c.z, shadowed ? shadows : -1);
                    if (shadowed) { calls += RenderPointShadow(shadows, new UVec(p.x, p.z, p.y), p.w); shadows++; }
                    n++;
                }
            }
            for (var i = n; i < MaxPointLights; i++) { plPos[i] = Vector4.zero; plCol[i] = Vector4.zero; }
            PointLights = n; PointShadows = shadows;
            worldMat.SetFloat("_PLCount", n);
            worldMat.SetVectorArray("_PLPos", plPos);
            worldMat.SetVectorArray("_PLCol", plCol);
            if (shadows > 0)
            {
                worldMat.SetMatrixArray("_PLVP", pointVP);
                worldMat.SetTexture("_PLShadow", pointRt);
                worldMat.SetVector("_PLAtlas", new Vector4(PointShadowSize, 1F / PointShadowSize, 1F / 3F, 1F / (2F * MaxPointShadows)));
            }
            return calls;
        }

        private int RenderPointShadow(int j, UVec L, float radius)
        {
            if (pointRt == null)
            {
                pointRt = new RenderTexture(3 * PointShadowSize, 2 * MaxPointShadows * PointShadowSize, 16, RenderTextureFormat.RFloat)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "RemasterPointShadows" };
                pointRt.Create();
                for (var k = 0; k < MaxPointShadows; k++) { plOpaque[k] = new MaterialPropertyBlock(); plMasked[k] = new MaterialPropertyBlock(); }
            }
            if (j == 0)
            {
                cmd.SetRenderTarget(pointRt);
                cmd.ClearRenderTarget(true, true, new Color(1e7F, 0, 0, 0));
            }
            plOpaque[j].SetVector("_PLCaster", new Vector4(L.x, L.y, L.z, 0));
            plMasked[j].SetVector("_PLCaster", new Vector4(L.x, L.y, L.z, 1));
            var proj = Matrix4x4.Perspective(90F, 1F, 1F, Mathf.Max(radius, 16F) * 1.05F);
            var calls = 0;
            for (var f = 0; f < 6; f++)
            {
                var fwd = FaceFwd[f]; var up = FaceUp[f]; var right = UVec.Cross(up, fwd);
                var view = Matrix4x4.identity;
                view.SetRow(0, new Vector4(right.x, right.y, right.z, -UVec.Dot(right, L)));
                view.SetRow(1, new Vector4(up.x, up.y, up.z, -UVec.Dot(up, L)));
                view.SetRow(2, new Vector4(-fwd.x, -fwd.y, -fwd.z, UVec.Dot(fwd, L)));
                view.SetRow(3, new Vector4(0, 0, 0, 1));
                pointVP[j * 6 + f] = proj * view;
                cmd.SetViewport(new Rect((f % 3) * PointShadowSize, (f / 3 + 2 * j) * PointShadowSize, PointShadowSize, PointShadowSize));
                cmd.SetViewProjectionMatrices(view, proj);
                cmd.DrawMesh(level, Matrix4x4.identity, worldMat, 0, 6, plOpaque[j]);
                cmd.DrawMesh(level, Matrix4x4.identity, worldMat, 1, 6, plMasked[j]);
                if (thingBuilder.FuzzIndexStart > 0) cmd.DrawMesh(things, Matrix4x4.identity, worldMat, 0, 6, plMasked[j]);
                calls += 3;
                // extruded things near the light
                foreach (var (mesh, m, sector, bright) in instances)
                {
                    var pos = (UVec)m.GetColumn(3);
                    if ((pos - L).sqrMagnitude > radius * radius) continue;
                    cmd.DrawMesh(mesh, m, worldMat, 0, 6, plMasked[j]);
                    calls++;
                }
            }
            return calls;
        }

        // ---------------------------------------------------------------- stage 6: extruded sprites
        private sealed class Extruded
        {
            public System.Threading.Tasks.Task<ExtrudedMesh> Task;
            public Mesh Mesh; public int Bytes; public int LastUse;
        }
        private readonly System.Collections.Generic.Dictionary<(Patch, bool, int), Extruded> extruded = new System.Collections.Generic.Dictionary<(Patch, bool, int), Extruded>();
        private readonly System.Collections.Generic.List<(Mesh mesh, Matrix4x4 m, float sector, float bright)> instances = new System.Collections.Generic.List<(Mesh, Matrix4x4, float, float)>();
        private readonly MaterialPropertyBlock instBlock = new MaterialPropertyBlock();
        private int frameNo, pendingBuilds;
        private long cacheBytes;
        public const long CacheBudget = 64L << 20;   // 64 MB (DEV6 §5)
        public float CacheMB => cacheBytes / 1048576F;
        public int ExtrudedCount => instances.Count;
        private float instCa, instSa;
        private Fixed curFrac;

        private void BeginExtruded(ThreeDRenderer.ViewInfo v)
        {
            frameNo++;
            instances.Clear();
            instCa = Mathf.Cos(v.Angle); instSa = Mathf.Sin(v.Angle);
            RefreshVoxels();
            thingBuilder.Replace = RekkrSettings.RemasterThings == 1 || voxelMap.Entries.Count > 0 ? ReplaceThing : (Func<Mobj, Patch, bool, float, float, float, bool>)null;
            // finished worker builds -> meshes (main thread), at most 24 per frame
            var made = 0;
            foreach (var e in extruded.Values)
            {
                if (e.Mesh != null || e.Task == null || !e.Task.IsCompleted || made >= 24) continue;
                made++;
                var r = e.Task.IsFaulted ? null : e.Task.Result;
                e.Task = null; pendingBuilds--;
                if (r == null || r.Indices.Length == 0) { e.Bytes = 0; continue; }
                var m = new Mesh { name = "RemasterExtruded" };
                m.SetVertexBufferParams(r.Vertices.Length, Layout);
                m.SetVertexBufferData(r.Vertices, 0, 0, r.Vertices.Length);
                m.SetIndexBufferParams(r.Indices.Length, IndexFormat.UInt32);
                m.SetIndexBufferData(r.Indices, 0, 0, r.Indices.Length);
                m.subMeshCount = 1;
                m.SetSubMesh(0, new SubMeshDescriptor(0, r.Indices.Length));
                m.RecalculateBounds();
                e.Mesh = m; e.Bytes = r.Bytes; cacheBytes += e.Bytes;
            }
            if (cacheBytes > CacheBudget) EvictLru();
        }

        private void EvictLru()
        {
            var list = new System.Collections.Generic.List<((Patch, bool, int) k, Extruded e)>();
            foreach (var kv in extruded) if (kv.Value.Mesh != null) list.Add((kv.Key, kv.Value));
            list.Sort((a, b) => a.e.LastUse.CompareTo(b.e.LastUse));
            foreach (var (k, e) in list)
            {
                if (cacheBytes <= CacheBudget * 3 / 4 || e.LastUse >= frameNo - 1) break;
                cacheBytes -= e.Bytes; UnityEngine.Object.Destroy(e.Mesh); extruded.Remove(k);
            }
        }

        private static int DepthClass(Mobj mo)
        {
            if ((mo.Flags & MobjFlags.CountKill) != 0) return 10;      // monsters: full volume
            if ((mo.Flags & MobjFlags.Missile) != 0) return 6;
            if ((mo.Flags & MobjFlags.Special) != 0) return 5;         // pickups: flatter
            return 8;                                                   // decorations, corpses
        }

        // ---------------------------------------------------------------- stage 7: KVX voxel packs
        public static string VoxelDir;                  // persistentDataPath/voxels (set by the app)
        private VoxelMap voxelMap = new VoxelMap();
        private readonly System.Collections.Generic.Dictionary<string, Mesh> voxelMeshes = new System.Collections.Generic.Dictionary<string, Mesh>();
        private DateTime voxelStamp = DateTime.MinValue;
        private float voxelCheck = -10;
        public int VoxelModels => voxelMeshes.Count;
        public int VoxelMappings => voxelMap.Entries.Count;
        private static readonly string[] spriteNames = Enum.GetNames(typeof(Sprite));

        private void RefreshVoxels()
        {
            if (string.IsNullOrEmpty(VoxelDir) || Time.realtimeSinceStartup - voxelCheck < 2F) return;
            voxelCheck = Time.realtimeSinceStartup;
            var txt = System.IO.Path.Combine(VoxelDir, "voxels.txt");
            var stamp = System.IO.File.Exists(txt) ? System.IO.File.GetLastWriteTimeUtc(txt) : DateTime.MinValue;
            if (stamp == voxelStamp) return;
            voxelStamp = stamp;
            foreach (var m in voxelMeshes.Values) if (m != null) UnityEngine.Object.Destroy(m);
            voxelMeshes.Clear();
            voxelMap = stamp == DateTime.MinValue ? new VoxelMap() : VoxelMap.Parse(System.IO.File.ReadAllText(txt));
            var pal = content.Wad.ReadLump("PLAYPAL");
            foreach (var kv in voxelMap.Entries)
            {
                try
                {
                    var file = System.IO.Path.Combine(VoxelDir, kv.Value.File);
                    if (!voxelMeshes.ContainsKey(kv.Value.File))
                    {
                        var model = KvxModel.Load(System.IO.File.ReadAllBytes(file), pal);
                        var em = model.Mesh(1F);
                        voxelMeshes[kv.Value.File] = ToMesh(em, "RemasterVoxel");
                        Debug.Log($"[REKKR] voxel {kv.Key} = {kv.Value.File} {model.SizeX}x{model.SizeY}x{model.SizeZ} tris={em.Indices.Length / 3}");
                    }
                }
                catch (Exception ex) { Debug.LogWarning($"[REKKR] voxel {kv.Key}: {ex.Message}"); }
            }
            Debug.Log($"[REKKR] voxels.txt loaded: {voxelMap.Entries.Count} mappings, {voxelMeshes.Count} models");
        }

        private static Mesh ToMesh(ExtrudedMesh r, string name)
        {
            var m = new Mesh { name = name };
            m.SetVertexBufferParams(r.Vertices.Length, Layout);
            m.SetVertexBufferData(r.Vertices, 0, 0, r.Vertices.Length);
            m.SetIndexBufferParams(r.Indices.Length, IndexFormat.UInt32);
            m.SetIndexBufferData(r.Indices, 0, 0, r.Indices.Length);
            m.subMeshCount = 1;
            m.SetSubMesh(0, new SubMeshDescriptor(0, r.Indices.Length));
            m.RecalculateBounds();
            return m;
        }

        private bool TryVoxel(Mobj mo, float x, float y, float z)
        {
            if (voxelMap.Entries.Count == 0) return false;
            var key = spriteNames[(int)mo.Sprite] + (char)('A' + (mo.Frame & 0x7FFF));
            if (!voxelMap.Entries.TryGetValue(key, out var e) || !voxelMeshes.TryGetValue(e.File, out var mesh) || mesh == null) return false;
            var a = (float)(mo.Angle.Data * (Math.PI * 2 / 4294967296.0)) + (e.Angle + e.Spin * Time.time) * Mathf.Deg2Rad;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            // model front (-z) faces the thing's direction: mesh z = -facing, x = right of the facing
            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(sa, 0, -ca, 0) * e.Scale);
            m.SetColumn(1, new Vector4(0, 1, 0, 0) * e.Scale);
            m.SetColumn(2, new Vector4(-ca, 0, -sa, 0) * e.Scale);
            m.SetColumn(3, new Vector4(x, z, y, 1));
            instances.Add((mesh, m, mo.Subsector.Sector.Number, (mo.Frame & 0x8000) != 0 ? 1F : 0F));
            return true;
        }

        private bool ReplaceThing(Mobj mo, Patch patch, bool flip, float x, float y, float bottom)
        {
            if ((mo.Flags & MobjFlags.Shadow) != 0) return false;      // spectres keep the fuzz billboard
            if (TryVoxel(mo, x, y, mo.GetInterpolatedZ(curFrac).ToFloat())) return true;
            if (RekkrSettings.RemasterThings != 1) return false;
            var slot = atlas.SpriteSlot(patch);
            if (slot < 0) return false;
            var key = (patch, flip, DepthClass(mo));
            if (!extruded.TryGetValue(key, out var e))
            {
                if (pendingBuilds > 64) return false;
                e = new Extruded();
                var depth = key.Item3 / 10F;
                e.Task = System.Threading.Tasks.Task.Run(() => SpriteExtruder.Build(patch, slot, flip, depth));
                pendingBuilds++;
                extruded[key] = e;
            }
            e.LastUse = frameNo;
            if (e.Mesh == null) return false;                          // billboard until the mesh is ready
            // camera-plane aligned like the billboard: mesh x = camera right, z = away from the camera
            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(instSa, 0, -instCa, 0));
            m.SetColumn(1, new Vector4(0, 1, 0, 0));
            m.SetColumn(2, new Vector4(instCa, 0, instSa, 0));
            m.SetColumn(3, new Vector4(x, bottom, y, 1));
            instances.Add((e.Mesh, m, mo.Subsector.Sector.Number, (mo.Frame & 0x8000) != 0 ? 1F : 0F));
            return true;
        }

        private int DrawExtruded(int pass, MaterialPropertyBlock casterBlock)
        {
            foreach (var (mesh, m, sector, bright) in instances)
            {
                instBlock.Clear();
                instBlock.SetVector("_Inst", new Vector4(sector, bright, 0, 0));
                if (casterBlock != null) instBlock.SetVector("_SunCasterMask", new Vector4(1, 0, 0, 0));
                cmd.DrawMesh(mesh, m, worldMat, 0, pass, instBlock);
            }
            return instances.Count;
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
                var l = Math.Max(secs[i].LightLevel, ThreeDRenderer.MinSectorLight);   // dev6 "dark areas" floor
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
            if (pointRt != null) { pointRt.Release(); UnityEngine.Object.Destroy(pointRt); }
            if (behind != null) { behind.Release(); UnityEngine.Object.Destroy(behind); }
            if (comp != null) { comp.Release(); UnityEngine.Object.Destroy(comp); }
            foreach (var t in new UnityEngine.Object[] { atlasTex, lookupTex, sectorTex, litPalTex, level, things, worldMat, compMat })
                if (t != null) UnityEngine.Object.Destroy(t);
            cmd.Release();
        }
    }
}
