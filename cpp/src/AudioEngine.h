#pragma once

#include "SpatialRenderer.h"
#include "OSCReceiver.h"
#include "OSCSender.h"
#include "IAudioOutput.h"
#include <vector>
#include <string>
#include <atomic>
#include <memory>
#include <thread>

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

    int GetSampleRate() const { return sampleRate_; }
    int GetNumChannels() const { return numChannels_; }
    SpatialRenderer* GetRenderer() const { return renderer_.get(); }
    void SetRenderer(std::unique_ptr<SpatialRenderer> renderer);

    // Configure where gain feedback OSC packets are sent (default: 127.0.0.1:9000).
    // Must be called before Initialize().
    void SetFeedbackTarget(const std::string& host, int port) {
        feedbackHost_ = host;
        feedbackPort_ = port;
    }

private:
    void ProcessAudioPlanar(float** outChannels, unsigned long nframes);
    void GainFeedbackThread();

    std::unique_ptr<IAudioOutput> output_;
    std::unique_ptr<SpatialRenderer> renderer_;
    OSCReceiver* oscReceiver_;
    OSCSender* oscSender_;

    std::thread gainFeedbackThread_;
    std::atomic<bool> gainFeedbackRunning_;

    std::string feedbackHost_;
    int feedbackPort_;

    std::vector<int> speakerIds_;   // parallel to speakers_, built in Initialize()

    std::vector<Speaker> speakers_;
    int sampleRate_;
    int numChannels_;

    std::vector<float> audioBuffer_;
    size_t audioBufferPos_;
    bool useSineWave_;
    float sinePhase_;

    float GenerateSineWave();

    std::atomic<bool> running_;
};
