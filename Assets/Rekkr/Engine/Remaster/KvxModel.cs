// my-rekkr dev6 stage 7 — optional voxel models in the Build/KVX format (the one GZDoom uses for Doom
// voxel packs) plus a small mapping file, so community packs can be dropped next to the WAD
// (persistentDataPath/voxels/) without rebuilding the app. No REKKR voxel pack exists today.
//
// KVX (mip level 0 only): int32 numbytes; int32 xsiz, ysiz, zsiz; int32 xpivot, ypivot, zpivot (8.8 fixed);
// int32 xoffset[xsiz+1]; int16 xyoffset[xsiz][ysiz+1]; slabs {byte ztop, zleng, cull, col[zleng]};
// the palette is the last 768 bytes of the file (6-bit RGB). x = right, y = front→back, z = top→down.
//
// voxels.txt, one mapping per line ('#' comments):
//   <SPRITE><FRAME> = <file.kvx> [scale=<f>] [angle=<degrees>] [spin=<deg per s>]
//   e.g.  BAR1A = barrel.kvx scale=1.0 angle=90       (SPRITE = 4 chars, FRAME = letter; all rotations)
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ManagedDoom.Remaster
{
    public sealed class KvxModel
    {
        public int SizeX, SizeY, SizeZ;
        public float PivotX, PivotY, PivotZ;
        public byte[] Voxels;   // palette index + 1 (0 = empty), [x, y, z] = (x * SizeY + y) * SizeZ + z
        public byte this[int x, int y, int z] => x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ ? (byte)0 : Voxels[(x * SizeY + y) * SizeZ + z];

        /// <summary>Parses a KVX file; colours are remapped to the nearest colour of <paramref name="doomPalette"/>
        /// (768 bytes, 8-bit RGB) so COLORMAP lighting works like for sprites.</summary>
        public static KvxModel Load(byte[] data, byte[] doomPalette)
        {
            if (data == null || data.Length < 28 + 768) throw new InvalidDataException("KVX too short");
            int I32(int o) => BitConverter.ToInt32(data, o);
            var m = new KvxModel { SizeX = I32(4), SizeY = I32(8), SizeZ = I32(12) };
            if (m.SizeX <= 0 || m.SizeY <= 0 || m.SizeZ <= 0 || m.SizeX > 256 || m.SizeY > 256 || m.SizeZ > 256) throw new InvalidDataException("KVX size");
            m.PivotX = I32(16) / 256F; m.PivotY = I32(20) / 256F; m.PivotZ = I32(24) / 256F;
            var xoffsetsAt = 28;
            var xyAt = xoffsetsAt + 4 * (m.SizeX + 1);
            var slabsAt = xyAt + 2 * m.SizeX * (m.SizeY + 1);
            var baseOffset = I32(xoffsetsAt);   // xoffset[0] is relative to the start of the xoffset table
            m.Voxels = new byte[m.SizeX * m.SizeY * m.SizeZ];
            // palette remap (KVX palette is 6-bit)
            var pal = new byte[256];
            var palAt = data.Length - 768;
            for (var i = 0; i < 256; i++)
            {
                int r = data[palAt + 3 * i] * 255 / 63, g = data[palAt + 3 * i + 1] * 255 / 63, b = data[palAt + 3 * i + 2] * 255 / 63;
                var best = 0; var bd = int.MaxValue;
                for (var j = 0; j < 256; j++)
                {
                    int dr = r - doomPalette[3 * j], dg = g - doomPalette[3 * j + 1], db = b - doomPalette[3 * j + 2];
                    var d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                    if (d < bd) { bd = d; best = j; }
                }
                pal[i] = (byte)best;
            }
            for (var x = 0; x < m.SizeX; x++)
            {
                var colStart = slabsAt + (I32(xoffsetsAt + 4 * x) - baseOffset);
                for (var y = 0; y < m.SizeY; y++)
                {
                    int xy0 = BitConverter.ToInt16(data, xyAt + 2 * (x * (m.SizeY + 1) + y));
                    int xy1 = BitConverter.ToInt16(data, xyAt + 2 * (x * (m.SizeY + 1) + y + 1));
                    var p = colStart + (xy0 & 0xFFFF); var end = colStart + (xy1 & 0xFFFF);
                    while (p + 3 <= end && p + 3 <= palAt)
                    {
                        int ztop = data[p], zleng = data[p + 1];
                        p += 3;
                        for (var k = 0; k < zleng && p < palAt; k++, p++)
                        {
                            var z = ztop + k;
                            if (z < m.SizeZ) m.Voxels[(x * m.SizeY + y) * m.SizeZ + z] = (byte)(pal[data[p]] + 1);
                        }
                    }
                }
            }
            return m;
        }

        /// <summary>Writes a KVX (used by tests to make a self-authored, public-domain model).</summary>
        public static byte[] Write(int sx, int sy, int sz, Func<int, int, int, int> colour, byte[] palette6)
        {
            var slabs = new List<byte>[sx, sy];
            for (var x = 0; x < sx; x++)
                for (var y = 0; y < sy; y++)
                {
                    var l = new List<byte>(); slabs[x, y] = l;
                    var z = 0;
                    while (z < sz)
                    {
                        if (colour(x, y, z) < 0) { z++; continue; }
                        var z0 = z; while (z < sz && colour(x, y, z) >= 0 && z - z0 < 255) z++;
                        l.Add((byte)z0); l.Add((byte)(z - z0)); l.Add(0x3F);
                        for (var k = z0; k < z; k++) l.Add((byte)colour(x, y, k));
                    }
                }
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write(0); w.Write(sx); w.Write(sy); w.Write(sz);
            w.Write(sx * 128); w.Write(sy * 128); w.Write(sz * 256);
            var xoffBase = 4 * (sx + 1) + 2 * sx * (sy + 1);
            var off = xoffBase;
            for (var x = 0; x <= sx; x++)
            {
                w.Write(off);
                if (x < sx) for (var y = 0; y < sy; y++) off += slabs[x, y].Count;
            }
            for (var x = 0; x < sx; x++)
            {
                short o = 0;
                for (var y = 0; y <= sy; y++) { w.Write(o); if (y < sy) o += (short)slabs[x, y].Count; }
            }
            for (var x = 0; x < sx; x++) for (var y = 0; y < sy; y++) w.Write(slabs[x, y].ToArray());
            w.Write(palette6);
            w.Flush();
            var bytes = ms.ToArray();
            BitConverter.GetBytes(bytes.Length - 4 - 768).CopyTo(bytes, 0);
            return bytes;
        }

        /// <summary>Face-culled voxel mesh. Mesh space like SpriteExtruder: x right, y up (0 = model bottom),
        /// z forward (away from the thing's front); origin at the pivot column. Colour = palette index in U
        /// (Tex = -3: no atlas lookup).</summary>
        public ExtrudedMesh Mesh(float scale)
        {
            var verts = new List<RVertex>(4096); var idx = new List<int>(6144);
            float px = PivotX, py = PivotY;
            void Face(float x, float y, float z, int axis, int sign, byte c)
            {
                // unit square on the face of voxel (x,y,z) in model coords; axis 0 = x, 1 = y (depth), 2 = z (vertical)
                var b = verts.Count;
                var n = new float[3]; n[axis] = sign;
                for (var k = 0; k < 4; k++)
                {
                    float a = (k == 1 || k == 2) ? 1 : 0, bb = (k >= 2) ? 1 : 0;
                    float vx = x, vy = y, vz = z;
                    if (axis == 0) { vx += sign > 0 ? 1 : 0; vy += a; vz += bb; }
                    else if (axis == 1) { vy += sign > 0 ? 1 : 0; vx += a; vz += bb; }
                    else { vz += sign > 0 ? 1 : 0; vx += a; vy += bb; }
                    // model -> mesh: x right, y up (model z grows downwards), z = model y (front -> back)
                    verts.Add(new RVertex { X = (vx - px) * scale, Y = (SizeZ - vz) * scale, Z = (vy - py) * scale, U = c - 1, V = 0, Tex = -3, Sector = -1,
                                            Nx = n[0], Nz = n[1], Light = -n[2], Kind = RKind.Thing });
                }
                idx.Add(b); idx.Add(b + 1); idx.Add(b + 2); idx.Add(b); idx.Add(b + 2); idx.Add(b + 3);
            }
            for (var x = 0; x < SizeX; x++)
                for (var y = 0; y < SizeY; y++)
                    for (var z = 0; z < SizeZ; z++)
                    {
                        var c = this[x, y, z];
                        if (c == 0) continue;
                        if (this[x - 1, y, z] == 0) Face(x, y, z, 0, -1, c);
                        if (this[x + 1, y, z] == 0) Face(x, y, z, 0, 1, c);
                        if (this[x, y - 1, z] == 0) Face(x, y, z, 1, -1, c);
                        if (this[x, y + 1, z] == 0) Face(x, y, z, 1, 1, c);
                        if (this[x, y, z - 1] == 0) Face(x, y, z, 2, -1, c);
                        if (this[x, y, z + 1] == 0) Face(x, y, z, 2, 1, c);
                    }
            return new ExtrudedMesh { Vertices = verts.ToArray(), Indices = idx.ToArray() };
        }
    }

    /// <summary>voxels.txt mapping (see file header).</summary>
    public sealed class VoxelMap
    {
        public struct Entry { public string File; public float Scale, Angle, Spin; }
        public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static VoxelMap Parse(string text)
        {
            var map = new VoxelMap();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw; var hash = line.IndexOf('#'); if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim(); if (line.Length == 0) continue;
                var eq = line.IndexOf('='); if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var parts = line.Substring(eq + 1).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || key.Length != 5) continue;
                var e = new Entry { File = parts[0], Scale = 1, Angle = 0, Spin = 0 };
                for (var i = 1; i < parts.Length; i++)
                {
                    var kv = parts[i].Split('='); if (kv.Length != 2) continue;
                    if (!float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) continue;
                    switch (kv[0].ToLowerInvariant()) { case "scale": e.Scale = f; break; case "angle": e.Angle = f; break; case "spin": e.Spin = f; break; }
                }
                map.Entries[key.ToUpperInvariant()] = e;
            }
            return map;
        }
    }
}
