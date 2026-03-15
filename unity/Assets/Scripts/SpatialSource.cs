using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个空间声源。可独立使用（单声源），也可由 SourceManager 统一管理（多声源）。
///
/// 职责：
///   1. 每帧按 updateRate 通过 OSC 发送位置到 C++ 后端
///      - multiSourceOSC = true  → /spatial/source_pos (int id, float x, float y, float z)
///      - multiSourceOSC = false → /spatial/source_pos (float x, float y, float z)  [单声源兼容]
///   2. ContributeGainsToManager() — 由 SourceManager 每帧调用，计算最近3个扬声器并累积增益
///   3. SetSelected(bool) — 由 SourceManager 调用，切换高亮外观
/// </summary>
public class SpatialSource : MonoBehaviour {

    // ──────────────────────────────────────────────────────────────────
    // Inspector 字段
    // ──────────────────────────────────────────────────────────────────

    [Header("OSC Settings")]
    public bool enableOSC = false;
    public string oscIP = "127.0.0.1";
    public int oscPort = 7000;
    public float updateRate = 60.0f;
    [Tooltip("true: 发送 (id, x, y, z)；false: 仅 (x, y, z)，兼容单声源后端")]
    public bool multiSourceOSC = true;

    [Header("Source Identity")]
    [Tooltip("声源编号 0-7，由 SourceManager 在创建时设置")]
    public int sourceId = 0;

    [Header("Visual Settings")]
    [Tooltip("未选中时的材质，留空则自动创建黄色")]
    public Material sourceMaterial;
    [Tooltip("选中时的材质，留空则自动创建青色")]
    public Material selectedMaterial;
    public float sourceScale = 0.15f;

    [Header("Line Renderer")]
    public Color lineColor = Color.red;
    public float lineWidth = 0.02f;
    public bool showLines = true;

    // ──────────────────────────────────────────────────────────────────
    // 内部状态
    // ──────────────────────────────────────────────────────────────────

    private OSCClient _oscClient;
    private SpeakerManager _speakerManager;
    private readonly List<LineRenderer> _lines = new List<LineRenderer>();
    private float _lastOscTime;
    private Renderer _ballRenderer;
    private Material _runtimeDefaultMat;
    private Material _runtimeSelectedMat;
    private bool _isSelected;
    private TextMesh _idLabel;

    // ──────────────────────────────────────────────────────────────────
    // Unity 生命周期
    // ──────────────────────────────────────────────────────────────────

    void Start() {
        _speakerManager = FindObjectOfType<SpeakerManager>();

        if (enableOSC)
            _oscClient = new OSCClient(oscIP, oscPort);

        BuildVisual();

#if UNITY_EDITOR
        if (!GetComponent<SimpleDrag>())
            gameObject.AddComponent<SimpleDrag>();
#endif
    }

    void Update() {
        // OSC 发送（按设定频率）—— 无论后端是否在线都发送位置
        if (Time.time - _lastOscTime >= 1f / Mathf.Max(updateRate, 1f)) {
            SendOSCPosition();
            _lastOscTime = Time.time;
        }

        // 单声源降级：仅当没有 SourceManager 且 C++ 后端未提供增益时驱动可视化
        if (FindObjectOfType<SourceManager>() == null) {
            if (_speakerManager == null || !_speakerManager.HasFreshCppGains)
                SingleSourceFallbackGain();
        }
    }

    void OnDestroy() {
        _oscClient?.Close();
    }

    // ──────────────────────────────────────────────────────────────────
    // Public API（供 SourceManager 调用）
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 计算此声源对各扬声器的近似增益，并累积到 SpeakerManager 的当帧增益帧中。
    /// 由 SourceManager.Update() 统一调用，不要在 Update() 中直接调用。
    /// </summary>
    public void ContributeGainsToManager() {
        if (_speakerManager == null) return;
        // Guard: if C++ backend is sending true VBAP gains, skip local approximation.
        // (SourceManager already skips calling this, but belt-and-suspenders.)
        if (_speakerManager.HasFreshCppGains) return;
        var speakers = _speakerManager.GetSpeakers();
        if (speakers == null || speakers.Count == 0) return;

        Vector3 pos = transform.position;

        // 计算到每个扬声器的距离及近似增益
        var dists = new List<(int id, float dist, float gain)>();
        foreach (var spk in speakers) {
            float d = Vector3.Distance(pos, new Vector3(spk.x, spk.y, spk.z));
            float g = 1f / (1f + d * d);       // 平方反比近似（VBAP 近似可视化）
            dists.Add((spk.id, d, g));
        }
        dists.Sort((a, b) => a.dist.CompareTo(b.dist));

        // 取最近 3 个向 SpeakerManager 累积（携带 sourceId，用于颜色映射）
        int top = Mathf.Min(3, dists.Count);
        for (int i = 0; i < top; i++)
            _speakerManager.AccumulateSpeakerGain(dists[i].id, dists[i].gain, sourceId);

        // 更新连线
        if (showLines) UpdateLines(dists, top);
    }

    /// <summary>设置选中状态，更新球体外观。由 SourceManager 调用。</summary>
    public void SetSelected(bool selected) {
        _isSelected = selected;
        RefreshMaterial();
        if (_idLabel != null)
            _idLabel.color = selected ? Color.cyan : Color.white;
    }

    /// <summary>刷新 ID 标签文字（sourceId 变更后调用）。</summary>
    public void RefreshLabel() {
        if (_idLabel != null) _idLabel.text = sourceId.ToString();
    }

    /// <summary>手动触发 OSC 发送（供 EditorDebugHelper 通过 SendMessage 调用）。</summary>
    public void SendOSCPosition() {
        if (!enableOSC || _oscClient == null) return;
        Vector3 p = transform.position;
        if (multiSourceOSC)
            _oscClient.Send("/spatial/source_pos", sourceId, p.x, p.y, p.z);
        else
            _oscClient.Send("/spatial/source_pos", p.x, p.y, p.z);
    }

    // ──────────────────────────────────────────────────────────────────
    // 私有辅助
    // ──────────────────────────────────────────────────────────────────

    void BuildVisual() {
        // 主球体
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "SourceBall";
        sphere.transform.SetParent(transform);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale = Vector3.one * sourceScale;
        Destroy(sphere.GetComponent<Collider>());   // 碰撞体留在父对象

        _ballRenderer = sphere.GetComponent<Renderer>();

        // 默认材质（未选中：黄色）
        if (sourceMaterial != null) {
            _runtimeDefaultMat = new Material(sourceMaterial);
        } else {
            var s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _runtimeDefaultMat = new Material(s);
            _runtimeDefaultMat.color = new Color(1f, 0.8f, 0.2f);
        }

        // 选中材质（青色）
        if (selectedMaterial != null) {
            _runtimeSelectedMat = new Material(selectedMaterial);
        } else {
            _runtimeSelectedMat = new Material(_runtimeDefaultMat);
            _runtimeSelectedMat.color = Color.cyan;
        }

        _ballRenderer.material = _runtimeDefaultMat;

        // ID 标签
        var labelGo = new GameObject("SourceLabel");
        labelGo.transform.SetParent(transform);
        labelGo.transform.localPosition = Vector3.up * (sourceScale * 0.6f + 0.06f);
        _idLabel = labelGo.AddComponent<TextMesh>();
        _idLabel.text = sourceId.ToString();
        _idLabel.fontSize = 24;
        _idLabel.anchor = TextAnchor.MiddleCenter;
        _idLabel.color = Color.white;
        labelGo.AddComponent<LabelFacer>();

        // Collider + Rigidbody（让 SimpleDrag / 手柄 Grab 可用）
        var col = gameObject.GetComponent<SphereCollider>();
        if (col == null) col = gameObject.AddComponent<SphereCollider>();
        col.radius = sourceScale * 1.2f;

        var rb = gameObject.GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void RefreshMaterial() {
        if (_ballRenderer == null) return;
        _ballRenderer.material = _isSelected ? _runtimeSelectedMat : _runtimeDefaultMat;
    }

    /// <summary>单声源降级：无 SourceManager 时，自己更新扬声器增益。（向后兼容）</summary>
    void SingleSourceFallbackGain() {
        if (_speakerManager == null) return;
        var speakers = _speakerManager.GetSpeakers();
        if (speakers == null || speakers.Count == 0) return;

        Vector3 pos = transform.position;
        var dists = new List<(int id, float dist, float gain)>();
        foreach (var spk in speakers) {
            float d = Vector3.Distance(pos, new Vector3(spk.x, spk.y, spk.z));
            dists.Add((spk.id, d, 1f / (1f + d * d)));
        }
        dists.Sort((a, b) => a.dist.CompareTo(b.dist));

        // 非活跃扬声器清零
        foreach (var spk in speakers)
            _speakerManager.UpdateSpeakerState(spk.id, 0f);

        int top = Mathf.Min(3, dists.Count);
        for (int i = 0; i < top; i++)
            _speakerManager.UpdateSpeakerState(dists[i].id, dists[i].gain);

        if (showLines) UpdateLines(dists, top);
    }

    void UpdateLines(List<(int id, float dist, float gain)> sorted, int top) {
        if (_speakerManager == null) return;

        while (_lines.Count < top) {
            var go = new GameObject($"Line_{_lines.Count}");
            go.transform.SetParent(transform);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
            lr.startColor = lr.endColor = lineColor;
            lr.startWidth = lr.endWidth = lineWidth;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            _lines.Add(lr);
        }
        while (_lines.Count > top) {
            Destroy(_lines[^1].gameObject);
            _lines.RemoveAt(_lines.Count - 1);
        }

        var spkList = _speakerManager.GetSpeakers();
        Vector3 srcPos = transform.position;
        for (int i = 0; i < top; i++) {
            var spk = spkList.Find(s => s.id == sorted[i].id);
            if (spk == null) continue;
            float w = lineWidth * Mathf.Lerp(0.5f, 1.5f, sorted[i].gain);
            _lines[i].SetPosition(0, srcPos);
            _lines[i].SetPosition(1, new Vector3(spk.x, spk.y, spk.z));
            _lines[i].startWidth = w;
            _lines[i].endWidth = w * 0.5f;
        }
    }
}
