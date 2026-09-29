// Headless checks for the REKKR engine core (no Unity needed).
// Usage: dotnet run -- <rekkr.wad> <outdir> [mode]
//   modes: all (default: load, demos, all maps, renders, saves, soak + save round trip, golden check)
//          golden-write|golden-check [golden.txt], hom [widths e.g. 1066,640], texholes, holefix,
//          secinfo <episode> <map> <x> <y> <radius>   (lists lines/textures near a point, for triage)
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ManagedDoom;
using ManagedDoom.Video;
using ManagedDoom.Audio;
using ManagedDoom.UserInput;

public static class Program
{
    public static int Main(string[] argv)
    {
        var wad = Path.GetFullPath(argv[0]);
        var outDir = argv.Length > 1 ? argv[1] : "out";
        Directory.CreateDirectory(outDir);
        ConfigUtilities.DataDirectory = Path.GetFullPath(outDir);
        int failures = 0;
        var mode = argv.Length > 2 ? argv[2] : "all";
        var thr = Environment.GetEnvironmentVariable("REKKR_THREADS");
        ThreeDRendererPool.Threads = string.IsNullOrEmpty(thr) ? 1 : int.Parse(thr);
        ThreeDRenderer.TrueColor = Environment.GetEnvironmentVariable("REKKR_TRUECOLOR") == "1";
        if (mode == "lightcmp")
        {
            var largs = new CommandLineArgs(new[] { "-iwad", wad, "-file", Path.Combine(Path.GetDirectoryName(wad), "rekkr-compat.wad") });
            return LightCmp.Run(new GameContent(largs), largs, outDir);
        }
        Console.WriteLine($"render threads={(ThreeDRendererPool.Threads > 0 ? ThreeDRendererPool.Threads : ThreeDRendererPool.AutoThreads)}");
        if (mode == "tdiff")
        {
            var targs = new CommandLineArgs(new[] { "-iwad", wad, "-file", Path.Combine(Path.GetDirectoryName(wad), "rekkr-compat.wad") });
            return ThreadDiff.Run(new GameContent(targs), targs, argv.Length > 3 ? int.Parse(argv[3]) : 4, outDir);
        }
        if (mode == "bench")
        {
            var bargs = new CommandLineArgs(new[] { "-iwad", wad, "-file", Path.Combine(Path.GetDirectoryName(wad), "rekkr-compat.wad") });
            return Bench.Run(new GameContent(bargs), bargs, argv.Length > 3 ? argv[3] : "1066");
        }
        var goldenPath = argv.Length > 3 ? argv[3] : Path.Combine(AppContext.BaseDirectory, "../../../golden.txt");
        if (mode == "secinfo")
        {
            var sa = new CommandLineArgs(new[] { "-iwad", wad, "-file", Path.Combine(Path.GetDirectoryName(wad), "rekkr-compat.wad") });
            var sc = new GameContent(sa);
            SecInfo.Run(sc, int.Parse(argv[3]), int.Parse(argv[4]), int.Parse(argv[5]), int.Parse(argv[6]), int.Parse(argv[7]));
            return 0;
        }
        if (mode == "holefix")
        {
            var ha = new CommandLineArgs(new[] { "-iwad", wad, "-file", Path.Combine(Path.GetDirectoryName(wad), "rekkr-compat.wad") });
            return HoleFix.Run(new GameContent(ha), ha, outDir);
        }
        if (mode == "texholes")
        {
            var targs = new CommandLineArgs(new[] { "-iwad", wad, "-file", Path.Combine(Path.GetDirectoryName(wad), "rekkr-compat.wad") });
            return TexHoles.Run(new GameContent(targs), targs);
        }
        if (mode == "hom")
        {
            var hargs = new CommandLineArgs(new[] { "-iwad", wad, "-file", Path.Combine(Path.GetDirectoryName(wad), "rekkr-compat.wad") });
            var widths = argv.Length > 3 ? argv[3].Split(',').ToArray() : new[] { "1066", "640" };
            return HomScan.Run(new GameContent(hargs), hargs, widths, outDir);
        }
        if (mode == "golden-write" || mode == "golden-check")
        {
            var gargs = new CommandLineArgs(new[] { "-iwad", wad, "-file", Path.Combine(Path.GetDirectoryName(wad), "rekkr-compat.wad") });
            return Golden.Run(new GameContent(gargs), gargs, Path.GetFullPath(goldenPath), mode == "golden-write");
        }

        // 1. Full content load (textures, flats, sprites, DeHackEd).
        var args = new CommandLineArgs(new[] { "-iwad", wad, "-file", Path.Combine(Path.GetDirectoryName(wad), "rekkr-compat.wad") });
        var content = new GameContent(args);
        Console.WriteLine($"GameMode={content.Wad.GameMode} Version={content.Wad.GameVersion} lumps={content.Wad.LumpInfos.Count}");
        if (content.Wad.GameMode != GameMode.Retail) { Console.WriteLine("FAIL game mode"); failures++; }

        // 2. Each built-in demo must play to its end without exceptions.
        foreach (var name in new[] { "DEMO1", "DEMO2", "DEMO3", "DEMO4" })
        {
            var demo = new Demo(content.Wad.ReadLump(name));
            var cmds = Enumerable.Range(0, Player.MaxPlayerCount).Select(i => new TicCmd()).ToArray();
            var game = new DoomGame(content, demo.Options);
            game.DeferedInitNew();
            int tics = 0;
            while (demo.ReadCmd(cmds)) { game.Update(cmds); tics++; }
            var p = game.World.ConsolePlayer;
            Console.WriteLine($"{name}: E{demo.Options.Episode}M{demo.Options.Map} skill={demo.Options.Skill} tics={tics} ({tics / 35.0:F1}s) state={game.State} health={p.Health} kills={p.KillCount}/{game.World.TotalKills} items={p.ItemCount} secrets={p.SecretCount}");
        }

        // 3. Every map loads and runs 10 s idle.
        for (int e = 1; e <= 4; e++)
            for (int m = 1; m <= 9; m++)
            {
                try
                {
                    var o = new GameOptions(); o.Skill = GameSkill.Medium; o.Episode = e; o.Map = m; o.Players[0].InGame = true;
                    var cmds = Enumerable.Range(0, Player.MaxPlayerCount).Select(i => new TicCmd()).ToArray();
                    var g = new DoomGame(content, o); g.DeferedInitNew();
                    for (int t = 0; t < 350; t++) g.Update(cmds);
                    Console.WriteLine($"E{e}M{m}: ok monsters={g.World.TotalKills} items={g.World.TotalItems} secrets={g.World.TotalSecrets} alive={g.World.ConsolePlayer.Health > 0}");
                }
                catch (Exception ex) { Console.WriteLine($"E{e}M{m}: FAIL {ex.GetType().Name}: {ex.Message}"); failures++; }
            }

        // 4. Attract loop with the real renderer: title, demos, rendered to PNG.
        var config = new Config(); config.video_highresolution = true;
        var video = new ShotVideo(config, content);
        var doom = new Doom(args, config, content, video, null, null, null);
        int shots = 0;
        for (int t = 0; t < 35 * 150; t++)
        {
            doom.Update();
            if (t % (35 * 6) == 20) { video.Shot(doom, Path.Combine(outDir, $"attract_{shots++:D2}.png")); }
        }
        Console.WriteLine($"attract shots={shots}");

        // 5. Widescreen (20:9 -> 1066x400) + fullscreen HUD render check.
        foreach (var wide in new[] { 1066, 640 })
        {
            var wc = new Config(); wc.video_highresolution = true; wc.video_gamescreensize = 7;
            var wv = new ShotVideo(wc, content, wide);
            var wd = new Doom(args, wc, content, wv, null, null, null);
            for (int t = 0; t < 200; t++) wd.Update();
            wv.Shot(wd, Path.Combine(outDir, $"wide{wide}_0title.png"));
            wd.NewGame(GameSkill.Medium, 1, 1);
            for (int t = 0; t < 120; t++) wd.Update();
            wv.Shot(wd, Path.Combine(outDir, $"wide{wide}_1statusbar.png"));
            wv.WindowSize = 9;
            wv.Shot(wd, Path.Combine(outDir, $"wide{wide}_2hud.png"));
            wv.WindowSize = 8;
            wv.Shot(wd, Path.Combine(outDir, $"wide{wide}_3clean.png"));
            wv.WindowSize = 5;
            wv.Shot(wd, Path.Combine(outDir, $"wide{wide}_4small.png"));
            wv.WindowSize = 7;
            wd.Game.World.AutoMap.Open();
            wv.Shot(wd, Path.Combine(outDir, $"wide{wide}_5automap.png"));
            wd.Game.World.AutoMap.Close();
            wd.Menu.Open();
            wv.Shot(wd, Path.Combine(outDir, $"wide{wide}_6menu.png"));
            wd.Menu.Close();
            Console.WriteLine($"wide {wide}: frame {wv.W}x{wv.H} ok");
        }
        // 6. Autosave/quick-save path used by the app: save mid-level into slot 9, then load it
        //    from the title screen (the CONTINUE button) in a fresh Doom instance.
        {
            var sc = new Config(); sc.video_highresolution = true;
            var sv = new ShotVideo(sc, content, 1066);
            var sd = new Doom(args, sc, content, sv, null, null, null);
            sd.NewGame(GameSkill.Hard, 2, 3);
            for (int t = 0; t < 200; t++) sd.Update();
            var savePath = Path.Combine(ConfigUtilities.GetExeDirectory(), "doomsav9.dsg");
            SaveAndLoad.Save(sd.Game, "AUTO E2M3", savePath);
            var ld = new Doom(args, sc, content, new ShotVideo(sc, content, 1066), null, null, null);
            for (int t = 0; t < 100; t++) ld.Update();
            ld.LoadGame(9);
            for (int t = 0; t < 100; t++) ld.Update();
            var ok = ld.State == DoomState.Game && ld.Game.State == GameState.Level && ld.Game.Options.Episode == 2 && ld.Game.Options.Map == 3;
            Console.WriteLine($"continue from title: state={ld.State} E{ld.Game.Options.Episode}M{ld.Game.Options.Map} skill={ld.Game.Options.Skill} size={new FileInfo(savePath).Length} {(ok ? "ok" : "FAIL")}");
            if (!ok) failures++;
        }

        // 8. dev3 stage 1: every map — 2000-tic random-bot soak, then save -> load -> replay the same
        //    200 tic commands on the original and on the loaded game: world state must be identical.
        for (int e = 1; e <= 4; e++)
            for (int m = 1; m <= 9; m++)
            {
                try
                {
                    var o = new GameOptions(); o.Skill = GameSkill.Hard; o.Episode = e; o.Map = m; o.Players[0].InGame = true;
                    var g = new DoomGame(content, o); g.DeferedInitNew();
                    var bot = new Random(e * 10 + m);
                    var cmds = Enumerable.Range(0, Player.MaxPlayerCount).Select(i => new TicCmd()).ToArray();
                    int t = 0;
                    for (; t < 2000 && g.State == GameState.Level; t++) { Bot(bot, cmds[0]); g.Update(cmds); }
                    if (g.State != GameState.Level || g.World.ConsolePlayer.Health <= 0)
                    {
                        // died or finished the level: restart the map for the save/load part
                        g = new DoomGame(content, o); g.DeferedInitNew();
                        for (int k = 0; k < 300; k++) { Bot(bot, cmds[0]); g.Update(cmds); }
                    }
                    var path = Path.Combine(ConfigUtilities.GetExeDirectory(), "doomsav7.dsg");
                    SaveAndLoad.Save(g, "TEST", path);
                    // Round trip: load the save into a fresh game (no tic in between) and save again;
                    // both files must be byte-identical (vanilla format, the RNG index is not stored).
                    var loaded = new DoomGame(content, o); loaded.DeferedInitNew(); loaded.Update(cmds);
                    SaveAndLoad.Load(loaded, path);
                    var path2 = Path.Combine(ConfigUtilities.GetExeDirectory(), "doomsav6.dsg");
                    SaveAndLoad.Save(loaded, "TEST", path2);
                    var f1 = File.ReadAllBytes(path); var f2 = File.ReadAllBytes(path2);
                    var ok = f1.AsSpan().SequenceEqual(f2) && WorldHash(g) == WorldHash(loaded);
                    var h1 = $"{f1.Length}B"; var h2 = $"{f2.Length}B {WorldHash(g)} vs {WorldHash(loaded)}";
                    Console.WriteLine($"soak+saveload E{e}M{m}: tics={t} health={g.World.ConsolePlayer.Health} save={h1} {(ok ? "ok" : $"FAIL {h1} != {h2}")}");
                    if (!ok) failures++;
                }
                catch (Exception ex) { Console.WriteLine($"soak+saveload E{e}M{m}: FAIL {ex}"); failures++; }
            }

        // 7. Classic renderer must stay pixel-identical to v0.2.0 (dev3 safety net).
        if (File.Exists(Path.GetFullPath(goldenPath)))
        {
            if (Golden.Run(content, args, Path.GetFullPath(goldenPath), false) != 0) failures++;
        }
        else Console.WriteLine("golden: no golden.txt (skipped)");

        Console.WriteLine(failures == 0 ? "RESULT PASS" : $"RESULT FAIL {failures}");
        return failures == 0 ? 0 : 1;
    }

    static void Bot(Random r, TicCmd c)
    {
        c.Clear();
        c.ForwardMove = (sbyte)r.Next(-50, 51);
        c.SideMove = (sbyte)r.Next(-40, 41);
        c.AngleTurn = (short)(r.Next(-1200, 1201));
        byte b = 0;
        if (r.Next(4) == 0) b |= TicCmdButtons.Attack;
        if (r.Next(12) == 0) b |= TicCmdButtons.Use;
        c.Buttons = b;
    }

    static string WorldHash(DoomGame g)
    {
        long h = 17;
        foreach (var th in g.World.Thinkers)
            if (th is Mobj mo) h = h * 31 + mo.X.Data * 7 + mo.Y.Data * 13 + mo.Z.Data + mo.Health * 101 + (int)mo.Angle.Data;
        var p = g.World.ConsolePlayer;
        h = h * 31 + p.Health + p.KillCount * 1000 + g.World.LevelTime;
        return h.ToString("x");
    }
}

/// <summary>dev3 safety net: SHA-256 of fixed rendered frames (attract loop with the demos, and each
/// episode start with several HUD sizes, widescreen and 4:3, interpolated and not). The Classic
/// renderer must reproduce tools/HeadlessTest/golden.txt exactly after every dev3 stage.</summary>
public static class Golden
{
    public static int Run(GameContent content, CommandLineArgs args, string path, bool write)
    {
        var lines = new System.Collections.Generic.List<string>();
        WipeEffect.TestSeed = 1234;
        using var sha = System.Security.Cryptography.SHA256.Create();
        string H(byte[] b) => Convert.ToHexString(sha.ComputeHash(b)).ToLowerInvariant().Substring(0, 16);
        foreach (var wide in new[] { 1066, 640 })
        {
            var c = new Config(); c.video_highresolution = true;
            var v = new ShotVideo(c, content, wide);
            var d = new Doom(args, c, content, v, null, null, null);
            for (int t = 0; t < 35 * 150; t++)
            {
                d.Update();
                if (t % 105 == 50) lines.Add($"attract w={wide} t={t} {H(v.Frame(d, Fixed.One))}");
                if (t % 525 == 300) lines.Add($"attract w={wide} t={t} frac=0.5 {H(v.Frame(d, Fixed.One / 2))}");
            }
            for (int e = 1; e <= 4; e++)
            {
                var gc = new Config(); gc.video_highresolution = true; gc.video_gamescreensize = 7;
                var gv = new ShotVideo(gc, content, wide);
                var gd = new Doom(args, gc, content, gv, null, null, null);
                gd.NewGame(GameSkill.Medium, e, 1);
                for (int t = 0; t < 140; t++) gd.Update();
                foreach (var size in new[] { 7, 9, 8, 5 })
                {
                    gv.WindowSize = size;
                    lines.Add($"E{e}M1 w={wide} size={size} {H(gv.Frame(gd, Fixed.One))}");
                }
                gv.WindowSize = 7;
                lines.Add($"E{e}M1 w={wide} frac=0.25 {H(gv.Frame(gd, Fixed.One / 4))}");
                gd.Game.World.AutoMap.Open();
                lines.Add($"E{e}M1 w={wide} automap {H(gv.Frame(gd, Fixed.One))}");
                gd.Game.World.AutoMap.Close();
                gd.Menu.Open();
                lines.Add($"E{e}M1 w={wide} menu {H(gv.Frame(gd, Fixed.One))}");
                gd.Menu.Close();
            }
        }
        if (write)
        {
            File.WriteAllLines(path, new[] { "# dev3 golden frame hashes (v0.2.0 Classic renderer). Regenerate ONLY if the Classic look is meant to change." }.Concat(lines));
            Console.WriteLine($"golden: wrote {lines.Count} hashes to {path}");
            return 0;
        }
        var want = File.ReadAllLines(path).Where(l => !l.StartsWith("#")).ToArray();
        int bad = 0;
        for (int i = 0; i < Math.Max(want.Length, lines.Count); i++)
        {
            var w = i < want.Length ? want[i] : "(missing)"; var g = i < lines.Count ? lines[i] : "(missing)";
            if (w != g) { bad++; if (bad <= 10) Console.WriteLine($"golden MISMATCH: want [{w}] got [{g}]"); }
        }
        Console.WriteLine(bad == 0 ? $"golden: {lines.Count} frames identical ok" : $"golden: FAIL {bad}/{lines.Count} frames differ");
        return bad == 0 ? 0 : 1;
    }
}

public sealed class ShotVideo : IVideo
{
    private readonly Renderer r; private readonly byte[] buf;
    public ShotVideo(Config c, GameContent content, int wide = 0, int lines = 0) { r = new Renderer(c, content, wide, lines); buf = new byte[4 * r.Width * r.Height]; }
    public int W => r.Width; public int H => r.Height;
    public void Render(Doom doom, Fixed frameFrac) { r.Render(doom, buf, frameFrac); }
    public byte[] Frame(Doom doom, Fixed frac) { r.Render(doom, buf, frac); return buf; }
    public Renderer Inner => r;
    public void Shot(Doom doom, string path)
    {
        r.Render(doom, buf, Fixed.One);
        int w = r.Width, h = r.Height; // buffer is column-major: x*h + y
        var rgb = new byte[w * h * 3];
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
        { int s = 4 * (x * h + y), d = 3 * (y * w + x); rgb[d] = buf[s]; rgb[d + 1] = buf[s + 1]; rgb[d + 2] = buf[s + 2]; }
        Png.Write(path, w, h, rgb);
    }
    public void InitializeWipe() => r.InitializeWipe();
    public bool HasFocus() => true;
    public int MaxWindowSize => r.MaxWindowSize;
    public int WindowSize { get => r.WindowSize; set => r.WindowSize = value; }
    public bool DisplayMessage { get => r.DisplayMessage; set => r.DisplayMessage = value; }
    public int MaxGammaCorrectionLevel => r.MaxGammaCorrectionLevel;
    public int GammaCorrectionLevel { get => r.GammaCorrectionLevel; set => r.GammaCorrectionLevel = value; }
    public int WipeBandCount => r.WipeBandCount;
    public int WipeHeight => r.WipeHeight;
}

public static class Png
{
    static uint[] crcT = Enumerable.Range(0, 256).Select(n => { uint c = (uint)n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; return c; }).ToArray();
    static uint Crc(byte[] d) { uint c = 0xFFFFFFFFu; foreach (var b in d) c = crcT[(c ^ b) & 0xFF] ^ (c >> 8); return c ^ 0xFFFFFFFFu; }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = BitConverter.GetBytes(data.Length); Array.Reverse(len); s.Write(len);
        var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); s.Write(td);
        var c = BitConverter.GetBytes(Crc(td)); Array.Reverse(c); s.Write(c);
    }
    public static void Write(string path, int w, int h, byte[] rgb)
    {
        using var f = File.Create(path);
        f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13]; var W = BitConverter.GetBytes(w); Array.Reverse(W); var H = BitConverter.GetBytes(h); Array.Reverse(H);
        W.CopyTo(ihdr, 0); H.CopyTo(ihdr, 4); ihdr[8] = 8; ihdr[9] = 2; Chunk(f, "IHDR", ihdr);
        var raw = new MemoryStream();
        for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgb, y * w * 3, w * 3); }
        var comp = new MemoryStream(); using (var z = new ZLibStream(comp, CompressionLevel.Fastest, true)) { raw.Position = 0; raw.CopyTo(z); }
        Chunk(f, "IDAT", comp.ToArray()); Chunk(f, "IEND", new byte[0]);
    }
}
