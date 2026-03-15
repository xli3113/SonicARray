#pragma once

#include <string>
#include <vector>

class UdpTransmitSocket;

// Sends VBAP gain data back to Unity via OSC UDP.
// Thread-safe: Send() may be called from any thread.
class OSCSender {
public:
    OSCSender(const std::string& host, int port);
    ~OSCSender();

    // Sends /vbap/gains with args:
    //   int sourceId, int speakerCount,
    //   int speakerId_0, float gain_0, int speakerId_1, float gain_1, ...
    void SendGains(int sourceId,
                   const std::vector<int>& speakerIds,
                   const std::vector<float>& gains);

private:
    UdpTransmitSocket* socket_;
};
