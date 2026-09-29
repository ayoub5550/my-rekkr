// dev3 stage 1: automatic hall-of-mirrors / unwritten-pixel scan over all 36 maps.
// Each view is rendered twice, after filling the frame with index 0 and with index 255.
// A pixel that differs between both renders was never written by the renderer = HOM/gap.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ManagedDoom;
using ManagedDoom.Video;

public static class HomScan
{
    public const int SamplesPerMap = 40;
    public const int Angles = 8;

    public static int Run(GameContent content, CommandLineArgs args, int[] widths, string outDir)
    {
        var total = 0; var views = 0; var bad = 0;
        var report = new List<string>();
        foreach (var wide in widths)
        {
            var c = new Config(); c.video_highresolution = true; c.video_gamescreensize = 8;
            var v = new ShotVideo(c, content, wide);
            v.DisplayMessage = false;
            var d = new Doom(args, c, content, v, null, null, null);
            for (int e = 1; e <= 4; e++)
                for (int m = 1; m <= 9; m++)
                {
                    d.NewGame(GameSkill.Medium, e, m);
                    for (int t = 0; t < 140; t++) d.Update();
                    v.WindowSize = 8;
                    var world = d.Game.World;
                    var player = world.ConsolePlayer;
                    var mo = player.Mobj;
                    var map = world.Map;
                    var rng = new Random(e * 100 + m);
                    var points = new List<(Fixed x, Fixed y, string tag)> { (mo.X, mo.Y, "start") };
                    var subs = map.Subsectors;
                    for (int i = 0; i < SamplesPerMap; i++)
                    {
                        var ss = subs[rng.Next(subs.Length)];
                        long sx = 0, sy = 0;
                        for (int k = 0; k < ss.SegCount; k++) { var sg = map.Segs[ss.FirstSeg + k]; sx += sg.Vertex1.X.Data; sy += sg.Vertex1.Y.Data; }
                        if (ss.SegCount == 0) continue;
                        // The seg-vertex average can fall outside the subsector (partition edges have no segs).
                        if (Geometry.PointInSubsector(new Fixed((int)(sx / ss.SegCount)), new Fixed((int)(sy / ss.SegCount)), map) != ss) continue;
                        points.Add((new Fixed((int)(sx / ss.SegCount)), new Fixed((int)(sy / ss.SegCount)), $"ss{Array.IndexOf(subs, ss)}"));
                    }
                    int mapBad = 0, mapWorst = 0;
                    foreach (var (px, py, tag) in points)
                    {
                        if (tag != "start" && !world.ThingMovement.CheckPosition(mo, px, py)) continue; // not standable
                        world.ThingMovement.UnsetThingPosition(mo);
                        mo.X = px; mo.Y = py;
                        world.ThingMovement.SetThingPosition(mo);
                        var sec = mo.Subsector.Sector;
                        if (sec.CeilingHeight - sec.FloorHeight < Fixed.FromInt(48)) continue; // closed door / lift
                        mo.Z = sec.FloorHeight;
                        var vz = mo.Z + Player.NormalViewHeight;
                        if (vz > sec.CeilingHeight - Fixed.FromInt(4)) vz = sec.CeilingHeight - Fixed.FromInt(4);
                        player.ViewZ = vz;
                        for (int a = 0; a < Angles; a++)
                        {
                            mo.Angle = new Angle((uint)(a * (uint.MaxValue / Angles)));
                            var data = v.Inner.ScreenDataForTest;
                            Array.Fill(data, (byte)0);
                            v.Frame(d, Fixed.One);
                            var first = (byte[])data.Clone();
                            Array.Fill(data, (byte)255);
                            v.Frame(d, Fixed.One);
                            int diff = 0;
                            for (int i = 0; i < data.Length; i++) if (data[i] != first[i]) diff++;
                            views++;
                            if (diff > 0)
                            {
                                bad++; mapBad++;
                                var line = $"HOM E{e}M{m} w={wide} at=({px.ToIntFloor()},{py.ToIntFloor()}) {tag} angle={a * 360 / Angles} sector={Array.IndexOf(map.Sectors, sec)} pixels={diff}";
                                report.Add(line);
                                if (diff > mapWorst)
                                {
                                    mapWorst = diff;
                                    SaveMask(v, first, data, Path.Combine(outDir, $"hom_E{e}M{m}_w{wide}.png"));
                                }
                            }
                        }
                    }
                    total++;
                    Console.WriteLine($"hom E{e}M{m} w={wide}: views={points.Count * Angles} bad={mapBad} worst={mapWorst}");
                }
        }
        File.WriteAllLines(Path.Combine(outDir, "hom_report.txt"), report);
        Console.WriteLine($"hom: maps={total} views={views} bad_views={bad} report={Path.Combine(outDir, "hom_report.txt")}");
        return bad == 0 ? 0 : 1;
    }

    // Writes the frame with unwritten pixels in magenta.
    private static void SaveMask(ShotVideo v, byte[] a, byte[] b, string path)
    {
        int w = v.W, h = v.H;
        var rgb = new byte[w * h * 3];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int s = x * h + y, dI = 3 * (y * w + x);
                if (a[s] != b[s]) { rgb[dI] = 255; rgb[dI + 1] = 0; rgb[dI + 2] = 255; }
                else { var g = a[s]; rgb[dI] = g; rgb[dI + 1] = g; rgb[dI + 2] = g; }
            }
        Png.Write(path, w, h, rgb);
    }
}

// Lists wall textures used in the maps that have columns without any patch (drawn as nothing = HOM).
public static class TexHoles
{
    public static int Run(GameContent content, CommandLineArgs args)
    {
        var tex = content.Textures;
        var holes = new Dictionary<int, int>();
        for (int i = 0; i < tex.Count; i++)
        {
            var cols = tex[i].Composite.Columns; int n = 0;
            foreach (var c in cols) if (c.Length == 0) n++;
            if (n > 0) holes[i] = n;
        }
        var used = new Dictionary<int, SortedSet<string>>();
        for (int e = 1; e <= 4; e++)
            for (int m = 1; m <= 9; m++)
            {
                var o = new GameOptions(); o.Episode = e; o.Map = m; o.Players[0].InGame = true;
                var g = new DoomGame(content, o); g.DeferedInitNew();
                g.Update(new TicCmd[] { new TicCmd(), new TicCmd(), new TicCmd(), new TicCmd() });
                // Missing upper/lower textures ("-") where the heights need one (vanilla draws nothing = HOM).
                int missing = 0; var sky = g.World.Map.SkyFlatNumber;
                foreach (var line in g.World.Map.Lines)
                {
                    if (line.BackSide == null) { if (line.FrontSide.MiddleTexture == 0) missing++; continue; }
                    foreach (var (side, me, other) in new[] { (line.FrontSide, line.FrontSector, line.BackSector), (line.BackSide, line.BackSector, line.FrontSector) })
                    {
                        if (me.CeilingHeight > other.CeilingHeight && side.TopTexture == 0 && !(me.CeilingFlat == sky && other.CeilingFlat == sky)) missing++;
                        if (me.FloorHeight < other.FloorHeight && side.BottomTexture == 0) missing++;
                    }
                }
                if (missing > 0) Console.WriteLine($"missing-texture E{e}M{m}: {missing} wall parts need a texture but have '-'");
                // Only solid uses matter: middle of one-sided lines, upper/lower of two-sided lines.
                // Masked middles of two-sided lines are meant to be see-through.
                foreach (var line in g.World.Map.Lines)
                    foreach (var side in new[] { line.FrontSide, line.BackSide })
                    {
                        if (side == null) continue;
                        var solid = line.BackSide == null ? new[] { side.MiddleTexture } : new[] { side.TopTexture, side.BottomTexture };
                        foreach (var t in solid)
                            if (t > 0 && holes.ContainsKey(t)) { if (!used.ContainsKey(t)) used[t] = new SortedSet<string>(); used[t].Add($"E{e}M{m}"); }
                    }
            }
        foreach (var kv in used)
            Console.WriteLine($"texhole {tex[kv.Key].Name} {tex[kv.Key].Width}x{tex[kv.Key].Height} empty_columns={holes[kv.Key]} maps={string.Join(",", kv.Value)}");
        Console.WriteLine($"texholes: {holes.Count} textures with empty columns, {used.Count} used as solid walls in maps");
        return 0;
    }
}
