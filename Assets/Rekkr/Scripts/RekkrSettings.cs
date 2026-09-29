// my-rekkr — app-side settings (touch, gyro, haptics, display, audio, language, button layout),
// stored in PlayerPrefs. Engine settings (volume, gamma, screen size) stay in the Doom config.
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ManagedDoom.UnityPort
{
    public static class RekkrSettings
    {
        public static int LookSensitivity = 5;   // 1..10
        public static int ControlsScale = 100;   // 70..140 %
        public static int ControlsOpacity = 75;  // 30..100 %
        public static bool LeftHanded;
        public static bool Gyro;
        public static int GyroSensitivity = 4;   // 1..10 (4 = 1:1 with the real rotation)
        public static bool GyroInvert;
        public static bool Haptics = true;
        public static int FpsMode;               // 0 auto, 60, 90, 120
        public static bool Widescreen = true;
        public static bool ShowFps;
        public static bool MusicHQ = true;       // GeneralUser GS vs TimGM6mb
        public static bool Arabic;
        public static bool SmoothLook = true;    // dev3: touch/gyro look applied every frame

        /// <summary>Custom button placement: centre as a fraction of the screen + size multiplier.</summary>
        public static readonly Dictionary<Ctl, (Vector2 pos, float scale)> Layout = new Dictionary<Ctl, (Vector2, float)>();
        public static readonly Ctl[] Editable =
        {
            Ctl.Stick, Ctl.Fire, Ctl.Use, Ctl.WeaponNext, Ctl.WeaponPrev, Ctl.Run,
            Ctl.Map, Ctl.Menu, Ctl.QuickSave, Ctl.QuickLoad
        };

        public static readonly int[] FpsModes = { 0, 60, 90, 120 };

        public static void Load()
        {
            LookSensitivity = PlayerPrefs.GetInt("look_sens", 5);
            ControlsScale = PlayerPrefs.GetInt("ctl_scale", 100);
            ControlsOpacity = PlayerPrefs.GetInt("ctl_alpha", 75);
            LeftHanded = PlayerPrefs.GetInt("left_handed", 0) == 1;
            Gyro = PlayerPrefs.GetInt("gyro", 0) == 1;
            GyroSensitivity = PlayerPrefs.GetInt("gyro_sens", 4);
            GyroInvert = PlayerPrefs.GetInt("gyro_inv", 0) == 1;
            Haptics = PlayerPrefs.GetInt("haptics", 1) == 1;
            FpsMode = PlayerPrefs.GetInt("fps_mode", 0);
            Widescreen = PlayerPrefs.GetInt("widescreen", 1) == 1;
            ShowFps = PlayerPrefs.GetInt("show_fps", 0) == 1;
            MusicHQ = PlayerPrefs.GetInt("music_hq", 1) == 1;
            SmoothLook = PlayerPrefs.GetInt("smooth_look", 1) == 1;
            var lang = PlayerPrefs.GetString("lang", "");
            Arabic = lang == "" ? Application.systemLanguage == SystemLanguage.Arabic : lang == "ar";
            Layout.Clear();
            foreach (var c in Editable)
            {
                var s = PlayerPrefs.GetString("lay_" + c, "");
                var p = s.Split(',');
                if (p.Length == 3 &&
                    float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                    float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var k))
                {
                    Layout[c] = (new Vector2(x, y), k);
                }
            }
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("look_sens", LookSensitivity);
            PlayerPrefs.SetInt("ctl_scale", ControlsScale);
            PlayerPrefs.SetInt("ctl_alpha", ControlsOpacity);
            PlayerPrefs.SetInt("left_handed", LeftHanded ? 1 : 0);
            PlayerPrefs.SetInt("gyro", Gyro ? 1 : 0);
            PlayerPrefs.SetInt("gyro_sens", GyroSensitivity);
            PlayerPrefs.SetInt("gyro_inv", GyroInvert ? 1 : 0);
            PlayerPrefs.SetInt("haptics", Haptics ? 1 : 0);
            PlayerPrefs.SetInt("fps_mode", FpsMode);
            PlayerPrefs.SetInt("widescreen", Widescreen ? 1 : 0);
            PlayerPrefs.SetInt("show_fps", ShowFps ? 1 : 0);
            PlayerPrefs.SetInt("music_hq", MusicHQ ? 1 : 0);
            PlayerPrefs.SetInt("smooth_look", SmoothLook ? 1 : 0);
            PlayerPrefs.SetString("lang", Arabic ? "ar" : "en");
            foreach (var c in Editable)
            {
                if (Layout.TryGetValue(c, out var v))
                {
                    PlayerPrefs.SetString("lay_" + c, string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F3}", v.pos.x, v.pos.y, v.scale));
                }
                else
                {
                    PlayerPrefs.DeleteKey("lay_" + c);
                }
            }
            PlayerPrefs.Save();
        }
    }
}
