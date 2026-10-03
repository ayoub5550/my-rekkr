# AGENTS.md — my-rekkr developer and agent handoff

Last updated: 2026-10-03 (v0.8.0, preparation tooling).

> **Marketplace submission preparation:** the owner has authorized the commercial
> Android submission. `tools/make_review_package.py --edition marketplace`
> creates a submission-labelled archive; the default remains `review`.
> Neither mode changes upstream notices or certifies marketplace approval.
> Buyer terms/context are in `docs/buyer/DISTRIBUTION.md`; factual listing fields
> are in `docs/marketplace/listing.json`. There are now 16 packaging/wrapper tests.
> No new Unity build is implied by those tests.
>
> **Preparation tooling (not a marketplace release):** English technical guides
> are in `docs/buyer/`, and the unpublished listing/checklist is in
> `docs/marketplace/DRAFT.md`. `tools/make_review_package.py` creates a review-only
> ZIP outside the checkout, retaining licences and rejecting common secret paths,
> signing settings, symlinks and unresolved LFS pointers. No gameplay or licence
> terms changed. `build_android.sh` now derives output/version-code defaults from
> `RekkrApp.Version`, rather than hard-coding v0.1.0.
> Run `python3 -m unittest discover -s tools/tests -v` (14 tests at this checkpoint).
> On 2026-10-03 the .NET 10 full engine suite passed: 36 map-load checks, 36
> soak/save-load checks and 176 golden frames. Unity/Android was not rebuilt for
> this preparation change; do not describe the wrapper stub tests as a Unity build.

> **dev8 (v0.8.0) — merged into `main`** (owner approval 2026-09-30): the animation layer, "modern but still classic",
> all **visual only** (HeadlessTest golden PASS). New settings tab **ANIMATION** (Classic = original / Modern = default /
> Custom): smooth per-frame weapon (was 35 Hz), sway / breathing / strafe tilt / landing dip, per-weapon recoil, eased
> switch, camera shake + hit kick + damage-direction marks, strafe lean (Remaster only, off), floating / glowing pickups,
> monster hit flash, impact chips / dust, richer blood, explosion smoke / embers / debris, liquid cross-fade, panel /
> wheel / HUD-number transitions. **Touch buttons are not animated (owner decision).** New test scenario 12. Plan,
> settings table, code map and measurements: [`docs/DEV8.md`](docs/DEV8.md). Open item: physical r8q Test Lab run.
>
> **dev7 (v0.7.0) — the first release merged into `main`** (owner approval 2026-09-30): fixes for the owner's 0.6.0
> reports (settings not applied on demos / smooth lighting in Remaster, far too strong dynamic lights, bad fog and
> weather), Remaster point-light shadows + 3D weapon, save backup (document picker), full gamepad + remap. Plan, status
> and measurements: [`docs/DEV7.md`](docs/DEV7.md). New test scenarios 8 (settings coverage), 9 (backup round trip),
> 10 (Remaster weapon / lights / door + lift), 11 (all 36 maps in Remaster).
>
> **dev5 (v0.5.0) was released 2026-09-29 from branch `feat/dev5`** (GitHub release v0.5.0): the G-buffer in the frame
> alpha feeds GPU effects: living sky and sun rays, reflective water, hot liquids, fog, weather and lightning, dynamic
> lights, particles, AO, the Voxile grade and the Masterpiece preset; plus the weapon wheel and automap touch. Plan and
> measurements: [`docs/DEV5.md`](docs/DEV5.md), including open items (weak-device test). **Next = dev6** (GPU 3D
> "Remaster" renderer + voxel/extruded things), fully specified in [`docs/DEV6.md`](docs/DEV6.md). When the owner says
> «نفذ dev6» or «أكمل dev6», follow its status table. No feat branch is merged into main: ask the owner first.
>
> **dev4 (v0.4.0) was released 2026-09-29 from branch `feat/dev4`** (GitHub release v0.4.0): free look, jump,
> fixed joystick, crosshair, carved-stone UI with the WAD pixel font. Plan, specs, measurements and what is deferred
> to dev5 are in [`docs/DEV4.md`](docs/DEV4.md). dev3 history: [`docs/DEV3.md`](docs/DEV3.md). If the owner says
> «أكمل dev4», check §3 of DEV4.md for any unfinished row.

## 1. Goal and status

REKKR v1.17 (Revae, CC BY-NC 4.0) on Android via **Unity 2022.3.62f3** (`96770f904ca7`), with the
game **exactly like the original**: the unmodified `REKKR.WAD` runs on a C# Doom engine
(Managed Doom v2.1a, GPL-2.0). Unity only provides video, audio, input and the touch UI.

v0.1.0 checkpoint:
- APK `REKKR-0.1.0.apk`: package `com.ayoub.rekkr`, versionCode 1, minSdk 24, targetSdk 36,
  IL2CPP, arm64-v8a + armeabi-v7a, GLES3, about 66.7 MB.
- Headless engine test (`tools/HeadlessTest`, dotnet):
  - Detected as GameMode=Retail, Version=Ultimate.
  - DEMO1–4 play to the end.
  - All 36 maps load and run for 350 tics.
- Firebase Test Lab Game Loop runs on Galaxy S20 FE 5G (`r8q`, Android 13, Adreno 650):
  - Result: Passed, no crashes and no `E Unity` lines in logcat.
  - Performance: `avg_fps=59.3 p99_frame_ms=16.8` at 2400×1080.
- v0.2.0 (dev2), branch `feat/dev2`:
- APK `REKKR-0.2.0.apk`: versionCode 2, permissions INTERNET + VIBRATE, about 99.6 MB. The extra
  size is the GeneralUser GS 2.0.3 soundfont (32 MB, redistributable, gain 1.33 vs TimGM6mb).
- Features:
  - Hor+ widescreen (frame = round(480·aspect) clamped 640–1200, even; 2400×1080 → 1066×400) with a 4:3 toggle.
  - Compact fullscreen HUD (screen size 9).
  - 120 Hz support (`DisplayRate`).
  - Haptics.
  - Gyro aim with an invert option.
  - Quick save (slot 8), quick load, autosave (slot 9) and a Continue button on the title.
  - Button layout editor (drag and scale).
  - EN/AR UI (`Loc.cs` + `ArabicShaper`, `RekkrArabic.ttf`).
  - Settings tabs.
- Test Lab r8q/33, Game Loop scenarios 1 and 2: result Passed, 0 `E Unity`, 0 FATAL.
  - Scenario 1: `avg_fps=116.8 p99=8.5 ms` at target 120. Autosave, quicksave and quickload OK.
    Haptic pulses=58.
  - Scenario 2: `avg_fps=118.0`. Continue OK. E1M1, E2M1, E3M1 and E4M1 all load, with the Arabic UI.
- **Not verified on hardware:** gyro direction (Test Lab devices are static) and audio.

- v0.3.0 (dev3), branch `feat/dev3`: bug fixes (E1M7 save overflow, see-through wall columns), GRAPHICS tab with
  Classic/Balanced/Enhanced presets, 400–1000 lines + dynres, 4 render threads, true-colour smooth lighting, bloom/grade/
  sharpen/vignette/CRT, side-fill, per-frame look. APK 99,819,729 B, versionCode 3, GLES3. Test Lab r8q/33 scenario 1:
  Passed, 0 `E Unity`, avg 109.4 fps, p99 16.7 ms (Enhanced; dynres settled at 400 lines). Details: `docs/DEV3.md`.
- v0.4.0 (dev4), branch `feat/dev4`: free look (y-shear renderer, pitch aim, swipe/gyro), JUMP button, fixed joystick
  (dead zone + curve, floating option), `+` crosshair, carved-stone skin + WAD STCFN pixel font, direct menu taps, Enhanced
  = 600 lines. APK 99,836,563 B, versionCode 4. Test Lab r8q/33 scenarios 1+2: Passed, 0 `E Unity`, S1 avg 109.2 fps
  p99 17.0 ms, S2 (Arabic) E1–E4 load, avg 95.2 fps. Details: `docs/DEV4.md`.
- v0.5.0 (dev5), branch `feat/dev5`: G-buffer (depth/material in the frame alpha, `GBuffer.cs`) → `WorldFx` + `RekkrWorld.shader`
  (sky/clouds/rays, water reflections, hot liquids, fog, rain/snow/embers/dust, lightning, ≤ 8 dynamic lights, ≤ 512 particles,
  AO, DoF), Voxile filmic grade, Masterpiece preset (3; Custom = 4), weapon wheel, automap touch. APK 100,040,073 B, versionCode 5.
  Test Lab r8q/33 S1+S2+S5 Passed, 0 `E Unity`; Masterpiece E1–E4 100.2–108.9 fps. Weak device not tested (quota). Details: `docs/DEV5.md`.

- v0.6.0 (dev6), branch `feat/dev6`: optional GPU 3D renderer "Remaster" (level mesh, billboards / extruded sprites,
  KVX loader, sun shadow map, dev5 effects on the GPU G-buffer), setting **Dark areas** (Original / Lifted / Bright),
  GPU-class auto preset (`DeviceClass`), fixes for the weapon/HUD outline, water ripples on the weapon, light blow-out,
  DoF halo and sun glare on walls. **New keystore** (cert SHA-256 `768de491…bab8`): uninstall ≤ 0.5.0 once.
  Remaster is offered only on strong GPUs (`RekkrSettings.RemasterAllowed`; Mali-G57 ran it at 19–28 fps).
  Test Lab r8q Remaster 84–103 fps; a15 (weak) 22–38 fps in software, same as v0.5.0 (≥ 55 gate open).
  Details and honest stage status: `docs/DEV6.md` §6/§9.

- v0.7.0 (dev7), merged into `main`: see the note at the top and `docs/DEV7.md` §3/§4. Same keystore as 0.6.0 (installs
  over it). Gamepad and the document picker are **not tested on hardware** (no pad / no person on Test Lab).
  r8q: classic ≈ 99–103 fps; Remaster all-maps 73.7 fps at thermal 3, 54.8 fps at thermal 4 (E1M7/E4M9/E2M7 the
  heaviest, 43–50 fps when hot). Point-light shadows follow `GpuRenderer.ShadowTriBudget` (240k / level tris).

- v0.8.0 (dev8), merged into `main`: animation layer (see the top note and `docs/DEV8.md` §5–§7). APK 101,797,141 B,
  versionCode 8, same keystore. Linux scenario 12: weapon "still" frames while walking 54.5 % → 0 % (software), 43.5 % → 0 %
  (Remaster). Test Lab virtual MediumPhone.arm/33 (S12, S1, S11): Passed, 0 `E Unity`, 36/36 maps errors=0.
  **Physical r8q run not done yet** (Spark quota used up on release day).

**Not verified:** a full campaign playthrough by a human; audible audio QA. The sandbox has no sound
  card, and Test Lab videos have no audio.

**Local builds only.** No Unity Cloud Build, no GitHub Actions.

## 2. Layout

| Path | What |
|---|---|
| `Assets/Rekkr/Engine/` | Managed Doom core (Doom, Audio/Video/UserInput interfaces, Config). Patched: `Wad.cs` (rekkr → Retail/Ultimate), `ConfigUtilities` (`DataDirectory`, REKKR.WAD in IWAD list), `Texture*.cs` (skip missing patches), `SaveSlots` (.NET Std 2.1), `SaveMenu` (auto save names) |
| `Assets/Rekkr/Synth/MeltySynth/` | MIDI synth (MIT) |
| `Assets/Rekkr/Scripts/RekkrApp.cs` | Boot (`RuntimeInitializeOnLoadMethod`, no scene objects), StreamingAssets→persistentDataPath copy, 35 Hz tic loop + interpolation, IMGUI controls, touch settings panel, Game Loop autopilot |
| `Assets/Rekkr/Scripts/TouchInput.cs` | Stick/swipe/buttons, menu pad, gamepad+keyboard, autopilot hooks |
| `Assets/Rekkr/Scripts/UnityVideo.cs` + `Resources/Rekkr/RekkrScreen.shader` | Renderer → Texture2D (transposed), sharp-bilinear |
| `Assets/Rekkr/Scripts/UnitySound.cs`, `UnityMusic.cs`, `UnityAudioShim.cs` | SFX (stereo pan) and MUS/MIDI → streamed AudioClip |
| `Assets/Rekkr/Editor/` | `RekkrBuild` (Configure/BuildAndroid/BuildLinux), `RekkrAndroidManifest` (TEST_LOOP intent), texture import rules |
| `Assets/StreamingAssets/` | `rekkr.wad` (original), `rekkr-compat.wad` (2 Freedoom patches), `TimGM6mb.sf2` |
| `tools/` | `HeadlessTest/`, `make_compat_wad.py`, `make_ui_textures.py`, `make_icon.py`, `sandbox/` |
| `ThirdParty/` | Original `rekkr.zip` + licences |

## 3. Build

```sh
REKKR_KEYSTORE=/path/rekkr.keystore REKKR_KEYSTORE_PASS=... \
  tools/sandbox/build_android.sh          # current version: Builds/REKKR-0.8.0.apk
```
- Signing keystore: alias `rekkr`, certificate SHA-256 `768de491…bab8` since v0.6.0 (≤ 0.5.0: `58c71163…248a9d0`, lost). Keep the same keystore
  for every release, otherwise updates will not install over the old app.
- The keystore and password are **never** committed. `RekkrBuild` reads them from the environment.
- Bump `RekkrApp.Version`, `RekkrBuild` versionName and `REKKR_VERSION_CODE` for each release.
- Verify every build with:
  - `apksigner verify --print-certs`
  - `aapt dump badging` (package, sdk, native-code)
  - `sha256sum`
- Headless engine test:
  `cd tools/HeadlessTest && dotnet run -c Release -- ../../Assets/StreamingAssets/rekkr.wad <outdir>`

## 4. Device testing (Firebase Test Lab)

- Run `tools/sandbox/ftl_gameloop.sh Builds/REKKR-x.apk [model] [version]`.
- Default device is `r8q`/33 (Galaxy S20 FE 5G: SD865, Adreno 650, 1080×2400), the closest Test Lab
  device to the owner's POCO F3 (SD870, Adreno 650).
- The Spark plan allows 5 physical runs per day.
- The Game Loop intent starts the autopilot. It goes title → menu → touch settings → New Game E1 →
  about 95 s of play (move, fire, use, weapon swap, automap) → in-game menu.
- At the end it logs `[REKKR-TEST] frames=… avg_fps=… p99_frame_ms=…`, writes `testloop_result.txt`
  and finishes the activity.
- Setting `REKKR_SHOTS=<dir>` on desktop enables the same autopilot and writes screenshots.
- Review `video.mp4` frames (title, settings, menu pad, in-game HUD) and grep logcat for
  `E Unity|FATAL|[REKKR`.

## 5. gVisor sandbox setup (no root, 17 CPUs)

The same setup is used for my-librequake (its §9).
- **Shader compiler:** Unity's `UnityShaderCompiler` crashes under gVisor (`PESetupFS`, arch_prctl).
  - Rename it to `UnityShaderCompiler.real`.
  - Install `tools/sandbox/UnityShaderCompiler.wrapper.sh` in its place. The wrapper runs it under
    `qemu-x86_64-static`.
  - Without it the editor dies with "failed to read magic number" (exit 134).
- **schedfix:** gVisor rejects realtime thread priorities, and FMOD aborts because of that.
  - Build `tools/sandbox/schedfix.c` with `gcc -shared -fPIC -o libschedfix.so`.
  - `run_unity.sh` LD_PRELOADs it.
- **AndroidPlayer:** the Linux editor tarball does not include AndroidPlayer.
  - Extract it from the macOS `UnitySetup-Android-Support` .pkg with `tools/sandbox/xar_extract.py`
    (xar + cpio/gzip payload).
  - Place it under `Editor/Data/PlaybackEngines/AndroidPlayer`.
  - Symlink its `SDK`, `NDK` and `OpenJDK` to JDK 11, the SDK (cmdline-tools, platform 36,
    build-tools 34) and NDK r23b.
- **GTK:** the editor needs GTK3 libs. Extract the debs to a lib dir and add it to LD_LIBRARY_PATH
  (see `run_unity.sh`).
- **Activation:** Personal licences activate headless with `-batchmode -quit -username … -password …`.
  Pass the credentials through the environment and never write them to disk or logs.
- **Desktop player under Xvfb (fixed in dev2).** The old hang after frame 0 was caused by
  `runInBackground=false`: Xvfb has no window focus, so the player paused itself.
  - `RekkrBuild.BuildLinux` now sets `runInBackground=true`.
  - Run: `HOME=<tmp> LP_NUM_THREADS=16 REKKR_SCENARIO=<1|2> REKKR_SHOTS=<dir>
    LD_LIBRARY_PATH=<unity libs> LD_PRELOAD=libschedfix.so xvfb-run -a -s "-screen 0 2400x1080x24"
    Builds/linux/rekkr.x86_64 -force-glcore -screen-width 2400 -screen-height 1080 -screen-fullscreen 0`.
  - llvmpipe runs at about 58 fps. That is useful for UI QA, not for performance numbers.

- **dev6 toolchain traps (fresh sandbox):**
  - Android cmdline-tools must be **7.0** (`commandlinetools-linux-8512546`); v12 needs Java 17 (class 61)
    and Unity's Gradle step runs on JDK 11.
  - NDK / build-tools / platform-tools symlinks can be extracted as small text files ("clang: not found").
    Re-create every file whose content is just a relative path as a real symlink.
  - Unity 2022 desktop player: `-logFile` is relative to the player's cwd — pass absolute paths.
- **dev6 test scenarios:** `6` (Remaster tour E1M1–E4M1 + software vs Remaster fps; on a weak GPU: software tour + presets auto/1/0; `REKKR_GPU_CLASS=weak|strong` forces the class), `7` (parity: warp + freeze,
  software vs Remaster shot of the same view; `REKKR_PARITY="e,m,x,y,angle,pitch;…"`, `REKKR_SHOT_ALPHA=1` also
  dumps the G-buffer alpha). Frame textures must be read with exact texel loads, never bilinear
  (bilinear mixes G-buffer codes → outlines).

## 6. Traps and decisions

- REKKR's TEXTURE2 lists 153 textures with missing patches. Only `SW1CMT` (all maps) and
  `FIREBLU1`/`KS_W10` (E1M7) are used in maps. The engine skips missing patches, and
  `rekkr-compat.wad` supplies `WALL54_1` and `W65B_1`.
- MAPINFO in the WAD is ZDoom-only and is ignored, as in vanilla. E4 uses the vanilla E4 music list,
  which is REKKR's own songs.
- `ManagedDoom.EventType`/`DoomKey` clash with `UnityEngine.EventType`. Qualify them.
- dev7: shader passes are addressed by their **order in the file** — add new passes at the end (RemasterWorld 6 = point
  shadow caster). World effects need the G-buffer: pass `ThreeDRenderer.TrueColor ? LevelGame : null` to `WorldFx`.
- dev7: weather is world particles (sky sectors only); the old screen-space `WeatherLayer` is gone — do not bring it back.
- dev7: a Doom sight check from the render side (`VisibilityCheck.CheckSight`) only bumps `validcount`; it does not
  touch the RNG or the demo/golden results (HeadlessTest PASS).
- dev8: animation state is render-only (`AnimHooks`, `Mobj.AnimFlash/AnimLastHealth`, `PlayerSpriteDef.Old*`,
  `Specials.*Next`); never let game code read it and never save it. Classic style must stay pixel-identical.
- dev8: REKKR's bow has **no muzzle-flash state** — detect firing from ammo spent / entering AttackState. A kill clears
  MF_SHOOTABLE on the same tic as the health drop — track health once the thing was shootable.
- dev8: `RekkrWorld.shader` pass 6 = alpha-blended solid particles (added at the end, see the pass-order rule above).
- dev8: interpolate the weapon only when the sprite is the same and the step ≤ 20 px, so state offsets are never smeared.
- Do not use Freedoom as the IWAD. All content except the two patches stays 100 % REKKR.
- The original public REKKR licence is CC BY-NC. Commercial submission relies
  on the seller's separate permission; do not remove upstream notices, invent
  sublicensing scope, or describe GPL-covered source as proprietary.

## 7. Ideas for next versions

dev2 did the soundfont, widescreen, button editor, haptics and quick save.
- Remaining ideas:
  - An in-game localized Doom menu. The WAD graphics stay English, so it would need a custom overlay.
  - (dev7 did controller remapping and exported saves.)
  - A human playtest on a POCO F3, including gyro direction (yaw and the new pitch) and audio.
  - Fewer gen0 GCs on IL2CPP. Run the weak-device test and add a GPU-class rule for auto-Masterpiece (DEV5 open items).
  - dev6: the GPU 3D renderer and voxel things (`docs/DEV6.md`).
- dev8 scenario `12`: weapon smoothness Classic vs Modern (software + Remaster), recoil, barrel explosion, E1M2 fight
  (spawned Troop + Possessed), liquid frames; grep `anim8` in the log.
- Test scenarios: `REKKR_SCENARIO=1` (play + saves + haptics), `2` (Continue + E1–E4 + Arabic), `5` (Masterpiece tour,
  weather, wheel, automap, Enhanced vs Masterpiece). dev5 runs used `SCENARIOS=1,2,5 FTL_TIMEOUT=15m`.
  `ftl_gameloop.sh` runs both by default (`SCENARIOS=1,2`, `FTL_TIMEOUT=12m`).
