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
        Up, Down, Left, Right, Ok, Back, Settings
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

        public bool Contains(Vector2 p, float slop) => (p - Center).sqrMagnitude <= (Radius * slop) * (Radius * slop);
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
                if (c == Ctl.None || c == Ctl.Stick || c == Ctl.Look) continue;
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
            }
            return null;
        }

        public IEnumerable<TouchButton> Buttons => buttons.Values;
        public TouchButton Get(Ctl c) => buttons[c];
        public bool RunOn => runToggle;

        // ----------------------------------------------------------------- layout

        public void Layout(Rect game, float scale, bool leftHanded)
        {
            float W = Screen.width, H = Screen.height;
            float u = Mathf.Min(W, H * 2.2F) / 1000F * scale; // unit ≈ 1/1000 of a 20:9 width
            float m = 18 * u;
            var doom = app.Doom;
            TitleMode = doom != null && (doom.State == DoomState.Opening || doom.State == DoomState.DemoPlayback) && !doom.Menu.Active;
            MenuMode = doom == null || doom.Menu.Active;
            bool inGame = !MenuMode && !TitleMode;

            foreach (var b in buttons.Values) b.Visible = false;

            // Right cluster.
            float fr = 78 * u;
            var fire = Place(Ctl.Fire, new Vector2(W - m - fr - 22 * u, H - m - fr - 40 * u), fr, inGame);
            Place(Ctl.Use, fire.Center + new Vector2(-fr - 70 * u, 28 * u), 55 * u, inGame);
            Place(Ctl.WeaponNext, fire.Center + new Vector2(18 * u, -fr - 62 * u), 44 * u, inGame);
            Place(Ctl.WeaponPrev, fire.Center + new Vector2(-fr - 60 * u, -fr - 30 * u), 44 * u, inGame);
            Place(Ctl.Run, fire.Center + new Vector2(-fr - 150 * u, -40 * u), 40 * u, inGame);

            // Top corners.
            Place(Ctl.Menu, new Vector2(m + 40 * u, m + 40 * u), 36 * u, !MenuMode);
            Place(Ctl.Map, new Vector2(W - m - 40 * u, m + 40 * u), 38 * u, inGame);

            // Menu pad: D-pad left, OK/BACK right.
            float pr = 50 * u;
            var pc = new Vector2(m + 3.2F * pr, H - m - 3.2F * pr);
            Place(Ctl.Up, pc + new Vector2(0, -2.05F * pr), pr, MenuMode);
            Place(Ctl.Down, pc + new Vector2(0, 2.05F * pr), pr, MenuMode);
            Place(Ctl.Left, pc + new Vector2(-2.05F * pr, 0), pr, MenuMode);
            Place(Ctl.Right, pc + new Vector2(2.05F * pr, 0), pr, MenuMode);
            Place(Ctl.Ok, new Vector2(W - m - 1.4F * fr, H - m - 1.6F * fr), fr * 0.95F, MenuMode);
            Place(Ctl.Back, new Vector2(W - m - 1.4F * fr - 2.3F * fr, H - m - 1.0F * fr), 58 * u, MenuMode);
            Place(Ctl.Settings, new Vector2(W - m - 40 * u, m + 40 * u), 36 * u, MenuMode || TitleMode);

            if (leftHanded)
            {
                foreach (var b in buttons.Values)
                {
                    if (b.Id == Ctl.Menu || b.Id == Ctl.Map || b.Id == Ctl.Settings) continue;
                    b.Center.x = W - b.Center.x;
                }
            }

            StickRadius = 95 * u;
            StickHome = new Vector2(m + StickRadius + 70 * u, H - m - StickRadius - 50 * u);
            if (leftHanded) StickHome.x = W - StickHome.x;
            if (!StickActive)
            {
                StickCenter = StickHome;
                StickKnob = StickHome + new Vector2(AutoStick.x, -AutoStick.y) * StickRadius;
            }
        }

        private TouchButton Place(Ctl c, Vector2 center, float r, bool visible)
        {
            var b = buttons[c];
            b.Center = center; b.Radius = r; b.Visible = visible;
            return b;
        }

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

            foreach (var kv in owner) if (buttons.TryGetValue(kv.Value, out var hb)) hb.Held = true;
            if (AutoFire) buttons[Ctl.Fire].Held = true;
            if (AutoUse) buttons[Ctl.Use].Held = true;

            // Menu key auto-repeat for held arrows.
            if (repeatCtl != Ctl.None)
            {
                repeatTimer -= Time.unscaledDeltaTime;
                if (repeatTimer <= 0) { SendKey(KeyFor(repeatCtl)); repeatTimer = 0.11F; }
            }

            PollKeyboard();
        }

        private void HandleFinger(int id, Vector2 p, bool began, bool ended, bool leftHanded, bool settingsOpen)
        {
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
                    var d = p - StickCenter;
                    // Floating stick: drag the base along if the finger runs past the rim.
                    if (d.magnitude > StickRadius) StickCenter += d - d.normalized * StickRadius;
                    StickKnob = StickCenter + Vector2.ClampMagnitude(p - StickCenter, StickRadius);
                }
                else if (ctl == Ctl.Look || ctl == Ctl.Fire)
                {
                    // Aim while firing: the attack finger also turns.
                    turnPixels += p.x - last.x;
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
            if (best != null) return best.Id;
            if (TitleMode) return Ctl.Ok; // tap anywhere on the title to open the menu
            if (MenuMode) return Ctl.None;
            bool leftSide = p.x < Screen.width * 0.45F;
            if (leftHanded) leftSide = !leftSide;
            return leftSide ? Ctl.Stick : Ctl.Look;
        }

        private void OnPress(Ctl ctl, int finger, Vector2 p)
        {
            switch (ctl)
            {
                case Ctl.Stick:
                    stickFinger = finger;
                    StickActive = true;
                    StickCenter = p;
                    StickKnob = p;
                    break;
                case Ctl.WeaponNext: weaponRequest = NextWeapon(+1); Flash(ctl); break;
                case Ctl.WeaponPrev: weaponRequest = NextWeapon(-1); Flash(ctl); break;
                case Ctl.Run: runToggle = !runToggle; config.game_alwaysrun = runToggle; Flash(ctl); break;
                case Ctl.Settings: app.ToggleSettings(); Flash(ctl); break;
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
                    StickCenter = StickHome; StickKnob = StickHome;
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

        public void BuildTicCmd(TicCmd cmd)
        {
            cmd.Clear();
            var speed = runToggle ? 1 : 0;

            // Analog stick (touch or autopilot), x = strafe right, y = forward; small dead zone.
            var s = StickActive
                ? new Vector2((StickKnob.x - StickCenter.x) / Mathf.Max(1, StickRadius), (StickCenter.y - StickKnob.y) / Mathf.Max(1, StickRadius))
                : AutoStick;
            if (s.magnitude < 0.12F) s = Vector2.zero;

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

            // Swipe to turn. Sensitivity 5 ≈ a 30%-of-screen swipe turns 180°.
            var sens = app.LookSensitivity;
            var perPixel = 32768F / (0.30F * Screen.width) * (sens / 5F);
            var turn = -turnPixels * perPixel + turnCarry - AutoTurn * 640F;
            turnPixels = 0;
            var turnInt = Mathf.Clamp(Mathf.RoundToInt(turn), -32000, 32000);
            turnCarry = turn - turnInt;
            cmd.AngleTurn += (short)turnInt;

            if (buttons[Ctl.Fire].Held || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.JoystickButton5)) cmd.Buttons |= TicCmdButtons.Attack;
            if (buttons[Ctl.Use].Held || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton2)) cmd.Buttons |= TicCmdButtons.Use;

            for (var i = 0; i < 7; i++)
            {
                if (Input.GetKey(KeyCode.Alpha1 + i)) { cmd.Buttons |= TicCmdButtons.Change; cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift); break; }
            }
            if (weaponRequest >= 0)
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
            turnPixels = 0; turnCarry = 0; weaponRequest = -1;
        }

        public void GrabMouse() { }
        public void ReleaseMouse() { }

        public int MaxMouseSensitivity => 15;
        public int MouseSensitivity { get => config.mouse_sensitivity; set => config.mouse_sensitivity = value; }
    }
}
