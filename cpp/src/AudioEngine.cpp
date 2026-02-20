#include "AudioEngine.h"
#include "RendererFactory.h"
#include "SimdUtils.h"
#include <iostream>
#include <fstream>
#include <cmath>
#include <cstring>
#include <cstdint>

bool LoadWavFile(const std::string& filepath, std::vector<float>& buffer, int& sampleRate) {
    std::ifstream file(filepath, std::ios::binary);
    if (!file.is_open()) {
        return false;
    }
    
    char header[44];
    file.read(header, 44);
    
    if (strncmp(header, "RIFF", 4) != 0 || strncmp(header + 8, "WAVE", 4) != 0) {
        return false;
    }

    int32_t sr = 0;
    int32_t dataSize = 0;
    std::memcpy(&sr, header + 24, 4);
    std::memcpy(&dataSize, header + 40, 4);
    sampleRate = sr;
    if (dataSize <= 0 || dataSize > 100000000) return false;

    std::vector<int16_t> intData(dataSize / 2);
    file.read((char*)intData.data(), dataSize);
    
    buffer.resize(intData.size());
    for (size_t i = 0; i < intData.size(); ++i) {
        buffer[i] = intData[i] / 32768.0f;
    }
    
    return true;
}

AudioEngine::AudioEngine()
    : stream_(nullptr), oscReceiver_(nullptr),
      sampleRate_(44100), numChannels_(28), audioBufferPos_(0),
      useSineWave_(true), running_(false) {
    
    sinePhase_ = 0.0f;
}

AudioEngine::~AudioEngine() {
    Shutdown();
}

bool AudioEngine::Initialize(const std::vector<Speaker>& speakers, int sampleRate) {
    speakers_ = speakers;
    sampleRate_ = sampleRate;
    numChannels_ = static_cast<int>(speakers.size());
    
    PaError err = Pa_Initialize();
    if (err != paNoError) {
        std::cerr << "pa init fail\n";
        return false;
    }
    
    renderer_ = RendererFactory::Create(RendererFactory::Type::VBAP);
    if (!renderer_->Initialize(speakers_)) {
        std::cerr << "renderer init fail\n";
        return false;
    }
    std::cout << "renderer: " << renderer_->GetName() << "\n";
    
    oscReceiver_ = new OSCReceiver(7000);
    oscReceiver_->SetMultiSourcePositionCallback([this](int sourceId, float x, float y, float z) {
        this->SetSourcePosition(sourceId, x, y, z);
    });
    
    PaStreamParameters outputParams;
    outputParams.device = Pa_GetDefaultOutputDevice();
    if (outputParams.device == paNoDevice) {
        std::cerr << "no audio dev, osc only\n";
        stream_ = nullptr;
        return true;
    }
    const PaDeviceInfo* devInfo = Pa_GetDeviceInfo(outputParams.device);
    if (!devInfo) {
        std::cerr << "bad dev info, osc only\n";
        stream_ = nullptr;
        return true;
    }
    outputParams.channelCount = numChannels_;
    outputParams.sampleFormat = paFloat32;
    outputParams.suggestedLatency = devInfo->defaultLowOutputLatency;
    outputParams.hostApiSpecificStreamInfo = nullptr;
    
    err = Pa_OpenStream(
        &stream_,
        nullptr,
        &outputParams,
        sampleRate_,
        paFramesPerBufferUnspecified,
        paClipOff,
        AudioCallback,
        this
    );
    
    if (err != paNoError) {
        std::cerr << "cant open " << numChannels_ << "ch stream, osc/vbap still ok\n";
        stream_ = nullptr;
    } else {
        std::cout << numChannels_ << "ch " << sampleRate_ << "hz\n";
    }
    
    return true;
}

void AudioEngine::Shutdown() {
    Stop();
    
    if (stream_) {
        Pa_CloseStream(stream_);
        stream_ = nullptr;
    }
    
    if (oscReceiver_) {
        oscReceiver_->Stop();
        delete oscReceiver_;
        oscReceiver_ = nullptr;
    }
    
    renderer_.reset();
    
    Pa_Terminate();
}

bool AudioEngine::Start() {
    if (stream_) {
        PaError err = Pa_StartStream(stream_);
        if (err != paNoError) {
            std::cerr << "stream start fail\n";
            return false;
        }
    }
    
    if (oscReceiver_) {
        oscReceiver_->Start();
    }
    
    running_ = true;
    return true;
}

void AudioEngine::Stop() {
    running_ = false;
    
    if (stream_ && Pa_IsStreamActive(stream_)) {
        Pa_StopStream(stream_);
    }
}

void AudioEngine::SetSourcePosition(float x, float y, float z) {
    SetSourcePosition(0, x, y, z);
}

void AudioEngine::SetSourcePosition(int sourceId, float x, float y, float z) {
    if (renderer_) {
        renderer_->UpdateSourcePosition(sourceId, x, y, z);
    }
}

void AudioEngine::SetRenderer(std::unique_ptr<SpatialRenderer> renderer) {
    if (renderer && renderer->Initialize(speakers_)) {
        renderer_ = std::move(renderer);
        std::cout << "switched to " << renderer_->GetName() << "\n";
    } else {
        std::cerr << "set renderer fail\n";
    }
}

bool AudioEngine::LoadAudioFile(const std::string& filepath) {
    int fileSampleRate = 0;
    std::vector<float> buffer;
    
    if (!LoadWavFile(filepath, buffer, fileSampleRate)) {
        std::cerr << "load wav fail " << filepath << "\n";
        return false;
    }
    
    if (fileSampleRate != sampleRate_) {
        std::vector<float> resampled;
        float ratio = static_cast<float>(sampleRate_) / fileSampleRate;
        resampled.resize(static_cast<size_t>(buffer.size() * ratio));
        
        for (size_t i = 0; i < resampled.size(); ++i) {
            float srcIdx = i / ratio;
            size_t idx0 = static_cast<size_t>(srcIdx);
            size_t idx1 = std::min(idx0 + 1, buffer.size() - 1);
            float t = srcIdx - idx0;
            resampled[i] = buffer[idx0] * (1.0f - t) + buffer[idx1] * t;
        }
        
        buffer = resampled;
    }
    
    audioBuffer_ = buffer;
    audioBufferPos_ = 0;
    useSineWave_ = false;
    
    std::cout << "loaded " << filepath << " " << buffer.size() << " samps\n";
    
    return true;
}

float AudioEngine::GenerateSineWave() {
    constexpr float kPi = 3.14159265358979323846f;
    constexpr float kFreq = 440.0f;
    float inc = 2.0f * kPi * kFreq / static_cast<float>(sampleRate_);
    sinePhase_ += inc;
    if (sinePhase_ >= 2.0f * kPi) sinePhase_ -= 2.0f * kPi;
    return std::sin(sinePhase_) * 0.2f;
}

int AudioEngine::AudioCallback(const void* inputBuffer,
                               void* outputBuffer,
                               unsigned long framesPerBuffer,
                               const PaStreamCallbackTimeInfo* timeInfo,
                               PaStreamCallbackFlags statusFlags,
                               void* userData) {
    AudioEngine* engine = static_cast<AudioEngine*>(userData);
    return engine->ProcessAudio(static_cast<float*>(outputBuffer), framesPerBuffer);
}

int AudioEngine::ProcessAudio(float* output, unsigned long framesPerBuffer) {
    if (!renderer_ || !running_) {
        std::memset(output, 0, framesPerBuffer * numChannels_ * sizeof(float));
        return paContinue;
    }

    float dt = framesPerBuffer / static_cast<float>(sampleRate_);
    renderer_->UpdateSmoothing(dt);

    int maxSources = renderer_->GetMaxSources();
    std::memset(output, 0, framesPerBuffer * numChannels_ * sizeof(float));

    for (unsigned long i = 0; i < framesPerBuffer; ++i) {
        float sample = 0.0f;

        if (useSineWave_) {
            sample = GenerateSineWave();
        } else if (!audioBuffer_.empty()) {
            if (audioBufferPos_ < audioBuffer_.size()) {
                sample = audioBuffer_[audioBufferPos_];
                audioBufferPos_++;
            } else {
                audioBufferPos_ = 0;
                sample = audioBuffer_[audioBufferPos_];
                audioBufferPos_++;
            }
        }

        float* frameOut = output + i * numChannels_;
        for (int s = 0; s < maxSources; ++s) {
            const std::vector<float>& gains = renderer_->GetGainsForSource(s);
            if (static_cast<int>(gains.size()) != numChannels_) continue;
            SimdUtils::AccumulateGains(sample, gains.data(), frameOut, numChannels_);
        }
    }

    return paContinue;
}
