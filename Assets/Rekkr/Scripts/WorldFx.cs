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
                        Fog = new Color(0.3F, 0.07F, 0.04F), FogMul = 1.3F, AutoWeather = 3, DriftBamPerSec = 3.5e6F },
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
        public Texture Process(UnityVideo video, Doom doom, bool inLevel, Fixed frameFrac)
        {
            var src = video.FrameTexture;   // dev6: Remaster composite when active
            if (!inLevel || !RekkrSettings.SmoothLighting) { ThreeDRenderer.SkyDriftBam = 0; return src; }
            var world3 = doom.Game.World;
            var ep = Mathf.Clamp(doom.Game.Options.Episode, 1, 4) - 1;
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
            var fogD = RekkrSettings.Fog == 0 ? 0F : (RekkrSettings.Fog == 1 ? 1F / 2600F : 1F / 1300F) * a.FogMul;
            mat.SetVector("_FogCol", new Vector4(a.Fog.r, a.Fog.g, a.Fog.b, fogD));
            mat.SetVector("_Cloud", new Vector4(a.Cloud.r, a.Cloud.g, a.Cloud.b, a.Coverage));
            mat.SetVector("_CloudP", new Vector4(a.Wind.x, a.Wind.y, a.CloudSpeed, RekkrSettings.SkyFx ? 1 : 0));

            // weather (outdoors only), lightning in rain
            var wType = RekkrSettings.Weather == 3 ? 0 : RekkrSettings.Weather == 0 ? a.AutoWeather : RekkrSettings.Weather;
            outdoor = Mathf.MoveTowards(outdoor, v.SkyCeiling ? 1F : 0F, dt * 2F);
            UpdateLightning(wType == 1, t, dt);
            var inten = wType == 0 ? 0F : wType == 1 ? 1F : wType == 2 ? 0.9F : wType == 3 ? 0.9F : 0.8F;
            mat.SetVector("_Weather", new Vector4(wType, inten * outdoor, lightning * outdoor, wType == 1 ? outdoor : 0F));

            mat.SetVector("_Fx", new Vector4(RekkrSettings.WaterFx ? 1 : 0, RekkrSettings.AO ? 0.55F : 0F, RekkrSettings.DynLights ? 1 : 0, RekkrSettings.WaterFx ? 1 : 0));
            mat.SetVector("_Fx2", new Vector4(fogD > 0 ? 1 : 0, RekkrSettings.DoF ? 1 : 0, 700F, DebugView ? 1 : 0));

            // lights
            if (RekkrSettings.DynLights) GatherLights(world3, frameFrac, v); else LightsLastFrame = 0;
            mat.SetFloat("_LightCount", LightsLastFrame);
            mat.SetVectorArray("_LightPos", lightPos);
            mat.SetVectorArray("_LightCol", lightCol);

            // passes
            mat.SetTexture("_GTex", src);
            Graphics.Blit(src, world, mat, 0);
            if (RekkrSettings.Particles) { UpdateParticles(world3, frameFrac, v, dt); DrawParticles(v); }
            else particles.Clear();
            var raysOn = sunScreen.z > 0;
            if (raysOn) Graphics.Blit(world, rays, mat, 1);
            if (RekkrSettings.DoF)
            {
                Graphics.Blit(world, blurA, mat, 2);
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
        private readonly Vector4[] lightPos = new Vector4[8];
        private readonly Vector4[] lightCol = new Vector4[8];
        private readonly List<(float d, Vector4 p, Vector4 c)> lightCand = new List<(float, Vector4, Vector4)>(64);
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

        private void GatherLights(World w, Fixed frac, ThreeDRenderer.ViewInfo v)
        {
            lightCand.Clear();
            float ca = Mathf.Cos(v.Angle), sa = Mathf.Sin(v.Angle);
            var player = w.ConsolePlayer;
            foreach (var th in w.Thinkers)
            {
                if (!(th is Mobj m) || m.ThinkerState != ThinkerState.Active) continue;
                if (m == player.Mobj || (m.Frame & 0x8000) == 0) continue;
                var x = m.GetInterpolatedX(frac).ToFloat(); var y = m.GetInterpolatedY(frac).ToFloat();
                var dx = x - v.X; var dy = y - v.Y;
                var zv = dx * ca + dy * sa;
                var xv = dx * sa - dy * ca;
                var d2 = dx * dx + dy * dy;
                if (d2 > 1500F * 1500F) continue;
                var h = m.Height.ToFloat();
                var radius = Mathf.Clamp(h * 4.5F, 150F, 320F);
                if (zv < -radius || Mathf.Abs(xv) > zv * 2.2F + radius) continue;
                var z = m.GetInterpolatedZ(frac).ToFloat() + h * 0.6F;
                var col = SpriteLightColor(m.Sprite, m.Frame);
                var k = m.Type == MobjType.Puff ? 0.35F : 1F;
                lightCand.Add((d2, new Vector4(xv, z - v.Z, zv, radius), new Vector4(col.r * k, col.g * k, col.b * k, 0)));
            }
            if (player.ExtraLight > 0 && player.Mobj != null)
            {
                var r = 200F + 90F * player.ExtraLight;
                lightCand.Add((0, new Vector4(0, -8, 24, r), new Vector4(1F, 0.78F, 0.45F, 0)));
            }
            lightCand.Sort((p, q) => p.d.CompareTo(q.d));
            var n = Mathf.Min(8, lightCand.Count);
            for (var i = 0; i < 8; i++)
            {
                lightPos[i] = i < n ? lightCand[i].p : Vector4.zero;
                lightCol[i] = i < n ? lightCand[i].c : Vector4.zero;
            }
            LightsLastFrame = n;
        }

        // ---------------------------------------------------------------- particles (visual only)
        private struct Particle { public UVec P, V; public Color C; public float Life, MaxLife, Size, Floor; public bool Glow, Rise; }
        private readonly List<Particle> particles = new List<Particle>(512);
        private readonly HashSet<Mobj> seen = new HashSet<Mobj>();
        private readonly HashSet<Mobj> alive = new HashSet<Mobj>();
        private World lastWorld;
        private float splashTimer, emberTimer;

        private void Spawn(UVec p, UVec vel, Color c, float life, float size, float floor, bool glow, bool rise = false)
        {
            if (particles.Count >= 512) return;
            particles.Add(new Particle { P = p, V = vel, C = c, Life = life, MaxLife = life, Size = size, Floor = floor, Glow = glow, Rise = rise });
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
                }
                else
                {
                    for (var i = 0; i < 6; i++)
                        Spawn(p, new UVec(Random.Range(-60F, 60F), Random.Range(-60F, 60F), Random.Range(10F, 110F)),
                              new Color(0.55F, 0.03F, 0.02F, 0.9F), Random.Range(0.5F, 0.9F), 1.8F, floor, false);
                }
            }
            seen.IntersectWith(alive);

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
                if (!q.Rise) q.V.z -= 420F * dt;
                else q.V.x += Mathf.Sin(Time.time * 3F + i) * 10F * dt;
                q.P += q.V * dt;
                if (q.P.z < q.Floor) { q.P.z = q.Floor; q.V *= 0.3F; q.V.z = -q.V.z * 0.35F; }
                particles[i] = q;
            }
        }

        private void DrawParticles(ThreeDRenderer.ViewInfo v)
        {
            if (particles.Count == 0) return;
            float ca = Mathf.Cos(v.Angle), sa = Mathf.Sin(v.Angle);
            float cx = v.WindowX + v.CenterX, cy = v.WindowY + v.CenterY;
            var prev = RenderTexture.active;
            RenderTexture.active = world;
            GL.PushMatrix();
            GL.LoadOrtho();
            mat.SetPass(4);
            GL.Begin(GL.QUADS);
            var W = (float)rtH; var H = (float)rtW;   // frame columns, rows
            foreach (var q in particles)
            {
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
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        private static void Corner(float fy, float fx, float H, float W, float z, float cu, float cv)
        {
            GL.MultiTexCoord(0, new UVec(z, cu, cv));
            GL.Vertex3(fy / H, fx / W, 0);
        }
    }
}
