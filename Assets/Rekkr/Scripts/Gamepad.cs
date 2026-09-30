// my-rekkr dev7 — full gamepad: both analog sticks (move / look), triggers, d-pad, and a remap flow that
// learns the player's own controller ("push the move stick right", "press the button for FIRE"...).
// Unity legacy input: joystick axes 1..12 are the InputManager entries "RekkrAxis1".."RekkrAxis12"
// (ProjectSettings/InputManager.asset), buttons are KeyCode.JoystickButton0..19.
// Bindings are strings in PlayerPrefs: "b5" = button 5, "a9+" / "a9-" = axis 9 pushed positive/negative.
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Collections.Generic;
using UnityEngine;

namespace ManagedDoom.UnityPort
{
    public enum PadAction { Fire, Use, Jump, Run, WeaponNext, WeaponPrev, Map, Menu }

    public static class Gamepad
    {
        public const int Axes = 12, Buttons = 20;
        public const float Dead = 0.18F;

        public struct AxisBind { public int Axis; public float Sign; }
        public static AxisBind MoveX, MoveY, LookX, LookY;
        public static readonly Dictionary<PadAction, List<string>> Binds = new Dictionary<PadAction, List<string>>();
        public static int LookSpeed = 5;      // 1..10
        public static bool InvertLookY;

        private static readonly float[] axis = new float[Axes];
        private static readonly Dictionary<PadAction, bool> held = new Dictionary<PadAction, bool>();
        private static readonly Dictionary<PadAction, bool> prevHeld = new Dictionary<PadAction, bool>();
        private static bool axesOk = true;
        public static string Name { get; private set; } = "";
        public static bool Connected => Name.Length > 0;

        static Gamepad() { Defaults(); }

        public static void Defaults()
        {
            // Android + most XInput-style pads: left stick axes 1/2, right stick 3/4 (Y down = positive)
            MoveX = new AxisBind { Axis = 0, Sign = 1 }; MoveY = new AxisBind { Axis = 1, Sign = -1 };
            LookX = new AxisBind { Axis = 2, Sign = 1 }; LookY = new AxisBind { Axis = 3, Sign = -1 };
            Binds.Clear();
            Binds[PadAction.Fire] = new List<string> { "b5", "b7" };        // R1 (+ R2 on pads that report it as a button)
            Binds[PadAction.Use] = new List<string> { "b2" };               // X
            Binds[PadAction.Jump] = new List<string> { "b0" };              // A
            Binds[PadAction.Run] = new List<string> { "b4" };               // L1 (toggle)
            Binds[PadAction.WeaponNext] = new List<string> { "b3" };        // Y
            Binds[PadAction.WeaponPrev] = new List<string> { "b1" };        // B
            Binds[PadAction.Map] = new List<string> { "b11", "b6" };        // Select / Back
            Binds[PadAction.Menu] = new List<string> { "b10" };             // Start
        }

        public static void Load()
        {
            Defaults();
            MoveX = LoadAxis("pad_mx", MoveX); MoveY = LoadAxis("pad_my", MoveY);
            LookX = LoadAxis("pad_lx", LookX); LookY = LoadAxis("pad_ly", LookY);
            foreach (PadAction a in System.Enum.GetValues(typeof(PadAction)))
            {
                var s = PlayerPrefs.GetString("pad_" + a, "");
                if (s.Length > 0) Binds[a] = new List<string>(s.Split(','));
            }
            LookSpeed = Mathf.Clamp(PlayerPrefs.GetInt("pad_look", 5), 1, 10);
            InvertLookY = PlayerPrefs.GetInt("pad_inv", 0) == 1;
        }

        public static void Save(System.Action<string, string> setS, System.Action<string, int> setI)
        {
            setS("pad_mx", AxisStr(MoveX)); setS("pad_my", AxisStr(MoveY));
            setS("pad_lx", AxisStr(LookX)); setS("pad_ly", AxisStr(LookY));
            foreach (var kv in Binds) setS("pad_" + kv.Key, string.Join(",", kv.Value));
            setI("pad_look", LookSpeed);
            setI("pad_inv", InvertLookY ? 1 : 0);
        }

        private static string AxisStr(AxisBind b) => "a" + b.Axis + (b.Sign >= 0 ? "+" : "-");

        private static AxisBind LoadAxis(string key, AxisBind def)
        {
            var s = PlayerPrefs.GetString(key, "");
            if (s.Length < 3 || s[0] != 'a' || !int.TryParse(s.Substring(1, s.Length - 2), out var i)) return def;
            return new AxisBind { Axis = Mathf.Clamp(i, 0, Axes - 1), Sign = s[s.Length - 1] == '-' ? -1 : 1 };
        }

        public static float Raw(int i) => i >= 0 && i < Axes ? axis[i] : 0F;

        /// <summary>Reads all axes and actions once per frame.</summary>
        public static void Poll()
        {
            var names = Input.GetJoystickNames();
            Name = "";
            foreach (var n in names) if (!string.IsNullOrEmpty(n)) { Name = n.Trim(); break; }
            for (var i = 0; i < Axes; i++)
            {
                if (!axesOk) { axis[i] = 0; continue; }
                try { axis[i] = Input.GetAxisRaw("RekkrAxis" + (i + 1)); }
                catch (System.ArgumentException) { axesOk = false; axis[i] = 0; Debug.LogWarning("[REKKR] InputManager has no RekkrAxis entries"); }
            }
            foreach (var kv in Binds)
            {
                prevHeld[kv.Key] = held.TryGetValue(kv.Key, out var h) && h;
                var on = false;
                foreach (var b in kv.Value) on |= BindHeld(b);
                held[kv.Key] = on;
            }
        }

        public static bool BindHeld(string b)
        {
            if (string.IsNullOrEmpty(b)) return false;
            if (b[0] == 'b' && int.TryParse(b.Substring(1), out var btn) && btn >= 0 && btn < Buttons)
                return Input.GetKey(KeyCode.JoystickButton0 + btn);
            if (b[0] == 'a' && b.Length >= 3 && int.TryParse(b.Substring(1, b.Length - 2), out var ax))
                return Raw(ax) * (b[b.Length - 1] == '-' ? -1 : 1) > 0.5F;
            return false;
        }

        public static bool Held(PadAction a) => held.TryGetValue(a, out var h) && h;
        public static bool Down(PadAction a) => Held(a) && !(prevHeld.TryGetValue(a, out var p) && p);

        /// <summary>Stick value with a radial dead zone and a mild response curve.</summary>
        public static Vector2 Stick(AxisBind x, AxisBind y)
        {
            var v = new Vector2(Raw(x.Axis) * x.Sign, Raw(y.Axis) * y.Sign);
            var m = v.magnitude;
            if (m < Dead) return Vector2.zero;
            var t = Mathf.Clamp01((m - Dead) / (0.95F - Dead));
            return v / m * Mathf.Pow(t, 1.5F);
        }

        public static Vector2 Move => Stick(MoveX, MoveY);
        public static Vector2 Look => Stick(LookX, LookY);

        // ------------------------------------------------------------------ remap flow
        public enum Step { MoveX, MoveY, LookX, LookY, Fire, Use, Jump, Run, WeaponNext, WeaponPrev, Map, Menu, Done }
        public static bool Capturing { get; private set; }
        public static Step Current { get; private set; }
        private static float stepStart;
        private static bool waitRelease;
        private static readonly float[] rest = new float[Axes];

        public static void StartCapture()
        {
            Capturing = true; Current = Step.MoveX; stepStart = Time.unscaledTime; waitRelease = true;
            for (var i = 0; i < Axes; i++) rest[i] = Raw(i);
        }

        public static void StopCapture() { Capturing = false; }

        /// <summary>Seconds left before the current step is skipped (keeps the old binding).</summary>
        public static float StepTimeLeft => Mathf.Max(0, 7F - (Time.unscaledTime - stepStart));

        private static bool AnyInput()
        {
            for (var i = 0; i < Buttons; i++) if (Input.GetKey(KeyCode.JoystickButton0 + i)) return true;
            for (var i = 0; i < Axes; i++) if (Mathf.Abs(Raw(i) - rest[i]) > 0.35F) return true;
            return false;
        }

        /// <summary>Call every frame while capturing (after Poll).</summary>
        public static void UpdateCapture()
        {
            if (!Capturing) return;
            if (waitRelease) { if (!AnyInput()) { waitRelease = false; stepStart = Time.unscaledTime; } return; }
            if (StepTimeLeft <= 0) { Advance(); return; }
            var stickStep = Current <= Step.LookY;
            // axes: the one moved furthest from rest
            int best = -1; float bestD = 0.6F;
            for (var i = 0; i < Axes; i++)
            {
                if (!stickStep && IsStickAxis(i)) continue;
                var d = Mathf.Abs(Raw(i) - rest[i]);
                if (d > bestD) { bestD = d; best = i; }
            }
            if (stickStep)
            {
                if (best < 0) return;
                var b = new AxisBind { Axis = best, Sign = Raw(best) - rest[best] >= 0 ? 1 : -1 };
                // "push RIGHT" gives +x; "push UP" gives +forward / look up
                if (Current == Step.MoveX) MoveX = b; else if (Current == Step.MoveY) MoveY = b;
                else if (Current == Step.LookX) LookX = b; else LookY = b;
                Advance();
                return;
            }
            string bind = null;
            for (var i = 0; i < Buttons && bind == null; i++) if (Input.GetKey(KeyCode.JoystickButton0 + i)) bind = "b" + i;
            if (bind == null && best >= 0) bind = "a" + best + (Raw(best) >= 0 ? "+" : "-");   // trigger as a button
            if (bind == null) return;
            var action = (PadAction)(Current - Step.Fire);
            // one control per action: drop it from every other action first
            foreach (var kv in Binds) kv.Value.Remove(bind);
            Binds[action] = new List<string> { bind };
            Advance();
        }

        private static bool IsStickAxis(int i) => i == MoveX.Axis || i == MoveY.Axis || i == LookX.Axis || i == LookY.Axis;

        private static void Advance()
        {
            Current++;
            waitRelease = true;
            stepStart = Time.unscaledTime;
            if (Current == Step.Done) Capturing = false;
        }
    }
}
