# DEV8 — REKKR for Android v0.8.0 "Animation: modern, still classic" (plan, spec and status)

> **ملخص للمالك:** هذه ورقة dev8. طلبت (2026-09-30): تحسين الأنيميشن وإضافة أنيميشن جديد وعصرنته مع
> الإبقاء على كلاسيكية اللعبة — كل البنود المقترحة **ما عدا أنيميشن أزرار اللمس** («الأزرار كما هي جيدة»).
> كل شيء بصري فقط: منطق اللعب والعروض والحفظ لم تتغير. تبويب جديد **ANIMATION**: Classic (الأصلي 100٪)
> / Modern (الافتراضي) / Custom. إذا توقف Viktor قل لأي مطور أو وكيل: **«أكمل dev8»**.

Owner request 2026-09-30 14:58 UTC: "dev8: improve the animation, add new animation and modernise it while
keeping the game classic". 15:03 UTC: "the buttons are fine as they are; the rest is excellent, do it".

---

## 0. How to resume ("أكمل dev8")

1. Read `AGENTS.md`, `docs/DEV7.md` §1 (hard rules), then this file.
2. Branch **`feat/dev8`** from `feat/dev7` @ `7db91a4` (= `main` after the v0.7.0 release). Work happens in
   the `.worktrees/dev7` checkout (its Unity `Library/` cache is reused). Never commit to `main` directly;
   merging dev8 into `main` needs the owner's OK.
3. Version **0.8.0**, versionCode **8**, same keystore (cert `768de491…bab8`) → installs over 0.7.0.

## 1. Rules

- Everything is **visual only**. The simulation never reads the animation state: `AnimHooks` (render side),
  `Mobj.AnimFlash/AnimLastHealth`, `PlayerSpriteDef.Old*`, `Specials.*Next` are not saved and not read by
  game code. HeadlessTest golden (176 frames) + DEMO1–4 must PASS.
- Animation style **Classic = every hook off = the original presentation** (pixel-identical frames).
- The touch buttons get no animation (owner decision 2026-09-30).
- Settings pages ≤ 6 rows, EN + AR strings.

## 2. Findings before coding

| Area | Finding |
|---|---|
| Weapon | `psp.Sx/Sy` (bob, raise, lower) change only per 35 Hz tic and were drawn as-is: at 90–120 Hz the weapon repeats the same spot on 2–3 frames then jumps (desktop probe: 54 % of walking frames did not move at all). |
| Camera | View height, bob and landing squat were already interpolated (`GetInterpolatedViewZ`). |
| Liquids | Animated flats switch picture every 8 tics (4.4 Hz) with a hard cut. |
| Remaster | The GPU camera is built from `ThreeDRenderer.LastView`, so view offsets applied in the software renderer reach Remaster too. |

## 3. Design

- `Engine/Video/AnimHooks.cs` — static values the renderers read (weapon offset / interpolation / eased
  switch, view yaw / pitch / height / roll, pickup float + glow, hit flash, liquid cross-fade, HUD pops).
- `Scripts/AnimFx.cs` — per tic: fire (ammo spent / attack state / flash state), hurt (DamageCount rise +
  attacker direction), landing, monsters hit (health drop), missiles exploded (lost MF_MISSILE), barrels
  killed; per frame: springs → weapon sway (turn/look lag), breathing, strafe tilt, landing dip, per-weapon
  recoil, camera shake (smooth noise), hit kick, damage marks, HUD pops.
- Weapon: `PlayerSpriteDef.OldSx/OldSy/OldState` saved in `Player.UpdateFrameInterpolationInfo`; drawn
  position = lerp when the sprite is the same and the step ≤ 20 px (state offsets never smeared); raise /
  lower mapped through smoothstep (same duration and end points).
- Liquids: `Specials.FlatTranslationNext/TextureTranslationNext` + `AnimBlend(frac)`. Software (true colour):
  planes write the next texel into `DrawScreen.AnimTex` (stamp-tagged) and the writer blends both colours
  with the same light. Remaster: second lookup texture `_Lookup2` + `_AnimBlend` (flats and walls).
- World FX: `WorldFx` pass 6 (alpha-blended "solid" particles): bullet chips + dust, blood drops that rest on
  the floor + mist, explosion smoke / embers / debris. They need the Particles setting.
- UI: settings panel open (fade + settle), weapon wheel pop-in (ease-out-back), fullscreen-HUD number pops,
  damage-direction crescents (IMGUI). The classic status bar and the melt wipe are unchanged.

## 4. Stages and status

| # | Stage | Status | Acceptance |
|---|---|---|---|
| 0 | Plan (this file), version 0.8.0/8, settings + ANIMATION tab (3 pages) | ✅ 2026-09-30 | builds, ≤ 6 rows/page |
| 1 | Smooth motion: interpolated weapon, eased switch, liquid cross-fade (software + Remaster) | ✅ 2026-09-30 Linux scenario 12: weapon "still" frames while walking 54.5 % → 0 % (software), 43.5 % → 0 % (Remaster) | still frames ≈ 0 % in Modern |
| 2 | Weapon motion: turn/look lag, breathing, strafe tilt (roll in Remaster 3D weapon), landing dip, per-weapon recoil | ✅ 2026-09-30 recoil detected for the bow (no flash state in REKKR → ammo/attack-state detection) | recoil visible in shots |
| 3 | Camera: explosion shake, hit kick, strafe lean (Remaster, off by default), damage direction marks | ✅ 2026-09-30 barrel at 220 u: shake 0.48 | shake / marks in scenario 12 |
| 4 | World: floating + glowing pickups, monster hit flash, impact chips/dust, richer blood, explosion smoke/embers/debris | ✅ 2026-09-30 | shots, no errors |
| 5 | UI: panel + wheel transitions, HUD pops (touch buttons untouched) | ✅ 2026-09-30 | shots |
| 6 | Tests: scenario 12 (smoothness metric Classic vs Modern, events, explosion, fight, liquid frames); HeadlessTest golden | ✅ 2026-09-30 HeadlessTest golden 176 frames identical, RESULT PASS | PASS |
| 7 | Android 0.8.0 build, Test Lab r8q (1, 11, 12), video; release on the owner's OK | 🚧 (Next: physical r8q run — the Spark quota of 5 physical runs/day was used up on 2026-09-30; virtual MediumPhone.arm/33 run of scenarios 12, 1, 11: Passed, 0 `E Unity`, 36/36 maps errors=0, anim8 still_pct 56.1 → 0.0, recoil fires=3, explosion particles=60; Remaster not available on the emulator, fps meaningless ≈ 8–10) | Passed, 0 `E Unity`, fps ≈ dev7 |

Legend: ⬜ not started · 🚧 in progress (Next: …) · ✅ done · ⏭ deferred (reason).

## 5. Log

- 2026-09-30: the sandbox has 17 cores; `nproc` prints 1 only because `OMP_NUM_THREADS=1` is set (use `nproc --all`).
- 2026-09-30: REKKR's bow (pistol slot) has no muzzle-flash psprite state — fire detection must not rely on it.
- 2026-09-30: a kill clears MF_SHOOTABLE on the same tic the health drops — track health once a thing was shootable.
