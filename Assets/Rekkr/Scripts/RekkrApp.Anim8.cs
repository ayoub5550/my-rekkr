// my-rekkr dev8 — scenario 12 "animation": measures how smoothly the weapon moves per rendered frame with the
// Classic style (the engine's 35 Hz steps) and with Modern (interpolated + sway), in the software renderer and
// in Remaster; counts the animation events (fire / hits / landings / explosions / monster flashes); captures
// recoil, explosion smoke + shake, liquid cross-fade and the damage-direction marks. Visual only: nothing here
// changes the simulation except the test's own spawned barrel.
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Collections;
using System.Collections.Generic;
using ManagedDoom;
using ManagedDoom.UnityPort;
using ManagedDoom.Video;
using UnityEngine;

public sealed partial class RekkrApp
{
    private IEnumerator Scenario12()
    {
        HudMode = 1;
        ApplyPreset(3);
        RekkrSettings.DynamicRes = false;
        RekkrSettings.Particles = true;
        Application.logMessageReceived += CountErrors;
        yield return Wait(3F);
        yield return TapSeq(Ctl.Ok, 1.0F);
        yield return TapSeq(Ctl.Back, 0.8F);
        var renderers = RekkrSettings.RemasterAllowed ? new[] { false, true } : new[] { false };
        foreach (var rm in renderers)
        {
            RekkrSettings.Remaster = rm;
            var tag = rm ? "rm" : "sw";
            // ---- 1. weapon smoothness while walking: Classic vs Modern
            foreach (var style in new[] { 0, 1 })
            {
                RekkrSettings.ApplyAnimStyle(style);
                yield return CoverAt(1, 1, 90);
                var xs = new List<float>(900); var ys = new List<float>(900); var moving = new List<bool>(900);
                var ft = new List<float>(900);
                var play = Play(5F, null);
                while (play.MoveNext())
                {
                    yield return play.Current;
                    var p = Doom.Game?.World?.ConsolePlayer;
                    xs.Add(ThreeDRenderer.LastWeaponX); ys.Add(ThreeDRenderer.LastWeaponY);
                    moving.Add(p?.Mobj != null && p.Bob.ToFloat() > 2F);
                    ft.Add(Time.unscaledDeltaTime);
                }
                LogSmoothness($"{tag}_style{style}", xs, ys, moving, ft);
            }

            // ---- 2. recoil + events (Modern): fire a few shots standing still, frames shot at fixed delays
            RekkrSettings.ApplyAnimStyle(1);
            yield return CoverAt(1, 1, 90);
            ResetAnimCounters();
            input.AutoFire = true;
            for (var k = 0; k < 6; k++) { yield return Wait(0.06F); ShotFrame($"a8_{tag}_recoil_{k}"); }
            input.AutoFire = false;
            yield return Wait(0.8F);
            Log($"anim8 {tag} recoil fires={animFx.Fires} max_dx={animFx.MaxWeaponDX:F1} max_dy={animFx.MaxWeaponDY:F1}");

            // ---- 3. explosion: a barrel 220 units ahead is destroyed (smoke, embers, debris, camera shake)
            {
                var w = Doom.Game.World; var mo = w.ConsolePlayer.Mobj;
                var a = (float)mo.Angle.ToRadian();
                float c = Mathf.Cos(a), s = Mathf.Sin(a), px = mo.X.ToFloat(), py = mo.Y.ToFloat();
                var barrel = w.ThingAllocation.SpawnMobj(Fixed.FromFloat(px + c * 220), Fixed.FromFloat(py + s * 220), Mobj.OnFloorZ, MobjType.Barrel);
                yield return Wait(0.3F);
                ResetAnimCounters();
                w.ThingInteraction.DamageMobj(barrel, null, null, 100);
                for (var k = 0; k < 8; k++) { yield return Wait(0.12F); ShotFrame($"a8_{tag}_boom_{k}"); }
                Log($"anim8 {tag} explosion explodes={animFx.Explodes} max_shake={animFx.MaxShake:F2} particles={worldFx?.ParticlesLastFrame}");
            }

            // ---- 4. a short fight (hits, flashes, damage marks, HUD pops) with the real screen (IMGUI marks)
            ResetAnimCounters();
            yield return CoverAt(1, 2, 0);
            {
                // two monsters ahead that will shoot back (hits, damage marks, monster flashes)
                var w = Doom.Game.World; var mo = w.ConsolePlayer.Mobj;
                var a = (float)mo.Angle.ToRadian();
                float c = Mathf.Cos(a), s = Mathf.Sin(a), px = mo.X.ToFloat(), py = mo.Y.ToFloat();
                foreach (var (d, side, type) in new[] { (260F, 40F, MobjType.Troop), (320F, -60F, MobjType.Possessed) })
                {
                    var m = w.ThingAllocation.SpawnMobj(Fixed.FromFloat(px + c * d - s * side), Fixed.FromFloat(py + s * d + c * side), Mobj.OnFloorZ, type);
                    m.Target = mo;
                    m.SetState(m.Info.SeeState);
                }
            }
            var fight = Play(10F, null);
            var shots = 0; var tShot = Time.unscaledTime + 2F;
            while (fight.MoveNext())
            {
                yield return fight.Current;
                if (Time.unscaledTime > tShot && shots < 4) { tShot += 2F; Shot($"a8_{tag}_fight_{shots++}"); }
            }
            Log($"anim8 {tag} fight fires={animFx.Fires} hits={animFx.Hits} landings={animFx.Landings} explodes={animFx.Explodes} monster_flashes={animFx.MonsterFlashes} marks={animFx.Marks.Count} errors={errorLogs}");
        }

        // ---- 5. liquid cross-fade: frozen view of an animated floor, several frames within one picture (8 tics)
        RekkrSettings.Remaster = false;
        foreach (var style in new[] { 0, 1 })
        {
            RekkrSettings.ApplyAnimStyle(style);
            yield return CoverAt(1, 1, 0);
            if (!FaceLiquid()) { Log("anim8 liquid none"); break; }
            yield return null; yield return null;
            for (var k = 0; k < 6; k++) { yield return Wait(0.045F); ShotFrame($"a8_liquid_style{style}_{k}"); }
        }
        RekkrSettings.ApplyAnimStyle(1);
        Application.logMessageReceived -= CountErrors;
        Log($"anim8 summary errors={errorLogs}{(firstError != null ? " first=\"" + firstError + "\"" : "")}");
        RekkrSettings.Remaster = false;
    }

    private void ResetAnimCounters()
    {
        animFx.Fires = animFx.Hits = animFx.Explodes = animFx.Landings = animFx.MonsterFlashes = 0;
        animFx.MaxWeaponDX = animFx.MaxWeaponDY = animFx.MaxShake = 0;
    }

    /// <summary>Logs how the weapon moves per frame while the player walks: the share of frames where it did not
    /// move at all (a 35 Hz step renders the same spot on 2–3 frames at 90–120 Hz) and the step spread.</summary>
    private void LogSmoothness(string tag, List<float> xs, List<float> ys, List<bool> moving, List<float> ft)
    {
        int n = 0, still = 0; float sum = 0, sum2 = 0, max = 0;
        for (var i = 1; i < xs.Count; i++)
        {
            if (!moving[i] || !moving[i - 1]) continue;
            var dx = xs[i] - xs[i - 1]; var dy = ys[i] - ys[i - 1];
            var d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d > 30F) continue;   // raise/lower or state offsets
            n++; sum += d; sum2 += d * d; max = Mathf.Max(max, d);
            if (d < 0.01F) still++;
        }
        float fsum = 0; foreach (var f in ft) fsum += f;
        var mean = n > 0 ? sum / n : 0; var sd = n > 1 ? Mathf.Sqrt(Mathf.Max(0, sum2 / n - mean * mean)) : 0;
        Log($"anim8 smooth {tag} frames={n} still_pct={(n > 0 ? 100F * still / n : 0):F1} mean_step={mean:F2} sd_step={sd:F2} cv={(mean > 0 ? sd / mean : 0):F2} max_step={max:F2} fps={ft.Count / Mathf.Max(0.001F, fsum):F1}");
    }

    /// <summary>Puts the player at the edge of the nearest animated-floor sector, looking into it (frozen world).</summary>
    private bool FaceLiquid()
    {
        var w = Doom.Game.World; var mo = w.ConsolePlayer.Mobj;
        var next = w.Specials.FlatTranslationNext;
        Sector best = null; var bestD = float.MaxValue; float bx = 0, by = 0;
        float px = mo.X.ToFloat(), py = mo.Y.ToFloat();
        foreach (var sec in w.Map.Sectors)
        {
            if (next[sec.FloorFlat] == sec.FloorFlat) continue;   // not animated
            var l = sec.Lines.Length > 0 ? sec.Lines[0] : null;
            if (l == null) continue;
            float cx = 0, cy = 0;
            foreach (var ln in sec.Lines) { cx += ln.Vertex1.X.ToFloat(); cy += ln.Vertex1.Y.ToFloat(); }
            cx /= sec.Lines.Length; cy /= sec.Lines.Length;
            var d = (cx - px) * (cx - px) + (cy - py) * (cy - py);
            if (d < bestD) { bestD = d; best = sec; bx = cx; by = cy; }
        }
        if (best == null) return false;
        // stand 160 units south-west of the centre on walkable ground if possible, looking at the centre
        foreach (var (ox, oy) in new[] { (-160F, 0F), (160F, 0F), (0F, -160F), (0F, 160F), (-110F, -110F), (110F, 110F) })
        {
            var tx = bx + ox; var ty = by + oy;
            var ss = Geometry.PointInSubsector(Fixed.FromFloat(tx), Fixed.FromFloat(ty), w.Map);
            if (ss.Sector == best) continue;
            if (ss.Sector.CeilingHeight - ss.Sector.FloorHeight < Fixed.FromInt(56)) continue;
            w.ThingMovement.UnsetThingPosition(mo);
            mo.X = Fixed.FromFloat(tx); mo.Y = Fixed.FromFloat(ty);
            w.ThingMovement.SetThingPosition(mo);
            mo.Z = mo.Subsector.Sector.FloorHeight; mo.FloorZ = mo.Z; mo.CeilingZ = mo.Subsector.Sector.CeilingHeight;
            mo.MomX = mo.MomY = Fixed.Zero;
            var ang = Mathf.Atan2(-oy, -ox);
            mo.Angle = new Angle((uint)((ang < 0 ? ang + 2 * Mathf.PI : ang) / (2 * Mathf.PI) * 4294967296.0));
            w.ConsolePlayer.ViewZ = mo.Z + Player.NormalViewHeight;
            mo.DisableFrameInterpolationForOneFrame(); w.ConsolePlayer.DisableFrameInterpolationForOneFrame();
            Log($"anim8 liquid sector={best.Number} flat={best.FloorFlat} at=({tx:F0},{ty:F0})");
            return true;
        }
        return false;
    }
}
