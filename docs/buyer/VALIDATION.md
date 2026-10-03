# Validation scope

## Fresh engine regression run — 2026-10-03

Executed against the v0.8.0 engine source using the repository's .NET 10
`tools/HeadlessTest` program in `all` mode:

- All four built-in demos ran to completion.
- All 36 maps loaded and advanced 350 simulation tics.
- Continue-from-title save loading passed.
- All 36 map soak/save-load checks passed.
- 176 golden rendered frames matched the stored baseline.
- Program returned `RESULT PASS` with exit code 0.

Some idle test players die in hostile maps; the load test is not a survival or
campaign-completion test. Soak and byte-preserving save round trips are also
not evidence of a completed campaign.

This run uses the standalone C# engine. It does **not** test the Unity host,
Android plugin, audio playback, touchscreen, gamepad, Remaster GPU rendering,
thermal behaviour or APK installation.

## Reproduce

Install .NET SDK 10 separately from Unity, then:

```sh
cd tools/HeadlessTest
dotnet run -c Release -- ../../Assets/StreamingAssets/rekkr.wad /tmp/rekkr-test all
```

Replace `/tmp/rekkr-test` with a writable output folder on your OS. Do not
regenerate `golden.txt` merely to make a failure disappear.

## Existing v0.8.0 Android evidence

The upstream release documentation records virtual Firebase Test Lab checks
for scenarios 1, 11 and 12. The physical-device v0.8.0 run remains recorded as
pending. Those are historical results, not rerun by this preparation change.

## Remaining validation before your own app release

- Clean Unity import and APK build from the extracted buyer ZIP.
- Install/launch on at least one actual supported phone.
- New Game, all episode starts, menu navigation, pause/resume and rotation.
- Save, load, Continue; document-picker backup export/import.
- Touch editing, left-handed input, gyro direction and gamepad/remapping.
- Listen to music and sound effects on a device.
- Software vs Remaster rendering, visual effects and animation toggles.
- Sustained performance on a lower-end and higher-end device; record device,
  settings and temperature state with measurements.
- Inspect the English buyer instructions by following them on a clean machine.
- Verify any advertised AAB/Play Store support against a real validated bundle.

Do not advertise "fully tested", "120 FPS guaranteed", "all phones supported",
"complete human playthrough" or "Play Store ready" based on the engine tests.

The marketplace package changes documentation and packaging, not gameplay.
It has not been re-imported or rebuilt with Unity in the packaging environment.
The demo APK is the existing v0.8.0 release, not a new build from this archive.
