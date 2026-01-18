#pragma once

#include "SpatialRenderer.h"
#include "OSCReceiver.h"
#include <portaudio.h>
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
    
    // Load audio file (mono WAV)
    bool LoadAudioFile(const std::string& filepath);
    
    // Generate pink noise if no file loaded
    void EnablePinkNoise(bool enable) { usePinkNoise_ = enable; }
    
    int GetSampleRate() const { return sampleRate_; }
    int GetNumChannels() const { return numChannels_; }
    
    // Get renderer for debug output
    SpatialRenderer* GetRenderer() const { return renderer_.get(); }
    
    // Set renderer (for switching algorithms)
    void SetRenderer(std::unique_ptr<SpatialRenderer> renderer);

private:
    static int AudioCallback(const void* inputBuffer,
                            void* outputBuffer,
                            unsigned long framesPerBuffer,
                            const PaStreamCallbackTimeInfo* timeInfo,
                            PaStreamCallbackFlags statusFlags,
                            void* userData);
    
    int ProcessAudio(float* output, unsigned long framesPerBuffer);
    
    PaStream* stream_;
    std::unique_ptr<SpatialRenderer> renderer_;
    OSCReceiver* oscReceiver_;
    
    std::vector<Speaker> speakers_;
    int sampleRate_;
    int numChannels_;
    
    // Audio source
    std::vector<float> audioBuffer_;
    size_t audioBufferPos_;
    bool usePinkNoise_;
    
    // Pink noise state
    float pinkNoiseState_[7];
    
    float GeneratePinkNoise();
    
    std::atomic<bool> running_;
};
