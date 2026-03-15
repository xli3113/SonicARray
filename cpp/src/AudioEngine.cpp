#include "AudioEngine.h"
#include "RendererFactory.h"
#include "SimdUtils.h"
#ifdef USE_JACK
#include "JackAudioOutput.h"
#else
#include "PortAudioOutput.h"
#endif
#include <iostream>
#include <fstream>
#include <cmath>
#include <cstring>
#include <cstdint>
#include <chrono>

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
    : oscReceiver_(nullptr),
      oscSender_(nullptr),
      gainFeedbackRunning_(false),
      feedbackHost_("127.0.0.1"),
      feedbackPort_(9000),
      sampleRate_(44100), numChannels_(0), audioBufferPos_(0),
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

    // Build ordered speaker ID list for feedback messages
    speakerIds_.clear();
    for (const auto& spk : speakers_) speakerIds_.push_back(spk.id);

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

    oscSender_ = new OSCSender(feedbackHost_, feedbackPort_);

#ifdef USE_JACK
    output_ = std::make_unique<JackAudioOutput>();
#else
    output_ = std::make_unique<PortAudioOutput>();
#endif

    if (output_ && !output_->Initialize(speakers_, sampleRate_)) {
        std::cerr << "audio output init fail, osc/vbap only\n";
        output_.reset();
    }
    if (output_) {
        std::cout << numChannels_ << "ch " << sampleRate_ << "Hz\n";
    }

    return true;
}

void AudioEngine::Shutdown() {
    Stop();

    if (output_) {
        output_->Stop();
        output_.reset();
    }

    if (oscReceiver_) {
        oscReceiver_->Stop();
        delete oscReceiver_;
        oscReceiver_ = nullptr;
    }

    delete oscSender_;
    oscSender_ = nullptr;

    renderer_.reset();
}

bool AudioEngine::Start() {
    if (output_) {
        auto cb = [this](float** outChannels, unsigned long nframes) {
            ProcessAudioPlanar(outChannels, nframes);
        };
        if (!output_->Start(std::move(cb))) {
            std::cerr << "audio output start fail\n";
            return false;
        }
    }

    if (oscReceiver_) {
        oscReceiver_->Start();
    }

    gainFeedbackRunning_ = true;
    gainFeedbackThread_ = std::thread(&AudioEngine::GainFeedbackThread, this);

    running_ = true;
    return true;
}

void AudioEngine::Stop() {
    running_ = false;

    gainFeedbackRunning_ = false;
    if (gainFeedbackThread_.joinable())
        gainFeedbackThread_.join();

    if (output_) {
        output_->Stop();
    }
}

void AudioEngine::GainFeedbackThread() {
    while (gainFeedbackRunning_) {
        std::this_thread::sleep_for(std::chrono::milliseconds(33)); // ~30 Hz

        if (!renderer_ || !oscSender_) continue;

        int maxSources = renderer_->GetMaxSources();
        for (int s = 0; s < maxSources; ++s) {
            std::vector<float> gains = renderer_->CopyGainsForSource(s);
            if (gains.empty()) continue;

            // Skip inactive sources (except source 0 which is always sent so
            // Unity knows the backend is alive and can clear stale visuals).
            if (s > 0) {
                bool active = false;
                for (float g : gains) { if (g > 1e-5f) { active = true; break; } }
                if (!active) continue;
            }

            oscSender_->SendGains(s, speakerIds_, gains);
        }
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

void AudioEngine::ProcessAudioPlanar(float** outChannels, unsigned long nframes) {
    if (!renderer_ || !running_) {
        for (int ch = 0; ch < numChannels_; ++ch) {
            if (outChannels[ch]) std::memset(outChannels[ch], 0, nframes * sizeof(float));
        }
        return;
    }

    float dt = nframes / static_cast<float>(sampleRate_);
    renderer_->UpdateSmoothing(dt);

    int maxSources = renderer_->GetMaxSources();

    for (unsigned long i = 0; i < nframes; ++i) {
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

        for (int s = 0; s < maxSources; ++s) {
            const std::vector<float>& gains = renderer_->GetGainsForSource(s);
            if (static_cast<int>(gains.size()) != numChannels_) continue;
            for (int ch = 0; ch < numChannels_; ++ch) {
                if (outChannels[ch]) {
                    outChannels[ch][i] += sample * gains[ch];
                }
            }
        }
    }
}
