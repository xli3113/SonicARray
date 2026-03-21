using System.IO;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

/// <summary>在 Android 上 StreamingAssets 位于 APK 内 (jar: 路径)，需用 UnityWebRequest 读取。</summary>
public static class StreamingAssetsHelper {
    /// <summary>从 StreamingAssets 读取文本。Android 自动用 UnityWebRequest，其他平台用 File.ReadAllText。</summary>
    public static string ReadAllText(string relativePath) {
        string path = Path.Combine(Application.streamingAssetsPath, relativePath);
#if UNITY_ANDROID && !UNITY_EDITOR
        var request = UnityWebRequest.Get(path);
        request.SendWebRequest();
        while (!request.isDone) { }
        if (request.result != UnityWebRequest.Result.Success) {
            Debug.LogError($"[StreamingAssets] 读取失败: {path} - {request.error}");
            return null;
        }
        return request.downloadHandler.text;
#else
        if (!File.Exists(path)) return null;
        return File.ReadAllText(path);
#endif
    }

    /// <summary>检查文件是否存在。Android 上 StreamingAssets 内文件始终“存在”，需尝试读取验证。</summary>
    public static bool Exists(string relativePath) {
        string path = Path.Combine(Application.streamingAssetsPath, relativePath);
#if UNITY_ANDROID && !UNITY_EDITOR
        return true; // 无法直接检查 jar: 路径，假定存在
#else
        return File.Exists(path);
#endif
    }
}
