// my-rekkr dev6 — Remaster test hooks: frame capture (the final frame texture, no touch buttons) and
// scenario 7 "parity": warps to fixed points, freezes the world, and captures the same view with the
// software renderer and with Remaster (REKKR_PARITY="e,m,x,y,angle,pitch;..." overrides the points).
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ManagedDoom;
using ManagedDoom.UnityPort;
using ManagedDoom.Video;
using UnityEngine;
using Texture = UnityEngine.Texture;

public sealed partial class RekkrApp
{
    /// <summary>Writes the final frame (after compositor/WorldFx/PostFx input) as PNG in frame orientation.</summary>
    private void ShotFrame(string name, Texture src = null)
    {
        if (string.IsNullOrEmpty(shotDir) || video == null) return;
        try
        {
            var mo = Doom.Game.World.ConsolePlayer.Mobj;   // where the shot was taken (REKKR_PARITY re-shoots it)
            Log($"shot {name} E{Doom.Game.Options.Episode}M{Doom.Game.Options.Map} at=({mo.X.ToFloat():F0},{mo.Y.ToFloat():F0}) angle={mo.Angle.Data / 4294967296.0 * 360.0:F0}");
        }
        catch (Exception) { }
        src ??= lastShownFrame != null ? lastShownFrame : video.FrameTexture;
        int tw = src.width, th = src.height;   // transposed: tw = frame height, th = frame width
        Color32[] p;
        Texture2D tex = null;
        if (src is Texture2D cpu) p = cpu.GetPixels32();   // the software frame: exact CPU bytes
        else
        {
            // read a RenderTexture directly (a Blit through the default shader may filter the G-buffer codes)
            var direct = src as RenderTexture;
            var rt = direct != null ? direct : RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
            if (direct == null) Graphics.Blit(src, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, tw, th), 0, 0); tex.Apply();
            RenderTexture.active = prev; if (direct == null) RenderTexture.ReleaseTemporary(rt);
            p = tex.GetPixels32();
        }
        int W = th, H = tw;
        var outTex = new Texture2D(W, H, TextureFormat.RGB24, false);
        var o = new Color32[W * H];
        for (var x = 0; x < W; x++)
            for (var y = 0; y < H; y++)
            {
                var c = p[x * tw + y];   // texel (u = y, v = x)
                o[(H - 1 - y) * W + x] = new Color32(c.r, c.g, c.b, 255);
            }
        outTex.SetPixels32(o); outTex.Apply();
        File.WriteAllBytes(Path.Combine(shotDir, $"s{testScenario}_{name}.png"), outTex.EncodeToPNG());
        if (Environment.GetEnvironmentVariable("REKKR_SHOT_ALPHA") == "1")   // debug: G-buffer codes as grey
        {
            for (var x = 0; x < W; x++)
                for (var y = 0; y < H; y++) { var a = p[x * tw + y].a; o[(H - 1 - y) * W + x] = new Color32(a, a, a, 255); }
            outTex.SetPixels32(o); outTex.Apply();
            File.WriteAllBytes(Path.Combine(shotDir, $"s{testScenario}_{name}_alpha.png"), outTex.EncodeToPNG());
        }
        if (tex != null) Destroy(tex);
        Destroy(outTex);
    }

    private Texture lastShownFrame;

    private static readonly string DefaultParity =
        "1,1,-1,-1,0,0;1,1,-1,-1,90,0;1,1,-1,-1,180,0;1,1,-1,-1,270,0;1,1,-1,-1,45,40;1,1,-1,-1,135,-40;" +
        "2,1,-1,-1,0,0;2,1,-1,-1,120,0;3,1,-1,-1,0,0;3,1,-1,-1,200,0;4,1,-1,-1,0,0;4,1,-1,-1,240,0;1,7,-1,-1,30,0";

    private IEnumerator Scenario7()
    {
        HudMode = 1;
        yield return Wait(3F);
        yield return TapSeq(Ctl.Ok, 1.0F);
        yield return TapSeq(Ctl.Back, 0.8F);
        var spec = Environment.GetEnvironmentVariable("REKKR_PARITY");
        if (string.IsNullOrEmpty(spec)) spec = DefaultParity;
        var k = 0;
        int lastE = -1, lastM = -1;
        foreach (var item in spec.Split(';'))
        {
            var f = item.Split(',');
            if (f.Length < 6) continue;
            int e = int.Parse(f[0]), m = int.Parse(f[1]);
            float x = float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture), y = float.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture);
            float ang = float.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture), pitch = float.Parse(f[5], System.Globalization.CultureInfo.InvariantCulture);
            if (e != lastE || m != lastM)
            {
                RekkrSettings.Remaster = false;
                Doom.NewGame(GameSkill.Easy, e, m);
                yield return Wait(1.2F);
                while (Doom.Wiping) yield return null;
                yield return Wait(0.3F);
                lastE = e; lastM = m;
            }
            var world = Doom.Game.World; var pl = world.ConsolePlayer; var mo = pl.Mobj;
            if (x != -1 || y != -1)
            {
                world.ThingMovement.UnsetThingPosition(mo);
                mo.X = Fixed.FromFloat(x); mo.Y = Fixed.FromFloat(y);
                world.ThingMovement.SetThingPosition(mo);
                mo.Z = mo.Subsector.Sector.FloorHeight;
                mo.FloorZ = mo.Z; mo.CeilingZ = mo.Subsector.Sector.CeilingHeight;
            }
            mo.Angle = new Angle((uint)(ang / 360.0 * 4294967296.0));
            pl.ViewZ = mo.Z + Player.NormalViewHeight;
            mo.DisableFrameInterpolationForOneFrame();   // frozen world: frameFrac 0 would render the old spot
            pl.DisableFrameInterpolationForOneFrame();
            input.SetPitch(pitch);
            freezeWorld = true;
            yield return null; yield return null;
            RekkrSettings.Remaster = false;
            yield return null; yield return null; yield return null;
            ShotFrame($"par{k:D2}_E{e}M{m}_a{ang:F0}_p{pitch:F0}_sw");
            if (Environment.GetEnvironmentVariable("REKKR_SHOT_ALPHA") == "1") ShotFrame($"par{k:D2}_swsoft", video.Texture);
            RekkrSettings.Remaster = true;
            yield return null; yield return null; yield return null;
            ShotFrame($"par{k:D2}_E{e}M{m}_a{ang:F0}_p{pitch:F0}_gpu");
            if (Environment.GetEnvironmentVariable("REKKR_SHOT_ALPHA") == "1")
            {
                ShotFrame($"par{k:D2}_comp", video.FrameTexture);
                ShotFrame($"par{k:D2}_soft", video.Texture);
            }
            Log($"parity {k} E{e}M{m} at=({mo.X.ToFloat():F0},{mo.Y.ToFloat():F0}) angle={ang} pitch={pitch} tris={gpu?.Triangles} things={gpu?.ThingCount} calls={gpu?.DrawCalls} gpu_cpu_ms={gpu?.LastCpuMs:F2}");
            freezeWorld = false;
            k++;
        }
        RekkrSettings.Remaster = false;
    }


    /// <summary>dev6 scenario 6 — Remaster tour (Test Lab): title demo in Remaster, E1M1..E4M1 played in
    /// Remaster with fps per map, then the same E1M1 run in software (Masterpiece) vs Remaster.</summary>
    private IEnumerator Scenario6()
    {
        HudMode = 1;
        // weak GPU: Remaster is hidden, so tour the maps at the automatic preset with the original renderer
        var allowed = RekkrSettings.RemasterAllowed;
        ApplyPreset(allowed ? 3 : ManagedDoom.UnityPort.DeviceClass.AutoPreset(SystemInfo.processorCount, false));
        RekkrSettings.Remaster = allowed;
        Log($"remaster tour preset={RekkrSettings.GfxPreset} remaster={RekkrSettings.Remaster} allowed={allowed}");
        // title demo (DEMO1) drawn by the GPU
        yield return Wait(9F); ShotFrame("rm_demo_a"); Shot("rm_demo_a_screen");
        yield return Wait(6F); ShotFrame("rm_demo_b");
        yield return TapSeq(Ctl.Ok, 1.0F);
        yield return TapSeq(Ctl.Back, 0.8F);
        for (var ep = 1; ep <= 4; ep++)
        {
            Doom.NewGame(GameSkill.Easy, ep, 1);
            yield return Wait(1.5F);
            var ft = new List<float>(6000);
            var evs = new (float, Action, string)[] { (5F, () => { ShotFrame($"rm_e{ep}_a"); if (Environment.GetEnvironmentVariable("REKKR_SHOT_ALPHA") == "1") { ShotFrame($"rm_e{ep}_a_comp", video.FrameTexture); ShotFrame($"rm_e{ep}_a_soft", video.Texture); } }, $"rm_e{ep}_a_screen"), (11F, () => input.AutoPitch = 30F, null), (12.2F, () => { input.AutoPitch = 0; ShotFrame($"rm_e{ep}_up"); }, null), (13F, () => input.CenterView(), null), (19F, () => ShotFrame($"rm_e{ep}_b"), null) };
            var play = Play(24F, evs);
            while (play.MoveNext()) { yield return play.Current; ft.Add(Time.unscaledDeltaTime); }
            float sum = 0; foreach (var f in ft) sum += f;
            ft.Sort();
            Log($"{(allowed ? "remaster" : "software")} E{ep}M1 avg_fps={ft.Count / Mathf.Max(0.001F, sum):F1} p99_frame_ms={ft[(int)(ft.Count * 0.99F)] * 1000:F1} lines={video.Lines} tris={gpu?.Triangles} things={gpu?.ThingCount} calls={gpu?.DrawCalls} gpu_cpu_ms={gpu?.LastCpuMs:F2}");
        }
        // weak GPU: software at the auto preset, then Balanced and Classic (picks the weak-device default)
        var autoP = RekkrSettings.GfxPreset;
        var runs = allowed ? new[] { (false, 3), (true, 3) } : new[] { (false, autoP), (false, 1), (false, 0) };
        foreach (var (rem, preset) in runs)
        {
            if (preset != RekkrSettings.GfxPreset) ApplyPreset(preset);
            RekkrSettings.Remaster = rem;
            Doom.NewGame(GameSkill.Easy, 1, 1);
            yield return Play(2F, null);
            var ft = new List<float>(4000);
            var play = Play(15F, null);
            while (play.MoveNext()) { yield return play.Current; ft.Add(Time.unscaledDeltaTime); }
            float sum = 0; foreach (var f in ft) sum += f;
            ft.Sort();
            Log($"compare renderer={(rem ? "remaster" : "software")} preset={RekkrSettings.GfxPreset} avg_fps={ft.Count / Mathf.Max(0.001F, sum):F1} p99_frame_ms={ft[(int)(ft.Count * 0.99F)] * 1000:F1} lines={video.Lines}");
        }
        HudMode = 0;
    }

    /// <summary>Test only: no tics run (the world holds still for A/B captures).</summary>
    private bool freezeWorld;
}
