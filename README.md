# REKKR for Android (Unity)

**REKKR** — the famous Viking total conversion for Ultimate Doom by Revae — ported to Android
through Unity. The game is the **complete, original REKKR v1.17**: all 4 episodes / 36 maps,
original textures, sprites, sounds, music, DEHACKED behaviour and the 4 attract demos, played by a
faithful Doom engine (Managed Doom, C#) — nothing is re-drawn or cut.

> Non-commercial fan port. REKKR is CC BY-NC 4.0 — **it may not be sold.** See
> [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## ما هي؟ (عربي)

نقل كامل للعبة **REKKR** الأصلية إلى أندرويد عبر Unity: الحلقات الأربع (36 خريطة) بكل
الرسوميات والأصوات والموسيقى الأصلية، مع تحكم لمس احترافي وتحسينات للهاتف.

* تحميل الـ APK من صفحة [Releases](https://github.com/ayoub5550/my-rekkr/releases).
* عصا تحريك عائمة على اليسار، واسحب على اليمين للالتفات، وأزرار: هجوم، استخدام، السلاح
  التالي/السابق، ركض، خريطة، قائمة. وفي القوائم تظهر أسهم وزر OK وزر BACK.
* إعدادات اللمس (⚙): حساسية النظر، حجم الأزرار، شفافيتها، وضع اليد اليسرى، الركض الدائم.
* رسم بدقة 640×400، و60 إطاراً في الثانية مع تنعيم الحركة، وحفظ تلقائي لاسم الـ save.
* تم اختبارها على Firebase Test Lab على Galaxy S20 FE (Snapdragon 865 / Adreno 650، نفس
  GPU هاتف POCO F3) بمعدل 59 FPS تقريباً.
* يدعم لوحة المفاتيح ويد التحكم (gamepad) أيضاً.

## Features / improvements over the original

| | |
|---|---|
| Content | Unmodified `REKKR.WAD` 1.17 (36 maps, E1–E4, music, demos) |
| Rendering | 640×400 high-res software renderer, sharp-bilinear upscale, 4:3 centred |
| Frame rate | 35 Hz game logic with frame interpolation → 60 fps on screen |
| Controls | Floating stick, swipe-look, aim-while-firing, ATTACK/USE/WEAPON ±/RUN/MAP/MENU, menu D-pad |
| Settings | Look sensitivity, button size (70–140 %), opacity, left-handed layout, always-run |
| Music | MUS/MIDI through MeltySynth + TimGM6mb soundfont |
| Saves | 6 slots, auto-named `E#M# dd/MM HH:mm` |
| Other | Gamepad + keyboard, pause→menu on app switch, Firebase Game Loop autopilot |

## Build

See [AGENTS.md](AGENTS.md) for the full toolchain (Unity 2022.3.62f3, Android SDK 36, NDK r23b,
IL2CPP arm64-v8a + armeabi-v7a) and the Firebase Test Lab workflow.

```sh
REKKR_KEYSTORE=... REKKR_KEYSTORE_PASS=... tools/sandbox/build_android.sh
tools/sandbox/ftl_gameloop.sh Builds/REKKR-0.1.0.apk        # Galaxy S20 FE, Android 13
```

## Licence

Code: GPL-2.0 (derived from Managed Doom). Game data: REKKR, CC BY-NC 4.0 by Revae and
contributors. Details in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
