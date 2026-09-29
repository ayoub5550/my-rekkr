// Headless checks for the REKKR engine core (no Unity needed).
// Usage: dotnet run -- <rekkr.wad> <outdir>
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
        Console.WriteLine(failures == 0 ? "RESULT PASS" : $"RESULT FAIL {failures}");
        return failures == 0 ? 0 : 1;
    }
}

public sealed class ShotVideo : IVideo
{
    private readonly Renderer r; private readonly byte[] buf;
    public ShotVideo(Config c, GameContent content) { r = new Renderer(c, content); buf = new byte[4 * r.Width * r.Height]; }
    public void Render(Doom doom, Fixed frameFrac) { r.Render(doom, buf, frameFrac); }
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
