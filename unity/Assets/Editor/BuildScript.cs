using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class BuildScript
{
    static readonly string[] scenes = {
        "Assets/Scenes/MainScene.unity",
        "Assets/Scenes/SpeakerDebug.unity"
    };

    const string PackageId = "com.sonicarray.app";
    const string ApkName = "SonicARray.apk";

    static string GetProjectRoot()
    {
        return Path.GetDirectoryName(Application.dataPath);
    }

    static string GetOutputPath()
    {
        string projectRoot = GetProjectRoot();
        string outputDir = Path.Combine(projectRoot, "Builds");
        Directory.CreateDirectory(outputDir);
        return Path.Combine(outputDir, ApkName);
    }

    static void EnsureAndroidPaths()
    {
        string editorRoot = Path.GetDirectoryName(EditorApplication.applicationPath);
        string androidPlayerRoot = Path.Combine(editorRoot, "Data", "PlaybackEngines", "AndroidPlayer");
        EditorPrefs.SetString("AndroidSdkRoot", Path.Combine(androidPlayerRoot, "SDK"));
        EditorPrefs.SetString("AndroidNdkRootR23b", Path.Combine(androidPlayerRoot, "NDK"));
        EditorPrefs.SetString("JdkPath", Path.Combine(androidPlayerRoot, "OpenJDK"));
    }

    static void EnsureSpeakersYaml()
    {
        string streamingPath = Path.Combine(Application.dataPath, "StreamingAssets", "speakers.yaml");
        if (File.Exists(streamingPath)) return;
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string[] sources = {
            Path.Combine(projectRoot, "..", "speakers.yaml"),
            Path.Combine(projectRoot, "..", "cpp", "speakers.yaml"),
            Path.Combine(projectRoot, "..", "cpp", "src", "speakers.yaml"),
        };
        foreach (var src in sources)
        {
            if (File.Exists(src))
            {
                string dir = Path.Combine(Application.dataPath, "StreamingAssets");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.Copy(src, streamingPath, overwrite: true);
                Debug.Log($"[Build] 已复制 speakers.yaml 到 StreamingAssets");
                return;
            }
        }
        Debug.LogWarning("[Build] StreamingAssets/speakers.yaml 不存在，扬声器可能不显示。请运行 SonicARray > Copy speakers.yaml from cpp");
    }

    static void ApplyPlayerSettings()
    {
        PlayerSettings.applicationIdentifier = PackageId;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
    }

    /// <summary>仅构建 APK，输出到 Builds/SonicARray.apk</summary>
    [MenuItem("SonicARray/Build Android APK")]
    public static void BuildAndroid()
    {
        string outputPath = GetOutputPath();
        EnsureSpeakersYaml();
        EnsureAndroidPaths();
        ApplyPlayerSettings();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        Debug.Log($"[Build] 开始构建 -> {outputPath}");
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[Build] 结果: {report.summary.result}, 错误: {report.summary.totalErrors}");

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
        else
            EditorApplication.Exit(0);
    }

    /// <summary>构建并部署到已连接的 Quest 设备</summary>
    [MenuItem("SonicARray/Build And Run on Quest")]
    public static void BuildAndRun()
    {
        string outputPath = GetOutputPath();
        EnsureSpeakersYaml();
        EnsureAndroidPaths();
        ApplyPlayerSettings();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        Debug.Log($"[Build] 开始构建 -> {outputPath}");
        var report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError($"[Build] 构建失败: {report.summary.totalErrors} 个错误");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("[Build] 构建成功，正在部署到 Quest...");
        DeployAndLaunch(outputPath);
        EditorApplication.Exit(0);
    }

    /// <summary>命令行批处理入口：-executeMethod BuildScript.BuildAndroid 或 BuildScript.BuildAndRun</summary>
    public static void BuildAndroidBatch()
    {
        BuildAndroid();
    }

    public static void BuildAndRunBatch()
    {
        BuildAndRun();
    }

    static void DeployAndLaunch(string apkPath)
    {
        if (!File.Exists(apkPath))
        {
            Debug.LogError($"[Deploy] APK 不存在: {apkPath}");
            return;
        }

        string adb = FindAdb();
        if (string.IsNullOrEmpty(adb))
        {
            Debug.LogError("[Deploy] 未找到 adb。请安装 Android SDK Platform Tools 并加入 PATH。");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = adb,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.Arguments = $"install -r \"{apkPath}\"";
        using (var p = Process.Start(psi))
        {
            p.WaitForExit(60000);
            string err = p.StandardError.ReadToEnd();
            if (p.ExitCode != 0)
            {
                Debug.LogError($"[Deploy] adb install 失败: {err}");
                return;
            }
        }

        psi.Arguments = $"shell am start -n {PackageId}/com.unity3d.player.UnityPlayerActivity";
        using (var p = Process.Start(psi))
        {
            p.WaitForExit(5000);
        }

        Debug.Log("[Deploy] 已安装并启动应用");
    }

    static string FindAdb()
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in path.Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(dir.Trim(), "adb.exe");
            if (File.Exists(candidate)) return candidate;
        }
        string androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        if (!string.IsNullOrEmpty(androidHome))
        {
            string platformTools = Path.Combine(androidHome, "platform-tools", "adb.exe");
            if (File.Exists(platformTools)) return platformTools;
        }
        return null;
    }
}
