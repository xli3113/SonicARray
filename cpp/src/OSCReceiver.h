#pragma once

#include <string>
#include <thread>
#include <atomic>
#include <functional>

class OSCReceiver {
public:
    using PositionCallback = std::function<void(float, float, float)>;
    
    OSCReceiver(int port = 7000);
    ~OSCReceiver();
    
    bool Start();
    void Stop();
    
    void SetPositionCallback(PositionCallback callback) {
        positionCallback_ = callback;
    }
    
    float GetLastX() const { return lastX_; }
    float GetLastY() const { return lastY_; }
    float GetLastZ() const { return lastZ_; }

private:
    void ListenThread();
    
    int port_;
    std::atomic<bool> running_;
    std::thread listenThread_;
    
    float lastX_, lastY_, lastZ_;
    PositionCallback positionCallback_;
};
