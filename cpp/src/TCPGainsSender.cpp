#include "TCPGainsSender.h"
#include <iostream>
#include <cstring>
#include <cstdint>

#ifdef _WIN32
  #pragma comment(lib, "ws2_32.lib")
#else
  #include <cerrno>
  #include <sys/time.h>
#endif

// ── platform helpers ──────────────────────────────────────────────────────────

#ifdef _WIN32

struct TcpMutexGuard {
    CRITICAL_SECTION& cs_;
    explicit TcpMutexGuard(CRITICAL_SECTION& cs) : cs_(cs) { EnterCriticalSection(&cs_); }
    ~TcpMutexGuard() { LeaveCriticalSection(&cs_); }
};
static inline void TcpSockClose(OscSock2 s) { closesocket(s); }

#else

struct TcpMutexGuard {
    pthread_mutex_t& cs_;
    explicit TcpMutexGuard(pthread_mutex_t& cs) : cs_(cs) { pthread_mutex_lock(&cs_); }
    ~TcpMutexGuard() { pthread_mutex_unlock(&cs_); }
};
static inline void TcpSockClose(OscSock2 s) { ::close(s); }

#endif

// ── OSC helpers ────────────────────────────────────────────────────────────

static void WriteInt32BE(char* p, int32_t v) {
    auto u = (uint32_t)v;
    p[0]=(char)(u>>24); p[1]=(char)(u>>16); p[2]=(char)(u>>8); p[3]=(char)u;
}
static void WriteFloat32BE(char* p, float v) {
    uint32_t u; std::memcpy(&u, &v, 4); WriteInt32BE(p, (int32_t)u);
}
static void AppendStr(std::vector<char>& buf, const char* s) {
    size_t n = std::strlen(s) + 1;
    for (size_t i = 0; i < n; ++i) buf.push_back(s[i]);
    while (buf.size() % 4) buf.push_back(0);
}
static void AppendI32(std::vector<char>& buf, int32_t v) {
    char t[4]; WriteInt32BE(t, v); buf.insert(buf.end(), t, t+4);
}
static void AppendF32(std::vector<char>& buf, float v) {
    char t[4]; WriteFloat32BE(t, v); buf.insert(buf.end(), t, t+4);
}

// ── TCPGainsSender ─────────────────────────────────────────────────────────

TCPGainsSender::TCPGainsSender(int port) : port_(port) {
#ifdef _WIN32
    WSADATA wsa; WSAStartup(MAKEWORD(2,2), &wsa);
    InitializeCriticalSection(&clientsCS_);
#else
    pthread_mutex_init(&clientsCS_, nullptr);
#endif
}

TCPGainsSender::~TCPGainsSender() {
    Stop();
#ifdef _WIN32
    DeleteCriticalSection(&clientsCS_);
#else
    pthread_mutex_destroy(&clientsCS_);
#endif
}

bool TCPGainsSender::Start() {
    serverSock_ = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (serverSock_ == OSC2_INVALID_SOCK) return false;

#ifdef _WIN32
    BOOL opt = TRUE;
    setsockopt(serverSock_, SOL_SOCKET, SO_REUSEADDR, (char*)&opt, sizeof(opt));
#else
    int opt = 1;
    setsockopt(serverSock_, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));
#endif

    sockaddr_in addr{};
    addr.sin_family      = AF_INET;
    addr.sin_port        = htons((unsigned short)port_);
    addr.sin_addr.s_addr = INADDR_ANY;
    if (bind(serverSock_, (sockaddr*)&addr, sizeof(addr)) != 0) {
        std::cerr << "TCPGainsSender: bind fail " << errno << "\n";
        TcpSockClose(serverSock_); serverSock_ = OSC2_INVALID_SOCK;
        return false;
    }
    if (listen(serverSock_, 4) != 0) {
        TcpSockClose(serverSock_); serverSock_ = OSC2_INVALID_SOCK;
        return false;
    }

    running_ = true;
#ifdef _WIN32
    acceptThread_ = CreateThread(NULL, 0, AcceptThreadEntry, this, 0, NULL);
#else
    int rc = pthread_create(&acceptThread_, nullptr, AcceptThreadEntry, this);
    acceptThreadValid_ = (rc == 0);
#endif
    std::cout << "tcp gains server :" << port_ << " (Quest connects here)\n";
    return true;
}

void TCPGainsSender::Stop() {
    running_ = false;
    if (serverSock_ != OSC2_INVALID_SOCK) {
        TcpSockClose(serverSock_); serverSock_ = OSC2_INVALID_SOCK;
    }
    {
        TcpMutexGuard g(clientsCS_);
        for (auto c : clients_) TcpSockClose(c);
        clients_.clear();
    }
#ifdef _WIN32
    if (acceptThread_) {
        WaitForSingleObject(acceptThread_, 2000);
        CloseHandle(acceptThread_); acceptThread_ = NULL;
    }
#else
    if (acceptThreadValid_) {
        pthread_join(acceptThread_, nullptr);
        acceptThreadValid_ = false;
    }
#endif
}

#ifdef _WIN32
DWORD WINAPI TCPGainsSender::AcceptThreadEntry(LPVOID p) {
    static_cast<TCPGainsSender*>(p)->AcceptThread(); return 0;
}
#else
void* TCPGainsSender::AcceptThreadEntry(void* p) {
    static_cast<TCPGainsSender*>(p)->AcceptThread(); return nullptr;
}
#endif

void TCPGainsSender::AcceptThread() {
    while (running_) {
        // Use select with timeout so we can check running_ periodically
        fd_set fds; FD_ZERO(&fds); FD_SET(serverSock_, &fds);
        struct timeval tv{ 0, 200000 }; // 200 ms
        int r = select((int)serverSock_ + 1, &fds, NULL, NULL, &tv);
        if (r <= 0) continue;

        sockaddr_in from{};
#ifdef _WIN32
        int fromLen = sizeof(from);
#else
        socklen_t fromLen = sizeof(from);
#endif
        OscSock2 client = accept(serverSock_, (sockaddr*)&from, &fromLen);
        if (client == OSC2_INVALID_SOCK) continue;

        // Set TCP_NODELAY so gains aren't batched by Nagle
#ifdef _WIN32
        BOOL nd = TRUE;
        setsockopt(client, IPPROTO_TCP, TCP_NODELAY, (char*)&nd, sizeof(nd));
#else
        int nd = 1;
        setsockopt(client, IPPROTO_TCP, TCP_NODELAY, &nd, sizeof(nd));
#endif

        char ip[20];
        const auto* a = (const uint8_t*)&from.sin_addr.s_addr;
        snprintf(ip, sizeof(ip), "%u.%u.%u.%u", a[0], a[1], a[2], a[3]);
        std::cout << "tcp client connected: " << ip << "\n";

        TcpMutexGuard g(clientsCS_);
        clients_.push_back(client);
    }
}

int TCPGainsSender::GetConnectedCount() const {
    TcpMutexGuard g(clientsCS_);
    return (int)clients_.size();
}

void TCPGainsSender::SendToAll(const std::vector<char>& data) {
    // Prefix with 4-byte big-endian length
    char lenBuf[4]; WriteInt32BE(lenBuf, (int32_t)data.size());

    TcpMutexGuard g(clientsCS_);
    for (int i = (int)clients_.size() - 1; i >= 0; --i) {
        auto c = clients_[i];
        bool ok = (send(c, lenBuf, 4, 0) == 4) &&
                  (send(c, data.data(), (int)data.size(), 0) == (int)data.size());
        if (!ok) {
            TcpSockClose(c);
            clients_.erase(clients_.begin() + i);
        }
    }
}

void TCPGainsSender::SendSpeakerGains(int sourceId,
                                      const std::vector<float>& gains) {
    {
        TcpMutexGuard g(clientsCS_);
        if (clients_.empty()) return;
    }

    std::vector<char> buf;
    buf.reserve(256);
    AppendStr(buf, "/spatial/speaker_gains");
    std::string tag = ",i"; tag.append(gains.size(), 'f');
    AppendStr(buf, tag.c_str());
    AppendI32(buf, sourceId);
    for (float g : gains) AppendF32(buf, g);

    SendToAll(buf);
}
