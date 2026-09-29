// my-rekkr — high refresh rate (90/120 Hz) on Android: pick the display mode with the
// requested rate at the current resolution (WindowManager.LayoutParams.preferredDisplayModeId)
// and set Application.targetFrameRate. The engine interpolates between 35 Hz tics, so every
// extra frame is a real, smoother frame.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ManagedDoom.UnityPort
{
    public static class DisplayRate
    {
        public static float MaxRate = 60F;
        public static int Target = 60;
        public static readonly List<float> Rates = new List<float>();
        private static readonly List<(int id, float rate)> modes = new List<(int, float)>();

        public static void Init()
        {
            Rates.Clear(); modes.Clear();
            MaxRate = Mathf.Max(60F, (float)Screen.currentResolution.refreshRateRatio.value);
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    if (version.GetStatic<int>("SDK_INT") < 23) return;
                }
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var wm = activity.Call<AndroidJavaObject>("getWindowManager"))
                using (var display = wm.Call<AndroidJavaObject>("getDefaultDisplay"))
                using (var current = display.Call<AndroidJavaObject>("getMode"))
                {
                    var w = current.Call<int>("getPhysicalWidth");
                    var h = current.Call<int>("getPhysicalHeight");
                    var all = display.Call<AndroidJavaObject[]>("getSupportedModes");
                    foreach (var m in all)
                    {
                        if (m.Call<int>("getPhysicalWidth") == w && m.Call<int>("getPhysicalHeight") == h)
                        {
                            var r = m.Call<float>("getRefreshRate");
                            modes.Add((m.Call<int>("getModeId"), r));
                            Rates.Add(r);
                            MaxRate = Mathf.Max(MaxRate, r);
                        }
                        m.Dispose();
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("DisplayRate init failed: " + e.Message); }
#endif
            Debug.Log("[REKKR] display rates=" + string.Join("/", Rates) + " max=" + MaxRate);
        }

        /// <summary>mode 0 = auto (highest the screen supports, up to 120), else 60/90/120.</summary>
        public static void Apply(int mode)
        {
            var want = mode == 0 ? Mathf.Min(120, Mathf.RoundToInt(MaxRate)) : mode;
            Target = want;
            Application.targetFrameRate = want;
            QualitySettings.vSyncCount = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (modes.Count == 0) return;
            var best = modes[0];
            foreach (var m in modes)
            {
                if (Mathf.Abs(m.rate - want) < Mathf.Abs(best.rate - want)) best = m;
            }
            var id = best.id;
            var rate = best.rate;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        try
                        {
                            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                            using (var lp = window.Call<AndroidJavaObject>("getAttributes"))
                            {
                                lp.Set<int>("preferredDisplayModeId", id);
                                lp.Set<float>("preferredRefreshRate", rate);
                                window.Call("setAttributes", lp);
                            }
                        }
                        catch (Exception e) { Debug.LogWarning("setAttributes failed: " + e.Message); }
                    }));
                }
            }
            catch (Exception e) { Debug.LogWarning("DisplayRate apply failed: " + e.Message); }
            Debug.Log($"[REKKR] frame rate target={want} mode={id} ({rate} Hz)");
#endif
        }
    }
}
