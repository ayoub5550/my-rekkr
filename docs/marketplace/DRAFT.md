# Codester preparation — not submitted

Working product: **my-rekkr**. Final public name and artwork are not selected.
No live listing or price is set by this change.

## Technical listing draft

**Working title:** my-rekkr — Unity Android FPS Source Project

**Short description:**
Unity Android FPS source project with touch controls, saves, graphics presets and classic or modern animation.

**Description draft:**

A Unity 2022.3 LTS Android source project combining a C# Doom-engine host with
mobile controls, widescreen rendering, English/Arabic settings and configurable
graphics. Includes autosave, quick save/load, backup tools, gyro, haptics and
source-level customization.

Choose classic presentation or modern animation, with weapon interpolation,
recoil, camera effects and world effects. An optional GPU Remaster is available
on supported GPU classes. Frame targets include 60, 90 and 120 Hz; actual
performance depends on device and settings.

The review package includes source, Unity settings, build tools, required data,
English setup/customization guides and headless engine tests. Unity and Android
tools must be installed separately. It is an engine/data-driven project, not a
prefab-only FPS kit. Physical-device QA and final distribution terms must be
completed before this text is published.

**Candidate tags:** unity, android, fps, source code, retro, touch controls

## Submission material checklist

Checked directly against https://www.codester.com/info/upload on 2026-10-03:

- [ ] Final public product name and description.
- [ ] Clean main ZIP including English buyer documentation.
- [ ] 800 × 400 preview image.
- [ ] 200 × 200 icon, not just a cropped screenshot.
- [ ] Screenshot ZIP: 3–9 actual product images in PNG/JPG.
- [ ] Demo URL without alternative purchase links.
- [ ] Optional YouTube preview URL.
- [ ] Correct category, file types, requirements and lowercase tags.
- [ ] Accurate development hours (do not invent them).
- [ ] Seller-selected Regular/Extended prices and support commitments.

## Distribution preflight

Preserve `LICENSE`, `THIRD_PARTY_NOTICES.md` and bundled upstream notices.
This preparation branch does not replace third-party terms or assert a blanket
commercial sublicense. The final terms must accurately describe any separate
permissions, their scope for downstream buyers, and GPL-covered components.

Codester's licence page was opened directly on 2026-10-03:
https://www.codester.com/info/licenses/
It describes restrictions on source redistribution. Compatibility with any
GPL-covered distribution must be resolved before submission; do not copy a
proprietary "no redistribution" notice over GPL-covered source.

## Packaging

From the repository root:

```sh
python3 tools/make_review_package.py --output /absolute/outside/project/my-rekkr-review.zip
python3 -m unittest discover -s tools/tests -v
```

This intentionally creates a **review** archive, not a marketplace-approved
release. It contains no sign-in credentials or signing keys. The package
manifest records SHA-256 for each file. Common secret paths and obvious private
key/token patterns are checked, but this is not a complete security audit.
