using System;
using System.Collections.Generic;
using UnityEngine;

namespace SonicARray.SpeakerDebug {

[Serializable]
public class SpeakerEntry {
    public int id;
    public float x;
    public float y;
    public float z;
}

[Serializable]
public class SpeakerListRoot {
    public List<SpeakerEntry> speakers;
}

/// <summary>YAML->Anchor 的 Transform 偏移，用于手动微调对齐。</summary>
[Serializable]
public class TransformOffsetData {
    public Vector3 position = Vector3.zero;
    public Vector3 rotation = Vector3.zero; // Euler degrees
    public Vector3 scale = Vector3.one;
}

/// <summary>本地持久化配置：锚点 UUID + TransformOffset。</summary>
[Serializable]
public class AnchorLocalConfig {
    public string anchorUuid = "";
    public TransformOffsetData transformOffset = new TransformOffsetData();
}

/// <summary>单个扬声器的增益状态（含平滑后值）。</summary>
[Serializable]
public class SpeakerGainSnapshot {
    public int speakerId;
    public float rawGain;
    public float smoothedGain;
}

/// <summary>录制帧：时间戳 + 各扬声器增益。</summary>
[Serializable]
public class GainRecordingFrame {
    public float time;
    public List<SpeakerGainSnapshot> gains;
}

/// <summary>完整录制数据，可序列化为 JSON。</summary>
[Serializable]
public class GainRecording {
    public float duration;
    public float sampleRate = 60f;
    public List<GainRecordingFrame> frames = new List<GainRecordingFrame>();
}

/// <summary>Shared Spatial Anchors：主机创建的锚点信息，用于共享给客户端。</summary>
[Serializable]
public class SharedAnchorPayload {
    public string uuid;
    public string roomId; // 可选：房间标识
    public long createdAt; // Unix timestamp
    public string metadata; // 可选扩展
}

}
