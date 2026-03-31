#pragma once

#ifdef _WIN32
  #ifndef WIN32_LEAN_AND_MEAN
  #define WIN32_LEAN_AND_MEAN
  #endif
  #include <windows.h>
  #include <winsock2.h>
  #include <ws2tcpip.h>
  #undef min
  #undef max
  using OscSock = SOCKET;
  #define OSC_INVALID_SOCK INVALID_SOCKET
#else
  #include <pthread.h>
  #include <sys/socket.h>
  #include <netinet/in.h>
  #include <arpa/inet.h>
  #include <unistd.h>
  using OscSock = int;
  #define OSC_INVALID_SOCK (-1)
#endif

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

    // Parse a raw UDP datagram as an OSC packet and fire callbacks.
    void DispatchPacket(const char* data, int len, const sockaddr_in& from);
    void StorePosition(int sourceId, float x, float y, float z);

#ifdef _WIN32
    static DWORD WINAPI ListenThreadEntry(LPVOID param);
    mutable CRITICAL_SECTION socketMutex_;
    mutable CRITICAL_SECTION sourcesMutex_;
    mutable CRITICAL_SECTION senderIPMutex_;
    HANDLE listenThread_ = NULL;
#else
    static void* ListenThreadEntry(void* param);
    mutable pthread_mutex_t socketMutex_;
    mutable pthread_mutex_t sourcesMutex_;
    mutable pthread_mutex_t senderIPMutex_;
    pthread_t listenThread_{};
    bool listenThreadValid_ = false;
#endif

    // ── socket ──────────────────────────────────────────────────────────
    OscSock rawSock_ = OSC_INVALID_SOCK;  // single socket: recv + send

    int port_;
    std::atomic<bool> running_;

    // ── last parsed position ─────────────────────────────────────────
    float lastX_, lastY_, lastZ_;
    PositionCallback positionCallback_;
    MultiSourcePositionCallback multiSourcePositionCallback_;

    // ── per-source position table ─────────────────────────────────────
    std::vector<std::array<float, 3>> sourcePositions_;

    // ── sender discovery ─────────────────────────────────────────────
    std::string lastSenderIP_;
    int         lastSenderPort_ = 0;

    std::atomic<uint64_t> packetCount_;
};
