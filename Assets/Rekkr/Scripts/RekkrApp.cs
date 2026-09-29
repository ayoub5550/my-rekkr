// my-rekkr — REKKR for Android. Unity host for the Managed Doom engine:
// boots the game, runs vanilla 35 Hz tics with interpolated frames (60/90/120 Hz), draws the
// original software-rendered frame (widescreen Hor+ or 4:3) with professional touch controls,
// and adds mobile features: autosave + quick save/load, gyro aim, haptics, a button layout
// editor, English/Arabic UI, and the Firebase Game Loop autopilot.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ManagedDoom;
using ManagedDoom.UnityPort;
using ManagedDoom.Video;
using UnityEngine;
using UnityEngine.Networking;

public sealed partial class RekkrApp : MonoBehaviour
{
    public const string Version = "0.3.0";

    private static readonly string[] dataFiles = { "rekkr.wad", "rekkr-compat.wad", "TimGM6mb.sf2", "GeneralUser-GS.sf2" };
    private const int QuickSlot = 8;   // doomsav8.dsg — not shown in the 6-slot Doom menu
    private const int AutoSlot = 9;    // doomsav9.dsg
    private const float GeneralUserGain = 1.33F; // measured: GeneralUser GS renders ~25% quieter than TimGM6mb

    public Doom Doom { get; private set; }
    private Config config;
    private GameContent content;
    private UnityVideo video;
    private UnitySound sound;
    private UnityMusic music;
    private TouchInput input;
    private string dataDir;

    private Material screenMat;
    private Texture2D texBtn, texBtnPressed, texStickBase, texStickKnob, texWhite;
    private readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
    private Font latoFont, arabicFont;

    private double ticAccum;
    private const double TicTime = 1.0 / 35.0;
    private string fatal;
    private string status;
    private Rect gameRect;
    private int lastScreenW, lastScreenH;

    private bool settingsOpen;
    private int settingsTab;

    // Test loop / capture.
    private bool testLoop;
    private int testScenario = 1;
    private readonly List<float> frameTimes = new List<float>(40000);
    private readonly List<float> renderTimes = new List<float>(40000);   // dev3: software render CPU ms
    private readonly List<float> uploadTimes = new List<float>(40000);   // dev3: texture upload CPU ms
    private string shotDir;

    // Gameplay watchers (haptics, autosave).
    private World lastWorld;
    private bool autosaveDue;
    private int lastHealth, lastArmor, lastAmmo = -1;
    private WeaponType lastWeapon;
    private bool canContinue;

    // FPS counter.
    private float fpsTimer; private int fpsFrames; private float fpsValue;

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
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Input.multiTouchEnabled = true;

        var cam = new GameObject("Camera").AddComponent<Camera>();
        cam.transform.SetParent(transform);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 0;
        cam.orthographic = true;

        RekkrSettings.Load();
        if (RekkrSettings.StablePerf) PerfMode.SetSustained(true);
        var envThreads = Environment.GetEnvironmentVariable("REKKR_THREADS");   // desktop A/B
        ThreeDRendererPool.Threads = string.IsNullOrEmpty(envThreads) ? RekkrSettings.RenderThreads : int.Parse(envThreads);
        Loc.Arabic = RekkrSettings.Arabic;
        status = Loc.T("loading");
        Haptics.Init();
        Haptics.Enabled = RekkrSettings.Haptics;
        DisplayRate.Init();
        DisplayRate.Apply(RekkrSettings.FpsMode);
        if (SystemInfo.supportsGyroscope) Input.gyro.enabled = RekkrSettings.Gyro;

        screenMat = new Material(Resources.Load<Shader>("Rekkr/RekkrScreen"));
        texBtn = Resources.Load<Texture2D>("Rekkr/UI/btn");
        texBtnPressed = Resources.Load<Texture2D>("Rekkr/UI/btn_pressed");
        texStickBase = Resources.Load<Texture2D>("Rekkr/UI/stick_base");
        texStickKnob = Resources.Load<Texture2D>("Rekkr/UI/stick_knob");
        texWhite = Texture2D.whiteTexture;
        latoFont = Resources.Load<Font>("Rekkr/UI/Lato-Black");
        arabicFont = Resources.Load<Font>("Rekkr/UI/RekkrArabic");
        foreach (var n in new[] { "fire", "use", "wnext", "wprev", "map", "menu", "run", "up", "down", "left", "right",
                                  "ok", "back", "settings", "qsave", "qload", "play", "move" })
        {
            icons[n] = Resources.Load<Texture2D>("Rekkr/UI/ic_" + n);
            var ar = Resources.Load<Texture2D>("Rekkr/UI/ic_" + n + "_ar");
            if (ar != null) icons[n + "_ar"] = ar;
        }

        DetectTestLoop();
        shotDir = Environment.GetEnvironmentVariable("REKKR_SHOTS");
        if (!string.IsNullOrEmpty(shotDir)) { testLoop = true; Directory.CreateDirectory(shotDir); }
        if (Environment.GetEnvironmentVariable("REKKR_SMOOTHLOOK") == "0") RekkrSettings.SmoothLook = false; // desktop A/B
        if (Environment.GetEnvironmentVariable("REKKR_DYNRES") == "0") RekkrSettings.DynamicRes = false;       // desktop A/B
        var envLight = Environment.GetEnvironmentVariable("REKKR_TRUECOLOR");
        if (!string.IsNullOrEmpty(envLight)) RekkrSettings.SmoothLighting = envLight == "1";
    }

    private IEnumerator Start()
    {
        dataDir = Path.Combine(Application.persistentDataPath, "data");
        Directory.CreateDirectory(dataDir);
        foreach (var f in dataFiles)
        {
            status = Loc.T("preparing");
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
            lastScreenW = Screen.width; lastScreenH = Screen.height;
            video = new UnityVideo(config, content, WideWidth(StartLines()), StartLines());
            sound = new UnitySound(config, content, gameObject);
            try
            {
                music = new UnityMusic(config, content, gameObject, SoundFontPath(), SoundFontGain());
            }
            catch (Exception e)
            {
                Debug.LogWarning("Music disabled: " + e.Message);
                music = null;
            }
            input = new TouchInput(config, this);
            Doom = new Doom(args, config, content, video, sound, music, input);
            status = null;
            RefreshContinue();
            Debug.Log($"[REKKR] started {Version} mode={content.Wad.GameMode} frame={video.FrameWidth}x{video.FrameHeight} threads={video.RenderThreads} cores={SystemInfo.processorCount} testLoop={testLoop} lang={(Loc.Arabic ? "ar" : "en")}");
        }
        catch (Exception e)
        {
            fatal = e.ToString();
            Debug.LogError(fatal);
            yield break;
        }

        if (testLoop) StartCoroutine(Autopilot());
    }

    private string SoundFontPath() => Path.Combine(dataDir, RekkrSettings.MusicHQ ? "GeneralUser-GS.sf2" : "TimGM6mb.sf2");
    private float SoundFontGain() => RekkrSettings.MusicHQ ? GeneralUserGain : 1F;

    /// <summary>Frame width in 640x400 pixels for the current screen: fills the display
    /// (Doom pixels are 1.2x taller than wide), 640 = classic 4:3.</summary>
    private int WideWidth() => WideWidth(video != null ? video.Lines : StartLines());

    /// <summary>dev3: frame width for a frame of <paramref name="lines"/> lines (400/600/800/1000).</summary>
    private int WideWidth(int lines)
    {
        if (!RekkrSettings.Widescreen) return lines * 8 / 5;
        float w = Mathf.Max(Screen.width, Screen.height), h = Mathf.Min(Screen.width, Screen.height);
        var width = Mathf.RoundToInt(lines * 1.2F * w / Mathf.Max(1F, h));
        return Mathf.Clamp(width, lines * 8 / 5, lines * 3) & ~1;
    }

    private void ApplyWidescreen()
    {
        if (video != null && video.SetFrame(WideWidth(), video.Lines))
        {
            Doom?.ResetWipe();
            Debug.Log($"[REKKR] frame {video.FrameWidth}x{video.FrameHeight}");
        }
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

    /// <summary>dev3 smooth look: only for a live, unpaused game with the menu/settings closed and
    /// touch/gyro turning (keyboard/gamepad turning keeps the classic interpolation).</summary>
    private bool SmoothLookActive()
    {
        if (!RekkrSettings.SmoothLook || !InLevel || settingsOpen || input.EditMode || input.LastTicKeyTurn) return false;
        if (Doom.Menu.Active || Doom.Game.Paused || Doom.Game.World.AutoMap.Visible) return false;
        var p = Doom.Game.World.ConsolePlayer;
        return p.PlayerState == PlayerState.Live && p.Mobj != null;
    }

    public bool InLevel => Doom != null && Doom.State == DoomState.Game && Doom.Game.State == GameState.Level;
    public bool CanContinue => canContinue;

    private void Update()
    {
        if (Doom == null || fatal != null) return;

        if (Screen.width != lastScreenW || Screen.height != lastScreenH)
        {
            lastScreenW = Screen.width; lastScreenH = Screen.height;
            ApplyWidescreen();
        }
        ComputeLayout();
        input.Poll(RekkrSettings.LeftHanded, settingsOpen);

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
            if (tics > 0) WatchGameplay();
            var frac = (float)(ticAccum / TicTime);
            video.LocalViewTurn = SmoothLookActive() ? input.PendingTurn : (Angle?)null;
            ThreeDRenderer.TrueColor = RekkrSettings.SmoothLighting;
            video.Render(Doom, Fixed.FromFloat(Mathf.Clamp01(frac)));
            if (testLoop) TrackViewAngle(frac);
            UpdateDynamicResolution();
        }
        catch (Exception e)
        {
            fatal = e.ToString();
            Debug.LogError(fatal);
        }

        fpsFrames++; fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 0.5F) { fpsValue = fpsFrames / fpsTimer; fpsFrames = 0; fpsTimer = 0; }
        if (testLoop)
        {
            frameTimes.Add(Time.unscaledDeltaTime);
            renderTimes.Add(video.LastRenderMs);
            uploadTimes.Add(video.LastUploadMs);
        }
    }

    /// <summary>Per-tic checks: vibration on attack/damage, and an autosave shortly after each level starts.</summary>
    private void WatchGameplay()
    {
        if (!InLevel) { lastWorld = null; return; }
        var world = Doom.Game.World;
        var p = world.ConsolePlayer;
        var ammoType = DoomInfo.WeaponInfos[(int)p.ReadyWeapon].Ammo;
        var ammo = ammoType == AmmoType.NoAmmo ? -1 : p.Ammo[(int)ammoType];
        if (world != lastWorld)
        {
            lastWorld = world;
            autosaveDue = true;
        }
        else
        {
            var lost = (lastHealth - p.Health) + (lastArmor - p.ArmorPoints);
            if (lost > 0 && p.Health < lastHealth + 1) Haptics.Pulse(Mathf.Clamp(25 + lost * 2, 30, 90), Mathf.Clamp(120 + lost * 6, 120, 255), 0.12F);
            else if (p.ReadyWeapon == lastWeapon && ammo >= 0 && ammo < lastAmmo) Haptics.Pulse(14, 110);
        }
        lastHealth = p.Health; lastArmor = p.ArmorPoints; lastAmmo = ammo; lastWeapon = p.ReadyWeapon;

        if (autosaveDue && world.LevelTime > 70 && p.Health > 0)
        {
            autosaveDue = false;
            SaveTo(AutoSlot, "AUTO");
        }
    }

    private void ComputeLayout()
    {
        float W = Screen.width, H = Screen.height;
        // Doom pixels are 1.2x taller than wide: a 640x400 frame is shown as 4:3.
        var aspect = video.FrameWidth / (video.FrameHeight * 1.2F);
        float gh = H, gw = H * aspect;
        if (gw > W) { gw = W; gh = W / aspect; }
        gameRect = new Rect((W - gw) / 2, (H - gh) / 2, gw, gh);
        input.Layout(gameRect, RekkrSettings.ControlsScale / 100F, RekkrSettings.LeftHanded);
    }

    /// <summary>Weapon cell (0..5 = weapons 2..7) under a screen point on the status bar or fullscreen HUD, else -1.</summary>
    public int ArmsSlotAt(Vector2 p)
    {
        if (!InLevel || settingsOpen || Doom.Menu.Active) return -1;
        var world = Doom.Game.World;
        if (world.Options.Deathmatch != 0) return -1;
        var size = video.WindowSize;
        bool hud;
        if (world.AutoMap.Visible || size <= 7) hud = false;
        else if (size == 9) hud = true;
        else return -1;
        var fx = (p.x - gameRect.x) / gameRect.width * video.FrameWidth;
        var fy = (p.y - gameRect.y) / gameRect.height * video.FrameHeight;
        var bx = (fx - video.CenterOffset) / video.Scale;
        var by = fy / video.Scale;
        return StatusBarRenderer.HitArms(bx, by, hud);
    }

    // ------------------------------------------------------------------ saves

    private string SavePath(int slot) => Path.Combine(ConfigUtilities.GetExeDirectory(), "doomsav" + slot + ".dsg");

    private bool SaveTo(int slot, string kind)
    {
        if (!InLevel) return false;
        var p = Doom.Game.World.ConsolePlayer;
        if (p.Health <= 0) return false;
        try
        {
            var o = Doom.Game.Options;
            var desc = $"{kind} E{o.Episode}M{o.Map} {DateTime.Now:dd/MM HH:mm}";
            SaveAndLoad.Save(Doom.Game, desc, SavePath(slot));
            canContinue = true;
            Debug.Log($"[REKKR] saved slot {slot}: {desc}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Save failed: " + e.Message);
            return false;
        }
    }

    private int LatestSave()
    {
        int best = -1; var bestTime = DateTime.MinValue;
        foreach (var s in new[] { QuickSlot, AutoSlot })
        {
            var f = SavePath(s);
            if (File.Exists(f) && File.GetLastWriteTimeUtc(f) > bestTime) { best = s; bestTime = File.GetLastWriteTimeUtc(f); }
        }
        return best;
    }

    private void RefreshContinue() => canContinue = LatestSave() >= 0;

    public void QuickSave()
    {
        if (SaveTo(QuickSlot, "QUICK"))
        {
            Doom.Game.World.ConsolePlayer.SendMessage("QUICK SAVE DONE.");
            Haptics.Pulse(20, 140);
        }
    }

    public void QuickLoad() => LoadLatest();
    public void Continue() => LoadLatest();

    private void LoadLatest()
    {
        var slot = LatestSave();
        if (slot < 0) return;
        if (Doom.Menu.Active) Doom.Menu.Close();
        settingsOpen = false;
        Doom.LoadGame(slot);
        Haptics.Pulse(20, 140);
        Debug.Log("[REKKR] load slot " + slot);
    }

    // ------------------------------------------------------------------ lifecycle

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
        // Leaving the app mid-level autosaves, then opens the menu (pauses single-player like vanilla).
        if (!testLoop && InLevel) SaveTo(AutoSlot, "AUTO");
        if (!testLoop && Doom.State == DoomState.Game && !Doom.Menu.Active)
        {
            Doom.PostEvent(new DoomEvent(ManagedDoom.EventType.KeyDown, ManagedDoom.DoomKey.Escape));
            Doom.PostEvent(new DoomEvent(ManagedDoom.EventType.KeyUp, ManagedDoom.DoomKey.Escape));
        }
    }

    private void OnApplicationQuit()
    {
        if (!testLoop && InLevel) SaveTo(AutoSlot, "AUTO");
        SaveSettings();
    }

    private void SaveSettings()
    {
        try { config?.Save(ConfigUtilities.GetConfigPath()); } catch (Exception e) { Debug.LogWarning(e.Message); }
        RekkrSettings.Save();
    }

    public void ToggleSettings()
    {
        settingsOpen = !settingsOpen;
        if (!settingsOpen) { input.EditMode = false; SaveSettings(); }
    }
}
