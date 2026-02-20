#include "OSCReceiver.h"
#include "osc/OscReceivedElements.h"
#include "osc/OscPacketListener.h"
#include "ip/UdpSocket.h"
#include <iostream>
#include <thread>
#include <chrono>
#include <cstring>

class OSCListener : public osc::OscPacketListener {
public:
    OSCListener(OSCReceiver* receiver) : receiver_(receiver) {}

protected:
    void ProcessMessage(const osc::ReceivedMessage& m, const IpEndpointName& remoteEndpoint) override {
        (void)remoteEndpoint;
        try {
            const char* addr = m.AddressPattern();
            if (std::strncmp(addr, "/spatial/source_pos", 18) != 0) return;

            int sourceId = 0;
            float x = 0.0f, y = 0.0f, z = 0.0f;

            if (addr[18] == '/' && addr[19] >= '0' && addr[19] <= '9') {
                sourceId = addr[19] - '0';
                osc::ReceivedMessageArgumentStream args = m.ArgumentStream();
                args >> x >> y >> z >> osc::EndMessage;
            } else {
                osc::ReceivedMessage::const_iterator it = m.ArgumentsBegin();
                if (it == m.ArgumentsEnd()) return;
                if (it->IsInt32()) {
                    sourceId = it->AsInt32();
                    ++it;
                } else if (it->IsFloat()) {
                    sourceId = static_cast<int>(it->AsFloat());
                    ++it;
                }
                if (it != m.ArgumentsEnd()) x = it->IsFloat() ? it->AsFloat() : (it->IsInt32() ? (float)it->AsInt32() : 0.0f);
                ++it;
                if (it != m.ArgumentsEnd()) y = it->IsFloat() ? it->AsFloat() : (it->IsInt32() ? (float)it->AsInt32() : 0.0f);
                ++it;
                if (it != m.ArgumentsEnd()) z = it->IsFloat() ? it->AsFloat() : (it->IsInt32() ? (float)it->AsInt32() : 0.0f);
            }

            receiver_->lastX_ = x;
            receiver_->lastY_ = y;
            receiver_->lastZ_ = z;

            {
                std::lock_guard<std::mutex> lock(receiver_->sourcesMutex_);
                if (sourceId >= static_cast<int>(receiver_->sourcePositions_.size())) {
                    receiver_->sourcePositions_.resize(sourceId + 1, {0.0f, 0.0f, 0.0f});
                }
                receiver_->sourcePositions_[sourceId] = {x, y, z};
            }

            if (receiver_->multiSourcePositionCallback_) {
                receiver_->multiSourcePositionCallback_(sourceId, x, y, z);
            } else if (receiver_->positionCallback_) {
                receiver_->positionCallback_(x, y, z);
            }
        } catch (osc::Exception& e) {
            std::cerr << "osc err " << e.what() << "\n";
        }
    }

private:
    OSCReceiver* receiver_;
};

int OSCReceiver::GetSourcePositions(std::array<std::array<float, 3>, kMaxSources>& out) const {
    std::lock_guard<std::mutex> lock(sourcesMutex_);
    int n = static_cast<int>(std::min(sourcePositions_.size(), static_cast<size_t>(kMaxSources)));
    for (int i = 0; i < n; ++i) {
        out[i] = sourcePositions_[i];
    }
    return n;
}

OSCReceiver::OSCReceiver(int port)
    : socket_(nullptr), port_(port), running_(false), lastX_(0.0f), lastY_(0.0f), lastZ_(0.0f) {
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
    if (!running_) return;
    running_ = false;

    {
        std::lock_guard<std::mutex> lk(socketMutex_);
        if (socket_) socket_->AsynchronousBreak();
    }

    if (listenThread_.joinable())
        listenThread_.join();
}

void OSCReceiver::ListenThread() {
    OSCListener* listener = new OSCListener(this);

    try {
        auto* socket = new UdpListeningReceiveSocket(
            IpEndpointName(IpEndpointName::ANY_ADDRESS, port_),
            listener
        );

        {
            std::lock_guard<std::mutex> lk(socketMutex_);
            socket_ = socket;
        }

        std::cout << "osc " << port_ << "\n";

        socket->Run();

        {
            std::lock_guard<std::mutex> lk(socketMutex_);
            socket_ = nullptr;
        }

        delete socket;
    } catch (std::exception& e) {
        std::cerr << "osc socket fail\n";
    }

    delete listener;
}
