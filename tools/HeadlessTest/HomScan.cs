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
                    // Sample where the designer put things (items, monsters, starts): places players really go.
                    // Random subsector centres were tried first and mostly hit sealed/self-referencing
                    // trick sectors (deep water, fake floors) that draw nothing in vanilla either.
                    var things = map.Things.Where(t => t.Type != 14 && t.Type < 4000).ToArray();
                    for (int i = 0; i < SamplesPerMap && things.Length > 0; i++)
                    {
                        var t = things[rng.Next(things.Length)];
                        points.Add((t.X, t.Y, $"thing{t.Type}"));
                    }
                    var reach = Reachable(map, mo.Subsector.Sector);
                    points.RemoveAll(pt => pt.tag != "start" && !reach.Contains(Geometry.PointInSubsector(pt.x, pt.y, map).Sector));
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

    // Sectors connected to the start (or to a teleport destination) through two-sided lines.
    // Sealed dummy/void sectors are skipped: a view from inside them draws nothing by design.
    private static HashSet<Sector> Reachable(ManagedDoom.Map map, Sector start)
    {
        var seen = new HashSet<Sector> { start };
        var queue = new Queue<Sector>(); queue.Enqueue(start);
        foreach (var t in map.Things)
            if (t.Type == 14) { var s = Geometry.PointInSubsector(t.X, t.Y, map).Sector; if (seen.Add(s)) queue.Enqueue(s); }
        var adj = new Dictionary<Sector, List<Sector>>();
        foreach (var l in map.Lines)
        {
            if (l.BackSector == null) continue;
            if (!adj.TryGetValue(l.FrontSector, out var a)) adj[l.FrontSector] = a = new List<Sector>(); a.Add(l.BackSector);
            if (!adj.TryGetValue(l.BackSector, out var b)) adj[l.BackSector] = b = new List<Sector>(); b.Add(l.FrontSector);
        }
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            if (adj.TryGetValue(s, out var n)) foreach (var o in n) if (seen.Add(o)) queue.Enqueue(o);
        }
        return seen;
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

// Views every solid wall that uses a texture with empty columns, with the hole fill off and on.
public static class HoleFix
{
    public static int Run(GameContent content, CommandLineArgs args, string outDir)
    {
        int before = 0, after = 0, walls = 0;
        var c = new Config(); c.video_highresolution = true; c.video_gamescreensize = 8;
        var v = new ShotVideo(c, content, 1066); v.DisplayMessage = false;
        var d = new Doom(args, c, content, v, null, null, null);
        foreach (var (e, m) in new[] { (1, 1), (1, 7) })
        {
            d.NewGame(GameSkill.Medium, e, m);
            for (int t = 0; t < 140; t++) d.Update();
            var world = d.Game.World; var map = world.Map; var mo = world.ConsolePlayer.Mobj;
            foreach (var l in map.Lines)
            {
                if (l.BackSide != null) continue;
                var tx = content.Textures[l.FrontSide.MiddleTexture];
                if (tx.Composite.Columns.All(col => col.Length > 0)) continue;
                // stand 96 units in front of the line centre (front side is to the right of v1->v2)
                double x1 = l.Vertex1.X.ToDouble(), y1 = l.Vertex1.Y.ToDouble(), x2 = l.Vertex2.X.ToDouble(), y2 = l.Vertex2.Y.ToDouble();
                double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1)); if (len < 32) continue;
                double nx = (y2 - y1) / len, ny = -(x2 - x1) / len;
                var px = Fixed.FromDouble((x1 + x2) / 2 + nx * 96); var py = Fixed.FromDouble((y1 + y2) / 2 + ny * 96);
                world.ThingMovement.UnsetThingPosition(mo); mo.X = px; mo.Y = py; world.ThingMovement.SetThingPosition(mo);
                if (mo.Subsector.Sector != l.FrontSector) continue;
                mo.Z = mo.Subsector.Sector.FloorHeight; world.ConsolePlayer.ViewZ = mo.Z + Player.NormalViewHeight;
                mo.Angle = new Angle((uint)(Math.Atan2(-ny, -nx) / (2 * Math.PI) * 4294967296.0));
                walls++;
                foreach (var fill in new[] { false, true })
                {
                    Texture.FillSolidHoles = fill;
                    var data = v.Inner.ScreenDataForTest;
                    Array.Fill(data, (byte)0); v.Frame(d, Fixed.One); var a = (byte[])data.Clone();
                    Array.Fill(data, (byte)255); v.Frame(d, Fixed.One);
                    int diff = 0; for (int i = 0; i < data.Length; i++) if (data[i] != a[i]) diff++;
                    if (fill) after += diff; else before += diff;
                    if (walls <= 2) v.Shot(d, Path.Combine(outDir, $"holefix_{tx.Name}_{(fill ? "on" : "off")}.png"));
                }
                Texture.FillSolidHoles = true;
            }
        }
        Console.WriteLine($"holefix: walls={walls} unwritten_pixels off={before} on={after}");
        return after == 0 || after < before / 20 ? 0 : 1;
    }
}

// Renderer speed: for each thread count, render the start views of several maps (8 angles each,
// 30 frames per view) and report ms per frame. Usage: bench <width>[:<height>]
public static class Bench
{
    public static int Run(GameContent content, CommandLineArgs args, string size)
    {
        var width = int.Parse(size.Split(':')[0]);
        foreach (var threads in new[] { 1, 2, 4, 6 })
        {
            ThreeDRendererPool.Threads = threads;
            var c = new Config(); c.video_highresolution = true; c.video_gamescreensize = 8;
            var v = new ShotVideo(c, content, width); v.DisplayMessage = false;
            var d = new Doom(args, c, content, v, null, null, null);
            double total = 0; int frames = 0; double worst = 0;
            foreach (var (e, m) in new[] { (1, 1), (1, 7), (2, 6), (3, 6), (4, 1), (4, 9) })
            {
                d.NewGame(GameSkill.Medium, e, m);
                for (int t = 0; t < 140; t++) d.Update();
                var mo = d.Game.World.ConsolePlayer.Mobj;
                for (int a = 0; a < 8; a++)
                {
                    mo.Angle = new Angle((uint)(a * (uint.MaxValue / 8)));
                    for (int f = 0; f < 30; f++)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        v.Frame(d, Fixed.One);
                        var ms = sw.Elapsed.TotalMilliseconds;
                        if (f >= 3) { total += ms; frames++; worst = Math.Max(worst, ms); }
                    }
                }
            }
            Console.WriteLine($"bench w={width} threads={threads} ms_avg={total / frames:F3} ms_max={worst:F2} frames={frames}");
        }
        return 0;
    }
}

// Compares 1-thread and N-thread renders of the same attract frames; prints differing pixels.
public static class ThreadDiff
{
    public static int Run(GameContent content, CommandLineArgs args, int threads, string outDir)
    {
        WipeEffect.TestSeed = 1234;
        ThreeDRendererPool.Threads = 1;
        var c1 = new Config(); c1.video_highresolution = true; var v1 = new ShotVideo(c1, content, 1066);
        var d1 = new Doom(args, c1, content, v1, null, null, null);
        ThreeDRendererPool.Threads = threads;
        var c2 = new Config(); c2.video_highresolution = true; var v2 = new ShotVideo(c2, content, 1066);
        var d2 = new Doom(args, c2, content, v2, null, null, null);
        int bad = 0;
        for (int t = 0; t < 35 * 60; t++)
        {
            d1.Update(); d2.Update();
            if (t % 105 != 50) continue;
            var a = (byte[])v1.Frame(d1, Fixed.One).Clone(); var b = v2.Frame(d2, Fixed.One);
            int n = 0, minX = int.MaxValue, maxX = -1, h = v1.H;
            for (int i = 0; i < a.Length; i += 4) if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) { n++; var x = (i / 4) / h; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); }
            if (n > 0) { bad++; Console.WriteLine($"tdiff t={t} pixels={n} x={minX}..{maxX}"); if (bad == 1) { v1.Shot(d1, Path.Combine(outDir, "td_1.png")); v2.Shot(d2, Path.Combine(outDir, "td_n.png")); } }
        }
        Console.WriteLine($"tdiff threads={threads}: {bad} frames differ");
        return bad == 0 ? 0 : 1;
    }
}
