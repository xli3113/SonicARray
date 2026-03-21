using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>从 cpp 复制 speakers.yaml 到 StreamingAssets。</summary>
public static class CopySpeakersYaml {

    [MenuItem("SonicARray/Copy speakers.yaml from cpp")]
    public static void Copy() {
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        var sources = new[] {
            Path.Combine(projectRoot, "cpp", "speakers.yaml"),
            Path.Combine(projectRoot, "cpp", "src", "speakers.yaml"),
            Path.Combine(projectRoot, "speakers.yaml"),
        };
        string src = null;
        foreach (var s in sources) {
            if (File.Exists(s)) { src = s; break; }
        }
        if (src == null) {
            EditorUtility.DisplayDialog("Copy speakers.yaml", $"未找到 speakers.yaml。\n已检查:\n- {projectRoot}/cpp/\n- {projectRoot}/cpp/src/\n- {projectRoot}/", "确定");
            return;
        }
        var destDir = Path.Combine(Application.dataPath, "StreamingAssets");
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, "speakers.yaml");
        File.Copy(src, dest, overwrite: true);
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Copy speakers.yaml", $"已复制:\n{src}\n→\n{dest}", "确定");
    }
}
