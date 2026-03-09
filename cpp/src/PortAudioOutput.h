#pragma once

#ifndef USE_JACK

#include "IAudioOutput.h"
#include <portaudio.h>
#include <vector>

/**
 * PortAudio output backend (fallback when USE_JACK=OFF).
 * Uses interleaved format internally; deinterleaves to planar for callback compatibility.
 */
class PortAudioOutput : public IAudioOutput {
public:
    PortAudioOutput();
    ~PortAudioOutput() override;

    bool Initialize(const std::vector<Speaker>& speakers, int sampleRate) override;
    bool Start(ProcessCallback callback) override;
    void Stop() override;

    int GetNumChannels() const override { return numChannels_; }
    int GetSampleRate() const override { return sampleRate_; }

private:
    static int PaCallback_(const void* input, void* output, unsigned long nframes,
                           const PaStreamCallbackTimeInfo* timeInfo,
                           PaStreamCallbackFlags flags, void* userData);

    PaStream* stream_;
    int numChannels_;
    int sampleRate_;
    ProcessCallback processCallback_;
    static constexpr int kMaxFrames = 8192;
    std::vector<float> scratchPlanar_;
};

#endif
