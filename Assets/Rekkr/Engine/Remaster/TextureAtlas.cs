// my-rekkr dev6 — one palette-index atlas for every wall texture, flat and sprite patch of the WAD.
// Texel = 2 bytes: R = palette index, G = 255 opaque / 0 transparent. For transparent texels R holds the
// nearest opaque texel of the column (or of the nearest non-empty column), so walls that use a texture
// with holes as a solid surface stay closed (same idea as Texture.SolidColumns); masked mid-textures and
// sprites use G for alpha-test. Lighting (COLORMAP) and the palette are applied in the shader, so the
// look is the original 256-colour art, with damage/bonus palettes and fixed colormaps working as in vanilla.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;

namespace ManagedDoom.Remaster
{
    public sealed class TextureAtlas
    {
        public struct Rect { public int X, Y, W, H; }

        public readonly int Width, Height;
        public readonly byte[] Data;          // RG8, row-major, Width*Height*2
        public readonly Rect[] Rects;         // per slot (texture number, then flats, then sprite patches)
        public readonly int TextureCount, FlatCount;
        public int FlatSlot(int flat) => TextureCount + flat;
        public int SpriteSlot(Patch p) => spriteSlots.TryGetValue(p, out var s) ? s : -1;
        public int SlotCount => Rects.Length;
        public long UsedTexels { get; }

        private readonly Dictionary<Patch, int> spriteSlots = new Dictionary<Patch, int>();

        public TextureAtlas(GameContent content, int maxWidth = 4096)
        {
            var textures = content.Textures; var flats = content.Flats;
            TextureCount = textures.Count; FlatCount = flats.Count;
            // Unique sprite patches (every frame / rotation of every sprite in the WAD).
            var spritePatches = new List<Patch>();
            foreach (Sprite s in Enum.GetValues(typeof(Sprite)))
            {
                if (s == Sprite.Count) continue;
                SpriteDef def;
                try { def = content.Sprites[s]; } catch { continue; }
                if (def == null) continue;
                foreach (var f in def.Frames)
                {
                    if (f == null) continue;
                    foreach (var p in f.Patches)
                        if (p != null && !spriteSlots.ContainsKey(p)) { spriteSlots[p] = TextureCount + FlatCount + spritePatches.Count; spritePatches.Add(p); }
                }
            }
            var n = TextureCount + FlatCount + spritePatches.Count;
            Rects = new Rect[n];
            var sizes = new (int w, int h)[n];
            for (var i = 0; i < TextureCount; i++) sizes[i] = (Math.Max(1, textures[i].Width), Math.Max(1, textures[i].Height));
            for (var i = 0; i < FlatCount; i++) sizes[TextureCount + i] = flats[i] != null ? (64, 64) : (1, 1);
            for (var i = 0; i < spritePatches.Count; i++) sizes[TextureCount + FlatCount + i] = (Math.Max(1, spritePatches[i].Width), Math.Max(1, spritePatches[i].Height));

            // Shelf packing, tallest first.
            var order = new int[n];
            for (var i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) => sizes[b].h != sizes[a].h ? sizes[b].h.CompareTo(sizes[a].h) : sizes[b].w.CompareTo(sizes[a].w));
            int x = 0, y = 0, shelfH = 0; long used = 0;
            foreach (var i in order)
            {
                var (w, h) = sizes[i];
                if (x + w > maxWidth) { y += shelfH; x = 0; shelfH = 0; }
                Rects[i] = new Rect { X = x, Y = y, W = w, H = h };
                x += w; if (h > shelfH) shelfH = h; used += (long)w * h;
            }
            Width = maxWidth; Height = Math.Max(1, y + shelfH);
            Height = (Height + 63) & ~63;
            UsedTexels = used;
            Data = new byte[Width * Height * 2];

            var col = new byte[1024]; var alpha = new bool[1024];
            for (var i = 0; i < TextureCount; i++) BlitColumns(textures[i].Composite.Columns, Rects[i], col, alpha);
            for (var i = 0; i < FlatCount; i++)
            {
                var f = flats[i]; if (f == null) continue;
                var r = Rects[TextureCount + i];
                for (var fy = 0; fy < 64; fy++)
                    for (var fx = 0; fx < 64; fx++)
                    {
                        var o = 2 * ((r.Y + fy) * Width + r.X + fx);
                        Data[o] = f.Data[fy * 64 + fx]; Data[o + 1] = 255;
                    }
            }
            for (var i = 0; i < spritePatches.Count; i++) BlitColumns(spritePatches[i].Columns, Rects[TextureCount + FlatCount + i], col, alpha);
        }

        private void BlitColumns(Column[][] columns, Rect r, byte[] col, bool[] alpha)
        {
            int w = r.W, h = r.H;
            if (col.Length < h) { col = new byte[h]; alpha = new bool[h]; }
            var colHas = new bool[w];
            var cols = new byte[w][];
            for (var cx = 0; cx < w && cx < columns.Length; cx++)
            {
                Array.Clear(alpha, 0, h);
                var any = false;
                foreach (var post in columns[cx])
                {
                    for (var k = 0; k < post.Length; k++)
                    {
                        var yy = post.TopDelta + k;
                        if (yy < 0 || yy >= h) continue;
                        col[yy] = post.Data[post.Offset + k]; alpha[yy] = true; any = true;
                    }
                }
                colHas[cx] = any;
                var c = new byte[h];
                if (any)
                {
                    // fill transparent texels from the nearest opaque texel above (else below)
                    int last = -1;
                    for (var yy = 0; yy < h; yy++) if (alpha[yy]) { last = yy; break; }
                    byte fill = col[last];
                    for (var yy = 0; yy < h; yy++) { if (alpha[yy]) fill = col[yy]; c[yy] = fill; }
                }
                cols[cx] = c;
                for (var yy = 0; yy < h; yy++)
                {
                    var o = 2 * ((r.Y + yy) * Width + r.X + cx);
                    Data[o + 1] = alpha[yy] ? (byte)255 : (byte)0;
                }
            }
            // empty columns borrow the nearest non-empty column (solid use only; alpha stays 0)
            for (var cx = 0; cx < w; cx++)
            {
                var src = cx;
                if (!colHas[cx])
                {
                    src = -1;
                    for (var d = 1; d < w && src < 0; d++)
                    {
                        if (colHas[((cx - d) % w + w) % w]) src = ((cx - d) % w + w) % w;
                        else if (colHas[(cx + d) % w]) src = (cx + d) % w;
                    }
                }
                if (src < 0 || cols[src] == null) continue;
                var c = cols[src];
                for (var yy = 0; yy < h; yy++) Data[2 * ((r.Y + yy) * Width + r.X + cx)] = c[yy];
            }
        }
    }
}
