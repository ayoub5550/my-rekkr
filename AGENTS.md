# AGENTS.md — my-rekkr developer and agent handoff

Last updated: 2026-09-29 (v0.1.0).

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
- **Not verified:** a full campaign playthrough by a human; audible audio QA. The sandbox has no sound
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
REKKR_KEYSTORE=/path/rekkr.keystore REKKR_KEYSTORE_PASS=... REKKR_VERSION_CODE=1 \
  tools/sandbox/build_android.sh          # ~3–4 min incremental, Builds/REKKR-0.1.0.apk
```
- Signing keystore: alias `rekkr`, certificate SHA-256 `58c71163…248a9d0`. Keep the same keystore
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
- **Desktop player under Xvfb hangs after frame 0.** The windowed GL present never returns under
  gVisor.
  - `-batchmode` runs the game and autopilot fine, but screenshots come out blank.
  - Use Test Lab videos for visual QA. The editor under Xvfb (`-force-glcore`) does render.

## 6. Traps and decisions

- REKKR's TEXTURE2 lists 153 textures with missing patches. Only `SW1CMT` (all maps) and
  `FIREBLU1`/`KS_W10` (E1M7) are used in maps. The engine skips missing patches, and
  `rekkr-compat.wad` supplies `WALL54_1` and `W65B_1`.
- MAPINFO in the WAD is ZDoom-only and is ignored, as in vanilla. E4 uses the vanilla E4 music list,
  which is REKKR's own songs.
- `ManagedDoom.EventType`/`DoomKey` clash with `UnityEngine.EventType`. Qualify them.
- Do not use Freedoom as the IWAD. All content except the two patches stays 100 % REKKR.
- No commercial packaging (CC BY-NC).

## 7. Ideas for next versions

- A better soundfont (e.g. GeneralUser GS, check its licence).
- Widescreen status-bar fill.
- Per-button repositioning.
- Haptics on fire/damage.
- A quick-save button.
