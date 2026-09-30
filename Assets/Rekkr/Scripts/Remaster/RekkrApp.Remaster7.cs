// my-rekkr dev7 — scenario 10 "Remaster dev7": 3D weapon vs original, point lights with/without shadows,
// spectre refraction, and a door + lift clip (shot sequences in Remaster). Frozen-world A/B shots where
// possible; the door/lift parts run the world so the sectors move.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections;
using ManagedDoom;
using ManagedDoom.UnityPort;
using UnityEngine;

public sealed partial class RekkrApp
{
    private static readonly LineSpecial[] DoorSpecials = { (LineSpecial)1, (LineSpecial)31, (LineSpecial)117, (LineSpecial)118 };
    private static readonly LineSpecial[] LiftSpecials = { (LineSpecial)62, (LineSpecial)88, (LineSpecial)120, (LineSpecial)121, (LineSpecial)21, (LineSpecial)10 };

    /// <summary>Puts the player 40 units in front of the nearest line with one of <paramref name="specials"/>
    /// (on its front side, facing it). Returns the line or null.</summary>
    private LineDef FaceNearestLine(LineSpecial[] specials)
    {
        var w = Doom.Game.World; var mo = w.ConsolePlayer.Mobj;
        LineDef best = null; var bestD = float.MaxValue;
        float px = mo.X.ToFloat(), py = mo.Y.ToFloat();
        foreach (var l in w.Map.Lines)
        {
            if (Array.IndexOf(specials, l.Special) < 0 || l.FrontSector == null) continue;
            var mx = (l.Vertex1.X.ToFloat() + l.Vertex2.X.ToFloat()) / 2; var my = (l.Vertex1.Y.ToFloat() + l.Vertex2.Y.ToFloat()) / 2;
            var d = (mx - px) * (mx - px) + (my - py) * (my - py);
            if (d < bestD) { bestD = d; best = l; }
        }
        if (best == null) return null;
        float x1 = best.Vertex1.X.ToFloat(), y1 = best.Vertex1.Y.ToFloat(), x2 = best.Vertex2.X.ToFloat(), y2 = best.Vertex2.Y.ToFloat();
        float dx = x2 - x1, dy = y2 - y1, len = Mathf.Sqrt(dx * dx + dy * dy);
        // front side normal of a Doom line is (dy, -dx)
        float nx = dy / len, ny = -dx / len;
        float cx = (x1 + x2) / 2 + nx * 48, cy = (y1 + y2) / 2 + ny * 48;
        w.ThingMovement.UnsetThingPosition(mo);
        mo.X = Fixed.FromFloat(cx); mo.Y = Fixed.FromFloat(cy);
        w.ThingMovement.SetThingPosition(mo);
        mo.Z = mo.Subsector.Sector.FloorHeight; mo.FloorZ = mo.Z; mo.CeilingZ = mo.Subsector.Sector.CeilingHeight;
        mo.MomX = mo.MomY = Fixed.Zero;
        var ang = Mathf.Atan2(-ny, -nx);
        mo.Angle = new Angle((uint)((ang < 0 ? ang + 2 * Mathf.PI : ang) / (2 * Mathf.PI) * 4294967296.0));
        w.ConsolePlayer.ViewZ = mo.Z + Player.NormalViewHeight;
        mo.DisableFrameInterpolationForOneFrame(); w.ConsolePlayer.DisableFrameInterpolationForOneFrame();
        return best;
    }

    private IEnumerator Scenario10()
    {
        HudMode = 1;
        ApplyPreset(3);
        RekkrSettings.DynamicRes = false;
        RekkrSettings.Remaster = RekkrSettings.RemasterAllowed;
        yield return Wait(3F);
        yield return TapSeq(Ctl.Ok, 1.0F);
        yield return TapSeq(Ctl.Back, 0.8F);
        foreach (var ep in new[] { 1, 2, 4 })
        {
            yield return CoverAt(ep, 1, 0);
            freezeWorld = true;
            for (var wpn = 0; wpn < 2; wpn++)
            {
                RekkrSettings.RemasterWeapon = wpn;
                for (var k = 0; k < 12; k++) yield return null;   // first use: the extruded mesh is built on a worker
                ShotFrame($"rm7_e{ep}_weapon{wpn}");
                Log($"rm7 weapon ep={ep} mode={wpn} gpu_layers={gpu?.WeaponLayersDrawn}");
            }
            freezeWorld = false;
        }
        // point lights: fireball + torch ahead; lights off / on without shadows / on with shadows
        yield return CoverAt(1, 1, 90);
        {
            var w = Doom.Game.World; var mo = w.ConsolePlayer.Mobj;
            var a = (float)mo.Angle.ToRadian(); float c = Mathf.Cos(a), s = Mathf.Sin(a), px = mo.X.ToFloat(), py = mo.Y.ToFloat();
            var ball = w.ThingAllocation.SpawnMobj(Fixed.FromFloat(px + c * 110 + s * 20), Fixed.FromFloat(py + s * 110 - c * 20), mo.Z + Fixed.FromInt(30), MobjType.Troopshot);
            ball.MomX = ball.MomY = ball.MomZ = Fixed.Zero;
            var spec = w.ThingAllocation.SpawnMobj(Fixed.FromFloat(px + c * 150 - s * 40), Fixed.FromFloat(py + s * 150 + c * 40), Mobj.OnFloorZ, MobjType.Shadows);
            freezeWorld = true;
            foreach (var (lights, sh, name) in new[] { (false, false, "off"), (true, false, "noshadow"), (true, true, "shadow") })
            {
                RekkrSettings.DynLights = lights; RekkrSettings.DynLightLevel = 2; RekkrSettings.RemasterLightShadows = sh;
                for (var k = 0; k < 20; k++) yield return null;
                ShotFrame($"rm7_lights_{name}");
                Log($"rm7 lights {name} gpu_lights={gpu?.PointLights} gpu_shadows={gpu?.PointShadows} calls={gpu?.DrawCalls} cpu_ms={gpu?.LastCpuMs:F2}");
            }
            freezeWorld = false;
            RekkrSettings.DynLightLevel = 1; RekkrSettings.RemasterLightShadows = true;
        }
        // door + lift clips (world running)
        foreach (var (e, m) in new[] { (1, 1), (1, 2), (2, 1) })
        {
            foreach (var (kind, specials) in new[] { ("door", DoorSpecials), ("lift", LiftSpecials) })
            {
                yield return CoverAt(e, m, 0);
                var line = FaceNearestLine(specials);
                if (line == null) { Log($"rm7 {kind} E{e}M{m} none"); continue; }
                yield return null; yield return null;
                var w = Doom.Game.World;
                var used = w.MapInteraction.UseSpecialLine(w.ConsolePlayer.Mobj, line, 0);
                Log($"rm7 {kind} E{e}M{m} line_mid=({(line.Vertex1.X.ToFloat() + line.Vertex2.X.ToFloat()) / 2:F0},{(line.Vertex1.Y.ToFloat() + line.Vertex2.Y.ToFloat()) / 2:F0}) special={(int)line.Special} used={used}");
                for (var k = 0; k < 6; k++)
                {
                    yield return Wait(0.5F);
                    ShotFrame($"rm7_{kind}_E{e}M{m}_{k}");
                }
                // the same moment in the software renderer (parity of the moved sectors)
                freezeWorld = true;
                RekkrSettings.Remaster = false;
                yield return null; yield return null; yield return null;
                ShotFrame($"rm7_{kind}_E{e}M{m}_5sw");
                RekkrSettings.Remaster = RekkrSettings.RemasterAllowed;
                yield return null; yield return null; yield return null;
                ShotFrame($"rm7_{kind}_E{e}M{m}_5gpu");
                freezeWorld = false;
            }
        }
        RekkrSettings.Remaster = false;
    }
}

public sealed partial class RekkrApp
{
    private int errorLogs;
    private string firstError;

    private void CountErrors(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        errorLogs++;
        firstError ??= msg.Length > 160 ? msg.Substring(0, 160) : msg;
    }

    /// <summary>dev7 scenario 11 — all 36 maps in Remaster with every effect on (Masterpiece + rain + lights + 3D
    /// weapon): frozen software/Remaster pair at the start (pixel difference), then 6 s of autopilot play with
    /// fire; logs fps, lights, errors per map.</summary>
    private IEnumerator Scenario11()
    {
        HudMode = 1;
        ApplyPreset(3);
        RekkrSettings.DynamicRes = false;
        RekkrSettings.Weather = 0; RekkrSettings.Particles = true;
        var rm = RekkrSettings.RemasterAllowed;
        Application.logMessageReceived += CountErrors;
        yield return Wait(3F);
        yield return TapSeq(Ctl.Ok, 1.0F);
        yield return TapSeq(Ctl.Back, 0.8F);
        var ok = 0;
        for (var e = 1; e <= 4; e++)
            for (var m = 1; m <= 9; m++)
            {
                var err0 = errorLogs;
                RekkrSettings.Remaster = false;
                Doom.NewGame(GameSkill.Medium, e, m);
                yield return Wait(1.0F);
                while (Doom.Wiping) yield return null;
                yield return Wait(0.3F);
                freezeWorld = true;
                yield return null; yield return null;
                var sw = ReadShownFrame();
                ShotFrame($"all_E{e}M{m}_sw");
                RekkrSettings.Remaster = rm;
                for (var k = 0; k < 4; k++) yield return null;
                var gp = ReadShownFrame();
                ShotFrame($"all_E{e}M{m}_rm");
                freezeWorld = false;
                var diff = DiffPct(sw, gp);
                var ft = new System.Collections.Generic.List<float>(1000);
                var play = Play(6F, null);
                while (play.MoveNext()) { yield return play.Current; ft.Add(Time.unscaledDeltaTime); }
                ShotFrame($"all_E{e}M{m}_play");
                float sum = 0; foreach (var f in ft) sum += f;
                var errs = errorLogs - err0;
                if (errs == 0) ok++;
                Log($"allmaps E{e}M{m} sw_vs_rm_pct={diff:F1} fps={ft.Count / Mathf.Max(0.001F, sum):F1} tris={gpu?.Triangles} lights={worldFx?.LightsLastFrame} gpu_lights={gpu?.PointLights} shadows={gpu?.PointShadows} weapon_layers={gpu?.WeaponLayersDrawn} errors={errs}{(errs > 0 ? " first=\"" + firstError + "\"" : "")}");
                firstError = null;
            }
        Application.logMessageReceived -= CountErrors;
        Log($"allmaps summary maps_without_errors={ok}/36 remaster={rm}");
        RekkrSettings.Remaster = false;
    }
}
