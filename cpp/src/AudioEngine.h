#pragma once

#include "SpatialRenderer.h"
#include "OSCReceiver.h"
#include "TCPGainsSender.h"
#include "IAudioOutput.h"
#include <vector>
#include <string>
#include <atomic>
#include <memory>

class AudioEngine {
public:
    AudioEngine();
    ~AudioEngine();

    bool Initialize(const std::vector<Speaker>& speakers, int sampleRate = 44100);
    void Shutdown();

    bool Start();
    void Stop();

    void SetSourcePosition(float x, float y, float z);
    void SetSourcePosition(int sourceId, float x, float y, float z);

    bool LoadAudioFile(const std::string& filepath);
    void EnableSineWave(bool enable) { useSineWave_ = enable; }
    void SendSpeakerGainsToUnity();

    int GetSampleRate() const { return sampleRate_; }
    int GetNumChannels() const { return numChannels_; }
    SpatialRenderer* GetRenderer()    const { return renderer_.get(); }
    OSCReceiver*     GetOSCReceiver() const { return oscReceiver_; }
    uint64_t         GetGainsSentCount() const { return gainsSent_; }
    void SetRenderer(std::unique_ptr<SpatialRenderer> renderer);

private:
    void ProcessAudioPlanar(float** outChannels, unsigned long nframes);

    std::unique_ptr<IAudioOutput> output_;
    std::unique_ptr<SpatialRenderer> renderer_;
    OSCReceiver*    oscReceiver_;
    TCPGainsSender* tcpSender_;
    int             tcpClientsConnected_ = 0; // for dashboard

    std::vector<Speaker> speakers_;
    int sampleRate_;
    int numChannels_;

    std::vector<float> audioBuffer_;
    size_t audioBufferPos_;
    bool useSineWave_;
    float sinePhase_;

    float GenerateSineWave();

    std::atomic<bool>     running_;
    std::atomic<uint64_t> gainsSent_{0};
};
