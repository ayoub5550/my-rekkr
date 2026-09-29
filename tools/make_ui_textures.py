#!/usr/bin/env python3
"""Render the touch-control textures (anti-aliased, 4x supersampled).
Icons: Material Design Icons webfont (SIL OFL 1.1 font, CC BY icons, Austin Andrews/Google).
Labels: Lato Black (SIL OFL 1.1). Run: uv run --with Pillow python tools/make_ui_textures.py"""
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import math, os
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets/Rekkr/Resources/Rekkr/UI")
MDI = "/usr/share/fonts/truetype/materialdesignicons-webfont/materialdesignicons-webfont.ttf"
LATO = "/usr/share/fonts/truetype/lato/Lato-Black.ttf"
ARABIC = "/usr/share/fonts/truetype/noto/NotoSansArabic-Bold.ttf"  # SIL OFL 1.1; shaped with libraqm
S = 256; K = 4; N = S * K
GOLD = (222, 178, 92); GOLD_HI = (255, 222, 140); DARK = (18, 13, 9)

def down(img): return img.resize((S, S), Image.LANCZOS)

def disc(pressed=False):
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = N / 2; r = N / 2 - 6 * K
    for i in range(80):  # radial gradient fill
        t = i / 79; rr = r * (1 - t)
        if pressed:
            col = (int(120 + 90 * t), int(88 + 70 * t), int(40 + 40 * t), int(215))
        else:
            col = (int(DARK[0] + 34 * t), int(DARK[1] + 28 * t), int(DARK[2] + 20 * t), int(150 + 20 * t))
        d.ellipse([c - rr, c - rr, c + rr, c + rr], fill=col)
    ring = 7 * K
    d.ellipse([c - r, c - r, c + r, c + r], outline=GOLD_HI if pressed else GOLD, width=ring)
    d.ellipse([c - r + ring + 3 * K, c - r + ring + 3 * K, c + r - ring - 3 * K, c + r - ring - 3 * K],
              outline=(255, 235, 190, 60), width=2 * K)
    return down(img)

def glyph(cp, label=None, size=0.52, arabic=False):
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    f = ImageFont.truetype(MDI, int(N * (size if not label else size * 0.86)))
    ch = chr(cp)
    bb = d.textbbox((0, 0), ch, font=f)
    w, h = bb[2] - bb[0], bb[3] - bb[1]
    cy = N * (0.44 if label else 0.5)
    pos = (N / 2 - w / 2 - bb[0], cy - h / 2 - bb[1])
    shadow = Image.new("RGBA", (N, N), (0, 0, 0, 0)); ds = ImageDraw.Draw(shadow)
    ds.text((pos[0], pos[1] + 5 * K), ch, font=f, fill=(0, 0, 0, 170))
    if label:
        if arabic:
            lf = ImageFont.truetype(ARABIC, int(N * (0.13 if len(label) < 7 else 0.105)), layout_engine=ImageFont.Layout.RAQM)
            kw = {"direction": "rtl", "language": "ar"}
        else:
            lf = ImageFont.truetype(LATO, int(N * 0.105)); kw = {}
        lb = d.textbbox((0, 0), label, font=lf, **kw)
        lp = (N / 2 - (lb[2] - lb[0]) / 2 - lb[0], N * 0.74 - (lb[3] - lb[1]) / 2 - lb[1])
        ds.text((lp[0], lp[1] + 4 * K), label, font=lf, fill=(0, 0, 0, 170), **kw)
    shadow = shadow.filter(ImageFilter.GaussianBlur(6 * K))
    img = Image.alpha_composite(img, shadow); d = ImageDraw.Draw(img)
    d.text(pos, ch, font=f, fill=(255, 255, 255, 255))
    if label:
        d.text(lp, label, font=lf, fill=(255, 255, 255, 235), **kw)
    return down(img)

def stick_base():
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    c = N / 2; r = N / 2 - 6 * K
    d.ellipse([c - r, c - r, c + r, c + r], fill=(12, 9, 6, 110), outline=GOLD + (200,), width=5 * K)
    r2 = r * 0.62
    d.ellipse([c - r2, c - r2, c + r2, c + r2], outline=(255, 235, 190, 45), width=2 * K)
    for a in range(4):  # direction notches
        ang = a * math.pi / 2; tip = r - 16 * K; base = r - 40 * K; wdt = 16 * K
        px, py = math.cos(ang), math.sin(ang); qx, qy = -py, px
        d.polygon([(c + px * tip, c + py * tip), (c + px * base + qx * wdt, c + py * base + qy * wdt),
                   (c + px * base - qx * wdt, c + py * base - qy * wdt)], fill=GOLD + (190,))
    return down(img)

def stick_knob():
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    c = N / 2; r = N / 2 - 10 * K
    for i in range(60):
        t = i / 59; rr = r * (1 - t)
        d.ellipse([c - rr, c - rr, c + rr, c + rr], fill=(int(150 + 90 * t), int(112 + 80 * t), int(52 + 60 * t), 235))
    d.ellipse([c - r, c - r, c + r, c + r], outline=(255, 240, 200, 255), width=5 * K)
    return down(img)

os.makedirs(OUT, exist_ok=True)
disc(False).save(f"{OUT}/btn.png"); disc(True).save(f"{OUT}/btn_pressed.png")
stick_base().save(f"{OUT}/stick_base.png"); stick_knob().save(f"{OUT}/stick_knob.png")
icons = {
    "fire": (0xF4E5, "ATTACK"), "use": (0xF2C7, "USE"), "wnext": (0xF13E, "WEAPON"), "wprev": (0xF13D, "WEAPON"),
    "map": (0xF34D, "MAP"), "menu": (0xF35C, None), "run": (0xF46E, "RUN"), "save": (0xF193, "SAVE"),
    "up": (0xF05E, None), "down": (0xF046, None), "left": (0xF04E, None), "right": (0xF055, None),
    "ok": (0xF12C, "OK"), "settings": (0xF493, None), "back": (0xF54C, "BACK"),
    "qsave": (0xF193, "QUICK SAVE"), "qload": (0xF2DA, "QUICK LOAD"), "move": (0xF1B6, None), "play": (0xF40A, None),
}
arabic = {
    "fire": "هجوم", "use": "استخدام", "wnext": "سلاح", "wprev": "سلاح", "map": "خريطة", "run": "ركض",
    "save": "حفظ", "ok": "موافق", "back": "رجوع", "qsave": "حفظ سريع", "qload": "تحميل سريع",
}
for k, (cp, label) in icons.items():
    glyph(cp, label).save(f"{OUT}/ic_{k}.png")
    if k in arabic:
        glyph(cp, arabic[k], arabic=True).save(f"{OUT}/ic_{k}_ar.png")
print("ok", sorted(os.listdir(OUT)))
