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

void PrintActiveSpeakers(SpatialRenderer* renderer, const std::vector<Speaker>& speakers) {
    if (!renderer) return;
    const std::vector<float>& gains = renderer->GetGains();
    
    std::cout << "\rActive Speakers: ";
    bool hasActive = false;
    
    for (size_t i = 0; i < gains.size(); ++i) {
        if (gains[i] > 0.01f) {
            std::cout << "[" << speakers[i].id << ": " 
                      << std::fixed << std::setprecision(2) << gains[i] << "] ";
            hasActive = true;
        }
    }
    
    if (!hasActive) {
        std::cout << "None";
    }
    
    std::cout << std::flush;
}

int main(int argc, char* argv[]) {
    std::cout << "=== SoundARray Audio Engine ===" << std::endl;
    
    // Load speaker configuration
    std::vector<Speaker> speakers;
    std::string yamlPath = "speakers.yaml";
    
    if (argc > 1) {
        yamlPath = argv[1];
    }
    
    if (!ConfigLoader::LoadSpeakers(yamlPath, speakers)) {
        std::cerr << "Failed to load speaker configuration!" << std::endl;
        return 1;
    }
    
    if (speakers.size() != 28) {
        std::cerr << "Warning: Expected 28 speakers, got " << speakers.size() << std::endl;
    }
    
    // Initialize audio engine
    AudioEngine engine;
    if (!engine.Initialize(speakers)) {
        std::cerr << "Failed to initialize audio engine!" << std::endl;
        return 1;
    }
    
    // Try to load audio file if provided
    if (argc > 2) {
        engine.LoadAudioFile(argv[2]);
    } else {
        engine.EnablePinkNoise(true);
        std::cout << "Using pink noise (provide audio file as second argument)" << std::endl;
    }
    
    // Start engine
    if (!engine.Start()) {
        std::cerr << "Failed to start audio engine!" << std::endl;
        return 1;
    }
    
    std::cout << "\nAudio engine running. Press Enter to exit..." << std::endl;
    std::cout << "Waiting for OSC messages on /spatial/source_pos..." << std::endl;
    
    // Get renderer for debug output
    SpatialRenderer* renderer = engine.GetRenderer();
    
    // Debug loop
    auto startTime = std::chrono::steady_clock::now();
    bool shouldExit = false;
    
    while (!shouldExit && engine.GetRenderer() != nullptr) {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
        
        // Print status every 500ms
        auto now = std::chrono::steady_clock::now();
        auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - startTime).count();
        
        if (elapsed > 500 && renderer) {
            PrintActiveSpeakers(renderer, speakers);
            startTime = now;
        }
        
        // Check for Enter key or Ctrl+C
        #ifdef _WIN32
        if (GetAsyncKeyState(VK_RETURN) & 0x8000) {
            shouldExit = true;
        }
        #else
        // On Linux, use standard input (non-blocking check)
        // User can press Ctrl+C to exit, or we can add proper input handling
        // For now, just let it run until interrupted
        #endif
    }
    
    engine.Stop();
    engine.Shutdown();
    
    return 0;
}
