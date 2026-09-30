// dev6: Remaster level mesh + atlas checks for all 36 maps (no Unity).
using System;
using System.Diagnostics;
using ManagedDoom;
using ManagedDoom.Remaster;

public static class Dev6Test
{
    public static int Run(GameContent content, CommandLineArgs args, string outDir)
    {
        var failures = 0;
        var sw = Stopwatch.StartNew();
        var atlas = new TextureAtlas(content);
        Console.WriteLine($"dev6 atlas {atlas.Width}x{atlas.Height} slots={atlas.SlotCount} textures={atlas.TextureCount} flats={atlas.FlatCount} used={atlas.UsedTexels} ({100.0 * atlas.UsedTexels / ((long)atlas.Width * atlas.Height):F0}% fill) build={sw.ElapsedMilliseconds} ms");
        if (atlas.Height > 8192) { Console.WriteLine("dev6 FAIL atlas too tall"); failures++; }
        long totalTris = 0;
        for (int e = 1; e <= 4; e++)
            for (int m = 1; m <= 9; m++)
            {
                var o = new GameOptions(); o.Episode = e; o.Map = m; o.Players[0].InGame = true;
                var g = new DoomGame(content, o); g.DeferedInitNew();
                g.Update(new TicCmd[] { new TicCmd(), new TicCmd(), new TicCmd(), new TicCmd() });
                var world = g.World;
                sw.Restart();
                LevelGeometry geo;
                try
                {
                    geo = new LevelGeometry(world.Map, content.Textures, content.Flats, atlas, SkyTextureNumber(content, world));
                }
                catch (Exception ex) { Console.WriteLine($"dev6 FAIL E{e}M{m}: {ex}"); failures++; continue; }
                var build = sw.ElapsedMilliseconds;
                // degenerate / NaN check
                int nan = 0; foreach (var v in geo.Vertices) if (float.IsNaN(v.X + v.Y + v.Z + v.U + v.V)) nan++;
                // simulate 200 tics with updates
                sw.Restart();
                int dirtyFrames = 0, dirtyVerts = 0;
                for (int t = 0; t < 200; t++)
                {
                    g.Update(new TicCmd[] { new TicCmd(), new TicCmd(), new TicCmd(), new TicCmd() });
                    geo.Update(world, Fixed.One);
                    if (geo.DirtyRanges.Count > 0) { dirtyFrames++; foreach (var r in geo.DirtyRanges) dirtyVerts += r.count; }
                }
                var upd = sw.Elapsed.TotalMilliseconds / 200;
                totalTris += geo.Triangles;
                Console.WriteLine($"dev6 E{e}M{m}: sectors={geo.SectorCount} traced={geo.TracedSectors} fallback={geo.FallbackSectors} empty={geo.EmptySectors} openEdges={geo.OpenLoopEdges} tris={geo.Triangles} verts={geo.Vertices.Length} nan={nan} build={build}ms update={upd:F3}ms dirtyFrames={dirtyFrames} dirtyVerts/f={(dirtyFrames > 0 ? dirtyVerts / dirtyFrames : 0)}");
                if (nan > 0) failures++;
            }
        Console.WriteLine($"dev6 total tris={totalTris} result={(failures == 0 ? "PASS" : "FAIL")}");
        return failures == 0 ? 0 : 1;
    }

    public static int SkyTextureNumber(GameContent content, World world)
    {
        var tex = content.Textures;
        for (var i = 0; i < tex.Count; i++) if (ReferenceEquals(tex[i], world.Map.SkyTexture)) return i;
        return 0;
    }
}
