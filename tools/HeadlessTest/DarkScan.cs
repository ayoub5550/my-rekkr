// dev6 triage ("black areas", mostly E3): per map, renders the HOM-scan views and measures how much of
// the 3D view is near-black (luma < 12 of 255). Saves the darkest view per map for a look.
//   dotnet run -- rekkr.wad out dark [episodes e.g. 3 or 1,2,3,4]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ManagedDoom;
using ManagedDoom.Video;

public static class DarkScan
{
    public static int Run(GameContent content, CommandLineArgs args, int[] episodes, string outDir)
    {
        var c = new Config(); c.video_highresolution = true; c.video_gamescreensize = 8;
        var v = new ShotVideo(c, content, 1066, 400); v.DisplayMessage = false;
        var d = new Doom(args, c, content, v, null, null, null);
        var report = new List<string>();
        foreach (var e in episodes)
            for (int m = 1; m <= 9; m++)
            {
                d.NewGame(GameSkill.Medium, e, m);
                for (int t = 0; t < 140; t++) d.Update();
                v.WindowSize = 8;
                var world = d.Game.World; var player = world.ConsolePlayer; var mo = player.Mobj; var map = world.Map;
                var rng = new Random(e * 100 + m);
                var things = map.Things.Where(t => t.Type != 14 && t.Type < 4000).ToArray();
                var pts = new List<(Fixed, Fixed)> { (mo.X, mo.Y) };
                for (int i = 0; i < 60 && things.Length > 0; i++) { var t = things[rng.Next(things.Length)]; pts.Add((t.X, t.Y)); }
                double worst = 0, sum = 0; int n = 0, over30 = 0; string worstTag = "";
                foreach (var (px, py) in pts)
                {
                    if (!world.ThingMovement.CheckPosition(mo, px, py)) continue;
                    world.ThingMovement.UnsetThingPosition(mo); mo.X = px; mo.Y = py; world.ThingMovement.SetThingPosition(mo);
                    var sec = mo.Subsector.Sector;
                    if (sec.CeilingHeight - sec.FloorHeight < Fixed.FromInt(48)) continue;
                    mo.Z = sec.FloorHeight; player.ViewZ = mo.Z + Player.NormalViewHeight;
                    for (int a = 0; a < 8; a++)
                    {
                        mo.Angle = new Angle((uint)(a * (uint.MaxValue / 8)));
                        var buf = v.Frame(d, Fixed.One);
                        int W = v.W, H = v.H, dark = 0, tot = 0;
                        // buffer is column-major RGBA (x * H + y); skip the status bar rows (bottom 17 %)
                        for (int x = 0; x < W; x += 2)
                            for (int y = 0; y < H * 83 / 100; y += 2)
                            {
                                int i = 4 * (x * H + y);
                                var l = 0.3 * buf[i] + 0.59 * buf[i + 1] + 0.11 * buf[i + 2];
                                tot++; if (l < 12) dark++;
                            }
                        var f = dark / (double)tot; sum += f; n++;
                        if (f > 0.3) over30++;
                        var tag = $"at=({px.ToIntFloor()},{py.ToIntFloor()}) angle={a * 45} sector={Array.IndexOf(map.Sectors, sec)} light={sec.LightLevel}";
                        if (f > 0.3) report.Add($"DARK E{e}M{m} {tag} dark={f:P0}");
                        if (f > worst) { worst = f; worstTag = tag; v.Shot(d, Path.Combine(outDir, $"dark_E{e}M{m}.png")); }
                    }
                }
                Console.WriteLine($"dark E{e}M{m}: views={n} avg={sum / Math.Max(1, n):P0} views_over30%={over30} worst={worst:P0} {worstTag}");
            }
        File.WriteAllLines(Path.Combine(outDir, "dark_report.txt"), report);
        return 0;
    }
}
