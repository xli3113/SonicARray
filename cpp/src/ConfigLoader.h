#pragma once

#include <vector>
#include <string>
#include "Speaker.h"

class ConfigLoader {
public:
    static bool LoadSpeakers(const std::string& yamlPath, std::vector<Speaker>& speakers);
};
