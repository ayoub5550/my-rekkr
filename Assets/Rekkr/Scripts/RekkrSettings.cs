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
        public static bool StablePerf;           // dev3: Android sustained performance mode
        public static int RenderThreads;         // dev3: 0 = auto (min(4, cores-1)), 1 = original single thread
        public static int Resolution = 800;      // dev3: max frame lines 400/600/800/1000 (400 = v0.2.0)
        public static bool DynamicRes = true;    // dev3: drop/raise lines to hold the frame rate
        public static bool SmoothLighting = true; // dev3: true-colour light gradients (no 32-step bands)
        public static int Bloom = 1;             // dev3 post: 0 off, 1 low, 2 medium, 3 high
        public static int Vignette = 15;         // dev3 post: 0..30 %
        public static int ColorGrade = 1;        // dev3 post: 0 neutral, 1 vivid, 2 warm
        public static bool Sharpen = true;       // dev3 post: sharpen when upscaling
        public static bool Crt;                  // dev3 post: CRT scanlines
        public static bool SideFill = true;      // dev3: blurred sides on centred 4:3 screens
        public static readonly int[] Resolutions = { 400, 600, 800, 1000 };
        public static int GfxPreset = 2;         // dev3: 0 Classic, 1 Balanced, 2 Enhanced, 3 Custom

        // Preset table: resolution, dynres, smooth light, bloom, vignette, grade, sharpen, crt, side fill, threads
        private static readonly (int res, bool dyn, bool light, int bloom, int vig, int grade, bool sharp, bool crt, bool side, int threads)[] Presets =
        {
            (400, false, false, 0, 0, 0, false, false, false, 1),   // Classic: pixel-identical to v0.2.0
            (600, true, true, 0, 0, 0, true, false, true, 0),       // Balanced
            (800, true, true, 1, 15, 1, true, false, true, 0),      // Enhanced
        };

        public static void ApplyPreset(int p)
        {
            var v = Presets[System.Math.Clamp(p, 0, 2)];
            Resolution = v.res; DynamicRes = v.dyn; SmoothLighting = v.light; Bloom = v.bloom; Vignette = v.vig;
            ColorGrade = v.grade; Sharpen = v.sharp; Crt = v.crt; SideFill = v.side; RenderThreads = v.threads;
            GfxPreset = p;
        }

        /// <summary>The preset the current values match, or 3 (Custom).</summary>
        public static int MatchPreset()
        {
            for (var p = 0; p < Presets.Length; p++)
            {
                var v = Presets[p];
                if (Resolution == v.res && DynamicRes == v.dyn && SmoothLighting == v.light && Bloom == v.bloom &&
                    Vignette == v.vig && ColorGrade == v.grade && Sharpen == v.sharp && Crt == v.crt && SideFill == v.side &&
                    (p == 0) == (RenderThreads == 1)) return p;
            }
            return 3;
        }

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
            StablePerf = PlayerPrefs.GetInt("stable_perf", 0) == 1;
            RenderThreads = PlayerPrefs.GetInt("gfx_threads", 0);
            Resolution = Mathf.Clamp(PlayerPrefs.GetInt("gfx_res", 800) / 200 * 200, 400, 1000);
            DynamicRes = PlayerPrefs.GetInt("gfx_dynres", 1) == 1;
            SmoothLighting = PlayerPrefs.GetInt("gfx_light", 1) == 1;
            Bloom = Mathf.Clamp(PlayerPrefs.GetInt("gfx_bloom", 1), 0, 3);
            Vignette = Mathf.Clamp(PlayerPrefs.GetInt("gfx_vignette", 15), 0, 30);
            ColorGrade = Mathf.Clamp(PlayerPrefs.GetInt("gfx_grade", 1), 0, 2);
            Sharpen = PlayerPrefs.GetInt("gfx_sharpen", 1) == 1;
            Crt = PlayerPrefs.GetInt("gfx_crt", 0) == 1;
            SideFill = PlayerPrefs.GetInt("gfx_sidefill", 1) == 1;
            // First start of 0.3.0 (new install or update from 0.2.0): pick a preset for the device.
            if (!PlayerPrefs.HasKey("gfx_preset")) ApplyPreset(SystemInfo.processorCount >= 6 ? 2 : 1);
            else GfxPreset = PlayerPrefs.GetInt("gfx_preset", 2);
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
            PlayerPrefs.SetInt("stable_perf", StablePerf ? 1 : 0);
            PlayerPrefs.SetInt("gfx_threads", RenderThreads);
            PlayerPrefs.SetInt("gfx_res", Resolution);
            PlayerPrefs.SetInt("gfx_dynres", DynamicRes ? 1 : 0);
            PlayerPrefs.SetInt("gfx_light", SmoothLighting ? 1 : 0);
            PlayerPrefs.SetInt("gfx_bloom", Bloom);
            PlayerPrefs.SetInt("gfx_vignette", Vignette);
            PlayerPrefs.SetInt("gfx_grade", ColorGrade);
            PlayerPrefs.SetInt("gfx_sharpen", Sharpen ? 1 : 0);
            PlayerPrefs.SetInt("gfx_crt", Crt ? 1 : 0);
            PlayerPrefs.SetInt("gfx_sidefill", SideFill ? 1 : 0);
            PlayerPrefs.SetInt("gfx_preset", GfxPreset);
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
