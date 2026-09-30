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
        public static int Resolution = 600;      // dev4 (was 800); dev3: max frame lines 400/600/800/1000 (400 = v0.2.0)
        public static bool DynamicRes = true;    // dev3: drop/raise lines to hold the frame rate
        public static bool SmoothLighting = true; // dev3: true-colour light gradients (no 32-step bands)
        public static int Bloom = 1;             // dev3 post: 0 off, 1 low, 2 medium, 3 high
        public static int Vignette = 15;         // dev3 post: 0..30 %
        public static int ColorGrade = 1;        // dev3 post: 0 neutral, 1 vivid, 2 warm
        public static bool Sharpen = true;       // dev3 post: sharpen when upscaling
        public static bool Crt;                  // dev3 post: CRT scanlines
        public static bool SideFill = true;      // dev3: blurred sides on centred 4:3 screens
        public static readonly int[] Resolutions = { 400, 600, 800, 1000 };
        public static bool FreeLook = true;      // dev4: look up/down (y-shearing)
        public static bool InvertLook;           // dev4: invert vertical swipe
        public static bool AutoAim = true;       // dev4: vanilla vertical autoaim (off = shots follow the crosshair)
        public static bool Jump = true;          // dev4: jump button (not in vanilla Doom)
        public static int StickMode;             // dev4: 0 fixed, 1 floating
        public static int Crosshair;             // dev4: 0 + bone, 1 + red, 2 + green, 3 dot, 4 off
        public const int CrosshairStyles = 5;
        public static int GfxPreset = 2;         // dev3: 0 Classic, 1 Balanced, 2 Enhanced, dev5: 3 Masterpiece, 4 Custom
        public const int PresetCustom = 4;
        // dev5 "Masterpiece" world effects (need smooth lighting: they read the G-buffer).
        public static bool SkyFx;                // animated sky: drift + cloud layer + sun glow
        public static bool WaterFx;              // real water: reflections, ripples, foam; hot liquids glow
        public static int Weather = 3;           // 0 auto (per episode), 1 rain, 2 snow, 3 off
        public static int Fog;                   // 0 off, 1 light, 2 medium
        public static bool DynLights;            // fireballs / torches / muzzle flash light the world
        public static int DynLightLevel = 1;     // dev7: 1 low (subtle, default), 2 high
        public static bool SunRays;              // god rays from the sun
        public static bool AO;                   // ambient occlusion in corners
        public static bool Particles;            // sparks, blood drops, splashes, embers
        public static bool DoF;                  // depth of field (far blur)
        public static bool Remaster;             // dev6: GPU 3D renderer ("Remaster") instead of the software 3D view
        /// <summary>dev6: Remaster is offered only on strong GPUs. Test Lab Galaxy A15 (Mali-G57 MC2) ran it at
        /// 19–28 fps (gate ≥ 45), so weak GPUs keep the original renderer and the option is hidden.</summary>
        public static bool RemasterAllowed => ManagedDoom.UnityPort.DeviceClass.StrongGpu;
        public static int RemasterThings = 1;    // dev6: 0 flat billboards (original sprites), 1 extruded 3D voxel sprites
        public static bool RemasterShadows = true; // dev6: sun shadow map in outdoor areas
        public static bool RemasterLightShadows = true; // dev7: the two nearest dynamic lights cast shadows (Remaster)
        public static int RemasterWeapon = 1;     // dev7: 0 original flat weapon, 1 3D (extruded) weapon (Remaster)
        public static int DarkAreas = 1;         // dev6: 0 original sector light, 1 lifted, 2 bright (E3 has many light-0 rooms)
        public static readonly int[] DarkAreaFloor = { 0, 112, 144 };

        // Preset table: resolution, dynres, smooth light, bloom, vignette, grade, sharpen, crt, side fill, threads, dev5 fx
        private static readonly (int res, bool dyn, bool light, int bloom, int vig, int grade, bool sharp, bool crt, bool side, int threads, bool fx)[] Presets =
        {
            (400, false, false, 0, 0, 0, false, false, false, 1, false),   // Classic: pixel-identical to v0.2.0
            (600, true, true, 0, 0, 0, true, false, true, 0, false),       // Balanced
            (600, true, true, 1, 15, 1, true, false, true, 0, false),      // Enhanced (dev4: 600 — r8q could not hold 800 at 120 Hz)
            (600, true, true, 1, 15, 3, true, false, true, 0, true),       // dev5 Masterpiece: Enhanced + world effects + Voxile grade
        };

        public static void ApplyPreset(int p)
        {
            var v = Presets[System.Math.Clamp(p, 0, Presets.Length - 1)];
            Resolution = v.res; DynamicRes = v.dyn; SmoothLighting = v.light; Bloom = v.bloom; Vignette = v.vig;
            ColorGrade = v.grade; Sharpen = v.sharp; Crt = v.crt; SideFill = v.side; RenderThreads = v.threads;
            SkyFx = WaterFx = DynLights = SunRays = AO = Particles = v.fx;
            Weather = v.fx ? 0 : 3; Fog = v.fx ? 1 : 0; DoF = false;
            GfxPreset = p;
        }

        private static bool FxAll(bool on) =>
            SkyFx == on && WaterFx == on && DynLights == on && SunRays == on && AO == on && Particles == on &&
            Weather == (on ? 0 : 3) && Fog == (on ? 1 : 0) && !DoF;

        /// <summary>The preset the current values match, or PresetCustom.</summary>
        public static int MatchPreset()
        {
            for (var p = 0; p < Presets.Length; p++)
            {
                var v = Presets[p];
                if (Resolution == v.res && DynamicRes == v.dyn && SmoothLighting == v.light && Bloom == v.bloom &&
                    Vignette == v.vig && ColorGrade == v.grade && Sharpen == v.sharp && Crt == v.crt && SideFill == v.side &&
                    (p == 0) == (RenderThreads == 1) && FxAll(v.fx)) return p;
            }
            return PresetCustom;
        }

        /// <summary>dev5: any world effect on (they need smooth lighting for the G-buffer).</summary>
        public static bool AnyWorldFx =>
            FxLighting && (SkyFx || WaterFx || Weather != 3 || Fog > 0 || DynLights || SunRays || AO || Particles || DoF);

        /// <summary>dev7: the G-buffer the world effects need exists (smooth lighting, or Remaster, which always
        /// renders true colour). dev6 required smooth lighting even in Remaster, so effects vanished there.</summary>
        public static bool FxLighting => SmoothLighting || (Remaster && RemasterAllowed);

        /// <summary>Custom button placement: centre as a fraction of the screen + size multiplier.</summary>
        public static readonly Dictionary<Ctl, (Vector2 pos, float scale)> Layout = new Dictionary<Ctl, (Vector2, float)>();
        public static readonly Ctl[] Editable =
        {
            Ctl.Stick, Ctl.Fire, Ctl.Use, Ctl.WeaponNext, Ctl.WeaponPrev, Ctl.Run,
            Ctl.Map, Ctl.Menu, Ctl.QuickSave, Ctl.QuickLoad, Ctl.Jump
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
            Resolution = Mathf.Clamp(PlayerPrefs.GetInt("gfx_res", 600) / 200 * 200, 400, 1000);
            DynamicRes = PlayerPrefs.GetInt("gfx_dynres", 1) == 1;
            SmoothLighting = PlayerPrefs.GetInt("gfx_light", 1) == 1;
            Bloom = Mathf.Clamp(PlayerPrefs.GetInt("gfx_bloom", 1), 0, 3);
            Vignette = Mathf.Clamp(PlayerPrefs.GetInt("gfx_vignette", 15), 0, 30);
            Sharpen = PlayerPrefs.GetInt("gfx_sharpen", 1) == 1;
            Crt = PlayerPrefs.GetInt("gfx_crt", 0) == 1;
            SideFill = PlayerPrefs.GetInt("gfx_sidefill", 1) == 1;
            FreeLook = PlayerPrefs.GetInt("free_look", 1) == 1;
            InvertLook = PlayerPrefs.GetInt("invert_look", 0) == 1;
            AutoAim = PlayerPrefs.GetInt("autoaim", 1) == 1;
            Jump = PlayerPrefs.GetInt("jump", 1) == 1;
            StickMode = Mathf.Clamp(PlayerPrefs.GetInt("stick_mode", 0), 0, 1);
            Crosshair = Mathf.Clamp(PlayerPrefs.GetInt("crosshair", 0), 0, CrosshairStyles - 1);
            SkyFx = PlayerPrefs.GetInt("fx_sky", 0) == 1;
            WaterFx = PlayerPrefs.GetInt("fx_water", 0) == 1;
            Weather = Mathf.Clamp(PlayerPrefs.GetInt("fx_weather", 3), 0, 3);
            Fog = Mathf.Clamp(PlayerPrefs.GetInt("fx_fog", 0), 0, 2);
            DynLights = PlayerPrefs.GetInt("fx_lights", 0) == 1;
            DynLightLevel = Mathf.Clamp(PlayerPrefs.GetInt("fx_lights_lvl", 1), 1, 2);
            SunRays = PlayerPrefs.GetInt("fx_rays", 0) == 1;
            AO = PlayerPrefs.GetInt("fx_ao", 0) == 1;
            Particles = PlayerPrefs.GetInt("fx_particles", 0) == 1;
            DoF = PlayerPrefs.GetInt("fx_dof", 0) == 1;
            Remaster = RemasterAllowed && PlayerPrefs.GetInt("gfx_remaster", 0) == 1;
            RemasterThings = Mathf.Clamp(PlayerPrefs.GetInt("rm_things", 1), 0, 1);
            RemasterShadows = PlayerPrefs.GetInt("rm_shadows", 1) == 1;
            RemasterLightShadows = PlayerPrefs.GetInt("rm_lshadows", 1) == 1;
            RemasterWeapon = Mathf.Clamp(PlayerPrefs.GetInt("rm_weapon", 1), 0, 1);
            DarkAreas = Mathf.Clamp(PlayerPrefs.GetInt("gfx_dark", 1), 0, 2);
            ColorGrade = Mathf.Clamp(PlayerPrefs.GetInt("gfx_grade", 1), 0, 3);
            // First start (new install): pick a preset for the device (dev5: Masterpiece on 8-core phones).
            var fresh = !PlayerPrefs.HasKey("gfx_preset");
            // dev6: Masterpiece also needs a strong GPU (DeviceClass), not only 8 cores.
            if (fresh) ApplyPreset(ManagedDoom.UnityPort.DeviceClass.AutoPreset(SystemInfo.processorCount, ManagedDoom.UnityPort.DeviceClass.StrongGpu));
            else GfxPreset = PlayerPrefs.GetInt("gfx_preset", 2);
            // dev5: preset 3 used to mean Custom; Custom is now 4 (3 = Masterpiece).
            if (!PlayerPrefs.HasKey("dev5_migrated"))
            {
                if (!fresh && GfxPreset == 3) GfxPreset = PresetCustom;
                // Enhanced (the old default) on 8-core phones moves up to Masterpiece once; Custom is kept.
                if (!fresh && GfxPreset == 2 && SystemInfo.processorCount >= 8 && ManagedDoom.UnityPort.DeviceClass.StrongGpu) ApplyPreset(3);
                PlayerPrefs.SetInt("dev5_migrated", 1);
            }
            // dev4: Enhanced moved from 800 to 600 lines (device measurement); migrate 0.3.0 Enhanced once.
            if (!PlayerPrefs.HasKey("dev4_migrated"))
            {
                if (GfxPreset == 2 && Resolution == 800) Resolution = 600;
                PlayerPrefs.SetInt("dev4_migrated", 1);
            }
            ManagedDoom.UnityPort.Gamepad.Load();   // dev7
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

        /// <summary>dev7 save backup: every value the last Save() wrote (key -> int or string).</summary>
        public static readonly Dictionary<string, object> Snapshot = new Dictionary<string, object>();
        private static void SetI(string k, int v) { PlayerPrefs.SetInt(k, v); Snapshot[k] = v; }
        private static void SetS(string k, string v) { PlayerPrefs.SetString(k, v); Snapshot[k] = v; }
        private static void DelKey(string k) { PlayerPrefs.DeleteKey(k); Snapshot.Remove(k); }

        /// <summary>dev7: writes imported settings to PlayerPrefs (layout keys not in the backup are cleared) and reloads.</summary>
        public static void Import(Dictionary<string, object> prefs)
        {
            foreach (var c in Editable) PlayerPrefs.DeleteKey("lay_" + c);
            foreach (var kv in prefs)
            {
                if (kv.Value is int i) PlayerPrefs.SetInt(kv.Key, i);
                else if (kv.Value is string str) PlayerPrefs.SetString(kv.Key, str);
            }
            PlayerPrefs.Save();
            Load();
        }

        public static void Save()
        {
            SetI("look_sens", LookSensitivity);
            SetI("ctl_scale", ControlsScale);
            SetI("ctl_alpha", ControlsOpacity);
            SetI("left_handed", LeftHanded ? 1 : 0);
            SetI("gyro", Gyro ? 1 : 0);
            SetI("gyro_sens", GyroSensitivity);
            SetI("gyro_inv", GyroInvert ? 1 : 0);
            SetI("haptics", Haptics ? 1 : 0);
            SetI("fps_mode", FpsMode);
            SetI("widescreen", Widescreen ? 1 : 0);
            SetI("show_fps", ShowFps ? 1 : 0);
            SetI("music_hq", MusicHQ ? 1 : 0);
            SetI("smooth_look", SmoothLook ? 1 : 0);
            SetI("stable_perf", StablePerf ? 1 : 0);
            SetI("gfx_threads", RenderThreads);
            SetI("gfx_res", Resolution);
            SetI("gfx_dynres", DynamicRes ? 1 : 0);
            SetI("gfx_light", SmoothLighting ? 1 : 0);
            SetI("gfx_bloom", Bloom);
            SetI("gfx_vignette", Vignette);
            SetI("gfx_grade", ColorGrade);
            SetI("gfx_sharpen", Sharpen ? 1 : 0);
            SetI("gfx_crt", Crt ? 1 : 0);
            SetI("gfx_sidefill", SideFill ? 1 : 0);
            SetI("gfx_preset", GfxPreset);
            SetI("free_look", FreeLook ? 1 : 0);
            SetI("invert_look", InvertLook ? 1 : 0);
            SetI("autoaim", AutoAim ? 1 : 0);
            SetI("jump", Jump ? 1 : 0);
            SetI("stick_mode", StickMode);
            SetI("crosshair", Crosshair);
            SetI("fx_sky", SkyFx ? 1 : 0);
            SetI("fx_water", WaterFx ? 1 : 0);
            SetI("fx_weather", Weather);
            SetI("fx_fog", Fog);
            SetI("fx_lights", DynLights ? 1 : 0);
            SetI("fx_lights_lvl", DynLightLevel);
            SetI("fx_rays", SunRays ? 1 : 0);
            SetI("fx_ao", AO ? 1 : 0);
            SetI("fx_particles", Particles ? 1 : 0);
            SetI("fx_dof", DoF ? 1 : 0);
            SetI("gfx_remaster", Remaster ? 1 : 0);
            SetI("rm_things", RemasterThings);
            SetI("rm_shadows", RemasterShadows ? 1 : 0);
            SetI("rm_lshadows", RemasterLightShadows ? 1 : 0);
            SetI("rm_weapon", RemasterWeapon);
            SetI("gfx_dark", DarkAreas);
            SetS("lang", Arabic ? "ar" : "en");
            ManagedDoom.UnityPort.Gamepad.Save(SetS, SetI);   // dev7
            foreach (var c in Editable)
            {
                if (Layout.TryGetValue(c, out var v))
                {
                    SetS("lay_" + c, string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F3}", v.pos.x, v.pos.y, v.scale));
                }
                else
                {
                    DelKey("lay_" + c);
                }
            }
            PlayerPrefs.Save();
        }
    }
}
