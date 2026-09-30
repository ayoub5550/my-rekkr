# DEV6 — REKKR "Remaster": a GPU 3D renderer with voxel/3D things (plan and full spec)

> **ملخص للمالك (nall):** هذه ورقة dev6، مكتوبة مسبقاً حتى يستطيع أي مطور أو وكيل ذكاء اصطناعي تنفيذها
> وحده. الهدف: الوصول إلى مستوى ألعاب مثل Voxile — عالم ثلاثي الأبعاد حقيقي على كرت الشاشة، بظلال
> حقيقية، وإضاءة شمس وسماء، وانعكاسات، وأعداء وأسلحة ثلاثية الأبعاد (Voxels) بدل الصور المسطحة.
> اللعبة نفسها (المحرك، القواعد، الخرائط، الحفظ، الديمو) لا تتغير أبداً؛ هذا "وضع عرض" جديد اسمه
> **Remaster** بجانب Classic/Enhanced/Masterpiece. قل للمطور: **«نفذ dev6»** أو **«أكمل dev6»**.

Written 2026-09-29 by Viktor at the owner's request ("write a dev6 paper so the developer after you
executes it and understands everything"). dev5 (`docs/DEV5.md`) must be finished first: dev6 re-uses
its sky, water, weather, particles, lights and grade code.

---

## 0. How to start / resume ("نفذ dev6" / «أكمل dev6»)

1. Read, in this order: `AGENTS.md` (build, Test Lab, sandbox, traps), `docs/DEV3.md` §2
   (architecture), `docs/DEV4.md` §1, `docs/DEV5.md` §1–§2, then this whole file.
2. Branch **`feat/dev6`** from the latest dev5 commit (`feat/dev5`, or `main` if the owner merged it).
   Never commit to `main`; never merge without the owner's explicit OK.
3. Version **0.6.0**, versionCode **6**. Work stage by stage (§6). Each stage = spec + checks → one
   commit `dev6: stage N — <title>` that updates the status table (§6) → push.
4. Report to the owner in **Arabic**, with a Test Lab video per build and honest numbers.
5. If a stage is impossible as written, write why in §9 (decisions), pick the closest alternative,
   and continue — never silently drop a stage.

## 1. Goal, non-goals, hard rules

**Goal:** an optional renderer that draws the *same* game state as real 3D on the GPU:
- level geometry as meshes (walls, floors, ceilings, moving doors/lifts/crushers);
- per-pixel lighting that stays faithful to Doom's sector light + distance fade, **plus** a modern
  layer: sun/sky light for outdoor areas with a real shadow map, point lights from fireballs/torches
  with shadows for the nearest few, AO, screen-space reflections on liquids;
- things (monsters, items, decorations, projectiles, the player's weapon) as **3D**: auto-extruded
  sprites (works for every sprite with zero art work) and, where available, **voxel models** (KVX);
- dev5's sky, weather, water, particles, fog, grade on top.

**Non-goals:** changing gameplay, maps, sounds or the WAD; multiplayer; ray tracing (Unity 2022 built-in
has no mobile RT; only Adreno 740+ phones have ray query — note for a later version, §8).

**Hard rules (same as dev3–dev5):**
- `REKKR.WAD` never modified; no WAD art committed (everything is built from the WAD at runtime).
- The simulation is Managed Doom, untouched: DEMO1–4 + golden hashes PASS. The renderer only
  *reads* `World` state; it never calls the game RNG.
- Classic/Enhanced/Masterpiece keep working exactly as before (the software renderer stays).
- Performance: Remaster ≥ 60 fps avg on r8q (SD865/Adreno 650) at native 2400×1080 with render
  scale ≤ 1.0 and dynamic resolution; ≥ 45 fps on the weak Test Lab device at its auto preset, or
  Remaster is hidden on that GPU class.
- CC BY-NC 4.0 (REKKR) — non-commercial. Third-party code only with GPL-2.0-compatible licences
  (MIT/BSD/zlib OK). Voxel packs: only with a licence allowing redistribution with a CC BY-NC game.

## 2. What you need to know about the existing code (5-minute tour)

| What | Where | Notes |
|---|---|---|
| Boot, main loop, 35 Hz tics + frame interpolation | `Assets/Rekkr/Scripts/RekkrApp.cs` | `Update()`: runs tics, then `video.Render(Doom, frac)`, then PostFx; IMGUI draws UI in `OnGUI` (`RekkrApp.UI.cs`) |
| Software renderer | `Assets/Rekkr/Engine/Video/ThreeDRenderer.cs` (+`ThreeDRendererPool` threads) | Doom BSP column renderer → byte frame → RGBA texture (`UnityVideo.cs`); dev5 adds the G-buffer alpha |
| Post chain | `Scripts/PostFx.cs`, `Resources/Rekkr/RekkrPost.shader`; dev5 `Scripts/WorldFx.cs`, `RekkrWorld.shader` | runs on the frame texture; draws in `OnGUI` via `Graphics.DrawTexture` |
| Game state | `Engine/Doom/World/*` (`World`, `Map`, `Sector`, `LineDef`, `SideDef`, `Vertex`, `Mobj`, `Thinkers`) | `Mobj.GetInterpolatedX/Y/Z(frac)`, `Player.GetInterpolatedViewZ(frac)`, `GetInterpolatedAngle(frac)`; sector heights change via thinkers (doors/plats/floors/ceilings) |
| Textures, flats, sprites, palette, colormap | `Engine/Doom/Graphics/*` (`TextureLookup`, `FlatLookup`, `SpriteLookup`, `Palette`, `ColorMap`, `Patch`, `Column`) | `Texture.Composite` gives the composed patch; animated textures/flats via `world.Specials`/`AnimationInfo` (check `TextureAnimation`) |
| Camera for a live game | `Player` (console player), dev3 smooth look (`input.PendingTurn`), dev4 pitch (`input.PitchInt`, 200-line units, slope = pitch/160) | the GPU camera must use the same values as the software view |
| Unity camera | created in `RekkrApp.Awake` (orthographic, `cullingMask = 0`, clear black) | dev6 adds a second, perspective camera rendering into a RenderTexture |
| Settings/presets | `Scripts/RekkrSettings.cs`, GRAPHICS tab in `RekkrApp.UI.cs`, strings `Scripts/Loc.cs` (EN/AR) | add `Renderer = Software/Remaster` |
| Tests | `tools/HeadlessTest` (dotnet, no Unity), Linux player screenshots, Test Lab Game Loop autopilot `RekkrApp.Test.cs` | see AGENTS.md §4–§5 |

Build: `REKKR_VERSION_CODE=6 REKKR_OUT=Builds/REKKR-0.6.0.apk /work/unity/build_rekkr.sh` (AGENTS §3).
The sandbox has no GPU; the Linux player runs on llvmpipe (slow but correct for screenshots). Real
performance numbers only come from Test Lab.

## 3. Doom geometry primer (what the mesh builder must reproduce)

- Map units: 1 unit ≈ 1 pixel of a 320×200 texture; Doom pixels are 1.2× taller than wide on screen
  (the software renderer handles this in its projection) → in the GPU camera use **vertical FOV that
  matches the software view** (`projection = nonWideWidth/2` px at 90° horizontal for 4:3, Hor+ for
  wide screens) and scale world Z by **1.2** (or equivalently stretch the projection) so heights look
  identical to Classic. Verify by overlaying both renderers (stage 1 check).
- Coordinates: Doom (x east, y north, z up) → Unity (x, z, y): `unity = (x, z*1.2?, y)` — pick one
  convention, document it in code, and keep it everywhere. Fixed-point: `Fixed.ToFloat()`.
- **Walls** per `LineDef` side (`SideDef`): one-sided → middle texture from floor to ceiling;
  two-sided → upper (front ceiling above back ceiling), lower (back floor above front floor), and
  optional masked middle (transparent, clipped to the opening, no tiling vertically for masked
  mid-textures). Pegging: `ML_DONTPEGTOP` (upper texture top-aligned vs bottom-aligned), `ML_DONTPEGBOTTOM`
  (lower/mid aligned to ceiling vs floor), texture X/Y offsets from `SideDef`. **Sky hack:** if both
  front and back ceilings are sky, do not draw the upper wall.
- **Flats:** each `Sector` floor/ceiling polygon. Sectors can be non-convex, have holes, and may be
  "broken" (unclosed) in PWADs. Build polygons by tracing line loops per sector and triangulate with
  **LibTessDotNet** (MIT, single-file C# port; handles holes and self-intersections). Fallback for
  broken sectors: use the BSP subsectors (`Map.Subsectors` + segs) with a convex-clip of the node
  planes (the "GL nodes" approach) — implement only if tracing fails for any REKKR map (check all 36).
  Flat UVs = world x/y / 64 (flats are 64×64, aligned to the world grid).
- **Sky:** sectors with ceiling flat `F_SKY1` are open sky: do not draw the ceiling; draw the sky
  (dev5 animated sky shader) as a big cylinder/dome behind everything; walls touching sky ceilings
  extend upward to the sky (Doom draws sky above them).
- **Moving sectors:** doors/lifts/crushers change `Sector.FloorHeight/CeilingHeight` (with
  interpolation fields — check `Sector` for `OldFloorHeight`/`GetInterpolatedFloorHeight`). Build
  mesh *per sector* (flats) and *per linedef* (walls), keep an index sector→affected vertices, and
  update only those vertices when a height changes (each frame, compare heights).
- **Lighting:** `Sector.LightLevel` (0–255) changes (flicker/glow thinkers). Faithful mode = the
  Doom formula in the shader: light by sector level + distance (`scalelight`/`zlight` tables → use
  the continuous version already in dev3 `ThreeDRenderer` "smooth lighting"), plus the "fake
  contrast" on axis-aligned walls (N/S walls darker, E/W lighter by 16 levels).
- **Things:** `Mobj` with sprite/frame/rotation (8 rotations by angle between viewer and thing),
  `MobjFlags.Shadow` = spectre fuzz, fullbright frames (`frame & 0x8000`), sprite offsets
  (left/top offsets of the patch) — the billboard must use them (top offset from the thing's z).
  Things are clipped by floors in Doom only via "foot clip" in some ports — vanilla draws them over
  the floor; keep vanilla.
- Status bar, HUD, menus, automap, intermission, finale: **keep them from the software renderer**
  (they are 2D); composite the GPU 3D view only into the 3D window rectangle
  (`UnityVideo.ViewWindow`), everything else is the existing frame.

## 4. Target architecture

```
Doom sim (unchanged) ──► WorldSnapshot (per frame, interpolated: camera, sectors, mobjs, lights)
                                 │
            ┌────────────────────┴───────────────────┐
            ▼                                        ▼
  Software renderer (existing)            GpuRenderer (new, Remaster)
  → frame RGBA (2D UI + 3D view)          LevelMesh  (walls/flats, per-sector chunks)
                                          ThingRenderer (billboard | extruded | voxel)
                                          WeaponRenderer (view model)
                                          Lighting (Doom light, sun+shadow map, point lights)
                                          SkyDome + dev5 weather/particles
                                          → RenderTexture 3D view (+ depth, normals)
            │                                        │
            └──────────► Compositor: 3D window from GPU, rest from software frame
                              → dev5 WorldFx (fog/AO/SSR/rays) → PostFx → screen
```

New files (suggested):
- `Assets/Rekkr/Remaster/GpuRenderer.cs` — owner; creates the camera + RTs; `Render(World, frac)`.
- `Remaster/LevelMeshBuilder.cs`, `Remaster/SectorPolygons.cs` (+ `ThirdParty/LibTessDotNet/`),
  `Remaster/LevelMesh.cs` (dynamic updates), `Remaster/TextureAtlas.cs` (Texture2DArray per size
  class, palette → RGBA, animation remap table), `Remaster/ThingRenderer.cs`,
  `Remaster/SpriteExtruder.cs`, `Remaster/KvxLoader.cs`, `Remaster/VoxelMesher.cs`,
  `Remaster/WeaponRenderer.cs`, `Remaster/Lights.cs`, `Remaster/ShadowMaps.cs`.
- Shaders `Resources/Rekkr/Remaster/{World.shader, Thing.shader, Sky.shader, Shadow.shader}`.
- Pipeline: **built-in render pipeline** (what the project uses; do not switch to URP — it would
  change the whole project, IMGUI flow and build size). Use `Camera.targetTexture`, manual
  `camera.Render()` from `Update`, forward shading with a custom light loop (≤ 8 point lights in
  uniform arrays, like dev5), one directional shadow map (sun) and ≤ 2 point-light cube shadows.
- Graphics API: GLES3 (current). Vulkan optional A/B (see DEV3 open item).

## 5. Things in 3D: three levels

1. **Billboard (exact):** camera-facing quad (cylindrical, rotate around the vertical axis only),
   8-rotation frame selection identical to `ThreeDRenderer.ProjectSprite`, patch offsets, alpha-test.
   This alone already gives real depth sorting, lighting and shadows (shadow = alpha-tested quad
   facing the light).
2. **Extruded sprites (automatic, default for Remaster):** for each sprite frame+rotation build a
   mesh at runtime: every opaque pixel becomes a voxel column; thickness from a depth heuristic
   (distance to the silhouette edge → rounded profile, ×0.5 for flat items); use rotation 1 (front)
   and 5 (back) where available for the two faces, rotations 3/7 for the side silhouette (carve).
   Greedy-mesh the voxels (merge coplanar faces) → typically 1–4k triangles per frame. Cache per
   (sprite, frame, rotation-set) in a dictionary with LRU (budget 64 MB). Build lazily on a worker
   thread; show the billboard until the mesh is ready. The result looks like Voxile's voxel
   characters, automatically, for every monster and item in REKKR.
3. **Voxel models (KVX):** loader for Build/KVX (the format GZDoom uses for Doom voxel packs:
   header, x/y/z sizes, pivot, column offsets, slabs of colour indices, palette at the end). A
   mapping file `voxels.txt` (sprite frame → model + scale + angle offset) in an optional folder
   next to the WAD (`persistentDataPath/voxels/`), so artists/community packs can be dropped in
   without rebuilding. No REKKR voxel pack exists today — this path exists for the future.
- Weapon (psprite): extruded mesh of the current weapon frame, rendered as a view model with its
  own FOV, lit by the player's sector light + muzzle flash light; bob from `Player.Bob` like vanilla.
- Spectre fuzz (`Shadow` flag): refraction shader (distort the scene behind) instead of fuzz noise.

## 6. Stages and status (update in every commit)

| # | Stage | Status | Acceptance (must be true before ✅) |
|---|---|---|---|
| 0 | Branch (from `feat/dev5`), version 0.6.0/6, settings `Renderer: Software/Remaster` (hidden until stage 4). First do the DEV5 "Open items after v0.5.0": the weak-device Test Lab run and the GPU-class rule for auto-Masterpiece | ✅ 2026-09-30 (dev5 weak-device run ⏭: owner tested v0.5.0 on his own phone and said it is not needed; Test Lab quota was also exhausted) | builds; nothing changes visually |
| 1 | Camera parity: GPU camera that matches the software view (position, angle, pitch, FOV, 1.2 aspect) | ✅ 2026-09-30 | Linux scenario 7: 13 views (E1M1 4 angles + pitch ±40, E2M1, E3M1, E4M1, E1M7) software vs Remaster: 0.2–1.1 % pixels differ (> 40/255), all 1-px edges; projection = LastView (sheared centre, projection px), no Z scale (1.2 applied on display like software) |
| 2 | Static level mesh: walls (pegging, offsets, masked mids, sky hack), flats (tessellated), textures (atlas/array), faithful Doom lighting shader | ✅ 2026-09-30 | HeadlessTest `dev6`: 36/36 maps build, 0 NaN; sectors traced (LibTess) 23 900+, BSP fallback 63, empty 3 (unreachable dummies); 8k–174k tris/map (E1M7 max), build 3–330 ms desktop. One RG8 atlas 4096×7424 (all 886 textures, 219 flats, all sprite patches). Lighting = vanilla scalelight/zlight in continuous form + fake contrast + extralight + fixed colormaps + damage/bonus palettes |
| 3 | Dynamic sectors (doors, lifts, crushers, light changes), animated textures/flats, scrolling walls, switches | ⬜ | E1M1 door + lift video; DEMO1 played back with the Remaster view looks right end-to-end |
| 4 | Things as billboards + weapon billboard; composite into the 3D window; HUD/menus from software; Remaster selectable | ⬜ | DEMO1–4 watchable in Remaster; Linux screenshots; first Test Lab run (fps) |
| 5 | Modern lighting: sun (per episode, from DEV5) + shadow map for outdoor sectors, point lights (dev5 detection) with ≤ 2 shadowed, AO (GTAO-lite), SSR water, fog | ⬜ | Test Lab r8q ≥ 60 fps at render scale 1.0 or dynres; screenshots |
| 6 | Extruded sprites (automatic 3D for every monster/item/weapon), cache, worker-thread build | ⬜ | all REKKR sprites extrude without errors (HeadlessTest-like tool); memory ≤ 64 MB cache |
| 7 | KVX voxel loader + `voxels.txt` mapping (optional packs) | ⬜ | loads a public-domain test KVX; mapping hot-reload |
| 8 | Polish: spectre refraction, particles/weather from dev5 in 3D, per-device auto preset, settings UI, Arabic strings | ⬜ | full playthrough smoke (autopilot scenario 4: all episodes' first maps in Remaster) |
| 9 | Build 0.6.0, Test Lab (r8q + weak device), video, release (byte-verified), report | ⬜ | release asset downloaded back: `sha256sum -c` + `cmp` |

Legend: ⬜ not started · 🚧 in progress (Next: …) · ✅ done · ⏭ deferred (reason).

## 7. Checks and tools

- HeadlessTest (dotnet) must still PASS (golden/demos/maps/dev4/dev5 modes) — the sim is untouched.
- New HeadlessTest mode `dev6`: build the level mesh for all 36 maps in plain .NET (mesh builder
  must not depend on UnityEngine types — use `System.Numerics`), report sectors without closed
  loops, triangle counts, degenerate triangles, T-junction count.
- Parity screenshots: Linux player env `REKKR_RENDERER=remaster REKKR_SHOTS=...` + the software
  shot at the same tic → side by side PNGs; review them visually, every map's start.
- Test Lab: `tools/sandbox/ftl_gameloop.sh` (AGENTS §4), scenarios 1–3 + new 4 (Remaster tour).
- Performance counters in the `[REKKR-TEST]` summary: `gpu_ms` (Unity `FrameTimingManager`),
  triangle count, draw calls, cache MB.

## 8. Risks and answers

| Risk | Answer |
|---|---|
| Broken/unclosed sectors break tessellation | BSP-subsector fallback (convex clip); log and screenshot the sector |
| Doom visual tricks (deep water, invisible stairs, self-referencing sectors) look wrong in true 3D | REKKR is vanilla-format; test all 36 maps; for a specific trick keep the billboard/software look or add a per-map exception list |
| Look differs from Classic (the owner values "exactly like the original") | Remaster is an extra mode; Classic stays default for purists; faithful lighting by default |
| Mobile GPU cost (shadows, AO, SSR at 2400×1080) | render scale 0.7–1.0 + dynres (dev3 logic), shadow map 1024², ≤ 2 cube shadows, half-res AO/SSR |
| Extruded sprites look odd for some frames (e.g., thin items) | per-sprite thickness overrides in a small table; fall back to billboard |
| Ray tracing wish | not on Unity 2022 built-in/Android; revisit when targeting Adreno 740+ with Vulkan ray query (would need a native plugin) |

## 9. Log of decisions
- 2026-09-29: built-in pipeline (no URP), GLES3 first; LibTessDotNet for flats; extruded sprites as
  the automatic "voxel look"; KVX for real voxel packs; software renderer kept for 2D and Classic.
