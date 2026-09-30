// my-rekkr dev6 — floor/ceiling polygons of a sector.
// 1. Trace closed loops of the sector's boundary lines (lines with the sector on exactly one side) and
//    triangulate them with LibTessDotNet (even-odd rule: holes and nested sectors work).
// 2. Fallback for sectors whose boundary does not close (broken/unclosed sectors, lines that touch the
//    sector on both sides only, overlapping tricks): the union of the sector's BSP subsectors, each one
//    rebuilt as a convex polygon by clipping a big square with the node partition lines on the way down
//    the tree and then with its own segs (the "GL nodes" approach).
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using LibTessDotNet;

namespace ManagedDoom.Remaster
{
    public static class SectorPolygons
    {
        /// <summary>Triangle list (x, y in map units, counter-clockwise seen from above) of sector
        /// <paramref name="s"/>.</summary>
        public static List<(double x, double y)> Triangulate(Map map, int s, out int openEdges, out bool usedFallback)
        {
            usedFallback = false;
            var sector = map.Sectors[s];
            var edges = new List<(Vertex a, Vertex b)>();
            foreach (var l in map.Lines)
            {
                if (l.FrontSector == l.BackSector) continue;
                if (l.FrontSector == sector) edges.Add((l.Vertex1, l.Vertex2));
                else if (l.BackSector == sector) edges.Add((l.Vertex2, l.Vertex1));
            }
            var loops = TraceLoops(edges, out openEdges);
            List<(double, double)> tris = null;
            if (openEdges == 0 && loops.Count > 0)
            {
                tris = Tessellate(loops);
                if (tris != null && Area(tris) < 0.5 * SubsectorArea(map, sector)) tris = null;   // tessellation lost the shape
            }
            if (tris == null)
            {
                usedFallback = true;
                tris = FromSubsectors(map, sector);
            }
            return tris;
        }

        private static List<List<(double x, double y)>> TraceLoops(List<(Vertex a, Vertex b)> edges, out int open)
        {
            var loops = new List<List<(double, double)>>();
            var used = new bool[edges.Count];
            var byStart = new Dictionary<(int, int), List<int>>();
            (int, int) Key(Vertex v) => (v.X.Data, v.Y.Data);
            for (var i = 0; i < edges.Count; i++)
            {
                var k = Key(edges[i].a);
                if (!byStart.TryGetValue(k, out var list)) byStart[k] = list = new List<int>();
                list.Add(i);
            }
            // undirected fallback lookup (sides can be flipped in badly built maps)
            var byEnd = new Dictionary<(int, int), List<int>>();
            for (var i = 0; i < edges.Count; i++)
            {
                var k = Key(edges[i].b);
                if (!byEnd.TryGetValue(k, out var list)) byEnd[k] = list = new List<int>();
                list.Add(i);
            }
            open = 0;
            for (var start = 0; start < edges.Count; start++)
            {
                if (used[start]) continue;
                var loop = new List<(double, double)>();
                used[start] = true;
                var first = Key(edges[start].a);
                var cur = edges[start].b;
                loop.Add((edges[start].a.X.ToDouble(), edges[start].a.Y.ToDouble()));
                var closed = false; var count = 1;
                while (count < edges.Count + 1)
                {
                    var k = Key(cur);
                    if (k == first) { closed = true; break; }
                    loop.Add((cur.X.ToDouble(), cur.Y.ToDouble()));
                    var next = -1;
                    if (byStart.TryGetValue(k, out var cand)) foreach (var c in cand) if (!used[c]) { next = c; break; }
                    if (next >= 0) { used[next] = true; cur = edges[next].b; count++; continue; }
                    if (byEnd.TryGetValue(k, out var cand2)) foreach (var c in cand2) if (!used[c]) { next = c; break; }
                    if (next >= 0) { used[next] = true; cur = edges[next].a; count++; continue; }
                    break;
                }
                if (closed && loop.Count >= 3) loops.Add(loop);
                else if (!closed) open += count;
            }
            return loops;
        }

        private static List<(double, double)> Tessellate(List<List<(double x, double y)>> loops)
        {
            try
            {
                var tess = new Tess();
                foreach (var loop in loops)
                {
                    var cv = new ContourVertex[loop.Count];
                    for (var i = 0; i < loop.Count; i++) cv[i].Position = new Vec3((float)loop[i].x, (float)loop[i].y, 0);
                    tess.AddContour(cv, ContourOrientation.Original);
                }
                tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);
                var res = new List<(double, double)>(tess.ElementCount * 3);
                for (var i = 0; i < tess.ElementCount; i++)
                {
                    var p0 = tess.Vertices[tess.Elements[i * 3]].Position;
                    var p1 = tess.Vertices[tess.Elements[i * 3 + 1]].Position;
                    var p2 = tess.Vertices[tess.Elements[i * 3 + 2]].Position;
                    AddCcw(res, (p0.X, p0.Y), (p1.X, p1.Y), (p2.X, p2.Y));
                }
                return res.Count > 0 ? res : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void AddCcw(List<(double, double)> res, (double x, double y) a, (double x, double y) b, (double x, double y) c)
        {
            var cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Math.Abs(cross) < 1e-6) return;   // degenerate
            if (cross > 0) { res.Add(a); res.Add(b); res.Add(c); }
            else { res.Add(a); res.Add(c); res.Add(b); }
        }

        private static double Area(List<(double x, double y)> tris)
        {
            double a = 0;
            for (var i = 0; i + 2 < tris.Count; i += 3)
            {
                var (ax, ay) = tris[i]; var (bx, by) = tris[i + 1]; var (cx, cy) = tris[i + 2];
                a += Math.Abs((bx - ax) * (cy - ay) - (by - ay) * (cx - ax)) * 0.5;
            }
            return a;
        }

        private static double SubsectorArea(Map map, Sector sector) => Area(FromSubsectors(map, sector));

        // ---------------------------------------------------------------- BSP fallback

        private static Dictionary<Map, List<(double x, double y)>[]> leafCache = new Dictionary<Map, List<(double, double)>[]>();

        private static List<(double x, double y)> FromSubsectors(Map map, Sector sector)
        {
            List<(double, double)>[] leaves;
            lock (leafCache)
            {
                if (!leafCache.TryGetValue(map, out leaves))
                {
                    if (leafCache.Count > 2) leafCache.Clear();
                    leaves = BuildLeaves(map);
                    leafCache[map] = leaves;
                }
            }
            var res = new List<(double, double)>();
            for (var i = 0; i < map.Subsectors.Length; i++)
            {
                if (map.Subsectors[i].Sector != sector) continue;
                var poly = leaves[i];
                if (poly == null || poly.Count < 3) continue;
                for (var k = 1; k + 1 < poly.Count; k++) AddCcw(res, poly[0], poly[k], poly[k + 1]);
            }
            return res;
        }

        private static List<(double x, double y)>[] BuildLeaves(Map map)
        {
            var leaves = new List<(double, double)>[map.Subsectors.Length];
            const double B = 70000;
            var root = new List<(double, double)> { (-B, -B), (B, -B), (B, B), (-B, B) };
            if (map.Nodes.Length == 0) { leaves[0] = Clip(root, map, 0); return leaves; }
            void Walk(int node, List<(double x, double y)> poly)
            {
                if (Node.IsSubsector(node))
                {
                    var ss = node == -1 ? 0 : Node.GetSubsector(node);
                    leaves[ss] = Clip(poly, map, ss);
                    return;
                }
                var n = map.Nodes[node];
                double px = n.X.ToDouble(), py = n.Y.ToDouble(), dx = n.Dx.ToDouble(), dy = n.Dy.ToDouble();
                // child 0 = right (front) side: cross((d), (p - origin)) <= 0
                var right = ClipHalf(poly, px, py, dx, dy, true);
                var left = ClipHalf(poly, px, py, dx, dy, false);
                Walk(n.Children[0], right);
                Walk(n.Children[1], left);
            }
            Walk(map.Nodes.Length - 1, root);
            return leaves;
        }

        private static List<(double x, double y)> Clip(List<(double x, double y)> poly, Map map, int ss)
        {
            var sub = map.Subsectors[ss];
            for (var i = 0; i < sub.SegCount && poly.Count >= 3; i++)
            {
                var seg = map.Segs[sub.FirstSeg + i];
                double ax = seg.Vertex1.X.ToDouble(), ay = seg.Vertex1.Y.ToDouble();
                double dx = seg.Vertex2.X.ToDouble() - ax, dy = seg.Vertex2.Y.ToDouble() - ay;
                poly = ClipHalf(poly, ax, ay, dx, dy, true);   // the subsector is on the right of its segs
            }
            return poly;
        }

        /// <summary>Sutherland–Hodgman against the line (px,py)+t(dx,dy); keeps the right side if <paramref name="right"/>.</summary>
        private static List<(double x, double y)> ClipHalf(List<(double x, double y)> poly, double px, double py, double dx, double dy, bool right)
        {
            var res = new List<(double, double)>(poly.Count + 2);
            double Side((double x, double y) p) { var s = dx * (p.y - py) - dy * (p.x - px); return right ? -s : s; } // >= 0 = keep
            for (var i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                var sa = Side(a); var sb = Side(b);
                if (sa >= -1e-7) res.Add(a);
                if ((sa >= -1e-7) != (sb >= -1e-7))
                {
                    var t = sa / (sa - sb);
                    res.Add((a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t));
                }
            }
            return res;
        }
    }
}
