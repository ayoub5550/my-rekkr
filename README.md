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
* تُختبر كل نسخة على Firebase Test Lab على Galaxy S20 FE (Snapdragon 865 / Adreno 650، نفس
  GPU هاتف POCO F3).
* يدعم لوحة المفاتيح ويد التحكم (gamepad) أيضاً.

## الجديد في dev8 (0.8.0) — أنيميشن حديث بروح كلاسيكية

* **تبويب «الأنيميشن» جديد في الإعدادات:** كلاسيكي (الأصلي 100٪) / حديث (الافتراضي) / مخصص.
* **سلاح ناعم:** يتحرك كل إطار على 60/90/120 Hz بدل 35 مرة في الثانية، مع تمايل وتنفس، وميلان وقت المشي الجانبي، وانخفاض عند الهبوط، وارتداد لكل سلاح، وتبديل انسيابي.
* **كاميرا حية:** اهتزاز مع الانفجارات، وارتداد عند الإصابة، وعلامات حمراء تبيّن اتجاه الضرر، وميلان جانبي في Remaster (مطفأ افتراضياً).
* **عالم حي:** أغراض تطفو وتلمع، ووميض الوحش عند الإصابة، وشظايا وغبار، ودماء أغنى، ودخان وجمر وحطام، وانتقال ناعم بين صور السوائل.
* **واجهة:** انتقالات للوحة الإعدادات وعجلة الأسلحة، ونبض أرقام الواجهة. أزرار اللمس بقيت كما هي.
* **بصري فقط:** اللعب والعروض والحفظ تبقى أصلية 100٪. التفاصيل الكاملة في [docs/DEV8.md](docs/DEV8.md).

## الجديد في dev2 (0.2.0)

* **شاشة عريضة (Widescreen):** اللعبة تملأ شاشة 20:9 بزاوية رؤية أوسع (Hor+)، مع خيار 4:3 الأصلي.
* **90 / 120 إطاراً في الثانية** على الشاشات السريعة (تلقائي / 60 / 90 / 120).
* **حفظ تلقائي** عند بداية كل خريطة وعند الخروج من التطبيق، وزرّا **حفظ سريع / تحميل سريع**، وزر **متابعة** في الشاشة الرئيسية.
* **واجهة بدون شريط الحالة (Fullscreen HUD)** بلوحات شفافة داكنة، أو بدون واجهة إطلاقاً.
* **التصويب بالجيروسكوب** (حساسية + عكس الاتجاه) و**اهتزاز** عند الهجوم وتلقي الضرر.
* **اختيار السلاح باللمس** على أرقام ARMS في شريط الحالة أو الواجهة.
* **محرر أماكن الأزرار:** اسحب أي زر وغيّر حجمه، مع زر إعادة للوضع الافتراضي.
* **موسيقى عالية الجودة** (GeneralUser GS) أو الكلاسيكية (TimGM6mb).
* **واجهة عربية كاملة** (الإعدادات والأزرار) مع التبديل إلى الإنجليزية.

## Features / improvements over the original

| | |
|---|---|
| Content | Unmodified `REKKR.WAD` 1.17 (36 maps, E1–E4, music, demos) |
| Rendering | 640×400 high-res software renderer, sharp-bilinear upscale, 4:3 centred |
| Frame rate | 35 Hz game logic with frame interpolation → 60 fps on screen |
| Controls | Floating stick, swipe-look, aim-while-firing, ATTACK/USE/WEAPON ±/RUN/MAP/MENU, menu D-pad |
| Settings | Look sensitivity, button size (70–140 %), opacity, left-handed layout, always-run |
| Music | MUS/MIDI through MeltySynth + TimGM6mb soundfont |
| Save slots | 6 slots, auto-named `E#M# dd/MM HH:mm` |
| Widescreen | Hor+ 3D view fills 20:9 (e.g. 1066x400 frame), 2D screens centred; 4:3 option |
| Frame rate | Auto / 60 / 90 / 120 Hz (display mode switch + interpolation) |
| Saves | Autosave at map start and on leaving the app, QUICK SAVE / QUICK LOAD buttons, CONTINUE |
| HUD | Status bar, compact fullscreen HUD, or none |
| Motion | Gyro aim (sensitivity, invert), haptic pulses on attack / damage |
| Weapons | Tap the ARMS numbers to pick a weapon |
| Layout | Button layout editor (drag + per-button size, reset) |
| Music | GeneralUser GS (high) or TimGM6mb (classic) SoundFont |
| Language | English / Arabic UI (RTL settings, Arabic button labels) |
| Animation (0.8.0) | ANIMATION tab: Classic / Modern / Custom — smooth weapon, sway, recoil, shake, damage marks, floating pickups, hit flash, impacts, smoke, liquid cross-fade (visual only) |
| Other | Gamepad + keyboard, pause→menu on app switch, Firebase Game Loop autopilot (2 scenarios) |

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
