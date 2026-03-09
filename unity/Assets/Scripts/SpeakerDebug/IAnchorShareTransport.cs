using System;
using System.Collections.Generic;

namespace SonicARray.SpeakerDebug {

/// <summary>Shared Spatial Anchors 传输接口。接入 Photon/Netcode 等实现此接口。</summary>
public interface IAnchorShareTransport {
    bool IsHost { get; }
    bool IsConnected { get; }
    /// <summary>Host：创建并共享锚点。将 payload 发送给所有 Client。</summary>
    void ShareAnchor(SharedAnchorPayload payload);
    /// <summary>Client：请求接收锚点。收到后触发 OnAnchorReceived。</summary>
    void RequestAnchors();
    /// <summary>Host 创建并共享时触发（可用于本地回调）。</summary>
    event Action<SharedAnchorPayload> OnAnchorShared;
    /// <summary>Client 收到 Host 共享的锚点。</summary>
    event Action<SharedAnchorPayload> OnAnchorReceived;
}

/// <summary>占位实现：不联网，仅打印日志。便于在 Inspector 中挂载调试。</summary>
public class AnchorShareTransportStub : IAnchorShareTransport {
    public bool IsHost => false;
    public bool IsConnected => false;
    public event Action<SharedAnchorPayload> OnAnchorShared;
    public event Action<SharedAnchorPayload> OnAnchorReceived;

    public void ShareAnchor(SharedAnchorPayload payload) {
        UnityEngine.Debug.Log($"[AnchorShare] Stub ShareAnchor: uuid={payload?.uuid} (接入 Photon/Netcode 后实现)");
        OnAnchorShared?.Invoke(payload);
    }

    public void RequestAnchors() {
        UnityEngine.Debug.Log("[AnchorShare] Stub RequestAnchors (接入 Photon/Netcode 后实现)");
    }
}

}
