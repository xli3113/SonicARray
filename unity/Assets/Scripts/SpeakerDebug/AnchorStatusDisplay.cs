using UnityEngine;
using UnityEngine.UI;

namespace SonicARray.SpeakerDebug {

/// <summary>在 UI Text 上显示 RoomAnchorManager 状态。</summary>
public class AnchorStatusDisplay : MonoBehaviour {
    public RoomAnchorManager anchorManager;
    public Text statusText;
    public float refreshInterval = 0.2f;

    float _nextRefresh;

    void Start() {
        if (anchorManager == null) anchorManager = FindObjectOfType<RoomAnchorManager>();
        if (statusText == null) statusText = GetComponent<Text>();
    }

    void Update() {
        if (Time.time < _nextRefresh || anchorManager == null || statusText == null) return;
        _nextRefresh = Time.time + refreshInterval;
        string uuid = anchorManager.CurrentAnchorUuid;
        string shortUuid = string.IsNullOrEmpty(uuid) ? "-" : (uuid.Length > 12 ? uuid.Substring(0, 12) + "..." : uuid);
        statusText.text = $"{(anchorManager.IsLocalized ? "Ready" : "Loading")} {shortUuid}";
    }
}

}
