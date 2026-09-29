# DEV4 — REKKR for Android v0.4.0 (plan, spec and status)

> **ملخص للمالك (nall):** هذه ورقة dev4. طلباتك بعد اللعب الطويل: النظر للأعلى والأسفل، زر قفز، ألوان
> وستايل كلاسيكي للأزرار والإعدادات بدل البني الميت، عصا تحكم ثابتة، علامة تصويب +، وأي شيء نسيته.
> إذا توقف Viktor لأي سبب، قل لأي مطور أو وكيل ذكاء اصطناعي: **«أكمل dev4»**. يقرأ هذا الملف، ويبحث في
> جدول الحالة (§3) عن أول مرحلة غير مكتملة، ويكملها بنفس القواعد.

Owner request 2026-09-29 18:06 UTC (after playing v0.3.0 long enough): vertical look, jump button,
classic-style buttons + settings (not "dead brown"), a more fixed joystick, a `+` crosshair, and
"anything I forgot".

---

## 0. If you are the next developer: how to resume ("أكمل dev4")

1. Read `AGENTS.md` fully (build §3, Test Lab §4, sandbox §5, traps §6), then `docs/DEV3.md` §2
   (architecture table) and this file.
2. Work on branch **`feat/dev4`** (branched from `feat/dev3` @ `ccf0d42`). Never commit to `main`;
   do not merge dev branches into `main` without asking the owner.
3. Take the first stage in §3 that is not ✅; its spec is in §4. Each stage ends with its checks (§5)
   → one commit `dev4: stage N — <title>` that also updates its status row → push `feat/dev4`.
   Half-done work is committed as 🚧 with a "Next:" note, never silently.
4. Secrets: same as DEV3 §0.5 (Unity login, Firebase SA JSON for `ayoub-261d7`, keystore outside the
   repo — if it is lost, stop and ask).
5. Report to the owner in **Arabic**, honestly (device-measured vs local-only), and send a Test Lab
   video with every build.

## 1. Rules

- **Original stays original where it matters.** `REKKR.WAD` is never modified. Demos (DEMO1–4) and
  the golden hashes (`tools/HeadlessTest/golden.txt`) must still PASS: every new gameplay input
  (pitch, jump) is carried in **new TicCmd fields that demos never set** (0 = vanilla behaviour).
- New gameplay options that change vanilla rules (jump, free aim) are **options**, clearly labelled.
  Jump is on by default because the owner asked for it; "Classic rules" can turn it off.
- Performance: no regression vs v0.3.0 on r8q (avg ≥ 105 fps at 120 Hz, Enhanced).
- Local builds only; CC BY-NC (no commercial packaging); APK growth ≤ +3 MB.

## 2. What changes where

| Piece | File(s) |
|---|---|
| TicCmd new fields `LookPitch` (short, 1/256 slope units… see stage 1) and `Jump` | `Engine/Doom/Game/TicCmd.cs` |
| Player pitch + jump physics, aim fallback | `Engine/Doom/Game/Player.cs`, `World/PlayerBehavior.cs`, `World/WeaponBehavior.cs` (`BulletSlope`), `World/ThingAllocation.cs` (`SpawnPlayerMissile`) |
| Y-shearing renderer | `Engine/Video/ThreeDRenderer.cs` (`centerY`, `planeYSlope`, sky, psprites), `ThreeDRendererPool.cs`, `Renderer.cs` (`LocalViewPitch`) |
| Touch: vertical swipe, jump, fixed stick | `Scripts/TouchInput.cs` |
| Crosshair, new UI skin, settings rows | `Scripts/RekkrApp.UI.cs`, new `Scripts/RekkrSkin.cs` (WAD font + stone frames), `Scripts/RekkrSettings.cs`, `Scripts/Loc.cs` |
| Button/stick textures | `tools/make_ui_textures.py` → `Resources/Rekkr/UI/*.png` |
| Autopilot (Test Lab) | `Scripts/RekkrApp.Test.cs` (look up/down, jump, crosshair shots) |

## 3. Status table (update in every stage commit)

| # | Stage | Status | Notes / measured |
|---|---|---|---|
| 0 | Plan, branch, version 0.4.0 / versionCode 4 | ✅ 2026-09-29 | `RekkrApp.Version` 0.4.0, ProjectSettings bundleVersion 0.4.0 / code 4 |
| 1 | Vertical look (free look): y-shear renderer, touch + gyro pitch, aim follows the crosshair | ✅ 2026-09-29 | `ThreeDRenderer.ViewPitch` (static, per frame) → `ApplyShear` (centerY + planeYSlope), psprites keep `baseCenterYFrac`, sky clamped + stretched ×1.8 when free look is on (`FreeLookSky`; Crispy "stretch sky"). Range ±80 (slope 0.5 ≈ 26.6°). `TicCmd.LookPitch/Ext` → `Player.LookPitch/NoAutoAim`; `BulletSlope`/`SpawnPlayerMissile` use the pitch when autoaim finds nothing, always when autoaim is off. HeadlessTest `dev4`: 4 maps × 5 pitches × 8 angles at 400 and 800 lines → worst never-written pixels 1 / 3 (vanilla specks); puff z pitch 0/+60/−40 = 33/64/13 units. Golden 176 frames identical, DEMO1–4 + all maps PASS. Settings (Controls ▸ page 2): Look up/down, Invert, Auto-aim. Gyro pitch axis = device ±Y by landscape side (**not verified on hardware**) |
| 2 | Jump (button + physics, option) | ✅ 2026-09-29 | `MomZ = 8` from the ground, 18-tic cooldown, no jump with noclip. HeadlessTest: apex 36.0 units, lands after 16 tics, held 3 s → 6 jumps. JUMP button above USE (layout editor), Space / gamepad A (Use moved to F). Short haptic tick on jump. Option "Jump button" |
| 3 | Fixed joystick (default) + floating option, dead zone, response curve | ✅ 2026-09-29 | Fixed: base never moves, grabbed within 2.1 R of home (rest of the left side = look). Floating: base where the finger lands, never dragged. Dead zone 12 %, full at 92 %, curve ^1.35, 80 ms knob return. Option "Joystick: Fixed / Floating" |
| 4 | Crosshair `+` (styles, colours, size) | ✅ 2026-09-29 | `+` bone (default) / red / green, dot, off; centre of the 3D view window (`Renderer.ViewWindow`), 1 px dark outline, size ∝ screen height. Hidden in menus, automap, title, death |
| 5 | Classic REKKR UI skin: stone buttons, carved-stone settings panel, WAD pixel font | ✅ 2026-09-29 | `tools/make_ui_textures.py` (procedural stone, numpy) → btn/btn_pressed (red ember), stick base/knob, 9-slice `panel`/`plate`/`plate_on`; icons without baked labels (Arabic icon variants deleted). `RekkrSkin.cs` reads STCFN033–095 from the WAD at runtime → 4 tinted point-filtered atlases (bone/red/dim/dark); Arabic/non-ASCII → Noto Arabic TTF with outline. Settings panel widened to 80 % width; Controls tab has 2 pages. EN + AR screenshots checked on the Linux player |
| 6 | Extras the owner did not name (see spec) | ✅ 2026-09-29 (partial) | Done: tap Doom menu lines directly (`Select(i)` on Selectable/Load/Save menus, sliders only select), Enhanced = 600 lines + one-time migration of 0.3.0 Enhanced/800, double-tap look area centres the view, jump haptic. **Deferred to dev5:** automap touch pan/zoom, weapon wheel |
| 7 | Build v0.4.0, local QA, Test Lab r8q, video, release (byte-verified), report | ⬜ | |

Legend: ⬜ not started · 🚧 in progress (see Next:) · ✅ done · ⏭ deferred (reason).

## 4. Stage specs

### Stage 0 — Plan, branch, version
- `RekkrApp.Version = "0.4.0"`, build with `REKKR_VERSION_CODE=4`, out `Builds/REKKR-0.4.0.apk`.

### Stage 1 — Vertical look
- **Renderer (y-shearing, the Heretic/Crispy Doom method):** a per-frame pitch shifts the horizon:
  `centerY = windowHeight/2 + shear`, `shear = pitch · (windowHeight_at_scale/200)`; walls, sprites and
  masked textures already project with `centerYFrac`. Must also change:
  - `planeYSlope[i]` depends on `i − centerY` → recompute per frame when `centerY` changes
    (≤ 1000 divisions per strip worker).
  - Sky: the column is 128 texels and wraps with `& 127` → when looking up the top would repeat the
    sky bottom. Sky columns clamp the texel row to `[0, h−1]` (top row repeats; REKKR skies have a
    flat top colour) and the sky texture-mid is moved up by the shear so the horizon stays put.
  - Player weapon sprites (psprites) use the **unsheared** centre (weapon does not slide).
  - `ResetWindow` stores `baseCenterY`; all threads get the same pitch (pool passes it).
- Range: pitch ∈ [−90, +90] in 200-line units (≈ ±29° at the vanilla 90° FOV). Distortion is the known
  y-shear trade-off; it is modest at this range.
- **Input:** right-side swipe: vertical component → pitch (same sensitivity as horizontal, optional
  invert). Gyro: pitch rate around the phone's horizontal axis. The attack finger also aims up/down.
  Option "Look up/down" (default on), "Invert look Y" (off), "Auto-centre view" (off: the view stays
  where you leave it; double-tap the look area re-centres).
- **Aim:** the sim receives the pitch in `TicCmd.LookPitch` → `Player.LookPitch`. `BulletSlope` and
  `SpawnPlayerMissile`: with autoaim on (vanilla default) keep the three vanilla aim traces and use
  the pitch slope only when none hits (Crispy behaviour); with option "Autoaim" off always use the
  pitch slope. Slope = pitch/160 (projection 160 in 320-wide units). Demos: pitch 0 → vanilla.
- The renderer uses the **per-frame** pitch from input (like smooth look), the sim uses the tic pitch.
- Checks: HeadlessTest golden + DEMO1–4 PASS; HeadlessTest renders E1M1 at pitch ±90 with no
  HOM/crash at 400/800 lines; Linux screenshots looking up (sky) and down (floor).

### Stage 2 — Jump
- `TicCmd.Jump` (bool). `PlayerBehavior.MovePlayer`: if `cmd.Jump && onGround && jumpTics == 0` →
  `MomZ = +8.0` (apex ≈ 36 units, enough to hop a 24-unit ledge + a bit, not enough to skip big
  heights; Crispy's "low jump" is 7, "high" 9) and `jumpTics = 18` (no bunny-hop spam). Air control
  stays vanilla (none). Landing uses the vanilla squat (`deltaViewHeight`).
- Sound: none (vanilla has no jump sound; the landing "oof" plays only on hard landings, vanilla).
- Button **JUMP** in the right cluster (editable in the layout editor); keyboard Space / gamepad A.
  (Space is Use today → Use moves to E/gamepad X; document.)
- Option "Jump" (default on) in Controls; off hides the button and ignores `Jump`.
- Check: HeadlessTest: jump on E1M1 start → peak ≈ 36 units, lands, no stuck; DEMO1–4 PASS.

### Stage 3 — Fixed joystick
- Today the stick is "floating": its base jumps to the finger and is dragged along past the rim —
  the owner finds this uncomfortable. New default **Fixed**: base stays at its home position; a touch
  anywhere in the left move zone controls it, knob = clamp(finger − home, radius); the base never
  moves. Option "Joystick: Fixed / Floating" (floating = old behaviour but without dragging the base).
- Dead zone 12 % → radial, plus a mild response curve (`s^1.35` above the dead zone) for fine walk
  control; full speed at 92 % deflection.
- Visual: base + direction notches stay, knob returns with a 60 ms ease.

### Stage 4 — Crosshair
- Styles: `+` (default), dot, circle, off; colours: bone (REKKR font cream), red, green, cyan; size
  S/M/L. Drawn at the centre of the 3D view window (above the status bar when it is shown), in
  physical pixels, with a 1 px dark outline for contrast. Hidden in menus, automap, intermission,
  title, dead view. Optional: turns red for 0.15 s when a shot hits (off; needs hit feedback → dev5).

### Stage 5 — Classic REKKR UI skin
- Style source = the WAD's own art (REKKR 1.17): carved blue-grey stone (M_DOOM frame, TITLEPIC),
  blood-red titles, mossy green, bone-cream pixel font STCFN with dark outline, stone arrow cursor.
- **Touch buttons:** stone discs — bevelled blue-grey stone rim (light top-left / dark bottom-right),
  dark translucent centre, bone-cream icon with dark outline; pressed = red ember glow + rim lights up.
  Labels (ATTACK, USE, …) in the pixel font. Stick = stone ring with 4 carved notches; knob = polished
  stone. Generated by `tools/make_ui_textures.py` (own art in the REKKR palette, no WAD art copied).
- **Settings panel:** stone frame (9-slice, generated) on a darkened background; title in REKKR red
  with the pixel font; tabs = stone plates (selected tab lit red-orange); rows in bone-cream WAD font;
  toggles = stone switch with a lit rune (ON red-gold, OFF dark); steppers = stone ◀ ▶ arrows.
  The pixel font is read **at runtime from the WAD** (`STCFN033–095`), so no art is copied into the
  repo. Latin text uses it (upper-case, as in vanilla); Arabic keeps the Noto Arabic font drawn in
  the same colours with a dark outline.
- Same skin for: layout editor toolbar, CONTINUE pill, FPS label, "tap to play", toasts.

### Stage 6 — Extras ("anything I forgot")
Chosen because they remove the most friction on a phone, without touching the original game data:
1. **Tap the Doom menu directly:** tapping a menu line selects+activates it (New Game, skill, episode,
   Load/Save slots, Options); the D-pad stays for sliders. Uses the menu item coordinates.
2. **Automap on touch:** drag to pan (follow off), pinch/± buttons to zoom, FOLLOW button.
3. **Enhanced resolution 600 lines** (v0.3.0 device run: r8q cannot hold 800 with smooth lighting at
   120 Hz, dynres dropped to 400 → p99 16.7 ms). Existing Enhanced users are migrated once.
4. **Weapon wheel:** long-press the weapon button → radial pick of owned weapons (optional if time).
5. Toast confirmations in the new skin ("QUICK SAVE", "JUMP OFF", …).
Anything not finished here is moved to "Deferred to dev5" with a reason.

### Stage 7 — Build, test, release
- HeadlessTest (golden, demos, all maps, save/load) PASS; Linux player screenshots of every new
  screen in EN + AR (§5 of AGENTS).
- APK `REKKR-0.4.0.apk` (GLES3, versionCode 4, same keystore) → Test Lab r8q/33 scenario 1 (+2):
  Passed, 0 ` E Unity`; video frames reviewed (look up/down, jump, crosshair, new buttons, settings).
- GitHub release **v0.4.0** from `feat/dev4`; download the asset back and `cmp` + `sha256sum -c`.
- Report in Arabic with the video.

## 5. Checks (before every stage commit)
- `cd tools/HeadlessTest && dotnet run -c Release -- ../../Assets/StreamingAssets/rekkr.wad <out>`
  → all PASS (golden, DEMO1–4, 36 maps, save/load).
- Unity compile (Linux build) OK; screenshots for UI stages.
- `git diff ProjectSettings` clean (keystore path, runInBackground, graphics APIs).

## 6. Bug log
| Date | Bug | Fix |
|---|---|---|

## 7. Log of decisions
- 2026-09-29: jump on by default (owner asked); pitch/jump in new TicCmd fields so demos stay vanilla.
