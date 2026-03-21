#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <winsock2.h>
#include <vector>
#include <atomic>

/// TCP server that pushes speaker-gain OSC messages to connected Unity clients.
///
/// Why TCP and not UDP:
///   - Windows Mobile Hotspot blocks ALL host-initiated traffic to WiFi clients
///     (confirmed by ping timeout).  UDP *from the PC* never arrives at the Quest.
///   - TCP connection is Quest-initiated (Quest connects OUT to PC:7001).
///     Once established the OS allows data to flow back through the same socket.
///   - This is the only reliable transport on a Windows Mobile Hotspot.
///
/// Wire format: 4-byte big-endian length, then the raw OSC bytes.
class TCPGainsSender {
public:
    explicit TCPGainsSender(int port = 7001);
    ~TCPGainsSender();

    bool Start();
    void Stop();

    /// Send /spatial/speaker_gains to every connected client.
    void SendSpeakerGains(int sourceId, const std::vector<float>& gains);

    int GetConnectedCount() const;

private:
    static DWORD WINAPI AcceptThreadEntry(LPVOID);
    void AcceptThread();
    void SendToAll(const std::vector<char>& data);

    int    port_;
    SOCKET serverSock_ = INVALID_SOCKET;
    std::atomic<bool> running_{ false };
    HANDLE acceptThread_ = NULL;

    mutable CRITICAL_SECTION clientsCS_;
    std::vector<SOCKET> clients_;
};
