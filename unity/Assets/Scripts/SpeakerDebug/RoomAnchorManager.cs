using System;
using System.IO;
using UnityEngine;
// OVRSpatialAnchor 需 Meta XR Core SDK。未安装时用 FakeAnchor。安装后手动添加 Scripting Define: USE_OVR_SPATIAL_ANCHORS
#if USE_OVR_SPATIAL_ANCHORS
using OVR;
using System.Collections.Generic;
#endif

namespace SonicARray.SpeakerDebug {

public enum AnchorState { NoAnchor, Creating, Saving, Loading, Localizing, Ready, Failed }

/// <summary>房间基准锚点管理。支持 OVRSpatialAnchor 持久化与 FakeAnchor（Editor 无头显）降级。</summary>
public class RoomAnchorManager : MonoBehaviour {
    [Header("Anchoring")]
    public Transform roomAnchorTransform;
    public Transform speakerRigParent;
    [Tooltip("存储路径：持久化 UUID 与 TransformOffset")]
    public string configFileName = "anchor_config.json";

    [Header("Fake Anchor (Editor Fallback)")]
    [Tooltip("无头显时使用此 Transform 作为虚拟锚点，可手动移动")]
    public Transform fakeAnchorTransform;
    public bool useFakeAnchorInEditor = true;

    public bool IsLocalized => _localized;
    public string CurrentAnchorUuid => _savedUuid ?? "";
    public AnchorState State => _state;
    public string StateMessage => _stateMessage;

    private bool _localized;
    private AnchorState _state = AnchorState.NoAnchor;
    private string _stateMessage = "";
    private string _savedUuid;
    private AnchorLocalConfig _config;
#if USE_OVR_SPATIAL_ANCHORS
    private OVRSpatialAnchor _ovrAnchor;
#endif

    public event Action OnAnchorCreatedAndSaved;
    public event Action OnAnchorLoadedAndLocalized;
    public event Action<string> OnAnchorLoadFailed;
    public event Action OnLocalUuidDeleted;

    const string PlayerPrefsKey = "SonicARray_RoomAnchor_Uuid";

    void Awake() {
#if UNITY_EDITOR
        useFakeAnchorInEditor = true;
#endif
    }

    void Start() {
        _config = LoadConfig();
        _savedUuid = GetStoredUuid();
        if (!string.IsNullOrEmpty(_savedUuid)) {
            SetState(AnchorState.Loading, $"UUID: {_savedUuid.Substring(0, Mathf.Min(8, _savedUuid.Length))}...");
            Debug.Log($"[RoomAnchorManager] 发现本地 UUID: {_savedUuid}，尝试加载");
            LoadAnchor();
        } else {
            TryUseFakeAnchor();
        }
    }

    void SetState(AnchorState s, string msg = "") {
        _state = s;
        _stateMessage = msg;
        Debug.Log($"[RoomAnchorManager] 状态: {s} {msg}");
    }

    string GetStoredUuid() {
        string fromFile = _config?.anchorUuid;
        if (!string.IsNullOrEmpty(fromFile)) return fromFile;
        return PlayerPrefs.GetString(PlayerPrefsKey, "");
    }

    void SaveUuid(string uuid) {
        _savedUuid = uuid;
        PlayerPrefs.SetString(PlayerPrefsKey, uuid);
        if (_config == null) _config = new AnchorLocalConfig();
        _config.anchorUuid = uuid;
        SaveConfig();
        Debug.Log($"[RoomAnchorManager] 已保存 UUID: {uuid}");
    }

    void SaveConfig() {
        if (_config == null) return;
        string path = GetConfigPath();
        try {
            string json = JsonUtility.ToJson(_config, true);
            File.WriteAllText(path, json);
        } catch (Exception e) {
            Debug.LogWarning($"[RoomAnchorManager] 保存配置失败: {e.Message}");
        }
    }

    AnchorLocalConfig LoadConfig() {
        string path = GetConfigPath();
        if (!File.Exists(path)) return new AnchorLocalConfig();
        try {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<AnchorLocalConfig>(json);
        } catch (Exception e) {
            Debug.LogWarning($"[RoomAnchorManager] 加载配置失败: {e.Message}");
        }
        return new AnchorLocalConfig();
    }

    string GetConfigPath() {
        return Path.Combine(Application.persistentDataPath, configFileName);
    }

    bool ShouldUseFakeAnchor() {
#if UNITY_EDITOR
        if (useFakeAnchorInEditor) return true;
#endif
#if !USE_OVR_SPATIAL_ANCHORS
        return true;
#endif
        return false;
    }

    void TryUseFakeAnchor() {
        if (!ShouldUseFakeAnchor()) return;
        var t = fakeAnchorTransform != null ? fakeAnchorTransform : roomAnchorTransform;
        if (t != null) {
            _localized = true;
            ApplyTransformOffset(t);
            SetState(AnchorState.Ready, "FakeAnchor (Editor)");
            Debug.Log("[RoomAnchorManager] 使用 FakeAnchor，已就绪（Editor 降级）");
            OnAnchorLoadedAndLocalized?.Invoke();
        }
    }

    void ApplyTransformOffset(Transform root) {
        if (_config?.transformOffset == null || speakerRigParent == null) return;
        var o = _config.transformOffset;
        root.localPosition = o.position;
        root.localEulerAngles = o.rotation;
        root.localScale = o.scale;
    }

    public void SaveTransformOffset() {
        if (roomAnchorTransform == null) return;
        if (_config == null) _config = new AnchorLocalConfig();
        _config.transformOffset = new TransformOffsetData {
            position = roomAnchorTransform.localPosition,
            rotation = roomAnchorTransform.localEulerAngles,
            scale = roomAnchorTransform.localScale
        };
        SaveConfig();
        Debug.Log("[RoomAnchorManager] 已保存 TransformOffset");
    }

    public TransformOffsetData GetTransformOffset() => _config?.transformOffset;

    public void CreateAndSaveAnchor() {
        if (ShouldUseFakeAnchor()) {
            var t = fakeAnchorTransform != null ? fakeAnchorTransform : roomAnchorTransform;
            if (t != null) {
                _localized = true;
                var uuid = "fake-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                SaveUuid(uuid);
                SaveTransformOffset();
                SetState(AnchorState.Ready, uuid);
                OnAnchorCreatedAndSaved?.Invoke();
            }
            return;
        }

#if USE_OVR_SPATIAL_ANCHORS
        StartCoroutine(DoCreateAndSaveOvrAnchor());
#else
        TryUseFakeAnchor();
        SaveUuid("fake-no-ovr");
        OnAnchorCreatedAndSaved?.Invoke();
#endif
    }

#if USE_OVR_SPATIAL_ANCHORS
    System.Collections.IEnumerator DoCreateAndSaveOvrAnchor() {
        SetState(AnchorState.Creating, "创建中...");
        var cam = Camera.main;
        if (cam == null) {
            SetState(AnchorState.Failed, "无 Main Camera");
            Debug.LogError("[RoomAnchorManager] 无 Main Camera，无法创建锚点");
            yield break;
        }

        var go = new GameObject("RoomAnchor_OVR");
        go.transform.SetParent(roomAnchorTransform != null ? roomAnchorTransform.parent : null);
        go.transform.position = cam.transform.position + cam.transform.forward * 1.5f;
        go.transform.rotation = Quaternion.identity;

        var anchor = go.AddComponent<OVRSpatialAnchor>();
        yield return new WaitUntil(() => anchor.Created);

        if (!anchor.Created) {
            SetState(AnchorState.Failed, "创建失败");
            Debug.LogError("[RoomAnchorManager] OVRSpatialAnchor 创建失败");
            Destroy(go);
            yield break;
        }

        SetState(AnchorState.Saving, "保存中...");
        var saveTask = anchor.SaveAnchorAsync();
        while (!saveTask.IsCompleted) yield return null;
        var saveResult = saveTask.Result;

        if (!saveResult.Success) {
            SetState(AnchorState.Failed, $"保存失败: {saveResult.Status}");
            Debug.LogError($"[RoomAnchorManager] 锚点保存失败: {saveResult.Status}");
            Destroy(go);
            yield break;
        }

        _ovrAnchor = anchor;
        string uuid = anchor.Uuid.ToString();
        SaveUuid(uuid);
        _localized = true;

        if (roomAnchorTransform != null) {
            roomAnchorTransform.SetParent(go.transform);
            roomAnchorTransform.localPosition = Vector3.zero;
            roomAnchorTransform.localRotation = Quaternion.identity;
            roomAnchorTransform.localScale = Vector3.one;
        }

        SetState(AnchorState.Ready, uuid);
        Debug.Log($"[RoomAnchorManager] 锚点已创建并保存: {uuid}");
        OnAnchorCreatedAndSaved?.Invoke();
    }
#endif

    public void LoadAnchor() {
        if (string.IsNullOrEmpty(_savedUuid)) {
            SetState(AnchorState.Failed, "无本地 UUID");
            Debug.LogWarning("[RoomAnchorManager] 无本地 UUID，无法加载");
            OnAnchorLoadFailed?.Invoke("无本地 UUID");
            return;
        }

        if (_savedUuid.StartsWith("fake-")) {
            TryUseFakeAnchor();
            OnAnchorLoadedAndLocalized?.Invoke();
            return;
        }

#if USE_OVR_SPATIAL_ANCHORS
        StartCoroutine(DoLoadOvrAnchor());
#else
        TryUseFakeAnchor();
        OnAnchorLoadedAndLocalized?.Invoke();
#endif
    }

#if USE_OVR_SPATIAL_ANCHORS
    System.Collections.IEnumerator DoLoadOvrAnchor() {
        SetState(AnchorState.Loading, "加载中...");
        Guid uuid;
        if (!Guid.TryParse(_savedUuid, out uuid)) {
            SetState(AnchorState.Failed, "无效 UUID");
            Debug.LogError($"[RoomAnchorManager] 无效 UUID: {_savedUuid}");
            OnAnchorLoadFailed?.Invoke("无效 UUID");
            yield break;
        }

        var list = new List<OVRSpatialAnchor.UnboundAnchor>();
        var loadTask = OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, list);
        while (!loadTask.IsCompleted) yield return null;

        if (!loadTask.Result.Success || list == null || list.Count == 0) {
            SetState(AnchorState.Failed, "LoadUnboundAnchors 失败");
            Debug.LogError("[RoomAnchorManager] 加载锚点失败");
            OnAnchorLoadFailed?.Invoke("LoadUnboundAnchors 失败");
            yield break;
        }

        SetState(AnchorState.Localizing, "本地化中...请缓慢环顾");
        var unbound = list[0];
        var localizeTask = unbound.LocalizeAsync();
        while (!localizeTask.IsCompleted) yield return null;

        if (!unbound.Localized) {
            SetState(AnchorState.Failed, "本地化失败，请缓慢环顾或重新创建");
            Debug.LogError("[RoomAnchorManager] 锚点本地化失败");
            OnAnchorLoadFailed?.Invoke("Localize 失败");
            yield break;
        }

        var go = roomAnchorTransform != null ? roomAnchorTransform.gameObject : gameObject;
        var spatialAnchor = go.GetComponent<OVRSpatialAnchor>();
        if (spatialAnchor == null) spatialAnchor = go.AddComponent<OVRSpatialAnchor>();
        unbound.BindTo(spatialAnchor);
        _ovrAnchor = spatialAnchor;
        _localized = true;
        ApplyTransformOffset(roomAnchorTransform);
        SetState(AnchorState.Ready, unbound.Uuid.ToString());
        Debug.Log("[RoomAnchorManager] 锚点已加载并本地化");
        OnAnchorLoadedAndLocalized?.Invoke();
    }
#endif

    public void DeleteLocalUuid() {
        _savedUuid = null;
        PlayerPrefs.DeleteKey(PlayerPrefsKey);
        if (_config != null) _config.anchorUuid = "";
        SaveConfig();
        _localized = false;
        SetState(AnchorState.NoAnchor, "");
        Debug.Log("[RoomAnchorManager] 已删除本地 UUID");
        OnLocalUuidDeleted?.Invoke();
    }
}

}
