// my-rekkr — Firebase Test Lab Game Loop autopilot (also local capture via REKKR_SHOTS).
// Scenario 1: showcase — title, settings tabs, layout editor, New Game via the real menu taps,
//             quick save/load, HUD modes, 4:3 <-> widescreen, weapon select by tapping ARMS.
// Scenario 2: Arabic UI + CONTINUE, then all four episodes (E1M1..E4M1) with the fullscreen HUD.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ManagedDoom;
using ManagedDoom.UnityPort;
using UnityEngine;

public sealed partial class RekkrApp
{
    private void DetectTestLoop()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
            {
                var action = intent.Call<string>("getAction");
                if (action == "com.google.intent.action.TEST_LOOP")
                {
                    testLoop = true;
                    testScenario = intent.Call<int>("getIntExtra", "scenario", 1);
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("Intent check failed: " + e.Message); }
#endif
        var env = Environment.GetEnvironmentVariable("REKKR_SCENARIO");
        if (!string.IsNullOrEmpty(env) && int.TryParse(env, out var sc)) testScenario = sc;
    }

    private void Shot(string name)
    {
        if (string.IsNullOrEmpty(shotDir)) return;
        ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, $"s{testScenario}_{name}.png"));
    }

    private IEnumerator Wait(float s) { var t = Time.unscaledTime + s; while (Time.unscaledTime < t) yield return null; }

    private IEnumerator TapSeq(Ctl c, float after = 0.55F) { input.Tap(c); yield return Wait(after); }

    private static void Log(string s) => Debug.Log("[REKKR-TEST] " + s);

    private IEnumerator Autopilot()
    {
        testGcStart = GC.CollectionCount(0);
        Log($"autopilot scenario {testScenario} frame={video.FrameWidth}x{video.FrameHeight} rateTarget={DisplayRate.Target} rates={string.Join("/", DisplayRate.Rates)} gyro={SystemInfo.supportsGyroscope}");
        if (testScenario == 2) yield return Scenario2();
        else yield return Scenario1();
        FinishTestLoop();
    }

    private IEnumerator Scenario1()
    {
        SetArabic(false);
        HudMode = 0;
        yield return Wait(2.5F); Shot("00_titlepic");                // centred 4:3 title (side-fill)
        yield return Wait(6.5F); Shot("01_title");
        yield return TapSeq(Ctl.Ok, 1.0F); Shot("02_menu");           // tap to play -> main menu
        yield return TapSeq(Ctl.Settings, 1.4F); Shot("03_settings_controls");
        settingsTab = 1; yield return Wait(1.4F); Shot("04_settings_motion");
        settingsTab = 2; yield return Wait(1.6F); Shot("05_settings_display");
        settingsTab = 0; yield return Wait(0.6F);
        OpenEditor(); yield return Wait(1.4F); Shot("06_editor");
        input.ScaleSelected(0.3F); yield return Wait(0.8F);
        input.EditSelected = Ctl.QuickSave;
        RekkrSettings.Layout[Ctl.QuickSave] = (new Vector2(0.5F, 0.55F), 1.2F); yield return Wait(1.0F); Shot("07_editor_moved");
        RekkrSettings.Layout.Clear(); yield return Wait(0.6F);
        input.EditMode = false; yield return Wait(0.5F);
        settingsOpen = false;
        yield return Wait(0.6F);
        yield return TapSeq(Ctl.Ok, 0.9F);                              // NEW GAME
        yield return TapSeq(Ctl.Ok, 0.9F); Shot("08_episode");          // episode 1
        yield return TapSeq(Ctl.Down, 0.5F);
        yield return TapSeq(Ctl.Up, 0.7F);
        yield return TapSeq(Ctl.Ok, 1.2F);                              // skill (default)

        var events = new (float t, Action a, string name)[]
        {
            (6F, () => Log("autosave exists=" + File.Exists(SavePath(AutoSlot))), null),
            (14F, () => { input.Tap(Ctl.QuickSave); Log("quicksave exists=" + File.Exists(SavePath(QuickSlot))); }, "10_quicksave"),
            (24F, () => HudMode = 1, "11_hud_fullscreen"),
            (32F, () => input.TapSlot(0), null),
            (38F, () => HudMode = 2, "12_hud_none"),
            (45F, () => HudMode = 0, "13_hud_bar"),
            (50F, () => input.Tap(Ctl.WeaponNext), null),
            (55F, () => input.Tap(Ctl.Map), "14_automap"),
            (59F, () => input.Tap(Ctl.Map), null),
            (62F, () => { RekkrSettings.Widescreen = false; ApplyWidescreen(); }, "15_classic_4x3"),
            (70F, () => { RekkrSettings.Widescreen = true; ApplyWidescreen(); }, null),
            (76F, () => input.Tap(Ctl.QuickLoad), null),
            (79F, () => Log("after quickload state=" + Doom.State + " level=" + InLevel), "16_quickloaded"),
            (88F, () => input.Tap(Ctl.WeaponPrev), null),
        };
        yield return Play(98F, events);
        Log($"haptic pulses={Haptics.Count}");
        yield return TapSeq(Ctl.Menu, 1.5F); Shot("20_ingame_menu");
        yield return TapSeq(Ctl.Back, 1.0F);
    }

    private IEnumerator Scenario2()
    {
        SetArabic(true);
        HudMode = 1;
        yield return Wait(8F); Shot("01_title_ar");
        if (canContinue)
        {
            yield return TapSeq(Ctl.Continue, 1.0F);
            yield return Play(10F, null);
            Log("continue state=" + Doom.State + " level=" + InLevel);
            Shot("02_continued");
            yield return TapSeq(Ctl.Menu, 1.0F);
        }
        else
        {
            yield return TapSeq(Ctl.Ok, 1.0F);
        }
        yield return TapSeq(Ctl.Settings, 1.5F); Shot("03_settings_ar");
        settingsTab = 1; yield return Wait(1.3F);
        settingsTab = 2; yield return Wait(1.5F); Shot("04_settings_ar_display");
        settingsTab = 0; settingsOpen = false;
        yield return TapSeq(Ctl.Back, 0.8F);

        for (var ep = 1; ep <= 4; ep++)
        {
            Doom.NewGame(GameSkill.Medium, ep, 1);
            yield return Wait(1.5F);
            yield return Play(34F, new (float, Action, string)[] { (8F, null, "ep" + ep) });
            var w = Doom.Game?.World;
            var p = w?.ConsolePlayer;
            Log($"episode {ep}: state={Doom.State} level={InLevel} map=E{Doom.Game?.Options.Episode}M{Doom.Game?.Options.Map} health={p?.Health} kills={p?.KillCount}/{w?.TotalKills}");
        }
        SetArabic(false);
        HudMode = 0;
    }

    /// <summary>Wander, fire and use for <paramref name="seconds"/>, running timed events.</summary>
    private IEnumerator Play(float seconds, (float t, Action a, string shot)[] events)
    {
        var start = Time.unscaledTime;
        var lastPos = Vector2.zero;
        var stuckFor = 0F;
        var turnDir = 1F;
        var phaseT = 0F;
        var next = 0;
        while (Time.unscaledTime - start < seconds)
        {
            var t = Time.unscaledTime - start;
            var world = Doom.Game?.World;
            if (InLevel && world != null && !Doom.Menu.Active)
            {
                var p = world.ConsolePlayer;
                var pos = new Vector2(p.Mobj.X.ToFloat(), p.Mobj.Y.ToFloat());
                stuckFor = (pos - lastPos).magnitude < 1.5F ? stuckFor + Time.unscaledDeltaTime : 0;
                lastPos = pos;
                phaseT -= Time.unscaledDeltaTime;
                if (stuckFor > 0.6F && phaseT <= 0) { phaseT = 0.7F; turnDir = UnityEngine.Random.value < 0.5F ? -1 : 1; input.AutoUse = true; }
                if (phaseT > 0)
                {
                    input.AutoStick = new Vector2(0.3F * turnDir, -0.4F);
                    input.AutoTurn = 2.6F * turnDir;
                }
                else
                {
                    input.AutoUse = false;
                    input.AutoStick = new Vector2(Mathf.Sin(t * 0.7F) * 0.35F, 1F);
                    input.AutoTurn = Mathf.Sin(t * 0.45F) * 0.5F;
                }
                input.AutoFire = (t % 3.0F) < 0.9F;
                if (p.Health <= 0) { input.AutoFire = false; input.AutoUse = (t % 1F) < 0.2F; }
            }
            else
            {
                input.AutoStick = Vector2.zero; input.AutoTurn = 0; input.AutoFire = false;
                if (Doom.State == DoomState.Game && Doom.Game.State != GameState.Level) input.AutoUse = (t % 1F) < 0.3F;
            }

            while (events != null && next < events.Length && t >= events[next].t)
            {
                try { events[next].a?.Invoke(); } catch (Exception e) { Log("event failed: " + e.Message); }
                var shotName = events[next].shot;
                next++;
                if (shotName != null) { yield return Wait(0.8F); Shot(shotName); }
            }
            yield return null;
        }
        input.AutoStick = Vector2.zero; input.AutoTurn = 0; input.AutoFire = false; input.AutoUse = false;
    }

    private int testGcStart = -1;

    // dev3 smooth-look metrics while the autopilot turns: per-frame view-angle step (evenness) and
    // lag = (angle the input asks for) - (angle actually rendered).
    private readonly List<float> lookSteps = new List<float>(20000);
    private readonly List<float> lookLags = new List<float>(20000);
    private Angle lastViewAngle; private bool hasLastView;

    private void TrackViewAngle(float frac)
    {
        if (!InLevel || input.AutoTurn == 0) { hasLastView = false; return; }
        var p = Doom.Game.World.ConsolePlayer;
        if (p.Mobj == null) return;
        var rendered = video.LocalViewTurn.HasValue ? p.Mobj.Angle + video.LocalViewTurn.Value
                                                    : p.GetInterpolatedAngle(Fixed.FromFloat(Mathf.Clamp01(frac)));
        var wanted = p.Mobj.Angle + input.PendingTurn;
        lookLags.Add(Mathf.Abs(DeltaDeg(wanted, rendered)));
        if (hasLastView) lookSteps.Add(Mathf.Abs(DeltaDeg(rendered, lastViewAngle)));
        lastViewAngle = rendered; hasLastView = true;
    }

    private static float DeltaDeg(Angle a, Angle b) => (float)((int)(a.Data - b.Data) * (180.0 / 2147483648.0));

    private string LookSummary()
    {
        if (lookSteps.Count < 10) return "look=n/a";
        float sum = 0, sq = 0; foreach (var v in lookSteps) { sum += v; sq += v * v; }
        var mean = sum / lookSteps.Count; var sd = Mathf.Sqrt(Mathf.Max(0, sq / lookSteps.Count - mean * mean));
        var (lagAvg, lagP99) = Stats(lookLags);
        return $"smooth_look={RekkrSettings.SmoothLook} look_step_deg={mean:F3} look_step_cv={(mean > 0 ? sd / mean : 0):F2} look_lag_deg_avg={lagAvg:F2} look_lag_deg_p99={lagP99:F2}";
    }

    private static (float avg, float p99) Stats(List<float> values)
    {
        if (values.Count == 0) return (0, 0);
        var sorted = new List<float>(values); sorted.Sort();
        float sum = 0; foreach (var v in sorted) sum += v;
        return (sum / sorted.Count, sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99F))]);
    }

    /// <summary>Android PowerManager thermal status (API 29+): 0 none … 6 shutdown; -1 unknown.</summary>
    public static int ThermalStatus()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                if (version.GetStatic<int>("SDK_INT") < 29) return -1;
            }
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var pm = activity.Call<AndroidJavaObject>("getSystemService", "power"))
            {
                return pm.Call<int>("getCurrentThermalStatus");
            }
        }
        catch (Exception) { return -1; }
#else
        return -1;
#endif
    }

    private void FinishTestLoop()
    {
        frameTimes.Sort();
        float sum = 0; foreach (var f in frameTimes) sum += f;
        var n = Math.Max(1, frameTimes.Count);
        var avgFps = n / Math.Max(0.001F, sum);
        var p99 = frameTimes.Count > 0 ? frameTimes[(int)(frameTimes.Count * 0.99F)] * 1000F : 0;
        var p50 = frameTimes.Count > 0 ? frameTimes[frameTimes.Count / 2] * 1000F : 0;
        var (renderAvg, renderP99) = Stats(renderTimes);
        var (uploadAvg, uploadP99) = Stats(uploadTimes);
        var gc0 = GC.CollectionCount(0) - testGcStart;
        var summary = $"[REKKR-TEST] scenario={testScenario} frames={frameTimes.Count} avg_fps={avgFps:F1} p50_frame_ms={p50:F1} p99_frame_ms={p99:F1} render_ms_avg={renderAvg:F2} render_ms_p99={renderP99:F2} upload_ms_avg={uploadAvg:F2} upload_ms_p99={uploadP99:F2} gc0={gc0} {LookSummary()} thermal={ThermalStatus()} target={DisplayRate.Target} screen={Screen.width}x{Screen.height} frame={video.FrameWidth}x{video.FrameHeight} lines_max={StartLines()} dynres={RekkrSettings.DynamicRes} dynres_switches={dynSwitches} threads={video.RenderThreads} smooth_light={RekkrSettings.SmoothLighting} post={PostFx.Active} bloom={RekkrSettings.Bloom} device={SystemInfo.deviceModel} gpu={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType}";
        Debug.Log(summary);
        try
        {
            var outPath = Path.Combine(string.IsNullOrEmpty(shotDir) ? Application.persistentDataPath : shotDir, $"testloop_result_{testScenario}.txt");
            File.WriteAllText(outPath, summary + "\n");
        }
        catch (Exception) { }
        RekkrSettings.Save();
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        {
            activity.Call("finish");
        }
#else
        Application.Quit();
#endif
    }
}
