#!/usr/bin/env python3
"""Render the touch-control and panel textures for the dev4 "carved stone" skin (4x supersampled).

Style follows REKKR 1.17's own art (blue-grey carved stone, blood red, bone-cream text) but every pixel
here is drawn procedurally — no WAD art is copied. Labels are NOT baked: the app draws them at runtime
with the WAD's own STCFN pixel font (Latin) or Noto Sans Arabic (Arabic), see Scripts/RekkrSkin.cs.
Icons: Material Design Icons webfont (SIL OFL 1.1 font, icons Apache 2.0 / CC BY, Austin Andrews/Google).
Run: uv run --with Pillow --with numpy python tools/make_ui_textures.py"""
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import numpy as np
import math, os, glob

OUT = os.path.join(os.path.dirname(__file__), "..", "Assets/Rekkr/Resources/Rekkr/UI")
MDI = "/usr/share/fonts/truetype/materialdesignicons-webfont/materialdesignicons-webfont.ttf"
K = 4

STONE_HI = np.array([158, 172, 172]); STONE = np.array([104, 118, 122]); STONE_DK = np.array([52, 60, 66])
DEEP = np.array([16, 18, 22]); RED = np.array([200, 30, 18]); EMBER = np.array([255, 128, 40])
rng = np.random.default_rng(1717)


def noise(n, scales=(4, 9, 23, 57), seed=0):
    """Tileable-enough value noise in [0,1] (sum of upsampled random grids)."""
    r = np.random.default_rng(seed)
    acc = np.zeros((n, n))
    w = 1.0; tot = 0
    for s in scales:
        g = r.random((s + 1, s + 1))
        im = Image.fromarray((g * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC)
        acc += w * np.asarray(im, dtype=float) / 255; tot += w; w *= 0.55
    acc /= tot
    return (acc - acc.min()) / (acc.max() - acc.min() + 1e-9)


def stone_rgb(n, seed, base=STONE, hi=STONE_HI, lo=STONE_DK):
    t = noise(n, seed=seed)
    cracks = noise(n, scales=(31, 77), seed=seed + 7)
    t = np.clip(t * 1.25 - 0.12, 0, 1)
    rgb = lo[None, None] * (1 - t[..., None]) + hi[None, None] * t[..., None]
    rgb = rgb * 0.55 + base[None, None] * 0.45
    rgb *= (0.86 + 0.14 * (cracks[..., None] > 0.35))
    return rgb


def to_img(rgb, alpha):
    a = np.clip(alpha, 0, 1) * 255
    arr = np.dstack([np.clip(rgb, 0, 255), a]).astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


def ring_disc(size, pressed=False, ring_w=0.085, inner_alpha=0.72, seed=3):
    """Stone ring with bevel lighting + dark translucent centre (+ red ember glow when pressed)."""
    N = size * K
    y, x = np.mgrid[0:N, 0:N].astype(float)
    c = (N - 1) / 2; dx, dy = x - c, y - c
    r = np.hypot(dx, dy) / (N / 2)
    R_out, R_in = 0.965, 0.965 - ring_w * 2
    stone = stone_rgb(N, seed)
    # bevel: normal of a rounded ring profile, light from the top-left
    mid = (R_out + R_in) / 2; half = (R_out - R_in) / 2
    prof = np.clip((r - mid) / half, -1, 1)
    ang = np.arctan2(dy, dx)
    nx = prof * np.cos(ang); ny = prof * np.sin(ang)
    light = -(nx * -0.62 + ny * -0.78)          # light direction (-0.62,-0.78)
    shade = 0.95 + 0.42 * light - 0.22 * prof ** 2
    ringc = stone * shade[..., None]
    if pressed:
        ringc = ringc * 1.12 + np.array([30, 10, 0])
    ring_a = ((r <= R_out) & (r >= R_in)).astype(float)
    # anti-aliasing is handled by the 4x downsample
    # dark outline just outside the ring and inside it
    outline = ((r > R_out) & (r <= R_out + 0.025)) | ((r < R_in) & (r >= R_in - 0.02))
    inner = r < R_in - 0.02
    rr = np.clip(r / (R_in - 0.02), 0, 1)
    if pressed:
        glow = np.clip(1 - rr, 0, 1) ** 0.9
        inner_c = DEEP[None, None] * (1 - glow[..., None]) + (RED * 0.75 + EMBER * 0.25)[None, None] * glow[..., None]
        inner_c = inner_c * (0.9 + 0.2 * noise(N, (5, 13), seed + 11)[..., None])
        inner_a = 0.80 + 0.12 * glow
    else:
        inner_c = DEEP[None, None] * (1.0 + 0.9 * (1 - rr[..., None]) ** 2) + np.array([6, 8, 10])
        inner_a = inner_alpha * (0.85 + 0.15 * (1 - rr))
        # inner shadow under the rim
        inner_c = inner_c * (0.6 + 0.4 * np.clip((R_in - 0.02 - r * (R_in - 0.02)) * 8, 0, 1))[..., None] if False else inner_c
    rgb = np.where(ring_a[..., None] > 0, ringc, np.where(outline[..., None], np.array([8, 9, 11]), inner_c))
    alpha = np.where(ring_a > 0, 1.0, np.where(outline, 0.9, np.where(inner, inner_a, 0.0)))
    img = to_img(rgb, alpha)
    return img.resize((size, size), Image.LANCZOS)


def stick_base(size=256):
    img = ring_disc(size, ring_w=0.05, inner_alpha=0.32, seed=5)
    N = size * K
    ov = Image.new("RGBA", (N, N), (0, 0, 0, 0)); d = ImageDraw.Draw(ov)
    c = N / 2; rr = N / 2 * 0.965
    for a in range(4):  # carved direction notches (bone with dark edge)
        ang = a * math.pi / 2 - math.pi / 2; tip = rr * 0.83; base = rr * 0.66; wdt = rr * 0.1
        px, py = math.cos(ang), math.sin(ang); qx, qy = -py, px
        pts = [(c + px * tip, c + py * tip), (c + px * base + qx * wdt, c + py * base + qy * wdt),
               (c + px * base - qx * wdt, c + py * base - qy * wdt)]
        d.polygon(pts, fill=(12, 12, 14, 230))
        pts2 = [(c + px * (tip - 10 * K), c + py * (tip - 10 * K)),
                (c + px * (base + 5 * K) + qx * (wdt - 9 * K), c + py * (base + 5 * K) + qy * (wdt - 9 * K)),
                (c + px * (base + 5 * K) - qx * (wdt - 9 * K), c + py * (base + 5 * K) - qy * (wdt - 9 * K))]
        d.polygon(pts2, fill=(236, 218, 170, 235))
    r2 = rr * 0.5
    d.ellipse([c - r2, c - r2, c + r2, c + r2], outline=(200, 212, 210, 70), width=3 * K)
    img.alpha_composite(ov.resize((size, size), Image.LANCZOS))
    return img


def stick_knob(size=256):
    N = size * K
    y, x = np.mgrid[0:N, 0:N].astype(float)
    c = (N - 1) / 2; dx, dy = (x - c) / (N / 2), (y - c) / (N / 2)
    r = np.hypot(dx, dy)
    R = 0.94
    z = np.sqrt(np.clip(1 - (r / R) ** 2, 0, 1))
    nx, ny = dx / R, dy / R
    light = np.clip(-(nx * -0.55 + ny * -0.7) * 0.8 + z * 0.6, 0, 1.3)
    stone = stone_rgb(N, 21, base=np.array([150, 162, 162]))
    rgb = stone * (0.45 + 0.75 * light)[..., None]
    # red rune ring in the centre
    ring = (np.abs(r - 0.36) < 0.035)
    rgb = np.where(ring[..., None], RED * (0.8 + 0.4 * light[..., None]), rgb)
    edge = (r > R) & (r <= R + 0.04)
    alpha = np.where(r <= R, 1.0, np.where(edge, 0.9, 0))
    rgb = np.where(edge[..., None], np.array([8, 9, 11]), rgb)
    return to_img(rgb, alpha).resize((size, size), Image.LANCZOS)


def panel(size=192, border=48, seed=9, lit=False, inner_alpha=0.96):
    """9-slice carved stone frame. Border width `border` px (of the final texture)."""
    N = size * K; B = border * K
    y, x = np.mgrid[0:N, 0:N].astype(float)
    # distance to the outer edge and to the inner edge of the frame
    d_out = np.minimum.reduce([x, y, N - 1 - x, N - 1 - y])
    stone = stone_rgb(N, seed)
    frame = d_out < B
    # bevel: outer bevel (0..B*0.3) lit top-left, inner bevel (B*0.7..B) lit bottom-right (carved in)
    t = d_out / B
    left = x < y; top = y < x  # which diagonal half
    # side identification
    side_top = (y <= x) & (y <= N - 1 - x)
    side_left = (x < y) & (x <= N - 1 - y)
    lit_side = side_top | side_left
    shade = np.ones((N, N))
    outer = t < 0.28; innerb = (t > 0.72) & frame
    shade = np.where(outer & lit_side, 1.22, shade)
    shade = np.where(outer & ~lit_side, 0.62, shade)
    shade = np.where(innerb & lit_side, 0.58, shade)
    shade = np.where(innerb & ~lit_side, 1.25, shade)
    fc = stone * shade[..., None]
    # engraved groove in the middle of the frame
    groove = frame & (np.abs(t - 0.5) < 0.05)
    fc = np.where(groove[..., None], fc * 0.55, fc)
    # rivets at the four corners
    for cx, cy in [(B / 2, B / 2), (N - B / 2, B / 2), (B / 2, N - B / 2), (N - B / 2, N - B / 2)]:
        rd = np.hypot(x - cx, y - cy) / (B * 0.15)
        m = rd < 1
        zz = np.sqrt(np.clip(1 - rd ** 2, 0, 1))
        riv = np.array([150, 160, 160])[None, None] * (0.5 + 0.7 * np.clip(zz - (x - cx) / (B * 0.5) * 0.3 - (y - cy) / (B * 0.5) * 0.3, 0, 1.2))[..., None]
        fc = np.where(m[..., None], riv, fc)
    # dark outline at the very edge + at the inner edge
    edge = (d_out < 1.5 * K) | (frame & (d_out > B - 1.5 * K))
    fc = np.where(edge[..., None], np.array([6, 7, 9]), fc)
    # inside: deep near-black with a diagonal sheen (like REKKR's menu frame)
    diag = (x + y) / (2 * N)
    if lit:
        u = np.clip(1 - np.hypot((x - N / 2) / (N / 2 - B), (y - N / 2) / (N / 2 - B)) * 0.8, 0, 1)
        ic = np.array([70, 10, 6])[None, None] * (1 - u[..., None]) + (RED * 0.85 + EMBER * 0.15)[None, None] * u[..., None]
    else:
        ic = np.array([14, 16, 19])[None, None] + np.array([16, 18, 20])[None, None] * diag[..., None]
    # inner shadow just inside the frame
    sh = np.clip((d_out - B) / (B * 0.5), 0, 1)
    ic = ic * (0.55 + 0.45 * sh[..., None])
    rgb = np.where(frame[..., None], fc, ic)
    alpha = np.where(frame, 1.0, inner_alpha)
    return to_img(rgb, alpha).resize((size, size), Image.LANCZOS)


def glyph(cp, size=256, scale=0.5, dy=0.0):
    """White icon with a soft dark shadow + 1px dark outline (tinted at runtime)."""
    N = size * K
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    f = ImageFont.truetype(MDI, int(N * scale))
    ch = chr(cp)
    bb = d.textbbox((0, 0), ch, font=f)
    w, h = bb[2] - bb[0], bb[3] - bb[1]
    pos = (N / 2 - w / 2 - bb[0], N * (0.5 + dy) - h / 2 - bb[1])
    sh = Image.new("RGBA", (N, N), (0, 0, 0, 0)); ds = ImageDraw.Draw(sh)
    ds.text((pos[0], pos[1] + 4 * K), ch, font=f, fill=(0, 0, 0, 200), stroke_width=5 * K, stroke_fill=(0, 0, 0, 200))
    sh = sh.filter(ImageFilter.GaussianBlur(5 * K))
    img = Image.alpha_composite(img, sh); d = ImageDraw.Draw(img)
    d.text(pos, ch, font=f, fill=(255, 255, 255, 255), stroke_width=2 * K, stroke_fill=(10, 10, 12, 255))
    return img.resize((size, size), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    for old in glob.glob(f"{OUT}/ic_*_ar.png*"):
        os.remove(old)  # dev4: labels are drawn at runtime
    ring_disc(256).save(f"{OUT}/btn.png")
    ring_disc(256, pressed=True).save(f"{OUT}/btn_pressed.png")
    stick_base().save(f"{OUT}/stick_base.png")
    stick_knob().save(f"{OUT}/stick_knob.png")
    panel().save(f"{OUT}/panel.png")
    panel(96, 20, seed=13, inner_alpha=1.0).save(f"{OUT}/plate.png")
    panel(96, 20, seed=13, lit=True, inner_alpha=1.0).save(f"{OUT}/plate_on.png")
    # (codepoint, vertical offset): labelled buttons keep the icon a bit higher, the label goes below
    icons = {
        "fire": (0xF4E5, -0.08), "use": (0xF2C7, -0.08), "wnext": (0xF13E, -0.08), "wprev": (0xF13D, -0.08),
        "map": (0xF34D, -0.08), "menu": (0xF35C, 0), "run": (0xF46E, -0.08), "save": (0xF193, -0.08),
        "up": (0xF05E, 0), "down": (0xF046, 0), "left": (0xF04E, 0), "right": (0xF055, 0),
        "ok": (0xF12C, -0.08), "settings": (0xF493, 0), "back": (0xF54C, -0.08),
        "qsave": (0xF193, -0.08), "qload": (0xF2DA, -0.08), "move": (0xF1B6, 0), "play": (0xF40A, 0),
        "jump": (0xF13F, -0.08),
    }
    for k, (cp, dy) in icons.items():
        glyph(cp, scale=0.46 if dy else 0.52, dy=dy).save(f"{OUT}/ic_{k}.png")
    print("ok", sorted(f for f in os.listdir(OUT) if not f.endswith(".meta")))


if __name__ == "__main__":
    main()
