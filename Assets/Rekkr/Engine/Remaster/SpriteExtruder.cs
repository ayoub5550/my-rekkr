// my-rekkr dev6 stage 6 — "voxel look" for every sprite, automatically: each opaque pixel of a sprite
// patch becomes a voxel column whose thickness follows a rounded profile (distance to the silhouette
// edge → circle), so monsters and items get real volume, side walls, correct lighting and shadows with
// zero art work. Faces are greedy-merged (equal thickness rectangles / edge runs); texels are sampled
// from the same palette atlas with point sampling, so the colours are the original sprite pixels.
// Mesh space: x = pixels from the patch's left edge minus LeftOffset (right = +x), y = up (0 = bottom row),
// z = depth (+z away from the viewer). Unity-free.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;

namespace ManagedDoom.Remaster
{
    public sealed class ExtrudedMesh
    {
        public RVertex[] Vertices;
        public int[] Indices;
        public int Bytes => Vertices.Length * RVertex.Size + Indices.Length * 4;
    }

    public static class SpriteExtruder
    {
        public static float Step = 0.5F;     // thickness quantisation: 1/Step map units
        public static int MaxSize = 160;

        /// <summary>Builds the mesh of <paramref name="patch"/> (atlas slot <paramref name="slot"/>) with the
        /// thickness scale <paramref name="depthScale"/> (1 monsters, ~0.5 flat items). Mirrored if flip.</summary>
        public static ExtrudedMesh Build(Patch patch, int slot, bool flip, float depthScale)
        {
            int w = patch.Width, h = patch.Height;
            if (w <= 0 || h <= 0 || w > MaxSize || h > MaxSize) return null;   // huge patches stay billboards
            var solid = new bool[w * h];
            for (var x = 0; x < w && x < patch.Columns.Length; x++)
                foreach (var post in patch.Columns[x])
                    for (var k = 0; k < post.Length; k++)
                    {
                        var y = post.TopDelta + k;
                        if (y >= 0 && y < h) solid[y * w + x] = true;
                    }
            // chamfer distance to the nearest empty pixel (outside counts as empty)
            var dist = new float[w * h];
            const float Inf = 1e9F;
            for (var i = 0; i < dist.Length; i++) dist[i] = solid[i] ? Inf : 0;
            float D(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? 0 : dist[y * w + x];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x; if (dist[i] == 0) continue;
                    var d = Math.Min(Math.Min(D(x - 1, y) + 1, D(x, y - 1) + 1), Math.Min(D(x - 1, y - 1) + 1.414F, D(x + 1, y - 1) + 1.414F));
                    dist[i] = Math.Min(dist[i], d);
                }
            for (var y = h - 1; y >= 0; y--)
                for (var x = w - 1; x >= 0; x--)
                {
                    var i = y * w + x; if (dist[i] == 0) continue;
                    var d = Math.Min(Math.Min(D(x + 1, y) + 1, D(x, y + 1) + 1), Math.Min(D(x + 1, y + 1) + 1.414F, D(x - 1, y + 1) + 1.414F));
                    dist[i] = Math.Min(dist[i], d);
                }
            // rounded profile: half thickness of a circle of radius R at distance d from the edge
            var R = Math.Clamp(Math.Min(w, h) * 0.18F, 2F, 8F) * depthScale;
            var hz = new float[w * h];
            for (var i = 0; i < hz.Length; i++)
            {
                if (!solid[i]) continue;
                var d = Math.Min(dist[i] - 0.5F, R);
                var t = MathF.Sqrt(Math.Max(0, 2 * R * d - d * d)) + 0.5F;
                hz[i] = Math.Max(1F, MathF.Round(t * Step) / Step);   // quantised: fewer, larger merged faces
            }
            var verts = new List<RVertex>(1024); var idx = new List<int>(1536);
            float X(int px) => flip ? (patch.LeftOffset - px) : (px - patch.LeftOffset);   // mesh x of pixel edge px
            float Y(int py) => h - py;   // mesh y of pixel edge py (row 0 = top)
            float U(int px) => flip ? w - px : px;   // texel u of pixel edge (patch columns)

            void Quad(float x0, float y0, float z0, float x1, float y1, float z1, float x2, float y2, float z2, float x3, float y3, float z3,
                      float u0, float v0, float u1, float v1, float u2, float v2, float u3, float v3, float nx, float ny, float nz, bool reverse)
            {
                var b = verts.Count;
                // Nx/Nz carry the horizontal normal; ny goes in Light's unused place? Use Kind=Thing and pack ny into Nz sign is not
                // possible, so the vertical component is dropped (sprites are lit by their sector like vanilla; the sun uses nx/nz).
                verts.Add(new RVertex { X = x0, Y = y0, Z = z0, U = u0, V = v0, Tex = slot, Sector = -1, Nx = nx, Nz = nz, Kind = RKind.Thing, Light = ny });
                verts.Add(new RVertex { X = x1, Y = y1, Z = z1, U = u1, V = v1, Tex = slot, Sector = -1, Nx = nx, Nz = nz, Kind = RKind.Thing, Light = ny });
                verts.Add(new RVertex { X = x2, Y = y2, Z = z2, U = u2, V = v2, Tex = slot, Sector = -1, Nx = nx, Nz = nz, Kind = RKind.Thing, Light = ny });
                verts.Add(new RVertex { X = x3, Y = y3, Z = z3, U = u3, V = v3, Tex = slot, Sector = -1, Nx = nx, Nz = nz, Kind = RKind.Thing, Light = ny });
                if (!reverse) { idx.Add(b); idx.Add(b + 1); idx.Add(b + 2); idx.Add(b); idx.Add(b + 2); idx.Add(b + 3); }
                else { idx.Add(b); idx.Add(b + 2); idx.Add(b + 1); idx.Add(b); idx.Add(b + 3); idx.Add(b + 2); }
            }

            // ---- front (-z) and back (+z) faces: greedy rectangles of equal thickness
            var used = new bool[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    if (!solid[i] || used[i]) continue;
                    var t = hz[i];
                    var x1 = x + 1;
                    while (x1 < w && solid[y * w + x1] && !used[y * w + x1] && hz[y * w + x1] == t) x1++;
                    var y1 = y + 1;
                    while (y1 < h)
                    {
                        var ok = true;
                        for (var k = x; k < x1; k++) { var j = y1 * w + k; if (!solid[j] || used[j] || hz[j] != t) { ok = false; break; } }
                        if (!ok) break;
                        y1++;
                    }
                    for (var yy = y; yy < y1; yy++) for (var k = x; k < x1; k++) used[yy * w + k] = true;
                    float ax = X(x), bx = X(x1), ty = Y(y), by = Y(y1);
                    // front: seen from -z; corners bottom-left, top-left, top-right, bottom-right (clockwise)
                    Quad(ax, by, -t, ax, ty, -t, bx, ty, -t, bx, by, -t, U(x), y1, U(x), y, U(x1), y, U(x1), y1, 0, 0, -1, flip);
                    Quad(ax, by, t, ax, ty, t, bx, ty, t, bx, by, t, U(x), y1, U(x), y, U(x1), y, U(x1), y1, 0, 0, 1, !flip);
                }

            // ---- side walls where the neighbour is thinner (or empty): runs along each edge direction
            // horizontal edges (between row y-1 and y): top faces (neighbour above thinner) / bottom faces
            for (var dir = 0; dir < 4; dir++)
            {
                // dir 0: left edge (neighbour x-1), 1: right edge (x+1), 2: top edge (y-1), 3: bottom edge (y+1)
                int dx = dir == 0 ? -1 : dir == 1 ? 1 : 0, dy = dir == 2 ? -1 : dir == 3 ? 1 : 0;
                bool alongX = dir >= 2;   // runs along x for top/bottom edges
                int outer = alongX ? h : w, inner = alongX ? w : h;
                for (var a = 0; a < outer; a++)
                {
                    var b0 = 0;
                    while (b0 < inner)
                    {
                        int px = alongX ? b0 : a, py = alongX ? a : b0;
                        var i = py * w + px;
                        float nt = 0;
                        int qx = px + dx, qy = py + dy;
                        if (qx >= 0 && qy >= 0 && qx < w && qy < h && solid[qy * w + qx]) nt = hz[qy * w + qx];
                        if (!solid[i] || hz[i] <= nt) { b0++; continue; }
                        var t = hz[i];
                        var b1 = b0 + 1;
                        while (b1 < inner)
                        {
                            int rx = alongX ? b1 : a, ry = alongX ? a : b1;
                            var j = ry * w + rx;
                            float mt = 0;
                            int sx = rx + dx, sy = ry + dy;
                            if (sx >= 0 && sy >= 0 && sx < w && sy < h && solid[sy * w + sx]) mt = hz[sy * w + sx];
                            if (!solid[j] || hz[j] != t || mt != nt) break;
                            b1++;
                        }
                        // wall between depth nt and t on both the front and the back side
                        for (var side = 0; side < 2; side++)
                        {
                            float zA = side == 0 ? -t : nt, zB = side == 0 ? -nt : t;
                            if (nt == 0) { zA = -t; zB = t; if (side == 1) break; }
                            if (alongX)
                            {
                                var ey = dir == 2 ? Y(a) : Y(a + 1);
                                float x0 = X(b0), x1 = X(b1);
                                var vrow = a + 0.5F;
                                var up = dir == 2;
                                Quad(x0, ey, zA, x0, ey, zB, x1, ey, zB, x1, ey, zA, U(b0), vrow, U(b0), vrow, U(b1), vrow, U(b1), vrow, 0, up ? 1 : -1, 0, up ^ flip);
                            }
                            else
                            {
                                var ex = dir == 0 ? X(a) : X(a + 1);
                                float y0 = Y(b0), y1 = Y(b1);
                                var ucol = flip ? w - a - 0.5F : a + 0.5F;
                                var nx = (dir == 0 ? -1F : 1F) * (flip ? -1 : 1);
                                Quad(ex, y1, zA, ex, y0, zA, ex, y0, zB, ex, y1, zB, ucol, b1, ucol, b0, ucol, b0, ucol, b1, nx, 0, 0, (dir == 1) ^ flip);
                            }
                        }
                        b0 = b1;
                    }
                }
            }
            return new ExtrudedMesh { Vertices = verts.ToArray(), Indices = idx.ToArray() };
        }
    }
}
