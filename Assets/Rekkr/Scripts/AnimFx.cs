// my-rekkr dev8 — modern animation layer, VISUAL ONLY.
// Watches the world once per tic (weapon fired, player hurt / landed, monsters hit, missiles exploded) and
// once per frame turns those events plus the camera motion into small spring-driven offsets that the
// renderers read from ManagedDoom.Video.AnimHooks: weapon sway / breathing / strafe tilt / landing dip /
// recoil, camera shake and hit kick, damage direction marks, HUD number pops. Nothing here writes to the
// simulation: player angle, height, momentum, RNG and demos are untouched (HeadlessTest golden = PASS).
// "Classic" animation style = every hook off = the original presentation.
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Collections.Generic;
using ManagedDoom.Video;
using UnityEngine;
using UVec = UnityEngine.Vector3;

namespace ManagedDoom.UnityPort
{
    public sealed class AnimFx
    {
        /// <summary>A critically damped (or slightly bouncy) spring towards a target.</summary>
        private struct Spring
        {
            public float X, V;

            public void Step(float target, float omega, float zeta, float dt)
            {
                // sub-steps keep it stable on a long frame (0.25 s max from the tic loop)
                var n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008F), 1, 32);
                var h = dt / n;
                for (var i = 0; i < n; i++)
                {
                    var a = omega * omega * (target - X) - 2F * zeta * omega * V;
                    V += a * h;
                    X += V * h;
                }
            }

            public void Reset() { X = 0; V = 0; }
        }

        public struct Explosion { public UVec P; public float Size, Floor; public bool Big; }
        public struct DamageMark { public float Angle, Life; }

        /// <summary>Explosions of the last tics, for WorldFx smoke / debris (consumed there, cleared each tic).</summary>
        public readonly List<Explosion> Explosions = new List<Explosion>();
        /// <summary>Directions (world angle, radians) the player was hit from, fading (life 1 → 0).</summary>
        public readonly List<DamageMark> Marks = new List<DamageMark>();
        /// <summary>Per-frame HUD pops (weapon pixels up) for health / armor / ammo.</summary>
        public float PopHealth, PopArmor, PopAmmo;

        // test counters (scenario 12)
        public int Fires, Hits, Explodes, Landings, MonsterFlashes;
        public float MaxWeaponDX, MaxWeaponDY, MaxShake;

        private World world;
        private int lastLevelTime = -1;
        private MobjStateDef lastFlash, lastWeaponState;
        private int lastDamageCount, lastHealth, lastArmor, lastAmmo = -1;
        private WeaponType lastWeapon;
        private Fixed lastMomZ;
        private bool lastOnGround = true;
        private float lastViewAngle, lastPitch, lastYawOffset;
        private bool haveView;
        private readonly HashSet<Mobj> missiles = new HashSet<Mobj>();
        private readonly List<Mobj> gone = new List<Mobj>();

        // springs (weapon pixels, degrees, shear pixels)
        private Spring swayX, swayY, roll, kickY, kickX, dip, camPitch, camYaw, camRoll;
        private float shake, clock, breatheAmp;
        private readonly float[] shakePhase = new float[6];

        /// <summary>The live instance (WorldFx reads its explosion list).</summary>
        public static AnimFx Current { get; private set; }

        public AnimFx()
        {
            Current = this;
            for (var i = 0; i < shakePhase.Length; i++) shakePhase[i] = Random.Range(0F, 100F);
        }

        private static float Level(int v) => v <= 0 ? 0F : v == 1 ? 1F : 1.6F;

        /// <summary>Copies the settings into the render hooks (always, also outside a level).</summary>
        public static void ApplySettings()
        {
            AnimHooks.SmoothWeapon = RekkrSettings.AnimSmoothWeapon;
            AnimHooks.EaseSwitch = RekkrSettings.AnimEaseSwitch;
            AnimHooks.PickupFloat = RekkrSettings.AnimPickups;
            AnimHooks.HitFlash = RekkrSettings.AnimHitFlash;
            AnimHooks.SmoothLiquids = RekkrSettings.AnimLiquids;
        }

        private void ResetAll()
        {
            swayX.Reset(); swayY.Reset(); roll.Reset(); kickY.Reset(); kickX.Reset(); dip.Reset();
            camPitch.Reset(); camYaw.Reset(); camRoll.Reset();
            shake = 0; breatheAmp = 0; haveView = false;
            missiles.Clear(); Explosions.Clear(); Marks.Clear();
            lastFlash = lastWeaponState = null; lastDamageCount = 0; lastAmmo = -1;
            PopHealth = PopArmor = PopAmmo = 0;
            ClearHooks();
        }

        private static void ClearHooks()
        {
            AnimHooks.WeaponDX = AnimHooks.WeaponDY = AnimHooks.WeaponRoll = 0;
            AnimHooks.ViewDZ = AnimHooks.ViewYaw = AnimHooks.ViewPitch = AnimHooks.ViewRoll = 0;
            AnimHooks.HudPopHealth = AnimHooks.HudPopArmor = AnimHooks.HudPopAmmo = 0;
        }

        /// <summary>Once per rendered frame, before the render. game = the level being shown (live, demo or
        /// title demo) or null; live = a live, unpaused game with no menu (springs only move then).</summary>
        public void Frame(DoomGame game, bool live, float pitch, float dt)
        {
            ApplySettings();
            clock += dt;
            AnimHooks.Clock = clock;
            var w = game?.World;
            if (w == null) { if (world != null) { world = null; ResetAll(); } ClearHooks(); return; }
            if (w != world) { world = w; lastLevelTime = -1; ResetAll(); }
            var p = w.DisplayPlayer;
            if (p?.Mobj == null) { ClearHooks(); return; }

            // ---- per-tic events (the tic loop may have run 0..6 tics since the last frame)
            if (w.LevelTime != lastLevelTime)
            {
                Explosions.Clear();
                var first = lastLevelTime < 0;
                lastLevelTime = w.LevelTime;
                Tic(w, p, first);
            }
            if (game.Paused) dt = 0;   // hold every motion while paused (frame = the last tic)

            var motion = Level(RekkrSettings.AnimWeaponMotion);
            var v = ThreeDRenderer.LastView;

            // ---- turn / look rates (the view angle of the last frame, without our own yaw offset)
            var ang = v.Angle - lastYawOffset * Mathf.Deg2Rad;
            float yawRate = 0, pitchRate = 0;
            if (haveView && dt > 0)
            {
                yawRate = Mathf.DeltaAngle(lastViewAngle * Mathf.Rad2Deg, ang * Mathf.Rad2Deg) / dt;   // deg/s, + = left
                pitchRate = (pitch - lastPitch) / dt;                                                 // shear px/s, + = up
            }
            lastViewAngle = ang; lastPitch = pitch; haveView = true;
            if (!live) { yawRate = 0; pitchRate = 0; }

            // ---- movement relative to the view (map units per tic)
            var mo = p.Mobj;
            float mx = mo.MomX.ToFloat(), my = mo.MomY.ToFloat();
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            var lateral = mx * sa - my * ca;                      // + = moving to the right
            var speed = Mathf.Sqrt(mx * mx + my * my);
            var onGround = mo.Z <= mo.FloorZ;

            // weapon sway: lags behind turning and looking, leans against strafing, sinks while rising
            float tx = 0, ty = 0, troll = 0;
            if (motion > 0 && live)
            {
                tx = Mathf.Clamp(yawRate * 0.028F, -9F, 9F) * motion - Mathf.Clamp(lateral * 0.35F, -4F, 4F) * motion;
                ty = Mathf.Clamp(pitchRate * 0.02F, -6F, 6F) * motion;
                if (!onGround) ty += Mathf.Clamp(-mo.MomZ.ToFloat() * -0.35F, -3F, 5F) * motion;
                troll = -Mathf.Clamp(lateral * 0.35F, -3F, 3F) * motion;
                // breathing when standing still
                var still = speed < 0.5F && Mathf.Abs(yawRate) < 20F ? 1F : 0F;
                breatheAmp = Mathf.MoveTowards(breatheAmp, still, dt * 1.5F);
                tx += Mathf.Sin(clock * 0.9F) * 0.9F * breatheAmp * motion;
                ty += (0.5F + 0.5F * Mathf.Sin(clock * 1.8F)) * 1.4F * breatheAmp * motion;
            }
            swayX.Step(tx, 11F, 0.75F, dt);
            swayY.Step(ty, 11F, 0.75F, dt);
            roll.Step(troll, 9F, 0.8F, dt);
            kickY.Step(0, 16F, 0.55F, dt);   // recoil impulses decay (slightly bouncy)
            kickX.Step(0, 16F, 0.6F, dt);
            dip.Step(0, 10F, 0.5F, dt);      // landing dip springs back with a small overshoot

            AnimHooks.WeaponDX = swayX.X + kickX.X;
            AnimHooks.WeaponDY = swayY.X + kickY.X + dip.X;
            AnimHooks.WeaponRoll = roll.X;
            MaxWeaponDX = Mathf.Max(MaxWeaponDX, Mathf.Abs(AnimHooks.WeaponDX));
            MaxWeaponDY = Mathf.Max(MaxWeaponDY, Mathf.Abs(AnimHooks.WeaponDY));

            // ---- camera: shake (smooth noise), hit kick, recoil pitch, optional strafe roll
            var shakeLvl = Level(RekkrSettings.AnimShake);
            shake = Mathf.Max(0, shake - dt * 2.2F * (0.4F + shake));
            var sAmp = shake * shake * shakeLvl;
            MaxShake = Mathf.Max(MaxShake, sAmp);
            float nYaw = Noise(0) * 0.9F * sAmp, nPitch = Noise(2) * 3.5F * sAmp, nZ = Noise(4) * 2.2F * sAmp;
            camPitch.Step(0, 14F, 0.7F, dt);
            camYaw.Step(0, 12F, 0.8F, dt);
            camRoll.Step(RekkrSettings.AnimRoll && live ? Mathf.Clamp(lateral * 0.12F, -1.2F, 1.2F) : 0F, 7F, 1F, dt);
            AnimHooks.ViewYaw = nYaw + camYaw.X;
            AnimHooks.ViewPitch = nPitch + camPitch.X;
            AnimHooks.ViewDZ = nZ;
            AnimHooks.ViewRoll = camRoll.X;
            lastYawOffset = AnimHooks.ViewYaw;

            // ---- damage marks and HUD pops fade
            for (var i = Marks.Count - 1; i >= 0; i--)
            {
                var m = Marks[i]; m.Life -= dt * 0.9F;
                if (m.Life <= 0) Marks.RemoveAt(i); else Marks[i] = m;
            }
            PopHealth = Mathf.Max(0, PopHealth - dt * 10F);
            PopArmor = Mathf.Max(0, PopArmor - dt * 10F);
            PopAmmo = Mathf.Max(0, PopAmmo - dt * 10F);
            AnimHooks.HudPopHealth = Mathf.RoundToInt(PopHealth);
            AnimHooks.HudPopArmor = Mathf.RoundToInt(PopArmor);
            AnimHooks.HudPopAmmo = Mathf.RoundToInt(PopAmmo);
        }

        private float Noise(int k)
        {
            var t = clock * 23F;
            return 0.6F * Mathf.Sin(t + shakePhase[k]) + 0.4F * Mathf.Sin(t * 1.73F + shakePhase[k + 1]);
        }

        private void Tic(World w, Player p, bool first)
        {
            var mo = p.Mobj;
            var flash = p.PlayerSprites[(int)PlayerSprite.Flash].State;
            var wstate = p.PlayerSprites[(int)PlayerSprite.Weapon].State;
            var ammoType = DoomInfo.WeaponInfos[(int)p.ReadyWeapon].Ammo;
            var ammo = ammoType == AmmoType.NoAmmo ? -1 : p.Ammo[(int)ammoType];
            var onGround = mo.Z <= mo.FloorZ;
            if (!first)
            {
                // weapon fired: a new muzzle-flash state, or (fist / axe) the attack state of the weapon
                var info = DoomInfo.WeaponInfos[(int)p.ReadyWeapon];
                // REKKR re-skins the weapons (the bow has no muzzle-flash state), so any of: ammo spent with the same
                // weapon, the weapon entering its attack state, or a new flash state
                var fired = (flash != null && lastFlash == null) ||
                            (wstate != null && wstate != lastWeaponState && wstate.Number == (int)info.AttackState) ||
                            (p.ReadyWeapon == lastWeapon && ammo >= 0 && lastAmmo >= 0 && ammo < lastAmmo);
                if (fired) Fire(p.ReadyWeapon);

                // hurt: DamageCount rises (also for armour-only hits)
                if (p.DamageCount > lastDamageCount && p.Health > 0)
                {
                    var dmg = Mathf.Max(1, (lastHealth - p.Health) + (lastArmor - p.ArmorPoints));
                    Hurt(p, dmg);
                }
                // landed after a fall / jump
                if (onGround && !lastOnGround && lastMomZ.ToFloat() < -6F) Land(-lastMomZ.ToFloat());

                if (RekkrSettings.AnimUi)
                {
                    if (p.Health != lastHealth) PopHealth = 2F;
                    if (p.ArmorPoints != lastArmor) PopArmor = 2F;
                    if (ammo != lastAmmo && p.ReadyWeapon == lastWeapon) PopAmmo = ammo > lastAmmo ? 2F : 1F;
                }
            }
            lastFlash = flash; lastWeaponState = wstate;
            lastDamageCount = p.DamageCount; lastHealth = p.Health; lastArmor = p.ArmorPoints;
            lastAmmo = ammo; lastWeapon = p.ReadyWeapon;
            lastMomZ = mo.MomZ; lastOnGround = onGround;

            // monsters hit, missiles exploded
            var vx = mo.X.ToFloat(); var vy = mo.Y.ToFloat();
            foreach (var th in w.Thinkers)
            {
                if (!(th is Mobj m) || m.ThinkerState != ThinkerState.Active) continue;
                if (m.AnimFlash > 0) m.AnimFlash = Mathf.Max(0, m.AnimFlash - 0.34F);
                if ((m.Flags & MobjFlags.Missile) != 0) { missiles.Add(m); continue; }
                // tracked once shootable: a kill clears MF_SHOOTABLE on the same tic the health drops
                if (m.Player == null && ((m.Flags & MobjFlags.Shootable) != 0 || m.AnimLastHealth != int.MinValue))
                {
                    if (m.AnimLastHealth != int.MinValue && m.Health < m.AnimLastHealth)
                    {
                        m.AnimFlash = 1F; MonsterFlashes++;
                        if (m.Type == MobjType.Barrel && m.Health <= 0) Explode(m, vx, vy, 1.4F, true);
                    }
                    m.AnimLastHealth = m.Health;
                }
            }
            // a tracked missile that lost MF_MISSILE (P_ExplodeMissile) or was removed has exploded
            gone.Clear();
            foreach (var m in missiles)
            {
                if (m.ThinkerState != ThinkerState.Active || (m.Flags & MobjFlags.Missile) == 0) gone.Add(m);
            }
            foreach (var m in gone)
            {
                missiles.Remove(m);
                if ((m.Flags & MobjFlags.Missile) == 0 && m.ThinkerState == ThinkerState.Active)
                {
                    var big = m.Type == MobjType.Rocket || m.Type == MobjType.Bfg || m.Type == MobjType.Fatshot;
                    Explode(m, vx, vy, big ? 1.1F : 0.45F, big);
                }
            }
        }

        private void Explode(Mobj m, float vx, float vy, float size, bool big)
        {
            Explodes++;
            Explosions.Add(new Explosion { P = new UVec(m.X.ToFloat(), m.Y.ToFloat(), m.Z.ToFloat() + 8F), Size = size, Floor = m.FloorZ.ToFloat(), Big = big });
            if (!big) return;
            var dx = m.X.ToFloat() - vx; var dy = m.Y.ToFloat() - vy;
            var d = Mathf.Sqrt(dx * dx + dy * dy);
            var k = Mathf.Clamp01(1F - d / 900F);
            shake = Mathf.Clamp01(Mathf.Max(shake, k * size));
        }

        private void Fire(WeaponType w)
        {
            Fires++;
            if (!RekkrSettings.AnimRecoil) return;
            // weapon pixels down (Y+) and camera shear up; REKKR re-skins the vanilla slots
            float y, x, cam;
            switch (w)
            {
                case WeaponType.Fist: y = 3F; x = 1.5F; cam = 0F; break;
                case WeaponType.Chainsaw: y = 1.5F; x = 0.8F; cam = 0F; break;
                case WeaponType.Pistol: y = 5F; x = 0.8F; cam = 0.7F; break;
                case WeaponType.Shotgun: y = 9F; x = 1.2F; cam = 2F; break;
                case WeaponType.SuperShotgun: y = 12F; x = 1.8F; cam = 3F; break;
                case WeaponType.Chaingun: y = 3.5F; x = 0.8F; cam = 0.5F; break;
                case WeaponType.Missile: y = 10F; x = 1F; cam = 2.5F; break;
                case WeaponType.Plasma: y = 2.5F; x = 0.5F; cam = 0.3F; break;
                default: y = 12F; x = 1F; cam = 3F; break;   // BFG
            }
            kickY.V += y * 22F;
            kickX.V += (Random.value < 0.5F ? -x : x) * 18F;
            camPitch.V += cam * 20F;
        }

        private void Hurt(Player p, int dmg)
        {
            Hits++;
            var k = Mathf.Clamp(dmg / 25F, 0.25F, 1F);
            var a = p.Attacker;
            float rel = 0; var hasDir = a != null && a != p.Mobj;
            if (hasDir)
            {
                var worldAng = Mathf.Atan2(a.Y.ToFloat() - p.Mobj.Y.ToFloat(), a.X.ToFloat() - p.Mobj.X.ToFloat());
                rel = Mathf.DeltaAngle(ThreeDRenderer.LastView.Angle * Mathf.Rad2Deg, worldAng * Mathf.Rad2Deg);   // + = attacker on the left
                if (RekkrSettings.AnimDamageDir) Marks.Add(new DamageMark { Angle = worldAng, Life = 1F });
            }
            if (!RekkrSettings.AnimHitKick) return;
            // the view is pushed away from the hit: yaw away from the attacker, pitch up a little
            camYaw.V += (hasDir ? -Mathf.Sign(rel) * Mathf.Clamp01(Mathf.Abs(rel) / 60F) : 0F) * 45F * k;
            camPitch.V += 40F * k;
            kickY.V += 60F * k;
            shake = Mathf.Max(shake, 0.35F * k);
        }

        private void Land(float fallSpeed)
        {
            Landings++;
            var motion = Level(RekkrSettings.AnimWeaponMotion);
            if (motion <= 0) return;
            dip.V += Mathf.Clamp(fallSpeed * 9F, 40F, 170F) * motion;
        }
    }
}
