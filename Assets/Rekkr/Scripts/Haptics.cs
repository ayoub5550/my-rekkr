// my-rekkr — short vibration pulses through the Android Vibrator service.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using UnityEngine;

namespace ManagedDoom.UnityPort
{
    public static class Haptics
    {
        public static bool Enabled = true;
        public static int Count;          // pulses sent (logged by the Test Lab autopilot)
        private static AndroidJavaObject vibrator;
        private static bool amplitude;
        private static int sdk;
        private static float next;

        public static void Init()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                if (vibrator != null && !vibrator.Call<bool>("hasVibrator")) vibrator = null;
                amplitude = vibrator != null && sdk >= 26 && vibrator.Call<bool>("hasAmplitudeControl");
                Debug.Log($"[REKKR] haptics vibrator={(vibrator != null)} amplitude={amplitude} sdk={sdk}");
            }
            catch (Exception e) { Debug.LogWarning("Haptics init failed: " + e.Message); vibrator = null; }
#endif
        }

        /// <summary>One pulse; <paramref name="strength"/> 1..255. Rate-limited so autofire does not buzz constantly.</summary>
        public static void Pulse(int ms, int strength, float minGap = 0.06F)
        {
            if (!Enabled) return;
            var now = Time.unscaledTime;
            if (now < next) return;
            next = now + minGap;
            Count++;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (vibrator == null) return;
            try
            {
                if (sdk >= 26)
                {
                    using (var ve = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var effect = ve.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, amplitude ? Mathf.Clamp(strength, 1, 255) : -1))
                    {
                        vibrator.Call("vibrate", effect);
                    }
                }
                else
                {
                    vibrator.Call("vibrate", (long)ms);
                }
            }
            catch (Exception) { }
#endif
        }
    }
}
