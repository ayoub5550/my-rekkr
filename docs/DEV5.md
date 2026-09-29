# DEV5 — REKKR for Android v0.5.0 "Masterpiece graphics" (plan, spec and status)

> **ملخص للمالك (nall):** هذه ورقة dev5. طلبت: كل المؤجل من dev4 (لمس الخريطة، عجلة الأسلحة)، وسحب
> متحركة، وجو أفضل، ومياه حقيقية، وإضاءة بروح Voxile — لتصبح اللعبة "تحفة". كل التأثيرات شكلية فقط
> ويمكن إطفاؤها، والإعداد Classic يبقى الأصل تماماً. إذا توقف Viktor لأي سبب قل لأي مطور أو وكيل ذكاء
> اصطناعي: **«أكمل dev5»**. يقرأ هذا الملف، ويبحث في جدول الحالة (§3) عن أول مرحلة غير مكتملة، ويكملها.

Owner request 2026-09-29 19:17–19:24 UTC: dev5 must contain everything deferred from dev4, plus
"animated clouds, a better atmosphere, real water like Minecraft (shader mods)", reference game
**Voxile** (ray-traced voxel game). Approved plan: see Slack 19:19 (9 stages) + "Voxile-style
lighting". Full voxel/3D renderer = **dev6** (`docs/DEV6.md`).

---

## 0. If you are the next developer: how to resume ("أكمل dev5")

1. Read `AGENTS.md` fully, then `docs/DEV3.md` §2 (architecture), `docs/DEV4.md` §1–§2, and this file.
2. Branch **`feat/dev5`** (from `feat/dev4` @ `0bbef5d`). Never commit to `main`; never merge into `main`
   without the owner's explicit OK.
3. Take the first stage in §3 that is not ✅, implement its spec (§4), run the checks (§5), commit
   `dev5: stage N — <title>` including the updated status row, push `feat/dev5`.
   Half-done work → commit as 🚧 with a "Next:" note.
4. Secrets: same as DEV3 §0.5 (never commit credentials; revoke gcloud after Test Lab).
5. Report to the owner in **Arabic**, honest (device-measured vs local-only), with a Test Lab video.

## 1. Rules

- `REKKR.WAD` is never modified, no WAD art is copied into the repo (read it at runtime).
- **Visual only.** Nothing in dev5 may change the simulation: DEMO1–4 + golden hashes PASS; particles
  and lights only *read* the world (no RNG calls — `M_Random`/`P_Random` are never touched).
- Every effect has a switch in the GRAPHICS tab; **Classic** preset = pixel-identical v0.2.0 path.
- Performance budget on r8q (SD865/Adreno 650, 120 Hz): **Masterpiece ≥ 90 fps avg**, Enhanced
  ≥ 105 fps (no regression vs 0.4.0). A second, weaker Test Lab device must reach ≥ 55 fps on its
  auto-picked preset.
- APK growth ≤ +2 MB (effects are procedural shaders, no texture packs).

## 2. Architecture: the G-buffer trick

The software renderer (Managed Doom, CPU) produced colour only. dev5 makes it also write one byte
per pixel — a **G-buffer code** — which is stored in the **alpha channel** of the RGBA frame texture
we already upload (free: the alpha byte was always 0xFF). GPU shaders then know, per pixel, what it is
and how far away it is — the same information Minecraft shader packs get from the depth buffer.

| Alpha code | Meaning |
|---|---|
| 0–199 | solid 3D pixel (wall, flat, sprite); depth `z = 8 · 2^(code/19.9)` map units (8…8192, log) |
| 200–223 | **water** (blue animated flats: REKKR `FWATER*`); depth code = `(code−200)·200/24` |
| 224–235 | **murky liquid** (dull animated flats: REKKR `BLOOD*` = olive swamp); depth `(code−224)·200/12` |
| 236–247 | **hot/toxic liquid** (red/green animated flats: `LAVA*`, `NUKAGE*`); depth `(code−236)·200/12` |
| 248 | sky |
| 249 | player weapon (psprite) |
| 255 | not 3D (HUD, status bar, menu, border, overdraw, fuzz) → no effects |

Implementation: `Engine/Video/GBuffer.cs` (codes, depth encode/decode, flat classification),
`DrawScreen.GData` (2D drawing marks its pixels 255), `Renderer.WriteChunk` (alpha = code; a pixel
whose palette index is not any COLORMAP row of its texel is treated as overdrawn → 255).

- Written in `ThreeDRenderer` next to the existing true-colour `texData/lightData` writes (so the
  G-buffer exists only when **smooth lighting** is on — Balanced/Enhanced/Masterpiece). Per-column
  (walls, sprites) or per-row (flats) codes, so the per-pixel cost is one byte store.
- `Renderer.WriteChunk` puts the code into alpha (255 where the 3D pixel was overdrawn by 2D).
- **Liquid classification** is data-driven, not by name (REKKR re-uses vanilla names for other
  colours: `FWATER10` is orange, `LAVA1` is green): every flat that belongs to an animated flat
  sequence is classified by its average palette colour — blue-dominant → water, else → hot/toxic.
- The view (position, angle, centre, projection, pitch shear) is published each frame as
  `ThreeDRenderer.LastView` so shaders can reconstruct view-space positions from depth.

GPU chain (`Scripts/WorldFx.cs` + `Resources/Rekkr/RekkrWorld.shader`), all in **frame space**
(the transposed column-major frame texture, before the upscale), then the dev3 PostFx chain:

1. **World pass** — per pixel by code: animated sky (drifting painted sky + procedural cloud layer +
   sun glow + lightning), water (screen-space planar reflection by scanning up to the shore line,
   ripples, fresnel, sparkles, shore foam, rain rings), hot liquids (emissive pulse + flow warp),
   dynamic lights (≤ 8 point lights), AO (depth-based, 8 taps), distance fog (per-episode colour),
   weather (rain/snow/embers/dust layers occluded by depth, only when the player is outdoors).
2. **Sun rays** — radial blur of the bright sky from the sun position (half res).
3. **Particles** — CPU-simulated visual particles (sparks, blood drops, splashes, embers) drawn as
   quads into the world target with a depth test against the G-buffer.
4. **Depth of field** (optional) — far pixels mixed with a blurred copy.
5. Then dev3 PostFx: upscale → bloom → grade (new **"Voxile" filmic grade**) → vignette → sharpen.

## 3. Status table (update in every stage commit)

| # | Stage | Status | Notes / measured |
|---|---|---|---|
| 0 | Plan, branch, version 0.5.0 / versionCode 5, DEV6 plan doc | ✅ 2026-09-29 | `RekkrApp.Version` 0.5.0, bundleVersion 0.5.0 / code 5; `docs/DEV6.md` written (GPU 3D renderer + extruded/voxel things) |
| 1 | dev4 carry-overs: automap touch (pan/pinch zoom/follow), weapon wheel | ✅ 2026-09-29 | `AutoMap.TouchPan/TouchZoom/SetFollow`; Look finger drags = pan, 2 fingers = pinch, double-tap = follow. WEAPON buttons: tap (on release) = next/prev, hold 0.25 s = wheel (`TouchInput.WheelOpen/WheelItems`, icons = pickup sprites read from the WAD, `RekkrApp.UI.DrawWheel`). Linux scenario 5: wheel 8 items, chose Shotgun → ready=Shotgun; automap zoom 1.00→2.99, follow off after pan, on after double-tap |
| 2 | G-buffer in the software renderer + liquid classification + `LastView`; WorldFx skeleton | ✅ 2026-09-29 | HeadlessTest `dev5`: 49 liquid flats (water 21, murky 3, hot 25), alpha = code everywhere (0 mismatches), depth order OK, false-colour shots reviewed (walls/floors grey by depth, water blue, sky cyan, weapon magenta). Golden 176 frames identical |
| 3 | Living sky: drifting sky, cloud layer, sun glow, sun rays | ✅ 2026-09-29 | `ThreeDRenderer.SkyDriftBam` (per-episode speed), fbm clouds on a sky plane tinted per episode, sun glow, 20-tap half-res rays. Tuned after screenshot review (rays ×0.9, scatter 0.18) |
| 4 | Real water + hot liquids | ✅ 2026-09-29 | Shore scan (≤ 64 taps + 3 refine; weapon/HUD pixels are looked through), mirrored row + ripples (world-anchored noise, eye height 41), fresnel, sparkles (sparse after review), foam, rain rings; murky = 35 % reflection; hot = flow warp + pulse (bloom glow). Note: E3's magenta floor is REKKR's own non-animated art (unchanged) |
| 5 | Atmosphere: fog, weather (rain/snow/embers/dust), lightning | ✅ 2026-09-29 | Exp fog (light 1/2600, medium 1/1300 × episode), weather layers occluded by depth, outdoors only (player sector ceiling = sky, 0.5 s fade), lightning in rain (2 flashes every 7–20 s). Auto: E1 clear, E2 rain, E3 embers, E4 dust |
| 6 | Dynamic lights (fullbright things, muzzle flash) | ✅ 2026-09-29 | ≤ 8 nearest fullbright mobjs within 1500 u (colour = bright-pixel average of the sprite, cached), muzzle flash from `ExtraLight`. Linux E3M1: 6 lights |
| 7 | Particles (sparks, blood, splashes, embers) | ✅ 2026-09-29 | ≤ 512, puffs → 8 sparks, blood → 6 drops, splashes when walking in water/murky, embers from hot floors near the player; GL quads depth-tested against the G-buffer (pass 4) |
| 8 | Voxile look: AO, filmic grade, DoF, Masterpiece preset, GRAPHICS tab page 2 | ✅ 2026-09-29 | AO 8 taps (strength 0.55), grade 3 "VOXILE (FILMIC)" (ACES fit + split tone), DoF (off by default). Presets: 3 = Masterpiece, Custom moved to 4 (`dev5_migrated`); Enhanced users on ≥ 8-core phones move to Masterpiece once; new installs: ≥ 8 cores → Masterpiece. GRAPHICS pages 3–4 (world effects). Test scenario 5 = Masterpiece tour |
| 9 | Build 0.5.0, QA, Test Lab (r8q + a weaker device), video, release (byte-verified), report | ✅ 2026-09-29 (weak device ⏭) | APK 100,040,073 B, versionCode 5, same cert. Test Lab r8q/33 scenarios 1+2+5: **Passed**, 0 ` E Unity`, 0 FATAL. S5 Masterpiece: E1M1 104.7 / E2M1 100.2 / E3M1 108.9 / E4M1 104.4 fps (dynres 400 lines); compare Enhanced 108.2 vs Masterpiece 102.7 fps, p99 17.0 ms both → gates (≥ 90 / ≥ 105) met. S1 100.8 fps p99 25.4 ms (now on Masterpiece; dev4 was 109.2 on Enhanced). S2 Arabic E1–E4 105.1 fps. Wheel → Shotgun, automap zoom 1.00→2.99 + follow OK. Video frames reviewed (washed-out frames = autopilot pressed against walls / pain flash, not bugs). Release v0.5.0 byte-verified. **Weak device (a15/34) not run: TEST_QUOTA_EXCEEDED** — see Open items |

### Open items after v0.5.0 (do these first in «أكمل dev5» or at the start of dev6)

1. Run `OUT=Artifacts/ftl-dev5-a15 SCENARIOS=5 sh tools/sandbox/ftl_gameloop.sh Builds/REKKR-0.5.0.apk a15 34` (Galaxy A15, Helio G99 / Mali-G57 MC2). Gate: ≥ 55 fps on its auto preset.
2. The auto-Masterpiece rule is only `cores ≥ 8`. Many weak-GPU phones have 8 cores. If the A15 misses the gate, also require a GPU class (Adreno ≥ 640, Mali-G7x/G710+/Immortalis, Xclipse) and give other phones Enhanced.
3. S1 p99 rose to 25.4 ms (spikes during saves or menu changes on Masterpiece). Profile it together with the IL2CPP gen0 GCs (~210 per run).

Legend: ⬜ not started · 🚧 in progress (see Next:) · ✅ done · ⏭ deferred (reason).

## 4. Stage specs

### Stage 0 — Plan, version
- `RekkrApp.Version = "0.5.0"`, ProjectSettings bundleVersion 0.5.0 / code 5, build with
  `REKKR_VERSION_CODE=5 REKKR_OUT=Builds/REKKR-0.5.0.apk`. Write `docs/DEV6.md`.

### Stage 1 — Automap touch + weapon wheel
- **Automap:** while the automap is open, a one-finger drag on the view pans the map (follow off),
  two-finger pinch zooms, a FOLLOW button (and double-tap) re-centres. Uses `AutoMap` state
  directly (visual only). The movement stick still walks.
- **Weapon wheel:** long-press (≥ 250 ms) on a WEAPON button opens a radial wheel with the owned
  weapons (their pickup sprites read from the WAD at runtime + the slot number); slide and release
  to select (same weapon-change path as tapping the status-bar ARMS cell). Short tap = next/prev.

### Stage 2 — G-buffer
- As in §2. `DrawScreen.GData` (byte per pixel) is allocated with `TexData`.
- Codes: walls `z = projection / scale` per column; flats `z = distance` per row; sprites
  `z = tz`; sky 248; psprites 249. Liquids use the flat's class.
- HeadlessTest `dev5` mode: render E1M1/E2M1/E3M1/E4M1 at several angles and check: sky codes
  only on sky pixels, water/hot codes appear on the known liquid sectors, depth of a wall column is
  monotonic along the wall, golden/demos unchanged.
- Linux player debug view `REKKR_GBUF=1` (false-colour G-buffer screenshot).

### Stage 3 — Living sky
- The painted sky drifts slowly (per-episode speed; offset added in the sky column lookup, so it
  also works without shaders), plus a procedural fbm cloud layer projected on a sky plane
  (parallax with look direction) tinted from the sky's own colours, wind direction per episode.
- Sun: per-episode direction/colour (E1 high white sun, E2 low orange, E3 none — red horizon glow,
  E4 low gold). Sun glow in the sky pass; **sun rays** (god rays) = 16-tap radial blur of the sky
  brightness at half resolution, added in the composite.

### Stage 4 — Real water + hot liquids
- Water pixel: find the shore row by scanning up the column (step 2 px, ≤ 64 taps, refine) until
  the first non-water pixel; reflected row = `shore − (y − shore)` + ripple offset (animated noise,
  stronger near the camera). Reflection colour × fresnel (grazing = more reflection), mixed with the
  water's own colour; if the mirror row leaves the frame or hits HUD → reflect the sky colour.
  Sparkles (sun-coloured specks), shore foam (bright band 1–3 px below the shore), rain rings when
  it rains. Reflections include the animated sky and monsters.
- Hot/toxic: emissive boost (so bloom picks it up) with a slow pulse, flow warp of the texture.

### Stage 5 — Atmosphere
- Fog: `1 − exp(−z · density)` towards a per-episode colour (derived from the sky's horizon
  colour), off/light/medium. Sky and weapon excluded.
- Weather layers (procedural, screen-space, 3 depth layers each occluded where the pixel depth is
  nearer than the layer): rain streaks (slanted, wind), snow flakes (drifting), embers (rising,
  glowing), dust motes (floating, sun-lit). Shown only when the player's sector has a sky ceiling
  (fades in/out over 0.5 s). Auto per episode: E1 clear (sea haze), E2 rain + lightning, E3 embers,
  E4 dust. Option: Auto / Rain / Snow / Off.
- Lightning (rain only): random (UnityEngine.Random, not the game RNG) flashes brighten sky and
  outdoor pixels for ~150 ms.

### Stage 6 — Dynamic lights
- Each frame (main thread): scan the world's mobjs (read-only); a thing whose current state is
  fullbright and within 1500 units becomes a light; colour = average of the bright pixels of its
  current sprite patch (cached), radius 160–320 by sprite size. Player weapon flash
  (`ExtraLight > 0`) = warm light at the player. The 8 nearest in view go to the shader.
- Shader reconstructs each pixel's view-space position from depth, adds
  `colour · albedo · (1 − d²/r²)²`. Interpolated positions (`GetInterpolatedX/Y`).

### Stage 7 — Particles
- Visual-only pool (≤ 512): new bullet puffs → 6–10 sparks; new blood → 6 drops; player/monster
  landing in liquid → splash; lava sectors near the player → rising embers; explosions (rocket-type
  deaths) → embers. Gravity, bounce on the floor height of the spawn sector, fade.
- Detected by diffing the mobj list each tic (read-only), never spawning game objects.
- Drawn as camera-facing quads into the world target in frame space, depth-tested against the
  G-buffer code in the fragment shader.

### Stage 8 — Voxile look + Masterpiece preset
- AO: 8 taps in a small ring, occlusion where neighbours are nearer, strength by depth ratio,
  applied to solid pixels only.
- Filmic grade "Voxile": ACES-like tone curve, warm highlights / cool shadows, +10 % saturation.
- DoF (off by default): pixels beyond a focus distance blend towards a blurred copy.
- GRAPHICS tab page 2: Sky (Original/Animated), Water (Classic/Real), Weather (Auto/Rain/Snow/
  Off), Fog (Off/Light/Medium), Lights, Sun rays, AO, Particles, Depth of field; grade gets "Voxile".
- Preset **Masterpiece** = Enhanced + everything (DoF off). Default on first start of 0.5.0 on
  devices with ≥ 8 cores (existing users keep their preset; a one-time notice offers it).

### Stage 9 — Build, test, release
- APK 0.5.0 → Test Lab r8q (scenarios 1+2; new scenario 3 = graphics tour: water, sky, weather, lights,
  with screenshots) + one weaker device. Release v0.5.0 from `feat/dev5`, byte-verified.

## 5. Checks (before every stage commit)
- HeadlessTest: `dotnet run -c Release -- ../../Assets/StreamingAssets/rekkr.wad <out>` → PASS
  (golden, DEMO1–4, 36 maps); `... <out> dev4` and `... <out> dev5` → PASS.
- Unity Linux build compiles; screenshots of the touched feature reviewed.
- `git diff ProjectSettings` clean except the intended version bump.

## 6. Bug log
| Date | Bug | Fix |
|---|---|---|

## 7. Log of decisions
- 2026-09-29: G-buffer in the alpha channel (zero extra upload) instead of a second texture.
- 2026-09-29: weather/particles never use the game RNG; everything visual-only.
- 2026-09-29: full 3D/voxel renderer is dev6 (`docs/DEV6.md`), not dev5.
