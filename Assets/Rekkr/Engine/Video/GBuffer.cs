// my-rekkr dev5 — G-buffer codes written by the software renderer next to each 3D pixel and stored in
// the alpha channel of the frame texture, so GPU shaders know what a pixel is (sky, liquid, weapon)
// and how far away it is. See docs/DEV5.md §2 for the table.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;

namespace ManagedDoom.Video
{
    public static class GBuffer
    {
        public const byte WaterBase = 200, WaterLevels = 24;   // 200..223
        public const byte MurkyBase = 224, MurkyLevels = 12;   // 224..235
        public const byte HotBase = 236, HotLevels = 12;       // 236..247
        public const byte Sky = 248;
        public const byte Weapon = 249;
        public const byte GpuDark = 251;   // dev6: GPU pixel under a dark translucent 2D panel (fullscreen HUD)
        public const byte Gpu = 250;       // dev6: pixel to be filled by the Remaster GPU renderer (never leaves the compositor)
        public const byte None = 255;
        public const int SolidLevels = 200;                    // 0..199
        public const float DepthK = 19.9F;                     // code = log2(z/8) * K
        public const float DepthMin = 8F;

        // Flat classes (Flat.GClass).
        public const byte ClassNone = 0, ClassWater = 1, ClassMurky = 2, ClassHot = 3;

        /// <summary>Depth code 0..199 for a view-space distance z (map units).</summary>
        public static byte FromZ(float z)
        {
            if (z <= DepthMin) return 0;
            var c = (int)((MathF.Log(z * (1F / DepthMin)) * 1.442695F) * DepthK);
            return (byte)(c > SolidLevels - 1 ? SolidLevels - 1 : c);
        }

        /// <summary>Depth code from a Doom projection scale (scale = projection / z).</summary>
        public static byte FromScale(float projPx, int scaleData)
        {
            if (scaleData <= 0) return SolidLevels - 1;
            return FromZ(projPx * 65536F / scaleData);
        }

        /// <summary>Code of a floor/ceiling pixel at distance <paramref name="distData"/> (Fixed) of a flat class.</summary>
        public static byte Plane(int distData, byte cls)
        {
            var d = FromZ(distData / 65536F);
            switch (cls)
            {
                case ClassWater: return (byte)(WaterBase + d * WaterLevels / SolidLevels);
                case ClassMurky: return (byte)(MurkyBase + d * MurkyLevels / SolidLevels);
                case ClassHot: return (byte)(HotBase + d * HotLevels / SolidLevels);
                default: return d;
            }
        }

        public static float ZFromCode(int code)
        {
            if (code < WaterBase) return DepthMin * MathF.Pow(2F, code / DepthK);
            if (code < MurkyBase) return ZFromCode((code - WaterBase) * SolidLevels / WaterLevels);
            if (code < HotBase) return ZFromCode((code - MurkyBase) * SolidLevels / MurkyLevels);
            if (code < Sky) return ZFromCode((code - HotBase) * SolidLevels / HotLevels);
            return float.PositiveInfinity;
        }

        private static object classified;

        /// <summary>Classifies every animated flat by the average colour of its whole animation cycle
        /// (REKKR re-uses vanilla names for other colours, so names are not used): blue → water,
        /// red/orange or bright green → hot/toxic (glows), dull → murky liquid.</summary>
        public static void ClassifyFlats(GameContent content)
        {
            if (ReferenceEquals(classified, content)) return;
            lock (typeof(GBuffer))
            {
                if (ReferenceEquals(classified, content)) return;
                var flats = content.Flats;
                var pal = content.Wad.ReadLump("PLAYPAL");   // raw palette 0 (RGB triplets)
                foreach (var f in flats) if (f != null) f.GClass = ClassNone;
                foreach (var a in content.Animation.Animations)
                {
                    if (a.IsTexture) continue;
                    double r = 0, g = 0, b = 0; var n = 0;
                    for (var i = a.BasePic; i <= a.PicNum && i < flats.Count; i++)
                    {
                        if (flats[i] == null) continue;
                        foreach (var t in flats[i].Data)
                        {
                            r += pal[3 * t]; g += pal[3 * t + 1]; b += pal[3 * t + 2]; n++;
                        }
                    }
                    if (n == 0) continue;
                    r /= n; g /= n; b /= n;
                    byte cls;
                    if (b > r && b > g) cls = ClassWater;
                    else if (r > 1.5 * g || (g > 1.3 * r && g > 110)) cls = ClassHot;
                    else if (g > 1.3 * r && g > 1.3 * b) cls = ClassHot;   // saturated green slime
                    else cls = ClassMurky;
                    for (var i = a.BasePic; i <= a.PicNum && i < flats.Count; i++) if (flats[i] != null) flats[i].GClass = cls;
                    Console.WriteLine($"[REKKR] liquid {flats[a.BasePic].Name}..{flats[Math.Min(a.PicNum, flats.Count - 1)].Name} rgb=({r:F0},{g:F0},{b:F0}) class={cls}");
                }
                classified = content;
            }
        }
    }
}
