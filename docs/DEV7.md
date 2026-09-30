# DEV7 — REKKR for Android v0.7.0 "Polish + official release" (plan, spec and status)

> **ملخص للمالك (nall):** هذه ورقة dev7. طلبت (2026-09-30): إصلاح الأخطاء التي رأيتها على هاتفك في
> 0.6.0 — إعدادات الجرافيك لا تنطبق في كل اللعبة، الإضاءة الديناميكية قوية جداً وسيئة، الطقس والضباب
> سيئان — ثم إكمال ما تأجل من dev6 (ظلال المصابيح، سلاح ثلاثي الأبعاد، انكسار الأشباح، فيديو الأبواب
> والمصاعد)، نسخ احتياطي للحفظ (تصدير/استيراد)، تحكم إضافي (يد تحكم كاملة مع تغيير الأزرار)، فحص كل
> الخرائط الـ36 في Remaster، ثم **إطلاق رسمي في main**. سرعة الهواتف الضعيفة ليست في dev7 (طلبك).
> إذا توقف Viktor لأي سبب قل لأي مطور أو وكيل ذكاء اصطناعي: **«أكمل dev7»**.

Owner request 2026-09-30 12:47 UTC: "do all of it except option 2 (weak-phone speed) and release dev7
officially in main. Bugs seen: the new graphics settings are not applied in the whole game; the dynamic
lighting is horribly strong and bad; the weather and the fog are bad."

---

## 0. How to resume ("أكمل dev7")

1. Read `AGENTS.md` (build, Test Lab, sandbox, traps), `docs/DEV6.md` §2 + §9, then this file.
2. Branch **`feat/dev7`** from `feat/dev6` @ `180e958`. Never commit to `main` directly. The owner
   approved (2026-09-30) merging dev7 into `main` at the end (stage 9) — that is the official release.
3. Version **0.7.0**, versionCode **7**, the same keystore as 0.6.0 (cert `768de491…bab8`), so 0.7.0
   installs over 0.6.0 without uninstalling.
4. Stage by stage (§3): spec + checks → commit `dev7: stage N — <title>` with the status row → push.
5. Report to the owner in **Arabic**, honest numbers, a Test Lab video per device build.

## 1. Hard rules (unchanged)

- `REKKR.WAD` never modified; the simulation (Managed Doom) untouched: HeadlessTest golden/demos PASS.
- Visual effects read the world, never call the game RNG.
- Classic preset stays pixel-identical to v0.2.0. Effects stay optional; defaults must look *good*,
  not loud: an effect that the owner calls "bad" at its default is a bug.
- Settings pages hold at most 6 rows (phones overlap otherwise). EN + AR strings for every new row.
- Owner decision: DVBLACK / unlit E3 sectors stay original (the "Dark areas" setting stays).

## 2. Findings before coding (code read 2026-09-30)

| Owner report | Cause found in the code |
|---|---|
| Settings not applied in the whole game | `WorldFx.Process` runs only for the live game (`InLevel`), so the title demos and demo playback never got the world effects — while Remaster *does* draw them (the "same" scene looks different there). Other contexts are checked by the new coverage test (stage 1). |
| Dynamic lights far too strong | Every thing with a fullbright frame is a light (torches, lamps, fireballs, even items), radius 150–320 units, added as `c += c·add·1.8` with `add ≤ 0.5` → up to ×1.9 brightness; no occlusion (lights shine through walls, lamps behind a wall light its front); the muzzle-flash light doubles Doom's own extralight. |
| Fog bad | One flat fog colour per episode, independent of the local light: dark rooms and corridors turn into a light blue/red haze, indoors as much as outdoors. |
| Weather bad | Rain/snow/embers/dust are a screen-space pattern pasted on every pixel farther than 60 units while the player stands under the sky: it covers indoor walls seen from outside, disappears the moment the player steps under a roof, and does not move with the world. E4 "dust" and E3 "embers" look like noise. |

## 3. Stages and status (update in every commit)

| # | Stage | Status | Acceptance |
|---|---|---|---|
| 0 | Plan (this file), version 0.7.0/7 | ✅ 2026-09-30 | builds |
| 1 | Settings coverage: new autopilot scenario 8 toggles every graphics setting in every context (live E1–E4, title demo, demo playback, after load, after a level change, software + Remaster) and measures the changed pixels; fix every "no effect where it should" | ✅ 2026-09-30 two bugs fixed: (a) world effects never ran on the title demos / demo playback (`WorldFx` only for the live game) → now every 3D level view; (b) "Smooth lighting" had no effect in Remaster (shader flag hard-wired to 1) → off = the 32 original COLORMAP steps there too (changes 1–7 % of pixels). Also effects no longer need smooth lighting in Remaster (`FxLighting`). Linux scenario 8: 19 contexts (title sw/rm, E1–E4 starts ×2 angles sw/rm, after load sw/rm): every setting changes pixels wherever its feature is in view; zeros left are by design (no sun in E3, no dark sector < 112 in view, E1 has no auto weather) | scenario 8 table: every setting changes pixels in every context where it applies |
| 2 | Dynamic lights rework: only real light sources (projectiles, flashes, explosions, torches/lamps), subtle intensity, occlusion (sight check), smooth falloff, no double muzzle light, "Dynamic lights: Off / Subtle / Strong" | ✅ 2026-09-30 pickups never light the world; per-kind radius/strength (projectiles 190 u, decorations 130 u weak, monster flashes 120 u); Doom sight check (REJECT + BSP) from the player with a 1/6 s fade; muzzle light small; soft saturation `add/(1+add)` ×1.2 instead of ×1.8 capped 0.5; setting OFF / LOW (default) / HIGH. Linux probe (fireball + torch in front): mean frame brightness +3.0 (LOW) / +4.5 (HIGH) of 31.7 in E3M1, +4.1 / +6.5 of 73.5 in E1M1 (dev6 allowed up to ×1.9 locally) | Linux A/B shots E1–E4 before/after; mean brightness gain ≤ 15 % near a torch, no light through walls |
| 3 | Fog + weather rework: fog colour follows the local light (dark stays dark), mostly outdoors / distance; weather = 3D particles that fall only in sky sectors, occluded by walls, visible from indoors through windows; E3/E4 defaults calmer | ✅ 2026-09-30 fog: density −19 %, E3 multiplier 1.3 → 1.0, indoors the fog colour is scaled by the pixel's own brightness and thinner (×0.5), outdoors full (×0.8). Weather: the screen-space pattern is gone; rain (900 streaks, splashes), snow (700), embers (110), dust (140) are world particles spawned only above sky-ceiling sectors within 600–900 u, depth-tested against the G-buffer (walls hide them), independent of the Particles setting | Linux A/B shots; no weather on indoor walls |
| 4 | Save backup: export all saves + settings to a file the user picks (Android document picker), import it back | ✅ 2026-09-30 DISPLAY tab → SAVES page (EXPORT / IMPORT). `SaveBackup` (plain versioned format `REKKR-BACKUP 1`: doomsav0–9 + settings), `Plugins/Android/RekkrDocs.java` (transparent activity, ACTION_CREATE/OPEN_DOCUMENT, no permission), manifest entry added by `RekkrAndroidManifest`. Linux scenario 9 PASS (2 slots packed → deleted → restored → quick load = E2M1); the APK contains the class + the activity (aapt). The picker itself needs a person: not testable on Test Lab | Linux round trip test; APK builds with the Java helper; Test Lab export smoke |
| 5 | Controls: full gamepad (both sticks, triggers, d-pad) + button remapping page | ✅ 2026-09-30 `Gamepad.cs`: InputManager axes `RekkrAxis1..12`, analog move (left) + look (right, speed 1–10, invert), 8 actions with bindings (buttons or trigger axes), menus by d-pad/stick + A/B/Start. CONTROLS → GAMEPAD page: status, look speed, invert, ASSIGN BUTTONS (guided: 4 stick moves + 8 presses, 7 s per step, skip keeps the old binding), defaults. **Not tested with a real gamepad** (none in the sandbox or on Test Lab) | Linux player: axes/buttons mapped; settings page ≤ 6 rows |
| 6 | Remaster deferred items: point-light shadows (nearest 2), 3D weapon (extruded psprite, optional), spectre refraction, door/lift clip | ✅ 2026-09-30 per-pixel point lights (max 4) with distance-cube shadows (atlas 3×2 tiles of 256², max 2, capped by a triangle budget: 240k/level tris → 1 shadow on E1M7); 3D extruded weapon (setting, default on); spectre pass samples a copy of the frame (REKKR has no MF_SHADOW thing, so it never shows in this WAD); door/lift clip checked at E1M1/E1M2/E2M1 (sw vs gpu parity). r8q scenario 10: lights off/noshadow/shadow = 1.26/2.64/1.72 ms CPU, 80.7 fps avg | Linux shots + Test Lab r8q ≥ 60 fps in Remaster |
| 7 | 36-map Remaster check (autopilot visits every map) + fixes | ✅ 2026-09-30 scenario 11 (Remaster, every effect on): Linux + r8q 36/36 maps with 0 errors, 3D weapon on every map; r8q avg 73.7 fps, 35/36 maps ≥ 60 fps; E1M7 47 fps with 2 shadows → shadow triangle budget added | 36/36 maps: no holes, no errors in log |
| 8 | Build 0.7.0, Test Lab r8q (+ a15 smoke), video | ✅ 2026-09-30 APK 101701165 B, sha256 `3e11d43af4aa03211235da1898c51298575c4779005e112b7b93dc27baec8fae`, cert 768de491…bab8 (same key as 0.6.0 → installs over it). r8q run 1 (scenarios 1,8,10,11, thermal 3): Passed, 0 `E Unity`, scenario 1 103.0 fps / p99 17.0 ms, scenario 11 73.7 fps. r8q run 2 with the shadow budget (1,11): Passed, 0 `E Unity`, scenario 1 98.9 fps, scenario 11 36/36 but 54.8 fps at **thermal 4** (device already hot: E1M7 42.9 fps even with 0 lights) → heavy Remaster maps on a throttled phone stay < 60 fps; the classic path stays ≈ 100 fps. a15 smoke skipped (quota; weak-GPU path unchanged from 0.6.0 apart from the effects) | Passed, 0 `E Unity` |
| 9 | Official release: merge `feat/dev7` → `main`, tag v0.7.0 (not prerelease), byte-verified asset | ✅ | GitHub release v0.7.0 (latest, target main), REKKR-0.7.0.apk 101,701,165 B, sha256 `3e11d43a…8fae`; downloaded asset `sha256sum -c` OK + `cmp` OK |

Legend: ⬜ not started · 🚧 in progress (Next: …) · ✅ done · ⏭ deferred (reason).

## 4. Log of decisions

- 2026-09-30: The owner's controls idea "gyro, vibration, move buttons" already exists (dev2–dev4:
  gyro aim, haptics on fire/damage, layout editor). dev7 adds what is missing: gamepad analog sticks,
  triggers and a remap page.
- 2026-09-30: REKKR has no fuzzy (MF_SHADOW) monster: its DEHACKED gives the spectre slot ("Mean Husk") solid
  bits. The Remaster spectre refraction is implemented (copy of the scene, bent by the sprite shape) but only
  shows for MF_SHADOW things, i.e. never in REKKR itself; the invisible player's weapon keeps the software fuzz.
- 2026-09-30: REKKR weapon sprites are huge (the bow `PISGI0` is 700×240), so the 3D weapon extrudes up to
  1024 px and turns only 1.5° (+ ±2.5° sway when turning): a larger turn distorts a screen-wide bow.
- 2026-09-30: Remaster lights the world itself (real normals, the nearest two lights cast shadows through
  distance maps in a 3×2-tile RFloat atlas, 256² per face); WorldFx skips its screen-space lights then. The
  light's own sprite (fireball / torch) is not a caster (within 28 u of the light), otherwise it shadowed
  everything on the camera side.
