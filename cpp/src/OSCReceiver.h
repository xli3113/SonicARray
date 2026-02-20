#pragma once

#include <string>
#include <thread>
#include <atomic>
#include <functional>
#include <vector>
#include <mutex>
#include <array>

class UdpListeningReceiveSocket;
class IpEndpointName;

using PositionCallback = std::function<void(float, float, float)>;
using MultiSourcePositionCallback = std::function<void(int, float, float, float)>;

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

private:
    friend class OSCListener;
    void ListenThread();

    std::mutex socketMutex_;
    UdpListeningReceiveSocket* socket_;

    int port_;
    std::atomic<bool> running_;
    std::thread listenThread_;
    
    float lastX_, lastY_, lastZ_;
    PositionCallback positionCallback_;
    MultiSourcePositionCallback multiSourcePositionCallback_;
    
    mutable std::mutex sourcesMutex_;
    std::vector<std::array<float, 3>> sourcePositions_;
};
