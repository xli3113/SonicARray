#include "OSCSender.h"
#include "ip/UdpSocket.h"
#include "ip/IpEndpointName.h"
#include "osc/OscOutboundPacketStream.h"
#include <iostream>

static constexpr int kSendBufSize = 8192;

OSCSender::OSCSender(const std::string& host, int port) : socket_(nullptr) {
    try {
        socket_ = new UdpTransmitSocket(IpEndpointName(host.c_str(), port));
        std::cout << "osc feedback -> " << host << ":" << port << "\n";
    } catch (std::exception& e) {
        std::cerr << "OSCSender init fail: " << e.what() << "\n";
    }
}

OSCSender::~OSCSender() {
    delete socket_;
}

void OSCSender::SendGains(int sourceId,
                          const std::vector<int>& speakerIds,
                          const std::vector<float>& gains) {
    if (!socket_ || speakerIds.size() != gains.size()) return;

    char buffer[kSendBufSize];
    osc::OutboundPacketStream p(buffer, kSendBufSize);

    p << osc::BeginMessage("/vbap/gains")
      << (int32_t)sourceId
      << (int32_t)speakerIds.size();

    for (size_t i = 0; i < speakerIds.size(); ++i) {
        p << (int32_t)speakerIds[i] << gains[i];
    }

    p << osc::EndMessage;

    try {
        socket_->Send(p.Data(), p.Size());
    } catch (std::exception& e) {
        std::cerr << "OSCSender send fail: " << e.what() << "\n";
    }
}
