# Third-party notices

my-rekkr is a **non-commercial** fan port. It combines the following works.

## REKKR (game data) — CC BY-NC 4.0

* `Assets/StreamingAssets/rekkr.wad` is **REKKR v1.17** (`REKKR.WAD`), byte-for-byte unchanged, from
  the official idgames archive: <https://www.doomworld.com/idgames/levels/doom/megawads/rekkr>
  (`rekkr.zip`, md5 `feb42b084ef1134e780d73894e736981`, kept unmodified in `ThirdParty/REKKR/`).
* Author: **Revae**. Music: **Tom Jensen**. Sounds: **TerminusEst13**. Mapping: AngrySaint, Bzzrak,
  AD_79, lupinx-Kassman, Velcrosasquatch, SuperCupcakeTactics, Jaws In Space, Jimmy, TerminusEst13.
  1.17 update uploaded by lupinx-Kassman. Full credits: `ThirdParty/REKKR/rekkr.txt`.
* Licence: Creative Commons Attribution-NonCommercial 4.0 International —
  <http://creativecommons.org/licenses/by-nc/4.0/>.
* **Changes made:** none to the WAD itself. The port adds `rekkr-compat.wad` (two wall patches,
  see Freedoom below) so that the three REKKR textures whose patches are missing from the WAD
  (`SW1CMT`, `FIREBLU1`, `KS_W10`) render without errors, and the game is played through a
  different engine (Managed Doom) with touch controls.
* This port is not endorsed by the REKKR authors. **It may not be sold or used commercially.**

## Managed Doom — GPL-2.0

Engine code in `Assets/Rekkr/Engine/` is from Managed Doom v2.1a by Nobuaki Tanaka (sinshu),
<https://github.com/sinshu/managed-doom>, GPL-2.0 (`ThirdParty/licenses/LICENSE_ManagedDoom.txt`).
Modified for this port: REKKR game-mode detection, data directory override, tolerance for missing
texture patches, .NET Standard 2.1 compatibility, automatic save names. The whole port is
therefore distributed under GPL-2.0 (`LICENSE`).

## MeltySynth — MIT

`Assets/Rekkr/Synth/MeltySynth/` — SoundFont MIDI synthesizer by Nobuaki Tanaka,
<https://github.com/sinshu/meltysynth> (`ThirdParty/licenses/LICENSE_MeltySynth.txt`).

## TimGM6mb.sf2 — GPL-2.0

`Assets/StreamingAssets/TimGM6mb.sf2`, General MIDI soundfont by Tim Brechbill, as shipped with
Managed Doom (`ThirdParty/licenses/LICENSE_TimGM6mb.txt`).

## Freedoom — BSD-3-Clause

`Assets/StreamingAssets/rekkr-compat.wad` contains only the patches `WALL54_1` and `W65B_1`
taken from Freedoom 0.13.0 `freedoom1.wad`, <https://freedoom.github.io/>
(`ThirdParty/licenses/LICENSE_Freedoom.txt`). Built by `tools/make_compat_wad.py`.

## Fonts and icons

* Lato (Łukasz Dziedzic) — SIL Open Font License 1.1. Used for touch-UI labels.
* Material Design Icons (Pictogrammers / Google) — SIL OFL 1.1 font, icons Apache-2.0/CC BY.
  Rasterised into the button textures by `tools/make_ui_textures.py`.
* Launcher icon: built from REKKR's own status-bar face sprite (`tools/make_icon.py`), CC BY-NC 4.0.

## Unity

Built with Unity 2022.3 (Personal). The Unity runtime is © Unity Technologies, used under the
Unity Terms of Service.
