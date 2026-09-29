#!/usr/bin/env python3
"""Build Assets/Rekkr/Resources/Rekkr/UI/RekkrArabic.ttf for the IMGUI settings screen.
Unity IMGUI does no text shaping, so the app shapes Arabic itself (ArabicShaper.cs) into
Presentation Forms-B; this font merges Noto Sans Arabic Bold (which has those forms) with
Noto Sans Bold's Latin/punctuation. Both fonts: SIL Open Font License 1.1.
Run: uv run --with fonttools python tools/make_arabic_font.py"""
import os
from fontTools.ttLib import TTFont
from fontTools.merge import Merger, Options
from fontTools import subset

NOTO = "/usr/share/fonts/truetype/noto/"
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets/Rekkr/Resources/Rekkr/UI/RekkrArabic.ttf")

def prep(src, dst, unicodes):
    f = TTFont(src)
    o = subset.Options(); o.layout_features = []; o.drop_tables += ["GSUB", "GPOS", "GDEF", "meta", "DSIG"]
    o.name_IDs = ["*"]; o.notdef_outline = True; o.hinting = False
    s = subset.Subsetter(o); s.populate(unicodes=unicodes); s.subset(f)
    for t in ("GSUB", "GPOS", "GDEF"):
        if t in f: del f[t]
    f.save(dst)

lat = list(range(0x20, 0x7F)) + [0xA0, 0xB0, 0xD7, 0x2013, 0x2014, 0x2022, 0x2026, 0x2190, 0x2192]
ara = list(range(0x600, 0x700)) + list(range(0xFB50, 0xFE00)) + list(range(0xFE70, 0xFF00))
prep(NOTO + "NotoSans-Bold.ttf", "/tmp/rk_lat.ttf", lat)
prep(NOTO + "NotoSansArabic-Bold.ttf", "/tmp/rk_ara.ttf", ara)
f = Merger(options=Options(drop_tables=["vmtx", "vhea"])).merge(["/tmp/rk_lat.ttf", "/tmp/rk_ara.ttf"])
f["name"].setName("RekkrUI Arabic", 1, 3, 1, 0x409)
f["name"].setName("RekkrUIArabic-Bold", 6, 3, 1, 0x409)
f.save(OUT)
print("wrote", OUT, os.path.getsize(OUT))
