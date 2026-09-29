// my-rekkr dev4 — "carved stone" UI skin helpers: REKKR's own pixel font (STCFN033–095 + 121, read
// from the WAD at runtime, nothing copied into the repo) drawn with IMGUI, in three tints (bone =
// original colours, red = REKKR title red, dim = disabled/hints), and 9-slice stone frames.
// Arabic (and any non-ASCII text) falls back to the TTF with a dark outline in the same colours.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ManagedDoom.UnityPort
{
    public enum Ink { Bone, Red, Dim, Dark }

    public sealed class RekkrSkin
    {
        private readonly Texture2D[] atlas = new Texture2D[4];
        private readonly Rect[] uv = new Rect[128];
        private readonly int[] gw = new int[128];
        private readonly int[] gtop = new int[128];
        private int glyphH = 8;
        private int cellW, cellH;
        public bool Ready { get; private set; }

        public GUIStyle Panel, Plate, PlateOn;
        private GUIStyle ttf;
        private static readonly Color[] inkTtf =
        {
            new Color(1F, 0.93F, 0.72F), new Color(0.86F, 0.14F, 0.08F), new Color(0.68F, 0.7F, 0.68F), new Color(0.08F, 0.05F, 0.03F)
        };

        public void InitFrames(Font fallback)
        {
            ttf = new GUIStyle { font = fallback, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            Panel = NineSlice("Rekkr/UI/panel", 48);
            Plate = NineSlice("Rekkr/UI/plate", 20);
            PlateOn = NineSlice("Rekkr/UI/plate_on", 20);
        }

        private static GUIStyle NineSlice(string res, int border)
        {
            var t = Resources.Load<Texture2D>(res);
            return new GUIStyle { normal = { background = t }, border = new RectOffset(border, border, border, border) };
        }

        /// <summary>Builds the font atlases from the WAD's STCFN patches (PLAYPAL palette 0).</summary>
        public void InitFont(Wad wad, Font fallback)
        {
            try
            {
                var pal = wad.ReadLump("PLAYPAL");
                var glyphs = new Dictionary<int, (int w, int h, int top, Color32[] px)>();
                int maxW = 1, maxH = 1;
                for (var c = 33; c < 128; c++)
                {
                    var name = "STCFN" + c.ToString("000");
                    if (wad.GetLumpNumber(name) < 0) continue;
                    var g = Decode(wad.ReadLump(name), pal);
                    glyphs[c] = g;
                    maxW = Math.Max(maxW, g.w); maxH = Math.Max(maxH, g.h);
                }
                if (!glyphs.ContainsKey('A')) return;
                glyphH = glyphs['A'].h;
                cellW = maxW + 2; cellH = maxH + 2;
                const int cols = 16; var rows = (128 - 32 + cols - 1) / cols;
                int aw = cols * cellW, ah = rows * cellH;
                var base0 = new Color32[aw * ah];
                foreach (var kv in glyphs)
                {
                    var i = kv.Key - 32; int cx = (i % cols) * cellW + 1, cy = (i / cols) * cellH + 1;
                    var g = kv.Value;
                    for (var y = 0; y < g.h; y++)
                        for (var x = 0; x < g.w; x++)
                        {
                            // atlas rows go bottom-up in Unity
                            base0[(ah - 1 - (cy + y)) * aw + cx + x] = g.px[y * g.w + x];
                        }
                    uv[kv.Key] = new Rect((float)cx / aw, 1F - (float)(cy + g.h) / ah, (float)g.w / aw, (float)g.h / ah);
                    gw[kv.Key] = g.w; gtop[kv.Key] = g.top;
                }
                for (var k = 0; k < 4; k++)
                {
                    var px = new Color32[base0.Length];
                    for (var i = 0; i < px.Length; i++) px[i] = Tint(base0[i], (Ink)k);
                    var t = new Texture2D(aw, ah, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    t.SetPixels32(px); t.Apply(false, true);
                    atlas[k] = t;
                }
                Ready = true;
            }
            catch (Exception e) { Debug.LogWarning("[REKKR] skin font: " + e.Message); }
        }

        private static (int w, int h, int top, Color32[] px) Decode(byte[] d, byte[] pal)
        {
            int w = BitConverter.ToInt16(d, 0), h = BitConverter.ToInt16(d, 2), top = BitConverter.ToInt16(d, 6);
            var px = new Color32[w * h];
            for (var x = 0; x < w; x++)
            {
                var p = BitConverter.ToInt32(d, 8 + 4 * x);
                while (p < d.Length && d[p] != 255)
                {
                    int y0 = d[p], len = d[p + 1];
                    for (var j = 0; j < len && y0 + j < h; j++)
                    {
                        var c = d[p + 3 + j];
                        px[(y0 + j) * w + x] = new Color32(pal[3 * c], pal[3 * c + 1], pal[3 * c + 2], 255);
                    }
                    p += len + 4;
                }
            }
            return (w, h, top, px);
        }

        private static Color32 Tint(Color32 c, Ink ink)
        {
            if (c.a == 0) return c;
            var l = (0.3F * c.r + 0.59F * c.g + 0.11F * c.b) / 255F;
            switch (ink)
            {
                case Ink.Red:   // REKKR logo blood red, keeps the dark outline
                    return new Color32((byte)Mathf.Clamp(l * 330F, 0, 255), (byte)Mathf.Clamp(l * l * 90F, 0, 255), (byte)Mathf.Clamp(l * l * 60F, 0, 255), 255);
                case Ink.Dim:
                    return new Color32((byte)(l * 175), (byte)(l * 182), (byte)(l * 178), 255);
                case Ink.Dark:
                    return new Color32((byte)(40 * (1 - l) + 20), (byte)(26 * (1 - l) + 12), (byte)(16 * (1 - l) + 6), 255);
            }
            return c;
        }

        private static bool Ascii(string s)
        {
            foreach (var ch in s) if (ch > 126) return false;
            return true;
        }

        private static char Map(char ch)
        {
            if (ch >= 'a' && ch <= 'z') return (char)(ch - 32);
            if (ch == '–' || ch == '—') return '-';
            return ch;
        }

        /// <summary>Text width in pixels at a given cap height (pixel font) — integer glyph scale.</summary>
        public float Width(string s, float px)
        {
            var k = Scale(px);
            float w = 0;
            foreach (var raw in s)
            {
                var ch = Map(raw);
                if (ch == ' ' || ch >= 128 || gw[ch] == 0) w += 4 * k;
                else w += gw[ch] * k;
            }
            return w;
        }

        private float Scale(float px) => Mathf.Max(1, Mathf.Round(px / glyphH));

        /// <summary>Draws text inside r. px = desired glyph height in screen pixels.</summary>
        public void Text(Rect r, string s, float px, TextAnchor align, Ink ink, float alpha = 1F)
        {
            if (string.IsNullOrEmpty(s) || Event.current.type != UnityEngine.EventType.Repaint) return;
            if (!Ready || !Ascii(s)) { TtfText(r, s, px * 1.25F, align, ink, alpha); return; }
            var k = Scale(px);
            var w = Width(s, px);
            if (w > r.width && k > 1) { k = Mathf.Max(1, Mathf.Floor(k * r.width / w)); w = Width(s, k * glyphH); }
            var h = glyphH * k;
            float x = align == TextAnchor.MiddleLeft || align == TextAnchor.UpperLeft || align == TextAnchor.LowerLeft ? r.x
                : align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight || align == TextAnchor.LowerRight ? r.xMax - w
                : r.x + (r.width - w) / 2;
            float y = align == TextAnchor.UpperLeft || align == TextAnchor.UpperCenter || align == TextAnchor.UpperRight ? r.y
                : align == TextAnchor.LowerLeft || align == TextAnchor.LowerCenter || align == TextAnchor.LowerRight ? r.yMax - h
                : r.y + (r.height - h) / 2;
            x = Mathf.Round(x); y = Mathf.Round(y);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha * old.a);
            var tex = atlas[(int)ink];
            foreach (var raw in s)
            {
                var ch = Map(raw);
                if (ch == ' ' || ch >= 128 || gw[ch] == 0) { x += 4 * k; continue; }
                var gwpx = gw[ch] * k;
                var gh = uv[ch].height * tex.height * k;
                GUI.DrawTextureWithTexCoords(new Rect(x, y - gtop[ch] * k, gwpx, gh), tex, uv[ch]);
                x += gwpx;
            }
            GUI.color = old;
        }

        /// <summary>Word-wrapped pixel text (hints); returns the height used.</summary>
        public float Wrapped(Rect r, string s, float px, Ink ink, bool rtl)
        {
            if (!Ready || !Ascii(s))
            {
                ttf.wordWrap = true;
                TtfText(r, s, px * 1.2F, rtl ? TextAnchor.UpperRight : TextAnchor.UpperLeft, ink, 1F);
                ttf.wordWrap = false;
                return px * 1.6F;
            }
            var words = s.Split(' ');
            var line = ""; var y = r.y; var lh = Scale(px) * glyphH * 1.35F;
            foreach (var wd in words)
            {
                var t = line.Length == 0 ? wd : line + " " + wd;
                if (Width(t, px) > r.width && line.Length > 0)
                {
                    Text(new Rect(r.x, y, r.width, lh), line, px, rtl ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, ink);
                    y += lh; line = wd;
                }
                else line = t;
            }
            if (line.Length > 0) { Text(new Rect(r.x, y, r.width, lh), line, px, rtl ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, ink); y += lh; }
            return y - r.y;
        }

        private void TtfText(Rect r, string s, float size, TextAnchor align, Ink ink, float alpha)
        {
            if (ttf == null) return;
            ttf.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
            ttf.alignment = align;
            var o = Mathf.Max(1F, size * 0.07F);
            var old = GUI.color;
            ttf.normal.textColor = new Color(0.03F, 0.03F, 0.04F, 0.9F * alpha);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x - o, r.y, r.width, r.height), s, ttf);
            GUI.Label(new Rect(r.x + o, r.y, r.width, r.height), s, ttf);
            GUI.Label(new Rect(r.x, r.y - o, r.width, r.height), s, ttf);
            GUI.Label(new Rect(r.x, r.y + o * 1.6F, r.width, r.height), s, ttf);
            var c = inkTtf[(int)ink]; c.a = alpha;
            ttf.normal.textColor = c;
            GUI.Label(r, s, ttf);
            GUI.color = old;
        }

        public void Frame(GUIStyle st, Rect r)
        {
            if (Event.current.type == UnityEngine.EventType.Repaint && st != null && st.normal.background != null) st.Draw(r, false, false, false, false);
        }
    }
}
