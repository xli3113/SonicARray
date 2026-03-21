#include "TCPGainsSender.h"
#include <iostream>
#include <cstring>

#pragma comment(lib, "ws2_32.lib")

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
    WSADATA wsa; WSAStartup(MAKEWORD(2,2), &wsa);
    InitializeCriticalSection(&clientsCS_);
}

TCPGainsSender::~TCPGainsSender() {
    Stop();
    DeleteCriticalSection(&clientsCS_);
}

bool TCPGainsSender::Start() {
    serverSock_ = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (serverSock_ == INVALID_SOCKET) return false;

    BOOL opt = TRUE;
    setsockopt(serverSock_, SOL_SOCKET, SO_REUSEADDR, (char*)&opt, sizeof(opt));

    sockaddr_in addr{};
    addr.sin_family      = AF_INET;
    addr.sin_port        = htons((u_short)port_);
    addr.sin_addr.s_addr = INADDR_ANY;
    if (bind(serverSock_, (sockaddr*)&addr, sizeof(addr)) == SOCKET_ERROR) {
        std::cerr << "TCPGainsSender: bind fail " << WSAGetLastError() << "\n";
        closesocket(serverSock_); serverSock_ = INVALID_SOCKET;
        return false;
    }
    if (listen(serverSock_, 4) == SOCKET_ERROR) {
        closesocket(serverSock_); serverSock_ = INVALID_SOCKET;
        return false;
    }

    running_ = true;
    acceptThread_ = CreateThread(NULL, 0, AcceptThreadEntry, this, 0, NULL);
    std::cout << "tcp gains server :7001 (Quest connects here)\n";
    return true;
}

void TCPGainsSender::Stop() {
    running_ = false;
    if (serverSock_ != INVALID_SOCKET) {
        closesocket(serverSock_); serverSock_ = INVALID_SOCKET;
    }
    {
        EnterCriticalSection(&clientsCS_); struct _CS_Guard_ { CRITICAL_SECTION& cs; ~_CS_Guard_(){LeaveCriticalSection(&cs);}} _csg_{clientsCS_};
        for (SOCKET c : clients_) closesocket(c);
        clients_.clear();
    }
    if (acceptThread_) {
        WaitForSingleObject(acceptThread_, 2000);
        CloseHandle(acceptThread_); acceptThread_ = NULL;
    }
}

DWORD WINAPI TCPGainsSender::AcceptThreadEntry(LPVOID p) {
    static_cast<TCPGainsSender*>(p)->AcceptThread(); return 0;
}

void TCPGainsSender::AcceptThread() {
    while (running_) {
        // Use select with timeout so we can check running_ periodically
        fd_set fds; FD_ZERO(&fds); FD_SET(serverSock_, &fds);
        timeval tv{ 0, 200000 }; // 200 ms
        int r = select(0, &fds, NULL, NULL, &tv);
        if (r <= 0) continue;

        sockaddr_in from{}; int fromLen = sizeof(from);
        SOCKET client = accept(serverSock_, (sockaddr*)&from, &fromLen);
        if (client == INVALID_SOCKET) continue;

        // Set TCP_NODELAY so gains aren't batched by Nagle
        BOOL nd = TRUE;
        setsockopt(client, IPPROTO_TCP, TCP_NODELAY, (char*)&nd, sizeof(nd));

        char ip[20];
        const auto* a = (const uint8_t*)&from.sin_addr.s_addr;
        snprintf(ip, sizeof(ip), "%u.%u.%u.%u", a[0], a[1], a[2], a[3]);
        std::cout << "tcp client connected: " << ip << "\n";

        EnterCriticalSection(&clientsCS_); struct _CS_Guard_ { CRITICAL_SECTION& cs; ~_CS_Guard_(){LeaveCriticalSection(&cs);}} _csg_{clientsCS_};
        clients_.push_back(client);
    }
}

int TCPGainsSender::GetConnectedCount() const {
    EnterCriticalSection(&clientsCS_); struct _CS_Guard_ { CRITICAL_SECTION& cs; ~_CS_Guard_(){LeaveCriticalSection(&cs);}} _csg_{clientsCS_};
    return (int)clients_.size();
}

void TCPGainsSender::SendToAll(const std::vector<char>& data) {
    // Prefix with 4-byte big-endian length
    char lenBuf[4]; WriteInt32BE(lenBuf, (int32_t)data.size());

    EnterCriticalSection(&clientsCS_); struct _CS_Guard_ { CRITICAL_SECTION& cs; ~_CS_Guard_(){LeaveCriticalSection(&cs);}} _csg_{clientsCS_};
    for (int i = (int)clients_.size() - 1; i >= 0; --i) {
        SOCKET c = clients_[i];
        bool ok = (send(c, lenBuf, 4, 0) == 4) &&
                  (send(c, data.data(), (int)data.size(), 0) == (int)data.size());
        if (!ok) {
            closesocket(c);
            clients_.erase(clients_.begin() + i);
        }
    }
}

void TCPGainsSender::SendSpeakerGains(int sourceId,
                                      const std::vector<float>& gains) {
    {
        EnterCriticalSection(&clientsCS_); struct _CS_Guard_ { CRITICAL_SECTION& cs; ~_CS_Guard_(){LeaveCriticalSection(&cs);}} _csg_{clientsCS_};
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
