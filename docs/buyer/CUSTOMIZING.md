# Customization map

This is a source-level project. Back up your work and make one change at a time.
Customization does not alter the licences of included third-party materials.

## Product identity

`Assets/Rekkr/Editor/RekkrBuild.cs` sets `PackageId`, company name, product name
and icon on every configure/build. Changing only Unity's Player Settings is
therefore not sufficient. The version is `RekkrApp.Version` in
`Assets/Rekkr/Scripts/RekkrApp.cs`.

The launcher icon lives at `Assets/Rekkr/Icon/icon.png`. The existing icon is
derived from REKKR game artwork; use appropriately licensed replacement art
when rebranding.

### Android bridge caution

The document picker uses the Java class `com.ayoub.rekkr.RekkrDocs`:

- `Assets/Plugins/Android/RekkrDocs.java`
- `Assets/Rekkr/Editor/RekkrAndroidManifest.cs`
- `Assets/Rekkr/Scripts/RekkrApp.Backup.cs`

An application's package ID can differ from this helper class's Java package.
Do **not** blindly replace every package string. If renaming the helper itself,
update its declaration, manifest entry and C# class references together, then
test export and import on a real device.

## Controls, language and presentation

| Area | Entry points under `Assets/Rekkr/Scripts` |
| --- | --- |
| Touch and movement input | `TouchInput.cs` |
| Gamepad input | `Gamepad.cs` |
| Setting defaults and graphics presets | `RekkrSettings.cs` |
| English / Arabic strings | `Loc.cs` |
| Settings panels and HUD overlays | `RekkrApp.UI.cs`, `RekkrSkin.cs` |
| Animation effects | `AnimFx.cs` |
| Screen and world effects | `PostFx.cs`, `WorldFx.cs` |
| GPU rendering | `Remaster/GpuRenderer.cs` |
| GPU capability gating | `DeviceClass.cs` |

Saved PlayerPrefs may override changed defaults. Test with a fresh app data
directory or reset settings. Do not weaken GPU gating merely to advertise
Remaster on every phone.

## Content and game logic

`RekkrApp.dataFiles` lists the files copied from StreamingAssets at startup.
Maps, sprites, sounds and much of the presentation come from WAD data; this
project does not import ordinary Unity scenes as Doom levels. Replacing content
requires compatible WAD layout, game definitions and resources, plus testing.

The gameplay runs at 35 Hz. Visual interpolation supports higher display
refresh rates without changing simulation timing. Keep animation state
render-only; rerun the golden-frame and save/load checks after engine edits.

## Monetization and publishing

No advertising, payment or analytics SDK integration is provided by this
preparation work. Adding any SDK requires a separate compatibility, privacy,
permissions and distribution-rights review. This documentation does not
promise store approval or a particular revenue level.
