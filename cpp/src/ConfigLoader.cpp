#include "ConfigLoader.h"
#include <fstream>
#include <sstream>
#include <iostream>

bool ConfigLoader::LoadSpeakers(const std::string& yamlPath, std::vector<Speaker>& speakers) {
    speakers.clear();
    
    std::ifstream file(yamlPath);
    if (!file.is_open()) {
        std::cerr << "Error: Cannot open file " << yamlPath << std::endl;
        return false;
    }
    
    std::string line;
    bool inSpeakersSection = false;
    int currentId = -1;
    float x = 0.0f, y = 0.0f, z = 0.0f;
    
    while (std::getline(file, line)) {
        // Trim whitespace
        line.erase(0, line.find_first_not_of(" \t"));
        line.erase(line.find_last_not_of(" \t") + 1);
        
        if (line.empty() || line[0] == '#') continue;
        
        if (line == "speakers:") {
            inSpeakersSection = true;
            continue;
        }
        
        if (inSpeakersSection) {
            if (line.find("- id:") != std::string::npos) {
                // Start of new speaker entry
                if (currentId >= 0) {
                    speakers.emplace_back(currentId, x, y, z);
                }
                // Parse id from this line
                std::istringstream iss(line);
                std::string dash, key;
                char colon;
                iss >> dash >> key >> colon >> currentId;
                x = y = z = 0.0f;
            } else if (line.find("id:") != std::string::npos && line.find("- id:") == std::string::npos) {
                std::istringstream iss(line);
                std::string key;
                char colon;
                iss >> key >> colon >> currentId;
            } else if (line.find("x:") != std::string::npos) {
                std::istringstream iss(line);
                std::string key;
                char colon;
                iss >> key >> colon >> x;
            } else if (line.find("y:") != std::string::npos) {
                std::istringstream iss(line);
                std::string key;
                char colon;
                iss >> key >> colon >> y;
            } else if (line.find("z:") != std::string::npos) {
                std::istringstream iss(line);
                std::string key;
                char colon;
                iss >> key >> colon >> z;
            }
        }
    }
    
    // Add last speaker
    if (currentId >= 0) {
        speakers.emplace_back(currentId, x, y, z);
    }
    
    file.close();
    
    std::cout << "Loaded " << speakers.size() << " speakers from " << yamlPath << std::endl;
    return !speakers.empty();
}
