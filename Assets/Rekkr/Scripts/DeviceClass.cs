// my-rekkr dev6 — device class for the automatic graphics preset (DEV5 open item 2): 8 CPU cores alone
// is not enough for Masterpiece; many 8-core phones have small GPUs (Mali-G57 MC2, Adreno 610/619...).
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text.RegularExpressions;
using UnityEngine;

namespace ManagedDoom.UnityPort
{
    public static class DeviceClass
    {
        /// <summary>True for GPUs that ran Masterpiece at the frame-rate target on device tests (Adreno ≥ 640,
        /// Mali-G7x / G710+ / G610+ / Immortalis, Xclipse, PowerVR not included) and for every non-mobile GPU.</summary>
        public static bool StrongGpu => IsStrong(SystemInfo.graphicsDeviceName, Application.isMobilePlatform);

        public static bool IsStrong(string gpu, bool mobile)
        {
            if (!mobile) return true;
            gpu = gpu ?? "";
            var m = Regex.Match(gpu, @"Adreno[^0-9]*(\d{3})");
            if (m.Success) return int.Parse(m.Groups[1].Value) >= 640;
            if (Regex.IsMatch(gpu, @"Immortalis|Xclipse", RegexOptions.IgnoreCase)) return true;
            m = Regex.Match(gpu, @"Mali-G(\d{2,3})");
            if (m.Success)
            {
                var n = int.Parse(m.Groups[1].Value);
                // G71/G72/G76/G77/G78 (2-digit 7x) and G610/G710/G615/G715/G720+ (3-digit ≥ 610)
                return n >= 100 ? n >= 610 : n >= 71 && n < 80;
            }
            return false;
        }

        /// <summary>Automatic preset for a fresh install: 3 Masterpiece, 2 Enhanced, 1 Balanced.</summary>
        public static int AutoPreset(int cores, bool strongGpu) =>
            cores >= 8 && strongGpu ? 3 : cores >= 6 ? 2 : 1;
    }
}
