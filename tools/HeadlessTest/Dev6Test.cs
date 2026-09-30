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
        failures += Extrude(content, atlas);
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


    // stage 6/7 checks: every sprite patch extrudes; a self-made KVX round-trips.
    public static int Extrude(GameContent content, TextureAtlas atlas)
    {
        var fails = 0; long bytes = 0; int n = 0, maxTris = 0; long tris = 0;
        var sw = Stopwatch.StartNew();
        var seen = new System.Collections.Generic.HashSet<Patch>();
        foreach (Sprite s in Enum.GetValues(typeof(Sprite)))
        {
            if (s == Sprite.Count) continue;
            var def = content.Sprites[s]; if (def == null) continue;
            foreach (var f in def.Frames)
            {
                if (f == null) continue;
                foreach (var p in f.Patches)
                {
                    if (p == null || !seen.Add(p)) continue;
                    try
                    {
                        var m = SpriteExtruder.Build(p, atlas.SpriteSlot(p), false, 1F);
                        if (m == null) continue;
                        foreach (var v in m.Vertices) if (float.IsNaN(v.X + v.Y + v.Z)) { fails++; break; }
                        n++; bytes += m.Bytes; var t = m.Indices.Length / 3; tris += t; if (t > maxTris) maxTris = t;
                    }
                    catch (Exception ex) { fails++; Console.WriteLine($"dev6 extrude FAIL {p.Name}: {ex.Message}"); }
                }
            }
        }
        Console.WriteLine($"dev6 extrude patches={n} fails={fails} tris_avg={(n > 0 ? tris / n : 0)} tris_max={maxTris} all_meshes_MB={bytes / 1048576.0:F1} time={sw.ElapsedMilliseconds} ms");
        // KVX: 8x8x16 barrel-like test model (self-authored → public domain), write → load → mesh
        var pal6 = new byte[768]; for (var i = 0; i < 256; i++) { pal6[3 * i] = (byte)(i / 4); pal6[3 * i + 1] = (byte)(i / 4); pal6[3 * i + 2] = (byte)(i / 4); }
        var kvx = KvxModel.Write(8, 8, 16, (x, y, z) => ((x - 3.5) * (x - 3.5) + (y - 3.5) * (y - 3.5) <= 13) ? 40 + z * 8 : -1, pal6);
        var model = KvxModel.Load(kvx, content.Wad.ReadLump("PLAYPAL"));
        int filled = 0; foreach (var b in model.Voxels) if (b != 0) filled++;
        var mesh = model.Mesh(1F);
        var map = VoxelMap.Parse("# test\nBAR1A = test.kvx scale=1.5 angle=90\nbad line\n");
        var ok = model.SizeX == 8 && model.SizeY == 8 && model.SizeZ == 16 && filled > 400 && mesh.Indices.Length > 0 && map.Entries.Count == 1 && map.Entries["BAR1A"].Scale == 1.5F;
        Console.WriteLine($"dev6 kvx size={model.SizeX}x{model.SizeY}x{model.SizeZ} voxels={filled} tris={mesh.Indices.Length / 3} mapping={map.Entries.Count} {(ok ? "OK" : "FAIL")}");
        System.IO.File.WriteAllBytes("test_barrel.kvx", kvx);
        if (!ok) fails++;
        return fails;
    }

    public static int SkyTextureNumber(GameContent content, World world)
    {
        var tex = content.Textures;
        for (var i = 0; i < tex.Count; i++) if (ReferenceEquals(tex[i], world.Map.SkyTexture)) return i;
        return 0;
    }
}
