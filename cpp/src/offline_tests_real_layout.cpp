#include "VBAPRenderer.h"
#include "Speaker.h"

#include <algorithm>
#include <cstdlib>
#include <cmath>
#include <fstream>
#include <iostream>
#include <random>
#include <string>
#include <vector>

static bool LoadSpeakersYaml(const std::string& path, std::vector<Speaker>& out) {
    std::ifstream in(path);
    if (!in.is_open()) return false;

    int curId = -1;
    float x = 0, y = 0, z = 0;
    bool hasX=false, hasY=false, hasZ=false;

    auto trim = [](std::string& s) {
        size_t l = s.find_first_not_of(" \t\r\n");
        if (l == std::string::npos) { s.clear(); return; }
        size_t r = s.find_last_not_of(" \t\r\n");
        s = s.substr(l, r - l + 1);
    };

    auto parseKV = [&](const std::string& line, const std::string& key, float& val, bool& has) {
        auto pos = line.find(key);
        if (pos == std::string::npos) return false;
        auto colon = line.find(':', pos);
        if (colon == std::string::npos) return false;
        std::string rhs = line.substr(colon + 1);
        trim(rhs);
        try { val = std::stof(rhs); has = true; return true; } catch (...) { return false; }
    };

    auto parseId = [&](const std::string& line, int& id) {
        auto pos = line.find("id");
        if (pos == std::string::npos) return false;
        auto colon = line.find(':', pos);
        if (colon == std::string::npos) return false;
        std::string rhs = line.substr(colon + 1);
        trim(rhs);
        try { id = std::stoi(rhs); return true; } catch (...) { return false; }
    };

    std::string line;
    while (std::getline(in, line)) {
        trim(line);
        if (line.empty() || line[0] == '#') continue;

        if (line.rfind("- id:", 0) == 0 || line.rfind("-id:", 0) == 0) {
            if (curId >= 0 && hasX && hasY && hasZ) {
                out.emplace_back(curId, x, y, z);
            }
            curId = -1;
            x=y=z=0; hasX=hasY=hasZ=false;

            int idTmp = -1;
            auto colon = line.find(':');
            if (colon != std::string::npos) {
                std::string rhs = line.substr(colon + 1);
                trim(rhs);
                try { idTmp = std::stoi(rhs); } catch (...) {}
            }
            curId = idTmp;
            continue;
        }

        if (line.rfind("id:", 0) == 0) {
            int idTmp = -1;
            if (parseId(line, idTmp)) curId = idTmp;
            continue;
        }

        if (line.rfind("x:", 0) == 0) { parseKV(line, "x", x, hasX); continue; }
        if (line.rfind("y:", 0) == 0) { parseKV(line, "y", y, hasY); continue; }
        if (line.rfind("z:", 0) == 0) { parseKV(line, "z", z, hasZ); continue; }
    }

    if (curId >= 0 && hasX && hasY && hasZ) {
        out.emplace_back(curId, x, y, z);
    }

    std::sort(out.begin(), out.end(), [](const Speaker& a, const Speaker& b) { return a.id < b.id; });
    return !out.empty();
}

int main(int argc, char** argv) {
    std::string yamlPath = "speakers.yaml";
    int trials = 2000;
    if (argc >= 2) yamlPath = argv[1];
    if (argc >= 3) trials = std::max(1, std::atoi(argv[2]));

    std::vector<Speaker> speakers;
    if (!LoadSpeakersYaml(yamlPath, speakers)) {
        std::cerr << "cant load " << yamlPath << "\n";
        return 1;
    }

    std::cout << "Loaded speakers: " << speakers.size() << "\n";

    VBAPRenderer r;
    if (!r.Initialize(speakers)) {
        std::cerr << "fail init\n";
        return 1;
    }

    std::mt19937 rng(123);
    std::uniform_real_distribution<float> u(-1.0f, 1.0f);

    int zeroCount = 0, negCount = 0, fallbackLike = 0;
    const int numSpeakers = static_cast<int>(speakers.size());

    for (int k = 0; k < trials; ++k) {
        float x = u(rng), y = u(rng), z = u(rng);
        float len = std::sqrt(x*x + y*y + z*z);
        if (len < 1e-4f) { --k; continue; }
        x /= len; y /= len; z /= len;

        r.UpdateSourcePosition(0, x, y, z);
        const auto& raw = r.GetRawGains();

        if (static_cast<int>(raw.size()) != numSpeakers) { --k; continue; }
        bool hasNonFinite = false;
        for (float g : raw) if (!std::isfinite(g)) { hasNonFinite = true; break; }
        if (hasNonFinite) { --k; continue; }

        bool allZero = true;
        bool hasNeg = false;
        for (size_t i = 0; i < raw.size(); ++i) {
            float g = raw[i];
            if (std::fabs(g) > 1e-6f) allZero = false;
            if (g < -1e-6f) hasNeg = true;
        }

        if (allZero) zeroCount++;
        if (hasNeg) negCount++;

        int nearOne = 0, nearZero = 0;
        for (size_t i = 0; i < raw.size(); ++i) {
            float g = raw[i];
            if (std::fabs(g - 1.0f) < 1e-3f) nearOne++;
            if (std::fabs(g) < 1e-3f) nearZero++;
        }
        if (nearOne == 1 && nearZero >= numSpeakers - 1) fallbackLike++;
    }

    std::cout << "Trials = " << trials << "\n";
    std::cout << "zeroCount = " << zeroCount << "\n";
    std::cout << "fallbackLike = " << fallbackLike << " (" << (100.0 * fallbackLike / trials) << "%)\n";
    std::cout << "negCount = " << negCount << "\n";

    return 0;
}
