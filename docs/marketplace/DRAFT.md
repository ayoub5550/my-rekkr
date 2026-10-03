# Codester submission materials

Product: **my-rekkr - Unity Android FPS Source Project**.
Version: v0.8.0 source; no gameplay change.

The listing fields are in [listing.json](listing.json). The price entered in
the Codester form is **$55, excluding Codester's buyer fee**.
Category: App Templates > Unity > Templates > Action (ID 282).

## Package

```sh
python3 tools/make_review_package.py --edition marketplace \
  --output /absolute/outside/project/my-rekkr-source-0.8.0.zip
python3 -m unittest discover -s tools/tests -v
```

The packager includes English buyer guides and retained upstream notices.
It rejects common sensitive paths, non-empty signing settings, symlinks and
unresolved LFS pointers. The manifest records every file's SHA-256.
The `marketplace-submission` status is an archive label, not Codester approval.

## Visuals

- Icon: existing game launcher artwork resized to 200 × 200.
- Preview: 1600 × 800, using the existing game's gold/dark palette and Lato.
- Screenshot ZIP: 5 PNGs made from the fresh HeadlessTest output.
- All captures are labelled as standalone classic-engine output, without the
  Android touch overlay or GPU Remaster. No generated/fabricated gameplay.

## Submission disclosures

- Seller confirms separate permission for the Android commercial offering;
  the separate permission instrument is not reproduced in this archive.
- All original notices remain. See `docs/buyer/DISTRIBUTION.md`.
- 16 packaging/build-wrapper tests passed. The engine suite passed separately.
- Unity import/rebuild, physical-device, audio and controller QA were not
  performed for this packaging change; see `docs/buyer/VALIDATION.md`.
- Demo URL points directly to the existing v0.8.0 release APK.
- Development hours are left blank rather than invented.
- Free-file and flash-sale promotions are not opted into.

The live Codester outcome must be checked separately. These files do not prove
that a submission was accepted or published.
