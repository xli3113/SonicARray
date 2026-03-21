#include "ConfigLoader.h"
#include <fstream>
#include <sstream>
#include <iostream>
#include <cstdlib>

static void trimLine(std::string& line) {
    auto l = line.find_first_not_of(" \t");
    if (l == std::string::npos) {
        line.clear();
        return;
    }
    line.erase(0, l);
    auto r = line.find_last_not_of(" \t");
    if (r != std::string::npos)
        line.erase(r + 1);
}

static float parseFloatAfterColon(const std::string& line) {
    auto pos = line.find(':');
    if (pos == std::string::npos) return 0.0f;
    return static_cast<float>(std::strtod(line.c_str() + pos + 1, nullptr));
}

static int parseIntAfterColon(const std::string& line) {
    auto pos = line.find(':');
    if (pos == std::string::npos) return -1;
    return static_cast<int>(std::strtol(line.c_str() + pos + 1, nullptr, 10));
}

bool ConfigLoader::LoadSpeakers(const std::string& yamlPath, std::vector<Speaker>& speakers) {
    speakers.clear();

    std::ifstream file(yamlPath);
    if (!file.is_open()) {
        std::cerr << "cant open " << yamlPath << "\n";
        return false;
    }

    std::string line;
    bool inSpeakersSection = false;
    int currentId = -1;
    float x = 0.0f, y = 0.0f, z = 0.0f;

    while (std::getline(file, line)) {
        trimLine(line);
        if (line.empty() || line[0] == '#') continue;

        if (line == "speakers:") {
            inSpeakersSection = true;
            continue;
        }

        if (inSpeakersSection) {
            if (line.find("- id:") != std::string::npos) {
                if (currentId >= 0) speakers.emplace_back(currentId, x, y, z);
                currentId = parseIntAfterColon(line);
                x = y = z = 0.0f;
            } else if (line.find("id:") != std::string::npos && line.find("- id:") == std::string::npos) {
                currentId = parseIntAfterColon(line);
            } else if (line.find("x:") != std::string::npos) {
                x = parseFloatAfterColon(line);
            } else if (line.find("y:") != std::string::npos) {
                y = parseFloatAfterColon(line);
            } else if (line.find("z:") != std::string::npos) {
                z = parseFloatAfterColon(line);
            }
        }
    }
    
    if (currentId >= 0) {
        speakers.emplace_back(currentId, x, y, z);
    }
    
    file.close();
    
    std::cout << "loaded " << speakers.size() << " spk from " << yamlPath << "\n";
    return !speakers.empty();
}
