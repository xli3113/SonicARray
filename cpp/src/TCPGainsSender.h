#pragma once

#ifdef _WIN32
  #ifndef WIN32_LEAN_AND_MEAN
  #define WIN32_LEAN_AND_MEAN
  #endif
  #include <windows.h>
  #include <winsock2.h>
  using OscSock2 = SOCKET;
  #define OSC2_INVALID_SOCK INVALID_SOCKET
#else
  #include <pthread.h>
  #include <sys/socket.h>
  #include <netinet/in.h>
  #include <arpa/inet.h>
  #include <unistd.h>
  #include <netinet/tcp.h>
  using OscSock2 = int;
  #define OSC2_INVALID_SOCK (-1)
#endif

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
    void AcceptThread();
    void SendToAll(const std::vector<char>& data);

    int    port_;
    OscSock2 serverSock_ = OSC2_INVALID_SOCK;
    std::atomic<bool> running_{ false };

#ifdef _WIN32
    static DWORD WINAPI AcceptThreadEntry(LPVOID);
    mutable CRITICAL_SECTION clientsCS_;
    HANDLE acceptThread_ = NULL;
    std::vector<SOCKET> clients_;
#else
    static void* AcceptThreadEntry(void*);
    mutable pthread_mutex_t clientsCS_;
    pthread_t acceptThread_{};
    bool acceptThreadValid_ = false;
    std::vector<int> clients_;
#endif
};
