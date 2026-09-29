#!/usr/bin/env python3
"""Launcher icon from REKKR's own art: the status-bar viking face (STFST01), pixel-scaled
on a dark disc with the gold ring used by the touch controls. Also exports lump previews.
Usage: uv run --with Pillow python tools/make_icon.py Assets/StreamingAssets/rekkr.wad"""
import struct, sys, os
from PIL import Image, ImageDraw
wad = open(sys.argv[1], "rb").read()
_, n, o = struct.unpack("<4sii", wad[:12])
L = {}
for i in range(n):
    fp, sz = struct.unpack("<ii", wad[o+16*i:o+16*i+8])
    L[wad[o+16*i+8:o+16*i+16].split(b"\0")[0].decode("latin1").upper()] = wad[fp:fp+sz]
pal = L["PLAYPAL"][:768]
def patch(name):
    d = L[name]; w, h, lo, to = struct.unpack("<hhhh", d[:8])
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0)); px = img.load()
    for x in range(w):
        p = struct.unpack("<i", d[8+4*x:12+4*x])[0]
        while d[p] != 255:
            top, ln = d[p], d[p+1]
            for j in range(ln):
                c = d[p+3+j]; px[x, top+j] = (pal[3*c], pal[3*c+1], pal[3*c+2], 255)
            p += ln + 4
    return img
out = os.path.join(os.path.dirname(__file__), "..", "Assets/Rekkr/Icon")
os.makedirs(out, exist_ok=True)
S = 1024
bg = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(bg)
for i in range(120):
    t = i / 119; r = (S/2 - 8) * (1 - t)
    d.ellipse([S/2-r, S/2-r, S/2+r, S/2+r], fill=(int(20+50*t), int(15+36*t), int(10+22*t), 255))
d.ellipse([12, 12, S-12, S-12], outline=(222, 178, 92, 255), width=34)
face = patch("STFST01")
k = int((S * 0.62) / max(face.size)); face = face.resize((face.width*k, face.height*k), Image.NEAREST)
bg.alpha_composite(face, ((S-face.width)//2, (S-face.height)//2 + 10))
bg.resize((512, 512), Image.LANCZOS).save(os.path.join(out, "icon.png"))
# Full-bleed square variant for adaptive icon background/foreground.
sq = Image.new("RGBA", (S, S), (26, 19, 12, 255)); sq.alpha_composite(face, ((S-face.width)//2, (S-face.height)//2))
sq.resize((432, 432), Image.LANCZOS).save(os.path.join(out, "icon_square.png"))
print("icon ok", face.size)
