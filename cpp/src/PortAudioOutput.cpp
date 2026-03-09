#ifndef USE_JACK

#include "PortAudioOutput.h"
#include <iostream>
#include <cstring>

PortAudioOutput::PortAudioOutput()
    : stream_(nullptr), numChannels_(0), sampleRate_(0) {
}

PortAudioOutput::~PortAudioOutput() {
    Stop();
}

bool PortAudioOutput::Initialize(const std::vector<Speaker>& speakers, int sampleRate) {
    if (speakers.empty()) {
        std::cerr << "PortAudioOutput: need at least 1 speaker\n";
        return false;
    }

    numChannels_ = static_cast<int>(speakers.size());
    sampleRate_ = sampleRate;
    scratchPlanar_.resize(numChannels_ * kMaxFrames);

    PaError err = Pa_Initialize();
    if (err != paNoError) {
        std::cerr << "Pa_Initialize fail\n";
        return false;
    }

    PaStreamParameters outParams;
    outParams.device = Pa_GetDefaultOutputDevice();
    if (outParams.device == paNoDevice) {
        std::cerr << "no audio device\n";
        Pa_Terminate();
        return false;
    }
    const PaDeviceInfo* dev = Pa_GetDeviceInfo(outParams.device);
    outParams.channelCount = numChannels_;
    outParams.sampleFormat = paFloat32;
    outParams.suggestedLatency = dev ? dev->defaultLowOutputLatency : 0.02;
    outParams.hostApiSpecificStreamInfo = nullptr;

    err = Pa_OpenStream(&stream_, nullptr, &outParams, sampleRate_,
                        paFramesPerBufferUnspecified, paClipOff, PaCallback_, this);
    if (err != paNoError) {
        std::cerr << "Pa_OpenStream fail " << numChannels_ << "ch\n";
        Pa_Terminate();
        return false;
    }

    std::cout << "PortAudioOutput: " << numChannels_ << "ch " << sampleRate_ << "Hz\n";
    return true;
}

bool PortAudioOutput::Start(ProcessCallback callback) {
    if (!stream_ || !callback) {
        std::cerr << "PortAudioOutput: not initialized or no callback\n";
        return false;
    }
    processCallback_ = std::move(callback);

    PaError err = Pa_StartStream(stream_);
    if (err != paNoError) {
        std::cerr << "Pa_StartStream fail\n";
        return false;
    }
    return true;
}

void PortAudioOutput::Stop() {
    if (stream_ && Pa_IsStreamActive(stream_)) {
        Pa_StopStream(stream_);
    }
    if (stream_) {
        Pa_CloseStream(stream_);
        stream_ = nullptr;
    }
    Pa_Terminate();
}

int PortAudioOutput::PaCallback_(const void* input, void* output, unsigned long nframes,
                                 const PaStreamCallbackTimeInfo*, PaStreamCallbackFlags, void* userData) {
    (void)input;
    PortAudioOutput* self = static_cast<PortAudioOutput*>(userData);
    if (!self->processCallback_) return paComplete;

    const int nc = self->numChannels_;
    float* out = static_cast<float*>(output);

    if (nframes > static_cast<unsigned long>(kMaxFrames)) return paComplete;

    float* bufs[256];
    if (nc > 256) return paComplete;
    for (int ch = 0; ch < nc; ++ch) {
        bufs[ch] = self->scratchPlanar_.data() + ch * kMaxFrames;
        std::memset(bufs[ch], 0, nframes * sizeof(float));
    }
    self->processCallback_(bufs, nframes);

    for (unsigned long i = 0; i < nframes; ++i) {
        for (int ch = 0; ch < nc; ++ch) {
            out[i * nc + ch] = bufs[ch][i];
        }
    }

    return paContinue;
}

#endif
