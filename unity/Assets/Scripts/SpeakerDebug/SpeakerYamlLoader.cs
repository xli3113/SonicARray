using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SonicARray.SpeakerDebug {

/// <summary>从 speakers.yaml 或 speakers.json 加载扬声器列表。支持 YAML 与 JSON 双格式。</summary>
public class SpeakerYamlLoader : MonoBehaviour {
    [Header("File")]
    [Tooltip("文件名，放在 StreamingAssets 下。支持 .yaml / .yml / .json")]
    public string fileName = "speakers.yaml";
    public bool loadOnStart = true;

    [Header("Output")]
    [Tooltip("解析结果，只读")]
    public List<SpeakerEntry> speakers = new List<SpeakerEntry>();

    public event Action<int> OnLoaded;

    void Awake() {
        if (loadOnStart) Load();
    }

    /// <summary>加载并解析，返回解析到的扬声器数量。Android 上 StreamingAssets 用 UnityWebRequest 读取。</summary>
    public int Load() {
        speakers.Clear();
        string content = ReadFileContent(fileName);
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        if (string.IsNullOrEmpty(content)) {
            string alt = fileName.EndsWith(".json") ? fileName.Replace(".json", ".yaml") : fileName.Replace(".yaml", ".json").Replace(".yml", ".json");
            content = ReadFileContent(alt);
            if (!string.IsNullOrEmpty(content)) path = Path.Combine(Application.streamingAssetsPath, alt);
        }
        if (string.IsNullOrEmpty(content)) {
            Debug.LogError($"[SpeakerYamlLoader] 文件不存在: 已尝试 StreamingAssets 与 persistentDataPath 下的 {fileName}");
            return 0;
        }
        int count = Parse(content);
        LogLoadResult(count, path);
        OnLoaded?.Invoke(count);
        return count;
    }

    string ReadFileContent(string name) {
#if UNITY_ANDROID && !UNITY_EDITOR
        string fromStreaming = StreamingAssetsHelper.ReadAllText(name);
        if (!string.IsNullOrEmpty(fromStreaming)) return fromStreaming;
        var p = Path.Combine(Application.persistentDataPath, name);
        if (File.Exists(p)) return File.ReadAllText(p);
        return null;
#else
        var p = Path.Combine(Application.streamingAssetsPath, name);
        if (File.Exists(p)) return File.ReadAllText(p);
        p = Path.Combine(Application.persistentDataPath, name);
        if (File.Exists(p)) return File.ReadAllText(p);
        if (File.Exists(name)) return File.ReadAllText(name);
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        var cppPaths = new[] {
            Path.Combine(projectRoot, "..", "cpp", name),
            Path.Combine(projectRoot, "..", "cpp", "src", name),
            Path.Combine(projectRoot, "..", name),
        };
        foreach (var cp in cppPaths) {
            if (File.Exists(cp)) return File.ReadAllText(cp);
        }
        return null;
#endif
    }

    /// <summary>解析内容。支持 JSON 和 YAML 两种格式。优先尝试 JSON。</summary>
    public int Parse(string content) {
        speakers.Clear();
        if (string.IsNullOrWhiteSpace(content)) return 0;

        content = content.Trim();
        int count = 0;
        if (content.StartsWith("{")) {
            count = ParseJson(content);
        }
        if (count == 0 && (content.StartsWith("speakers:") || content.Contains("\n- id:"))) {
            count = ParseYaml(content);
        }
        return count;
    }

    void LogLoadResult(int count, string path) {
        Debug.Log($"[SpeakerYamlLoader] 解析到 {count} 个扬声器，路径: {path}");
        if (count > 0) {
            var first = speakers[0];
            var last = speakers[count - 1];
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var s in speakers) {
                if (s.x < minX) minX = s.x; if (s.x > maxX) maxX = s.x;
                if (s.y < minY) minY = s.y; if (s.y > maxY) maxY = s.y;
                if (s.z < minZ) minZ = s.z; if (s.z > maxZ) maxZ = s.z;
            }
            Debug.Log($"[SpeakerYamlLoader] id 范围 {first.id}..{last.id}, x:[{minX:F2},{maxX:F2}] y:[{minY:F2},{maxY:F2}] z:[{minZ:F2},{maxZ:F2}]");
        }
    }

    int ParseJson(string json) {
        try {
            var root = JsonUtility.FromJson<SpeakerListRoot>(json);
            if (root?.speakers != null) {
                speakers.AddRange(root.speakers);
                return speakers.Count;
            }
            // 兼容顶层数组格式 [...]
            if (json.TrimStart().StartsWith("[")) {
                var wrapped = "{\"speakers\":" + json + "}";
                root = JsonUtility.FromJson<SpeakerListRoot>(wrapped);
                if (root?.speakers != null) {
                    speakers.AddRange(root.speakers);
                    return speakers.Count;
                }
            }
        } catch (Exception e) {
            Debug.LogWarning($"[SpeakerYamlLoader] JSON 解析失败: {e.Message}");
        }
        return 0;
    }

    int ParseYaml(string yaml) {
        var list = new List<SpeakerEntry>();
        string[] lines = yaml.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        bool inSpeakers = false;
        SpeakerEntry cur = null;

        foreach (string line in lines) {
            string t = line.Trim();
            if (string.IsNullOrEmpty(t) || t.StartsWith("#")) continue;

            if (t == "speakers:" || t.StartsWith("speakers:")) {
                inSpeakers = true;
                continue;
            }

            if (!inSpeakers) continue;

            if (t.StartsWith("-") || Regex.IsMatch(t, @"^\s*-\s*id:")) {
                if (cur != null) list.Add(cur);
                cur = new SpeakerEntry();
                var idMatch = Regex.Match(t, @"id:\s*(\d+)");
                if (idMatch.Success) cur.id = int.Parse(idMatch.Groups[1].Value);
            } else if (cur != null) {
                var xMatch = Regex.Match(t, @"x:\s*([-\d.]+)");
                var yMatch = Regex.Match(t, @"y:\s*([-\d.]+)");
                var zMatch = Regex.Match(t, @"z:\s*([-\d.]+)");
                if (xMatch.Success) cur.x = float.Parse(xMatch.Groups[1].Value);
                if (yMatch.Success) cur.y = float.Parse(yMatch.Groups[1].Value);
                if (zMatch.Success) cur.z = float.Parse(zMatch.Groups[1].Value);
            }
        }
        if (cur != null) list.Add(cur);

        speakers.AddRange(list);
        return speakers.Count;
    }

    public List<SpeakerEntry> GetSpeakers() => speakers;
}

}
