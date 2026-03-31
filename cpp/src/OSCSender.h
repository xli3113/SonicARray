#pragma once

#ifdef _WIN32
  #ifndef WIN32_LEAN_AND_MEAN
  #define WIN32_LEAN_AND_MEAN
  #endif
  #include <windows.h>
  #include <winsock2.h>
  #include <ws2tcpip.h>
  using OscSendSock = SOCKET;
  #define OSC_SEND_INVALID INVALID_SOCKET
#else
  #include <sys/socket.h>
  #include <netinet/in.h>
  #include <arpa/inet.h>
  #include <unistd.h>
  using OscSendSock = int;
  #define OSC_SEND_INVALID (-1)
#endif

#include <string>
#include <vector>

/// UDP sender that can optionally bind to a specific source port.
///
/// When srcPort > 0, the socket is bound to 0.0.0.0:srcPort with SO_REUSEADDR
/// so that outgoing datagrams always carry that source port.  This is critical
/// for traversing Android's stateful firewall: the Quest sends position packets
/// from port 7002 TO 192.168.137.1:7000.  Android's connection-tracking creates
/// the reverse-flow entry  src=192.168.137.1:7000 dst=192.168.137.66:7002.
/// The backend must therefore reply FROM port 7000 for those packets to be
/// allowed in.
class OSCSender {
public:
    /// @param host     Destination host (IP string)
    /// @param port     Destination port
    /// @param srcPort  Local source port to bind to (0 = OS-assigned ephemeral)
    OSCSender(const std::string& host, int port, int srcPort = 0);
    ~OSCSender();

    void SendSpeakerGains(int sourceId, const std::vector<float>& gains);

private:
    OscSendSock sock_;
    sockaddr_in dest_;
};
