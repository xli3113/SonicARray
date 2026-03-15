using UnityEngine;

/// <summary>
/// Listens for VBAP gain feedback from the C++ backend on UDP port 9000 and
/// drives SpeakerManager's visual representation directly from true VBAP gains.
///
/// Message format (/vbap/gains):
///   int sourceId, int speakerCount,
///   int speakerId_0, float gain_0, int speakerId_1, float gain_1, ...
///
/// When this receiver is getting live data, SpeakerManager.HasFreshCppGains
/// returns true and the local inverse-square approximation in SpatialSource
/// and SourceManager is suppressed.
/// </summary>
public class VBAPGainReceiver : MonoBehaviour {

    [Header("Network")]
    [Tooltip("UDP port to listen on for VBAP gain feedback from C++ backend")]
    public int listenPort = 9000;

    [Header("References")]
    [Tooltip("Leave empty to auto-find in scene")]
    public SpeakerManager speakerManager;

    private OSCServer _server;

    void Start() {
        if (speakerManager == null)
            speakerManager = FindObjectOfType<SpeakerManager>();

        if (speakerManager == null) {
            Debug.LogError("[VBAPGainReceiver] SpeakerManager not found in scene.");
            return;
        }

        try {
            _server = new OSCServer(listenPort);
            Debug.Log($"[VBAPGainReceiver] Listening on UDP port {listenPort}");
        } catch (System.Exception e) {
            Debug.LogWarning($"[VBAPGainReceiver] Could not open port {listenPort}: {e.Message}. " +
                             "Falling back to local VBAP approximation.");
        }
    }

    void Update() {
        if (_server == null || speakerManager == null) return;

        // Drain the queue first, then apply as one atomic gain frame.
        // This avoids opening a gain frame when nothing arrived, which would
        // leave SpeakerManager._inGainFrame = true without a matching commit.
        OSCServer.Message msg;
        bool hasAny = false;

        while (_server.TryDequeue(out msg)) {
            if (msg.Address == "/vbap/gains") {
                if (!hasAny) speakerManager.BeginGainFrame();
                ApplyGainMessage(msg);
                hasAny = true;
            }
        }

        if (hasAny) {
            speakerManager.CommitGainFrame();
            speakerManager.NotifyCppGainsReceived();
        }
        // If nothing arrived, HasFreshCppGains will timeout after 0.5 s
        // and SourceManager's local approximation re-engages automatically.
    }

    private void ApplyGainMessage(OSCServer.Message msg) {
        // Expected args: int sourceId, int speakerCount,
        //                int spkId_0, float gain_0, int spkId_1, float gain_1, ...
        if (msg.Args == null || msg.Args.Length < 2) return;
        if (!(msg.Args[0] is int) || !(msg.Args[1] is int)) return;

        int sourceId = (int)msg.Args[0];
        int count    = (int)msg.Args[1];

        int needed = 2 + count * 2;
        if (msg.Args.Length < needed) return;

        int idx = 2;
        for (int i = 0; i < count; i++) {
            if (!(msg.Args[idx] is int) || !(msg.Args[idx + 1] is float)) { idx += 2; continue; }
            int   spkId = (int)  msg.Args[idx];
            float gain  = (float)msg.Args[idx + 1];
            speakerManager.AccumulateSpeakerGain(spkId, gain, sourceId);
            idx += 2;
        }
    }

    void OnDestroy() {
        _server?.Dispose();
        _server = null;
    }
}
