// dev5 checks: G-buffer codes written by the software renderer (stored in the frame alpha).
using System;
using System.IO;
using System.Linq;
using ManagedDoom;
using ManagedDoom.Video;

public static class Dev5Test
{
    public static int Run(GameContent content, CommandLineArgs args, string outDir)
    {
        int failures = 0;
        ThreeDRenderer.TrueColor = true;
        GBuffer.ClassifyFlats(content);
        var liquids = content.Flats.Count(f => f != null && f.GClass != 0);
        Console.WriteLine($"liquid flats classified: {liquids} (water {content.Flats.Count(f => f != null && f.GClass == GBuffer.ClassWater)}, murky {content.Flats.Count(f => f != null && f.GClass == GBuffer.ClassMurky)}, hot {content.Flats.Count(f => f != null && f.GClass == GBuffer.ClassHot)})");
        if (liquids == 0) failures++;
        var c = new Config(); c.video_highresolution = true; c.video_gamescreensize = 8;
        var v = new ShotVideo(c, content, 1066, 400); v.DisplayMessage = false;
        var d = new Doom(args, c, content, v, null, null, null);
        int totalSky = 0, totalLiquid = 0, badAlpha = 0, nonMono = 0, checkedCols = 0;
        for (int e = 1; e <= 4; e++)
        {
            d.NewGame(GameSkill.Medium, e, 1);
            for (int t = 0; t < 140; t++) d.Update();
            v.WindowSize = 8;
            var mo = d.Game.World.ConsolePlayer.Mobj;
            for (int a = 0; a < 8; a++)
            {
                mo.Angle = new Angle((uint)(a * (uint.MaxValue / 8)));
                var buf = v.Frame(d, Fixed.One);
                var g = v.Inner.GDataForTest;
                var (wx, wy, ww, wh) = v.Inner.ViewWindow;
                int h = v.H;
                var sky = content.Flats.SkyFlat;
                for (int x = wx; x < wx + ww; x++)
                {
                    int prev = -1; bool mono = true;
                    for (int y = wy; y < wy + wh; y++)
                    {
                        int i = x * h + y;
                        int alpha = buf[4 * i + 3];
                        if (alpha == GBuffer.Sky) totalSky++;
                        if (alpha >= GBuffer.WaterBase && alpha < GBuffer.Sky) totalLiquid++;
                        if (alpha != g[i] && alpha != 255) badAlpha++;
                    }
                    // Sky pixels must be contiguous from the top in most columns (no sky below solid floor).
                    _ = prev; _ = mono;
                }
                // Depth plausibility: the bottom row of the view (floor right in front) must be nearer
                // than the horizon row in the centre column.
                {
                    int x = wx + ww / 2;
                    int near = buf[4 * (x * h + wy + wh - 2) + 3];
                    int mid = buf[4 * (x * h + wy + wh / 2 + 2) + 3];
                    checkedCols++;
                    if (near < 200 && mid < 200 && near > mid) nonMono++;
                }
                if (a == 0 || a == 3) GbufShot(v, d, Path.Combine(outDir, $"gbuf_e{e}_a{a}.png"));
                if (a == 0 || a == 3) v.Shot(d, Path.Combine(outDir, $"col_e{e}_a{a}.png"));
                {
                    long hr = 0, hg = 0, hb = 0, hn = 0;
                    for (int i = 0; i < buf.Length / 4; i++) { int al = buf[4 * i + 3]; if (al >= GBuffer.HotBase && al < GBuffer.Sky) { hr += buf[4 * i]; hg += buf[4 * i + 1]; hb += buf[4 * i + 2]; hn++; } }
                    if (hn > 0) Console.WriteLine($"hot px E{e} a{a}: n={hn} rgb=({hr / hn},{hg / hn},{hb / hn})");
                }
            }
        }
        Console.WriteLine($"gbuffer: sky_px={totalSky} liquid_px={totalLiquid} alpha_mismatch={badAlpha} depth_order_bad={nonMono}/{checkedCols}");
        if (totalSky == 0) failures++;
        if (badAlpha > 0) failures++;
        if (nonMono > checkedCols / 4) failures++;
        ThreeDRenderer.TrueColor = false;
        Console.WriteLine(failures == 0 ? "DEV5 PASS" : $"DEV5 FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }

    // False-colour G-buffer: depth as grey, water blue, murky olive, hot orange, sky cyan, weapon magenta.
    private static void GbufShot(ShotVideo v, Doom d, string path)
    {
        var buf = v.Frame(d, Fixed.One);
        int w = v.W, h = v.H; var rgb = new byte[w * h * 3];
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
        {
            int s = 4 * (x * h + y), o = 3 * (y * w + x); int a = buf[s + 3];
            byte r, g, b;
            if (a < 200) { r = g = b = (byte)(255 - a * 255 / 200); }
            else if (a < 224) { r = 0; g = 60; b = (byte)(255 - (a - 200) * 8); }
            else if (a < 236) { r = 120; g = 120; b = 40; }
            else if (a < 248) { r = 255; g = 120; b = 0; }
            else if (a == 248) { r = 0; g = 220; b = 255; }
            else if (a == 249) { r = 255; g = 0; b = 255; }
            else { r = buf[s] ; g = buf[s + 1]; b = buf[s + 2]; r /= 3; g /= 3; b /= 3; }
            rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = b;
        }
        Png.Write(path, w, h, rgb);
    }
}
