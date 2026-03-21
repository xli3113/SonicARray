#include "OSCSender.h"
#include <iostream>
#include <cstring>
#include <vector>
#include <string>

static constexpr size_t kOutputBufferSize = 4096;

// Big-endian helpers (OSC wire format is big-endian)
static uint32_t htonFloat(float f) {
    uint32_t u;
    std::memcpy(&u, &f, 4);
    return htonl(u);
}

// OSC string: null-terminated, padded to next 4-byte boundary
static void AppendOSCString(std::vector<char>& buf, const char* s) {
    size_t len = std::strlen(s) + 1; // include null
    buf.insert(buf.end(), s, s + len);
    while (buf.size() % 4) buf.push_back(0);
}

static void AppendInt32BE(std::vector<char>& buf, int32_t v) {
    uint32_t n = htonl(static_cast<uint32_t>(v));
    char* p = reinterpret_cast<char*>(&n);
    buf.insert(buf.end(), p, p + 4);
}

static void AppendFloat32BE(std::vector<char>& buf, float v) {
    uint32_t n = htonFloat(v);
    char* p = reinterpret_cast<char*>(&n);
    buf.insert(buf.end(), p, p + 4);
}

// ─────────────────────────────────────────────────────────────────────────────

OSCSender::OSCSender(const std::string& host, int port, int srcPort)
    : sock_(INVALID_SOCKET)
{
    // Winsock may already be initialised; calling WSAStartup again is safe.
    WSADATA wsa;
    WSAStartup(MAKEWORD(2, 2), &wsa);

    sock_ = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (sock_ == INVALID_SOCKET) {
        std::cerr << "OSCSender: socket() failed " << WSAGetLastError() << "\n";
        return;
    }

    if (srcPort > 0) {
        // Allow sharing the port with the OSC listener on the same port
        BOOL opt = TRUE;
        setsockopt(sock_, SOL_SOCKET, SO_REUSEADDR,
                   reinterpret_cast<const char*>(&opt), sizeof(opt));

        sockaddr_in bindAddr{};
        bindAddr.sin_family      = AF_INET;
        bindAddr.sin_port        = htons(static_cast<u_short>(srcPort));
        bindAddr.sin_addr.s_addr = INADDR_ANY;
        if (bind(sock_, reinterpret_cast<sockaddr*>(&bindAddr),
                 sizeof(bindAddr)) == SOCKET_ERROR) {
            std::cerr << "OSCSender: bind(:" << srcPort
                      << ") failed " << WSAGetLastError()
                      << " — falling back to ephemeral port\n";
            // Non-fatal: we can still send, just without the fixed source port
        } else {
            std::cout << "osc sender bound src=:" << srcPort
                      << " -> " << host << ":" << port << "\n";
        }
    }

    dest_.sin_family = AF_INET;
    dest_.sin_port   = htons(static_cast<u_short>(port));
    dest_.sin_addr.s_addr = inet_addr(host.c_str());
}

OSCSender::~OSCSender() {
    if (sock_ != INVALID_SOCKET) {
        closesocket(sock_);
        sock_ = INVALID_SOCKET;
    }
}

void OSCSender::SendSpeakerGains(int sourceId, const std::vector<float>& gains) {
    if (sock_ == INVALID_SOCKET || gains.empty()) return;

    // Build OSC message manually so we are not constrained by UdpTransmitSocket.
    // Format: /spatial/speaker_gains  ,if...f   sourceId  gain[0]...gain[N-1]
    std::vector<char> buf;
    buf.reserve(256);

    AppendOSCString(buf, "/spatial/speaker_gains");

    // Type tag: ",i" + N×'f'
    std::string tag = ",i";
    tag.append(gains.size(), 'f');
    AppendOSCString(buf, tag.c_str());

    AppendInt32BE(buf, sourceId);
    for (float g : gains) AppendFloat32BE(buf, g);

    sendto(sock_,
           buf.data(), static_cast<int>(buf.size()), 0,
           reinterpret_cast<const sockaddr*>(&dest_),
           sizeof(dest_));
}
