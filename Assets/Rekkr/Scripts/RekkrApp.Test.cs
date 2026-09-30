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
        else if (testScenario == 3) yield return Scenario3();
        else if (testScenario == 4) yield return Scenario4();
        else if (testScenario == 5) yield return Scenario5();
        else if (testScenario == 6) yield return Scenario6();
        else if (testScenario == 7) yield return Scenario7();
        else if (testScenario == 8) yield return Scenario8();   // dev7 settings coverage
        else if (testScenario == 9) yield return Scenario9();   // dev7 save backup round trip
        else if (testScenario == 10) yield return Scenario10(); // dev7 Remaster: 3D weapon, light shadows, spectre, door/lift
        else if (testScenario == 11) yield return Scenario11(); // dev7 all 36 maps in Remaster, every effect on
        else yield return Scenario1();
        FinishTestLoop();
    }

    private IEnumerator Scenario1()
    {
        SetArabic(false);
        HudMode = 0;
        yield return Wait(2.5F); Shot("00_titlepic"); yield return null;                // centred 4:3 title (side-fill)
        yield return Wait(6.5F); Shot("01_title"); yield return null;
        yield return TapSeq(Ctl.Ok, 1.0F); Shot("02_menu"); yield return null;           // tap to play -> main menu
        yield return TapSeq(Ctl.Settings, 1.4F); Shot("03_settings_controls"); yield return null;
        controlsPage = 1; yield return Wait(1.4F); Shot("03b_settings_aim_jump"); yield return null;
        controlsPage = 0;
        settingsTab = 1; yield return Wait(1.4F); Shot("04_settings_motion"); yield return null;
        settingsTab = 2; yield return Wait(1.6F); Shot("05_settings_display"); yield return null;
        settingsTab = 3; gfxPage = 0; yield return Wait(1.4F); Shot("05b_settings_graphics"); yield return null;
        gfxPage = 1; yield return Wait(1.4F); Shot("05c_settings_effects"); yield return null;
        gfxPage = 2; yield return Wait(1.4F); Shot("05d_settings_world"); yield return null;
        gfxPage = 3; RekkrSettings.Remaster = true; yield return Wait(1.4F); Shot("05e_settings_renderer"); yield return null;
        RekkrSettings.Remaster = false;
        gfxPage = 4; yield return Wait(1.4F); Shot("05f_settings_world2"); yield return null;
        gfxPage = 0;
        settingsTab = 0; yield return Wait(0.6F);
        OpenEditor(); yield return Wait(1.4F); Shot("06_editor"); yield return null;
        input.ScaleSelected(0.3F); yield return Wait(0.8F);
        input.EditSelected = Ctl.QuickSave;
        RekkrSettings.Layout[Ctl.QuickSave] = (new Vector2(0.5F, 0.55F), 1.2F); yield return Wait(1.0F); Shot("07_editor_moved"); yield return null;
        RekkrSettings.Layout.Clear(); yield return Wait(0.6F);
        input.EditMode = false; yield return Wait(0.5F);
        settingsOpen = false;
        yield return Wait(0.6F);
        // dev4: tap the menu lines directly (NEW GAME, then episode 1) like a finger would.
        var tapped = MenuTapAt(MenuItemScreenPos(0), true); yield return Wait(0.9F);
        Log("menu tap new game=" + tapped + " current=" + Doom.Menu.Current?.GetType().Name);
        tapped = MenuTapAt(MenuItemScreenPos(0), true); yield return Wait(0.9F); Shot("08_episode"); yield return null;
        Log("menu tap episode=" + tapped);
        yield return TapSeq(Ctl.Down, 0.5F);
        yield return TapSeq(Ctl.Up, 0.7F);
        yield return TapSeq(Ctl.Ok, 1.2F);                              // skill (default)

        var events = new (float t, Action a, string name)[]
        {
            (6F, () => Log("autosave exists=" + File.Exists(SavePath(AutoSlot))), null),
            // dev4: look up / down, jump, crosshair styles
            (7F, () => input.AutoPitch = 45F, null),
            (8.7F, () => { input.AutoPitch = 0; Log("look up pitch=" + input.PitchInt); }, "09_look_up"),
            (10F, () => input.AutoPitch = -60F, null),
            (12.5F, () => { input.AutoPitch = 0; Log("look down pitch=" + input.PitchInt); }, "09b_look_down"),
            (13.5F, () => input.CenterView(), null),
            (18F, () => input.AutoJump = true, "10b_jump"),
            (21F, () => { input.AutoJump = false; Log("jumps=" + testJumps); }, null),
            (40F, () => RekkrSettings.Crosshair = 1, "13b_crosshair_red"),
            (43F, () => RekkrSettings.Crosshair = 3, "13c_crosshair_dot"),
            (46F, () => RekkrSettings.Crosshair = 0, null),
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
        yield return TapSeq(Ctl.Menu, 1.5F); Shot("20_ingame_menu"); yield return null;
        yield return TapSeq(Ctl.Back, 1.0F);
    }

    private IEnumerator Scenario2()
    {
        SetArabic(true);
        HudMode = 1;
        yield return Wait(8F); Shot("01_title_ar"); yield return null;
        if (canContinue)
        {
            yield return TapSeq(Ctl.Continue, 1.0F);
            yield return Play(10F, null);
            Log("continue state=" + Doom.State + " level=" + InLevel);
            Shot("02_continued"); yield return null;
            yield return TapSeq(Ctl.Menu, 1.0F);
        }
        else
        {
            yield return TapSeq(Ctl.Ok, 1.0F);
        }
        yield return TapSeq(Ctl.Settings, 1.5F); Shot("03_settings_ar"); yield return null;
        settingsTab = 1; yield return Wait(1.3F);
        settingsTab = 2; yield return Wait(1.5F); Shot("04_settings_ar_display"); yield return null;
        settingsTab = 3; gfxPage = 0; yield return Wait(1.4F); Shot("04b_settings_ar_graphics"); yield return null;
        gfxPage = 1; yield return Wait(1.4F); Shot("04c_settings_ar_effects"); yield return null;
        gfxPage = 2; yield return Wait(1.4F); Shot("04d_settings_ar_world"); yield return null;
        gfxPage = 3; RekkrSettings.Remaster = true; yield return Wait(1.4F); Shot("04e_settings_ar_renderer"); yield return null;
        RekkrSettings.Remaster = false;
        gfxPage = 4; yield return Wait(1.4F); Shot("04f_settings_ar_world2"); yield return null;
        gfxPage = 0;
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

    // ------------------------------------------------------------ dev3 scenario 3: graphics benchmark
    // Fixed configs, 20 s each, in E1M1 and E1M7 (REKKR's biggest map), autopilot wandering + turning.
    private static readonly (string name, int preset, int lines, bool light, bool post)[] BenchConfigs =
    {
        ("classic400", 0, 400, false, false),
        ("enh600", 2, 600, true, true),
        ("enh800", 2, 800, true, true),
        ("enh1000", 2, 1000, true, true),
        ("enh800_nolight", 2, 800, false, true),
        ("enh800_nopost", 2, 800, true, false),
        ("enh_auto", 2, 800, true, true),   // dynamic resolution on
    };

    private IEnumerator Scenario3()
    {
        HudMode = 0;
        yield return Wait(4F);
        yield return TapSeq(Ctl.Ok, 1.0F);
        yield return TapSeq(Ctl.Back, 0.8F);
        foreach (var map in new[] { 1, 7 })
        {
            Doom.NewGame(GameSkill.Easy, 1, map);
            yield return Wait(2F);
            foreach (var cfg in BenchConfigs)
            {
                ApplyPreset(cfg.preset);
                RekkrSettings.SmoothLighting = cfg.light;
                if (!cfg.post) { RekkrSettings.Bloom = 0; RekkrSettings.Vignette = 0; RekkrSettings.ColorGrade = 0; RekkrSettings.Sharpen = false; RekkrSettings.SideFill = false; }
                RekkrSettings.Resolution = cfg.lines;
                RekkrSettings.DynamicRes = cfg.name == "enh_auto";
                SetLines(cfg.lines, "bench");
                yield return Play(3F, null);   // warm-up (renderer creation, caches)
                var ft = new List<float>(4000); var rt = new List<float>(4000);
                var end = Time.unscaledTime + 20F; var switches = dynSwitches;
                var thermalStart = ThermalStatus();
                var play = Play(20F, null);
                while (Time.unscaledTime < end && play.MoveNext())
                {
                    yield return play.Current;
                    ft.Add(Time.unscaledDeltaTime); rt.Add(video.LastRenderMs + video.LastUploadMs);
                }
                ft.Sort();
                float sum = 0; foreach (var f in ft) sum += f;
                var (ra, rp) = Stats(rt);
                Log($"bench map=E1M{map} cfg={cfg.name} frame={video.FrameWidth}x{video.FrameHeight} avg_fps={ft.Count / Mathf.Max(0.001F, sum):F1} p50_frame_ms={ft[ft.Count / 2] * 1000:F1} p99_frame_ms={ft[(int)(ft.Count * 0.99F)] * 1000:F1} render_ms_avg={ra:F2} render_ms_p99={rp:F2} dynres_switches={dynSwitches - switches} thermal={thermalStart}->{ThermalStatus()}");
                Shot($"bench_E1M{map}_{cfg.name}"); yield return null;
            }
        }
        ApplyPreset(2);
    }

    // ------------------------------------------------------------ dev5 scenario 5: Masterpiece tour
    // Masterpiece preset on each episode's first map (sky, water, weather, lights, particles), forced
    // rain/snow, weapon wheel, automap touch, and an Enhanced vs Masterpiece fps comparison.
    private IEnumerator Scenario5()
    {
        HudMode = 1;
        yield return Wait(4F);
        yield return TapSeq(Ctl.Ok, 1.0F);
        yield return TapSeq(Ctl.Back, 0.8F);
        ApplyPreset(3);
        Log($"preset={RekkrSettings.GfxPreset} worldfx={WorldFx.Active}");
        for (var ep = 1; ep <= 4; ep++)
        {
            Doom.NewGame(GameSkill.Easy, ep, 1);
            yield return Wait(1.5F);
            var ft = new List<float>(6000);
            var evs = new (float, Action, string)[] { (5F, null, $"mp_e{ep}_a"), (12F, () => input.AutoPitch = 30F, null), (13.2F, () => input.AutoPitch = 0, $"mp_e{ep}_up"), (14F, () => input.CenterView(), null), (20F, null, $"mp_e{ep}_b") };
            var play = Play(26F, evs);
            while (play.MoveNext()) { yield return play.Current; ft.Add(Time.unscaledDeltaTime); }
            float sum = 0; foreach (var f in ft) sum += f;
            Log($"masterpiece E{ep}M1 avg_fps={ft.Count / Mathf.Max(0.001F, sum):F1} lines={video.Lines} lights={worldFx?.LightsLastFrame} particles={worldFx?.ParticlesLastFrame} outdoor={ManagedDoom.Video.ThreeDRenderer.LastView.SkyCeiling}");
        }
        // Forced weather on E1M1 (outdoors at the start).
        Doom.NewGame(GameSkill.Easy, 1, 1);
        yield return Wait(1.5F);
        RekkrSettings.Weather = 1;
        yield return Play(7F, new (float, Action, string)[] { (5F, null, "mp_rain") });
        RekkrSettings.Weather = 2;
        yield return Play(7F, new (float, Action, string)[] { (5F, null, "mp_snow") });
        RekkrSettings.Weather = 0;
        // Weapon wheel (test only: give the weapons).
        var pl = Doom.Game.World.ConsolePlayer;
        for (var i = 0; i < pl.WeaponOwned.Length; i++) pl.WeaponOwned[i] = true;
        for (var i = 0; i < pl.Ammo.Length; i++) pl.Ammo[i] = pl.MaxAmmo[i];
        input.TestWheel(true, 3); yield return Wait(0.8F); Shot("wheel_open"); yield return null;
        var want = input.WheelItems.Count > 3 ? input.WheelItems[3] : WeaponType.NoChange;
        input.TestWheel(false, -1);
        yield return Play(2.5F, null);
        Log($"wheel items={input.WheelItems.Count} wanted={want} ready={pl.ReadyWeapon} pending={pl.PendingWeapon}");
        // Automap touch: zoom in, pan, follow.
        input.Tap(Ctl.Map); yield return Wait(0.6F);
        var z0 = Doom.Game.World.AutoMap.Zoom.ToFloat();
        for (var i = 0; i < 6; i++) { AutomapZoom(1.2F); yield return null; }
        AutomapPan(new Vector2(-Screen.width * 0.2F, 0)); yield return Wait(0.6F); Shot("automap_touch"); yield return null;
        Log($"automap zoom {z0:F2}->{Doom.Game.World.AutoMap.Zoom.ToFloat():F2} follow={Doom.Game.World.AutoMap.Follow}");
        AutomapFollow(); yield return Wait(0.3F);
        Log($"automap follow={Doom.Game.World.AutoMap.Follow}");
        input.Tap(Ctl.Map); yield return Wait(0.5F);
        // Enhanced vs Masterpiece on E1M1.
        foreach (var preset in new[] { 2, 3 })
        {
            Doom.NewGame(GameSkill.Easy, 1, 1);
            ApplyPreset(preset);
            yield return Play(2F, null);
            var ft = new List<float>(4000);
            var play = Play(15F, null);
            while (play.MoveNext()) { yield return play.Current; ft.Add(Time.unscaledDeltaTime); }
            float sum = 0; foreach (var f in ft) sum += f;
            ft.Sort();
            Log($"compare preset={preset} avg_fps={ft.Count / Mathf.Max(0.001F, sum):F1} p99_frame_ms={ft[(int)(ft.Count * 0.99F)] * 1000:F1} lines={video.Lines}");
        }
        HudMode = 0;
    }

    // ------------------------------------------------------------ dev3 scenario 4: long soak
    // 20 minutes of play, a new map every 2 minutes, one log line per minute (fps, lines, memory, heat).
    private IEnumerator Scenario4()
    {
        HudMode = 0;
        yield return Wait(4F);
        yield return TapSeq(Ctl.Ok, 1.0F);
        yield return TapSeq(Ctl.Back, 0.8F);
        var maps = new[] { (1, 1), (1, 7), (2, 6), (3, 6), (4, 1), (2, 3), (3, 4), (4, 9), (1, 4), (4, 7) };
        var minute = 0;
        foreach (var (e, m) in maps)
        {
            Doom.NewGame(GameSkill.Easy, e, m);
            yield return Wait(1.5F);
            for (var half = 0; half < 2; half++)
            {
                var ft = new List<float>(8000);
                var play = Play(60F, null);
                while (play.MoveNext()) { yield return play.Current; ft.Add(Time.unscaledDeltaTime); }
                float sum = 0; foreach (var f in ft) sum += f;
                ft.Sort();
                minute++;
                Log($"soak minute={minute} map=E{e}M{m} lines={video.Lines} avg_fps={ft.Count / Mathf.Max(0.001F, sum):F1} p99_frame_ms={(ft.Count > 0 ? ft[(int)(ft.Count * 0.99F)] * 1000 : 0):F1} mem_mb={GC.GetTotalMemory(false) / 1048576} unity_mb={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} gc0={GC.CollectionCount(0) - testGcStart} thermal={ThermalStatus()} health={Doom.Game?.World?.ConsolePlayer?.Health}");
                if (Doom.Game?.World?.ConsolePlayer?.Health <= 0) { Doom.NewGame(GameSkill.Easy, e, m); yield return Wait(1.5F); }
            }
            Shot($"soak_E{e}M{m}"); yield return null;
        }
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
                if (shotName != null) { yield return Wait(0.8F); Shot(shotName); yield return null; }
            }
            yield return null;
        }
        input.AutoStick = Vector2.zero; input.AutoTurn = 0; input.AutoFire = false; input.AutoUse = false;
    }

    private int testGcStart = -1;
    private int testJumps, lastJumpTics;

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
        var summary = $"[REKKR-TEST] scenario={testScenario} frames={frameTimes.Count} avg_fps={avgFps:F1} p50_frame_ms={p50:F1} p99_frame_ms={p99:F1} render_ms_avg={renderAvg:F2} render_ms_p99={renderP99:F2} upload_ms_avg={uploadAvg:F2} upload_ms_p99={uploadP99:F2} gc0={gc0} jumps={testJumps} {LookSummary()} thermal={ThermalStatus()} target={DisplayRate.Target} screen={Screen.width}x{Screen.height} frame={video.FrameWidth}x{video.FrameHeight} lines_max={StartLines()} dynres={RekkrSettings.DynamicRes} dynres_switches={dynSwitches} threads={video.RenderThreads} smooth_light={RekkrSettings.SmoothLighting} post={PostFx.Active} bloom={RekkrSettings.Bloom} device={SystemInfo.deviceModel} gpu={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType}";
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
