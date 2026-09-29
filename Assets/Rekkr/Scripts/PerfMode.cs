// my-rekkr dev3 — Android sustained performance mode (Window.setSustainedPerformanceMode, API 24+).
// It trades peak clocks for clocks the phone can hold without throttling in long sessions.
// Off by default: the 120 Hz target needs peak clocks; offered as "Stable performance".
// SPDX-License-Identifier: GPL-2.0-or-later
using UnityEngine;

namespace ManagedDoom.UnityPort
{
    public static class PerfMode
    {
        public static bool Supported { get; private set; }

        public static void SetSustained(bool on)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    using (var pm = activity.Call<AndroidJavaObject>("getSystemService", "power"))
                    {
                        Supported = pm.Call<bool>("isSustainedPerformanceModeSupported");
                    }
                    if (!Supported) { Debug.Log("[REKKR] sustained performance mode not supported"); return; }
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                        {
                            window.Call("setSustainedPerformanceMode", on);
                        }
                    }));
                    Debug.Log("[REKKR] sustained performance mode " + (on ? "on" : "off"));
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[REKKR] sustained mode: " + e.Message); }
#endif
        }
    }
}
