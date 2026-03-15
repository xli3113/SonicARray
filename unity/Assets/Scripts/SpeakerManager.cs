using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;

[System.Serializable]
public class SpeakerData {
    public int id;
    public float x, y, z;
}

[System.Serializable]
public class SpeakerConfig {
    public List<SpeakerData> speakers;
}

public class SpeakerManager : MonoBehaviour {
    [Header("Configuration")]
    public string yamlFilePath = "speakers.yaml";
    public GameObject speakerPrefab;
    public Material defaultMaterial;
    public Material activeMaterial;
    
    [Header("Speaker Exclusions")]
    [Tooltip("排除不参与可视化的扬声器 ID（如 29 号超范围或低频炮）")]
    public List<int> excludedSpeakerIds = new List<int> { 29 };

    [Header("Multi-Source Colors")]
    [Tooltip("每个声源 ID (0-7) 对应的扬声器激活颜色；不足8个时循环使用")]
    public Color[] sourceColors = new Color[] {
        new Color(1.0f, 0.85f, 0.1f),  // 0: 黄
        new Color(0.0f, 0.9f, 1.0f),   // 1: 青
        new Color(1.0f, 0.2f, 0.9f),   // 2: 洋红
        new Color(0.2f, 1.0f, 0.3f),   // 3: 绿
        new Color(1.0f, 0.5f, 0.0f),   // 4: 橙
        new Color(0.4f, 0.5f, 1.0f),   // 5: 蓝紫
        new Color(1.0f, 0.2f, 0.2f),   // 6: 红
        new Color(1.0f, 1.0f, 1.0f),   // 7: 白
    };

    [Header("Visual Settings")]
    [Tooltip("扬声器长方体整体缩放，调大可让长方体更明显")]
    public float speakerScale = 0.5f;
    [Tooltip("是否显示数字标签，取消勾选则只显示长方体")]
    public bool showLabels = true;
    
    [Header("Speaker Visualization")]
    [Tooltip("长方体基础尺寸 (宽, 高, 深)，与 speakerScale 相乘得到最终大小")]
    public Vector3 speakerBaseSize = new Vector3(0.2f, 0.15f, 0.2f);
    public float maxHeightMultiplier = 3.0f; // 激活时最大高度倍数
    public Material highlightMaterial; // 外层高亮材质（激活时显示）
    public float highlightThickness = 0.02f; // 外层高亮长方体厚度
    
    private List<GameObject> speakerObjects = new List<GameObject>();
    private List<GameObject> highlightObjects = new List<GameObject>(); // 外层高亮对象
    private List<SpeakerData> speakers = new List<SpeakerData>();
    private Dictionary<int, float> speakerGains = new Dictionary<int, float>(); // 存储每个扬声器的增益

    // ── 多声源增益帧累积 ─────────────────────────────────────────────
    // speakerId → (dominant sourceId, max gain across sources)
    private readonly Dictionary<int, (int sourceId, float gain)> _pendingDominant =
        new Dictionary<int, (int, float)>();
    private bool _inGainFrame;

    // ── C++ VBAP feedback tracking ────────────────────────────────────
    private float _lastCppGainsTime = -999f;

    /// <summary>
    /// True when a /vbap/gains OSC message was received within the last 0.5 s.
    /// While true, SourceManager and SpatialSource suppress the local
    /// inverse-square approximation so C++ VBAP gains drive visualization.
    /// </summary>
    public bool HasFreshCppGains => UnityEngine.Time.time - _lastCppGainsTime < 0.5f;

    /// <summary>Called by VBAPGainReceiver after committing a C++ gain frame.</summary>
    public void NotifyCppGainsReceived() {
        _lastCppGainsTime = UnityEngine.Time.time;
    }
    
    void Start() {
        LoadSpeakers();
        CreateSpeakerVisualizations();
    }
    
    void LoadSpeakers() {
        string fullPath = Path.Combine(Application.streamingAssetsPath, yamlFilePath);
        
        if (!File.Exists(fullPath)) {
            // Try relative path
            fullPath = yamlFilePath;
            if (!File.Exists(fullPath)) {
                Debug.LogError($"Speaker YAML file not found: {yamlFilePath}");
                return;
            }
        }
        
        string yamlContent = File.ReadAllText(fullPath);
        ParseYAML(yamlContent);
        
        Debug.Log($"Loaded {speakers.Count} speakers from {fullPath}");
    }
    
    void ParseYAML(string yamlContent) {
        speakers.Clear();
        
        // Simple YAML parser for speaker data
        string[] lines = yamlContent.Split('\n');
        bool inSpeakersSection = false;
        SpeakerData currentSpeaker = null;
        
        foreach (string line in lines) {
            string trimmed = line.Trim();
            
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) {
                continue;
            }
            
            if (trimmed == "speakers:") {
                inSpeakersSection = true;
                continue;
            }
            
            if (inSpeakersSection) {
                if (trimmed.StartsWith("- id:")) {
                    if (currentSpeaker != null) {
                        speakers.Add(currentSpeaker);
                    }
                    currentSpeaker = new SpeakerData();
                    Match match = Regex.Match(trimmed, @"id:\s*(\d+)");
                    if (match.Success) {
                        currentSpeaker.id = int.Parse(match.Groups[1].Value);
                    }
                } else if (currentSpeaker != null) {
                    Match match;
                    if ((match = Regex.Match(trimmed, @"x:\s*([-\d.]+)")).Success) {
                        currentSpeaker.x = float.Parse(match.Groups[1].Value);
                    } else if ((match = Regex.Match(trimmed, @"y:\s*([-\d.]+)")).Success) {
                        currentSpeaker.y = float.Parse(match.Groups[1].Value);
                    } else if ((match = Regex.Match(trimmed, @"z:\s*([-\d.]+)")).Success) {
                        currentSpeaker.z = float.Parse(match.Groups[1].Value);
                    }
                }
            }
        }
        
        if (currentSpeaker != null) {
            speakers.Add(currentSpeaker);
        }
    }
    
    void CreateSpeakerVisualizations() {
        foreach (SpeakerData speaker in speakers) {
            if (excludedSpeakerIds != null && excludedSpeakerIds.Contains(speaker.id)) {
                Debug.Log($"[SpeakerManager] 跳过排除的扬声器 id={speaker.id}");
                continue;
            }
            GameObject speakerObj = CreateSpeakerObject(speaker);
            speakerObjects.Add(speakerObj);
        }
    }
    
    GameObject CreateSpeakerObject(SpeakerData speaker) {
        GameObject obj;
        
        if (speakerPrefab != null) {
            obj = Instantiate(speakerPrefab);
        } else {
            // 使用长方体而不是球体
            obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        }
        
        obj.name = $"Speaker_{speaker.id}";
        obj.transform.position = new Vector3(speaker.x, speaker.y, speaker.z);
        
        // 设置长方体尺寸
        obj.transform.localScale = new Vector3(
            speakerBaseSize.x * speakerScale,
            speakerBaseSize.y * speakerScale,
            speakerBaseSize.z * speakerScale
        );
        obj.transform.SetParent(transform);
        
        // 存储原始位置和尺寸（用于高度动画）
        SpeakerVisualData visualData = obj.AddComponent<SpeakerVisualData>();
        visualData.baseHeight = speakerBaseSize.y * speakerScale;
        visualData.basePosition = obj.transform.position;
        
        // Add material
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null && defaultMaterial != null) {
            renderer.material = defaultMaterial;
        }
        
        // 创建外层高亮长方体（初始隐藏）
        GameObject highlightObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        highlightObj.name = $"SpeakerHighlight_{speaker.id}";
        highlightObj.transform.SetParent(obj.transform);
        highlightObj.transform.localPosition = Vector3.zero;
        highlightObj.transform.localScale = Vector3.one + Vector3.one * highlightThickness;
        
        Renderer highlightRenderer = highlightObj.GetComponent<Renderer>();
        if (highlightRenderer != null) {
            if (highlightMaterial != null) {
                highlightRenderer.material = highlightMaterial;
            } else {
                // 创建默认高亮材质
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(1f, 0.2f, 0.2f, 0.5f); // 半透明红色
                mat.SetFloat("_Mode", 3); // Transparent mode
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
                highlightRenderer.material = mat;
            }
            highlightRenderer.enabled = false; // 初始隐藏
        }
        
        highlightObjects.Add(highlightObj);
        
        // Add label
        if (showLabels) {
            GameObject labelObj = new GameObject($"Label_{speaker.id}");
            labelObj.transform.SetParent(obj.transform);
            labelObj.transform.localPosition = Vector3.up * (speakerBaseSize.y * speakerScale * 0.5f + 0.05f);
            
            TextMesh textMesh = labelObj.AddComponent<TextMesh>();
            textMesh.text = speaker.id.ToString();
            textMesh.fontSize = 20;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.color = Color.white;
            
            // Make label face camera (update in LateUpdate)
            LabelFacer facer = labelObj.AddComponent<LabelFacer>();
        }
        
        return obj;
    }
    
    // ── 多声源增益帧 API（由 SourceManager 调用）──────────────────────

    /// <summary>
    /// 开始新的增益帧。清空 dominant-source 缓冲。由 SourceManager.Update() 首先调用。
    /// </summary>
    public void BeginGainFrame() {
        _pendingDominant.Clear();
        _inGainFrame = true;
    }

    /// <summary>
    /// 累积指定扬声器的增益贡献。
    /// 采用 "dominant source" 策略：保留增益最高的那个声源颜色。
    /// </summary>
    /// <param name="speakerId">扬声器 ID</param>
    /// <param name="gain">该声源对此扬声器的增益 [0,1]</param>
    /// <param name="sourceId">声源编号 0-7，用于颜色映射；-1 表示无来源（用默认激活色）</param>
    public void AccumulateSpeakerGain(int speakerId, float gain, int sourceId = -1) {
        if (!_inGainFrame) return;
        if (!_pendingDominant.TryGetValue(speakerId, out var cur) || gain > cur.gain)
            _pendingDominant[speakerId] = (sourceId, Mathf.Clamp01(gain));
    }

    /// <summary>
    /// 提交增益帧：将本帧 dominant-source 结果应用到所有扬声器可视化。
    /// 由 SourceManager.Update() 最后调用。
    /// </summary>
    public void CommitGainFrame() {
        _inGainFrame = false;
        foreach (var spk in speakers) {
            if (excludedSpeakerIds != null && excludedSpeakerIds.Contains(spk.id)) continue;
            if (_pendingDominant.TryGetValue(spk.id, out var entry)) {
                speakerGains[spk.id] = entry.gain;
                Color c = GetSourceColor(entry.sourceId);
                ApplyGainVisual(spk.id, entry.gain, c);
            } else {
                speakerGains[spk.id] = 0f;
                ApplyGainVisual(spk.id, 0f, Color.white);
            }
        }
    }

    // ── 单声源兼容接口（直接调用，不走增益帧）────────────────────────

    /// <summary>
    /// 直接更新单个扬声器增益并立即应用视觉（单声源/兼容模式）。
    /// 若 SourceManager 正在运行增益帧，此调用被忽略。
    /// </summary>
    public void UpdateSpeakerState(int speakerId, float gain) {
        if (_inGainFrame) return;
        speakerGains[speakerId] = gain;
        ApplyGainVisual(speakerId, gain, Color.white);
    }

    // ── 工具方法 ─────────────────────────────────────────────────────

    /// <summary>根据声源编号返回对应颜色；超出范围时循环。</summary>
    public Color GetSourceColor(int sourceId) {
        if (sourceColors == null || sourceColors.Length == 0) return Color.white;
        if (sourceId < 0) return Color.white;
        return sourceColors[sourceId % sourceColors.Length];
    }

    /// <summary>
    /// 核心可视化：将增益 + 声源颜色映射到长方体高度、颜色、高亮外框。
    /// </summary>
    private void ApplyGainVisual(int speakerId, float gain, Color sourceColor) {
        GameObject speakerObj = GetSpeakerObject(speakerId);
        if (speakerObj == null) return;

        SpeakerVisualData visualData = speakerObj.GetComponent<SpeakerVisualData>();
        if (visualData == null) return;

        bool active = gain > 0.01f;

        // ── 高度（底部固定）──
        float heightMultiplier = 1f + (maxHeightMultiplier - 1f) * gain;
        float newHeight = visualData.baseHeight * heightMultiplier;
        Vector3 s = speakerObj.transform.localScale;
        speakerObj.transform.localScale = new Vector3(s.x, newHeight, s.z);
        speakerObj.transform.position =
            visualData.basePosition + Vector3.up * ((newHeight - visualData.baseHeight) * 0.5f);

        // ── 材质颜色（激活时使用声源颜色，关闭时恢复默认）──
        var rend = speakerObj.GetComponent<Renderer>();
        if (rend != null) {
            if (active) {
                // 实例化材质避免共享材质被污染
                if (rend.sharedMaterial == defaultMaterial || rend.sharedMaterial == activeMaterial) {
                    rend.material = activeMaterial != null
                        ? new Material(activeMaterial)
                        : new Material(Shader.Find("Standard"));
                }
                Color c = sourceColor;
                c.a = Mathf.Lerp(0.6f, 1f, gain);
                rend.material.color = c;
                // Emission（让激活扬声器发光）
                if (rend.material.HasProperty("_EmissionColor")) {
                    rend.material.EnableKeyword("_EMISSION");
                    rend.material.SetColor("_EmissionColor", c * (gain * 1.5f));
                }
            } else {
                if (defaultMaterial != null) rend.sharedMaterial = defaultMaterial;
            }
        }

        // ── 高亮外框 ──
        foreach (GameObject obj in highlightObjects) {
            if (obj == null || obj.name != $"SpeakerHighlight_{speakerId}") continue;
            var hr = obj.GetComponent<Renderer>();
            if (hr == null) break;
            hr.enabled = active;
            if (active) {
                Vector3 bs = new Vector3(
                    speakerBaseSize.x * speakerScale,
                    speakerBaseSize.y * speakerScale,
                    speakerBaseSize.z * speakerScale);
                obj.transform.localScale = new Vector3(
                    (bs.x + highlightThickness) / bs.x,
                    (newHeight + highlightThickness) / Mathf.Max(bs.y, 0.001f),
                    (bs.z + highlightThickness) / bs.z);
                obj.transform.localPosition = Vector3.zero;
                // 高亮框颜色跟随声源颜色（半透明）
                Color hc = sourceColor;
                hc.a = 0.4f;
                hr.material.color = hc;
            }
            break;
        }
    }
    
    void LateUpdate() {
        // 更新标签朝向相机
        if (showLabels && Camera.main != null) {
            foreach (GameObject obj in speakerObjects) {
                Transform labelTransform = obj.transform.Find($"Label_{obj.name.Split('_')[1]}");
                if (labelTransform != null) {
                    labelTransform.LookAt(Camera.main.transform);
                    labelTransform.Rotate(0, 180, 0);
                }
            }
        }
    }
    
    public List<SpeakerData> GetSpeakers() {
        return speakers;
    }
    
    public GameObject GetSpeakerObject(int id) {
        // 精确匹配，避免 id=1 误匹配 "Speaker_12"、"Speaker_13" 等
        string targetName = $"Speaker_{id}";
        foreach (GameObject obj in speakerObjects) {
            if (obj != null && obj.name == targetName)
                return obj;
        }
        return null;
    }
    
    public void HighlightSpeakers(List<int> speakerIds, bool highlight) {
        // 这个方法保留用于兼容性，但实际使用 UpdateSpeakerState
        foreach (int id in speakerIds) {
            float gain = highlight ? 1.0f : 0.0f;
            UpdateSpeakerState(id, gain);
        }
    }
}

// 辅助类：存储扬声器可视化数据
public class SpeakerVisualData : MonoBehaviour {
    public float baseHeight;
    public Vector3 basePosition;
}

// 辅助类：让标签始终面向相机
public class LabelFacer : MonoBehaviour {
    void LateUpdate() {
        if (Camera.main != null) {
            transform.LookAt(Camera.main.transform);
            transform.Rotate(0, 180, 0);
        }
    }
}
