#include "AudioEngine.h"
#include "RendererFactory.h"
#include <iostream>
#include <fstream>
#include <cmath>
#include <cstring>

// Simple WAV file reader (mono, 16-bit PCM)
bool LoadWavFile(const std::string& filepath, std::vector<float>& buffer, int& sampleRate) {
    std::ifstream file(filepath, std::ios::binary);
    if (!file.is_open()) {
        return false;
    }
    
    char header[44];
    file.read(header, 44);
    
    // Check RIFF header
    if (strncmp(header, "RIFF", 4) != 0 || strncmp(header + 8, "WAVE", 4) != 0) {
        return false;
    }
    
    // Read sample rate
    sampleRate = *(int*)(header + 24);
    
    // Read data size
    int dataSize = *(int*)(header + 40);
    
    // Read audio data (assuming 16-bit mono)
    std::vector<int16_t> intData(dataSize / 2);
    file.read((char*)intData.data(), dataSize);
    
    // Convert to float
    buffer.resize(intData.size());
    for (size_t i = 0; i < intData.size(); ++i) {
        buffer[i] = intData[i] / 32768.0f;
    }
    
    return true;
}

AudioEngine::AudioEngine()
    : stream_(nullptr), oscReceiver_(nullptr),
      sampleRate_(44100), numChannels_(28), audioBufferPos_(0),
      usePinkNoise_(true), running_(false) {
    
    // Initialize pink noise state
    for (int i = 0; i < 7; ++i) {
        pinkNoiseState_[i] = 0.0f;
    }
}

AudioEngine::~AudioEngine() {
    Shutdown();
}

bool AudioEngine::Initialize(const std::vector<Speaker>& speakers, int sampleRate) {
    speakers_ = speakers;
    sampleRate_ = sampleRate;
    numChannels_ = static_cast<int>(speakers.size());
    
    // Initialize PortAudio
    PaError err = Pa_Initialize();
    if (err != paNoError) {
        std::cerr << "PortAudio initialization failed: " << Pa_GetErrorText(err) << std::endl;
        return false;
    }
    
    // Create renderer using factory (default: VBAP)
    renderer_ = RendererFactory::Create(RendererFactory::Type::VBAP);
    if (!renderer_->Initialize(speakers_)) {
        std::cerr << "Failed to initialize renderer!" << std::endl;
        return false;
    }
    std::cout << "Using renderer: " << renderer_->GetName() << std::endl;
    
    // Create OSC receiver
    oscReceiver_ = new OSCReceiver(7000);
    oscReceiver_->SetPositionCallback([this](float x, float y, float z) {
        this->SetSourcePosition(x, y, z);
    });
    
    // Open audio stream
    PaStreamParameters outputParams;
    outputParams.device = Pa_GetDefaultOutputDevice();
    outputParams.channelCount = numChannels_;
    outputParams.sampleFormat = paFloat32;
    outputParams.suggestedLatency = Pa_GetDeviceInfo(outputParams.device)->defaultLowOutputLatency;
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
        std::cerr << "Failed to open audio stream: " << Pa_GetErrorText(err) << std::endl;
        return false;
    }
    
    std::cout << "Audio Engine initialized: " << numChannels_ << " channels, " 
              << sampleRate_ << " Hz" << std::endl;
    
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
    
    // renderer_ 是 unique_ptr，会自动释放
    renderer_.reset();
    
    Pa_Terminate();
}

bool AudioEngine::Start() {
    if (!stream_) {
        return false;
    }
    
    PaError err = Pa_StartStream(stream_);
    if (err != paNoError) {
        std::cerr << "Failed to start audio stream: " << Pa_GetErrorText(err) << std::endl;
        return false;
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
    if (renderer_) {
        renderer_->UpdateSourcePosition(x, y, z);
    }
}

void AudioEngine::SetRenderer(std::unique_ptr<SpatialRenderer> renderer) {
    if (renderer && renderer->Initialize(speakers_)) {
        renderer_ = std::move(renderer);
        std::cout << "Renderer switched to: " << renderer_->GetName() << std::endl;
    } else {
        std::cerr << "Failed to set renderer!" << std::endl;
    }
}

bool AudioEngine::LoadAudioFile(const std::string& filepath) {
    int fileSampleRate = 0;
    std::vector<float> buffer;
    
    if (!LoadWavFile(filepath, buffer, fileSampleRate)) {
        std::cerr << "Failed to load audio file: " << filepath << std::endl;
        return false;
    }
    
    // Resample if needed (simplified - you may want to use a proper resampler)
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
    usePinkNoise_ = false;
    
    std::cout << "Loaded audio file: " << filepath << " (" << buffer.size() << " samples)" << std::endl;
    
    return true;
}

float AudioEngine::GeneratePinkNoise() {
    // Simplified pink noise generator
    float white = (static_cast<float>(rand()) / RAND_MAX) * 2.0f - 1.0f;
    
    pinkNoiseState_[0] = 0.99886f * pinkNoiseState_[0] + white * 0.0555179f;
    pinkNoiseState_[1] = 0.99332f * pinkNoiseState_[1] + white * 0.0750759f;
    pinkNoiseState_[2] = 0.96900f * pinkNoiseState_[2] + white * 0.1538520f;
    pinkNoiseState_[3] = 0.86650f * pinkNoiseState_[3] + white * 0.3104856f;
    pinkNoiseState_[4] = 0.55000f * pinkNoiseState_[4] + white * 0.5329522f;
    pinkNoiseState_[5] = -0.7616f * pinkNoiseState_[5] - white * 0.0168980f;
    
    float pink = pinkNoiseState_[0] + pinkNoiseState_[1] + pinkNoiseState_[2] +
                 pinkNoiseState_[3] + pinkNoiseState_[4] + pinkNoiseState_[5] + pinkNoiseState_[6] +
                 white * 0.5362f;
    
    pinkNoiseState_[6] = white * 0.115926f;
    
    return pink * 0.11f; // Scale down
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
    
    // Update smoothing
    float dt = framesPerBuffer / static_cast<float>(sampleRate_);
    renderer_->UpdateSmoothing(dt);
    
    const std::vector<float>& gains = renderer_->GetGains();
    
    // Generate or read audio source
    for (unsigned long i = 0; i < framesPerBuffer; ++i) {
        float sample = 0.0f;
        
        if (usePinkNoise_) {
            sample = GeneratePinkNoise();
        } else if (!audioBuffer_.empty()) {
            if (audioBufferPos_ < audioBuffer_.size()) {
                sample = audioBuffer_[audioBufferPos_];
                audioBufferPos_++;
            } else {
                // Loop
                audioBufferPos_ = 0;
                sample = audioBuffer_[audioBufferPos_];
                audioBufferPos_++;
            }
        }
        
        // Apply gains to all channels
        for (int ch = 0; ch < numChannels_; ++ch) {
            output[i * numChannels_ + ch] = sample * gains[ch];
        }
    }
    
    return paContinue;
}
