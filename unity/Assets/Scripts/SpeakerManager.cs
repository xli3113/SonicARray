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
    
    // 更新扬声器状态（根据增益）
    public void UpdateSpeakerState(int speakerId, float gain) {
        speakerGains[speakerId] = gain;
        
        // 调试日志（仅在 Editor 中显示，且增益变化明显时）
        #if UNITY_EDITOR
        if (gain > 0.01f || (speakerGains.ContainsKey(speakerId) && Mathf.Abs(speakerGains[speakerId] - gain) > 0.1f)) {
            Debug.Log($"[Speaker] ID={speakerId}, Gain={gain:F3}");
        }
        #endif
        
        GameObject speakerObj = GetSpeakerObject(speakerId);
        if (speakerObj == null) return;
        
        SpeakerVisualData visualData = speakerObj.GetComponent<SpeakerVisualData>();
        if (visualData == null) return;
        
        // 计算高度倍数（基于增益，0-1映射到1-maxHeightMultiplier）
        float heightMultiplier = 1.0f + (maxHeightMultiplier - 1.0f) * gain;
        float newHeight = visualData.baseHeight * heightMultiplier;
        
        // 更新高度
        Vector3 currentScale = speakerObj.transform.localScale;
        speakerObj.transform.localScale = new Vector3(
            currentScale.x,
            newHeight,
            currentScale.z
        );
        
        // 调整位置，使底部保持在同一位置
        Vector3 basePos = visualData.basePosition;
        float heightDiff = (newHeight - visualData.baseHeight) * 0.5f;
        speakerObj.transform.position = basePos + Vector3.up * heightDiff;
        
        // 更新高亮显示
        GameObject highlightObj = null;
        foreach (GameObject obj in highlightObjects) {
            if (obj.name.Contains($"Highlight_{speakerId}")) {
                highlightObj = obj;
                break;
            }
        }
        if (highlightObj != null) {
            Renderer highlightRenderer = highlightObj.GetComponent<Renderer>();
            if (highlightRenderer != null) {
                highlightRenderer.enabled = gain > 0.01f; // 增益大于阈值时显示
                
                // 更新高亮高度（相对于父对象的本地缩放）
                if (highlightRenderer.enabled) {
                    // 高亮对象应该比主对象稍大
                    Vector3 baseScale = new Vector3(
                        speakerBaseSize.x * speakerScale,
                        speakerBaseSize.y * speakerScale,
                        speakerBaseSize.z * speakerScale
                    );
                    
                    highlightObj.transform.localScale = new Vector3(
                        (baseScale.x + highlightThickness) / baseScale.x,
                        (newHeight + highlightThickness) / baseScale.y,
                        (baseScale.z + highlightThickness) / baseScale.z
                    );
                    highlightObj.transform.localPosition = Vector3.zero; // 与父对象中心对齐
                }
            }
        }
        
        // 更新材质颜色（根据增益强度）
        Renderer renderer = speakerObj.GetComponent<Renderer>();
        if (renderer != null) {
            if (gain > 0.01f && activeMaterial != null) {
                // 使用激活材质，但可以根据增益调整颜色强度
                renderer.material = activeMaterial;
                if (renderer.material.HasProperty("_Color")) {
                    Color baseColor = activeMaterial.color;
                    renderer.material.color = new Color(
                        baseColor.r,
                        baseColor.g,
                        baseColor.b,
                        Mathf.Lerp(0.5f, 1.0f, gain)
                    );
                }
            } else if (defaultMaterial != null) {
                renderer.material = defaultMaterial;
            }
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
        foreach (GameObject obj in speakerObjects) {
            if (obj.name.Contains(id.ToString())) {
                return obj;
            }
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
