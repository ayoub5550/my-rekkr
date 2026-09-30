// my-rekkr dev7 — save backup UI actions (export / import through the Android document picker) and
// scenario 9, the automatic round trip test (pack -> delete the saves -> restore -> load).
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections;
using System.IO;
using ManagedDoom;
using ManagedDoom.UnityPort;
using ManagedDoom.Video;
using UnityEngine;

public sealed partial class RekkrApp
{
    private string backupStatus;   // shown under the backup rows (localised text)

    private string BackupTemp(string name) => Path.Combine(Application.temporaryCachePath, name);

    /// <summary>Packs every save slot + the settings into a temp file; returns its path (null on failure).</summary>
    private string PackBackup(out int saves)
    {
        saves = 0;
        try
        {
            RekkrSettings.Save();
            var bytes = SaveBackup.Pack(ConfigUtilities.GetExeDirectory(), RekkrSettings.Snapshot, out saves);
            var path = BackupTemp("rekkr-backup-out.rkb");
            File.WriteAllBytes(path, bytes);
            Debug.Log($"[REKKR] backup packed saves={saves} bytes={bytes.Length}");
            return path;
        }
        catch (Exception e) { Debug.LogWarning("Backup pack failed: " + e.Message); return null; }
    }

    public void ExportSaves()
    {
        var path = PackBackup(out var saves);
        if (path == null) { backupStatus = Loc.T("backup_fail"); return; }
        var name = $"rekkr-saves-{DateTime.Now:yyyyMMdd-HHmm}.rkb";
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var docs = new AndroidJavaClass("com.ayoub.rekkr.RekkrDocs"))
            {
                docs.CallStatic("start", activity, "export", path, name);
            }
            backupStatus = Loc.T("backup_pick");
        }
        catch (Exception e) { Debug.LogWarning("Backup picker failed: " + e.Message); backupStatus = Loc.T("backup_fail"); }
#else
        var dir = Path.Combine(Application.persistentDataPath, "backups");
        Directory.CreateDirectory(dir);
        File.Copy(path, Path.Combine(dir, name), true);
        backupStatus = Loc.T("backup_saved").Replace("{n}", saves.ToString());
        Debug.Log("[REKKR] backup written " + Path.Combine(dir, name));
#endif
    }

    public void ImportSaves()
    {
        var path = BackupTemp("rekkr-backup-in.rkb");
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var docs = new AndroidJavaClass("com.ayoub.rekkr.RekkrDocs"))
            {
                docs.CallStatic("start", activity, "import", path, "");
            }
            backupStatus = Loc.T("backup_pick");
        }
        catch (Exception e) { Debug.LogWarning("Backup picker failed: " + e.Message); backupStatus = Loc.T("backup_fail"); }
#else
        // desktop: the newest file in persistentDataPath/backups
        var dir = Path.Combine(Application.persistentDataPath, "backups");
        string newest = null;
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.rkb"))
                if (newest == null || File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(newest)) newest = f;
        if (newest == null) { backupStatus = Loc.T("backup_fail"); return; }
        File.Copy(newest, path, true);
        ApplyBackup(path);
#endif
    }

    /// <summary>Called by RekkrDocs.java: "mode:status:detail".</summary>
    public void OnBackupResult(string msg)
    {
        Debug.Log("[REKKR] backup result " + msg);
        var p = msg.Split(new[] { ':' }, 3);
        if (p.Length < 2) return;
        if (p[1] == "cancel") { backupStatus = Loc.T("backup_cancel"); return; }
        if (p[1] != "ok") { backupStatus = Loc.T("backup_fail"); return; }
        if (p[0] == "export") backupStatus = Loc.T("backup_saved").Replace("{n}", CountSaves().ToString());
        else ApplyBackup(BackupTemp("rekkr-backup-in.rkb"));
    }

    private int CountSaves()
    {
        var n = 0;
        for (var i = 0; i <= 9; i++) if (File.Exists(SavePath(i))) n++;
        return n;
    }

    /// <summary>Restores the saves + settings of a backup file. Returns the number of save slots restored, -1 on error.</summary>
    private int ApplyBackup(string path)
    {
        try
        {
            var files = SaveBackup.Unpack(File.ReadAllBytes(path), out var error);
            if (files == null) { Debug.LogWarning("Backup rejected: " + error); backupStatus = Loc.T("backup_bad"); return -1; }
            var prefs = SaveBackup.Apply(files, ConfigUtilities.GetExeDirectory(), out var saves);
            var threadsBefore = RekkrSettings.RenderThreads;
            RekkrSettings.Import(prefs);
            // side effects of the loaded settings (same as at start)
            Loc.Arabic = RekkrSettings.Arabic;
            Haptics.Enabled = RekkrSettings.Haptics;
            DisplayRate.Apply(RekkrSettings.FpsMode);
            if (SystemInfo.supportsGyroscope) Input.gyro.enabled = RekkrSettings.Gyro;
            PerfMode.SetSustained(RekkrSettings.StablePerf);
            if (RekkrSettings.RenderThreads != threadsBefore) { ThreeDRendererPool.Threads = RekkrSettings.RenderThreads; video.Rebuild(); }
            ApplyWidescreen();
            Doom.Menu.SaveSlots.Refresh();
            RefreshContinue();
            backupStatus = Loc.T("backup_restored").Replace("{n}", saves.ToString());
            Debug.Log($"[REKKR] backup restored saves={saves} prefs={prefs.Count}");
            return saves;
        }
        catch (Exception e) { Debug.LogWarning("Backup restore failed: " + e.Message); backupStatus = Loc.T("backup_fail"); return -1; }
    }

    /// <summary>dev7 scenario 9 — backup round trip: play, quick save, pack, delete every save, restore, load.</summary>
    private IEnumerator Scenario9()
    {
        HudMode = 1;
        yield return Wait(3F);
        yield return TapSeq(Ctl.Ok, 1.0F);
        yield return TapSeq(Ctl.Back, 0.8F);
        Doom.NewGame(GameSkill.Easy, 2, 1);
        yield return Wait(1.5F);
        yield return Play(6F, null);
        QuickSave();
        yield return Wait(0.5F);
        var before = CountSaves();
        var packed = PackBackup(out var saves);
        var javaOk = false;
#if UNITY_ANDROID && !UNITY_EDITOR
        try { using (var docs = new AndroidJavaClass("com.ayoub.rekkr.RekkrDocs")) javaOk = docs != null; } catch (Exception e) { Log("RekkrDocs missing: " + e.Message); }
#endif
        for (var i = 0; i <= 9; i++) if (File.Exists(SavePath(i))) File.Delete(SavePath(i));
        Doom.Menu.SaveSlots.Refresh(); RefreshContinue();
        var afterDelete = CountSaves();
        var restored = packed != null ? ApplyBackup(packed) : -1;
        var after = CountSaves();
        Doom.NewGame(GameSkill.Easy, 1, 1);
        yield return Wait(1F);
        QuickLoad();
        yield return Wait(1.5F);
        while (Doom.Wiping) yield return null;
        var map = InLevel ? $"E{Doom.Game.Options.Episode}M{Doom.Game.Options.Map}" : "none";
        Log($"backup roundtrip saves_before={before} packed={saves} after_delete={afterDelete} restored={restored} after={after} loaded_map={map} java_class={javaOk} status=\"{backupStatus}\" result={(before > 0 && restored == before && after == before && map == "E2M1" ? "PASS" : "FAIL")}");
        ShotFrame("backup_loaded");
    }
}
