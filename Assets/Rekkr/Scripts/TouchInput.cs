// my-rekkr — touch, keyboard and gamepad input for Managed Doom on Android.
// Builds vanilla TicCmds (same move/turn limits as Doom) from on-screen controls.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using ManagedDoom.UserInput;
using UnityEngine;

namespace ManagedDoom.UnityPort
{
    public enum Ctl
    {
        None, Stick, Look, Fire, Use, WeaponNext, WeaponPrev, Map, Menu, Run,
        Up, Down, Left, Right, Ok, Back, Settings,
        QuickSave, QuickLoad, Continue, Slot, Jump, MenuTap
    }

    /// <summary>One on-screen control (circle), in GUI pixel coordinates (y down).</summary>
    public sealed class TouchButton
    {
        public Ctl Id;
        public Vector2 Center;
        public float Radius;
        public string Icon;
        public bool Visible;
        public bool Held;          // pressed by a finger or the autopilot
        public float PressFlash;   // short highlight after a tap
        public float PillWidth;    // > 0: rounded-rectangle button (CONTINUE)

        public Rect PillRect => new Rect(Center.x - PillWidth / 2, Center.y - Radius, PillWidth, 2 * Radius);
    }

    public sealed class TouchInput : IUserInput
    {
        private readonly Config config;
        private readonly RekkrApp app;
        private readonly Dictionary<Ctl, TouchButton> buttons = new Dictionary<Ctl, TouchButton>();
        private readonly Dictionary<int, Ctl> owner = new Dictionary<int, Ctl>();
        private readonly Dictionary<int, Vector2> lastPos = new Dictionary<int, Vector2>();

        // Move stick state (GUI pixels).
        public Vector2 StickHome;
        public Vector2 StickCenter;
        public Vector2 StickKnob;
        public float StickRadius;
        public bool StickActive;
        private int stickFinger = -1;

        // dev4 free look: view pitch in 200-line units (up = positive), applied every frame.
        private float pitch;
        public float Pitch => pitch;
        public int PitchInt => Mathf.Clamp(Mathf.RoundToInt(pitch), -TicCmdExt.MaxPitch, TicCmdExt.MaxPitch);
        public void CenterView() { pitch = 0; }
        private float lastLookTap = -10;
        private float stickReturn;     // knob ease back to the centre after release (s)
        private Vector2 stickReleaseKnob;
        public float AutoPitch;        // autopilot: pitch units per second

        private float turnPixels;      // accumulated horizontal swipe since last tic
        private float turnCarry;
        private int weaponRequest = -1;
        private bool runToggle;

        // Autopilot (Firebase Game Loop / local capture) writes these.
        public Vector2 AutoStick;
        public float AutoTurn;

        private float repeatTimer;
        private Ctl repeatCtl = Ctl.None;

        public bool MenuMode { get; private set; }
        public bool TitleMode { get; private set; }

        public TouchInput(Config config, RekkrApp app)
        {
            this.config = config;
            this.app = app;
            foreach (Ctl c in Enum.GetValues(typeof(Ctl)))
            {
                if (c == Ctl.None || c == Ctl.Stick || c == Ctl.Look || c == Ctl.Slot || c == Ctl.MenuTap) continue;
                buttons[c] = new TouchButton { Id = c, Icon = IconFor(c) };
            }
            runToggle = config.game_alwaysrun;
        }

        private static string IconFor(Ctl c)
        {
            switch (c)
            {
                case Ctl.Fire: return "fire";
                case Ctl.Use: return "use";
                case Ctl.WeaponNext: return "wnext";
                case Ctl.WeaponPrev: return "wprev";
                case Ctl.Map: return "map";
                case Ctl.Menu: return "menu";
                case Ctl.Run: return "run";
                case Ctl.Up: return "up";
                case Ctl.Down: return "down";
                case Ctl.Left: return "left";
                case Ctl.Right: return "right";
                case Ctl.Ok: return "ok";
                case Ctl.Back: return "back";
                case Ctl.Settings: return "settings";
                case Ctl.QuickSave: return "qsave";
                case Ctl.QuickLoad: return "qload";
                case Ctl.Continue: return "play";
                case Ctl.Jump: return "jump";
            }
            return null;
        }

        public IEnumerable<TouchButton> Buttons => buttons.Values;
        public TouchButton Get(Ctl c) => buttons[c];
        public bool RunOn => runToggle;
        public void SetRun(bool on) { runToggle = on; config.game_alwaysrun = on; }

        // ----------------------------------------------------------------- layout

        public bool EditMode;          // button layout editor
        public Ctl EditSelected = Ctl.None;
        public Rect EditToolbar;       // editor toolbar rect (touches there go to IMGUI)
        private Vector2 editGrab;
        private int editFinger = -1;

        public void Layout(Rect game, float scale, bool leftHanded)
        {
            float W = Screen.width, H = Screen.height;
            float u0 = Mathf.Min(W, H * 2.2F) / 1000F;          // unit ≈ 1/1000 of a 20:9 width
            float u = u0 * scale;
            float m = 18 * u0;
            var doom = app.Doom;
            TitleMode = doom != null && (doom.State == DoomState.Opening || doom.State == DoomState.DemoPlayback) && !doom.Menu.Active;
            MenuMode = doom == null || doom.Menu.Active;
            bool inGame = (!MenuMode && !TitleMode) || EditMode;
            if (EditMode) { MenuMode = false; TitleMode = false; }

            foreach (var b in buttons.Values) b.Visible = false;

            // Right cluster (dev4: + JUMP above USE, arc around ATTACK).
            float fr = 78 * u;
            var fire = Place(Ctl.Fire, new Vector2(W - m - fr - 22 * u, H - m - fr - 40 * u), fr, inGame);
            Place(Ctl.Use, fire.Center + new Vector2(-fr - 72 * u, 30 * u), 55 * u, inGame);
            Place(Ctl.Jump, fire.Center + new Vector2(-fr - 34 * u, -fr - 70 * u), 52 * u, inGame && RekkrSettings.Jump);
            Place(Ctl.WeaponNext, fire.Center + new Vector2(40 * u, -fr - 78 * u), 44 * u, inGame);
            Place(Ctl.WeaponPrev, fire.Center + new Vector2(-fr - 140 * u, -fr - 30 * u), 42 * u, inGame);
            Place(Ctl.Run, fire.Center + new Vector2(-fr - 205 * u, -20 * u), 40 * u, inGame);

            // Top corners: menu left; map + quick save/load right.
            Place(Ctl.Menu, new Vector2(m + 40 * u, m + 40 * u), 36 * u, !MenuMode);
            var map = Place(Ctl.Map, new Vector2(W - m - 40 * u, m + 40 * u), 38 * u, inGame);
            Place(Ctl.QuickLoad, map.Center + new Vector2(-98 * u, 0), 38 * u, inGame);
            Place(Ctl.QuickSave, map.Center + new Vector2(-196 * u, 0), 38 * u, inGame);

            // Menu pad: D-pad left, OK/BACK right.
            float pr = 50 * u;
            var pc = new Vector2(m + 3.2F * pr, H - m - 3.2F * pr);
            Place(Ctl.Up, pc + new Vector2(0, -2.05F * pr), pr, MenuMode);
            Place(Ctl.Down, pc + new Vector2(0, 2.05F * pr), pr, MenuMode);
            Place(Ctl.Left, pc + new Vector2(-2.05F * pr, 0), pr, MenuMode);
            Place(Ctl.Right, pc + new Vector2(2.05F * pr, 0), pr, MenuMode);
            Place(Ctl.Ok, new Vector2(W - m - 1.4F * fr, H - m - 1.6F * fr), fr * 0.95F, MenuMode);
            Place(Ctl.Back, new Vector2(W - m - 1.4F * fr - 2.3F * fr, H - m - 1.0F * fr), 58 * u, MenuMode);
            Place(Ctl.Settings, new Vector2(W - m - 40 * u0, m + 40 * u0), 36 * u0, MenuMode || TitleMode);

            // CONTINUE (latest quick/auto save) on the title screen and the main menu outside a level.
            var cont = Place(Ctl.Continue, new Vector2(W / 2, H * 0.74F), 34 * u0,
                !EditMode && app.CanContinue && (TitleMode || (MenuMode && !app.InLevel)));
            cont.PillWidth = 250 * u0;

            if (leftHanded)
            {
                foreach (var b in buttons.Values)
                {
                    if (b.Id == Ctl.Menu || b.Id == Ctl.Map || b.Id == Ctl.Settings || b.Id == Ctl.Continue ||
                        b.Id == Ctl.QuickSave || b.Id == Ctl.QuickLoad) continue;
                    b.Center.x = W - b.Center.x;
                }
            }

            StickRadius = 95 * u;
            StickHome = new Vector2(m + StickRadius + 70 * u, H - m - StickRadius - 50 * u);
            if (leftHanded) StickHome.x = W - StickHome.x;

            // Custom layout from the editor (absolute positions, per-button size).
            foreach (var kv in RekkrSettings.Layout)
            {
                var pos = new Vector2(kv.Value.pos.x * W, kv.Value.pos.y * H);
                if (kv.Key == Ctl.Stick)
                {
                    StickHome = pos; StickRadius *= kv.Value.scale;
                }
                else if (buttons.TryGetValue(kv.Key, out var cb))
                {
                    cb.Center = pos; cb.Radius *= kv.Value.scale;
                }
            }
            if (!StickActive)
            {
                StickCenter = StickHome;
                var target = StickHome + new Vector2(AutoStick.x, -AutoStick.y) * StickRadius;
                if (stickReturn > 0)
                {
                    stickReturn = Mathf.Max(0, stickReturn - Time.unscaledDeltaTime);
                    var k = stickReturn / 0.08F;
                    StickKnob = Vector2.Lerp(target, StickHome + stickReleaseKnob, k * k);
                }
                else StickKnob = target;
            }
        }

        private TouchButton Place(Ctl c, Vector2 center, float r, bool visible)
        {
            var b = buttons[c];
            b.Center = center; b.Radius = r; b.Visible = visible; b.PillWidth = 0;
            return b;
        }

        /// <summary>Default position + radius of an editable control (for the editor's RESET).</summary>
        public static bool IsEditable(Ctl c) => Array.IndexOf(RekkrSettings.Editable, c) >= 0;

        // ----------------------------------------------------------------- per-frame input

        public void Poll(bool leftHanded, bool settingsOpen)
        {
            var doom = app.Doom;
            if (doom == null) return;

            foreach (var b in buttons.Values) { b.Held = false; b.PressFlash = Mathf.Max(0, b.PressFlash - Time.unscaledDeltaTime); }

            // Fingers (and the mouse as finger 99 for desktop testing).
            var seen = new HashSet<int>();
            for (var i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                var p = new Vector2(t.position.x, Screen.height - t.position.y);
                bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                HandleFinger(t.fingerId, p, t.phase == TouchPhase.Began, ended, leftHanded, settingsOpen);
                if (!ended) seen.Add(t.fingerId);
            }
            if (!Application.isMobilePlatform && Input.touchCount == 0)
            {
                var mp = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (Input.GetMouseButtonDown(0)) HandleFinger(99, mp, true, false, leftHanded, settingsOpen);
                else if (Input.GetMouseButton(0)) HandleFinger(99, mp, false, false, leftHanded, settingsOpen);
                else if (Input.GetMouseButtonUp(0)) HandleFinger(99, mp, false, true, leftHanded, settingsOpen);
                if (Input.GetMouseButton(0)) seen.Add(99);
            }

            // Release fingers that vanished without an Ended phase.
            var stale = new List<int>();
            foreach (var kv in owner) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var f in stale) Release(f);

            foreach (var kv in owner) if (buttons.TryGetValue(kv.Value, out var hb) && !EditMode) hb.Held = true;

            // Gyro aim: angular velocity around the world "up" axis (from gravity), so it works in
            // either landscape orientation and however the phone is tilted.
            if (RekkrSettings.Gyro && !EditMode && !settingsOpen && !MenuMode && !TitleMode && SystemInfo.supportsGyroscope)
            {
                var g = Input.gyro;
                if (!g.enabled) g.enabled = true;
                var rate = g.rotationRateUnbiased;
                var grav = g.gravity;
                var yaw = grav.sqrMagnitude > 0.01F ? UnityEngine.Vector3.Dot(rate, -grav.normalized) : rate.x;
                if (RekkrSettings.GyroInvert) yaw = -yaw;
                if (Mathf.Abs(yaw) > 0.02F) gyroRadians += yaw * Time.unscaledDeltaTime;
                // dev4: pitch = rotation around the screen's horizontal axis (device ±Y in landscape).
                if (RekkrSettings.FreeLook)
                {
                    var right = Screen.orientation == ScreenOrientation.LandscapeRight ? UnityEngine.Vector3.up : UnityEngine.Vector3.down;
                    var pr = UnityEngine.Vector3.Dot(rate, right);
                    if (RekkrSettings.GyroInvert) pr = -pr;
                    if (Mathf.Abs(pr) > 0.02F) AddPitch(pr * Time.unscaledDeltaTime * Mathf.Rad2Deg * PitchUnitsPerDegree * (RekkrSettings.GyroSensitivity / 4F));
                }
            }
            if (AutoPitch != 0) AddPitch(AutoPitch * Time.unscaledDeltaTime);
            if (!RekkrSettings.FreeLook) pitch = 0;
            // Autopilot turning is fed like a finger swipe (640 units per tic at AutoTurn = 1), so
            // Test Lab runs exercise the same per-frame smooth-look path as a real player.
            if (AutoTurn != 0) turnPixels -= AutoTurn * 640F * 35F * Time.unscaledDeltaTime / SwipeUnitsPerPixel;
            if (AutoFire) buttons[Ctl.Fire].Held = true;
            if (AutoJump) buttons[Ctl.Jump].Held = true;
            if (AutoUse) buttons[Ctl.Use].Held = true;

            // Menu key auto-repeat for held arrows.
            if (repeatCtl != Ctl.None)
            {
                repeatTimer -= Time.unscaledDeltaTime;
                if (repeatTimer <= 0) { SendKey(KeyFor(repeatCtl)); repeatTimer = 0.11F; }
            }

            PollKeyboard();
        }

        private float gyroRadians;

        private void HandleFinger(int id, Vector2 p, bool began, bool ended, bool leftHanded, bool settingsOpen)
        {
            if (EditMode) { HandleEditFinger(id, p, began, ended); return; }
            if (began)
            {
                if (settingsOpen) return; // settings panel consumes touches (drawn with IMGUI)
                var ctl = HitTest(p, leftHanded);
                owner[id] = ctl;
                lastPos[id] = p;
                OnPress(ctl, id, p);
            }
            else if (owner.TryGetValue(id, out var ctl))
            {
                var last = lastPos[id];
                lastPos[id] = p;
                if (ctl == Ctl.Stick)
                {
                    // dev4: the base never follows the finger any more (fixed at home, or where the
                    // finger first landed in floating mode); the knob is clamped to the rim.
                    StickKnob = StickCenter + Vector2.ClampMagnitude(p - StickCenter, StickRadius);
                }
                else if (ctl == Ctl.Look || ctl == Ctl.Fire || ctl == Ctl.Jump)
                {
                    // Aim while firing (or jumping): that finger also turns and looks up/down.
                    turnPixels += p.x - last.x;
                    if (RekkrSettings.FreeLook)
                    {
                        var dy = last.y - p.y;               // finger up = look up
                        if (RekkrSettings.InvertLook) dy = -dy;
                        AddPitch(dy * PitchUnitsPerPixel);
                    }
                }
                if (ended) Release(id);
            }
        }

        private Ctl HitTest(Vector2 p, bool leftHanded)
        {
            TouchButton best = null;
            float bestD = float.MaxValue;
            foreach (var b in buttons.Values)
            {
                if (!b.Visible) continue;
                var d = (p - b.Center).magnitude / b.Radius;
                if (d < 1.15F && d < bestD) { best = b; bestD = d; }
            }
            var cont = buttons[Ctl.Continue];
            if (cont.Visible && cont.PillRect.Contains(p)) return Ctl.Continue;
            if (best != null) return best.Id;
            if (TitleMode) return Ctl.Ok; // tap anywhere on the title to open the menu
            if (MenuMode) return app.MenuTapAt(p, false) ? Ctl.MenuTap : Ctl.None;   // dev4: tap a menu line
            // Tap a weapon number on the status bar / fullscreen HUD to select it.
            var slot = app.ArmsSlotAt(p);
            if (slot >= 0) { pendingSlot = slot; return Ctl.Slot; }
            bool leftSide = p.x < Screen.width * 0.45F;
            if (leftHanded) leftSide = !leftSide;
            // dev4 fixed stick: only touches near the stick grab it; the rest of the left side looks.
            if (leftSide && RekkrSettings.StickMode == 0 && (p - StickHome).magnitude > StickRadius * 2.1F) return Ctl.Look;
            return leftSide ? Ctl.Stick : Ctl.Look;
        }

        private void OnPress(Ctl ctl, int finger, Vector2 p)
        {
            switch (ctl)
            {
                case Ctl.Stick:
                    stickFinger = finger;
                    StickActive = true;
                    stickReturn = 0;
                    if (RekkrSettings.StickMode == 0)
                    {
                        // Fixed: the base stays at home; the knob jumps to the finger (clamped).
                        StickCenter = StickHome;
                        StickKnob = StickHome + Vector2.ClampMagnitude(p - StickHome, StickRadius);
                    }
                    else
                    {
                        StickCenter = p;
                        StickKnob = p;
                    }
                    break;
                case Ctl.Look:
                    // Double-tap the look area: centre the view vertically.
                    if (Time.unscaledTime - lastLookTap < 0.3F && RekkrSettings.FreeLook) { pitch = 0; lastLookTap = -10; }
                    else lastLookTap = Time.unscaledTime;
                    break;
                case Ctl.Jump: Flash(ctl); break;
                case Ctl.MenuTap: app.MenuTapAt(p, true); break;
                case Ctl.WeaponNext: weaponRequest = NextWeapon(+1); Flash(ctl); break;
                case Ctl.WeaponPrev: weaponRequest = NextWeapon(-1); Flash(ctl); break;
                case Ctl.Run: runToggle = !runToggle; config.game_alwaysrun = runToggle; Flash(ctl); break;
                case Ctl.Settings: app.ToggleSettings(); Flash(ctl); break;
                case Ctl.QuickSave: app.QuickSave(); Flash(ctl); break;
                case Ctl.QuickLoad: app.QuickLoad(); Flash(ctl); break;
                case Ctl.Continue: app.Continue(); Flash(ctl); break;
                case Ctl.Slot:
                    if (pendingSlot >= 0) { slotRequest = pendingSlot + 1; Haptics.Pulse(10, 80); }
                    pendingSlot = -1;
                    break;
                case Ctl.Map:
                case Ctl.Menu:
                case Ctl.Up:
                case Ctl.Down:
                case Ctl.Left:
                case Ctl.Right:
                case Ctl.Ok:
                case Ctl.Back:
                    if (ctl == Ctl.Ok && TitleMode) { SendKey(DoomKey.Escape); Flash(ctl); break; }
                    SendKey(KeyFor(ctl));
                    Flash(ctl);
                    if (ctl == Ctl.Up || ctl == Ctl.Down || ctl == Ctl.Left || ctl == Ctl.Right)
                    {
                        repeatCtl = ctl; repeatTimer = 0.38F;
                    }
                    break;
            }
        }

        private void Release(int finger)
        {
            if (owner.TryGetValue(finger, out var ctl))
            {
                if (ctl == Ctl.Stick && finger == stickFinger)
                {
                    StickActive = false; stickFinger = -1;
                    stickReleaseKnob = Vector2.ClampMagnitude(StickKnob - StickCenter, StickRadius);
                    stickReturn = 0.08F;
                    StickCenter = StickHome;
                }
                if (ctl == repeatCtl) repeatCtl = Ctl.None;
            }
            owner.Remove(finger);
            lastPos.Remove(finger);
        }

        private void Flash(Ctl c) { if (buttons.TryGetValue(c, out var b)) b.PressFlash = 0.18F; }

        /// <summary>Autopilot / tests: press a control exactly like a tap.</summary>
        public void Tap(Ctl c) { OnPress(c, -100 - (int)c, buttons.ContainsKey(c) ? buttons[c].Center : Vector2.zero); owner.Remove(-100 - (int)c); if (c == repeatCtl) repeatCtl = Ctl.None; }

        public bool AutoFire;
        public bool AutoUse;
        public bool AutoJump;
        private int pendingSlot = -1;
        private int slotRequest = -1;

        /// <summary>Autopilot: select weapon cell 0..5 (weapons 2..7) as if its number was tapped.</summary>
        public void TapSlot(int cell) { slotRequest = cell + 1; }

        // ----------------------------------------------------------------- layout editor

        private void HandleEditFinger(int id, Vector2 p, bool began, bool ended)
        {
            if (began)
            {
                if (EditToolbar.Contains(p)) return;
                var hit = EditHitTest(p);
                if (hit != Ctl.None)
                {
                    EditSelected = hit;
                    editFinger = id;
                    editGrab = EditCenter(hit) - p;
                }
                return;
            }
            if (id != editFinger) return;
            if (EditSelected != Ctl.None)
            {
                var c = p + editGrab;
                c.x = Mathf.Clamp(c.x, 0, Screen.width); c.y = Mathf.Clamp(c.y, 0, Screen.height);
                SetLayoutPos(EditSelected, c);
            }
            if (ended) editFinger = -1;
        }

        public Ctl EditHitTest(Vector2 p)
        {
            Ctl best = Ctl.None; float bestD = 1.2F;
            foreach (var c in RekkrSettings.Editable)
            {
                var d = (p - EditCenter(c)).magnitude / Mathf.Max(1, EditRadius(c));
                if (d < bestD) { best = c; bestD = d; }
            }
            return best;
        }

        public Vector2 EditCenter(Ctl c) => c == Ctl.Stick ? StickHome : buttons[c].Center;
        public float EditRadius(Ctl c) => c == Ctl.Stick ? StickRadius : buttons[c].Radius;

        private void SetLayoutPos(Ctl c, Vector2 screenPos)
        {
            var k = RekkrSettings.Layout.TryGetValue(c, out var v) ? v.scale : 1F;
            RekkrSettings.Layout[c] = (new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height), k);
        }

        public void ScaleSelected(float delta)
        {
            if (EditSelected == Ctl.None) return;
            var c = EditSelected;
            var pos = EditCenter(c);
            var k = RekkrSettings.Layout.TryGetValue(c, out var v) ? v.scale : 1F;
            k = Mathf.Clamp(k + delta, 0.6F, 1.8F);
            RekkrSettings.Layout[c] = (new Vector2(pos.x / Screen.width, pos.y / Screen.height), k);
        }

        public float SelectedScale => EditSelected != Ctl.None && RekkrSettings.Layout.TryGetValue(EditSelected, out var v) ? v.scale : 1F;

        private static DoomKey KeyFor(Ctl c)
        {
            switch (c)
            {
                case Ctl.Map: return DoomKey.Tab;
                case Ctl.Menu: return DoomKey.Escape;
                case Ctl.Up: return DoomKey.Up;
                case Ctl.Down: return DoomKey.Down;
                case Ctl.Left: return DoomKey.Left;
                case Ctl.Right: return DoomKey.Right;
                case Ctl.Ok: return DoomKey.Enter;
                case Ctl.Back: return DoomKey.Escape;
            }
            return DoomKey.Unknown;
        }

        private void SendKey(DoomKey key)
        {
            if (key == DoomKey.Unknown) return;
            app.Doom.PostEvent(new DoomEvent(EventType.KeyDown, key));
            app.Doom.PostEvent(new DoomEvent(EventType.KeyUp, key));
        }

        // Doom slot order; slot 1 holds fist+chainsaw, slot 3 shotgun (+SSG in Doom II).
        private static readonly WeaponType[] cycle =
        {
            WeaponType.Fist, WeaponType.Chainsaw, WeaponType.Pistol, WeaponType.Shotgun,
            WeaponType.Chaingun, WeaponType.Missile, WeaponType.Plasma, WeaponType.Bfg
        };

        private int NextWeapon(int dir)
        {
            var game = app.Doom?.Game;
            if (game == null || game.World == null) return -1;
            var p = game.World.ConsolePlayer;
            var current = p.PendingWeapon != WeaponType.NoChange ? p.PendingWeapon : p.ReadyWeapon;
            var idx = Array.IndexOf(cycle, current);
            for (var k = 1; k <= cycle.Length; k++)
            {
                var w = cycle[((idx + dir * k) % cycle.Length + cycle.Length) % cycle.Length];
                if (!p.WeaponOwned[(int)w]) continue;
                var ammo = DoomInfo.WeaponInfos[(int)w].Ammo;
                if (ammo != AmmoType.NoAmmo && p.Ammo[(int)ammo] <= 0) continue;
                if (w == current) return -1;
                // Chainsaw shares slot 1 with the fist: vanilla toggles between them on "1".
                return (int)w;
            }
            return -1;
        }

        private static int SlotOf(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Fist:
                case WeaponType.Chainsaw: return 0;
                case WeaponType.Pistol: return 1;
                case WeaponType.Shotgun:
                case WeaponType.SuperShotgun: return 2;
                case WeaponType.Chaingun: return 3;
                case WeaponType.Missile: return 4;
                case WeaponType.Plasma: return 5;
                case WeaponType.Bfg: return 6;
            }
            return 0;
        }

        // ----------------------------------------------------------------- keyboard / gamepad

        private static readonly (KeyCode, DoomKey)[] keyMap =
        {
            (KeyCode.UpArrow, DoomKey.Up), (KeyCode.DownArrow, DoomKey.Down), (KeyCode.LeftArrow, DoomKey.Left),
            (KeyCode.RightArrow, DoomKey.Right), (KeyCode.Return, DoomKey.Enter), (KeyCode.Escape, DoomKey.Escape),
            (KeyCode.Tab, DoomKey.Tab), (KeyCode.Y, DoomKey.Y), (KeyCode.N, DoomKey.N), (KeyCode.Backspace, DoomKey.Backspace),
            (KeyCode.JoystickButton0, DoomKey.Enter), (KeyCode.JoystickButton1, DoomKey.Escape), (KeyCode.JoystickButton7, DoomKey.Escape),
        };

        private void PollKeyboard()
        {
            foreach (var (kc, dk) in keyMap)
            {
                if (Input.GetKeyDown(kc)) app.Doom.PostEvent(new DoomEvent(EventType.KeyDown, dk));
                if (Input.GetKeyUp(kc)) app.Doom.PostEvent(new DoomEvent(EventType.KeyUp, dk));
            }
        }

        // ----------------------------------------------------------------- TicCmd

        /// <summary>True if the last tic turned with keys/gamepad (then the renderer keeps the classic
        /// interpolated angle, which is smooth for constant-rate turning).</summary>
        public bool LastTicKeyTurn { get; private set; }

        // dev4 free look. Pitch units are 200-line screen units (slope = pitch/160). Near the centre one
        // degree ≈ 160·π/180 ≈ 2.8 units; swipe speed matches ~60 % of the horizontal look speed.
        private const float PitchUnitsPerDegree = 2.79F;
        private float PitchUnitsPerPixel => 180F / (0.30F * Screen.width) * PitchUnitsPerDegree * 0.6F * (RekkrSettings.LookSensitivity / 5F);
        private void AddPitch(float d) { pitch = Mathf.Clamp(pitch + d, -TicCmdExt.MaxPitch, TicCmdExt.MaxPitch); }

        private float SwipeUnitsPerPixel => 32768F / (0.30F * Screen.width) * (RekkrSettings.LookSensitivity / 5F);
        private static float GyroUnitsPerRadian => (65536F / (2F * Mathf.PI)) * (RekkrSettings.GyroSensitivity / 4F);

        /// <summary>Turn (Doom BAM) input since the last tic that the next tic will apply — the same
        /// arithmetic as BuildTicCmd, without consuming it. Used for per-frame "smooth look".</summary>
        public Angle PendingTurn
        {
            get
            {
                var turn = -turnPixels * SwipeUnitsPerPixel + turnCarry + gyroRadians * GyroUnitsPerRadian;
                var units = Mathf.Clamp(Mathf.RoundToInt(turn), -32000, 32000);
                return new Angle((uint)(units << 16));
            }
        }

        public void BuildTicCmd(TicCmd cmd)
        {
            cmd.Clear();
            var speed = runToggle ? 1 : 0;

            // Analog stick (touch or autopilot), x = strafe right, y = forward.
            // dev4: radial dead zone 12 %, full speed at 92 %, mild curve for fine walking.
            var s = StickActive
                ? new Vector2((StickKnob.x - StickCenter.x) / Mathf.Max(1, StickRadius), (StickCenter.y - StickKnob.y) / Mathf.Max(1, StickRadius))
                : AutoStick;
            var mag = s.magnitude;
            if (mag < 0.12F) s = Vector2.zero;
            else if (StickActive)
            {
                var t = Mathf.Clamp01((mag - 0.12F) / (0.92F - 0.12F));
                s = s / mag * Mathf.Pow(t, 1.35F);
            }

            var forward = Mathf.RoundToInt(s.y * PlayerBehavior.ForwardMove[speed] * 1.05F);
            var side = Mathf.RoundToInt(s.x * PlayerBehavior.SideMove[speed] * 1.1F);

            // Keyboard / gamepad movement.
            float kx = 0, ky = 0;
            if (Input.GetKey(KeyCode.W)) ky += 1;
            if (Input.GetKey(KeyCode.S)) ky -= 1;
            if (Input.GetKey(KeyCode.A)) kx -= 1;
            if (Input.GetKey(KeyCode.D)) kx += 1;
            forward += Mathf.RoundToInt(ky * PlayerBehavior.ForwardMove[speed]);
            side += Mathf.RoundToInt(kx * PlayerBehavior.SideMove[speed]);
            if (Input.GetKey(KeyCode.Q)) cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[speed];
            if (Input.GetKey(KeyCode.E)) cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[speed];
            LastTicKeyTurn = cmd.AngleTurn != 0;

            // Swipe to turn. Sensitivity 5 ≈ a 30%-of-screen swipe turns 180°.
            var sens = RekkrSettings.LookSensitivity;
            var perPixel = 32768F / (0.30F * Screen.width) * (sens / 5F);
            var turn = -turnPixels * perPixel + turnCarry;
            turnPixels = 0;
            // Gyro: radians -> Doom angle units (65536 per turn) x sensitivity (4 = 1:1).
            turn += gyroRadians * (65536F / (2F * Mathf.PI)) * (RekkrSettings.GyroSensitivity / 4F);
            gyroRadians = 0;
            var turnInt = Mathf.Clamp(Mathf.RoundToInt(turn), -32000, 32000);
            turnCarry = turn - turnInt;
            cmd.AngleTurn += (short)turnInt;

            if (buttons[Ctl.Fire].Held || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.JoystickButton5)) cmd.Buttons |= TicCmdButtons.Attack;
            if (buttons[Ctl.Use].Held || Input.GetKey(KeyCode.F) || Input.GetKey(KeyCode.JoystickButton2)) cmd.Buttons |= TicCmdButtons.Use;

            // dev4 extensions (never in demos): free-look pitch, aim mode, jump.
            if (RekkrSettings.FreeLook)
            {
                cmd.LookPitch = (short)PitchInt;
                if (!RekkrSettings.AutoAim) cmd.Ext |= TicCmdExt.NoAutoAim;
            }
            if (RekkrSettings.Jump && (buttons[Ctl.Jump].Held || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton0)))
            {
                cmd.Ext |= TicCmdExt.Jump;
            }

            for (var i = 0; i < 7; i++)
            {
                if (Input.GetKey(KeyCode.Alpha1 + i)) { cmd.Buttons |= TicCmdButtons.Change; cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift); break; }
            }
            if (slotRequest >= 0)
            {
                cmd.Buttons |= TicCmdButtons.Change;
                cmd.Buttons |= (byte)(slotRequest << TicCmdButtons.WeaponShift);
                slotRequest = -1;
            }
            else if (weaponRequest >= 0)
            {
                cmd.Buttons |= TicCmdButtons.Change;
                cmd.Buttons |= (byte)(SlotOf((WeaponType)weaponRequest) << TicCmdButtons.WeaponShift);
                weaponRequest = -1;
            }

            forward = Mathf.Clamp(forward, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
            side = Mathf.Clamp(side, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
            cmd.ForwardMove += (sbyte)forward;
            cmd.SideMove += (sbyte)side;
        }

        public void Reset()
        {
            turnPixels = 0; turnCarry = 0; weaponRequest = -1; slotRequest = -1; gyroRadians = 0; pitch = 0;
        }

        public void GrabMouse() { }
        public void ReleaseMouse() { }

        public int MaxMouseSensitivity => 15;
        public int MouseSensitivity { get => config.mouse_sensitivity; set => config.mouse_sensitivity = value; }
    }
}
