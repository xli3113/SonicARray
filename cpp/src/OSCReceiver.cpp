#include "OSCReceiver.h"
#include <iostream>
#include <cstring>
#include <cstdint>
#include <algorithm>

#ifdef _WIN32
  #pragma comment(lib, "ws2_32.lib")
#else
  #include <cerrno>
  #include <sys/time.h>
#endif

// ── platform helpers ──────────────────────────────────────────────────────────

#ifdef _WIN32

struct CritSectionGuard {
    CRITICAL_SECTION& cs_;
    explicit CritSectionGuard(CRITICAL_SECTION& cs) : cs_(cs) { EnterCriticalSection(&cs_); }
    ~CritSectionGuard() { LeaveCriticalSection(&cs_); }
};

static inline void OscSocketClose(OscSock s) { closesocket(s); }
static inline int  OscSocketError()          { return WSAGetLastError(); }

#else  // POSIX

struct CritSectionGuard {
    pthread_mutex_t& cs_;
    explicit CritSectionGuard(pthread_mutex_t& cs) : cs_(cs) { pthread_mutex_lock(&cs_); }
    ~CritSectionGuard() { pthread_mutex_unlock(&cs_); }
};

static inline void OscSocketClose(OscSock s) { ::close(s); }
static inline int  OscSocketError()          { return errno; }

#endif

// ── OSC big-endian helpers ────────────────────────────────────────────────────

static int32_t ReadInt32BE(const char* p) {
    auto* b = reinterpret_cast<const uint8_t*>(p);
    return (int32_t)(((uint32_t)b[0] << 24) | ((uint32_t)b[1] << 16)
                   | ((uint32_t)b[2] << 8)  |  (uint32_t)b[3]);
}
static float ReadFloat32BE(const char* p) {
    uint32_t u = (uint32_t)ReadInt32BE(p);
    float f; std::memcpy(&f, &u, 4); return f;
}
static void WriteInt32BE(char* p, int32_t v) {
    auto u = static_cast<uint32_t>(v);
    p[0] = (char)(u >> 24); p[1] = (char)(u >> 16);
    p[2] = (char)(u >> 8);  p[3] = (char)(u);
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
    char tmp[4]; WriteInt32BE(tmp, v);
    buf.insert(buf.end(), tmp, tmp + 4);
}
static void AppendF32(std::vector<char>& buf, float v) {
    char tmp[4]; WriteFloat32BE(tmp, v);
    buf.insert(buf.end(), tmp, tmp + 4);
}

// ── OSCReceiver ───────────────────────────────────────────────────────────────

OSCReceiver::OSCReceiver(int port)
    : port_(port), running_(false),
      lastX_(0), lastY_(0), lastZ_(0),
      packetCount_(0)
{
#ifdef _WIN32
    WSADATA wsa; WSAStartup(MAKEWORD(2, 2), &wsa);
    listenThread_ = NULL;
    InitializeCriticalSection(&socketMutex_);
    InitializeCriticalSection(&sourcesMutex_);
    InitializeCriticalSection(&senderIPMutex_);
#else
    pthread_mutex_init(&socketMutex_, nullptr);
    pthread_mutex_init(&sourcesMutex_, nullptr);
    pthread_mutex_init(&senderIPMutex_, nullptr);
#endif
}

OSCReceiver::~OSCReceiver() {
    Stop();
#ifdef _WIN32
    DeleteCriticalSection(&socketMutex_);
    DeleteCriticalSection(&sourcesMutex_);
    DeleteCriticalSection(&senderIPMutex_);
#else
    pthread_mutex_destroy(&socketMutex_);
    pthread_mutex_destroy(&sourcesMutex_);
    pthread_mutex_destroy(&senderIPMutex_);
#endif
}

bool OSCReceiver::Start() {
    if (running_) return false;
    running_ = true;
#ifdef _WIN32
    listenThread_ = CreateThread(NULL, 0, ListenThreadEntry, this, 0, NULL);
    return listenThread_ != NULL;
#else
    int rc = pthread_create(&listenThread_, nullptr, ListenThreadEntry, this);
    listenThreadValid_ = (rc == 0);
    return listenThreadValid_;
#endif
}

void OSCReceiver::Stop() {
    if (!running_) return;
    running_ = false;

    // Closing the socket unblocks recvfrom in the listen thread
    {
        CritSectionGuard lk(socketMutex_);
        if (rawSock_ != OSC_INVALID_SOCK) {
            OscSocketClose(rawSock_);
            rawSock_ = OSC_INVALID_SOCK;
        }
    }

#ifdef _WIN32
    if (listenThread_ != NULL) {
        WaitForSingleObject(listenThread_, 3000);
        CloseHandle(listenThread_);
        listenThread_ = NULL;
    }
#else
    if (listenThreadValid_) {
        pthread_join(listenThread_, nullptr);
        listenThreadValid_ = false;
    }
#endif
}

#ifdef _WIN32
DWORD WINAPI OSCReceiver::ListenThreadEntry(LPVOID param) {
    static_cast<OSCReceiver*>(param)->ListenThread();
    return 0;
}
#else
void* OSCReceiver::ListenThreadEntry(void* param) {
    static_cast<OSCReceiver*>(param)->ListenThread();
    return nullptr;
}
#endif

void OSCReceiver::ListenThread() {
    OscSock sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (sock == OSC_INVALID_SOCK) {
        std::cerr << "osc socket create fail " << OscSocketError() << "\n";
        return;
    }

    // Allow sharing the port (defensive; we're the only user here)
#ifdef _WIN32
    BOOL opt = TRUE;
    setsockopt(sock, SOL_SOCKET, SO_REUSEADDR, (char*)&opt, sizeof(opt));

    // 200 ms receive timeout so we can check running_ periodically
    DWORD tv = 200;
    setsockopt(sock, SOL_SOCKET, SO_RCVTIMEO, (char*)&tv, sizeof(tv));
#else
    int opt = 1;
    setsockopt(sock, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

    // 200 ms receive timeout so we can check running_ periodically
    struct timeval tv;
    tv.tv_sec  = 0;
    tv.tv_usec = 200000;
    setsockopt(sock, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
#endif

    sockaddr_in bindAddr{};
    bindAddr.sin_family      = AF_INET;
    bindAddr.sin_port        = htons((unsigned short)port_);
    bindAddr.sin_addr.s_addr = INADDR_ANY;

    if (bind(sock, (sockaddr*)&bindAddr, sizeof(bindAddr)) != 0) {
        std::cerr << "osc bind fail port=" << port_
                  << " err=" << OscSocketError() << "\n";
        OscSocketClose(sock);
        return;
    }

    {
        CritSectionGuard lk(socketMutex_);
        rawSock_ = sock;
    }
    std::cout << "osc :" << port_ << " (send+recv on same socket)\n";

    char buf[4096];
    while (running_) {
        sockaddr_in from{};
#ifdef _WIN32
        int fromLen = sizeof(from);
#else
        socklen_t fromLen = sizeof(from);
#endif
        int n = recvfrom(sock, buf, sizeof(buf), 0, (sockaddr*)&from, &fromLen);
        if (n <= 0) continue;   // timeout or error — loop again
        DispatchPacket(buf, n, from);
    }
    // Socket already closed in Stop(), or close it here if Stop set rawSock_=OSC_INVALID_SOCK
    {
        CritSectionGuard lk(socketMutex_);
        if (rawSock_ != OSC_INVALID_SOCK) {
            OscSocketClose(rawSock_);
            rawSock_ = OSC_INVALID_SOCK;
        }
    }
}

// ── packet parser ─────────────────────────────────────────────────────────────

// Read a null-terminated, 4-byte-padded OSC string at *offset.
// Returns a pointer into `data` (valid while data is alive).
static const char* ReadOSCStr(const char* data, int len, int& offset) {
    int start = offset;
    while (offset < len && data[offset]) ++offset;
    if (offset >= len) return nullptr;
    const char* s = data + start;
    ++offset;
    offset = (offset + 3) & ~3;
    return s;
}

void OSCReceiver::DispatchPacket(const char* data, int len,
                                 const sockaddr_in& from) {
    // Record sender IP/port so SendSpeakerGains knows where to reply
    {
        char ipBuf[20];
        const auto* a = reinterpret_cast<const uint8_t*>(&from.sin_addr.s_addr);
        snprintf(ipBuf, sizeof(ipBuf), "%u.%u.%u.%u", a[0], a[1], a[2], a[3]);
        CritSectionGuard lock(senderIPMutex_);
        lastSenderIP_   = ipBuf;
        lastSenderPort_ = ntohs(from.sin_port);
    }

    // Minimal OSC message parser (no bundle support needed)
    // Layout: null-padded address | null-padded type-tags | packed args
    int offset = 0;

    const char* addr = ReadOSCStr(data, len, offset);
    if (!addr) return;

    // Only care about /spatial/source_pos  and  /spatial/source_pos/N
    if (std::strncmp(addr, "/spatial/source_pos", 19) != 0) return;

    const char* tags = ReadOSCStr(data, len, offset);
    if (!tags || tags[0] != ',') return;
    ++tags; // skip leading ','

    int sourceId = 0;
    float x = 0.f, y = 0.f, z = 0.f;

    // Format A: address ends with /N  →  args are (float x, float y, float z)
    if (addr[19] == '/' && addr[20] >= '0' && addr[20] <= '9') {
        sourceId = addr[20] - '0';
        if (len - offset < 12) return;
        x = ReadFloat32BE(data + offset);     offset += 4;
        y = ReadFloat32BE(data + offset);     offset += 4;
        z = ReadFloat32BE(data + offset);
    }
    // Format B: address is exactly /spatial/source_pos  →  args (int|float id, float x, float y, float z)
    else {
        if (len - offset < 4) return;
        if (tags[0] == 'i') {
            sourceId = ReadInt32BE(data + offset); offset += 4; ++tags;
        } else if (tags[0] == 'f') {
            sourceId = (int)ReadFloat32BE(data + offset); offset += 4; ++tags;
        } else {
            return;
        }
        if (len - offset < 12) return;
        x = (tags[0] == 'f') ? ReadFloat32BE(data + offset) : 0.f; offset += 4;
        y = (tags[1] == 'f') ? ReadFloat32BE(data + offset) : 0.f; offset += 4;
        z = (tags[2] == 'f') ? ReadFloat32BE(data + offset) : 0.f;
    }

    StorePosition(sourceId, x, y, z);
}

void OSCReceiver::StorePosition(int sourceId, float x, float y, float z) {
    lastX_ = x; lastY_ = y; lastZ_ = z;

    {
        CritSectionGuard lock(sourcesMutex_);
        if (sourceId >= (int)sourcePositions_.size())
            sourcePositions_.resize(sourceId + 1, {0.f, 0.f, 0.f});
        sourcePositions_[sourceId] = {x, y, z};
    }

    packetCount_.fetch_add(1, std::memory_order_relaxed);

    if (multiSourcePositionCallback_)
        multiSourcePositionCallback_(sourceId, x, y, z);
    else if (positionCallback_)
        positionCallback_(x, y, z);
}

// ── accessors ─────────────────────────────────────────────────────────────────

int OSCReceiver::GetSourcePositions(
        std::array<std::array<float, 3>, kMaxSources>& out) const {
    CritSectionGuard lock(sourcesMutex_);
    int n = (int)std::min(sourcePositions_.size(), (size_t)kMaxSources);
    for (int i = 0; i < n; ++i) out[i] = sourcePositions_[i];
    return n;
}

std::string OSCReceiver::GetLastSenderIP() const {
    CritSectionGuard lock(senderIPMutex_);
    return lastSenderIP_;
}

int OSCReceiver::GetLastSenderPort() const {
    CritSectionGuard lock(senderIPMutex_);
    return lastSenderPort_;
}

// ── sender ────────────────────────────────────────────────────────────────────

void OSCReceiver::SendSpeakerGains(int sourceId, const std::vector<float>& gains) {
    OscSock sock;
    { CritSectionGuard lk(socketMutex_); sock = rawSock_; }
    if (sock == OSC_INVALID_SOCK || gains.empty()) return;

    std::string ip;
    int port;
    {
        CritSectionGuard lk(senderIPMutex_);
        ip   = lastSenderIP_;
        port = lastSenderPort_;   // reply to whatever port Unity sent FROM
    }
    if (ip.empty() || port <= 0) return;

    // Build OSC message: /spatial/speaker_gains ,if...f sourceId gain[0]...gain[N-1]
    std::vector<char> buf;
    buf.reserve(256);

    AppendStr(buf, "/spatial/speaker_gains");

    std::string tag = ",i";
    tag.append(gains.size(), 'f');
    AppendStr(buf, tag.c_str());

    AppendI32(buf, sourceId);
    for (float g : gains) AppendF32(buf, g);

    sockaddr_in dest{};
    dest.sin_family      = AF_INET;
    dest.sin_port        = htons((unsigned short)port);
    dest.sin_addr.s_addr = inet_addr(ip.c_str());

    sendto(sock, buf.data(), (int)buf.size(), 0,
           (const sockaddr*)&dest, sizeof(dest));
}
