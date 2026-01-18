#include "OSCReceiver.h"
#include "osc/OscReceivedElements.h"
#include "osc/OscPacketListener.h"
#include "ip/UdpSocket.h"
#include <iostream>
#include <thread>
#include <chrono>

class OSCListener : public osc::OscPacketListener {
public:
    OSCListener(OSCReceiver* receiver) : receiver_(receiver) {}
    
protected:
    void ProcessMessage(const osc::ReceivedMessage& m, const IpEndpointName& remoteEndpoint) override {
        try {
            if (std::strcmp(m.AddressPattern(), "/spatial/source_pos") == 0) {
                osc::ReceivedMessageArgumentStream args = m.ArgumentStream();
                float x, y, z;
                args >> x >> y >> z >> osc::EndMessage;
                
                receiver_->lastX_ = x;
                receiver_->lastY_ = y;
                receiver_->lastZ_ = z;
                
                if (receiver_->positionCallback_) {
                    receiver_->positionCallback_(x, y, z);
                }
            }
        } catch (osc::Exception& e) {
            std::cerr << "OSC Error: " << e.what() << std::endl;
        }
    }
    
private:
    OSCReceiver* receiver_;
};

OSCReceiver::OSCReceiver(int port) 
    : port_(port), running_(false), lastX_(0.0f), lastY_(0.0f), lastZ_(0.0f) {
}

OSCReceiver::~OSCReceiver() {
    Stop();
}

bool OSCReceiver::Start() {
    if (running_) {
        return false;
    }
    
    running_ = true;
    listenThread_ = std::thread(&OSCReceiver::ListenThread, this);
    
    return true;
}

void OSCReceiver::Stop() {
    if (running_) {
        running_ = false;
        if (listenThread_.joinable()) {
            listenThread_.join();
        }
    }
}

void OSCReceiver::ListenThread() {
    OSCListener* listener = new OSCListener(this);
    
    try {
        UdpListeningReceiveSocket socket(
            IpEndpointName(IpEndpointName::ANY_ADDRESS, port_),
            listener
        );
        
        std::cout << "OSC Receiver listening on port " << port_ << std::endl;
        
        // Run socket in a loop that can be interrupted
        while (running_) {
            try {
                // Process available messages (non-blocking if possible)
                socket.Run();
            } catch (std::exception& e) {
                if (running_) {
                    std::cerr << "OSC socket error: " << e.what() << std::endl;
                    std::this_thread::sleep_for(std::chrono::milliseconds(100));
                }
            }
        }
    } catch (std::exception& e) {
        std::cerr << "Failed to create OSC socket: " << e.what() << std::endl;
    }
    
    delete listener;
}
