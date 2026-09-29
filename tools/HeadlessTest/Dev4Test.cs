// dev4 checks: free-look renders (unwritten pixels + PNGs), jump physics, pitch aim.
using System;
using System.IO;
using System.Linq;
using ManagedDoom;
using ManagedDoom.Video;

public static class Dev4Test
{
    public static int Run(GameContent content, CommandLineArgs args, string outDir)
    {
        int failures = 0;
        ThreeDRenderer.FreeLookSky = true;
        // 1. Free look renders at 400 and 800 lines: no never-written pixels in the 3D window.
        foreach (var (wide, lines) in new[] { (1066, 400), (2134, 800) })
        {
            var c = new Config(); c.video_highresolution = true; c.video_gamescreensize = 8;
            var v = new ShotVideo(c, content, wide, lines); v.DisplayMessage = false;
            var d = new Doom(args, c, content, v, null, null, null);
            int worst = 0, views = 0;
            for (int e = 1; e <= 4; e++)
            {
                d.NewGame(GameSkill.Medium, e, 1);
                for (int t = 0; t < 140; t++) d.Update();
                v.WindowSize = 8;
                var mo = d.Game.World.ConsolePlayer.Mobj;
                foreach (var pitch in new[] { -80, -40, 0, 40, 80 })
                {
                    v.Inner.LocalViewPitch = pitch;
                    for (int a = 0; a < 8; a++)
                    {
                        mo.Angle = new Angle((uint)(a * (uint.MaxValue / 8)));
                        var data = v.Inner.ScreenDataForTest;
                        Array.Fill(data, (byte)0); v.Frame(d, Fixed.One); var first = (byte[])data.Clone();
                        Array.Fill(data, (byte)255); v.Frame(d, Fixed.One);
                        int diff = 0; for (int i = 0; i < data.Length; i++) if (data[i] != first[i]) diff++;
                        views++; worst = Math.Max(worst, diff);
                        if (diff > 60) Console.WriteLine($"FREELOOK gap E{e}M1 {lines}l pitch={pitch} angle={a * 45} pixels={diff}");
                        if (a == 0 || a == 3) v.Shot(d, Path.Combine(outDir, $"look_e{e}_{lines}_p{pitch}_a{a}.png"));
                    }
                }
                v.Inner.LocalViewPitch = 0;
            }
            Console.WriteLine($"freelook {wide}x{lines}: views={views} worst_unwritten={worst}");
            if (worst > 200) failures++;
        }

        ThreeDRenderer.FreeLookSky = false;
        // 2. Jump: peak height and landing on E1M1 start.
        {
            var o = new GameOptions(); o.Skill = GameSkill.Medium; o.Episode = 1; o.Map = 1; o.Players[0].InGame = true;
            var cmds = Enumerable.Range(0, Player.MaxPlayerCount).Select(i => new TicCmd()).ToArray();
            var g = new DoomGame(content, o); g.DeferedInitNew();
            for (int t = 0; t < 40; t++) g.Update(cmds);
            var mo = g.World.ConsolePlayer.Mobj;
            var floor = mo.FloorZ; double peak = 0; int landed = -1;
            cmds[0].Ext = TicCmdExt.Jump; g.Update(cmds); cmds[0].Ext = 0;
            for (int t = 1; t < 60; t++)
            {
                g.Update(cmds);
                var h = (mo.Z - floor).ToDouble();
                peak = Math.Max(peak, h);
                if (h <= 0 && landed < 0 && t > 2) landed = t;
            }
            // a held jump button must not bunny-hop faster than the cooldown
            int jumps = 0; bool air = false;
            cmds[0].Ext = TicCmdExt.Jump;
            for (int t = 0; t < 105; t++) { g.Update(cmds); var up = mo.Z > mo.FloorZ; if (up && !air) jumps++; air = up; }
            cmds[0].Ext = 0;
            Console.WriteLine($"jump: peak={peak:F1} units landed_after={landed} tics, held 3 s -> {jumps} jumps");
            if (peak < 30 || peak > 45 || landed < 0 || jumps > 6) { Console.WriteLine("FAIL jump"); failures++; }
        }

        // 3. Aim: with autoaim off the bullet puff follows the pitch.
        {
            double PuffZ(int pitch, bool noAuto)
            {
                var o = new GameOptions(); o.Skill = GameSkill.Medium; o.Episode = 1; o.Map = 1; o.Players[0].InGame = true;
                var cmds = Enumerable.Range(0, Player.MaxPlayerCount).Select(i => new TicCmd()).ToArray();
                var g = new DoomGame(content, o); g.DeferedInitNew();
                for (int t = 0; t < 20; t++) g.Update(cmds);
                cmds[0].LookPitch = (short)pitch; cmds[0].Ext = noAuto ? TicCmdExt.NoAutoAim : (byte)0;
                cmds[0].Buttons = TicCmdButtons.Attack;
                for (int t = 0; t < 8; t++) g.Update(cmds);
                var puffs = new System.Collections.Generic.List<Mobj>();
                foreach (var th in g.World.Thinkers) if (th is Mobj m && m.Type == MobjType.Puff) puffs.Add(m);
                return puffs.Count == 0 ? double.NaN : puffs.Average(m => (m.Z - g.World.ConsolePlayer.Mobj.Z).ToDouble());
            }
            var z0 = PuffZ(0, true); var zUp = PuffZ(60, true); var zDown = PuffZ(-40, true); var zAuto = PuffZ(0, false);
            Console.WriteLine($"aim puff z (rel. player): pitch0={z0:F1} pitch+60={zUp:F1} pitch-40={zDown:F1} autoaim0={zAuto:F1}");
            if (!(zUp > z0 && zDown < z0)) { Console.WriteLine("FAIL pitch aim"); failures++; }
        }
        Console.WriteLine(failures == 0 ? "DEV4 PASS" : $"DEV4 FAIL {failures}");
        return failures;
    }
}
