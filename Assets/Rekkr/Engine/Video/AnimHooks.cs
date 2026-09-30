// my-rekkr dev8 — render-side animation hooks. Everything here is VISUAL ONLY: the renderers read these
// values while drawing a frame; the simulation (tics, RNG, demos, saves) never reads them. They are
// written once per frame on the main thread by ManagedDoom.UnityPort.AnimFx before the render starts, so
// the parallel strip renderers only read them. All zero/false = the original presentation.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;

namespace ManagedDoom.Video
{
    public static class AnimHooks
    {
        /// <summary>Weapon (psprite) position interpolated between tics (the engine moves it at 35 Hz).</summary>
        public static bool SmoothWeapon;
        /// <summary>Raise / lower of a weapon switch follows an ease curve (same duration, same end points).</summary>
        public static bool EaseSwitch;
        /// <summary>Extra weapon offset in 320×200 weapon pixels (sway, breathing, recoil, landing).</summary>
        public static float WeaponDX, WeaponDY;
        /// <summary>Extra weapon roll in degrees (only the Remaster 3D weapon can roll).</summary>
        public static float WeaponRoll;

        /// <summary>Camera offsets: height (map units), yaw (degrees, CCW) and pitch (200-line shear pixels).</summary>
        public static float ViewDZ, ViewYaw, ViewPitch;
        /// <summary>Camera roll in degrees (Remaster only; the column renderer cannot roll).</summary>
        public static float ViewRoll;

        /// <summary>Animated flats (liquids) cross-fade between their pictures instead of switching every 8 tics.</summary>
        public static bool SmoothLiquids;

        /// <summary>Pickups float and pulse; seconds clock for their phase.</summary>
        public static bool PickupFloat;
        public static float Clock;

        /// <summary>Fullscreen HUD numbers pop up by this many 320-pixels when they change.</summary>
        public static int HudPopHealth, HudPopArmor, HudPopAmmo;

        /// <summary>Monsters flash when hit (Mobj.AnimFlash 0..1, set per tic by AnimFx).</summary>
        public static bool HitFlash;

        /// <summary>Height a pickup is lifted above its spot (map units, 0..5): a slow bob with a per-thing phase.</summary>
        public static float PickupLift(Mobj m)
        {
            if (!PickupFloat || (m.Flags & MobjFlags.Special) == 0) return 0F;
            var phase = (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m) & 63) * 0.1F;
            return 2.5F + 2.5F * MathF.Sin(Clock * 2.4F + phase);
        }

        /// <summary>Brightness pulse of a pickup, 0..1 (used to lift its light a little).</summary>
        public static float PickupGlow(Mobj m)
        {
            if (!PickupFloat || (m.Flags & MobjFlags.Special) == 0) return 0F;
            var phase = (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m) & 63) * 0.1F;
            return 0.5F + 0.5F * MathF.Sin(Clock * 2.4F + phase + 0.8F);
        }

        // ---- weapon states of the raise / lower phase (for the eased switch)
        private static bool[] raiseLower;

        public static bool IsRaiseLower(MobjStateDef s)
        {
            if (s == null) return false;
            if (raiseLower == null)
            {
                var t = new bool[DoomInfo.States.Length];
                foreach (var w in DoomInfo.WeaponInfos)
                {
                    if ((int)w.UpState < t.Length) t[(int)w.UpState] = true;
                    if ((int)w.DownState < t.Length) t[(int)w.DownState] = true;
                }
                raiseLower = t;
            }
            return s.Number >= 0 && s.Number < raiseLower.Length && raiseLower[s.Number];
        }

        /// <summary>The weapon position to draw for a psprite this frame (interpolated / eased / offset).</summary>
        public static void WeaponPos(PlayerSpriteDef psp, Fixed frac, out Fixed sx, out Fixed sy)
        {
            sx = psp.Sx; sy = psp.Sy;
            if (SmoothWeapon && psp.OldState != null && psp.OldSprite == psp.State.Sprite)
            {
                var dx = psp.Sx - psp.OldSx; var dy = psp.Sy - psp.OldSy;
                // a jump bigger than one raise/lower step is a state offset (misc1/misc2) — never smear it
                if (Fixed.Abs(dx) <= Fixed.FromInt(20) && Fixed.Abs(dy) <= Fixed.FromInt(20))
                {
                    sx = psp.OldSx + frac * dx;
                    sy = psp.OldSy + frac * dy;
                }
            }
            if (EaseSwitch && (IsRaiseLower(psp.State) || IsRaiseLower(psp.OldState)))
            {
                // 32 (top) .. 128 (bottom): smoothstep keeps the end points and the duration of the vanilla move
                var top = WeaponBehavior.WeaponTop.ToFloat(); var bot = WeaponBehavior.WeaponBottom.ToFloat();
                var y = sy.ToFloat();
                if (y > top && y < bot)
                {
                    var t = (y - top) / (bot - top);
                    t = t * t * (3F - 2F * t);
                    sy = Fixed.FromFloat(top + t * (bot - top));
                }
            }
            if (WeaponDX != 0 || WeaponDY != 0)
            {
                sx += Fixed.FromFloat(WeaponDX);
                sy += Fixed.FromFloat(WeaponDY);
            }
        }
    }
}
