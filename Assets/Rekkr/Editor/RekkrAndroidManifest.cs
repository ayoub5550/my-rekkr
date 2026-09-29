// my-rekkr — adds the Firebase Test Lab Game Loop intent-filter to the generated launcher
// activity (https://firebase.google.com/docs/test-lab/android/game-loop).
// SPDX-License-Identifier: GPL-2.0-or-later
using System.IO;
using UnityEditor.Android;
using UnityEngine;

public sealed class RekkrAndroidManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 100;

    private const string Filter =
        "<intent-filter><action android:name=\"com.google.intent.action.TEST_LOOP\" />" +
        "<category android:name=\"android.intent.category.DEFAULT\" />" +
        "<data android:mimeType=\"application/javascript\" /></intent-filter>";

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        var manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
        if (!File.Exists(manifest)) { Debug.LogWarning("[RekkrManifest] missing " + manifest); return; }
        var xml = File.ReadAllText(manifest);
        // Haptics (VibrationEffect pulses on attack / damage).
        if (!xml.Contains("android.permission.VIBRATE"))
        {
            var app = xml.IndexOf("<application");
            if (app > 0) xml = xml.Insert(app, "<uses-permission android:name=\"android.permission.VIBRATE\" />");
        }
        if (xml.Contains("com.google.intent.action.TEST_LOOP")) { File.WriteAllText(manifest, xml); return; }
        var launcher = xml.IndexOf("android.intent.category.LAUNCHER");
        if (launcher < 0) { Debug.LogWarning("[RekkrManifest] no launcher activity"); return; }
        var end = xml.IndexOf("</intent-filter>", launcher);
        end += "</intent-filter>".Length;
        xml = xml.Insert(end, Filter);
        File.WriteAllText(manifest, xml);
        Debug.Log("[RekkrManifest] TEST_LOOP intent-filter added");
    }
}
