// my-rekkr — command-line build entry points (local builds only).
//   Unity -batchmode -quit -projectPath . -executeMethod RekkrBuild.BuildAndroid
// Env: REKKR_VERSION_CODE (int), REKKR_OUT (apk path), REKKR_KEYSTORE / REKKR_KEYSTORE_PASS
// (optional custom signing key kept OUTSIDE the repository).
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RekkrBuild
{
    public const string ScenePath = "Assets/Scenes/Main.unity";
    public const string PackageId = "com.ayoub.rekkr";

    [MenuItem("REKKR/Configure Project")]
    public static void Configure()
    {
        EnsureScene();
        PlayerSettings.companyName = "ayoub5550";
        PlayerSettings.productName = "REKKR";
        PlayerSettings.bundleVersion = RekkrApp.Version;
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PackageId);
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, PackageId);
        PlayerSettings.allowUnsafeCode = true;
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.runInBackground = false;
        PlayerSettings.SplashScreen.backgroundColor = Color.black;
        PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 800;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;

        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, Il2CppCompilerConfiguration.Release);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Low);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
        PlayerSettings.Android.renderOutsideSafeArea = true;
        PlayerSettings.Android.startInFullscreen = true;
        PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, true);
        EditorUserBuildSettings.buildAppBundle = false;

        var code = Environment.GetEnvironmentVariable("REKKR_VERSION_CODE");
        PlayerSettings.Android.bundleVersionCode = string.IsNullOrEmpty(code) ? int.Parse(RekkrApp.Version.Split('.')[1]) : int.Parse(code); // default: 0.N.x -> N

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Rekkr/Icon/icon.png");
        if (icon != null)
        {
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
        }

        var ks = Environment.GetEnvironmentVariable("REKKR_KEYSTORE");
        var ksPass = Environment.GetEnvironmentVariable("REKKR_KEYSTORE_PASS");
        if (!string.IsNullOrEmpty(ks) && File.Exists(ks) && !string.IsNullOrEmpty(ksPass))
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ks;
            PlayerSettings.Android.keystorePass = ksPass;
            PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("REKKR_KEY_ALIAS") ?? "rekkr";
            PlayerSettings.Android.keyaliasPass = ksPass;
        }
        else
        {
            PlayerSettings.Android.useCustomKeystore = false;
        }

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[RekkrBuild] configured versionCode=" + PlayerSettings.Android.bundleVersionCode);
    }

    private static void EnsureScene()
    {
        if (File.Exists(ScenePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        // Empty scene: RekkrApp boots itself via RuntimeInitializeOnLoadMethod, so no
        // serialized MonoBehaviour references can go missing.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
    }

    public static void BuildAndroid()
    {
        Configure();
        var outPath = Environment.GetEnvironmentVariable("REKKR_OUT") ?? "Builds/REKKR-" + RekkrApp.Version + ".apk";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            // Incremental player-data reuse produced a broken APK once ("Could not produce class with ID 115",
            // GUI skin null) after a Linux build in the same Library, so Android builds always start clean.
            options = BuildOptions.CleanBuildCache,
        });
        // Keep the local keystore path out of ProjectSettings.asset (it is committed).
        PlayerSettings.Android.useCustomKeystore = false;
        PlayerSettings.Android.keystoreName = "";
        PlayerSettings.Android.keyaliasName = "";
        AssetDatabase.SaveAssets();
        Finish(report, outPath);
    }

    public static void BuildLinux()
    {
        Configure();
        var outPath = Environment.GetEnvironmentVariable("REKKR_OUT") ?? "Builds/linux/rekkr.x86_64";
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        // Desktop test player: keep running without window focus (Xvfb has no window manager,
        // so a focus-dependent player stops after its first frame).
        PlayerSettings.runInBackground = true;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outPath,
            target = BuildTarget.StandaloneLinux64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        });
        // Leave ProjectSettings as the release expects (runInBackground off on Android), so a desktop
        // QA build never dirties the committed settings.
        PlayerSettings.runInBackground = false;
        AssetDatabase.SaveAssets();
        Finish(report, outPath);
    }

    private static void Finish(BuildReport report, string outPath)
    {
        var s = report.summary;
        Debug.Log($"[RekkrBuild] result={s.result} errors={s.totalErrors} warnings={s.totalWarnings} size={s.totalSize} out={outPath} time={s.totalTime}");
        if (s.result != BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
        }
    }
}
