using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 多声源生命周期管理器（最多 8 路，与 C++ 后端 VBAP 上限一致）。
///
/// 职责：
///   - 创建 / 删除 / 选中 SpatialSource
///   - 每帧统一聚合所有声源对扬声器的增益贡献，推送给 SpeakerManager
///
/// 使用方法：
///   1. 在场景中新建空 GameObject → 挂载此脚本
///   2. 指定 sourcePrefab（含 SpatialSource 脚本的 Prefab）
///   3. 指定 speakerManager（或让脚本自动查找）
///   4. 将此脚本引用传给 XRSourceController
/// </summary>
public class SourceManager : MonoBehaviour {

    // ──────────────────────────────────────────────────────────────────
    // Inspector 字段
    // ──────────────────────────────────────────────────────────────────

    [Header("Prefab")]
    [Tooltip("含 SpatialSource 组件的 Prefab")]
    public GameObject sourcePrefab;

    [Header("Scene References")]
    [Tooltip("留空则自动查找场景中的 SpeakerManager")]
    public SpeakerManager speakerManager;

    [Header("OSC（应用于所有声源）")]
    public bool enableOSC = true;
    public string oscIP = "127.0.0.1";
    public int oscPort = 7000;

    [Header("Limits")]
    [Tooltip("最多同时存在的声源数量，与后端 VBAP 上限一致")]
    public int maxSources = 8;

    // ──────────────────────────────────────────────────────────────────
    // 只读属性
    // ──────────────────────────────────────────────────────────────────

    private readonly List<SpatialSource> _sources = new List<SpatialSource>();

    private int _selectedIndex = -1;

    /// <summary>当前所有活跃声源（只读）。</summary>
    public IReadOnlyList<SpatialSource> Sources => _sources;

    /// <summary>当前声源数量。</summary>
    public int Count => _sources.Count;

    /// <summary>当前选中的声源，无则返回 null。</summary>
    public SpatialSource SelectedSource =>
        (_selectedIndex >= 0 && _selectedIndex < _sources.Count) ? _sources[_selectedIndex] : null;

    /// <summary>当前选中索引（-1 表示无）。</summary>
    public int SelectedIndex => _selectedIndex;

    // ──────────────────────────────────────────────────────────────────
    // Unity 生命周期
    // ──────────────────────────────────────────────────────────────────

    void Start() {
        if (speakerManager == null)
            speakerManager = FindObjectOfType<SpeakerManager>();

        if (speakerManager == null)
            Debug.LogWarning("[SourceManager] 未找到 SpeakerManager，扬声器增益可视化将不可用");

        if (sourcePrefab == null)
            Debug.LogError("[SourceManager] sourcePrefab 未设置！请在 Inspector 中指定含 SpatialSource 的 Prefab。");
    }

    void Update() {
        if (speakerManager == null || _sources.Count == 0) return;

        // When the C++ backend is sending real VBAP gains, VBAPGainReceiver
        // drives the speaker visuals. Skip the local approximation to avoid
        // overwriting the ground-truth data.
        if (speakerManager.HasFreshCppGains) return;

        // ── 每帧：聚合所有声源的增益贡献（本地近似，后端离线时使用）──
        speakerManager.BeginGainFrame();
        foreach (var src in _sources) {
            if (src != null)
                src.ContributeGainsToManager();
        }
        speakerManager.CommitGainFrame();
    }

    // ──────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 在世界坐标 <paramref name="worldPosition"/> 创建一个新声源。
    /// 超过上限时返回 null 并打印警告。
    /// </summary>
    public SpatialSource CreateSource(Vector3 worldPosition) {
        if (_sources.Count >= maxSources) {
            Debug.LogWarning($"[SourceManager] 已达上限 {maxSources}，无法创建新声源");
            return null;
        }
        if (sourcePrefab == null) {
            Debug.LogError("[SourceManager] sourcePrefab 未设置，无法创建声源");
            return null;
        }

        int newId = FindFirstFreeId();
        var go = Instantiate(sourcePrefab, worldPosition, Quaternion.identity, transform);
        go.name = $"Source_{newId}";

        var src = go.GetComponent<SpatialSource>();
        if (src == null) {
            Debug.LogError("[SourceManager] sourcePrefab 上缺少 SpatialSource 组件！");
            Destroy(go);
            return null;
        }

        // 覆盖 OSC 设置
        src.sourceId = newId;
        src.enableOSC = enableOSC;
        src.oscIP = oscIP;
        src.oscPort = oscPort;
        src.multiSourceOSC = true;
        src.RefreshLabel();

        _sources.Add(src);
        _selectedIndex = _sources.Count - 1;
        RefreshSelectionVisuals();

        Debug.Log($"[SourceManager] 创建声源 #{newId} at {worldPosition}，当前共 {_sources.Count} 个");
        return src;
    }

    /// <summary>删除当前选中的声源。</summary>
    public void DeleteSelectedSource() {
        if (SelectedSource == null) {
            Debug.LogWarning("[SourceManager] 无选中声源，无法删除");
            return;
        }
        DeleteAt(_selectedIndex);
    }

    /// <summary>按列表索引删除声源。</summary>
    public void DeleteAt(int listIndex) {
        if (listIndex < 0 || listIndex >= _sources.Count) return;

        var src = _sources[listIndex];
        int id = src != null ? src.sourceId : -1;

        // 清零该声源对所有扬声器的贡献
        if (speakerManager != null) {
            foreach (var spk in speakerManager.GetSpeakers())
                speakerManager.UpdateSpeakerState(spk.id, 0f);
        }

        if (src != null) Destroy(src.gameObject);
        _sources.RemoveAt(listIndex);

        // 修正选中索引
        _selectedIndex = _sources.Count == 0 ? -1
            : Mathf.Clamp(_selectedIndex, 0, _sources.Count - 1);

        RefreshSelectionVisuals();
        Debug.Log($"[SourceManager] 删除声源 #{id}，剩余 {_sources.Count} 个");
    }

    /// <summary>循环切换到下一个声源。</summary>
    public void CycleSelection() {
        if (_sources.Count == 0) return;
        _selectedIndex = (_selectedIndex + 1) % _sources.Count;
        RefreshSelectionVisuals();

        if (SelectedSource != null)
            Debug.Log($"[SourceManager] 切换到声源 #{SelectedSource.sourceId}");
    }

    /// <summary>按声源 ID 选中（找不到则不做任何事）。</summary>
    public void SelectById(int sourceId) {
        for (int i = 0; i < _sources.Count; i++) {
            if (_sources[i] != null && _sources[i].sourceId == sourceId) {
                _selectedIndex = i;
                RefreshSelectionVisuals();
                return;
            }
        }
    }

    /// <summary>删除所有声源。</summary>
    public void ClearAll() {
        for (int i = _sources.Count - 1; i >= 0; i--)
            DeleteAt(i);
    }

    // ──────────────────────────────────────────────────────────────────
    // 私有辅助
    // ──────────────────────────────────────────────────────────────────

    void RefreshSelectionVisuals() {
        for (int i = 0; i < _sources.Count; i++) {
            if (_sources[i] != null)
                _sources[i].SetSelected(i == _selectedIndex);
        }
    }

    int FindFirstFreeId() {
        for (int id = 0; id < maxSources; id++) {
            bool used = false;
            foreach (var s in _sources) {
                if (s != null && s.sourceId == id) { used = true; break; }
            }
            if (!used) return id;
        }
        return _sources.Count;   // 理论上不会到这里（已检查上限）
    }
}
