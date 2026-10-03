# my-rekkr — Unity Android project

**Source project based on v0.8.0.**

A Unity 2022.3 LTS Android project hosting the Managed Doom C# engine and
REKKR v1.17 content. This package includes editable source, game data, project
settings, build tools, and English technical documentation.

## Start here

1. Install **Unity 2022.3.62f3** with Android Build Support.
2. Open the project folder containing `Assets`, `Packages`, and `ProjectSettings`.
3. Follow [BUILDING.md](BUILDING.md).
4. Read [CUSTOMIZING.md](CUSTOMIZING.md) before renaming the application.
5. Check [VALIDATION.md](VALIDATION.md) for tested scope and limitations.

## Included features

- Four episodes / 36 maps from the bundled REKKR data.
- Touch movement and look, adjustable layout, left-handed mode, gyro and haptics.
- English and Arabic settings; the original WAD menu artwork remains English.
- Autosave, quick save/load, Continue, and save backup import/export.
- Widescreen and 4:3 modes; selectable refresh targets of 60, 90 and 120 Hz.
- Software rendering plus optional GPU Remaster on supported GPU classes.
- Graphics presets, dynamic resolution, and configurable visual effects.
- Classic / Modern / Custom animation styles, including interpolated weapons,
  recoil, camera effects, pickup motion, and liquid cross-fades.
- Keyboard and gamepad input; physical-controller validation is still required.

Frame-rate settings are targets, **not performance guarantees**. Remaster is
restricted by the project's GPU classification. This is not a drag-and-drop
prefab-based FPS kit: the simulation and content formats follow the Doom engine.

## Package contents

| Location | Purpose |
| --- | --- |
| `Assets/Rekkr/Engine` | C# simulation and renderers |
| `Assets/Rekkr/Scripts` | Unity host, input, settings, UI and effects |
| `Assets/Rekkr/Editor` | Project configuration and build entry points |
| `Assets/StreamingAssets` | WAD data and SoundFonts |
| `Assets/Plugins/Android` | Android integration |
| `Packages`, `ProjectSettings` | Unity project configuration |
| `tools` | Build, content-generation and headless test tools |
| `ThirdParty`, `LICENSE`, `THIRD_PARTY_NOTICES.md` | Upstream materials and notices |
| `PACKAGE_MANIFEST.json` | File sizes, SHA-256 hashes and archive edition |

No signing keys, Unity installation, marketplace credentials, generated Unity
cache, APK or AAB are included. Restore Unity packages and install the Android
toolchain separately. Python and .NET are optional for packaging/engine tests.

## Licensing and distribution

Read [DISTRIBUTION.md](DISTRIBUTION.md), `LICENSE` and
`THIRD_PARTY_NOTICES.md`. The seller offers the Android project relying on
separate commercial permission; the original public upstream notices are
retained. This is a mixed-licence source project, not a blanket proprietary
relicensing of all components. Keep copyright notices and comply with the
applicable GPL/source obligations. Do not assume the artwork can be resold
standalone or used in unrelated projects.
