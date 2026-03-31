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

    [Header("Backend Connection")]
    [Tooltip("IP address of the PC running the C++ VBAP backend. " +
             "Quest sends OSC to this IP on port 7000. Use 127.0.0.1 only when running both on the same machine.")]
    public string backendIP = "10.10.10.3";
    public Material defaultMaterial;
    public Material activeMaterial;
    
    [Header("World Position (AR Alignment)")]
    [Tooltip("Offset the entire speaker array. In Follow-Head mode this is a local offset " +
             "relative to the camera; otherwise it is a world-space offset from the origin.")]
    public Vector3 arrayOriginOffset = new Vector3(0f, 0f, 2f);
    [Tooltip("When enabled the speaker array stays fixed relative to the user's head (useful " +
             "for test/debug scenes). Disable for AR so the array stays fixed in the room.")]
    public bool followHeadMovement = false;

    [Header("AR Calibration")]
    [Tooltip("Speaker ID of the physical reference speaker used for calibration. " +
             "Walk to this speaker and call CalibrateToHead() to align the virtual array.")]
    public int calibrationReferenceSpeakerId = 0;

    /// <summary>
    /// Calibration: shifts arrayOriginOffset so the virtual speaker <calibrationReferenceSpeakerId>
    /// aligns with the current head (camera) position. Walk up to the physical reference speaker,
    /// then call this (e.g., from a UI button or gesture).
    /// </summary>
    public void CalibrateToHead() {
        Camera cam = Camera.main;
        if (cam == null) { Debug.LogError("[SpeakerManager] No main camera for calibration."); return; }

        // Find the YAML position of the reference speaker
        SpeakerData refSpk = speakers.Find(s => s.id == calibrationReferenceSpeakerId);
        if (refSpk == null) { Debug.LogError($"[SpeakerManager] Calibration speaker {calibrationReferenceSpeakerId} not found."); return; }

        // Current head position in world space
        Vector3 headPos = cam.transform.position;
        // Where the virtual reference speaker currently sits relative to array origin
        Vector3 speakerLocalPos = new Vector3(refSpk.x, refSpk.z, refSpk.y);

        // Required offset: head should coincide with reference speaker world position
        // worldPos = arrayOriginOffset + speakerLocalPos  →  arrayOriginOffset = headPos - speakerLocalPos
        arrayOriginOffset = headPos - speakerLocalPos;
        transform.position = arrayOriginOffset;

        // Speakers use localPosition — moving the parent (transform) automatically
        // repositions all children. Just refresh the cached world basePosition.
        for (int i = 0; i < speakerObjects.Count; i++) {
            SpeakerVisualData vd = speakerObjects[i].GetComponent<SpeakerVisualData>();
            if (vd != null) vd.basePosition = speakerObjects[i].transform.position;
        }

        Debug.Log($"[SpeakerManager] Calibrated: arrayOriginOffset={arrayOriginOffset} (ref spk {calibrationReferenceSpeakerId} → head at {headPos})");
    }

    [Header("Visual Settings")]
    [Tooltip("扬声器长方体整体缩放，调大可让长方体更明显")]
    public float speakerScale = 0.5f;
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

    // Per-source gains received from backend: sourceId -> gains array indexed by speaker order
    private Dictionary<int, float[]> sourceGains = new Dictionary<int, float[]>();

    // Per-source colors registered by SpatialSource instances
    private Dictionary<int, Color> sourceColors = new Dictionary<int, Color>();

    /// <summary>Called by each SpatialSource on Start so speakers can tint by dominant source.</summary>
    public void RegisterSourceColor(int sourceId, Color color) {
        sourceColors[sourceId] = color;
    }

    /// <summary>
    /// Called by SpatialSource.OnDestroy. Removes this source's gains and resets all
    /// speakers to idle if no sources remain.
    /// </summary>
    public void ClearSourceGains(int sourceId) {
        sourceGains.Remove(sourceId);
        sourceColors.Remove(sourceId);

        if (sourceGains.Count == 0) {
            // Reset the ID counter so the next ball created starts from 0 again,
            // preventing sourceId from accumulating past kMaxSources across create/destroy cycles.
            SpatialSource.ResetIdCounter();

            // No sources left — reset all speakers to default appearance
            foreach (SpeakerData spk in speakers) {
                speakerGains[spk.id] = 0f;
                GameObject obj = GetSpeakerObject(spk.id);
                if (obj == null) continue;

                // Restore base scale
                SpeakerVisualData vd = obj.GetComponent<SpeakerVisualData>();
                if (vd != null) {
                    obj.transform.localScale = new Vector3(
                        speakerBaseSize.x * speakerScale,
                        vd.baseHeight,
                        speakerBaseSize.z * speakerScale);
                    obj.transform.position = vd.basePosition;
                }

                // Restore default material
                Renderer r = obj.GetComponent<Renderer>();
                if (r != null && defaultMaterial != null) {
                    r.material = defaultMaterial;
                    if (r.material.HasProperty("_EmissionColor")) {
                        r.material.SetColor("_EmissionColor", Color.black);
                    }
                }

                // Hide highlight
                foreach (GameObject hl in highlightObjects) {
                    if (hl.name.Contains($"Highlight_{spk.id}")) {
                        Renderer hr = hl.GetComponent<Renderer>();
                        if (hr != null) hr.enabled = false;
                        break;
                    }
                }
            }
            Debug.Log("[SpeakerManager] All sources removed — speakers reset to idle.");
        }
    }
    
    void Awake() {
#if UNITY_EDITOR
        backendIP = "127.0.0.1";
#endif
    }

    void Start() {
        if (followHeadMovement)
        {
            // Parent the array to the head camera so it moves with the user
            Transform headAnchor = FindHeadAnchor();
            if (headAnchor != null)
            {
                transform.SetParent(headAnchor, worldPositionStays: false);
                Debug.Log($"[SpeakerManager] Following head: parented to '{headAnchor.name}'");
            }
            else
            {
                Debug.LogWarning("[SpeakerManager] followHeadMovement=true but no camera found; using world position.");
            }
            transform.localPosition = arrayOriginOffset;
            transform.localRotation = Quaternion.identity;
        }
        else
        {
            transform.position = arrayOriginOffset;
        }
        LoadSpeakers();
        CreateSpeakerVisualizations();
        SendHello();
    }

    /// <summary>
    /// Sends /spatial/hello to the backend so it learns the Quest's IP immediately,
    /// before any source is created. This unblocks the two-way OSC handshake.
    /// </summary>
    void SendHello() {
        // Hello is now sent from OSCReceiver's socket (port 7002) via SpatialSource.
        // SpeakerManager no longer needs its own send socket.
        // This stub is kept so the Start() call compiles.
    }

    Transform FindHeadAnchor()
    {
        // Prefer OVRCameraRig's CenterEyeAnchor for accurate head position
        OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
        if (rig != null)
        {
            Transform ts = rig.transform.Find("TrackingSpace");
            if (ts != null)
            {
                Transform cea = ts.Find("CenterEyeAnchor");
                if (cea != null) return cea;
            }
            return rig.centerEyeAnchor;
        }
        // Fallback to main camera
        return Camera.main != null ? Camera.main.transform : null;
    }
    
    void LoadSpeakers() {
        string yamlContent = null;
        string fullPath = Path.Combine(Application.streamingAssetsPath, yamlFilePath);
        
        // Android: StreamingAssets 在 APK 内，需用 UnityWebRequest
        yamlContent = StreamingAssetsHelper.ReadAllText(yamlFilePath);
        if (string.IsNullOrEmpty(yamlContent)) {
            fullPath = Path.Combine(Application.persistentDataPath, yamlFilePath);
            if (File.Exists(fullPath)) {
                yamlContent = File.ReadAllText(fullPath);
            }
        }
        if (string.IsNullOrEmpty(yamlContent) && File.Exists(yamlFilePath)) {
            fullPath = yamlFilePath;
            yamlContent = File.ReadAllText(fullPath);
        }
        
        if (string.IsNullOrEmpty(yamlContent)) {
            Debug.LogError($"Speaker YAML file not found: {yamlFilePath}");
            return;
        }
        
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

        // Parent first so localPosition = position relative to array center.
        // This ensures source directions (ball - SpeakerManager.position) always
        // match YAML directions regardless of arrayOriginOffset.
        obj.transform.SetParent(transform);

        // YAML/C++ convention: x=left-right, y=forward-back, z=height
        // Unity convention:    X=left-right, Y=up,           Z=forward
        obj.transform.localPosition = new Vector3(speaker.x, speaker.z, speaker.y);

        // 设置长方体尺寸
        obj.transform.localScale = new Vector3(
            speakerBaseSize.x * speakerScale,
            speakerBaseSize.y * speakerScale,
            speakerBaseSize.z * speakerScale
        );
        
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
        
        
        return obj;
    }
    
    // 更新扬声器状态（根据增益）
    public void UpdateSpeakerState(int speakerId, float gain) {
        UpdateSpeakerState(speakerId, gain, Color.white);
    }

    public void UpdateSpeakerState(int speakerId, float gain, Color sourceColor) {
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
        
        // 更新材质颜色: active = dominant-source color at gain brightness; idle = default
        Renderer renderer = speakerObj.GetComponent<Renderer>();
        if (renderer != null) {
            if (gain > 0.01f) {
                // Tint by the dominant source color, brightness scales with gain
                if (renderer.material == null || renderer.material == defaultMaterial) {
                    renderer.material = activeMaterial != null
                        ? new Material(activeMaterial)
                        : new Material(Shader.Find("Standard"));
                }
                // Use full-brightness blended color so mixing is clearly visible.
                // Emission is also set so the color is visible regardless of scene lighting.
                Color col = sourceColor;
                col.a = 1f;
                renderer.material.color = col;
                if (renderer.material.HasProperty("_EmissionColor")) {
                    renderer.material.EnableKeyword("_EMISSION");
                    renderer.material.SetColor("_EmissionColor", sourceColor * 0.8f);
                }
            } else if (defaultMaterial != null) {
                renderer.material = defaultMaterial;
            }
        }
    }
    
    /// <summary>
    /// Called by OSCReceiver (main thread) with VBAP gains from the C++ backend.
    /// gains[] is indexed by speaker order (matches speakers.yaml load order).
    /// Merges across all sources using max, then drives speaker visuals.
    /// </summary>
    public void ApplyBackendGains(int sourceId, float[] gains) {
        sourceGains[sourceId] = gains;

        int activeAfterMerge = 0;
        float dbgTop1 = 0f, dbgTop2 = 0f, dbgTop3 = 0f;
        int dbgTop1id = -1, dbgTop2id = -1, dbgTop3id = -1;

        for (int i = 0; i < speakers.Count; i++) {
            float maxGain    = 0f;
            Color blended    = new Color(0f, 0f, 0f, 0f);  // zero alpha avoids polluting the blend
            float totalGain  = 0f;

            foreach (var kvp in sourceGains) {
                if (i >= kvp.Value.Length) continue;
                float g = kvp.Value[i];
                if (g > maxGain) maxGain = g;
                // Option D: accumulate each source's color weighted by its gain
                if (g > 0f && sourceColors.TryGetValue(kvp.Key, out Color sc)) {
                    blended   += sc * g;
                    totalGain += g;
                }
            }

            // Normalize blended color; fall back to white if no source active
            Color finalColor = totalGain > 1e-4f ? blended / totalGain : Color.white;
            UpdateSpeakerState(speakers[i].id, maxGain, finalColor);

            #if UNITY_EDITOR
            if (maxGain > 0.01f) activeAfterMerge++;
            int sid = speakers[i].id;
            if (maxGain > dbgTop1) { dbgTop3 = dbgTop2; dbgTop3id = dbgTop2id; dbgTop2 = dbgTop1; dbgTop2id = dbgTop1id; dbgTop1 = maxGain; dbgTop1id = sid; }
            else if (maxGain > dbgTop2) { dbgTop3 = dbgTop2; dbgTop3id = dbgTop2id; dbgTop2 = maxGain; dbgTop2id = sid; }
            else if (maxGain > dbgTop3) { dbgTop3 = maxGain; dbgTop3id = sid; }
            #endif
        }

        #if UNITY_EDITOR
        Debug.Log($"[SpeakerManager] ApplyBackendGains src={sourceId}  gains.len={gains.Length}  speakers={speakers.Count}" +
                  $"  active(>0.01)={activeAfterMerge}" +
                  $"  top3: spk{dbgTop1id}={dbgTop1:F3}  spk{dbgTop2id}={dbgTop2:F3}  spk{dbgTop3id}={dbgTop3:F3}");
        #endif
    }

    /// <summary>Returns the current merged (max across all sources) gain for a speaker.</summary>
    public float GetSpeakerGain(int speakerId) {
        return speakerGains.TryGetValue(speakerId, out float g) ? g : 0f;
    }

    /// <summary>
    /// Returns the gain contribution of one specific source on one speaker.
    /// Use this for per-source line rendering so each ball only shows its own 3 speakers.
    /// </summary>
    public float GetSpeakerGainForSource(int speakerId, int sourceId) {
        if (!sourceGains.TryGetValue(sourceId, out float[] gains)) return 0f;
        // gains[] is indexed by speaker order in the loaded list, not by speaker id
        List<SpeakerData> spks = GetSpeakers();
        int idx = spks.FindIndex(s => s.id == speakerId);
        if (idx < 0 || idx >= gains.Length) return 0f;
        return gains[idx];
    }

    public List<SpeakerData> GetSpeakers() {
        return speakers;
    }

    /// <summary>Returns the current world-space position of a speaker's GameObject,
    /// accounting for arrayOriginOffset. Use this instead of raw YAML x/y/z values.</summary>
    public Vector3 GetSpeakerWorldPosition(int id) {
        GameObject obj = GetSpeakerObject(id);
        return obj != null ? obj.transform.position : Vector3.zero;
    }

    public GameObject GetSpeakerObject(int id) {
        foreach (GameObject obj in speakerObjects) {
            if (obj.name == $"Speaker_{id}") {
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
