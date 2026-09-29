# DEV3 — REKKR for Android v0.3.0 (plan, spec and status)

> **ملخص للمالك (nall):** هذه ورقة dev3 الكاملة. إذا توقف Viktor لأي سبب، يكفي أن تقول لأي مطور أو وكيل
> ذكاء اصطناعي: **«أكمل dev3»**. يقرأ هذا الملف، ويبحث في جدول الحالة (§3) عن أول مرحلة غير مكتملة، ويكملها
> بنفس القواعد. الهدف: إصلاح الأخطاء + أفضل جرافيك ممكن + سلاسة قصوى، مع بقاء اللعبة أصلية 100 % (وضع
> **Classic** يعطي نفس صورة v0.2.0 بالضبط، ووضع **Enhanced** يشغّل كل التحسينات).

Owner approved this plan on 2026-09-29 («نعم موافق على خطتك»). No bugs reported by the owner yet
(he has not played long enough to be sure), so bug hunting is **automatic** (stage 1).

---

## 0. If you are the next developer: how to resume ("أكمل dev3")

1. Read `AGENTS.md` fully (build, sandbox setup §5, Test Lab §4, traps §6). Then read this file fully.
2. Work on branch **`feat/dev3`** (branched from `feat/dev2` @ `5eb431c`). Never commit to `main`;
   do not merge `feat/dev2`/`feat/dev3` into `main` without asking the owner.
3. Open §3 (status table). Take the **first stage that is not ✅**. Its spec is in §4.
4. Every stage ends with: the stage's checks pass (§5) → one commit `dev3: stage N — <title>` that also
   updates the status row (✅ + date + commit note + measured numbers) → push `feat/dev3`.
   A half-finished stage is committed with status 🚧 and a "Next:" note in its row, never silently.
5. Secrets you need from the owner (ask him in Slack, use once, never commit/log them, remind him to
   rotate): Unity account login (headless licence activation, AGENTS §5), a Firebase service-account
   JSON for project `ayoub-261d7`. The signing keystore lives outside the repo; if it is lost, **stop
   and ask** — a new keystore breaks updates over v0.2.0.
6. Firebase Spark plan = 5 physical runs/day. Verify everything locally first (HeadlessTest + Linux
   player under Xvfb) and spend Test Lab runs only on real candidate builds.
7. Report to the owner in **Arabic**, honestly: what was measured on the device vs. only locally, real
   fps with p99, and what was deferred.

## 1. Goals and hard rules

- **Original stays original.** `REKKR.WAD` is never modified. The game simulation (35 Hz tics, demos,
  saves) must stay bit-identical: DEMO1–4 must still play to the end in HeadlessTest after every stage.
  All improvements are presentation-only (renderer, post-processing, input latency).
- **Classic preset = v0.2.0 pixels.** With the Classic preset the 3D/2D frame must be byte-identical
  to v0.2.0 (checked by golden hashes, stage 0). Enhanced = everything on.
- **Performance target (r8q / Galaxy S20 FE, Adreno 650 — closest to POCO F3):**
  Enhanced default must hold **avg ≥ 110 fps and p99 ≤ 11 ms at 120 Hz**; if a feature can't, it
  goes to a lower default resolution or is off by default. Never ship a feature below 60 fps p99.
- **No half-done features.** If a stage can't reach good quality, disable it, document it here under
  "Deferred to dev4", and tell the owner. Stage 10 (dynamic lights) is the likely candidate.
- Local builds only (no Cloud Build, no GitHub Actions). CC BY-NC: no commercial packaging.
- APK growth budget: ≤ +5 MB over v0.2.0 (99.6 MB).

## 2. Current architecture (what you are changing)

| Piece | File | Notes |
|---|---|---|
| Tic loop + interpolation | `Scripts/RekkrApp.cs` `Update()` ~l.225 | `ticAccum`, max 6 tics/frame, `frac = ticAccum/TicTime` passed to render |
| Touch/gyro turn | `Scripts/TouchInput.cs` l.49–50 (`turnPixels`, `turnCarry`), l.245 (`gyroRadians`), `BuildTicCmd` l.532–566 | Turn is only applied when a tic is built → up to 28.6 ms visual lag |
| View angle | `Engine/Video/ThreeDRenderer.cs` `Render()` l.724 | `player.GetInterpolatedAngle(frameFrac)` |
| Software renderer | `Engine/Video/ThreeDRenderer.cs` (3069 lines) | Writes palette indices into `screenData` (byte[], column-major `x*height+y`). Lighting = `colorMap[level]` byte tables (32 levels → visible bands) |
| Frame size | `Engine/Video/Renderer.cs` ctor, `DrawScreen(wad, w, 400)` | Height fixed at 400 (scale 2). Width = Hor+ (`round(1.2·400·aspect)`, 1066 on 2400×1080) |
| Palette → RGBA | `Renderer.WriteData` ~l.300–315 | `p[i] = colors[screenData[i]]`, palette chosen per damage/bonus/radsuit |
| Upload + draw | `Scripts/UnityVideo.cs` (`SetPixelData` + `Apply` every frame), `Scripts/RekkrApp.UI.cs` l.63 `Graphics.DrawTexture(gameRect, tex, screenMat)` in `OnGUI` | Texture is transposed (w=frame height) |
| Screen shader | `Resources/Rekkr/RekkrScreen.shader` | Sharp-bilinear only |
| Frame rate | `Scripts/DisplayRate.cs` | `targetFrameRate`, `vSyncCount=0`, 60/90/120 |
| Graphics API | `Editor/RekkrBuild.cs` l.52–53 | GLES3 only |
| Settings | `Scripts/RekkrSettings.cs` (PlayerPrefs), `Scripts/RekkrApp.UI.cs` tabs l.201–300, strings `Scripts/Loc.cs` (EN + AR) | Tabs: Controls / Gyro & Vibration / Display & Sound |
| Test autopilot | `Scripts/RekkrApp.Test.cs` | Scenario 1, 2; summary line `[REKKR-TEST] scenario=… avg_fps=… p99_frame_ms=…` |
| Engine tests | `tools/HeadlessTest/Program.cs` (compiles `Assets/Rekkr/Engine/**`) | DEMO1–4, all 36 maps, widescreen, save/load |

## 3. Status table (update in every stage commit)

| # | Stage | Status | Notes / measured |
|---|---|---|---|
| 0 | Safety net: golden hashes, timing instrumentation, version bump | ✅ 2026-09-29 | `tools/HeadlessTest/golden.txt` = 176 frame hashes of the v0.2.0 renderer (wipe seed fixed via `WipeEffect.TestSeed`); summary line now has `render_ms_*`, `upload_ms_*`, `gc0`, `thermal`, `api`. Linux llvmpipe baseline 1066×400: render 4.14 ms avg / 5.70 p99, upload 0.43 ms, gc0=4 in scenario 1 |
| 1 | Automatic bug hunt: HOM scan, all-maps soak, save/load all maps, fixes | ✅ 2026-09-29 | 2 real bugs fixed (§6): **save crash on E1M7** (buffer overflow) and **see-through wall columns** in 4 textures; saves now atomic. All 36 maps: 2000-tic bot soak + byte-exact save→load→save round trip PASS. HOM scan at 1066/640 from 5744 thing positions per width: only vanilla 1–9 px sparkles + one E4M1 voodoo-machinery closet (not reachable). Golden unchanged. Lifecycle review items moved to stages 5/9/11 (see stage 1 spec) |
| 2 | Per-frame look (touch + gyro at render rate) | ✅ 2026-09-29 | `Renderer.LocalViewTurn` + `TouchInput.PendingTurn`; setting "Smooth look" (Gyro & Vibration tab — Controls tab is full, default on, EN/AR). Autopilot turning now feeds the swipe path. Linux A/B scenario 1: view lag avg **1.97° → 0.06°**, p99 **15.3° → 1.4°**. Golden unchanged (demos use the classic path). Device check in stage 11 (`look_*` fields) |
| 3 | Frame pacing, Vulkan, sustained performance, zero-GC frame | ✅ 2026-09-29 (device numbers in stage 11) | Zero-copy: renderer writes straight into `GetRawTextureData` → upload CPU **0.54 → 0.01 ms** (Linux). Swappy `optimizedFramePacing` on (`REKKR_FRAMEPACING=0` to disable). `REKKR_GFX_API=vulkan` builds Vulkan+GLES3; default stays GLES3 until the stage 11 A/B on r8q. `PerfMode.SetSustained` + setting `StablePerf` (UI in stage 9, default off). Per-frame string allocations removed (FPS label, Arabic icon keys); `gc0` still 4 per ~2.5 min scenario (IMGUI internals) — incremental GC on, acceptable |
| 4 | Multithreaded renderer (column strips) | ✅ 2026-09-29 | `ThreeDRendererPool` (2×threads strips, long-lived workers, caller works too); per-instance sector valid-count; sprites/weapon clamped to strip. HeadlessTest `bench` 1066×400 (x86 sandbox): **1.82 → 1.16 / 0.81 / 0.69 ms** for 1/2/4/6 threads; Unity Mono player render 4.06 → 2.87 ms. HOM scan with 4 threads: no seam gaps (43 vs 45 bad views, all vanilla specks). Setting `RenderThreads` (0 auto = min(4, cores−1)), env `REKKR_THREADS`. Not bit-identical to 1 thread — see spec note |
| 5 | Resolution levels 400/600/800/1000 + dynamic resolution | ✅ 2026-09-29 (UI rows in stage 9) | `Renderer(config, content, width, lines)`; `UnityVideo` caches one renderer+texture per size; `RekkrApp.DynRes.cs` controller (85 %/1 s down, 55 %/4 s up, missed-frame + thermal ≥ SEVERE down, logs `[REKKR] dynres A->B`). `WriteData` now parallel. HeadlessTest bench (x86, 4 threads): 1066×400 0.81 ms, 2134×800 **1.90 ms**, 2666×1000 2.91 ms. HOM scan at 600/800/1000 × 36 maps: only E4M1 closet + ≤67 px one-row plane/wall seams (vanilla precision, 1 physical px). Automap lines `height/400` px thick. Settings `Resolution` (default 800) + `DynamicRes` (default on); env `REKKR_LINES`, `REKKR_DYNRES=0`. Linux 800 lines: dynres stepped 800→600→400 on llvmpipe (GPU-bound) as designed |
| 6 | True-colour smooth lighting | ✅ 2026-09-29 | Continuous light curves (same formulas, no truncation, +0.5 level = vanilla mean) blend the two COLORMAP rows around each pixel's level; 3D writes texel + light (`DrawScreen.TexData/LightData`), `Renderer.WriteChunk` blends with flat LUTs, 2D overdraw detected by palette-index mismatch; palette flashes automatically correct. `lightcmp`: mean tone change ≤ 0.89 %, unique colours per frame ×5–6 (136 → 842). **Cost** (x86, 4 threads, 2134×800): 2.0 → 5.0 ms (3D extra stores +0.9, blend write +2.1) → on phones dynres will likely hold 600 lines with it at 120 Hz. Setting `SmoothLighting` (default on), env `REKKR_TRUECOLOR`. Golden (off) unchanged |
| 7 | GPU post-processing (bloom, vignette, grading, sharpen, CRT) | ✅ 2026-09-29 (GPU cost measured in stage 11) | `PostFx.cs` + `Resources/Rekkr/RekkrPost.shader`: sharp upscale into a screen-size RT, 5-level bloom pyramid (threshold 0.72, Low/Med/High 0.35/0.6/0.9), contrast/saturation/warmth grade (Neutral/Vivid/Warm), CAS-style sharpen only when upscaling, vignette 0–30 %, CRT scanlines + aperture mask. All off ⇒ v0.2.0 direct draw. Linux screenshots OK (orientation, colours, CRT). llvmpipe is a software GPU (7.5 fps with post), so GPU numbers only from the device. Env `REKKR_POST=0`, `REKKR_CRT=1` |
| 8 | Blurred side-fill for 4:3 screens | ✅ 2026-09-29 | `Renderer.LastFrameCentred` (set by `ClearIfWide`: title/credits, intermission, finale) → composite pass samples a 1/16 blurred copy of the centre stretched over the sides, ×0.42, soft edge. Verified on the REKKR title screen (scenario 1 shot `00_titlepic`) |
| 9 | Graphics tab + Classic/Enhanced presets + first-run auto preset | ✅ 2026-09-29 | 4th tab GRAPHICS, 2 pages (main: preset, max resolution + current lines, dynamic res, smooth lighting, stable performance, Effects ▶; effects: bloom, colours, vignette, sharpen, CRT, side fill, BACK). Presets Classic (= v0.2.0 exactly, 1 render thread) / Balanced / Enhanced / Custom (auto when a row is changed). First start of 0.3.0 → Enhanced on ≥ 6 cores, else Balanced (no benchmark — simpler; dynres covers slow phones). EN + AR screenshots checked. Also fixed the test screenshots being one step late (`Shot(); yield return null;`) |
| 10 | Dynamic lights (optional; defer to dev4 if not good) | ⏭ deferred to dev4 | Smooth lighting already costs ~3 ms/frame at 800 lines on the CPU (stage 6); per-pixel lights on top would push phones below the 120 Hz budget. Right way: first move palette/light blending to the GPU (stage 6 note), then add lights there. Not shipped half-done |
| 11 | Build v0.3.0, Test Lab (scenarios 1–4), video, release, report | 🚧 | APKs built 2026-09-29: `REKKR-0.3.0.apk` (GLES3, sha256 `7f937c33…e9c629`, 99.8 MB) and `REKKR-0.3.0-vulkan.apk` (Vulkan+GLES3, `da75ec3e…06c5`), versionCode 3, cert `58c71163…248a9d0`. Scenario 3 (bench) + 4 (soak) added and run locally. **Next:** Firebase quota resets 00:00 UTC → run A: GLES `SCENARIOS=1,2,3 FTL_TIMEOUT=20m`; run B: Vulkan `SCENARIOS=3`; run C: winner `SCENARIOS=4 FTL_TIMEOUT=30m`; pick default API, check video/logcat (` E Unity`, FATAL), release from `feat/dev3` |

Legend: ⬜ not started · 🚧 in progress (see Next:) · ✅ done · ⏭ deferred to dev4 (reason).

Deferred to dev4: stage 10 dynamic lights (needs GPU lighting first); GPU palette/light blending; runtime Vulkan/GLES switch.

## 4. Stage specs

### Stage 0 — Safety net
- Bump version: `RekkrApp.Version = "0.3.0"` (RekkrBuild uses it for versionName), build with
  `REKKR_VERSION_CODE=3`.
- **Golden hashes** (done): HeadlessTest modes `golden-write` / `golden-check` render fixed frames —
  the attract loop with all 4 demos every 105 tics (+ interpolated frac 0.5 frames), and each E?M1
  start with HUD sizes 7/9/8/5, frac 0.25, automap and menu — at 1066×400 and 640×400, and store the
  SHA-256 (16 hex) of the RGBA output in `tools/HeadlessTest/golden.txt`. The normal full run checks
  it automatically. The wipe RNG was time-seeded, so tests set `WipeEffect.TestSeed`. Regenerate
  golden **only** when the Classic look is intentionally changed (e.g. a real bug fix) and note it
  in §7.
- **Timing instrumentation**: in `RekkrApp.Update` measure with `Stopwatch` the CPU time of
  `video.Render` (render_ms), of the upload (upload_ms), and GC count delta. Add to the
  `[REKKR-TEST]` summary: `render_ms_avg`, `render_ms_p99`, `upload_ms_avg`, `gc0`, and the thermal
  status at the end. These numbers drive stages 3–6 (the 120 fps cap hides real headroom).
- Check: HeadlessTest all PASS, golden generated, Linux player scenario 1 runs and prints new fields.

### Stage 1 — Automatic bug hunt (owner reported none yet)
1. **HOM / unwritten-pixel scan** (HeadlessTest): for every one of the 36 maps, from the player start
   plus ~40 sample points (random reachable subsector centres, 8 view angles each), render the 3D view
   twice — once after filling the buffer with index 0 and once with index 255. Any pixel that differs
   between the two renders inside the view window was never written = HOM/gap. Run at 1066×400,
   640×400, and (after stage 5) 600/800/1000 lines. Report map, position, angle, pixel count; save a
   PNG for each hit. Fix real renderer bugs (widescreen edge columns, `xToAngle` at the edges, sky at
   Hor+ edges, overflow at high res). Known non-bug: E3M1 opening area is very dark by design.
2. **All-maps soak**: HeadlessTest warps to each map, runs 2000 tics with a random-walk bot
   (move/turn/fire/use) — no exceptions, no NaN/huge mobj coordinates.
3. **Save/load everywhere**: for each map, after 500 tics save → load → run 200 tics on both the
   original and the loaded game with the same tic commands → world state hash must match.
4. **Device soak = Test Lab scenario 4** (added in stage 11): 20 min of play across maps with warps,
   logging fps per minute, memory (`Profiler.GetTotalAllocatedMemoryLong`/GC count) and thermal
   status. Looks for leaks, heat throttling, stutters.
5. **Code review pass** for mobile lifecycle: pause/resume (audio restart, autosave), app killed
   during save (write to temp file then rename), back button in every state, notch/cutout safe area
   for buttons, orientation flip (landscape left/right), locale Arabic in every settings row,
   automap line thickness at higher resolutions (1 px lines get thin at 800 lines → draw `scale/2`
   px thick).
- Every real bug found gets a row in §6 (bug log) with its fix commit.
- Result notes (2026-09-29): sampling random subsector centres gave many false positives (sealed
  dummy sectors, self-referencing "deep water"/fake-floor tricks, voodoo/conveyor closets — they
  draw nothing in vanilla too). The scan therefore samples **map thing positions** (where the
  designer put items/monsters) restricted to sectors connected to the start. 1–9 px specks at wall
  seams are vanilla fixed-point "sparkles", identical at 640 (original engine path) — kept in
  Classic; re-check at high resolution in stage 5.
- Still to do in later stages (not automated here): automap line thickness at 600+ lines (stage 5);
  notch/safe-area + Arabic rows (stage 9); pause/resume audio and back button in every state
  (stage 11 device run + code review).

### Stage 2 — Per-frame look (biggest "feel" improvement)
- Problem: touch swipe and gyro only change the view when a tic is built (35 Hz), and the renderer
  then interpolates the angle → up to ~28 ms extra lag and stepping at 120 Hz.
- Fix (Crispy Doom "local view" style, visual only — the sim still gets the same tic commands):
  - `TouchInput` exposes `PendingTurnBam` = the turn that is accumulated since the last tic
    (`-turnPixels*perPixel + turnCarry + gyro`), converted exactly like `BuildTicCmd` does, **without
    consuming it**.
  - `ThreeDRenderer.Render` gets an optional `Angle? localView`. For the console player in a live game
    (not demo playback, not dead, not automap, menu closed) and when the last tic's turn came from
    touch/gyro, use `viewAngle = player.Mobj.Angle (current tic, not interpolated) + PendingTurnBam`.
    Otherwise keep `GetInterpolatedAngle(frameFrac)` (keyboard/gamepad turn stays smooth).
  - `BuildTicCmd` keeps building the identical command, so demos/saves are unaffected.
  - Beware of Doom's `angleturn` 16-bit quantisation: the remainder already goes to `turnCarry`; the
    local view must include that carry so there is no snap-back after each tic.
- Setting: `Smooth look` (on in both presets; it changes feel, not look).
- Check: golden hashes unchanged (demo path untouched); Linux player with autopilot constant turn —
  log per-frame view angle deltas: must be monotonic and even (no 35 Hz steps); DEMO1–4 PASS.
- Done as specified. Summary line fields: `smooth_look`, `look_step_deg`, `look_step_cv`,
  `look_lag_deg_avg/p99`. Desktop A/B: `REKKR_SMOOTHLOOK=0`. `RekkrBuild.BuildLinux` now resets
  `runInBackground` after the build, and versionCode defaults to the minor version (0.3.x → 3).

### Stage 3 — Frame pacing, Vulkan, sustained performance, zero-GC
- `PlayerSettings.Android.optimizedFramePacing = true` (Swappy) and keep `targetFrameRate` = display
  rate; compare frame-time variance on device with and without.
- Graphics APIs: ship **Vulkan + GLES3** (`SetGraphicsAPIs(Android, {Vulkan, OpenGLES3})`). Unity picks
  the first supported at startup; there is no runtime switch in 2022.3. Option "Graphics API: Auto /
  Vulkan / OpenGL ES" is implemented by relaunching the activity with the `unity` intent extra
  (`-force-vulkan` / `-force-gles`) — **verify that release builds honour it**; if not, drop the
  option and pick the better default from Test Lab numbers (run scenario 3 on both).
- Upload: write the RGBA frame straight into `texture.GetRawTextureData<uint>()` (unsafe pointer,
  `allowUnsafeCode` in the asmdef) instead of `byte[]` + `SetPixelData` (saves one full-frame copy);
  consider 2 textures in round-robin to avoid GPU sync stalls.
- Sustained performance: `Window.setSustainedPerformanceMode(true)` via JNI when supported (option
  "Stable performance", default on for long sessions); read thermal status
  (`PowerManager.getCurrentThermalStatus`, API 29+; headroom API 30+) and feed it to dynamic
  resolution (stage 5). Optional "Battery saver: 60 fps" toggle.
- Zero-GC frame: no per-frame allocations in `Update`/`OnGUI` (cache strings like the FPS label, avoid
  LINQ/closures/boxing, reuse `GUIContent`). Enable incremental GC (`PlayerSettings.gcIncremental`).
  Target: `gc0` delta = 0 during 60 s of gameplay.
- Runtime API switch via the `unity` intent extra was **not** implemented (unverifiable in the
  sandbox); decide the default API from the stage 11 A/B instead.
- Check audio thread: MeltySynth with the 32 MB GeneralUser GS must not cause frame spikes (measure
  render p99 with music HQ on/off).

### Stage 4 — Multithreaded renderer (column strips)
- SD870 = 1+3 big A77 cores + 4 little A55. Split the 3D view into **vertical strips** rendered in
  parallel; 2D (status bar, menus, HUD) stays single-threaded after the 3D pass.
- Design: N independent `ThreeDRenderer` "workers" sharing the read-only content (textures, flats,
  sprites, colormaps) and the same `screenData`, each owning all mutable state (clip ranges,
  openings, visplanes, drawsegs, vissprites, `upperClip/lowerClip`, fuzz position). Each worker walks
  the BSP but seeds its solid-seg clip list with everything outside `[x0, x1)`, and clamps sprite /
  masked / weapon / plane spans to its strip. Traversal is duplicated (cheap); drawing is split.
- Shared-state traps to fix:
  - `Sector.ValidCount` (used by `AddSprites`, l.2465) is written during rendering → give each worker
    its own `int[] sectorValid` indexed by sector number.
  - `world.GetNewValidCount()` → call once per frame on the main thread.
  - Check `SpriteLookup` cache (`SpriteLookup.cs` l.181) and any other lazy cache for thread safety
    (textures are composited at load time, OK).
- Threads: dedicated long-lived worker threads + `ManualResetEventSlim`/`Barrier` (not the thread
  pool, not `Parallel.For` per frame — too much overhead on IL2CPP). Use 8 strips pulled from a queue
  by 4 threads (load balancing: the sky/sprite-heavy side of the screen differs a lot). Setting
  `Render threads: Auto (=4) / 1 / 2 / 4 / 6`.
- ~~Must be pixel-identical~~ — **finding:** floors/ceilings are drawn with a per-row
  incremental texture-coordinate cache (`ceilingXFrac/Step`, `floorXFrac/Step`) that restarts at
  each strip's first column, so threaded frames differ from 1 thread by ±1 texel rounding on some
  plane pixels (≈1–2 % of pixels, invisible; `tdiff 4` shows them). Fuzz order also differs.
  Therefore **Classic preset forces 1 thread** (golden stays exact); Balanced/Enhanced use auto.
- Tools: `REKKR_THREADS=<n>` for any HeadlessTest mode, `tdiff <n>` (1 vs n threads diff PNG),
  `bench <width>` (ms/frame for 1/2/4/6 threads over 6 maps × 8 angles).
- Next speed-up candidate (stage 5): `Renderer.WriteData` (palette → RGBA) is single-threaded;
  split it across the same workers once frames reach 1.7–2.7 Mpx.
- Check: golden PASS with threads=1 and threads=4, HOM scan PASS, local speed-up measured in
  HeadlessTest (report ms/frame for 1/2/4 threads).

### Stage 5 — Resolution levels + dynamic resolution
- Levels: **400 / 600 / 800 / 1000 lines** (scale 2/3/4/5). Heights must be multiples of 200 because
  2D patches (status bar, menus, fonts) use integer scale (`DrawScreen.DrawPatch(..., scale)`).
  Width stays Hor+ `round(1.2·H·aspect)` (even): on 2400×1080 → 1066 / 1600 / 2134 / 2666.
  800 lines ≈ native on a 1080p phone; 1000 is supersampling (only if headroom). (The dev3 proposal
  said "1080"; 1000 is the nearest integer-scale level.)
- `Renderer` ctor takes the height instead of the `video_highresolution` 200/400 switch. Audit
  hard-coded 200/400/320 values: `ThreeDRenderer` l.53/73/310/342 already derive from
  `screenHeight/200`; check wipe, automap, intermission, finale, menu, status bar, sky scaling, weapon
  sprite position, `upperClip/lowerClip` as `short` (fine), fixed-point overflow at close walls
  (`rw_scale` clamp, texture column wobble) — run the stage 1 HOM scan at every level.
- **Dynamic resolution** (option, on in Enhanced): renderers for each level are created lazily and
  cached; switch only between frames and never during a wipe/menu. Controller on `render_ms` EMA vs
  budget (1000/target fps): > 85 % for 1 s → one level down; < 55 % for 4 s → one level up (never
  above the user's max); thermal status ≥ SEVERE forces one level down. Log `[REKKR] dynres A->B`.
- Upload cost grows (2134×800×4 = 6.8 MB/frame); stage 3 zero-copy upload is a prerequisite. In the
  Classic (palette) path an alternative is uploading the **8-bit index buffer (R8) + 256×N palette
  texture** and doing the lookup in the shader (4× less bandwidth).
- Check: golden PASS at 400; HOM scan PASS at all levels; Linux screenshots at each level; FTL
  scenario 3 numbers.

### Stage 6 — True-colour smooth lighting
- Goal: remove the 32-step light banding (visible in dark REKKR areas) while keeping REKKR's exact
  colour tone. Do **not** invent a lighting model: derive it from the WAD's own `COLORMAP`.
- Method: for palette index `t` and continuous light `L ∈ [0, 31]` (computed from the same
  distance/scale formulas, but without truncation to an integer level):
  `rgb = lerp(pal[cm[floor(L)][t]], pal[cm[floor(L)+1][t]], fract(L))`. At integer L this equals
  the original. Precompute a `uint[32 * 256]` RGB table per gamma level; per pixel it's 2 lookups +
  a lerp with 8-bit weight (or use 128 sub-levels in a `uint[128*256]` table = 1 lookup).
- Output: in true-colour mode the 3D pass writes 32-bit pixels into a `uint[]` view buffer; the 2D
  pass (HUD, status bar, messages, menu) still draws palette indices into `screenData` and marks an
  **overlay mask** in `DrawScreen` (every `DrawPatch/DrawColumn/FillRect/DrawText` sets the mask);
  `WriteData` takes the overlay palette pixel where the mask is set, else the true-colour pixel.
- Special cases: palette effects (damage red / bonus gold / radsuit green, PLAYPAL 1–13) → apply as
  a tint fitted from PLAYPAL vs palette 0 (least squares per palette, done at load) in `WriteData` or
  the shader; invulnerability/fixed colormaps → use the colormap row directly (banded, like the
  original); fuzz (spectre) → multiply the destination pixel by the colormap-6 factor; sky and
  full-bright → unchanged.
- Implementation note: the overlay mask was replaced by the cheaper "index mismatch" test (a 2D
  pixel equal to the 3D banded index keeps the smooth colour — visually identical). Fuzz stays
  banded (mismatch). Wipes use the palette path.
- **Faster option for dev4 / if phones are too slow:** move the palette + blend to the GPU — upload
  the 8-bit index frame (R8) + texel/light (RG8) and do `mix(pal[cm[row][t]], pal[cm[row+1][t]], f)`
  in `RekkrScreen.shader` with 256×1 palette and 256×32 colormap textures. Removes the whole CPU
  write-out (also for Classic) and 25 % of the upload bandwidth.
- Optional extra: 8×8 ordered dither on the light fraction when using 32 levels (no banding, no cost).
- Must work with stage 4 threads and stage 5 levels. Classic preset keeps the byte path (golden).
- Check: golden PASS in Classic; side-by-side screenshots (E1M1, E3M1 dark area, E4 outdoor) show no
  banding and same average colour (mean RGB difference < 2 % vs Classic per image).

### Stage 7 — GPU post-processing
- Replace the single `Graphics.DrawTexture` with a small pipeline of `RenderTexture`s and
  `Graphics.Blit` run before `OnGUI` draws (UI buttons must stay un-processed on top):
  1. Upscale pass to the game rect size with the existing sharp-bilinear (+ optional **CAS-style
     sharpening** when frame < native).
  2. **Bloom**: bright-pass (luminance soft threshold ~0.7), 5-level dual-Kawase down/up pyramid at
     ½…1/32, additive composite; intensity Low/Medium/High (Classic off). Makes fireballs, torches,
     full-bright sprites glow.
  3. **Colour grading**: a 16³/32³ LUT generated in code (contrast, saturation, warm/cool) —
     Neutral / Vivid / Warm; default Vivid-light in Enhanced.
  4. **Vignette**: subtle, 0–30 %.
  5. **CRT** (optional, off by default): scanlines at physical-pixel pitch, aperture mask,
     slight curvature toggle.
  - Keep palette flashes (damage/bonus) intact (they are in the frame already).
- Mobile budget: all post ≤ 1.5 ms GPU at 2400×1080 on Adreno 650. Use half-precision, small RTs,
  no MRT. Fallback: disable post if `SystemInfo.graphicsShaderLevel` too low.
- Check: Linux player screenshots with each effect; FTL scenario 3 with post on/off.

### Stage 8 — Blurred side-fill for 4:3 screens
- When a centred 4:3 screen is shown on the wide frame (title/opening, intermission, finale — where
  `Renderer.ClearIfWide` runs), the sides are black today. Expose `Renderer.LastFrameCentred` and the
  content rect; the screen shader samples a blurred (1/8 downsample + 2 blur passes), darkened
  (×0.45) copy of the centre stretched over the full width outside the content rect.
- Option "Side fill: Black / Blur" (Blur in Enhanced).

### Stage 9 — Graphics tab, presets, first-run auto preset
- Layout limit: the settings panel fits **6 rows per tab** (rowH = 0.093·H from 0.25·H; the DONE
  button starts at 0.835·H). The Graphics tab has more rows → split it into two pages
  ("Graphics 1/2" with ◀ ▶) or add a scroll view. Smooth look already lives in Gyro & Vibration.
- New 4th settings tab **Graphics** (`tab_graphics`), all strings in `Loc.cs` EN + AR (use the
  existing `ArabicShaper`). Settings in `RekkrSettings` (PlayerPrefs keys `gfx_*`):
  Preset (Classic / Balanced / Enhanced / Custom), Resolution (Auto, 400, 600, 800, 1000),
  Dynamic resolution, Render threads, Lighting (Classic / Smooth), Bloom, Colour, Vignette, Sharpen,
  CRT, Side fill, Dynamic lights (if stage 10 ships), Graphics API (if stage 3 option works),
  Stable performance, Smooth look (Controls tab).
- Presets: **Classic** = exactly v0.2.0 (400 lines, banded, no post, black sides).
  **Balanced** = 600 + smooth light + sharpen + side fill. **Enhanced** = 800 + dynamic res + smooth
  light + bloom low + vivid + vignette 15 % + sharpen + side fill. Editing any row → Custom.
- First run: 5-second silent benchmark on the title demo picks Enhanced if render p99 fits, else
  Balanced. Existing v0.2.0 users (PlayerPrefs present, no `gfx_preset`) get the same auto choice
  once, with a one-line toast "Graphics: Enhanced (change in Settings)".
- Check: every row readable in Arabic and English at 2400×1080 and 1600×720; screenshots.

### Stage 10 — Dynamic lights (optional)
- Only in true-colour mode. Light sources: projectiles and explosion states, full-bright
  decorations (torches, lamps, candles — detect by thing type/state with full-bright frames); colour
  = average colour of the full-bright sprite frame (automatic, keeps REKKR's own colours); radius by
  type (128–256 units), max 16 lights per frame, culled by distance and sector reachability.
- Walls: per column compute world x,y from the seg and add light at ≤ 4 z samples (interpolated).
  Flats: per span, per 8-pixel block. Sprites: one sample at the sprite centre. Additive on the
  smooth-light result, clamped. No shadows.
- Acceptance: ≤ 2 ms extra render time at 800 lines on r8q and no visible blockiness; otherwise mark
  ⏭ deferred to dev4 and ship without it.

### Stage 11 — Build, device test, release
- Test Lab scenarios: keep 1 and 2; add **3 = graphics benchmark** (fixed routes in E1M1 and the
  heaviest map found by HeadlessTest timing; 20 s per config: Classic 400, 600, 800, 1000, Enhanced
  800, Enhanced 1000; lines `[REKKR-TEST] bench cfg=… avg_fps=… p99_frame_ms=… render_ms_p99=…`)
  and **4 = 20-minute soak** (warp across maps, fps per minute, memory, thermal). Update
  `ftl_gameloop.sh` defaults (`SCENARIOS=1,2,3`; 4 as a separate run with `FTL_TIMEOUT=30m`).
- Build: `REKKR_VERSION_CODE=3 REKKR_OUT=Builds/REKKR-0.3.0.apk` (see AGENTS §3); verify with
  `apksigner` (cert `58c71163…248a9d0`), `aapt dump badging`, `sha256sum`.
- Video: Test Lab `video.mp4` → ffmpeg `fps=30, crf 28, -an`, upload to Slack with the report.
- Release `v0.3.0` from `feat/dev3` with notes (Classic vs Enhanced, measured numbers, what was not
  verified) and `SHA256SUMS`; download the asset back and re-check sha256.
- Update `AGENTS.md` §1 (v0.3.0 status + numbers) and this file (all rows ✅/⏭).

## 5. Checks (run before every stage commit)

```sh
cd tools/HeadlessTest && dotnet run -c Release -- ../../Assets/StreamingAssets/rekkr.wad /tmp/ht   # all PASS
dotnet run -c Release -- ../../Assets/StreamingAssets/rekkr.wad /tmp/ht golden-check "$PWD/golden.txt"
dotnet run -c Release -- ../../Assets/StreamingAssets/rekkr.wad /tmp/ht hom 1066,640        # HOM scan, PNGs + hom_report.txt
dotnet run -c Release -- ../../Assets/StreamingAssets/rekkr.wad /tmp/ht texholes              # textures with empty columns on solid walls
```
- Linux player QA under Xvfb (AGENTS §5): scenario 1 and 2 screenshots, no exceptions in the log.
  llvmpipe fps is not a performance number.
- Android build + Test Lab only for candidate builds (quota).

## 6. Bug log

| Found in | Bug | Fix |
|---|---|---|
| stage 1 soak | **Saving crashed on E1M7** (and could on other big maps): `SaveAndLoad` used a fixed 360 KB buffer, E1M7 needs 418 KB → `IndexOutOfRangeException`. Quick save/autosave silently failed there; a save from the Doom menu threw inside the tic loop (game stopped). | Buffer sized from sectors/lines/thinkers before writing; save written to `.tmp` then renamed (no truncated saves on app kill) |
| stage 1 scan | 4 textures used as **solid** walls have columns no patch covers (`DVMIDBLD` E1M1, `BLODGR1`/`DVPNT1`/`DVPNT2` E1M7) → those columns were never drawn (HOM smear) | `Texture.SolidColumns`: empty columns borrow the nearest covered column for solid walls only (masked middles stay see-through). `holefix` test: 728 → 0 unwritten px. Golden unchanged |
| stage 1 scan | Many wall parts use "-" where heights need a texture (E4M1 571, E1M7 405, …) | Not a bug: vanilla tricks (flat bleeding/deep water); left as in the original |

## 7. Log of decisions

- 2026-09-29: plan approved by owner. Branch `feat/dev3` from `feat/dev2` (`5eb431c`). Resolution
  levels are multiples of 200 lines (integer patch scale) → 1000 instead of the proposed 1080.
