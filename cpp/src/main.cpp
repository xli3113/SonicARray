#include <iostream>
#include <iomanip>
#include <thread>
#include <chrono>
#include "ConfigLoader.h"
#include "AudioEngine.h"
#include "SpatialRenderer.h"

#ifdef _WIN32
#include <windows.h>
#endif

static void PauseBeforeExit() {
#ifdef _WIN32
    std::cout << "\nenter to exit\n";
    std::cin.clear();
    std::cin.get();
#endif
}

void PrintActiveSpeakers(SpatialRenderer* renderer, const std::vector<Speaker>& speakers) {
    if (!renderer) return;
    std::vector<float> gains = renderer->CopyGainsForSource(0);

    std::cout << "[DBG] 作用扬声器: ";
    bool hasActive = false;
    for (size_t i = 0; i < gains.size(); ++i) {
        if (gains[i] > 0.01f) {
            std::cout << "id=" << speakers[i].id << " g=" << std::fixed << std::setprecision(2) << gains[i] << " ";
            hasActive = true;
        }
    }
    if (!hasActive) std::cout << "无";
    std::cout << std::endl;
}

int main(int argc, char* argv[]) {
    std::cout << "=== SoundARray ===" << std::endl;
    
    std::vector<Speaker> speakers;
    std::string yamlPath = "speakers.yaml";
    
    if (argc > 1) {
        yamlPath = argv[1];
    }
    
    if (!ConfigLoader::LoadSpeakers(yamlPath, speakers)) {
        std::cerr << "cant load speakers " << yamlPath << "\n";
        PauseBeforeExit();
        return 1;
    }
    
    if (speakers.empty()) {
        std::cerr << "no speakers in " << yamlPath << "\n";
        PauseBeforeExit();
        return 1;
    }
    
    AudioEngine engine;
    if (!engine.Initialize(speakers)) {
        std::cerr << "engine init fail\n";
        PauseBeforeExit();
        return 1;
    }
    
    if (argc > 2) {
        engine.LoadAudioFile(argv[2]);
    } else {
        engine.EnableSineWave(true);
        std::cout << "sine wave (or pass wav as 2nd arg)\n";
    }
    
    if (!engine.Start()) {
        std::cerr << "start fail\n";
        PauseBeforeExit();
        return 1;
    }
    
    std::cout << "\nrunning, enter to quit, osc /spatial/source_pos\n";
    
    SpatialRenderer* renderer = engine.GetRenderer();
    
    auto startTime = std::chrono::steady_clock::now();
    bool shouldExit = false;
    
    while (!shouldExit && engine.GetRenderer() != nullptr) {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
        
        auto now = std::chrono::steady_clock::now();
        auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - startTime).count();
        
        if (elapsed > 500 && renderer) {
            PrintActiveSpeakers(renderer, speakers);
            startTime = now;
        }
        
        #ifdef _WIN32
        if (GetAsyncKeyState(VK_RETURN) & 0x8000) {
            shouldExit = true;
        }
        #endif
    }
    
    engine.Stop();
    engine.Shutdown();
    
    PauseBeforeExit();
    return 0;
}
