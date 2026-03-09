#ifdef USE_JACK

#include "JackAudioOutput.h"
#include <iostream>
#include <cstring>

JackAudioOutput::JackAudioOutput()
    : client_(nullptr), numChannels_(0), sampleRate_(0) {
}

JackAudioOutput::~JackAudioOutput() {
    Stop();
}

bool JackAudioOutput::Initialize(const std::vector<Speaker>& speakers, int sampleRate) {
    if (speakers.empty()) {
        std::cerr << "JackAudioOutput: need at least 1 speaker\n";
        return false;
    }

    numChannels_ = static_cast<int>(speakers.size());
    sampleRate_ = sampleRate;

    jack_status_t status;
    client_ = jack_client_open("SonicARrayBackend", JackNoStartServer, &status);
    if (!client_) {
        std::cerr << "jack_client_open fail: " << (status & JackServerFailed ? "server not running" : "error") << "\n";
        return false;
    }

    int jackSr = static_cast<int>(jack_get_sample_rate(client_));
    if (jackSr != sampleRate) {
        std::cerr << "JACK sr " << jackSr << " != requested " << sampleRate << ", using JACK sr\n";
        sampleRate_ = jackSr;
    }

    portNames_.resize(numChannels_);
    ports_.resize(numChannels_, nullptr);

    for (int i = 0; i < numChannels_; ++i) {
        portNames_[i] = "spk_" + std::to_string(speakers[i].id);
        ports_[i] = jack_port_register(client_, portNames_[i].c_str(),
                                       JACK_DEFAULT_AUDIO_TYPE, JackPortIsOutput, 0);
        if (!ports_[i]) {
            std::cerr << "jack_port_register fail for " << portNames_[i] << "\n";
            Stop();
            return false;
        }
    }

    std::cout << "JackAudioOutput: " << numChannels_ << " ports (spk_<id>), sr=" << sampleRate_ << "\n";
    return true;
}

bool JackAudioOutput::Start(ProcessCallback callback) {
    if (!client_) {
        std::cerr << "JackAudioOutput: not initialized\n";
        return false;
    }
    processCallback_ = std::move(callback);
    if (!processCallback_) {
        std::cerr << "JackAudioOutput: no process callback\n";
        return false;
    }

    if (jack_set_process_callback(client_, ProcessCallback_, this) != 0) {
        std::cerr << "jack_set_process_callback fail\n";
        return false;
    }

    if (jack_activate(client_) != 0) {
        std::cerr << "jack_activate fail\n";
        return false;
    }

    std::cout << "JACK active, ports: SonicARrayBackend:spk_<id>\n";
    return true;
}

void JackAudioOutput::Stop() {
    if (!client_) return;

    jack_deactivate(client_);
    jack_client_close(client_);
    client_ = nullptr;
    ports_.clear();
    portNames_.clear();
}

int JackAudioOutput::ProcessCallback_(jack_nframes_t nframes, void* arg) {
    JackAudioOutput* self = static_cast<JackAudioOutput*>(arg);
    if (!self->processCallback_) return 0;

    const int nc = self->numChannels_;
    if (nc == 0) return 0;

    constexpr int kMaxChannels = 256;
    float* bufs[kMaxChannels];
    if (nc > kMaxChannels) return 0;

    for (int ch = 0; ch < nc; ++ch) {
        bufs[ch] = static_cast<float*>(jack_port_get_buffer(self->ports_[ch], nframes));
        if (bufs[ch]) std::memset(bufs[ch], 0, nframes * sizeof(float));
    }

    self->processCallback_(bufs, nframes);
    return 0;
}

#endif
