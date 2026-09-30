// dev6 triage: render the view from a point (and its unwritten-pixel mask).
//   dotnet run -- rekkr.wad out shot <e> <m> <x> <y> <angleDeg> [pitch] [wide] [lines]
using System;
using System.IO;
using ManagedDoom;
using ManagedDoom.Video;

public static class ShotAt
{
    public static int Run(GameContent content, CommandLineArgs args, string[] a, string outDir)
    {
        int e = int.Parse(a[3]), m = int.Parse(a[4]); int x = int.Parse(a[5]), y = int.Parse(a[6]);
        double ang = double.Parse(a[7]); int pitch = a.Length > 8 ? int.Parse(a[8]) : 0;
        int wide = a.Length > 9 ? int.Parse(a[9]) : 1066; int lines = a.Length > 10 ? int.Parse(a[10]) : 400;
        var c = new Config(); c.video_highresolution = true; c.video_gamescreensize = 8;
        var v = new ShotVideo(c, content, wide, lines); v.DisplayMessage = false;
        var d = new Doom(args, c, content, v, null, null, null);
        d.NewGame(GameSkill.Medium, e, m);
        for (int t = 0; t < 140; t++) d.Update();
        v.WindowSize = 8;
        var world = d.Game.World; var player = world.ConsolePlayer; var mo = player.Mobj;
        world.ThingMovement.UnsetThingPosition(mo);
        mo.X = Fixed.FromInt(x); mo.Y = Fixed.FromInt(y);
        world.ThingMovement.SetThingPosition(mo);
        var sec = mo.Subsector.Sector;
        mo.Z = sec.FloorHeight; player.ViewZ = mo.Z + Player.NormalViewHeight;
        mo.Angle = new Angle((uint)(ang / 360.0 * 4294967296.0));
        v.Inner.LocalViewPitch = pitch;
        var tag = $"E{e}M{m}_{x}_{y}_{ang}_{pitch}";
        v.Shot(d, Path.Combine(outDir, $"shot_{tag}.png"));
        var data = v.Inner.ScreenDataForTest;
        Array.Fill(data, (byte)0); v.Frame(d, Fixed.One); var first = (byte[])data.Clone();
        Array.Fill(data, (byte)255); v.Frame(d, Fixed.One);
        int diff = 0; for (int i = 0; i < data.Length; i++) if (data[i] != first[i]) diff++;
        Console.WriteLine($"shot {tag} sector={Array.IndexOf(world.Map.Sectors, sec)} unwritten={diff}");
        if (Environment.GetEnvironmentVariable("SHOT_SECTORS") == "1")
        {
            var fl = content.Flats; var map = world.Map;
            for (int i = 0; i < map.Sectors.Length; i++)
            {
                var s = map.Sectors[i];
                if (s.FloorHeight.ToIntFloor() <= -700 || s.CeilingHeight.ToIntFloor() >= 700)
                {
                    int nl = 0; foreach (var l in map.Lines) if (l.FrontSector == s || l.BackSector == s) nl++;
                    Console.WriteLine($"sector {i} floor={s.FloorHeight.ToIntFloor()} ceil={s.CeilingHeight.ToIntFloor()} fflat={fl[s.FloorFlat].Name} cflat={fl[s.CeilingFlat].Name} light={s.LightLevel} special={s.Special} tag={s.Tag} lines={nl}");
                }
            }
        }
        return 0;
    }
}
