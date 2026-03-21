#pragma once

#ifdef USE_JACK

#include "IAudioOutput.h"
#include <jack/jack.h>
#include <vector>
#include <string>

/**
 * JACK audio output backend.
 * Port names: spk_<speaker.id> (from speakers.yaml).
 * numChannels = speakers.size(), fully dynamic.
 */
class JackAudioOutput : public IAudioOutput {
public:
    JackAudioOutput();
    ~JackAudioOutput() override;

    bool Initialize(const std::vector<Speaker>& speakers, int sampleRate) override;
    bool Start(ProcessCallback callback) override;
    void Stop() override;

    int GetNumChannels() const override { return numChannels_; }
    int GetSampleRate() const override { return sampleRate_; }

private:
    static int ProcessCallback_(jack_nframes_t nframes, void* arg);

    jack_client_t* client_;
    std::vector<jack_port_t*> ports_;
    std::vector<std::string> portNames_;
    ProcessCallback processCallback_;
    int numChannels_;
    int sampleRate_;
};

#endif
