# Building the Android project

## Requirements

Use the pinned **Unity 2022.3.62f3** Editor and Android Build Support:
SDK platform 36, NDK r23b and JDK 11, as used by this project's recorded builds.
Install and activate Unity with your own eligible licence. A buyer should not
need the original developer's Unity account or signing key.

Current source configuration:

| Setting | Value |
| --- | --- |
| Package ID | `com.ayoub.rekkr` (change for your own application) |
| Version | `0.8.0` |
| Android version code | `8` by default in the C# build entry point |
| Minimum Android | API 24 / Android 7.0 |
| Target SDK | 36 |
| Backend / ABIs | IL2CPP / ARM64 and ARMv7 |
| Graphics | OpenGL ES 3 by default |
| Orientation | Landscape |

Target SDK alone does not establish Play Store readiness. Validate the actual
bundle, native-library requirements, signing, privacy disclosures and current
store policies before publishing.

## Open and run

1. Extract the project to a writable folder.
2. Add that folder in Unity Hub and open with the pinned Editor.
3. Allow import to finish, then run **REKKR → Configure Project**.
4. Open `Assets/Scenes/Main.unity`, then press Play.

The scene is intentionally minimal: `RekkrApp.Boot()` creates the game host at
runtime. The game loads WAD data rather than one Unity scene per map. Keep the
`.meta` files with their assets.

## Build an APK

`RekkrBuild.BuildAndroid` is the existing command-line entry point; it is not a
dedicated Build menu item. With the Editor closed, run the following from the
project folder, replacing `Unity` with your Editor executable:

```sh
Unity -batchmode -quit -buildTarget Android -projectPath . \
  -executeMethod RekkrBuild.BuildAndroid -logFile build-android.log
```

On Windows use the same arguments with `Unity.exe` (one line).
The default C# output is `Builds/REKKR-0.8.0.apk`. If custom signing variables are
absent, Unity's debug signing is used. A debug APK is for testing, not a signed
production release.

### Own-key signing and output overrides

The build reads these environment variables:

| Variable | Meaning |
| --- | --- |
| `REKKR_KEYSTORE` | Existing key file outside the project |
| `REKKR_KEYSTORE_PASS` | Key store and alias password (same value in current code) |
| `REKKR_KEY_ALIAS` | Alias, default `rekkr` |
| `REKKR_VERSION_CODE` | Positive Android version code |
| `REKKR_OUT` | Output APK path |
| `REKKR_GFX_API` | `vulkan` opts into Vulkan-first with GLES3 fallback |

Inject passwords through your private build environment; do not put them into
source, screenshots, shell history, documentation or ZIPs. Keep your signing key
for future updates.

The developer-specific `tools/sandbox/build_android.sh` derives its output name
and default code from `RekkrApp.Version` (currently `0.8.0` / `8`). It accepts
explicit `REKKR_VERSION_CODE` and `REKKR_OUT` overrides and requires custom
signing variables. Configure `UNITY` for your machine if using this wrapper.
The sandbox wrappers are not required on normal PCs.

## Verify the output

Use Android SDK tools against the actual APK:

```sh
apksigner verify --print-certs Builds/REKKR-0.8.0.apk
aapt dump badging Builds/REKKR-0.8.0.apk
```

Confirm package ID, version name/code, ABIs and signer. Install on a physical
Android device and exercise the checklist in `VALIDATION.md`.

## AAB / Play Store limitation

The current build code sets `EditorUserBuildSettings.buildAppBundle = false`.
Do not pass a `.aab` filename and assume it becomes a valid bundle. A dedicated
AAB build path and bundle validation are still needed for a store-ready release.

## Troubleshooting

- No Android target: install Android Build Support for this exact Editor.
- Missing data: verify the four required WAD/SoundFont files under
  `Assets/StreamingAssets`; do not replace them with Git LFS pointer text.
- Device cannot install over an earlier APK: compare package ID, version code
  and certificate. Uninstalling removes local app data; export saves first.
- Slow graphics: choose a lower preset and frame target; test both cold and
  sustained-load performance. Emulator FPS is not a phone benchmark.
- Missing/failed shaders in a restricted Linux sandbox: use the existing
  developer wrappers only if that environment needs them, not on ordinary PCs.
