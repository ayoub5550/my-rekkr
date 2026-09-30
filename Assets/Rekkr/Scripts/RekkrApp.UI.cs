// my-rekkr — IMGUI layer: game frame, touch controls, crosshair, settings panel (4 tabs, English/
// Arabic with right-to-left layout), button layout editor and FPS counter.
// dev4: "carved stone" skin in REKKR's own style (RekkrSkin: WAD pixel font + stone frames).
// SPDX-License-Identifier: GPL-2.0-or-later
using ManagedDoom;
using ManagedDoom.UnityPort;
using UnityEngine;
using EventType = UnityEngine.EventType;

public sealed partial class RekkrApp
{
    private static readonly Color Bone = new Color(1F, 0.93F, 0.72F);
    private readonly RekkrSkin skin = new RekkrSkin();
    private bool skinFrames;

    // Text sizes (glyph height in screen pixels; the pixel font snaps to integer scales).
    private static float PxTitle => Screen.height * 0.046F;
    private static float PxRow => Screen.height * 0.028F;
    private static float PxSmall => Screen.height * 0.022F;
    private static float PxHint => Screen.height * 0.019F;
    private static TextAnchor Lead => Loc.Arabic ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

    private void EnsureSkin()
    {
        if (!skinFrames) { skin.InitFrames(arabicFont != null ? arabicFont : latoFont); skinFrames = true; }
        if (!skin.Ready && content != null) skin.InitFont(content.Wad, arabicFont != null ? arabicFont : latoFont);
    }

    private void OnGUI()
    {
        var evt = Event.current.type;
        if (evt != EventType.Repaint && !settingsOpen && (input == null || !input.EditMode)) return;
        EnsureSkin();
        float H = Screen.height, W = Screen.width;

        if (fatal != null)
        {
            GUI.color = Color.white;
            skin.Wrapped(new Rect(20, 20, W - 40, H - 40), "REKKR failed to start: " + fatal, PxHint, Ink.Bone, false);
            return;
        }
        if (Doom == null)
        {
            skin.Text(new Rect(0, 0, W, H), status ?? "", PxTitle, TextAnchor.MiddleCenter, Ink.Bone);
            return;
        }

        if (evt == EventType.Repaint)
        {
            if (postThisFrame) postFx.Draw(gameRect);
            else Graphics.DrawTexture(gameRect, video.FrameTexture, screenMat);
            if (input.EditMode)
            {
                GUI.color = new Color(0, 0, 0, 0.55F);
                GUI.DrawTexture(new Rect(0, 0, W, H), texWhite);
                GUI.color = Color.white;
            }
            DrawCrosshair();
            DrawControls();
            if (input.WheelOpen && !settingsOpen) DrawWheel();
            if (input.TitleMode && !settingsOpen)
            {
                var a = 0.55F + 0.45F * Mathf.Sin(Time.unscaledTime * 3.2F);
                var demo = Doom.State == DoomState.DemoPlayback || (Doom.State == DoomState.Opening && Doom.Opening.State == OpeningSequenceState.Demo);
                var y = canContinue ? H * 0.56F : demo ? H * 0.68F : H * 0.86F;
                var pill = new Rect(W * 0.5F - H * 0.3F, y, H * 0.6F, H * 0.1F);
                if (demo)
                {
                    GUI.color = new Color(0, 0, 0, 0.6F * a);
                    GUI.DrawTexture(pill, texWhite);
                    GUI.color = Color.white;
                }
                skin.Text(pill, Loc.T("tap_to_play"), PxTitle * 0.85F, TextAnchor.MiddleCenter, Ink.Bone, a);
            }
            if (RekkrSettings.ShowFps && !settingsOpen)
            {
                GUI.color = new Color(0, 0, 0, 0.55F);
                var r = new Rect(W * 0.5F - H * 0.1F, H * 0.01F, H * 0.2F, H * 0.05F);
                GUI.DrawTexture(r, texWhite);
                GUI.color = Color.white;
                var fpsInt = Mathf.RoundToInt(fpsValue);
                if (fpsInt != fpsShown) { fpsShown = fpsInt; fpsLabel = fpsInt + " FPS"; }
                skin.Text(r, fpsLabel, PxSmall, TextAnchor.MiddleCenter, Ink.Bone);
            }
        }
        if (input.EditMode) DrawEditor();
        else if (settingsOpen) DrawSettings();
    }

    private int fpsShown = -1; private string fpsLabel = "";
    private readonly System.Collections.Generic.Dictionary<string, string> labelKeys = new System.Collections.Generic.Dictionary<string, string>();

    private Texture2D Icon(string name)
    {
        if (name == null) return null;
        return icons.TryGetValue(name, out var t) ? t : null;
    }

    /// <summary>Button caption under the icon (runtime pixel font; Arabic via TTF). Null = icon only.</summary>
    private string Label(string icon)
    {
        if (icon == null) return null;
        if (!labelKeys.TryGetValue(icon, out var key)) labelKeys[icon] = key = "lbl_" + icon;
        var s = Loc.T(key);
        return ReferenceEquals(s, key) ? null : s;
    }

    // ------------------------------------------------------------------ crosshair

    private bool CrosshairVisible()
    {
        if (RekkrSettings.Crosshair >= 4 || !InLevel || settingsOpen || input.EditMode || input.MenuMode || input.TitleMode) return false;
        if (Doom.Menu.Active || Doom.Game.World.AutoMap.Visible) return false;
        var p = Doom.Game.World.ConsolePlayer;
        return p.PlayerState == PlayerState.Live && p.Mobj != null;
    }

    private void DrawCrosshair()
    {
        if (!CrosshairVisible()) return;
        var (wx, wy, ww, wh) = video.ViewWindow;
        var sx = gameRect.width / video.FrameWidth; var sy = gameRect.height / video.FrameHeight;
        var c = new Vector2(gameRect.x + (wx + ww * 0.5F) * sx, gameRect.y + (wy + wh * 0.5F) * sy);
        c.x = Mathf.Round(c.x); c.y = Mathf.Round(c.y);
        float H = Screen.height;
        var th = Mathf.Max(2F, Mathf.Round(H * 0.004F));
        var len = Mathf.Round(H * 0.021F);
        var gap = Mathf.Round(H * 0.006F);
        var col = RekkrSettings.Crosshair == 1 ? new Color(0.95F, 0.16F, 0.1F) : RekkrSettings.Crosshair == 2 ? new Color(0.4F, 1F, 0.35F) : Bone;
        col.a = 0.92F;
        var dark = new Color(0.02F, 0.02F, 0.03F, 0.75F);
        if (RekkrSettings.Crosshair == 3)
        {
            var d = Mathf.Round(th * 2.6F);
            Box(new Rect(c.x - d / 2 - 1, c.y - d / 2 - 1, d + 2, d + 2), dark);
            Box(new Rect(c.x - d / 2, c.y - d / 2, d, d), col);
            return;
        }
        // four arms with a gap in the middle, 1 px dark outline
        var arms = new[]
        {
            new Rect(c.x - gap - len, c.y - th / 2, len, th), new Rect(c.x + gap, c.y - th / 2, len, th),
            new Rect(c.x - th / 2, c.y - gap - len, th, len), new Rect(c.x - th / 2, c.y + gap, th, len)
        };
        foreach (var a in arms) Box(new Rect(a.x - 1, a.y - 1, a.width + 2, a.height + 2), dark);
        foreach (var a in arms) Box(a, col);
        GUI.color = Color.white;
    }

    private void Box(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, texWhite);
    }

    // ------------------------------------------------------------------ touch controls

    // ------------------------------------------------------------------ dev5 weapon wheel

    private readonly System.Collections.Generic.Dictionary<WeaponType, Texture2D> weaponIcons =
        new System.Collections.Generic.Dictionary<WeaponType, Texture2D>();
    private byte[] playpalRaw;

    /// <summary>Weapon icon from the WAD at runtime: the pickup sprite (or the hand sprite for fist/pistol).</summary>
    private Texture2D WeaponIcon(WeaponType w)
    {
        if (weaponIcons.TryGetValue(w, out var tex)) return tex;
        tex = null;
        try
        {
            ManagedDoom.Sprite sp;
            switch (w)
            {
                case WeaponType.Fist: sp = ManagedDoom.Sprite.PUNG; break;
                case WeaponType.Pistol: sp = ManagedDoom.Sprite.PISG; break;
                case WeaponType.Chainsaw: sp = ManagedDoom.Sprite.CSAW; break;
                case WeaponType.Shotgun: sp = ManagedDoom.Sprite.SHOT; break;
                case WeaponType.SuperShotgun: sp = ManagedDoom.Sprite.SGN2; break;
                case WeaponType.Chaingun: sp = ManagedDoom.Sprite.MGUN; break;
                case WeaponType.Missile: sp = ManagedDoom.Sprite.LAUN; break;
                case WeaponType.Plasma: sp = ManagedDoom.Sprite.PLAS; break;
                default: sp = ManagedDoom.Sprite.BFUG; break;
            }
            playpalRaw ??= content.Wad.ReadLump("PLAYPAL");
            var patch = content.Sprites[sp].Frames[0].Patches[0];
            int pw = patch.Width, ph = patch.Height;
            var px = new Color32[pw * ph];
            for (var x = 0; x < pw; x++)
                foreach (var col in patch.Columns[x])
                    for (var i = 0; i < col.Length; i++)
                    {
                        var y = col.TopDelta + i;
                        if (y < 0 || y >= ph) continue;
                        var c = col.Data[col.Offset + i];
                        px[(ph - 1 - y) * pw + x] = new Color32(playpalRaw[3 * c], playpalRaw[3 * c + 1], playpalRaw[3 * c + 2], 255);
                    }
            tex = new Texture2D(pw, ph, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, true);
        }
        catch (System.Exception e) { Debug.LogWarning("weapon icon " + w + ": " + e.Message); }
        weaponIcons[w] = tex;
        return tex;
    }

    private void DrawWheel()
    {
        float H = Screen.height;
        var items = input.WheelItems;
        var n = items.Count;
        if (n == 0) return;
        var c = gameRect.center;
        var R = H * 0.27F;
        var cell = H * 0.19F;
        GUI.color = new Color(0, 0, 0, 0.45F);
        GUI.DrawTexture(new Rect(c.x - R - cell * 0.7F, c.y - R - cell * 0.7F, 2 * (R + cell * 0.7F), 2 * (R + cell * 0.7F)), texStickBase);
        GUI.color = Color.white;
        for (var i = 0; i < n; i++)
        {
            var a = i * Mathf.PI * 2 / n;
            var pos = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * R;
            var sel = i == input.WheelSel;
            var usable = input.WheelUsable[i];
            var sz = sel ? cell * 1.18F : cell;
            var r = new Rect(pos.x - sz / 2, pos.y - sz / 2, sz, sz);
            GUI.color = new Color(1, 1, 1, usable ? 1F : 0.45F);
            GUI.DrawTexture(r, sel ? texBtnPressed : texBtn);
            var icon = WeaponIcon(items[i]);
            if (icon != null)
            {
                var k = Mathf.Min(sz * 0.62F / icon.width, sz * 0.5F / icon.height);
                var iw = icon.width * k; var ih = icon.height * k * 1.2F;   // Doom pixels are 1.2x tall
                GUI.DrawTexture(new Rect(pos.x - iw / 2, pos.y - ih / 2 - sz * 0.04F, iw, ih), icon);
            }
            GUI.color = Color.white;
            var slot = WeaponSlotNumber(items[i]);
            skin.Text(new Rect(r.x, r.yMax - sz * 0.34F, r.width, sz * 0.22F), slot.ToString(), PxSmall, TextAnchor.MiddleCenter, sel ? Ink.Red : Ink.Bone);
        }
    }

    private static int WeaponSlotNumber(WeaponType w)
    {
        switch (w)
        {
            case WeaponType.Fist: case WeaponType.Chainsaw: return 1;
            case WeaponType.Pistol: return 2;
            case WeaponType.Shotgun: case WeaponType.SuperShotgun: return 3;
            case WeaponType.Chaingun: return 4;
            case WeaponType.Missile: return 5;
            case WeaponType.Plasma: return 6;
            default: return 7;
        }
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
            GUI.color = new Color(1, 1, 1, alpha * (input.StickActive || sel ? 1F : 0.75F));
            GUI.DrawTexture(new Rect(input.StickCenter.x - r, input.StickCenter.y - r, 2 * r, 2 * r), texStickBase);
            var k = r * 0.44F;
            GUI.color = new Color(1, 1, 1, alpha * (input.StickActive ? 1F : 0.85F));
            GUI.DrawTexture(new Rect(input.StickKnob.x - k, input.StickKnob.y - k, 2 * k, 2 * k), texStickKnob);
            if (input.EditMode)
            {
                GUI.color = Color.white;
                skin.Text(new Rect(input.StickHome.x - r, input.StickHome.y + r * 1.02F, 2 * r, r * 0.3F), Loc.T("stick"), PxSmall, TextAnchor.MiddleCenter, Ink.Bone);
                if (sel) DrawRing(input.StickHome, r * 1.06F);
            }
        }

        foreach (var b in input.Buttons)
        {
            if (!b.Visible) continue;
            if (settingsOpen && !input.EditMode && b.Id != Ctl.Settings) continue;
            if (b.PillWidth > 0) { DrawPill(b); continue; }
            var pressed = b.Held || b.PressFlash > 0;
            var lit = pressed || (b.Id == Ctl.Run && input.RunOn);
            var scale = pressed ? 0.95F : 1F;
            var r = b.Radius * scale;
            var rect = new Rect(b.Center.x - r, b.Center.y - r, 2 * r, 2 * r);
            GUI.color = new Color(1, 1, 1, lit ? Mathf.Min(1F, alpha + 0.2F) : alpha);
            GUI.DrawTexture(rect, lit ? texBtnPressed : texBtn);
            var ic = Icon(b.Icon);
            var label = Label(b.Icon);
            var ia = Mathf.Min(1F, alpha + 0.2F);
            if (ic != null)
            {
                GUI.color = lit ? new Color(1F, 0.98F, 0.9F, 1F) : new Color(Bone.r, Bone.g, Bone.b, ia);
                GUI.DrawTexture(rect, ic);
            }
            if (label != null && r > Screen.height * 0.035F)
            {
                GUI.color = Color.white;
                var lr = new Rect(rect.x + r * 0.12F, b.Center.y + r * 0.2F, 2 * r * 0.88F, r * 0.34F);
                skin.Text(lr, label, Mathf.Max(7F, r * 0.2F), TextAnchor.MiddleCenter, Ink.Bone, ia);
            }
            if (input.EditMode && input.EditSelected == b.Id) DrawRing(b.Center, b.Radius * 1.12F);
        }
        GUI.color = Color.white;
    }

    private void DrawRing(Vector2 c, float r)
    {
        GUI.color = new Color(1F, 0.35F, 0.2F, 0.55F + 0.35F * Mathf.Sin(Time.unscaledTime * 6F));
        GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), texStickBase);
        GUI.color = Color.white;
    }

    private void DrawPill(TouchButton b)
    {
        var r = b.PillRect;
        var pressed = b.Held || b.PressFlash > 0;
        r = new Rect(r.x - r.height * 0.15F, r.y - r.height * 0.1F, r.width + r.height * 0.3F, r.height * 1.2F);
        GUI.color = Color.white;
        skin.Frame(pressed ? skin.PlateOn : skin.Plate, r);
        var ic = Icon("play");
        var iconRect = Loc.Arabic
            ? new Rect(r.xMax - r.height * 1.05F, r.y, r.height, r.height)
            : new Rect(r.x + r.height * 0.1F, r.y, r.height, r.height);
        if (ic != null) { GUI.color = Bone; GUI.DrawTexture(iconRect, ic); GUI.color = Color.white; }
        var textRect = Loc.Arabic ? new Rect(r.x, r.y, r.width - r.height * 0.9F, r.height) : new Rect(r.x + r.height * 0.9F, r.y, r.width - r.height * 0.9F, r.height);
        skin.Text(textRect, Loc.T("continue"), PxRow * 1.1F, TextAnchor.MiddleCenter, Ink.Bone);
    }

    // ------------------------------------------------------------------ settings panel

    private Rect panel;
    private float rowY, rowH;
    private int controlsPage;

    private void DrawSettings()
    {
        float W = Screen.width, H = Screen.height;
        panel = new Rect(W * 0.1F, H * 0.035F, W * 0.8F, H * 0.93F);
        GUI.color = new Color(0, 0, 0, 0.78F);
        GUI.DrawTexture(new Rect(0, 0, W, H), texWhite);
        GUI.color = Color.white;
        skin.Frame(skin.Panel, panel);

        skin.Text(new Rect(panel.x, panel.y + H * 0.035F, panel.width, H * 0.07F), Loc.T("settings"), PxTitle, TextAnchor.MiddleCenter, Ink.Red);

        // Tabs (right-to-left order in Arabic).
        string[] tabs = { Loc.T("tab_controls"), Loc.T("tab_motion"), Loc.T("tab_display"), Loc.T("tab_graphics") };
        var inner = panel.width - W * 0.07F;
        var tw = inner / 4F;
        for (var i = 0; i < 4; i++)
        {
            var slot = Loc.Arabic ? 3 - i : i;
            var tr = new Rect(panel.x + W * 0.035F + slot * tw + W * 0.004F, panel.y + H * 0.115F, tw - W * 0.008F, H * 0.078F);
            if (Plate(tr, tabs[i], settingsTab == i, PxSmall)) { settingsTab = i; controlsPage = 0; gfxPage = 0; displayPage = 0; }
        }

        rowY = panel.y + H * 0.215F;
        rowH = H * 0.087F;
        string footer = Loc.T("footer");
        switch (settingsTab)
        {
            case 0: DrawControlsTab(ref footer); break;
            case 1: DrawMotionTab(); break;
            case 2: DrawDisplayTab(); break;
            default: DrawGraphicsTab(); break;
        }

        var done = new Rect(panel.center.x - W * 0.08F, panel.yMax - H * 0.135F, W * 0.16F, H * 0.08F);
        if (Plate(done, Loc.T("done"), true, PxRow)) ToggleSettings();
        skin.Text(new Rect(panel.x + W * 0.03F, panel.yMax - H * 0.058F, panel.width - W * 0.06F, H * 0.03F), footer, PxHint, TextAnchor.MiddleCenter, Ink.Dim);
    }

    /// <summary>Bottom corner button (page switch) on the leading side, left of DONE.</summary>
    private bool CornerButton(string text)
    {
        float W = Screen.width, H = Screen.height;
        var w = W * 0.2F;
        var r = Loc.Arabic
            ? new Rect(panel.xMax - W * 0.04F - w, panel.yMax - H * 0.135F, w, H * 0.08F)
            : new Rect(panel.x + W * 0.04F, panel.yMax - H * 0.135F, w, H * 0.08F);
        return Plate(r, text, false, PxSmall);
    }

    private void DrawControlsTab(ref string footer)
    {
        if (controlsPage == 0)
        {
            RekkrSettings.LookSensitivity = Stepper(Loc.T("look_sens"), RekkrSettings.LookSensitivity, 1, 10, 1, "");
            RekkrSettings.ControlsScale = Stepper(Loc.T("btn_size"), RekkrSettings.ControlsScale, 70, 140, 10, "%");
            RekkrSettings.ControlsOpacity = Stepper(Loc.T("btn_alpha"), RekkrSettings.ControlsOpacity, 30, 100, 10, "%");
            if (Cycle(Loc.T("stick_mode"), Loc.T("stick_" + RekkrSettings.StickMode))) RekkrSettings.StickMode = 1 - RekkrSettings.StickMode;
            RekkrSettings.LeftHanded = Toggle(Loc.T("left"), RekkrSettings.LeftHanded);
            if (ActionRow(Loc.T("edit"), Loc.T("edit_btn"))) OpenEditor();
            if (CornerButton(Loc.Arabic ? "< " + Loc.T("more") : Loc.T("more") + " >")) controlsPage = 1;
        }
        else if (controlsPage == 1)
        {
            var fl = Toggle(Loc.T("free_look"), RekkrSettings.FreeLook);
            if (fl != RekkrSettings.FreeLook) { RekkrSettings.FreeLook = fl; if (!fl) input.CenterView(); }
            RekkrSettings.InvertLook = Toggle(Loc.T("invert_look"), RekkrSettings.InvertLook);
            RekkrSettings.AutoAim = Toggle(Loc.T("autoaim"), RekkrSettings.AutoAim);
            RekkrSettings.Jump = Toggle(Loc.T("jump"), RekkrSettings.Jump);
            if (Cycle(Loc.T("crosshair"), Loc.T("xh_" + RekkrSettings.Crosshair)))
                RekkrSettings.Crosshair = (RekkrSettings.Crosshair + 1) % RekkrSettings.CrosshairStyles;
            var run = Toggle(Loc.T("run"), config.game_alwaysrun);
            if (run != config.game_alwaysrun) input.SetRun(run);
            if (CornerButton(Loc.Arabic ? "< " + Loc.T("pad_tab") : Loc.T("pad_tab") + " >")) controlsPage = 2;
            footer = Loc.T("look_hint");
        }
        else DrawGamepadPage(ref footer);
    }

    /// <summary>dev7: gamepad status, look speed, invert, remap flow, reset.</summary>
    private void DrawGamepadPage(ref string footer)
    {
        var name = Gamepad.Connected ? Gamepad.Name.ToUpperInvariant() : Loc.T("pad_none");
        if (name.Length > 22) name = name.Substring(0, 22);
        Cycle(Loc.T("pad"), name, true);
        Gamepad.LookSpeed = Stepper(Loc.T("pad_look"), Gamepad.LookSpeed, 1, 10, 1, "");
        Gamepad.InvertLookY = Toggle(Loc.T("pad_inv"), Gamepad.InvertLookY);
        if (Gamepad.Capturing)
        {
            var step = Gamepad.Current;
            var what = Loc.T("pad_step_" + step.ToString().ToLowerInvariant());
            if (ActionRow(what + "  (" + Mathf.CeilToInt(Gamepad.StepTimeLeft) + ")", Loc.T("pad_stop"))) Gamepad.StopCapture();
            footer = Loc.T("pad_capture_hint");
        }
        else
        {
            if (ActionRow(Loc.T("pad_remap"), Loc.T("pad_remap_btn"))) Gamepad.StartCapture();
            footer = Loc.T("pad_hint");
        }
        if (ActionRow(Loc.T("pad_reset"), Loc.T("reset"))) { Gamepad.StopCapture(); Gamepad.Defaults(); }
        if (CornerButton(Loc.Arabic ? Loc.T("gfx_back") + " >" : "< " + Loc.T("gfx_back"))) { Gamepad.StopCapture(); controlsPage = 0; }
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
        // dev3: in this tab because the Controls tab is full.
        RekkrSettings.SmoothLook = Toggle(Loc.T("smooth_look"), RekkrSettings.SmoothLook);
    }

    private int displayPage;   // dev7: page 2 = save backup

    private void DrawDisplayTab()
    {
        if (displayPage == 1) { DrawBackupPage(); return; }
        var modeIdx = System.Array.IndexOf(RekkrSettings.FpsModes, RekkrSettings.FpsMode);
        if (modeIdx < 0) modeIdx = 0;
        var fpsLabelTxt = RekkrSettings.FpsMode == 0 ? Mix(Loc.T("auto"), DisplayRate.Target.ToString()) : RekkrSettings.FpsMode.ToString();
        if (Cycle(Loc.T("fps"), fpsLabelTxt))
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
        if (CornerButton(Loc.Arabic ? "< " + Loc.T("saves_tab") : Loc.T("saves_tab") + " >")) { displayPage = 1; backupStatus = null; }
    }

    /// <summary>dev7: export / import all saves + settings (Android document picker).</summary>
    private void DrawBackupPage()
    {
        Hint(Loc.T("backup_hint"));
        rowY += rowH * 0.3F;
        if (ActionRow(Loc.T("backup_export"), Loc.T("backup_export_btn"))) ExportSaves();
        if (ActionRow(Loc.T("backup_import"), Loc.T("backup_import_btn"))) ImportSaves();
        rowY += rowH * 0.3F;
        if (!string.IsNullOrEmpty(backupStatus)) Hint(backupStatus);
        if (CornerButton(Loc.Arabic ? Loc.T("gfx_back") + " >" : "< " + Loc.T("gfx_back"))) displayPage = 0;
    }

    // dev3 stage 9: Graphics tab (two pages: main + effects; the panel fits 6 rows per page).
    private int gfxPage;

    private void DrawGraphicsTab()
    {
        var next = Loc.Arabic ? "< " + Loc.T("gfx_next") : Loc.T("gfx_next") + " >";
        if (gfxPage == 0)
        {
            var preset = RekkrSettings.GfxPreset;
            if (Cycle(Loc.T("preset"), Loc.T("preset_" + preset)))
            {
                ApplyPreset(preset >= 3 ? 0 : preset + 1);   // Classic -> Balanced -> Enhanced -> Masterpiece -> Classic
            }
            var resIdx = System.Array.IndexOf(RekkrSettings.Resolutions, RekkrSettings.Resolution);
            if (Cycle(Loc.T("resolution"), RekkrSettings.Resolution + (RekkrSettings.DynamicRes ? "  (" + video.Lines + ")" : ""), true))
            {
                RekkrSettings.Resolution = RekkrSettings.Resolutions[(resIdx + 1) % RekkrSettings.Resolutions.Length];
                MarkCustom();
            }
            var dyn = Toggle(Loc.T("dynres"), RekkrSettings.DynamicRes);
            if (dyn != RekkrSettings.DynamicRes) { RekkrSettings.DynamicRes = dyn; MarkCustom(); }
            var sl = Toggle(Loc.T("smooth_light"), RekkrSettings.SmoothLighting);
            if (sl != RekkrSettings.SmoothLighting) { RekkrSettings.SmoothLighting = sl; MarkCustom(); }
            var sp = Toggle(Loc.T("stable_perf"), RekkrSettings.StablePerf);
            if (sp != RekkrSettings.StablePerf) { RekkrSettings.StablePerf = sp; PerfMode.SetSustained(sp); }
            if (ActionRow(Loc.T("gfx_more"), Loc.T("gfx_open"))) gfxPage = 1;
            if (CornerButton(next)) gfxPage = 2;
        }
        else if (gfxPage == 1)
        {
            if (Cycle(Loc.T("bloom"), Loc.T("lvl_" + RekkrSettings.Bloom))) { RekkrSettings.Bloom = (RekkrSettings.Bloom + 1) % 4; MarkCustom(); }
            if (Cycle(Loc.T("grade"), Loc.T("grade_" + RekkrSettings.ColorGrade))) { RekkrSettings.ColorGrade = (RekkrSettings.ColorGrade + 1) % 4; MarkCustom(); }
            var v = Stepper(Loc.T("vignette"), RekkrSettings.Vignette, 0, 30, 5, "%");
            if (v != RekkrSettings.Vignette) { RekkrSettings.Vignette = v; MarkCustom(); }
            var sh = Toggle(Loc.T("sharpen"), RekkrSettings.Sharpen);
            if (sh != RekkrSettings.Sharpen) { RekkrSettings.Sharpen = sh; MarkCustom(); }
            var crt = Toggle(Loc.T("crt"), RekkrSettings.Crt);
            if (crt != RekkrSettings.Crt) { RekkrSettings.Crt = crt; MarkCustom(); }
            var sf = Toggle(Loc.T("sidefill"), RekkrSettings.SideFill);
            if (sf != RekkrSettings.SideFill) { RekkrSettings.SideFill = sf; MarkCustom(); }
            if (CornerButton(next)) gfxPage = 2;
        }
        else if (gfxPage == 2)
        {
            // dev5 world effects, page 1 of 2
            var a = Toggle(Loc.T("fx_sky"), RekkrSettings.SkyFx);
            if (a != RekkrSettings.SkyFx) { RekkrSettings.SkyFx = a; MarkCustom(); }
            var b = Toggle(Loc.T("fx_water"), RekkrSettings.WaterFx);
            if (b != RekkrSettings.WaterFx) { RekkrSettings.WaterFx = b; MarkCustom(); }
            if (Cycle(Loc.T("fx_weather"), Loc.T("weather_" + RekkrSettings.Weather))) { RekkrSettings.Weather = (RekkrSettings.Weather + 1) % 4; MarkCustom(); }
            if (Cycle(Loc.T("fx_fog"), Loc.T("lvl_" + RekkrSettings.Fog))) { RekkrSettings.Fog = (RekkrSettings.Fog + 1) % 3; MarkCustom(); }
            // dev7: Off / Low (subtle, default) / High
            var lv = RekkrSettings.DynLights ? RekkrSettings.DynLightLevel : 0;
            if (Cycle(Loc.T("fx_lights"), Loc.T(lv == 0 ? "lvl_0" : lv == 1 ? "lvl_1" : "lvl_3")))
            {
                lv = (lv + 1) % 3;
                RekkrSettings.DynLights = lv > 0; if (lv > 0) RekkrSettings.DynLightLevel = lv;
                MarkCustom();
            }
            var d = Toggle(Loc.T("fx_rays"), RekkrSettings.SunRays);
            if (d != RekkrSettings.SunRays) { RekkrSettings.SunRays = d; MarkCustom(); }
            if (!RekkrSettings.FxLighting) Hint(Loc.T("fx_needs_light"));
            if (CornerButton(next)) gfxPage = 3;
        }
        else if (gfxPage == 3)
        {
            // dev6: renderer (software original / Remaster GPU 3D) + its options, dark areas
            if (!RekkrSettings.RemasterAllowed) { rowY += rowH * 0.45F; Hint(Loc.T("renderer_weak")); rowY += rowH * 0.25F; }   // weak GPU: original renderer only
            else if (Cycle(Loc.T("renderer"), Loc.T(RekkrSettings.Remaster ? "renderer_gpu" : "renderer_sw"))) RekkrSettings.Remaster = !RekkrSettings.Remaster;
            if (RekkrSettings.Remaster && RekkrSettings.RemasterAllowed)
            {
                if (Cycle(Loc.T("rm_things"), Loc.T("rm_things_" + RekkrSettings.RemasterThings))) RekkrSettings.RemasterThings = (RekkrSettings.RemasterThings + 1) % 2;
                var sh = Toggle(Loc.T("rm_shadows"), RekkrSettings.RemasterShadows);
                if (sh != RekkrSettings.RemasterShadows) RekkrSettings.RemasterShadows = sh;
                RekkrSettings.RemasterLightShadows = Toggle(Loc.T("rm_lshadows"), RekkrSettings.RemasterLightShadows);   // dev7
                if (Cycle(Loc.T("rm_weapon"), Loc.T("rm_weapon_" + RekkrSettings.RemasterWeapon))) RekkrSettings.RemasterWeapon = 1 - RekkrSettings.RemasterWeapon;
            }
            // dev6: floor for very dark sectors (E3 has many light-0 rooms); works in both renderers
            if (Cycle(Loc.T("dark_areas"), Loc.T("dark_areas_" + RekkrSettings.DarkAreas))) RekkrSettings.DarkAreas = (RekkrSettings.DarkAreas + 1) % 3;
            if (CornerButton(next)) gfxPage = 4;
        }
        else
        {
            // dev5 world effects, page 2 of 2
            var a = Toggle(Loc.T("fx_ao"), RekkrSettings.AO);
            if (a != RekkrSettings.AO) { RekkrSettings.AO = a; MarkCustom(); }
            var b = Toggle(Loc.T("fx_particles"), RekkrSettings.Particles);
            if (b != RekkrSettings.Particles) { RekkrSettings.Particles = b; MarkCustom(); }
            var c = Toggle(Loc.T("fx_dof"), RekkrSettings.DoF);
            if (c != RekkrSettings.DoF) { RekkrSettings.DoF = c; MarkCustom(); }
            if (!RekkrSettings.FxLighting) Hint(Loc.T("fx_needs_light"));
            if (CornerButton(Loc.Arabic ? Loc.T("gfx_back") + " >" : "< " + Loc.T("gfx_back"))) gfxPage = 0;
        }
    }

    private void MarkCustom()
    {
        RekkrSettings.GfxPreset = RekkrSettings.MatchPreset();
    }

    /// <summary>Applies a graphics preset (0 Classic = exactly v0.2.0, 1 Balanced, 2 Enhanced).</summary>
    public void ApplyPreset(int preset)
    {
        var threadsBefore = RekkrSettings.RenderThreads;
        RekkrSettings.ApplyPreset(preset);
        if (RekkrSettings.RenderThreads != threadsBefore)
        {
            ManagedDoom.Video.ThreeDRendererPool.Threads = RekkrSettings.RenderThreads;
            video.Rebuild();
            Doom?.ResetWipe();
        }
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
        ? new Rect(panel.center.x - panel.width * 0.02F, rowY, panel.width * 0.45F, rowH)
        : new Rect(panel.x + panel.width * 0.07F, rowY, panel.width * 0.5F, rowH);

    private Rect ControlRect(float width)
    {
        var bsz = rowH * 0.8F;
        var y = rowY + (rowH - bsz) / 2;
        return Loc.Arabic
            ? new Rect(panel.x + panel.width * 0.07F, y, width, bsz)
            : new Rect(panel.xMax - panel.width * 0.07F - width, y, width, bsz);
    }

    private void RowLabel(string label)
    {
        skin.Text(LabelRect(), label, PxRow, Lead, Ink.Bone);
        // thin engraved separator above every row but the first
        if (Event.current.type == EventType.Repaint && rowY > panel.y + Screen.height * 0.22F)
        {
            GUI.color = new Color(0.55F, 0.62F, 0.62F, 0.18F);
            GUI.DrawTexture(new Rect(panel.x + panel.width * 0.06F, rowY, panel.width * 0.88F, 1), texWhite);
            GUI.color = Color.white;
        }
    }

    private int Stepper(string label, int value, int min, int max, int step, string unit)
    {
        RowLabel(label);
        var bsz = rowH * 0.8F;
        var r = ControlRect(bsz * 5F);
        if (Plate(new Rect(r.x, r.y, bsz * 1.1F, bsz), "-", false, PxRow * 1.5F)) value = Mathf.Max(min, value - step);
        skin.Text(new Rect(r.x + bsz * 1.1F, r.y, r.width - 2.2F * bsz, bsz), value + unit, PxRow * 1.15F, TextAnchor.MiddleCenter, Ink.Red);
        if (Plate(new Rect(r.xMax - bsz * 1.1F, r.y, bsz * 1.1F, bsz), "+", false, PxRow * 1.5F)) value = Mathf.Min(max, value + step);
        rowY += rowH;
        return value;
    }

    private bool Toggle(string label, bool value)
    {
        RowLabel(label);
        var bsz = rowH * 0.8F;
        if (Plate(ControlRect(bsz * 5F), value ? Loc.T("on") : Loc.T("off"), value, PxRow, !value)) value = !value;
        rowY += rowH;
        return value;
    }

    private bool Cycle(string label, string value, bool raw = false)
    {
        RowLabel(label);
        var bsz = rowH * 0.8F;
        var hit = Plate(ControlRect(bsz * 7F), raw ? Loc.Shape(value) : value, false, PxSmall);
        rowY += rowH;
        return hit;
    }

    private bool ActionRow(string label, string button)
    {
        RowLabel(label);
        var bsz = rowH * 0.8F;
        var hit = Plate(ControlRect(bsz * 5F), button, true, PxRow);
        rowY += rowH;
        return hit;
    }

    private void Hint(string text)
    {
        var r = new Rect(panel.x + panel.width * 0.07F, rowY - rowH * 0.22F, panel.width * 0.86F, rowH * 0.55F);
        skin.Wrapped(r, text, PxHint, Ink.Dim, Loc.Arabic);
        rowY += rowH * 0.42F;
    }

    /// <summary>Stone plate button (lit red when <paramref name="on"/>). Returns true when tapped.</summary>
    private bool Plate(Rect r, string text, bool on, float px, bool dim = false)
    {
        var e = Event.current;
        var hit = e.type == EventType.MouseDown && r.Contains(e.mousePosition);
        if (e.type == EventType.Repaint)
        {
            GUI.color = Color.white;
            skin.Frame(on ? skin.PlateOn : skin.Plate, r);
            var inset = Mathf.Min(r.height * 0.28F, 22F);
            skin.Text(new Rect(r.x + inset, r.y, r.width - 2 * inset, r.height), text, px, TextAnchor.MiddleCenter, dim ? Ink.Dim : Ink.Bone);
        }
        if (hit) { e.Use(); return true; }
        return false;
    }

    // ------------------------------------------------------------------ layout editor

    private void DrawEditor()
    {
        float W = Screen.width, H = Screen.height;
        // Top centre, between the menu button (left) and quick save/load + map (right).
        var bar = new Rect(W * 0.13F, H * 0.02F, W * 0.54F, H * 0.22F);
        input.EditToolbar = bar;
        GUI.color = Color.white;
        skin.Frame(skin.Panel, bar);
        var bh = H * 0.085F;
        var y = bar.y + H * 0.03F;
        var x = bar.x + W * 0.03F;
        if (Plate(new Rect(x, y, bh, bh), "-", false, PxRow)) input.ScaleSelected(-0.1F);
        skin.Text(new Rect(x + bh, y, W * 0.12F, bh), Mix(Loc.T("size"), Mathf.RoundToInt(input.SelectedScale * 100) + "%"), PxSmall, TextAnchor.MiddleCenter, Ink.Bone);
        if (Plate(new Rect(x + bh + W * 0.12F, y, bh, bh), "+", false, PxRow)) input.ScaleSelected(0.1F);
        if (Plate(new Rect(bar.xMax - W * 0.03F - W * 0.215F, y, W * 0.1F, bh), Loc.T("reset"), false, PxSmall))
        {
            RekkrSettings.Layout.Clear();
        }
        if (Plate(new Rect(bar.xMax - W * 0.03F - W * 0.1F, y, W * 0.1F, bh), Loc.T("done"), true, PxSmall))
        {
            input.EditMode = false;
            RekkrSettings.Save();
        }
        skin.Text(new Rect(bar.x + W * 0.02F, bar.y + H * 0.13F, bar.width - W * 0.04F, H * 0.06F), Loc.T("editor_hint"), PxHint, TextAnchor.MiddleCenter, Ink.Dim);
        GUI.color = Color.white;
    }
}
