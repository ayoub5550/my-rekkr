// my-rekkr dev3 stage 5 — resolution levels + dynamic resolution.
// The frame height is 400/600/800/1000 lines (integer 2D patch scale). With dynamic resolution the
// app steps one level down when the CPU render time stays above 85 % of the frame budget for 1 s
// (or frames keep missing the refresh), and one level up after 4 s below 55 %, never above the
// user's maximum. Switches happen only in a level, never during a wipe or with a menu open.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using ManagedDoom;
using ManagedDoom.UnityPort;
using UnityEngine;

public sealed partial class RekkrApp
{
    private float dynEma = -1, dynOver, dynUnder, dynThermalTimer;
    private int dynSwitches;

    /// <summary>Lines to start with: the user's maximum (env REKKR_LINES overrides on desktop).</summary>
    private static int StartLines()
    {
        var env = Environment.GetEnvironmentVariable("REKKR_LINES");
        if (!string.IsNullOrEmpty(env) && int.TryParse(env, out var l)) return Mathf.Clamp(l / 200 * 200, 400, 1000);
        return RekkrSettings.Resolution;
    }

    public int DynamicSwitches => dynSwitches;

    private void SetLines(int lines, string why)
    {
        if (video == null || lines == video.Lines) return;
        var from = video.Lines;
        if (video.SetFrame(WideWidth(lines), lines))
        {
            Doom?.ResetWipe();
            dynSwitches++;
            dynEma = -1; dynOver = dynUnder = 0;
            Debug.Log($"[REKKR] dynres {from}->{lines} ({why}) frame={video.FrameWidth}x{video.FrameHeight}");
        }
    }

    private void UpdateDynamicResolution()
    {
        if (video == null) return;
        var max = StartLines();
        if (!RekkrSettings.DynamicRes)
        {
            if (video.Lines != max && (!InLevel || !Doom.Wiping)) SetLines(max, "fixed");
            return;
        }
        if (video.Lines > max) { SetLines(max, "max lowered"); return; }
        if (!InLevel || Doom.Wiping || Doom.Menu.Active || settingsOpen || input.EditMode) return;

        var dt = Time.unscaledDeltaTime;
        var ms = video.LastRenderMs + video.LastUploadMs;
        dynEma = dynEma < 0 ? ms : dynEma * 0.92F + ms * 0.08F;
        var budget = 1000F / Mathf.Max(30, DisplayRate.Target);
        var missed = dt * 1000F > budget * 1.6F;          // GPU- or system-bound frames
        if (dynEma > 0.85F * budget || missed) { dynOver += dt; dynUnder = 0; }
        else if (dynEma < 0.55F * budget) { dynUnder += dt; dynOver = 0; }
        else { dynOver = 0; dynUnder = 0; }

        dynThermalTimer += dt;
        if (dynThermalTimer > 5F)
        {
            dynThermalTimer = 0;
            if (ThermalStatus() >= 3 && video.Lines > 400) { SetLines(video.Lines - 200, "thermal"); return; }
        }
        if (dynOver > 1F && video.Lines > 400) SetLines(video.Lines - 200, $"render {dynEma:F1}ms > {0.85F * budget:F1}");
        else if (dynUnder > 4F && video.Lines < max) SetLines(video.Lines + 200, $"render {dynEma:F1}ms < {0.55F * budget:F1}");
    }
}
