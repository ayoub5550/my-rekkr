// my-rekkr dev7 — scenario 8 "settings coverage": in every context the game shows a 3D view (title demo,
// live game E1–E4, after a level change, after loading a save; software and Remaster), hold the world
// still, switch each graphics setting on alone and count the frame pixels it changes. A setting that
// changes nothing where it should apply is the owner's "settings are not applied in the whole game" bug.
// Output: one "[REKKR-TEST] cover ..." line per (context, setting) + baseline/all-on shots per context.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using ManagedDoom;
using ManagedDoom.UnityPort;
using UnityEngine;
using Texture = UnityEngine.Texture;

public sealed partial class RekkrApp
{
    /// <summary>Pixels of the last shown frame (after compositor + WorldFx, before PostFx), transposed layout.</summary>
    private Color32[] ReadShownFrame()
    {
        Texture src = lastShownFrame != null ? lastShownFrame : video.FrameTexture;
        if (src is Texture2D cpu) return cpu.GetPixels32();
        var rt = src as RenderTexture;
        var tmp = rt == null ? RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32) : null;
        if (tmp != null) Graphics.Blit(src, tmp);
        var read = rt != null ? rt : tmp;
        var prev = RenderTexture.active; RenderTexture.active = read;
        var tex = new Texture2D(read.width, read.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, read.width, read.height), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        if (tmp != null) RenderTexture.ReleaseTemporary(tmp);
        var p = tex.GetPixels32();
        Destroy(tex);
        return p;
    }

    /// <summary>Percentage of pixels whose largest channel difference is above 6/255 (sizes must match).</summary>
    private static float DiffPct(Color32[] a, Color32[] b)
    {
        if (a == null || b == null) return -1;
        if (a.Length != b.Length) return 100;
        var n = 0;
        for (var i = 0; i < a.Length; i++)
        {
            int dr = Math.Abs(a[i].r - b[i].r), dg = Math.Abs(a[i].g - b[i].g), db = Math.Abs(a[i].b - b[i].b);
            if (Math.Max(dr, Math.Max(dg, db)) > 6) n++;
        }
        return 100F * n / Math.Max(1, a.Length);
    }

    /// <summary>Mean luminance 0..255 of a frame.</summary>
    private static float MeanLum(Color32[] a)
    {
        double s = 0;
        foreach (var c in a) s += 0.3 * c.r + 0.55 * c.g + 0.15 * c.b;
        return (float)(s / Math.Max(1, a.Length));
    }

    private struct CoverToggle { public string Name; public Action On; public bool RemasterOnly; }

    /// <summary>All world-effect settings off, post chain neutral (the "baseline" of the coverage test).</summary>
    private static void CoverBaseline()
    {
        RekkrSettings.SkyFx = RekkrSettings.WaterFx = RekkrSettings.DynLights = RekkrSettings.SunRays = false;
        RekkrSettings.AO = RekkrSettings.Particles = RekkrSettings.DoF = false;
        RekkrSettings.Weather = 3; RekkrSettings.Fog = 0;
        RekkrSettings.SmoothLighting = true;
        RekkrSettings.DarkAreas = 1;
        RekkrSettings.RemasterThings = 1; RekkrSettings.RemasterShadows = false;
        RekkrSettings.DynLightLevel = 1;
    }

    private static readonly CoverToggle[] CoverToggles =
    {
        new CoverToggle { Name = "sky", On = () => RekkrSettings.SkyFx = true },
        new CoverToggle { Name = "water", On = () => RekkrSettings.WaterFx = true },
        new CoverToggle { Name = "weather_rain", On = () => RekkrSettings.Weather = 1 },
        new CoverToggle { Name = "weather_auto", On = () => RekkrSettings.Weather = 0 },
        new CoverToggle { Name = "fog", On = () => RekkrSettings.Fog = 1 },
        new CoverToggle { Name = "lights", On = () => RekkrSettings.DynLights = true },
        new CoverToggle { Name = "rays", On = () => { RekkrSettings.SunRays = true; RekkrSettings.SkyFx = true; } },
        new CoverToggle { Name = "ao", On = () => RekkrSettings.AO = true },
        new CoverToggle { Name = "dof", On = () => RekkrSettings.DoF = true },
        new CoverToggle { Name = "dark_orig", On = () => RekkrSettings.DarkAreas = 0 },
        new CoverToggle { Name = "dark_bright", On = () => RekkrSettings.DarkAreas = 2 },
        new CoverToggle { Name = "smooth_off", On = () => RekkrSettings.SmoothLighting = false },
        new CoverToggle { Name = "rm_flat_things", On = () => RekkrSettings.RemasterThings = 0, RemasterOnly = true },
        new CoverToggle { Name = "rm_shadows", On = () => RekkrSettings.RemasterShadows = true, RemasterOnly = true },
    };

    private readonly StringBuilder coverTable = new StringBuilder();

    /// <summary>Measures every toggle in the current (frozen) view. <paramref name="ctx"/> names the context.</summary>
    private IEnumerator CoverContext(string ctx, bool remaster)
    {
        RekkrSettings.Remaster = remaster && RekkrSettings.RemasterAllowed;
        freezeWorld = true;
        CoverBaseline();
        yield return null; yield return null; yield return null;
        var baseA = ReadShownFrame();
        yield return null; yield return null;
        var baseB = ReadShownFrame();
        var noise = DiffPct(baseA, baseB);
        ShotFrame($"cov_{ctx}_base");
        var lum0 = MeanLum(baseA);
        coverTable.Append($"{ctx}: noise={noise:F2}% lum={lum0:F1}");
        foreach (var t in CoverToggles)
        {
            if (t.RemasterOnly && !RekkrSettings.Remaster) continue;
            CoverBaseline();
            t.On();
            yield return null; yield return null; yield return null;
            var f = ReadShownFrame();
            var d = DiffPct(baseA, f);
            var lum = f != null && f.Length == baseA.Length ? MeanLum(f) : -1;
            Log($"cover ctx={ctx} setting={t.Name} changed_pct={d:F2} noise_pct={noise:F2} lum_base={lum0:F1} lum={lum:F1} lights={worldFx?.LightsLastFrame ?? 0} remaster={RekkrSettings.Remaster}");
            coverTable.Append($" {t.Name}={d:F1}");
        }
        // everything on (Masterpiece world effects) for a visual check of the combination
        CoverBaseline();
        RekkrSettings.SkyFx = RekkrSettings.WaterFx = RekkrSettings.DynLights = RekkrSettings.SunRays = RekkrSettings.AO = true;
        RekkrSettings.Weather = 0; RekkrSettings.Fog = 1; RekkrSettings.RemasterShadows = true;
        yield return null; yield return null; yield return null;
        ShotFrame($"cov_{ctx}_all");
        coverTable.Append('\n');
        freezeWorld = false;
        CoverBaseline();
    }

    private IEnumerator CoverAt(int e, int m, float angle)
    {
        Doom.NewGame(GameSkill.Easy, e, m);
        yield return Wait(1.2F);
        while (Doom.Wiping) yield return null;
        yield return Wait(0.3F);
        var mo = Doom.Game.World.ConsolePlayer.Mobj;
        mo.Angle = new Angle((uint)(angle / 360.0 * 4294967296.0));
        mo.DisableFrameInterpolationForOneFrame();
        Doom.Game.World.ConsolePlayer.DisableFrameInterpolationForOneFrame();
    }

    /// <summary>Spawns a red torch 160 units ahead and an imp fireball 90 units ahead (test only; the world is
    /// frozen), then captures dynamic lights off / low / high with the mean brightness of each.</summary>
    private IEnumerator LightsProbe(string ctx)
    {
        var w = Doom.Game.World; var mo = w.ConsolePlayer.Mobj;
        var ang = mo.Angle.ToRadian();
        float px = mo.X.ToFloat(), py = mo.Y.ToFloat();
        float c = (float)Math.Cos(ang), sn = (float)Math.Sin(ang);
        var torch = w.ThingAllocation.SpawnMobj(Fixed.FromFloat(px + c * 160 - sn * 40), Fixed.FromFloat(py + sn * 160 + c * 40), Mobj.OnFloorZ, MobjType.Misc43);
        var ball = w.ThingAllocation.SpawnMobj(Fixed.FromFloat(px + c * 90 + sn * 30), Fixed.FromFloat(py + sn * 90 - c * 30), mo.Z + Fixed.FromInt(32), MobjType.Troopshot);
        ball.MomX = ball.MomY = ball.MomZ = Fixed.Zero;
        RekkrSettings.Remaster = false;
        freezeWorld = true;
        CoverBaseline();
        Color32[] off = null;
        foreach (var lv in new[] { 0, 1, 2 })
        {
            RekkrSettings.DynLights = lv > 0; RekkrSettings.DynLightLevel = Math.Max(1, lv);
            for (var k = 0; k < 20; k++) yield return null;   // light fade-in
            var f = ReadShownFrame();
            if (lv == 0) off = f;
            Log($"lights ctx={ctx} level={lv} lum={MeanLum(f):F1} lum_off={MeanLum(off):F1} changed_pct={DiffPct(off, f):F2} lights={worldFx?.LightsLastFrame ?? 0}");
            ShotFrame($"lights_{ctx}_lv{lv}");
        }
        freezeWorld = false;
        CoverBaseline();
    }

    /// <summary>dev7 scenario 8 — settings coverage (Linux player or Test Lab).</summary>
    private IEnumerator Scenario8()
    {
        HudMode = 1;
        ApplyPreset(2);   // Enhanced post chain; world effects are switched by the test itself
        RekkrSettings.DynamicRes = false;   // a frame-size change between two captures is not a setting effect
        var rm = RekkrSettings.RemasterAllowed;
        // 1) title demo (DEMO1 in the opening sequence), software and Remaster
        yield return Wait(9F);
        Log($"cover title state={Doom.State} opening={Doom.Opening?.State}");
        yield return CoverContext("title_sw", false);
        if (rm) yield return CoverContext("title_rm", true);
        yield return TapSeq(Ctl.Ok, 1.0F);
        yield return TapSeq(Ctl.Back, 0.8F);
        // 2) live game, map starts of every episode (level changes in between)
        var views = new (int e, int m, float a)[] { (1, 1, 0), (1, 1, 180), (2, 1, 0), (3, 1, 0), (3, 1, 200), (4, 1, 0), (4, 1, 240) };
        foreach (var (e, m, a) in views)
        {
            yield return CoverAt(e, m, a);
            yield return CoverContext($"e{e}m{m}_a{a:F0}_sw", false);
            if (rm) yield return CoverContext($"e{e}m{m}_a{a:F0}_rm", true);
        }
        // 2b) dynamic lights: a torch and a fireball in front of the player, off / low / high
        foreach (var (e, m, a) in new[] { (3, 1, 0F), (1, 1, 90F) })
        {
            yield return CoverAt(e, m, a);
            yield return LightsProbe($"e{e}m{m}_a{a:F0}");
        }
        // 3) after loading a save: save in E1M1, go to E2M1, load
        yield return CoverAt(1, 1, 90);
        QuickSave();
        yield return Wait(0.5F);
        yield return CoverAt(2, 1, 0);
        QuickLoad();
        yield return Wait(1.5F);
        while (Doom.Wiping) yield return null;
        yield return Wait(0.3F);
        Log($"cover after load map=E{Doom.Game.Options.Episode}M{Doom.Game.Options.Map}");
        yield return CoverContext("loaded_sw", false);
        if (rm) yield return CoverContext("loaded_rm", true);
        RekkrSettings.Remaster = false;
        foreach (var line in coverTable.ToString().Split('\n'))
            if (line.Length > 0) Log("covertable " + line);
    }
}
