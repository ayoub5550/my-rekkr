// my-rekkr — IMGUI layer: game frame, touch controls, settings panel (3 tabs, English/Arabic
// with right-to-left layout), button layout editor and FPS counter.
// SPDX-License-Identifier: GPL-2.0-or-later
using ManagedDoom;
using ManagedDoom.UnityPort;
using UnityEngine;
using EventType = UnityEngine.EventType;

public sealed partial class RekkrApp
{
    private static readonly Color Gold = new Color(0.87F, 0.70F, 0.36F);
    private GUIStyle titleStyle, rowStyle, smallStyle, btnStyle, hintStyle;
    private bool stylesArabic;

    private void EnsureStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, normal = { textColor = Gold } };
            rowStyle = new GUIStyle { alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.95F, 0.92F, 0.85F) } };
            smallStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1, 1, 1, 0.85F) } };
            btnStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            hintStyle = new GUIStyle { alignment = TextAnchor.MiddleLeft, wordWrap = true, normal = { textColor = new Color(1, 1, 1, 0.6F) } };
            stylesArabic = !Loc.Arabic;
        }
        if (stylesArabic != Loc.Arabic)
        {
            stylesArabic = Loc.Arabic;
            var f = Loc.Arabic && arabicFont != null ? arabicFont : latoFont;
            titleStyle.font = rowStyle.font = smallStyle.font = btnStyle.font = hintStyle.font = f;
            rowStyle.alignment = Loc.Arabic ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            hintStyle.alignment = Loc.Arabic ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        }
        float H = Screen.height;
        titleStyle.fontSize = Mathf.RoundToInt(H * 0.052F);
        rowStyle.fontSize = Mathf.RoundToInt(H * 0.04F);
        smallStyle.fontSize = Mathf.RoundToInt(H * 0.028F);
        btnStyle.fontSize = Mathf.RoundToInt(H * 0.038F);
        hintStyle.fontSize = Mathf.RoundToInt(H * 0.027F);
    }

    private void OnGUI()
    {
        var evt = Event.current.type;
        if (evt != EventType.Repaint && !settingsOpen) return;
        EnsureStyles();
        float H = Screen.height, W = Screen.width;

        if (fatal != null)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(20, 20, W - 40, H - 40), "REKKR failed to start:\n" + fatal, smallStyle);
            return;
        }
        if (Doom == null)
        {
            GUI.Label(new Rect(0, 0, W, H), status ?? "", titleStyle);
            return;
        }

        if (evt == EventType.Repaint)
        {
            Graphics.DrawTexture(gameRect, video.Texture, screenMat);
            if (input.EditMode)
            {
                GUI.color = new Color(0, 0, 0, 0.55F);
                GUI.DrawTexture(new Rect(0, 0, W, H), texWhite);
                GUI.color = Color.white;
            }
            DrawControls();
            if (input.TitleMode && !settingsOpen)
            {
                var a = 0.55F + 0.45F * Mathf.Sin(Time.unscaledTime * 3.2F);
                var demo = Doom.State == DoomState.DemoPlayback || (Doom.State == DoomState.Opening && Doom.Opening.State == OpeningSequenceState.Demo);
                var y = canContinue ? H * 0.56F : demo ? H * 0.68F : H * 0.86F;
                var pill = new Rect(W * 0.5F - H * 0.26F, y, H * 0.52F, H * 0.1F);
                if (demo)
                {
                    GUI.color = new Color(0, 0, 0, 0.55F * a);
                    GUI.DrawTexture(pill, texWhite);
                }
                GUI.color = new Color(1, 1, 1, a);
                GUI.Label(pill, Loc.T("tap_to_play"), titleStyle);
                GUI.color = Color.white;
            }
            if (RekkrSettings.ShowFps && !settingsOpen)
            {
                GUI.color = new Color(0, 0, 0, 0.5F);
                var r = new Rect(W * 0.5F - H * 0.09F, H * 0.01F, H * 0.18F, H * 0.05F);
                GUI.DrawTexture(r, texWhite);
                GUI.color = Color.white;
                var fpsInt = Mathf.RoundToInt(fpsValue);
                if (fpsInt != fpsShown) { fpsShown = fpsInt; fpsLabel = fpsInt + " FPS"; }
                GUI.Label(r, fpsLabel, smallStyle);
            }
        }
        if (input.EditMode) DrawEditor();
        else if (settingsOpen) DrawSettings();
    }

    private readonly System.Collections.Generic.Dictionary<string, string> arIconKeys = new System.Collections.Generic.Dictionary<string, string>();
    private int fpsShown = -1; private string fpsLabel = "";

    private Texture2D Icon(string name)
    {
        if (name == null) return null;
        if (Loc.Arabic)
        {
            // dev3 zero-GC: cache the "_ar" key instead of concatenating every frame.
            if (!arIconKeys.TryGetValue(name, out var key)) arIconKeys[name] = key = name + "_ar";
            if (icons.TryGetValue(key, out var ar) && ar != null) return ar;
        }
        return icons.TryGetValue(name, out var t) ? t : null;
    }

    private void DrawControls()
    {
        var alpha = RekkrSettings.ControlsOpacity / 100F;
        if (input.EditMode) alpha = 1F;
        bool inGame = !input.MenuMode && !input.TitleMode;

        if (inGame && (!settingsOpen || input.EditMode))
        {
            var r = input.StickRadius;
            var sel = input.EditMode && input.EditSelected == Ctl.Stick;
            GUI.color = new Color(1, 1, 1, alpha * (input.StickActive || sel ? 1F : 0.7F));
            GUI.DrawTexture(new Rect(input.StickCenter.x - r, input.StickCenter.y - r, 2 * r, 2 * r), texStickBase);
            var k = r * 0.46F;
            GUI.color = new Color(1, 1, 1, alpha * (input.StickActive ? 1F : 0.85F));
            GUI.DrawTexture(new Rect(input.StickKnob.x - k, input.StickKnob.y - k, 2 * k, 2 * k), texStickKnob);
            if (input.EditMode)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(input.StickHome.x - r, input.StickHome.y + r * 0.72F, 2 * r, r * 0.4F), Loc.T("stick"), smallStyle);
                if (sel) DrawRing(input.StickHome, r * 1.06F);
            }
        }

        foreach (var b in input.Buttons)
        {
            if (!b.Visible) continue;
            if (settingsOpen && !input.EditMode && b.Id != Ctl.Settings) continue;
            if (b.PillWidth > 0) { DrawPill(b); continue; }
            var pressed = b.Held || b.PressFlash > 0;
            if (b.Id == Ctl.Run && input.RunOn) pressed = true;
            var scale = pressed ? 0.94F : 1F;
            var r = b.Radius * scale;
            var rect = new Rect(b.Center.x - r, b.Center.y - r, 2 * r, 2 * r);
            GUI.color = new Color(1, 1, 1, pressed ? Mathf.Min(1F, alpha + 0.2F) : alpha);
            GUI.DrawTexture(rect, pressed ? texBtnPressed : texBtn);
            var ic = Icon(b.Icon);
            if (ic != null)
            {
                GUI.color = pressed ? new Color(0.12F, 0.08F, 0.04F, 1F) : new Color(1F, 0.96F, 0.88F, Mathf.Min(1F, alpha + 0.15F));
                GUI.DrawTexture(rect, ic);
            }
            if (input.EditMode && input.EditSelected == b.Id) DrawRing(b.Center, b.Radius * 1.12F);
        }
        GUI.color = Color.white;
    }

    private void DrawRing(Vector2 c, float r)
    {
        GUI.color = new Color(1F, 0.85F, 0.4F, 0.55F + 0.35F * Mathf.Sin(Time.unscaledTime * 6F));
        GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), texStickBase);
        GUI.color = Color.white;
    }

    private void DrawPill(TouchButton b)
    {
        var r = b.PillRect;
        var pressed = b.Held || b.PressFlash > 0;
        GUI.color = pressed ? Gold : new Color(0.09F, 0.07F, 0.05F, 0.9F);
        GUI.DrawTexture(r, texWhite);
        GUI.color = Gold;
        var bw = Mathf.Max(2, Screen.height * 0.004F);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, bw), texWhite);
        GUI.DrawTexture(new Rect(r.x, r.yMax - bw, r.width, bw), texWhite);
        GUI.DrawTexture(new Rect(r.x, r.y, bw, r.height), texWhite);
        GUI.DrawTexture(new Rect(r.xMax - bw, r.y, bw, r.height), texWhite);
        var ic = Icon("play");
        var iconRect = Loc.Arabic
            ? new Rect(r.xMax - r.height * 1.05F, r.y, r.height, r.height)
            : new Rect(r.x + r.height * 0.05F, r.y, r.height, r.height);
        if (ic != null) { GUI.color = pressed ? Color.black : Color.white; GUI.DrawTexture(iconRect, ic); }
        GUI.color = pressed ? Color.black : Color.white;
        var textRect = Loc.Arabic ? new Rect(r.x, r.y, r.width - r.height * 0.9F, r.height) : new Rect(r.x + r.height * 0.9F, r.y, r.width - r.height * 0.9F, r.height);
        GUI.Label(textRect, Loc.T("continue"), btnStyle);
        GUI.color = Color.white;
    }

    // ------------------------------------------------------------------ settings panel

    private Rect panel;
    private float rowY, rowH;

    private void DrawSettings()
    {
        float W = Screen.width, H = Screen.height;
        panel = new Rect(W * 0.18F, H * 0.05F, W * 0.64F, H * 0.9F);
        GUI.color = new Color(0, 0, 0, 0.82F);
        GUI.DrawTexture(new Rect(0, 0, W, H), texWhite);
        GUI.color = new Color(0.09F, 0.07F, 0.05F, 0.97F);
        GUI.DrawTexture(panel, texWhite);
        Frame(panel, Gold, Mathf.Max(2, H * 0.004F));
        GUI.color = Color.white;

        GUI.Label(new Rect(panel.x, panel.y + H * 0.01F, panel.width, H * 0.08F), Loc.T("settings"), titleStyle);

        // Tabs (right-to-left order in Arabic).
        string[] tabs = { Loc.T("tab_controls"), Loc.T("tab_motion"), Loc.T("tab_display") };
        var tw = (panel.width - W * 0.04F) / 3F;
        for (var i = 0; i < 3; i++)
        {
            var slot = Loc.Arabic ? 2 - i : i;
            var tr = new Rect(panel.x + W * 0.02F + slot * tw + W * 0.004F, panel.y + H * 0.1F, tw - W * 0.008F, H * 0.075F);
            if (FlatButton(tr, tabs[i], settingsTab == i, smallStyle)) settingsTab = i;
        }

        rowY = panel.y + H * 0.2F;
        rowH = H * 0.093F;
        switch (settingsTab)
        {
            case 0: DrawControlsTab(); break;
            case 1: DrawMotionTab(); break;
            default: DrawDisplayTab(); break;
        }

        var done = new Rect(panel.center.x - W * 0.08F, panel.yMax - H * 0.115F, W * 0.16F, H * 0.08F);
        if (FlatButton(done, Loc.T("done"))) ToggleSettings();
        GUI.Label(new Rect(panel.x, panel.yMax - H * 0.035F, panel.width, H * 0.03F), Loc.T("footer"), smallStyle);
    }

    private void DrawControlsTab()
    {
        RekkrSettings.LookSensitivity = Stepper(Loc.T("look_sens"), RekkrSettings.LookSensitivity, 1, 10, 1, "");
        RekkrSettings.ControlsScale = Stepper(Loc.T("btn_size"), RekkrSettings.ControlsScale, 70, 140, 10, "%");
        RekkrSettings.ControlsOpacity = Stepper(Loc.T("btn_alpha"), RekkrSettings.ControlsOpacity, 30, 100, 10, "%");
        RekkrSettings.LeftHanded = Toggle(Loc.T("left"), RekkrSettings.LeftHanded);
        var run = Toggle(Loc.T("run"), config.game_alwaysrun);
        if (run != config.game_alwaysrun) input.SetRun(run);
        if (ActionRow(Loc.T("edit"), Loc.T("edit_btn"))) OpenEditor();
    }

    private void DrawMotionTab()
    {
        var gyro = Toggle(Loc.T("gyro"), RekkrSettings.Gyro);
        if (gyro != RekkrSettings.Gyro)
        {
            RekkrSettings.Gyro = gyro;
            if (SystemInfo.supportsGyroscope) Input.gyro.enabled = gyro;
        }
        RekkrSettings.GyroSensitivity = Stepper(Loc.T("gyro_sens"), RekkrSettings.GyroSensitivity, 1, 10, 1, "");
        RekkrSettings.GyroInvert = Toggle(Loc.T("gyro_inv"), RekkrSettings.GyroInvert);
        Hint(Loc.T("gyro_hint"));
        var hap = Toggle(Loc.T("haptics"), RekkrSettings.Haptics);
        if (hap != RekkrSettings.Haptics)
        {
            RekkrSettings.Haptics = Haptics.Enabled = hap;
            if (hap) Haptics.Pulse(40, 200, 0);
        }
        Hint(Loc.T("haptics_hint"));
        // dev3: in this tab because the Controls tab is full (7 rows would overlap DONE).
        RekkrSettings.SmoothLook = Toggle(Loc.T("smooth_look"), RekkrSettings.SmoothLook);
    }

    private void DrawDisplayTab()
    {
        var modeIdx = System.Array.IndexOf(RekkrSettings.FpsModes, RekkrSettings.FpsMode);
        if (modeIdx < 0) modeIdx = 0;
        var fpsLabel = RekkrSettings.FpsMode == 0 ? Mix(Loc.T("auto"), DisplayRate.Target.ToString()) : RekkrSettings.FpsMode.ToString();
        if (Cycle(Loc.T("fps"), fpsLabel))
        {
            RekkrSettings.FpsMode = RekkrSettings.FpsModes[(modeIdx + 1) % RekkrSettings.FpsModes.Length];
            DisplayRate.Apply(RekkrSettings.FpsMode);
        }
        var wide = Toggle(Loc.T("wide"), RekkrSettings.Widescreen);
        if (wide != RekkrSettings.Widescreen) { RekkrSettings.Widescreen = wide; ApplyWidescreen(); }
        var hud = HudMode;
        string[] hudNames = { Loc.T("hud_bar"), Loc.T("hud_full"), Loc.T("hud_none") };
        if (Cycle(Loc.T("hud"), hudNames[hud])) HudMode = (hud + 1) % 3;
        RekkrSettings.ShowFps = Toggle(Loc.T("show_fps"), RekkrSettings.ShowFps);
        if (Cycle(Loc.T("music"), RekkrSettings.MusicHQ ? Mix(Loc.T("music_hq"), "GeneralUser GS") : Loc.T("music_classic")))
        {
            SetMusicHQ(!RekkrSettings.MusicHQ);
        }
        if (Cycle(Loc.T("lang"), Loc.Arabic ? "العربية" : "ENGLISH", true)) SetArabic(!Loc.Arabic);
    }

    /// <summary>0 = status bar (screen size 7), 1 = fullscreen HUD (9), 2 = no HUD (8).</summary>
    public int HudMode
    {
        get { var s = video.WindowSize; return s == 9 ? 1 : s == 8 ? 2 : 0; }
        set { video.WindowSize = value == 1 ? 9 : value == 2 ? 8 : 7; }
    }

    public void SetMusicHQ(bool hq)
    {
        RekkrSettings.MusicHQ = hq;
        try { music?.SetSoundFont(SoundFontPath(), SoundFontGain()); }
        catch (System.Exception e) { Debug.LogWarning("SoundFont switch failed: " + e.Message); }
    }

    public void SetArabic(bool ar)
    {
        RekkrSettings.Arabic = Loc.Arabic = ar;
    }

    /// <summary>Word + left-to-right value in reading order (value first on screen in Arabic).</summary>
    private static string Mix(string word, string ltr) => Loc.Arabic ? ltr + " " + word : word + " " + ltr;

    private void OpenEditor()
    {
        input.EditMode = true;
        input.EditSelected = Ctl.Fire;
    }

    // Row helpers: label on the leading side, control on the trailing side (mirrored in Arabic).
    private Rect LabelRect() => Loc.Arabic
        ? new Rect(panel.center.x - panel.width * 0.05F, rowY, panel.width * 0.48F, rowH)
        : new Rect(panel.x + panel.width * 0.06F, rowY, panel.width * 0.5F, rowH);

    private Rect ControlRect(float width)
    {
        var bsz = rowH * 0.78F;
        var y = rowY + (rowH - bsz) / 2;
        return Loc.Arabic
            ? new Rect(panel.x + panel.width * 0.06F, y, width, bsz)
            : new Rect(panel.xMax - panel.width * 0.06F - width, y, width, bsz);
    }

    private int Stepper(string label, int value, int min, int max, int step, string unit)
    {
        GUI.Label(LabelRect(), label, rowStyle);
        var bsz = rowH * 0.78F;
        var r = ControlRect(bsz * 4.6F);
        if (FlatButton(new Rect(r.x, r.y, bsz, bsz), "–")) value = Mathf.Max(min, value - step);
        GUI.Label(new Rect(r.x + bsz, r.y, r.width - 2 * bsz, bsz), value + unit, titleStyle);
        if (FlatButton(new Rect(r.xMax - bsz, r.y, bsz, bsz), "+")) value = Mathf.Min(max, value + step);
        rowY += rowH;
        return value;
    }

    private bool Toggle(string label, bool value)
    {
        GUI.Label(LabelRect(), label, rowStyle);
        var bsz = rowH * 0.78F;
        if (FlatButton(ControlRect(bsz * 4.6F), value ? Loc.T("on") : Loc.T("off"), value)) value = !value;
        rowY += rowH;
        return value;
    }

    private bool Cycle(string label, string value, bool raw = false)
    {
        GUI.Label(LabelRect(), label, rowStyle);
        var bsz = rowH * 0.78F;
        var hit = FlatButton(ControlRect(bsz * 6.2F), raw ? Loc.Shape(value) : value, false, smallStyle);
        rowY += rowH;
        return hit;
    }

    private bool ActionRow(string label, string button)
    {
        GUI.Label(LabelRect(), label, rowStyle);
        var bsz = rowH * 0.78F;
        var hit = FlatButton(ControlRect(bsz * 4.6F), button, true);
        rowY += rowH;
        return hit;
    }

    private void Hint(string text)
    {
        var r = Loc.Arabic
            ? new Rect(panel.x + panel.width * 0.06F, rowY - rowH * 0.2F, panel.width * 0.88F, rowH * 0.55F)
            : new Rect(panel.x + panel.width * 0.06F, rowY - rowH * 0.2F, panel.width * 0.88F, rowH * 0.55F);
        GUI.Label(r, text, hintStyle);
        rowY += rowH * 0.45F;
    }

    private void Frame(Rect r, Color c, float bw)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, bw), texWhite);
        GUI.DrawTexture(new Rect(r.x, r.yMax - bw, r.width, bw), texWhite);
        GUI.DrawTexture(new Rect(r.x, r.y, bw, r.height), texWhite);
        GUI.DrawTexture(new Rect(r.xMax - bw, r.y, bw, r.height), texWhite);
        GUI.color = Color.white;
    }

    private bool FlatButton(Rect r, string text, bool on = false, GUIStyle style = null)
    {
        var e = Event.current;
        var hit = e.type == EventType.MouseDown && r.Contains(e.mousePosition);
        if (e.type == EventType.Repaint)
        {
            GUI.color = on ? Gold : new Color(0.22F, 0.17F, 0.11F, 1F);
            GUI.DrawTexture(r, texWhite);
            Frame(r, Gold, Mathf.Max(2, Screen.height * 0.003F));
            GUI.color = on ? new Color(0.1F, 0.07F, 0.03F) : Color.white;
            var st = style ?? btnStyle;
            var old = st.normal.textColor;
            st.normal.textColor = GUI.color;
            GUI.color = Color.white;
            GUI.Label(r, text, st);
            st.normal.textColor = old;
        }
        if (hit) { e.Use(); return true; }
        return false;
    }

    // ------------------------------------------------------------------ layout editor

    private void DrawEditor()
    {
        float W = Screen.width, H = Screen.height;
        // Top centre, between the menu button (left) and quick save/load + map (right).
        var bar = new Rect(W * 0.13F, H * 0.02F, W * 0.54F, H * 0.2F);
        input.EditToolbar = bar;
        GUI.color = new Color(0.09F, 0.07F, 0.05F, 0.95F);
        GUI.DrawTexture(bar, texWhite);
        Frame(bar, Gold, Mathf.Max(2, H * 0.004F));
        var bh = H * 0.085F;
        var y = bar.y + H * 0.018F;
        var x = bar.x + W * 0.012F;
        if (FlatButton(new Rect(x, y, bh, bh), "–")) input.ScaleSelected(-0.1F);
        GUI.Label(new Rect(x + bh, y, W * 0.13F, bh), Mix(Loc.T("size"), Mathf.RoundToInt(input.SelectedScale * 100) + "%"), smallStyle);
        if (FlatButton(new Rect(x + bh + W * 0.13F, y, bh, bh), "+")) input.ScaleSelected(0.1F);
        if (FlatButton(new Rect(bar.xMax - W * 0.012F - W * 0.215F, y, W * 0.1F, bh), Loc.T("reset")))
        {
            RekkrSettings.Layout.Clear();
        }
        if (FlatButton(new Rect(bar.xMax - W * 0.012F - W * 0.1F, y, W * 0.1F, bh), Loc.T("done"), true))
        {
            input.EditMode = false;
            RekkrSettings.Save();
        }
        GUI.Label(new Rect(bar.x, bar.y + H * 0.115F, bar.width, H * 0.07F), Loc.T("editor_hint"), smallStyle);
        GUI.color = Color.white;
    }
}
