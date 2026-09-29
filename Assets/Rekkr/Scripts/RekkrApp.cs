// my-rekkr — REKKR for Android. Unity host for the Managed Doom engine:
// boots the game, runs vanilla 35 Hz tics, draws the original software-rendered frame
// in 4:3 with professional touch controls, and drives the Firebase Game Loop test.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ManagedDoom;
using ManagedDoom.UnityPort;
using UnityEngine;
using UnityEngine.Networking;

public sealed class RekkrApp : MonoBehaviour
{
    public const string Version = "0.1.0";

    private static readonly string[] dataFiles = { "rekkr.wad", "rekkr-compat.wad", "TimGM6mb.sf2" };

    public Doom Doom { get; private set; }
    private Config config;
    private GameContent content;
    private UnityVideo video;
    private UnitySound sound;
    private UnityMusic music;
    private TouchInput input;

    private Material screenMat;
    private Texture2D texBtn, texBtnPressed, texStickBase, texStickKnob, texWhite;
    private readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

    private double ticAccum;
    private const double TicTime = 1.0 / 35.0;
    private string fatal;
    private string status = "Loading REKKR…";
    private Rect gameRect;

    // Touch settings (PlayerPrefs).
    public int LookSensitivity = 5;
    private int controlsScale = 100;
    private int controlsOpacity = 75;
    private bool leftHanded;
    private bool settingsOpen;

    // Test loop / capture.
    private bool testLoop;
    private int testScenario = 1;
    private readonly List<float> frameTimes = new List<float>(20000);
    private string shotDir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindObjectOfType<RekkrApp>() != null) return;
        var go = new GameObject("REKKR");
        DontDestroyOnLoad(go);
        go.AddComponent<RekkrApp>();
    }

    private void Awake()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Input.multiTouchEnabled = true;

        var cam = new GameObject("Camera").AddComponent<Camera>();
        cam.transform.SetParent(transform);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 0;
        cam.orthographic = true;

        LookSensitivity = PlayerPrefs.GetInt("look_sens", 5);
        controlsScale = PlayerPrefs.GetInt("ctl_scale", 100);
        controlsOpacity = PlayerPrefs.GetInt("ctl_alpha", 75);
        leftHanded = PlayerPrefs.GetInt("left_handed", 0) == 1;

        screenMat = new Material(Resources.Load<Shader>("Rekkr/RekkrScreen"));
        texBtn = Resources.Load<Texture2D>("Rekkr/UI/btn");
        texBtnPressed = Resources.Load<Texture2D>("Rekkr/UI/btn_pressed");
        texStickBase = Resources.Load<Texture2D>("Rekkr/UI/stick_base");
        texStickKnob = Resources.Load<Texture2D>("Rekkr/UI/stick_knob");
        texWhite = Texture2D.whiteTexture;
        foreach (var n in new[] { "fire", "use", "wnext", "wprev", "map", "menu", "run", "up", "down", "left", "right", "ok", "back", "settings" })
        {
            icons[n] = Resources.Load<Texture2D>("Rekkr/UI/ic_" + n);
        }

        DetectTestLoop();
        shotDir = Environment.GetEnvironmentVariable("REKKR_SHOTS");
        if (!string.IsNullOrEmpty(shotDir)) { testLoop = true; Directory.CreateDirectory(shotDir); }
    }

    private IEnumerator Start()
    {
        var dataDir = Path.Combine(Application.persistentDataPath, "data");
        Directory.CreateDirectory(dataDir);
        foreach (var f in dataFiles)
        {
            status = "Preparing " + f + "…";
            yield return CopyStreamingAsset(f, Path.Combine(dataDir, f));
            if (fatal != null) yield break;
        }

        try
        {
            ConfigUtilities.DataDirectory = Application.persistentDataPath;
            var cfgPath = ConfigUtilities.GetConfigPath();
            var firstRun = !File.Exists(cfgPath);
            config = new Config(cfgPath);
            if (firstRun)
            {
                // Mobile defaults: sharp 640x400 renderer, always run, full status bar view.
                config.video_highresolution = true;
                config.game_alwaysrun = true;
                config.video_gamescreensize = 7;
                config.audio_randompitch = true;
            }
            config.video_highresolution = true;

            var args = new CommandLineArgs(new[]
            {
                "-iwad", Path.Combine(dataDir, "rekkr.wad"),
                "-file", Path.Combine(dataDir, "rekkr-compat.wad"),
            });
            content = new GameContent(args);
            video = new UnityVideo(config, content);
            sound = new UnitySound(config, content, gameObject);
            try
            {
                music = new UnityMusic(config, content, gameObject, Path.Combine(dataDir, "TimGM6mb.sf2"));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Music disabled: " + e.Message);
                music = null;
            }
            input = new TouchInput(config, this);
            Doom = new Doom(args, config, content, video, sound, music, input);
            status = null;
            Debug.Log("[REKKR] started " + Version + " mode=" + content.Wad.GameMode + " testLoop=" + testLoop);
        }
        catch (Exception e)
        {
            fatal = e.ToString();
            Debug.LogError(fatal);
            yield break;
        }

        if (testLoop) StartCoroutine(Autopilot());
    }

    private IEnumerator CopyStreamingAsset(string name, string dest)
    {
        var src = Path.Combine(Application.streamingAssetsPath, name);
        if (src.Contains("://"))
        {
            using (var req = UnityWebRequest.Get(src))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { fatal = "Cannot read " + name + ": " + req.error; yield break; }
                var bytes = req.downloadHandler.data;
                if (!File.Exists(dest) || new FileInfo(dest).Length != bytes.Length) File.WriteAllBytes(dest, bytes);
            }
        }
        else
        {
            if (!File.Exists(src)) { fatal = "Missing " + src; yield break; }
            if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(src).Length) File.Copy(src, dest, true);
        }
    }

    // ------------------------------------------------------------------ main loop

    private void Update()
    {
        if (Doom == null || fatal != null) return;

        ComputeLayout();
        input.Poll(leftHanded, settingsOpen);

        try
        {
            ticAccum += Math.Min(Time.unscaledDeltaTime, 0.25);
            var tics = 0;
            while (ticAccum >= TicTime && tics < 6)
            {
                ticAccum -= TicTime;
                tics++;
                if (Doom.Update() == UpdateResult.Completed)
                {
                    Quit();
                    return;
                }
            }
            if (tics == 6) ticAccum = 0;
            var frac = (float)(ticAccum / TicTime);
            video.Render(Doom, Fixed.FromFloat(Mathf.Clamp01(frac)));
        }
        catch (Exception e)
        {
            fatal = e.ToString();
            Debug.LogError(fatal);
        }

        if (testLoop) frameTimes.Add(Time.unscaledDeltaTime);
    }

    private void ComputeLayout()
    {
        float W = Screen.width, H = Screen.height;
        // Original Doom: 320x200 shown on a 4:3 display (non-square pixels).
        float gh = H, gw = H * 4F / 3F;
        if (gw > W) { gw = W; gh = W * 3F / 4F; }
        gameRect = new Rect((W - gw) / 2, (H - gh) / 2, gw, gh);
        input.Layout(gameRect, controlsScale / 100F, leftHanded);
    }

    private void Quit()
    {
        SaveSettings();
        if (testLoop) { FinishTestLoop(); return; }
        Application.Quit();
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused || Doom == null) return;
        SaveSettings();
        // Leaving the app mid-level opens the menu, which pauses single-player like vanilla.
        if (!testLoop && Doom.State == DoomState.Game && !Doom.Menu.Active)
        {
            Doom.PostEvent(new DoomEvent(ManagedDoom.EventType.KeyDown, ManagedDoom.DoomKey.Escape));
            Doom.PostEvent(new DoomEvent(ManagedDoom.EventType.KeyUp, ManagedDoom.DoomKey.Escape));
        }
    }

    private void OnApplicationQuit() => SaveSettings();

    private void SaveSettings()
    {
        try { config?.Save(ConfigUtilities.GetConfigPath()); } catch (Exception e) { Debug.LogWarning(e.Message); }
        PlayerPrefs.SetInt("look_sens", LookSensitivity);
        PlayerPrefs.SetInt("ctl_scale", controlsScale);
        PlayerPrefs.SetInt("ctl_alpha", controlsOpacity);
        PlayerPrefs.SetInt("left_handed", leftHanded ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void ToggleSettings()
    {
        settingsOpen = !settingsOpen;
        if (!settingsOpen) SaveSettings();
    }

    // ------------------------------------------------------------------ drawing

    private GUIStyle titleStyle, rowStyle, smallStyle, btnStyle;

    private void EnsureStyles()
    {
        if (titleStyle != null) return;
        var font = Resources.Load<Font>("Rekkr/UI/Lato-Black");
        var gold = new Color(0.87F, 0.70F, 0.36F);
        titleStyle = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter, normal = { textColor = gold } };
        rowStyle = new GUIStyle { font = font, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.95F, 0.92F, 0.85F) } };
        smallStyle = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1, 1, 1, 0.85F) } };
        btnStyle = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
    }

    private void OnGUI()
    {
        if (Event.current.type != UnityEngine.EventType.Repaint && !settingsOpen) return;
        EnsureStyles();
        float H = Screen.height;
        titleStyle.fontSize = Mathf.RoundToInt(H * 0.055F);
        rowStyle.fontSize = Mathf.RoundToInt(H * 0.042F);
        smallStyle.fontSize = Mathf.RoundToInt(H * 0.03F);
        btnStyle.fontSize = Mathf.RoundToInt(H * 0.045F);

        if (fatal != null)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(20, 20, Screen.width - 40, Screen.height - 40), "REKKR failed to start:\n" + fatal, smallStyle);
            return;
        }
        if (Doom == null)
        {
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), status ?? "", titleStyle);
            return;
        }

        if (Event.current.type == UnityEngine.EventType.Repaint)
        {
            Graphics.DrawTexture(gameRect, video.Texture, screenMat);
            DrawControls();
            if (input.TitleMode && !settingsOpen)
            {
                var a = 0.55F + 0.45F * Mathf.Sin(Time.unscaledTime * 3.2F);
                GUI.color = new Color(1, 1, 1, a);
                if (Doom.State == DoomState.DemoPlayback || (Doom.State == DoomState.Opening && Doom.Opening.State == OpeningSequenceState.Demo))
                {
                    // Attract demo: sit above the status bar on a dark pill instead of over the HUD.
                    var pill = new Rect(Screen.width * 0.5F - H * 0.24F, H * 0.70F, H * 0.48F, H * 0.1F);
                    GUI.color = new Color(0, 0, 0, 0.55F * a);
                    GUI.DrawTexture(pill, texWhite);
                    GUI.color = new Color(1, 1, 1, a);
                    GUI.Label(pill, "TAP TO PLAY", titleStyle);
                }
                else
                {
                    GUI.Label(new Rect(0, H * 0.86F, Screen.width, H * 0.1F), "TAP TO PLAY", titleStyle);
                }
                GUI.color = Color.white;
            }
        }
        if (settingsOpen) DrawSettings();
    }

    private void DrawControls()
    {
        var alpha = controlsOpacity / 100F;
        bool inGame = !input.MenuMode && !input.TitleMode;

        if (inGame && !settingsOpen)
        {
            var r = input.StickRadius;
            GUI.color = new Color(1, 1, 1, alpha * (input.StickActive ? 1F : 0.7F));
            GUI.DrawTexture(new Rect(input.StickCenter.x - r, input.StickCenter.y - r, 2 * r, 2 * r), texStickBase);
            var k = r * 0.46F;
            GUI.color = new Color(1, 1, 1, alpha * (input.StickActive ? 1F : 0.85F));
            GUI.DrawTexture(new Rect(input.StickKnob.x - k, input.StickKnob.y - k, 2 * k, 2 * k), texStickKnob);
        }

        foreach (var b in input.Buttons)
        {
            if (!b.Visible) continue;
            if (settingsOpen && b.Id != Ctl.Settings) continue;
            var pressed = b.Held || b.PressFlash > 0;
            if (b.Id == Ctl.Run && input.RunOn) pressed = true;
            var scale = pressed ? 0.94F : 1F;
            var r = b.Radius * scale;
            var rect = new Rect(b.Center.x - r, b.Center.y - r, 2 * r, 2 * r);
            GUI.color = new Color(1, 1, 1, pressed ? Mathf.Min(1F, alpha + 0.2F) : alpha);
            GUI.DrawTexture(rect, pressed ? texBtnPressed : texBtn);
            if (b.Icon != null && icons.TryGetValue(b.Icon, out var ic) && ic != null)
            {
                GUI.color = pressed ? new Color(0.12F, 0.08F, 0.04F, 1F) : new Color(1F, 0.96F, 0.88F, Mathf.Min(1F, alpha + 0.15F));
                GUI.DrawTexture(rect, ic);
            }
        }
        GUI.color = Color.white;
    }

    private void DrawSettings()
    {
        float W = Screen.width, H = Screen.height;
        var panel = new Rect(W * 0.2F, H * 0.1F, W * 0.6F, H * 0.8F);
        GUI.color = new Color(0, 0, 0, 0.82F);
        GUI.DrawTexture(new Rect(0, 0, W, H), texWhite);
        GUI.color = new Color(0.09F, 0.07F, 0.05F, 0.97F);
        GUI.DrawTexture(panel, texWhite);
        GUI.color = new Color(0.87F, 0.70F, 0.36F, 1F);
        var bw = Mathf.Max(2, H * 0.004F);
        GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, bw), texWhite);
        GUI.DrawTexture(new Rect(panel.x, panel.yMax - bw, panel.width, bw), texWhite);
        GUI.DrawTexture(new Rect(panel.x, panel.y, bw, panel.height), texWhite);
        GUI.DrawTexture(new Rect(panel.xMax - bw, panel.y, bw, panel.height), texWhite);
        GUI.color = Color.white;

        GUI.Label(new Rect(panel.x, panel.y + H * 0.02F, panel.width, H * 0.09F), "TOUCH SETTINGS", titleStyle);
        var y = panel.y + H * 0.14F;
        var rowH = H * 0.1F;
        LookSensitivity = Stepper(panel, ref y, rowH, "Look sensitivity", LookSensitivity, 1, 10, 1, "");
        controlsScale = Stepper(panel, ref y, rowH, "Button size", controlsScale, 70, 140, 10, "%");
        controlsOpacity = Stepper(panel, ref y, rowH, "Button opacity", controlsOpacity, 30, 100, 10, "%");
        leftHanded = Toggle(panel, ref y, rowH, "Left-handed layout", leftHanded);
        var run = Toggle(panel, ref y, rowH, "Always run", config.game_alwaysrun);
        if (run != config.game_alwaysrun) { config.game_alwaysrun = run; }

        var done = new Rect(panel.center.x - W * 0.09F, panel.yMax - H * 0.13F, W * 0.18F, H * 0.09F);
        if (FlatButton(done, "DONE")) ToggleSettings();
        GUI.Label(new Rect(panel.x, panel.yMax - H * 0.04F, panel.width, H * 0.03F), "Sound, music, gamma and screen size: Doom menu > OPTIONS", smallStyle);
    }

    private int Stepper(Rect panel, ref float y, float rowH, string label, int value, int min, int max, int step, string unit)
    {
        var x0 = panel.x + panel.width * 0.07F;
        GUI.Label(new Rect(x0, y, panel.width * 0.5F, rowH), label, rowStyle);
        var bsz = rowH * 0.8F;
        var xr = panel.xMax - panel.width * 0.07F;
        // [–] value [+] : the value gets its own 2.2-button-wide slot so "100%" never overlaps the buttons.
        if (FlatButton(new Rect(xr - bsz * 4.4F, y + rowH * 0.1F, bsz, bsz), "–")) value = Mathf.Max(min, value - step);
        GUI.Label(new Rect(xr - bsz * 3.35F, y, bsz * 2.3F, rowH), value + unit, titleStyle);
        if (FlatButton(new Rect(xr - bsz, y + rowH * 0.1F, bsz, bsz), "+")) value = Mathf.Min(max, value + step);
        y += rowH;
        return value;
    }

    private bool Toggle(Rect panel, ref float y, float rowH, string label, bool value)
    {
        var x0 = panel.x + panel.width * 0.07F;
        GUI.Label(new Rect(x0, y, panel.width * 0.6F, rowH), label, rowStyle);
        var bsz = rowH * 0.8F;
        var xr = panel.xMax - panel.width * 0.07F;
        if (FlatButton(new Rect(xr - bsz * 4.4F, y + rowH * 0.1F, bsz * 4.4F, bsz), value ? "ON" : "OFF", value)) value = !value;
        y += rowH;
        return value;
    }

    private bool FlatButton(Rect r, string text, bool on = false)
    {
        var e = Event.current;
        var hit = e.type == UnityEngine.EventType.MouseDown && r.Contains(e.mousePosition);
        if (e.type == UnityEngine.EventType.Repaint)
        {
            GUI.color = on ? new Color(0.87F, 0.70F, 0.36F, 1F) : new Color(0.22F, 0.17F, 0.11F, 1F);
            GUI.DrawTexture(r, texWhite);
            GUI.color = new Color(0.87F, 0.70F, 0.36F, 1F);
            var b = Mathf.Max(2, Screen.height * 0.003F);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, b), texWhite);
            GUI.DrawTexture(new Rect(r.x, r.yMax - b, r.width, b), texWhite);
            GUI.DrawTexture(new Rect(r.x, r.y, b, r.height), texWhite);
            GUI.DrawTexture(new Rect(r.xMax - b, r.y, b, r.height), texWhite);
            GUI.color = on ? new Color(0.1F, 0.07F, 0.03F) : Color.white;
            GUI.Label(r, text, btnStyle);
            GUI.color = Color.white;
        }
        if (hit) { e.Use(); return true; }
        return false;
    }

    // ------------------------------------------------------------------ Firebase Game Loop

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
    }

    private void Shot(string name)
    {
        if (string.IsNullOrEmpty(shotDir)) return;
        ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, name + ".png"));
    }

    private IEnumerator Wait(float s) { var t = Time.unscaledTime + s; while (Time.unscaledTime < t) yield return null; }

    private IEnumerator TapSeq(Ctl c, float after = 0.55F) { input.Tap(c); yield return Wait(after); }

    /// Scripted session that uses the same on-screen controls a player would, so the
    /// Test Lab video shows the title, the menus, the touch HUD and real gameplay.
    private IEnumerator Autopilot()
    {
        Debug.Log("[REKKR-TEST] autopilot scenario " + testScenario);
        yield return Wait(9F); Shot("01_title");
        yield return TapSeq(Ctl.Ok, 1.0F); Shot("02_menu");           // tap to play -> main menu
        yield return TapSeq(Ctl.Settings, 1.6F); Shot("03_settings"); // show touch settings
        settingsOpen = false;
        yield return Wait(0.6F);
        yield return TapSeq(Ctl.Ok, 0.9F);                              // NEW GAME
        yield return TapSeq(Ctl.Ok, 0.9F); Shot("04_episode");          // episode 1
        yield return TapSeq(Ctl.Down, 0.5F);
        yield return TapSeq(Ctl.Up, 0.7F);
        yield return TapSeq(Ctl.Ok, 1.2F);                              // skill (default)
        var start = Time.unscaledTime;
        var shots = 0;
        var lastPos = Vector2.zero;
        var stuckFor = 0F;
        var turnDir = 1F;
        var phaseT = 0F;
        while (Time.unscaledTime - start < 95F)
        {
            var t = Time.unscaledTime - start;
            var world = Doom.Game?.World;
            if (Doom.State == DoomState.Game && world != null && !Doom.Menu.Active)
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

            if (t > 30 && t < 30.1F) input.Tap(Ctl.WeaponNext);
            if (t > 40 && t < 40.1F) input.Tap(Ctl.WeaponPrev);
            if (t > 52 && t < 52.1F) input.Tap(Ctl.Map);
            if (t > 56 && t < 56.1F) input.Tap(Ctl.Map);
            if (t > 14 * (shots + 1)) { Shot("10_game_" + shots.ToString("D2")); shots++; }
            yield return null;
        }
        input.AutoStick = Vector2.zero; input.AutoTurn = 0; input.AutoFire = false; input.AutoUse = false;
        yield return TapSeq(Ctl.Menu, 1.5F); Shot("20_ingame_menu");
        yield return TapSeq(Ctl.Back, 1.0F);
        FinishTestLoop();
    }

    private void FinishTestLoop()
    {
        frameTimes.Sort();
        float sum = 0; foreach (var f in frameTimes) sum += f;
        var n = Math.Max(1, frameTimes.Count);
        var avgFps = n / Math.Max(0.001F, sum);
        var p99 = frameTimes.Count > 0 ? frameTimes[(int)(frameTimes.Count * 0.99F)] * 1000F : 0;
        var summary = $"[REKKR-TEST] frames={frameTimes.Count} avg_fps={avgFps:F1} p99_frame_ms={p99:F1} screen={Screen.width}x{Screen.height} device={SystemInfo.deviceModel} gpu={SystemInfo.graphicsDeviceName}";
        Debug.Log(summary);
        try
        {
            var outPath = Path.Combine(string.IsNullOrEmpty(shotDir) ? Application.persistentDataPath : shotDir, "testloop_result.txt");
            File.WriteAllText(outPath, summary + "\n");
        }
        catch (Exception) { }
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
