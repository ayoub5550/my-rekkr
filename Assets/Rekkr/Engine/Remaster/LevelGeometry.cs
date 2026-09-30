// my-rekkr dev6 — the level as a fixed-topology triangle mesh built from the map (walls per side,
// flats per sector) that is updated in place when sectors move, textures change (switches, scrollers)
// or animate. Unity-free (System only): the same code is checked by tools/HeadlessTest (mode dev6).
// Coordinates: see RVertex.cs. Texture pegging follows ThreeDRenderer (vanilla R_StoreWallRange).
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;

namespace ManagedDoom.Remaster
{
    public sealed class LevelGeometry
    {
        public enum Part : byte { Middle, Upper, Lower, Masked, SkyUpper, SkyLower }
        public enum Group : byte { Opaque = 0, Masked = 1, Sky = 2 }

        private struct Quad
        {
            public int Line;
            public bool Back;        // the back side of the line
            public Part Part;
            public int V0;           // first of 4 vertices
        }

        private struct FlatRun
        {
            public int Sector;
            public bool Ceiling;
            public int V0, Count;    // vertices
        }

        public RVertex[] Vertices;
        public int[][] Indices = new int[3][];          // per Group
        public int SectorCount => map.Sectors.Length;

        // stats (HeadlessTest dev6)
        public int Triangles, FallbackSectors, TracedSectors, EmptySectors, OpenLoopEdges, DegenerateTriangles;

        private readonly Map map;
        private readonly ITextureLookup textures;
        private readonly IFlatLookup flatLookup;
        private readonly TextureAtlas atlas;
        private readonly int skyFlat;
        private readonly int skyTexSlot;
        private readonly Quad[] quads;
        private readonly FlatRun[] flats;
        private readonly List<int>[] sectorQuads;      // sector -> quads touching it
        private readonly List<int>[] sectorFlats;      // sector -> flat runs
        private readonly List<int>[] lineQuads;
        private readonly float[] floorH, ceilH;        // last written heights
        private readonly int[] sideState;              // hash of (textures, offsets) per side
        private readonly bool[] lineDirty;
        private readonly bool[] flatDirty;

        /// <summary>Vertex ranges written by the last Update (for partial GPU uploads).</summary>
        public readonly List<(int start, int count)> DirtyRanges = new List<(int, int)>();

        public LevelGeometry(Map map, ITextureLookup textures, IFlatLookup flatPics, TextureAtlas atlas, int skyTextureNumber)
        {
            this.map = map; this.textures = textures; flatLookup = flatPics; this.atlas = atlas;
            var skyFlatNumber = flatPics.SkyFlatNumber; skyFlat = skyFlatNumber; skyTexSlot = skyTextureNumber;
            var sc = map.Sectors.Length;
            floorH = new float[sc]; ceilH = new float[sc];
            sectorQuads = new List<int>[sc]; sectorFlats = new List<int>[sc];
            for (var i = 0; i < sc; i++) { sectorQuads[i] = new List<int>(); sectorFlats[i] = new List<int>(); }
            lineQuads = new List<int>[map.Lines.Length];

            // ---- walls: a fixed set of quads per side, degenerate when not visible
            var q = new List<Quad>();
            var v = 0;
            for (var li = 0; li < map.Lines.Length; li++)
            {
                var line = map.Lines[li];
                lineQuads[li] = new List<int>();
                void Add(bool back, Part p)
                {
                    lineQuads[li].Add(q.Count);
                    var fs = back ? line.BackSector : line.FrontSector; var bs = back ? line.FrontSector : line.BackSector;
                    sectorQuads[fs.Number].Add(q.Count);
                    if (bs != null && bs != fs) sectorQuads[bs.Number].Add(q.Count);
                    q.Add(new Quad { Line = li, Back = back, Part = p, V0 = v }); v += 4;
                }
                if (line.BackSide == null || line.BackSector == null)
                {
                    Add(false, Part.Middle);
                    continue;
                }
                foreach (var back in new[] { false, true })
                {
                    Add(back, Part.Upper); Add(back, Part.Lower); Add(back, Part.Masked);
                    Add(back, Part.SkyUpper); Add(back, Part.SkyLower);
                }
            }
            quads = q.ToArray();
            sideState = new int[map.Lines.Length * 2];
            lineDirty = new bool[map.Lines.Length];

            // ---- flats: triangulated sector polygons
            var fr = new List<FlatRun>();
            var tris = new List<(double x, double y)>[sc];
            for (var s = 0; s < sc; s++)
            {
                var res = SectorPolygons.Triangulate(map, s, out var openEdges, out var usedFallback);
                tris[s] = res;
                OpenLoopEdges += openEdges;
                if (res.Count == 0) EmptySectors++;
                else if (usedFallback) FallbackSectors++;
                else TracedSectors++;
            }
            var flatVerts = 0;
            for (var s = 0; s < sc; s++) flatVerts += 2 * tris[s].Count;
            Vertices = new RVertex[v + flatVerts];
            for (var s = 0; s < sc; s++)
            {
                if (tris[s].Count == 0) continue;
                foreach (var ceil in new[] { false, true })
                {
                    sectorFlats[s].Add(fr.Count);
                    fr.Add(new FlatRun { Sector = s, Ceiling = ceil, V0 = v, Count = tris[s].Count });
                    for (var i = 0; i < tris[s].Count; i++)
                    {
                        var (x, y) = tris[s][ceil ? (i / 3 * 3 + 2 - i % 3) : i];   // ceilings: reversed winding
                        Vertices[v + i] = new RVertex { X = (float)x, Z = (float)y, U = (float)x, V = (float)-y, Sector = s, Kind = ceil ? RKind.Ceiling : RKind.Floor, Tex = -1 };
                    }
                    v += tris[s].Count;
                }
            }
            flats = fr.ToArray();
            flatDirty = new bool[flats.Length];

            // ---- index buffers: walls by their current group are rebuilt when a quad changes group, so
            // quads are listed in every group they can appear in and made degenerate elsewhere.
            var idx = new[] { new List<int>(), new List<int>(), new List<int>() };
            foreach (var quad in quads)
            {
                var g = quad.Part == Part.Masked ? Group.Masked : (quad.Part == Part.SkyUpper || quad.Part == Part.SkyLower) ? Group.Sky : Group.Opaque;
                var list = idx[(int)g];
                var b = quad.V0;
                list.Add(b); list.Add(b + 1); list.Add(b + 2);
                list.Add(b); list.Add(b + 2); list.Add(b + 3);
            }
            wallIndices = idx;
            BuildIndices();

            // first full write
            for (var s = 0; s < sc; s++) { floorH[s] = float.NaN; ceilH[s] = float.NaN; }
            Update(null, Fixed.One, true);
        }

        private List<int>[] wallIndices;
        private bool[] flatSky;

        /// <summary>Incremented when <see cref="Indices"/> changed (a flat became sky or stopped being sky).</summary>
        public int IndicesVersion { get; private set; }

        private void BuildIndices()
        {
            flatSky ??= new bool[flats.Length];
            var op = new List<int>(wallIndices[0]); var sk = new List<int>(wallIndices[2]);
            for (var fi = 0; fi < flats.Length; fi++)
            {
                var f = flats[fi]; var sec = map.Sectors[f.Sector];
                flatSky[fi] = (f.Ceiling ? sec.CeilingFlat : sec.FloorFlat) == flatLookup.SkyFlatNumber;
                var list = flatSky[fi] ? sk : op;
                for (var i = 0; i < f.Count; i++) list.Add(f.V0 + i);
            }
            Indices[0] = op.ToArray(); Indices[1] = wallIndices[1].ToArray(); Indices[2] = sk.ToArray();
            Triangles = (Indices[0].Length + Indices[1].Length + Indices[2].Length) / 3;
            IndicesVersion++;
        }

        public int QuadCount => quads.Length;
        public int FlatVertexCount => Vertices.Length - quads.Length * 4;

        /// <summary>Re-writes the vertices of everything that changed since the last call (sector heights
        /// at this frame's interpolation, side textures and offsets). <see cref="DirtyRanges"/> lists them.</summary>
        public void Update(World world, Fixed frac, bool all = false)
        {
            DirtyRanges.Clear();
            var sectors = map.Sectors;
            for (var s = 0; s < sectors.Length; s++)
            {
                var sec = sectors[s];
                var f = sec.GetInterpolatedFloorHeight(frac).ToFloat();
                var c = sec.GetInterpolatedCeilingHeight(frac).ToFloat();
                if (!all && f == floorH[s] && c == ceilH[s]) continue;
                floorH[s] = f; ceilH[s] = c;
                foreach (var qi in sectorQuads[s]) lineDirty[quads[qi].Line] = true;
                foreach (var fi in sectorFlats[s]) flatDirty[fi] = true;
            }
            // side textures / offsets (switches, scrolling walls)
            for (var li = 0; li < map.Lines.Length; li++)
            {
                var line = map.Lines[li];
                for (var b = 0; b < 2; b++)
                {
                    var side = b == 0 ? line.FrontSide : line.BackSide;
                    if (side == null) continue;
                    var h = side.TopTexture * 73856093 ^ side.BottomTexture * 19349663 ^ side.MiddleTexture * 83492791
                            ^ side.TextureOffset.Data * 2654435 ^ side.RowOffset.Data * 40503 ^ (int)(line.Flags & (LineFlags.DontPegTop | LineFlags.DontPegBottom));
                    if (all || h != sideState[li * 2 + b]) { sideState[li * 2 + b] = h; lineDirty[li] = true; }
                }
            }
            // flat pictures can change (floor/ceiling changers): cheap to compare every frame
            var rebuild = false;
            for (var fi = 0; fi < flats.Length; fi++)
            {
                var fl = flats[fi];
                var sec = sectors[fl.Sector];
                var pic = fl.Ceiling ? sec.CeilingFlat : sec.FloorFlat;
                if (all || flatDirty[fi] || (int)Vertices[fl.V0].Light != PackFlat(pic))
                {
                    if (flatSky != null && flatSky[fi] != (pic == skyFlat)) rebuild = true;
                    WriteFlat(fl, sec, pic);
                    DirtyRanges.Add((fl.V0, fl.Count));
                    flatDirty[fi] = false;
                }
            }
            for (var li = 0; li < map.Lines.Length; li++)
            {
                if (!lineDirty[li]) continue;
                lineDirty[li] = false;
                var lq = lineQuads[li];
                foreach (var qi in lq) WriteQuad(quads[qi]);
                DirtyRanges.Add((quads[lq[0]].V0, lq.Count * 4));
            }
            if (rebuild) BuildIndices();
            MergeRanges();
        }

        private void MergeRanges()
        {
            if (DirtyRanges.Count < 2) return;
            DirtyRanges.Sort((a, b) => a.start.CompareTo(b.start));
            var outList = new List<(int, int)>();
            var (s0, c0) = DirtyRanges[0];
            for (var i = 1; i < DirtyRanges.Count; i++)
            {
                var (s, c) = DirtyRanges[i];
                if (s <= s0 + c0 + 64) { c0 = Math.Max(c0, s + c - s0); }
                else { outList.Add((s0, c0)); s0 = s; c0 = c; }
            }
            outList.Add((s0, c0));
            DirtyRanges.Clear(); DirtyRanges.AddRange(outList);
        }

        // flats store (flat number + 1) * 8 + class in Light so a picture change is detected cheaply;
        // the shader only uses (Light % 8) = G-buffer class.
        private int PackFlat(int pic) => (pic + 1) * 8 + FlatClass(pic);

        private int FlatClass(int pic) => pic >= 0 && pic < flatLookup.Count && flatLookup[pic] != null ? flatLookup[pic].GClass : 0;

        private void WriteFlat(FlatRun fl, Sector sec, int pic)
        {
            var z = fl.Ceiling ? ceilH[fl.Sector] : floorH[fl.Sector];
            var sky = pic == skyFlat;
            var kind = sky ? RKind.Sky : fl.Ceiling ? RKind.Ceiling : RKind.Floor;
            var slot = sky ? skyTexSlot : atlas.FlatSlot(pic);
            var light = PackFlat(pic);
            for (var i = 0; i < fl.Count; i++)
            {
                ref var vx = ref Vertices[fl.V0 + i];
                vx.Y = z; vx.Kind = kind; vx.Tex = slot; vx.Light = light;
            }
        }

        private void Degenerate(int v0)
        {
            for (var i = 0; i < 4; i++) { ref var vx = ref Vertices[v0 + i]; vx = Vertices[v0]; vx.Tex = -2; }
            // all four at the same position → zero-area triangles (rasterise nothing)
            var p = Vertices[v0];
            for (var i = 1; i < 4; i++) { ref var vx = ref Vertices[v0 + i]; vx.X = p.X; vx.Y = p.Y; vx.Z = p.Z; }
        }

        private void WriteQuad(Quad q)
        {
            var line = map.Lines[q.Line];
            var side = q.Back ? line.BackSide : line.FrontSide;
            var me = q.Back ? line.BackSector : line.FrontSector;
            var other = q.Back ? line.FrontSector : line.BackSector;
            var a = q.Back ? line.Vertex2 : line.Vertex1;
            var b = q.Back ? line.Vertex1 : line.Vertex2;
            float ax = a.X.ToFloat(), ay = a.Y.ToFloat(), bx = b.X.ToFloat(), by = b.Y.ToFloat();
            float dx = bx - ax, dy = by - ay;
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4F || side == null) { Degenerate(q.V0); return; }
            // normal pointing into "me" (the side is on the right of a->b)
            float nx = dy / len, ny = -dx / len;
            float myF = floorH[me.Number], myC = ceilH[me.Number];
            var contrast = ay == by ? -1F : ax == bx ? 1F : 0F;
            var peg = line.Flags;
            var rowOff = side.RowOffset.ToFloat();
            var u0 = side.TextureOffset.ToFloat();

            float bottom, top, anchor; int tex;
            switch (q.Part)
            {
                case Part.Middle:
                {
                    tex = side.MiddleTexture;
                    if (tex == 0) { Degenerate(q.V0); return; }
                    var t = textures[tex];
                    bottom = myF; top = myC;
                    anchor = (peg & LineFlags.DontPegBottom) != 0 ? myF + t.Height : myC;
                    break;
                }
                case Part.Upper:
                case Part.SkyUpper:
                {
                    float oC = ceilH[other.Number], oF = floorH[other.Number];
                    var mySky = me.CeilingFlat == skyFlat; var otherSky = other.CeilingFlat == skyFlat;
                    if (!(oC < myC)) { Degenerate(q.V0); return; }
                    bottom = Math.Max(oC, myF); top = myC;
                    if (top <= bottom) { Degenerate(q.V0); return; }
                    // Sky hack: between two sky ceilings nothing is drawn in vanilla (the sky shows);
                    // a missing upper texture under a sky ceiling is also sky (GZDoom rule).
                    var skyPart = mySky && (otherSky || side.TopTexture == 0);
                    if (skyPart != (q.Part == Part.SkyUpper)) { Degenerate(q.V0); return; }
                    if (skyPart) { WriteSkyQuad(q.V0, ax, ay, bx, by, bottom, top, me.Number); return; }
                    tex = side.TopTexture;
                    if (tex == 0) { Degenerate(q.V0); return; }
                    var t = textures[tex];
                    anchor = (peg & LineFlags.DontPegTop) != 0 ? myC : oC + t.Height;
                    break;
                }
                case Part.Lower:
                case Part.SkyLower:
                {
                    float oF = floorH[other.Number];
                    if (!(oF > myF)) { Degenerate(q.V0); return; }
                    bottom = myF; top = Math.Min(oF, myC);
                    if (top <= bottom) { Degenerate(q.V0); return; }
                    var skyPart = me.FloorFlat == skyFlat && (other.FloorFlat == skyFlat || side.BottomTexture == 0);
                    if (skyPart != (q.Part == Part.SkyLower)) { Degenerate(q.V0); return; }
                    if (skyPart) { WriteSkyQuad(q.V0, ax, ay, bx, by, bottom, top, me.Number); return; }
                    tex = side.BottomTexture;
                    if (tex == 0) { Degenerate(q.V0); return; }
                    anchor = (peg & LineFlags.DontPegBottom) != 0 ? myC : oF;
                    break;
                }
                default: // Masked
                {
                    tex = side.MiddleTexture;
                    if (tex == 0) { Degenerate(q.V0); return; }
                    var t = textures[tex];
                    float oC = ceilH[other.Number], oF = floorH[other.Number];
                    var openBottom = Math.Max(myF, oF); var openTop = Math.Min(myC, oC);
                    anchor = (peg & LineFlags.DontPegBottom) != 0 ? openBottom + t.Height : openTop;
                    var texTop = anchor + rowOff; var texBottom = texTop - t.Height;
                    top = Math.Min(openTop, texTop); bottom = Math.Max(openBottom, texBottom);
                    if (top <= bottom) { Degenerate(q.V0); return; }
                    break;
                }
            }
            var vTop = anchor + rowOff;
            // quad: a-bottom, a-top, b-top, b-bottom (clockwise seen from the front in Unity's left-handed space)
            WriteV(q.V0 + 0, ax, bottom, ay, u0, vTop - bottom, tex, me.Number, nx, ny, contrast);
            WriteV(q.V0 + 1, ax, top, ay, u0, vTop - top, tex, me.Number, nx, ny, contrast);
            WriteV(q.V0 + 2, bx, top, by, u0 + len, vTop - top, tex, me.Number, nx, ny, contrast);
            WriteV(q.V0 + 3, bx, bottom, by, u0 + len, vTop - bottom, tex, me.Number, nx, ny, contrast);
        }

        private void WriteSkyQuad(int v0, float ax, float ay, float bx, float by, float bottom, float top, int sector)
        {
            for (var i = 0; i < 4; i++)
            {
                var x = i < 2 ? ax : bx; var y = i < 2 ? ay : by;
                var z = (i == 0 || i == 3) ? bottom : top;
                Vertices[v0 + i] = new RVertex { X = x, Y = z, Z = y, Tex = skyTexSlot, Sector = sector, Kind = RKind.Sky };
            }
        }

        private void WriteV(int i, float x, float z, float y, float u, float v, int tex, int sector, float nx, float ny, float contrast)
        {
            Vertices[i] = new RVertex { X = x, Y = z, Z = y, U = u, V = v, Tex = tex, Sector = sector, Nx = nx, Nz = ny, Kind = RKind.Wall, Light = contrast };
        }
    }
}
