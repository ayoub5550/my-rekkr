# DEV8 — REKKR for Android v0.8.0 "Animation: modern, still classic" (plan, spec and status)

> **ملخص للمالك:** هذه ورقة dev8. طلبت (2026-09-30): تحسين الأنيميشن وإضافة أنيميشن جديد وعصرنته مع
> الإبقاء على كلاسيكية اللعبة — كل البنود المقترحة **ما عدا أنيميشن أزرار اللمس** («الأزرار كما هي جيدة»).
> كل شيء بصري فقط: منطق اللعب والعروض والحفظ لم تتغير. تبويب جديد **ANIMATION**: Classic (الأصلي 100٪)
> / Modern (الافتراضي) / Custom. إذا توقف Viktor قل لأي مطور أو وكيل: **«أكمل dev8»**.

Owner request 2026-09-30 14:58 UTC: "dev8: improve the animation, add new animation and modernise it while
keeping the game classic". 15:03 UTC: "the buttons are fine as they are; the rest is excellent, do it".

---

## 0. How to resume ("أكمل dev8")

1. Read `AGENTS.md`, `docs/DEV7.md` §1 (hard rules), then this file.
2. Branch **`feat/dev8`** from `feat/dev7` @ `7db91a4` (= `main` after the v0.7.0 release). Work happens in
   the `.worktrees/dev7` checkout (its Unity `Library/` cache is reused). Never commit to `main` directly;
   merging dev8 into `main` needs the owner's OK.
3. Version **0.8.0**, versionCode **8**, same keystore (cert `768de491…bab8`) → installs over 0.7.0.

## 1. Rules

- Everything is **visual only**. The simulation never reads the animation state: `AnimHooks` (render side),
  `Mobj.AnimFlash/AnimLastHealth`, `PlayerSpriteDef.Old*`, `Specials.*Next` are not saved and not read by
  game code. HeadlessTest golden (176 frames) + DEMO1–4 must PASS.
- Animation style **Classic = every hook off = the original presentation** (pixel-identical frames).
- The touch buttons get no animation (owner decision 2026-09-30).
- Settings pages ≤ 6 rows, EN + AR strings.

## 2. Findings before coding

| Area | Finding |
|---|---|
| Weapon | `psp.Sx/Sy` (bob, raise, lower) change only per 35 Hz tic and were drawn as-is: at 90–120 Hz the weapon repeats the same spot on 2–3 frames then jumps (desktop probe: 54 % of walking frames did not move at all). |
| Camera | View height, bob and landing squat were already interpolated (`GetInterpolatedViewZ`). |
| Liquids | Animated flats switch picture every 8 tics (4.4 Hz) with a hard cut. |
| Remaster | The GPU camera is built from `ThreeDRenderer.LastView`, so view offsets applied in the software renderer reach Remaster too. |

## 3. Design

- `Engine/Video/AnimHooks.cs` — static values the renderers read (weapon offset / interpolation / eased
  switch, view yaw / pitch / height / roll, pickup float + glow, hit flash, liquid cross-fade, HUD pops).
- `Scripts/AnimFx.cs` — per tic: fire (ammo spent / attack state / flash state), hurt (DamageCount rise +
  attacker direction), landing, monsters hit (health drop), missiles exploded (lost MF_MISSILE), barrels
  killed; per frame: springs → weapon sway (turn/look lag), breathing, strafe tilt, landing dip, per-weapon
  recoil, camera shake (smooth noise), hit kick, damage marks, HUD pops.
- Weapon: `PlayerSpriteDef.OldSx/OldSy/OldState` saved in `Player.UpdateFrameInterpolationInfo`; drawn
  position = lerp when the sprite is the same and the step ≤ 20 px (state offsets never smeared); raise /
  lower mapped through smoothstep (same duration and end points).
- Liquids: `Specials.FlatTranslationNext/TextureTranslationNext` + `AnimBlend(frac)`. Software (true colour):
  planes write the next texel into `DrawScreen.AnimTex` (stamp-tagged) and the writer blends both colours
  with the same light. Remaster: second lookup texture `_Lookup2` + `_AnimBlend` (flats and walls).
- World FX: `WorldFx` pass 6 (alpha-blended "solid" particles): bullet chips + dust, blood drops that rest on
  the floor + mist, explosion smoke / embers / debris. They need the Particles setting.
- UI: settings panel open (fade + settle), weapon wheel pop-in (ease-out-back), fullscreen-HUD number pops,
  damage-direction crescents (IMGUI). The classic status bar and the melt wipe are unchanged.

## 4. Stages and status

| # | Stage | Status | Acceptance |
|---|---|---|---|
| 0 | Plan (this file), version 0.8.0/8, settings + ANIMATION tab (3 pages) | ✅ 2026-09-30 | builds, ≤ 6 rows/page |
| 1 | Smooth motion: interpolated weapon, eased switch, liquid cross-fade (software + Remaster) | ✅ 2026-09-30 Linux scenario 12: weapon "still" frames while walking 54.5 % → 0 % (software), 43.5 % → 0 % (Remaster) | still frames ≈ 0 % in Modern |
| 2 | Weapon motion: turn/look lag, breathing, strafe tilt (roll in Remaster 3D weapon), landing dip, per-weapon recoil | ✅ 2026-09-30 recoil detected for the bow (no flash state in REKKR → ammo/attack-state detection) | recoil visible in shots |
| 3 | Camera: explosion shake, hit kick, strafe lean (Remaster, off by default), damage direction marks | ✅ 2026-09-30 barrel at 220 u: shake 0.48 | shake / marks in scenario 12 |
| 4 | World: floating + glowing pickups, monster hit flash, impact chips/dust, richer blood, explosion smoke/embers/debris | ✅ 2026-09-30 | shots, no errors |
| 5 | UI: panel + wheel transitions, HUD pops (touch buttons untouched) | ✅ 2026-09-30 | shots |
| 6 | Tests: scenario 12 (smoothness metric Classic vs Modern, events, explosion, fight, liquid frames); HeadlessTest golden | ✅ 2026-09-30 HeadlessTest golden 176 frames identical, RESULT PASS | PASS |
| 7 | Android 0.8.0 build, Test Lab (1, 11, 12), merge into `main` + release v0.8.0 on the owner's OK | ✅ 2026-09-30 owner OK («ارفعها الى المستودع في main»); virtual MediumPhone.arm/33: Passed, 0 `E Unity`, 36/36 maps errors=0; physical r8q run still to do (Spark quota used up 2026-09-30) — see §6 | Passed, 0 `E Unity` |

Legend: ⬜ not started · 🚧 in progress (Next: …) · ✅ done · ⏭ deferred (reason).

## 5. What v0.8.0 contains — full description / الوصف الكامل

### 5.1 للمالك (عربي)

- **تبويب جديد في الإعدادات: الأنيميشن (ANIMATION)** بثلاث صفحات. أول خيار هو **نمط الأنيميشن**:
  - **كلاسيكي (الأصلي):** كل الإضافات مطفأة، والصورة مطابقة 100٪ للعبة الأصلية.
  - **حديث (الافتراضي):** كل الإضافات تشتغل بقوة «عادي»، ما عدا الميلان الجانبي.
  - **مخصص:** يتحول له النمط تلقائياً أول ما تغيّر أي خيار بنفسك.
- **السلاح:**
  - **حركة ناعمة:** يتحرك السلاح كل إطار على 60/90/120 Hz. قبل كانت حركته 35 مرة في الثانية فقط، فيبان متقطع.
  - **تمايل وتنفس:** السلاح يتأخر شوي عن التفاتك، ويميل وقت المشي الجانبي، وينخفض لما تنزل من قفزة.
  - **ارتداد خاص بكل سلاح.**
  - **تبديل سلاح انسيابي:** السلاح يطلع وينزل بنفس المدة الأصلية، لكن بحركة ناعمة.
- **الكاميرا:**
  - اهتزاز مع الانفجارات القريبة.
  - ارتداد خفيف لما تنصاب.
  - علامات حمراء تبيّن اتجاه اللي ضربك.
  - ميلان جانبي وقت المشي الجانبي: في Remaster فقط، ومطفأ افتراضياً.
- **العالم:**
  - الأغراض تطفو وتلمع.
  - الوحش يومض لما تصيبه.
  - شظايا وغبار مكان الضربة.
  - دماء أغنى تستقر على الأرض.
  - دخان وجمر وحطام مع الانفجارات.
  - انتقال ناعم بين صور السوائل المتحركة: في وضع البرمجيات (software) للأرضيات فقط، وفي Remaster للأرضيات والجدران.
- **الواجهة:**
  - لوحة الإعدادات تفتح بتلاشٍ وتستقر.
  - عجلة الأسلحة تظهر بحركة.
  - الأرقام تنبض في الواجهة الكاملة (Fullscreen HUD).
  - **أزرار اللمس ما تغيّرت، بطلب منك.**
  - شريط الحالة الكلاسيكي وانتقال الذوبان (melt) بين الشاشات بقيوا كما هم.
- **كله بصري فقط:** منطق اللعب والعروض والحفظ ما تغيّرت، واختبار المحرك طابق 176 إطاراً مع الأصل.
- **الشظايا والدماء** تحتاج خيار **الجسيمات (Particles)** في تبويب الرسوميات.

### 5.2 Settings reference

| Setting (EN / AR) | Key | Values | Modern | Classic | Page |
|---|---|---|---|---|---|
| Animation style / نمط الأنيميشن | (derived) | Classic / Modern / Custom | Modern | Classic | 1 |
| Smooth weapon motion / حركة سلاح ناعمة | `an_smooth` | on/off | on | off | 1 |
| Weapon sway & breathing / تمايل السلاح والتنفس | `an_motion` | off / normal / strong | normal | off | 1 |
| Weapon recoil / ارتداد السلاح | `an_recoil` | on/off | on | off | 1 |
| Eased weapon switch / تبديل سلاح انسيابي | `an_ease` | on/off | on | off | 1 |
| Smooth liquid animation / أنيميشن سوائل ناعم | `an_liquids` | on/off | on | off | 1 |
| Camera shake / اهتزاز الكاميرا | `an_shake` | off / normal / strong | normal | off | 2 |
| Hit kick / ارتداد عند الإصابة | `an_kick` | on/off | on | off | 2 |
| Damage direction / اتجاه الضرر | `an_dmgdir` | on/off | on | off | 2 |
| Strafe lean (Remaster) / ميلان جانبي | `an_roll` | on/off (shown only if Remaster is allowed) | **off** | off | 2 |
| Floating pickups / أغراض طافية | `an_pickups` | on/off | on | off | 2 |
| Monster hit flash / وميض الوحش | `an_flash` | on/off | on | off | 2 |
| Impacts, smoke & debris / شظايا ودخان وحطام | `an_impacts` | on/off (needs Particles) | on | off | 3 |
| Richer blood / دماء أغنى | `an_blood` | on/off (needs Particles) | on | off | 3 |
| Menu & HUD animation / أنيميشن القوائم والواجهة | `an_ui` | on/off | on | off | 3 |

The style is not stored: `RekkrSettings.MatchAnimStyle()` derives it from the 14 values at load. The GYRO tab
(was MOTION) and DISPLAY got shorter labels so that 5 tabs fit.

### 5.3 Code map

| File | Change |
|---|---|
| `Engine/Video/AnimHooks.cs` (new) | Static render-side values + `WeaponPos()` (lerp / smoothstep raise-lower / offsets), `IsRaiseLower()` |
| `Scripts/AnimFx.cs` (new) | Event detection per tic + springs per frame; `AnimFx.Current`; counters used by scenario 12 |
| `Scripts/RekkrApp.Anim8.cs` (new) | Test scenario 12 |
| `Engine/Doom/Game/Player.cs`, `World/PlayerSpriteDef.cs` | `OldSx/OldSy/OldState/OldSprite` saved per tic; cleared on `DisableFrameInterpolation` |
| `Engine/Doom/World/Mobj.cs` | `AnimFlash`, `AnimLastHealth` (not saved) |
| `Engine/Doom/World/Specials.cs` | `FlatTranslationNext/TextureTranslationNext`, `AnimSpeed`, `AnimBlend(frac)` |
| `Engine/Video/ThreeDRenderer.cs`, `Renderer.cs`, `DrawScreen.cs` | Weapon position, view offsets, fractional pitch shear, pickup lift/glow, hit flash (FullBright), liquid `AnimTex/AnimBase` + blended `WriteChunk` |
| `Engine/Video/StatusBarRenderer.cs` | Fullscreen-HUD number pops |
| `Engine/Remaster/ThingBuilder.cs`, `Scripts/Remaster/GpuRenderer.cs`, `RemasterWorld.shader` | Pickup lift/glow, 3D weapon roll, camera roll, `_Lookup2/_AnimBlend` cross-fade |
| `Resources/Rekkr/RekkrWorld.shader`, `Scripts/WorldFx.cs` | Pass 6 alpha-blended solid particles (chips, dust, blood that rests, smoke, embers, debris), 900 max particles |
| `Scripts/RekkrSettings.cs`, `Loc.cs`, `RekkrApp.UI.cs`, `RekkrApp.cs` | Settings, EN/AR strings, ANIMATION tab, damage marks, wheel/panel transitions, version 0.8.0 |

## 6. Measurements

| Test | Result |
|---|---|
| HeadlessTest (engine parity) | golden 176 frames identical, DEMO1–4, RESULT PASS |
| Linux scenario 12 (llvmpipe) | weapon "still" frames while walking: software 54.5 % → 0 %, Remaster 43.5 % → 0 %; recoil fires=2; barrel shake 0.48; errors=0 |
| Test Lab virtual MediumPhone.arm/33, scenarios 12, 1, 11 (2026-09-30) | **Passed**, 0 `E Unity`, 0 FATAL. S12: still 56.1 % → 0.0 %, recoil fires=3, explosion particles=60, liquid E1M1 sector 2 flat 148, errors=0. S1: menus, autosave, quicksave/quickload, look ±75°, jumps=6, haptics=65. S11: 36/36 maps errors=0 (software; Remaster is not available on the emulator). fps ≈ 8–10 (emulator, not meaningful) |
| Test Lab physical r8q/33 | **not run yet**: the Spark quota (5 physical runs/day) was used up on 2026-09-30. Next: `OUT=… SCENARIOS=1,11,12 FTL_TIMEOUT=30m tools/sandbox/ftl_gameloop.sh Builds/REKKR-0.8.0.apk` and compare with dev7 (classic 99–103 fps; Remaster all-maps 73.7 fps at thermal 3) |

APK `REKKR-0.8.0.apk`: 101,797,141 B, versionCode 8, versionName 0.8.0, minSdk 24, arm64-v8a + armeabi-v7a,
cert SHA-256 `768de491…bab8` (installs over 0.6.0/0.7.0), sha256
`d26ce38e0b97b436207538e12fcbc60ef490352806cc8d6ad64ea802810b510d`.

## 7. Limits and not verified

- Smoothness on a real phone at 120 Hz has not been judged by a human yet; physical Test Lab run pending (§6).
- Camera roll exists only in Remaster (the column renderer cannot roll) and is off by default.
- In software, only flats cross-fade (walls do not); Remaster does both.
- Impacts / blood / smoke need the Particles setting (hint shown on page 3).
- Fight counters on the emulator: hits=0 while monster_flashes=1 — the counter misses some hits at 9 fps; the flash works.

## 8. Log

- 2026-09-30: the sandbox has 17 cores; `nproc` prints 1 only because `OMP_NUM_THREADS=1` is set (use `nproc --all`).
- 2026-09-30: REKKR's bow (pistol slot) has no muzzle-flash psprite state — fire detection must not rely on it.
- 2026-09-30: a kill clears MF_SHOOTABLE on the same tic the health drops — track health once a thing was shootable.
- 2026-09-30: when the physical quota is exhausted (`TEST_QUOTA_EXCEEDED`), a virtual `MediumPhone.arm` v33 run (separate quota) still catches crashes and errors.
- 2026-09-30: owner approved the merge into `main` and the v0.8.0 release.
