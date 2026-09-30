// my-rekkr dev5 — "Masterpiece" world effects driver: per-episode atmosphere (sun, clouds, fog,
// weather), dynamic lights from fullbright things, visual-only particles, and the GPU passes of
// Rekkr/World on the frame texture (see docs/DEV5.md §2). Reads the game world only; never touches
// the simulation or its RNG (UnityEngine.Random is used for visuals).
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Collections.Generic;
using ManagedDoom;
using ManagedDoom.Video;
using UnityEngine;

namespace ManagedDoom.UnityPort
{
    using Texture = UnityEngine.Texture;
    using UVec = UnityEngine.Vector3;
    using Random = UnityEngine.Random;

    public sealed class WorldFx
    {
        private readonly Material mat;
        private readonly GameContent content;
        private readonly byte[] playpal;
        private RenderTexture world, output, rays, blurA, blurB;
        private int rtW, rtH;   // RT size = (frame rows, frame columns) — transposed like the frame

        // ---------------------------------------------------------------- per-episode atmosphere
        private struct Atmos
        {
            public float SunYaw, SunElev, SunStrength; public Color Sun; public float Rays;
            public Color Cloud; public float Coverage, CloudSpeed; public Vector2 Wind;
            public Color Fog; public float FogMul; public int AutoWeather; public float DriftBamPerSec;
        }

        // E1 blue sky (sea), E2 orange dusk storm, E3 hell, E4 sunset (sky art: SKY1..SKY4).
        private static readonly Atmos[] Episodes =
        {
            new Atmos { SunYaw = 60, SunElev = 48, SunStrength = 1.0F, Sun = new Color(1F, 0.95F, 0.82F), Rays = 0.9F,
                        Cloud = new Color(0.97F, 0.98F, 1F), Coverage = 0.42F, CloudSpeed = 0.018F, Wind = new Vector2(1F, 0.35F),
                        Fog = new Color(0.62F, 0.72F, 0.86F), FogMul = 0.8F, AutoWeather = 0, DriftBamPerSec = 1.2e6F },
            new Atmos { SunYaw = 200, SunElev = 7, SunStrength = 0.9F, Sun = new Color(1F, 0.58F, 0.28F), Rays = 1.1F,
                        Cloud = new Color(0.52F, 0.28F, 0.17F), Coverage = 0.58F, CloudSpeed = 0.03F, Wind = new Vector2(-0.8F, 0.6F),
                        Fog = new Color(0.42F, 0.28F, 0.2F), FogMul = 1.1F, AutoWeather = 1, DriftBamPerSec = 2.5e6F },
            new Atmos { SunYaw = 0, SunElev = 0, SunStrength = 0F, Sun = new Color(1F, 0.3F, 0.1F), Rays = 0F,
                        Cloud = new Color(0.26F, 0.07F, 0.04F), Coverage = 0.5F, CloudSpeed = 0.04F, Wind = new Vector2(0.5F, 1F),
                        Fog = new Color(0.3F, 0.07F, 0.04F), FogMul = 1.0F, AutoWeather = 3, DriftBamPerSec = 3.5e6F },
            new Atmos { SunYaw = 300, SunElev = 9, SunStrength = 1.0F, Sun = new Color(1F, 0.8F, 0.45F), Rays = 1.2F,
                        Cloud = new Color(1F, 0.78F, 0.55F), Coverage = 0.34F, CloudSpeed = 0.015F, Wind = new Vector2(0.9F, -0.3F),
                        Fog = new Color(0.56F, 0.38F, 0.45F), FogMul = 0.9F, AutoWeather = 4, DriftBamPerSec = 1.0e6F },
        };

        /// <summary>dev6: the episode's sun (yaw/elevation in degrees, strength, colour) for the Remaster shadows.</summary>
        public static (float yaw, float elev, float strength, Color color) SunOf(int episode)
        {
            var a = Episodes[Mathf.Clamp(episode, 1, 4) - 1];
            return (a.SunYaw, a.SunElev, a.SunStrength, a.Sun);
        }

        public WorldFx(GameContent content)
        {
            this.content = content;
            mat = new Material(Resources.Load<Shader>("Rekkr/RekkrWorld"));
            playpal = content.Wad.ReadLump("PLAYPAL");
        }

        public static bool Active => RekkrSettings.AnyWorldFx;

        /// <summary>Debug false-colour G-buffer view (REKKR_GBUF=1 on desktop).</summary>
        public static bool DebugView;

        private static RenderTexture Make(int w, int h, bool bilinear)
        {
            var rt = new RenderTexture(Mathf.Max(1, w), Mathf.Max(1, h), 0, RenderTextureFormat.ARGB32);
            rt.filterMode = bilinear ? FilterMode.Bilinear : FilterMode.Point;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.Create();
            return rt;
        }

        private void Ensure(int w, int h)
        {
            if (world != null && w == rtW && h == rtH) return;
            Release();
            rtW = w; rtH = h;
            world = Make(w, h, false);
            output = Make(w, h, true);
            rays = Make(w / 2, h / 2, true);
            blurA = Make(w / 2, h / 2, true);
            blurB = Make(w / 4, h / 4, true);
        }

        public void Release()
        {
            foreach (var rt in new[] { world, output, rays, blurA, blurB })
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            world = output = rays = blurA = blurB = null;
        }

        // ---------------------------------------------------------------- state
        private float outdoor, lightning, nextLightning = 8F;
        private int lightningStage;
        private float lightningT;
        public int LightsLastFrame { get; private set; }
        public int ParticlesLastFrame => particles.Count;

        /// <summary>Runs the effect passes for this frame and returns the processed frame texture
        /// (same layout as the input), or the input when nothing to do.</summary>
        public Texture Process(UnityVideo video, DoomGame game, Fixed frameFrac)
        {
            var src = video.FrameTexture;   // dev6: Remaster composite when active
            if (game == null || !RekkrSettings.FxLighting) { ThreeDRenderer.SkyDriftBam = 0; return src; }
            var world3 = game.World;
            var ep = Mathf.Clamp(game.Options.Episode, 1, 4) - 1;
            var a = Episodes[ep];
            var t = Time.time;
            var dt = Mathf.Min(Time.deltaTime, 0.05F);
            ThreeDRenderer.SkyDriftBam = RekkrSettings.SkyFx ? (uint)(long)(t * a.DriftBamPerSec) : 0;

            int W = video.FrameWidth, H = video.FrameHeight;
            Ensure(H, W);
            var v = ThreeDRenderer.LastView;

            mat.SetVector("_Frame", new Vector4(W, H, 1F / W, 1F / H));
            mat.SetVector("_ViewP", new Vector4(v.CenterX + v.WindowX, v.CenterY + v.WindowY, v.Projection, t));
            mat.SetVector("_Win", new Vector4(v.WindowX, v.WindowY, v.WindowW, v.WindowH));
            mat.SetVector("_Cam", new Vector4(v.X, v.Y, v.Z, v.Angle));

            // sun
            float yaw = a.SunYaw * Mathf.Deg2Rad, el = a.SunElev * Mathf.Deg2Rad;
            var sun = new UVec(Mathf.Cos(el) * Mathf.Cos(yaw), Mathf.Cos(el) * Mathf.Sin(yaw), Mathf.Sin(el));
            var sunOn = RekkrSettings.SkyFx ? a.SunStrength : 0F;
            mat.SetVector("_Sun", new Vector4(sun.x, sun.y, sun.z, sunOn));
            mat.SetVector("_SunCol", new Vector4(a.Sun.r, a.Sun.g, a.Sun.b, RekkrSettings.SunRays ? a.Rays : 0F));
            sunCol = a.Sun;
            var sunScreen = Vector4.zero;
            {
                float ca = Mathf.Cos(v.Angle), sa = Mathf.Sin(v.Angle);
                var zv = sun.x * ca + sun.y * sa;
                var xv = sun.x * sa - sun.y * ca;
                if (zv > 0.05F && RekkrSettings.SunRays && a.Rays > 0)
                {
                    var sx = v.WindowX + v.CenterX + xv / zv * v.Projection;
                    var sy = v.WindowY + v.CenterY - sun.z / zv * v.Projection;
                    var margin = v.WindowW * 0.5F;
                    if (sx > v.WindowX - margin && sx < v.WindowX + v.WindowW + margin && sy > v.WindowY - margin && sy < v.WindowY + v.WindowH)
                        sunScreen = new Vector4(sx, sy, 1, v.WindowW * 0.9F);
                }
            }
            mat.SetVector("_SunScreen", sunScreen);

            // fog / clouds
            var fogD = RekkrSettings.Fog == 0 ? 0F : (RekkrSettings.Fog == 1 ? 1F / 3200F : 1F / 1600F) * a.FogMul;   // dev7: thinner
            mat.SetVector("_FogCol", new Vector4(a.Fog.r, a.Fog.g, a.Fog.b, fogD));
            mat.SetVector("_Cloud", new Vector4(a.Cloud.r, a.Cloud.g, a.Cloud.b, a.Coverage));
            mat.SetVector("_CloudP", new Vector4(a.Wind.x, a.Wind.y, a.CloudSpeed, RekkrSettings.SkyFx ? 1 : 0));

            // weather (outdoors only), lightning in rain
            var wType = RekkrSettings.Weather == 3 ? 0 : RekkrSettings.Weather == 0 ? a.AutoWeather : RekkrSettings.Weather;
            outdoor = Mathf.MoveTowards(outdoor, v.SkyCeiling ? 1F : 0F, dt * 2F);
            UpdateLightning(wType == 1, t, dt);
            var inten = wType == 0 ? 0F : wType == 1 ? 1F : wType == 2 ? 0.9F : wType == 3 ? 0.9F : 0.8F;
            mat.SetVector("_Weather", new Vector4(wType, inten * outdoor, lightning * outdoor, wType == 1 ? outdoor : 0F));
            mat.SetVector("_FogP", new Vector4(outdoor, 0, 0, 0));

            mat.SetVector("_Fx", new Vector4(RekkrSettings.WaterFx ? 1 : 0, RekkrSettings.AO ? 0.55F : 0F, RekkrSettings.DynLights ? 1 : 0, RekkrSettings.WaterFx ? 1 : 0));
            mat.SetVector("_Fx2", new Vector4(fogD > 0 ? 1 : 0, RekkrSettings.DoF ? 1 : 0, 700F, DebugView ? 1 : 0));

            // lights
            if (preparedFrame != Time.frameCount) PrepareLights(game, frameFrac);
            mat.SetFloat("_LightCount", RemasterLights ? 0 : LightsLastFrame);   // dev7: Remaster lights in its own shader
            mat.SetVectorArray("_LightPos", lightPos);
            mat.SetVectorArray("_LightCol", lightCol);

            // passes
            mat.SetTexture("_GTex", src);
            Graphics.Blit(src, world, mat, 0);
            if (RekkrSettings.Particles) UpdateParticles(world3, frameFrac, v, dt);
            else particles.Clear();
            UpdateWeather(world3, v, wType, a, dt);   // dev7: 3D weather
            DrawParticles(v);
            var raysOn = sunScreen.z > 0;
            if (raysOn) Graphics.Blit(world, rays, mat, 1);
            if (RekkrSettings.DoF)
            {
                Graphics.Blit(world, blurA, mat, 5);   // dev6: masked (no weapon/HUD halo)
                Graphics.Blit(blurA, blurB, mat, 2);
            }
            mat.SetTexture("_RaysTex", raysOn ? (Texture)rays : Texture2D.blackTexture);
            mat.SetTexture("_BlurTex", RekkrSettings.DoF ? (Texture)blurB : Texture2D.blackTexture);
            mat.SetTexture("_GTex", src);
            Graphics.Blit(world, output, mat, 3);
            return output;
        }

        private void UpdateLightning(bool raining, float t, float dt)
        {
            if (!raining) { lightning = 0; lightningStage = 0; return; }
            if (lightningStage == 0)
            {
                nextLightning -= dt;
                if (nextLightning <= 0) { lightningStage = 1; lightningT = 0; }
                lightning = 0;
                return;
            }
            lightningT += dt;
            // two quick flashes: 0-0.08 s, gap, 0.16-0.3 s
            if (lightningT < 0.08F) lightning = 0.9F * (1 - lightningT / 0.08F);
            else if (lightningT < 0.16F) lightning = 0.05F;
            else if (lightningT < 0.34F) lightning = 0.7F * (1 - (lightningT - 0.16F) / 0.18F);
            else { lightning = 0; lightningStage = 0; nextLightning = Random.Range(7F, 20F); }
        }

        // ---------------------------------------------------------------- dynamic lights
        /// <summary>dev7: the same lights in world space (Doom x, y, z, radius) + rgb, for the Remaster shader
        /// (real normals + point-light shadows). Valid after PrepareLights this frame.</summary>
        public readonly Vector4[] WorldLightPos = new Vector4[8];
        public readonly Vector4[] WorldLightCol = new Vector4[8];
        /// <summary>dev7: set by the app when the GPU renderer lights the world itself (then this pass does not).</summary>
        public static bool RemasterLights;
        /// <summary>dev7: the last light of this frame is the player's muzzle flash (casts no shadow).</summary>
        public bool MuzzleLast { get; private set; }
        private int preparedFrame = -1;

        /// <summary>dev7: gathers this frame's lights (call after the software view pass set LastView).</summary>
        public void PrepareLights(DoomGame game, Fixed frac)
        {
            preparedFrame = Time.frameCount;
            if (game == null || !RekkrSettings.DynLights || !RekkrSettings.FxLighting) { LightsLastFrame = 0; lightFade.Clear(); return; }
            GatherLights(game.World, frac, ThreeDRenderer.LastView, Mathf.Min(Time.deltaTime, 0.05F));
        }

        private readonly Vector4[] lightPos = new Vector4[8];
        private readonly Vector4[] lightCol = new Vector4[8];
        private readonly List<(float d, Vector4 p, Vector4 c, Mobj m)> lightCand = new List<(float, Vector4, Vector4, Mobj)>(64);
        private readonly Dictionary<int, Color> spriteColor = new Dictionary<int, Color>();

        /// <summary>Average colour of the bright pixels of a sprite frame (cached), as a light colour.</summary>
        private Color SpriteLightColor(Sprite sprite, int frame)
        {
            var key = ((int)sprite << 8) | (frame & 0xFF);
            if (spriteColor.TryGetValue(key, out var c)) return c;
            c = new Color(1F, 0.7F, 0.4F);
            try
            {
                var def = content.Sprites[sprite];
                var f = def.Frames[frame & 0x7FFF];
                var patch = f.Patches[0];
                double r = 0, g = 0, b = 0, wsum = 0;
                foreach (var cols in patch.Columns)
                    foreach (var col in cols)
                        for (var i = 0; i < col.Length; i++)
                        {
                            var p = col.Data[col.Offset + i];
                            double pr = playpal[3 * p] / 255.0, pg = playpal[3 * p + 1] / 255.0, pb = playpal[3 * p + 2] / 255.0;
                            var lum = 0.3 * pr + 0.55 * pg + 0.15 * pb;
                            var w = lum * lum * lum;
                            r += pr * w; g += pg * w; b += pb * w; wsum += w;
                        }
                if (wsum > 0)
                {
                    var m = System.Math.Max(r, System.Math.Max(g, b));
                    c = new Color((float)(r / m), (float)(g / m), (float)(b / m));
                }
            }
            catch { }
            spriteColor[key] = c;
            return c;
        }

        // dev7 rework (owner: "the dynamic lighting is horribly strong and bad"). dev5/dev6 lit the world from
        // every thing with a fullbright frame (items, lamps, torches, fireballs) at 150–320 units, up to ×1.9,
        // through walls, plus a big muzzle light on top of Doom's own extralight. Now:
        //  - sources: projectiles and their explosions (strong), fullbright decorations (torches, lamps: weak,
        //    small), monster attack flashes (short, weak); pickups (MF_SPECIAL) never glow on the world;
        //  - occlusion: a source the player cannot see (Doom sight check, REJECT + BSP) does not light;
        //  - the muzzle flash is small (Doom's extralight already brightens the whole view);
        //  - intensity per level (Low = subtle, High = the old look halved) and a smooth fade in/out.
        private readonly Dictionary<Mobj, float> lightFade = new Dictionary<Mobj, float>();
        private readonly List<Mobj> fadeGone = new List<Mobj>();

        private void GatherLights(World w, Fixed frac, ThreeDRenderer.ViewInfo v, float dt)
        {
            lightCand.Clear();
            float ca = Mathf.Cos(v.Angle), sa = Mathf.Sin(v.Angle);
            var player = w.DisplayPlayer;
            if (player.FixedColorMap != 0) { LightsLastFrame = 0; lightFade.Clear(); return; }   // light amp / invulnerability
            var level = Mathf.Clamp(RekkrSettings.DynLightLevel, 1, 2);
            var gain = level == 1 ? 0.55F : 1F;
            foreach (var th in w.Thinkers)
            {
                if (!(th is Mobj m) || m.ThinkerState != ThinkerState.Active) continue;
                if (m == player.Mobj || (m.Frame & 0x8000) == 0) continue;
                if ((m.Flags & MobjFlags.Special) != 0) continue;              // pickups: glow themselves only
                // projectiles in flight, or exploding (ExplodeMissile clears MF_MISSILE, NOBLOCKMAP|NOGRAVITY stay)
                var missile = (m.Flags & MobjFlags.Missile) != 0 || ((m.Flags & MobjFlags.NoBlockMap) != 0 && (m.Flags & MobjFlags.NoGravity) != 0);
                var monster = (m.Flags & MobjFlags.CountKill) != 0;
                var x = m.GetInterpolatedX(frac).ToFloat(); var y = m.GetInterpolatedY(frac).ToFloat();
                var dx = x - v.X; var dy = y - v.Y;
                var d2 = dx * dx + dy * dy;
                if (d2 > 1200F * 1200F) continue;
                var zv = dx * ca + dy * sa;
                var xv = dx * sa - dy * ca;
                // radius / strength per kind: projectiles + explosions, attacking monsters, decorations
                float radius, k;
                if (m.Type == MobjType.Puff) { radius = 70F; k = 0.25F; }
                else if (missile) { radius = 190F; k = 0.9F; }
                else if (monster) { radius = 120F; k = 0.35F; }
                else { radius = 130F; k = 0.3F; }
                if (zv < -radius || Mathf.Abs(xv) > zv * 2.2F + radius) continue;
                var h = m.Height.ToFloat();
                var z = m.GetInterpolatedZ(frac).ToFloat() + h * 0.6F;
                lightCand.Add((d2, new Vector4(xv, z - v.Z, zv, radius), new Vector4(k, x, y, z), m));
            }
            lightCand.Sort((p, q) => p.d.CompareTo(q.d));
            var n = 0;
            fadeGone.Clear();
            foreach (var key in lightFade.Keys) fadeGone.Add(key);
            for (var i = 0; i < lightCand.Count && n < 8; i++)
            {
                var (d, pos, kk, m) = lightCand[i];
                // occlusion: only sources the player can see light the view (no light through walls)
                var visible = player.Mobj == null || w.VisibilityCheck.CheckSight(player.Mobj, m);
                lightFade.TryGetValue(m, out var fade);
                fade = Mathf.MoveTowards(fade, visible ? 1F : 0F, dt * 6F);
                lightFade[m] = fade;
                fadeGone.Remove(m);
                if (fade <= 0.01F) continue;
                var col = SpriteLightColor(m.Sprite, m.Frame);
                var s = kk.x * gain * fade;
                lightPos[n] = pos;
                lightCol[n] = new Vector4(col.r * s, col.g * s, col.b * s, 0);
                WorldLightPos[n] = new Vector4(kk.y, kk.z, kk.w, pos.w);
                WorldLightCol[n] = lightCol[n];
                n++;
            }
            foreach (var g in fadeGone) lightFade.Remove(g);
            MuzzleLast = false;
            if (player.ExtraLight > 0 && player.Mobj != null && n < 8)
            {
                MuzzleLast = true;
                // muzzle flash: small and warm; Doom's extralight already lifts the whole view
                lightPos[n] = new Vector4(0, -6, 30, 150F + 30F * player.ExtraLight);
                lightCol[n] = new Vector4(0.45F * gain, 0.34F * gain, 0.2F * gain, 0);
                WorldLightPos[n] = new Vector4(v.X + ca * 30F, v.Y + sa * 30F, v.Z - 6F, lightPos[n].w);
                WorldLightCol[n] = lightCol[n];
                n++;
            }
            for (var i = n; i < 8; i++) { lightPos[i] = Vector4.zero; lightCol[i] = Vector4.zero; WorldLightPos[i] = Vector4.zero; WorldLightCol[i] = Vector4.zero; }
            LightsLastFrame = n;
        }

        // ---------------------------------------------------------------- particles (visual only)
        // dev8: Solid = alpha-blended (pass 6: smoke, chips, blood), Grow = size growth per second, Stick = rests on the floor
        private struct Particle { public UVec P, V; public Color C; public float Life, MaxLife, Size, Floor, Grow, Drag; public bool Glow, Rise, Solid, Stick, Rest; }
        private struct Drop { public UVec P, V; public float Floor, Life, Phase; public byte Kind; }   // dev7 weather
        private const int MaxParticles = 900;   // dev8 (was 512): impacts, smoke and debris
        private readonly List<Particle> particles = new List<Particle>(MaxParticles);
        private readonly HashSet<Mobj> seen = new HashSet<Mobj>();
        private readonly HashSet<Mobj> alive = new HashSet<Mobj>();
        private World lastWorld;
        private float splashTimer, emberTimer;

        private void Spawn(UVec p, UVec vel, Color c, float life, float size, float floor, bool glow, bool rise = false)
        {
            if (particles.Count >= MaxParticles) return;
            particles.Add(new Particle { P = p, V = vel, C = c, Life = life, MaxLife = life, Size = size, Floor = floor, Glow = glow, Rise = rise });
        }

        /// <summary>dev8: alpha-blended particle (smoke, chips, debris, blood).</summary>
        private void SpawnSolid(UVec p, UVec vel, Color c, float life, float size, float floor, float grow = 0, bool rise = false, float drag = 0, bool stick = false)
        {
            if (particles.Count >= MaxParticles) return;
            particles.Add(new Particle { P = p, V = vel, C = c, Life = life, MaxLife = life, Size = size, Floor = floor, Grow = grow, Drag = drag, Rise = rise, Solid = true, Stick = stick });
        }

        private static UVec Rnd(float h, float z0, float z1) => new UVec(Random.Range(-h, h), Random.Range(-h, h), Random.Range(z0, z1));

        /// <summary>dev8 bullet impact: stone chips that bounce, a small dust puff that drifts up.</summary>
        private void ImpactDetail(UVec p, float floor)
        {
            for (var i = 0; i < 5; i++)
            {
                var g = Random.Range(0.18F, 0.32F);
                SpawnSolid(p, Rnd(110F, 30F, 170F), new Color(g, g * 0.92F, g * 0.8F, 0.95F), Random.Range(0.6F, 1.0F), 0.9F, floor);
            }
            for (var i = 0; i < 2; i++)
                SpawnSolid(p + Rnd(2F, 0F, 2F), Rnd(12F, 8F, 22F), new Color(0.5F, 0.48F, 0.44F, 0.28F), Random.Range(0.6F, 0.9F), 2.5F, floor - 50F, 9F, true, 1.5F);
        }

        /// <summary>dev8 blood: more drops, a fine mist, and drops that rest on the floor for a while.</summary>
        private void BloodDetail(UVec p, float floor)
        {
            for (var i = 0; i < 6; i++)
                SpawnSolid(p, Rnd(70F, 20F, 120F), new Color(Random.Range(0.35F, 0.55F), 0.02F, 0.02F, 0.95F), Random.Range(1.6F, 3.0F), Random.Range(1.2F, 2.0F), floor, 0, false, 0, true);
            SpawnSolid(p, Rnd(10F, 4F, 14F), new Color(0.45F, 0.03F, 0.03F, 0.35F), 0.5F, 2.5F, floor - 50F, 10F, true, 2F);
        }

        /// <summary>dev8 explosion: rising dark smoke, glowing embers and debris thrown out.</summary>
        private void ExplosionDetail(AnimFx.Explosion e)
        {
            var n = e.Big ? 14 : 5;
            for (var i = 0; i < n; i++)
            {
                var g = Random.Range(0.14F, 0.26F);
                SpawnSolid(e.P + Rnd(14F * e.Size, -4F, 10F), Rnd(30F * e.Size, 18F, 55F), new Color(g, g * 0.95F, g * 0.9F, 0.62F),
                           Random.Range(1.4F, 2.6F) * (e.Big ? 1F : 0.7F), 9F * e.Size, e.Floor - 200F, 16F * e.Size, true, 1.1F);
            }
            for (var i = 0; i < (e.Big ? 16 : 6); i++)
                Spawn(e.P, Rnd(160F * e.Size, 40F, 220F * e.Size), new Color(1F, Random.Range(0.4F, 0.7F), 0.15F, 1F), Random.Range(0.4F, 0.9F), 1.1F, e.Floor, true);
            if (!e.Big) return;
            for (var i = 0; i < 8; i++)
            {
                var g = Random.Range(0.1F, 0.22F);
                SpawnSolid(e.P, Rnd(200F, 80F, 260F), new Color(g, g * 0.9F, g * 0.8F, 1F), Random.Range(1.0F, 1.6F), 1.4F, e.Floor);
            }
        }

        private void UpdateParticles(World w, Fixed frac, ThreeDRenderer.ViewInfo v, float dt)
        {
            if (w != lastWorld) { lastWorld = w; seen.Clear(); particles.Clear(); }
            alive.Clear();
            foreach (var th in w.Thinkers)
            {
                if (!(th is Mobj m) || m.ThinkerState != ThinkerState.Active) continue;
                if (m.Type != MobjType.Puff && m.Type != MobjType.Blood) continue;
                alive.Add(m);
                if (!seen.Add(m)) continue;
                var p = new UVec(m.X.ToFloat(), m.Y.ToFloat(), m.Z.ToFloat());
                var floor = m.FloorZ.ToFloat();
                if (m.Type == MobjType.Puff)
                {
                    for (var i = 0; i < 8; i++)
                        Spawn(p, new UVec(Random.Range(-90F, 90F), Random.Range(-90F, 90F), Random.Range(20F, 150F)),
                              new Color(1F, Random.Range(0.55F, 0.85F), 0.25F, 1F), Random.Range(0.25F, 0.5F), 1.2F, floor, true);
                    if (RekkrSettings.AnimImpacts) ImpactDetail(p, floor);
                }
                else if (RekkrSettings.AnimBlood) BloodDetail(p, floor);   // dev8 (replaces the additive red sparks)
                else
                {
                    for (var i = 0; i < 6; i++)
                        Spawn(p, new UVec(Random.Range(-60F, 60F), Random.Range(-60F, 60F), Random.Range(10F, 110F)),
                              new Color(0.55F, 0.03F, 0.02F, 0.9F), Random.Range(0.5F, 0.9F), 1.8F, floor, false);
                }
            }
            seen.IntersectWith(alive);
            // dev8: explosions found by the animation layer this tic
            var anim = AnimFx.Current;
            if (anim != null && RekkrSettings.AnimImpacts)
            {
                foreach (var e in anim.Explosions) ExplosionDetail(e);
                anim.Explosions.Clear();
            }

            // player splashes on liquid floors, embers above hot liquids near the player
            var pm = w.ConsolePlayer.Mobj;
            if (pm != null)
            {
                var sec = pm.Subsector.Sector;
                var flat = sec.FloorFlat >= 0 && sec.FloorFlat < content.Flats.Count ? content.Flats[sec.FloorFlat] : null;
                var cls = flat != null ? flat.GClass : (byte)0;
                var moving = Mathf.Abs(pm.MomX.ToFloat()) + Mathf.Abs(pm.MomY.ToFloat()) > 2F;
                var onFloor = pm.Z <= pm.FloorZ;
                splashTimer -= dt;
                if (cls != 0 && moving && onFloor && splashTimer <= 0 && cls != GBuffer.ClassHot)
                {
                    splashTimer = 0.22F;
                    var col = cls == GBuffer.ClassWater ? new Color(0.55F, 0.7F, 1F, 0.7F) : new Color(0.45F, 0.45F, 0.3F, 0.7F);
                    var p = new UVec(pm.X.ToFloat(), pm.Y.ToFloat(), pm.FloorZ.ToFloat() + 2F);
                    float ca = Mathf.Cos(v.Angle), sa = Mathf.Sin(v.Angle);
                    p += new UVec(ca, sa, 0) * 20F;
                    for (var i = 0; i < 7; i++)
                        Spawn(p + new UVec(Random.Range(-10F, 10F), Random.Range(-10F, 10F), 0),
                              new UVec(Random.Range(-40F, 40F), Random.Range(-40F, 40F), Random.Range(60F, 130F)), col,
                              Random.Range(0.35F, 0.6F), 1.4F, p.z - 2F, false);
                }
                emberTimer -= dt;
                if (emberTimer <= 0)
                {
                    emberTimer = 0.06F;
                    // sample a random point around the player; embers rise from hot liquid floors
                    var ang = Random.Range(0F, Mathf.PI * 2); var dist = Random.Range(40F, 520F);
                    var px = pm.X.ToFloat() + Mathf.Cos(ang) * dist; var py = pm.Y.ToFloat() + Mathf.Sin(ang) * dist;
                    var ss = Geometry.PointInSubsector(Fixed.FromFloat(px), Fixed.FromFloat(py), w.Map);
                    var s2 = ss.Sector;
                    var f2 = s2.FloorFlat >= 0 && s2.FloorFlat < content.Flats.Count ? content.Flats[s2.FloorFlat] : null;
                    if (f2 != null && f2.GClass == GBuffer.ClassHot)
                    {
                        var fz = s2.FloorHeight.ToFloat();
                        Spawn(new UVec(px, py, fz + 2F), new UVec(Random.Range(-8F, 8F), Random.Range(-8F, 8F), Random.Range(25F, 55F)),
                              new Color(1F, Random.Range(0.35F, 0.6F), 0.1F, 1F), Random.Range(1.2F, 2.4F), 1.3F, fz - 100F, true, true);
                    }
                }
            }

            for (var i = particles.Count - 1; i >= 0; i--)
            {
                var q = particles[i];
                q.Life -= dt;
                if (q.Life <= 0) { particles.RemoveAt(i); continue; }
                if (q.Rest) { particles[i] = q; continue; }   // dev8: a drop lying on the floor
                if (!q.Rise) q.V.z -= 420F * dt;
                else q.V.x += Mathf.Sin(Time.time * 3F + i) * 10F * dt;
                if (q.Drag > 0) q.V *= Mathf.Max(0F, 1F - q.Drag * dt);
                q.Size += q.Grow * dt;
                q.P += q.V * dt;
                if (q.P.z < q.Floor)
                {
                    q.P.z = q.Floor;
                    if (q.Stick) { q.V = UVec.zero; q.Rest = true; q.Size = Mathf.Min(q.Size * 1.3F, 3F); q.Stick = false; }   // rests on the floor
                    else { q.V *= 0.3F; q.V.z = -q.V.z * 0.35F; }
                }
                particles[i] = q;
            }
        }

        private void DrawParticles(ThreeDRenderer.ViewInfo v)
        {
            if (particles.Count == 0 && drops.Count == 0) return;
            float ca = Mathf.Cos(v.Angle), sa = Mathf.Sin(v.Angle);
            float cx = v.WindowX + v.CenterX, cy = v.WindowY + v.CenterY;
            var prev = RenderTexture.active;
            RenderTexture.active = world;
            GL.PushMatrix();
            GL.LoadOrtho();
            mat.SetPass(4);
            GL.Begin(GL.QUADS);
            var W = (float)rtH; var H = (float)rtW;   // frame columns, rows
            var solids = 0;
            foreach (var q in particles)
            {
                if (q.Solid) { solids++; continue; }
                var dx = q.P.x - v.X; var dy = q.P.y - v.Y;
                var zv = dx * ca + dy * sa;
                if (zv < 6F) continue;
                var xv = dx * sa - dy * ca;
                var yv = q.P.z - v.Z;
                var fx = cx + xv / zv * v.Projection;
                var fy = cy - yv / zv * v.Projection;
                var r = Mathf.Max(0.8F, q.Size / zv * v.Projection) * (q.Glow ? 1.6F : 1F);
                var fade = Mathf.Clamp01(q.Life / (q.MaxLife * 0.4F));
                var col = q.C; col.a *= fade * (q.Glow ? 1.6F : 1F);
                GL.Color(col);
                // frame (x, y) -> RT uv (u = y / H, v = x / W)
                Corner(fy - r, fx - r, H, W, zv, 0, 0);
                Corner(fy + r, fx - r, H, W, zv, 1, 0);
                Corner(fy + r, fx + r, H, W, zv, 1, 1);
                Corner(fy - r, fx + r, H, W, zv, 0, 1);
            }
            DrawDrops(v, cx, cy, W, H, ca, sa);
            GL.End();
            if (solids > 0)
            {
                // dev8: smoke / chips / blood alpha-blended (pass 6; pass 4 is additive light)
                mat.SetPass(6);
                GL.Begin(GL.QUADS);
                foreach (var q in particles)
                {
                    if (!q.Solid) continue;
                    var dx = q.P.x - v.X; var dy = q.P.y - v.Y;
                    var zv = dx * ca + dy * sa;
                    if (zv < 6F) continue;
                    var xv = dx * sa - dy * ca;
                    var yv = q.P.z - v.Z;
                    var fx = cx + xv / zv * v.Projection;
                    var fy = cy - yv / zv * v.Projection;
                    var r = Mathf.Max(0.8F, q.Size / zv * v.Projection);
                    var col = q.C; col.a *= Mathf.Clamp01(q.Life / (q.MaxLife * 0.4F));
                    GL.Color(col);
                    Corner(fy - r, fx - r, H, W, zv, 0, 0);
                    Corner(fy + r, fx - r, H, W, zv, 1, 0);
                    Corner(fy + r, fx + r, H, W, zv, 1, 1);
                    Corner(fy - r, fx + r, H, W, zv, 0, 1);
                }
                GL.End();
            }
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        // ---------------------------------------------------------------- dev7: 3D weather
        // Owner: "the weather is bad". dev5 drew rain/snow as a screen-space pattern on every pixel while the
        // player stood under the sky: it covered indoor walls, vanished under any roof and did not move with the
        // world. Now drops are world-space particles spawned only above sectors open to the sky, around the
        // camera, falling to that sector's floor, depth-tested against the G-buffer (walls hide them, windows
        // show them), with small splashes. Kinds: 1 rain (streaks), 2 snow, 3 embers (rise), 4 dust motes.
        private readonly List<Drop> drops = new List<Drop>(1200);
        private World dropWorld;
        private int dropType;

        private static int DropTarget(int type) => type == 1 ? 900 : type == 2 ? 700 : type == 3 ? 110 : type == 4 ? 140 : 0;

        private bool SpawnDrop(World w, ThreeDRenderer.ViewInfo v, int type, bool anyHeight, out Drop d)
        {
            d = default;
            var r = type == 1 ? 700F : type == 2 ? 600F : 900F;
            // 80 % in front of the camera (what the player sees), 20 % all around (turning)
            var ang = Random.value < 0.8F ? v.Angle + Random.Range(-1.3F, 1.3F) : Random.Range(0F, Mathf.PI * 2F);
            var dist = Mathf.Sqrt(Random.value) * r + 12F;
            var x = v.X + Mathf.Cos(ang) * dist; var y = v.Y + Mathf.Sin(ang) * dist;
            var sec = Geometry.PointInSubsector(Fixed.FromFloat(x), Fixed.FromFloat(y), w.Map).Sector;
            if (sec.CeilingFlat != content.Flats.SkyFlatNumber) return false;   // under a roof: no weather
            var floor = sec.FloorHeight.ToFloat();
            var top = Mathf.Max(v.Z + 260F, floor + 220F);
            float z;
            if (type == 3) z = anyHeight ? Random.Range(floor, floor + 180F) : floor + 2F;
            else if (type == 4) z = Random.Range(floor + 8F, floor + 150F);
            else z = anyHeight ? Random.Range(floor, top) : top + Random.Range(0F, 60F);
            UVec vel;
            var wind = new UVec(windX, windY, 0);
            if (type == 1) vel = wind * 60F + new UVec(0, 0, -Random.Range(820F, 980F));
            else if (type == 2) vel = wind * 25F + new UVec(Random.Range(-10F, 10F), Random.Range(-10F, 10F), -Random.Range(55F, 85F));
            else if (type == 3) vel = new UVec(Random.Range(-8F, 8F), Random.Range(-8F, 8F), Random.Range(22F, 50F));
            else vel = new UVec(Random.Range(-6F, 6F), Random.Range(-6F, 6F), Random.Range(-3F, 3F));
            d = new Drop { P = new UVec(x, y, z), V = vel, Floor = floor, Life = type >= 3 ? Random.Range(2.5F, 5F) : 99F, Phase = Random.value * 6.3F, Kind = (byte)type };
            return true;
        }

        private float windX, windY;

        private void UpdateWeather(World w, ThreeDRenderer.ViewInfo v, int type, Atmos a, float dt)
        {
            if (w != dropWorld || type != dropType) { dropWorld = w; dropType = type; drops.Clear(); }
            windX = a.Wind.x; windY = a.Wind.y;
            var target = DropTarget(type);
            if (target == 0) { drops.Clear(); return; }
            var rMax = type == 1 ? 760F : type == 2 ? 660F : 960F;
            for (var i = drops.Count - 1; i >= 0; i--)
            {
                var d = drops[i];
                d.P += d.V * dt;
                d.Life -= dt;
                if (d.Kind == 2) d.P.x += Mathf.Sin(Time.time * 1.3F + d.Phase) * 12F * dt;
                if (d.Kind == 4) { d.P.x += Mathf.Sin(Time.time * 0.4F + d.Phase) * 5F * dt; d.P.y += Mathf.Cos(Time.time * 0.33F + d.Phase) * 5F * dt; }
                var dx = d.P.x - v.X; var dy = d.P.y - v.Y;
                var gone = d.Life <= 0 || dx * dx + dy * dy > rMax * rMax || d.P.z > d.Floor + 900F;
                if (!gone && d.P.z <= d.Floor)
                {
                    gone = true;
                    // rain splash: a few short droplets (visual only; uses the particle list)
                    if (d.Kind == 1 && Random.value < 0.35F && particles.Count < 480)
                        for (var k = 0; k < 2; k++)
                            Spawn(new UVec(d.P.x, d.P.y, d.Floor + 1F), new UVec(Random.Range(-30F, 30F), Random.Range(-30F, 30F), Random.Range(40F, 80F)),
                                  new Color(0.6F, 0.68F, 0.8F, 0.45F), 0.18F, 0.9F, d.Floor, false);
                }
                if (gone) drops.RemoveAt(i); else drops[i] = d;
            }
            // refill: the first frames fill the whole column (anyHeight), later drops start at the top
            var budget = drops.Count < target / 3 ? 400 : 90;
            var tries = budget * 3;
            while (drops.Count < target && budget > 0 && tries-- > 0)
            {
                if (SpawnDrop(w, v, type, drops.Count < target / 2, out var d)) { drops.Add(d); budget--; }
            }
        }

        private void DrawDrops(ThreeDRenderer.ViewInfo v, float cx, float cy, float W, float H, float ca, float sa)
        {
            if (drops.Count == 0) return;
            var t = Time.time;
            foreach (var d in drops)
            {
                var dx = d.P.x - v.X; var dy = d.P.y - v.Y;
                var zv = dx * ca + dy * sa;
                if (zv < 8F) continue;
                var xv = dx * sa - dy * ca;
                var fx = cx + xv / zv * v.Projection;
                var fy = cy - (d.P.z - v.Z) / zv * v.Projection;
                if (fx < -20 || fx > W + 20 || fy < -60 || fy > H + 20) continue;
                var near = Mathf.Clamp01(1.3F - zv / 700F);
                Color col;
                if (d.Kind == 1)
                {
                    // streak: from the drop up along its velocity (motion blur of ~1/40 s)
                    var tx = d.P.x - d.V.x * 0.025F - v.X; var ty = d.P.y - d.V.y * 0.025F - v.Y;
                    var tz = d.P.z - d.V.z * 0.025F;
                    var zt = tx * ca + ty * sa; if (zt < 8F) continue;
                    var gx = cx + (tx * sa - ty * ca) / zt * v.Projection;
                    var gy = cy - (tz - v.Z) / zt * v.Projection;
                    col = new Color(0.62F, 0.68F, 0.78F, 0.16F + 0.18F * near);
                    GL.Color(col);
                    Segment(fx, fy, gx, gy, Mathf.Max(0.55F, 0.9F / zv * v.Projection), H, W, zv);
                    continue;
                }
                var r = d.Kind == 2 ? Mathf.Max(0.7F, 1.6F / zv * v.Projection) : Mathf.Max(0.6F, 1.1F / zv * v.Projection);
                if (d.Kind == 2) col = new Color(0.9F, 0.93F, 1F, 0.55F + 0.3F * near);
                else if (d.Kind == 3) col = new Color(1F, 0.42F, 0.1F, (0.6F + 0.4F * Mathf.Sin(t * 9F + d.Phase * 5F)) * Mathf.Clamp01(d.Life));
                else col = new Color(sunCol.r * 0.5F, sunCol.g * 0.5F, sunCol.b * 0.45F, 0.3F * Mathf.Clamp01(d.Life));
                GL.Color(col);
                Corner(fy - r, fx - r, H, W, zv, 0, 0);
                Corner(fy + r, fx - r, H, W, zv, 1, 0);
                Corner(fy + r, fx + r, H, W, zv, 1, 1);
                Corner(fy - r, fx + r, H, W, zv, 0, 1);
            }
        }

        private Color sunCol = Color.white;

        /// <summary>A thin quad from frame point (ax, ay) to (bx, by), half-width w (frame px).</summary>
        private static void Segment(float ax, float ay, float bx, float by, float w, float H, float W, float z)
        {
            var dx = bx - ax; var dy = by - ay;
            var len = Mathf.Max(0.001F, Mathf.Sqrt(dx * dx + dy * dy));
            var nx = -dy / len * w; var ny = dx / len * w;
            // corners: uv.y across (0..1), uv.z along (0..1); the particle shader fades both ends
            Corner(ay + ny, ax + nx, H, W, z, 0, 0);
            Corner(ay - ny, ax - nx, H, W, z, 1, 0);
            Corner(by - ny, bx - nx, H, W, z, 1, 1);
            Corner(by + ny, bx + nx, H, W, z, 0, 1);
        }

        private static void Corner(float fy, float fx, float H, float W, float z, float cu, float cv)
        {
            GL.MultiTexCoord(0, new UVec(z, cu, cv));
            GL.Vertex3(fy / H, fx / W, 0);
        }
    }
}
