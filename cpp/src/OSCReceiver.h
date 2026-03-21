#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <winsock2.h>
#include <ws2tcpip.h>
#undef min
#undef max

#include <string>
#include <atomic>
#include <functional>
#include <vector>
#include <array>

using PositionCallback = std::function<void(float, float, float)>;
using MultiSourcePositionCallback = std::function<void(int, float, float, float)>;

/// Listens on a single UDP socket for OSC position messages and sends
/// speaker-gain replies back on the SAME socket.  Using one socket means
/// outgoing packets always originate from port 7000, which is exactly the
/// destination the Quest targeted — Android's stateful firewall therefore
/// recognises them as replies and lets them through.
class OSCReceiver {
public:
    static constexpr int kMaxSources = 8;

    OSCReceiver(int port = 7000);
    ~OSCReceiver();

    bool Start();
    void Stop();

    void SetPositionCallback(PositionCallback cb) { positionCallback_ = cb; }
    void SetMultiSourcePositionCallback(MultiSourcePositionCallback cb) { multiSourcePositionCallback_ = cb; }

    float GetLastX() const { return lastX_; }
    float GetLastY() const { return lastY_; }
    float GetLastZ() const { return lastZ_; }

    int GetSourcePositions(std::array<std::array<float, 3>, kMaxSources>& out) const;

    std::string GetLastSenderIP()   const;
    int         GetLastSenderPort() const;

    // Total OSC packets received since Start() (thread-safe).
    uint64_t GetPacketCount() const { return packetCount_.load(std::memory_order_relaxed); }

    // Send /spatial/speaker_gains to the last known sender IP, always on replyPort_.
    // No-op if no sender has been discovered yet.
    // replyPort_ defaults to 7002 — the Unity OSCReceiver's listen port.
    void SendSpeakerGains(int sourceId, const std::vector<float>& gains);

    // Override the reply port (default 7002 = Unity OSCReceiver.listenPort).
    void SetReplyPort(int port) { replyPort_ = port; }

private:
    int replyPort_ = 7002;
    void ListenThread();
    static DWORD WINAPI ListenThreadEntry(LPVOID param);

    // Parse a raw UDP datagram as an OSC packet and fire callbacks.
    void DispatchPacket(const char* data, int len, const sockaddr_in& from);
    void StorePosition(int sourceId, float x, float y, float z);

    // ── socket ──────────────────────────────────────────────────────
    mutable CRITICAL_SECTION socketMutex_;
    SOCKET rawSock_ = INVALID_SOCKET;  // single socket: recv + send

    int port_;
    std::atomic<bool> running_;
    HANDLE listenThread_;

    // ── last parsed position ─────────────────────────────────────────
    float lastX_, lastY_, lastZ_;
    PositionCallback positionCallback_;
    MultiSourcePositionCallback multiSourcePositionCallback_;

    // ── per-source position table ─────────────────────────────────────
    mutable CRITICAL_SECTION sourcesMutex_;
    std::vector<std::array<float, 3>> sourcePositions_;

    // ── sender discovery ─────────────────────────────────────────────
    mutable CRITICAL_SECTION senderIPMutex_;
    std::string lastSenderIP_;
    int         lastSenderPort_ = 0;

    std::atomic<uint64_t> packetCount_;
};
