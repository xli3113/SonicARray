#include "VBAPRenderer.h"
#include <cmath>
#include <algorithm>
#include <limits>
#include <iostream>
#include <unordered_set>
#include <vector>

namespace {
constexpr int kKNN = 10;
constexpr float kAreaEps = 1e-6f;
constexpr float kFeasEps = 1e-6f;

bool Normalize3(float& x, float& y, float& z) {
    float len = std::sqrt(x * x + y * y + z * z);
    if (len <= 1e-6f) return false;
    x /= len; y /= len; z /= len;
    return true;
}

struct Triple {
    int a, b, c;
    bool operator==(const Triple& o) const { return a == o.a && b == o.b && c == o.c; }
};
struct TripleHash {
    size_t operator()(const Triple& t) const {
        return size_t(t.a) * 31337u + size_t(t.b) * 31u + size_t(t.c);
    }
};

float TriangleArea(float p1x, float p1y, float p1z,
                   float p2x, float p2y, float p2z,
                   float p3x, float p3y, float p3z) {
    float v1x = p2x - p1x, v1y = p2y - p1y, v1z = p2z - p1z;
    float v2x = p3x - p1x, v2y = p3y - p1y, v2z = p3z - p1z;
    float cx = v1y * v2z - v1z * v2y;
    float cy = v1z * v2x - v1x * v2z;
    float cz = v1x * v2y - v1y * v2x;
    return 0.5f * std::sqrt(cx * cx + cy * cy + cz * cz);
}

bool SolveLg(float L[3][3], float px, float py, float pz, float& g1, float& g2, float& g3) {
    float det = L[0][0] * (L[1][1] * L[2][2] - L[2][1] * L[1][2]) -
                L[0][1] * (L[1][0] * L[2][2] - L[2][0] * L[1][2]) +
                L[0][2] * (L[1][0] * L[2][1] - L[2][0] * L[1][1]);
    if (std::fabs(det) < 1e-6f) return false;

    float invL[3][3];
    invL[0][0] = (L[1][1] * L[2][2] - L[2][1] * L[1][2]) / det;
    invL[0][1] = -(L[0][1] * L[2][2] - L[2][1] * L[0][2]) / det;
    invL[0][2] = (L[0][1] * L[1][2] - L[1][1] * L[0][2]) / det;
    invL[1][0] = -(L[1][0] * L[2][2] - L[2][0] * L[1][2]) / det;
    invL[1][1] = (L[0][0] * L[2][2] - L[2][0] * L[0][2]) / det;
    invL[1][2] = -(L[0][0] * L[1][2] - L[1][0] * L[0][2]) / det;
    invL[2][0] = (L[1][0] * L[2][1] - L[2][0] * L[1][1]) / det;
    invL[2][1] = -(L[0][0] * L[2][1] - L[2][0] * L[0][1]) / det;
    invL[2][2] = (L[0][0] * L[1][1] - L[1][0] * L[0][1]) / det;

    g1 = invL[0][0] * px + invL[0][1] * py + invL[0][2] * pz;
    g2 = invL[1][0] * px + invL[1][1] * py + invL[1][2] * pz;
    g3 = invL[2][0] * px + invL[2][1] * py + invL[2][2] * pz;
    return true;
}
}

VBAPRenderer::VBAPRenderer()
    : smoothingTime_(0.05f) {
}

bool VBAPRenderer::Initialize(const std::vector<Speaker>& speakers) {
    speakers_ = speakers;

    if (speakers_.size() < 3) {
        std::cerr << "need 3+ spk\n";
        return false;
    }

    for (int b = 0; b < kBuf; ++b) {
        for (int s = 0; s < kMaxSources; ++s) {
            rawGainsBuf_[b][s].assign(speakers_.size(), 0.0f);
        }
    }
    for (int s = 0; s < kMaxSources; ++s) rawIdx_[s].store(0);
    smoothedGains_.resize(kMaxSources);
    for (int s = 0; s < kMaxSources; ++s) {
        smoothedGains_[s].assign(speakers_.size(), 0.0f);
    }

    BuildTriangles();

    std::cout << "vbap " << speakers_.size() << " spk " << kMaxSources << " src\n";
    return true;
}

VBAPRenderer::~VBAPRenderer() {
}

void VBAPRenderer::BuildTriangles() {
    triangles_.clear();
    if (speakers_.size() < 3) {
        std::cerr << "not enough spk\n";
        return;
    }

    const int n = static_cast<int>(speakers_.size());
    std::unordered_set<Triple, TripleHash> seen;

    for (int i = 0; i < n; ++i) {
        std::vector<std::pair<float, int>> dists;
        for (int j = 0; j < n; ++j) {
            if (i == j) continue;
            float dx = speakers_[j].x - speakers_[i].x;
            float dy = speakers_[j].y - speakers_[i].y;
            float dz = speakers_[j].z - speakers_[i].z;
            float d = std::sqrt(dx * dx + dy * dy + dz * dz);
            dists.emplace_back(d, j);
        }
        if (dists.empty()) continue;
        std::partial_sort(dists.begin(), dists.begin() + std::min(kKNN, (int)dists.size()),
                         dists.end(), [](const auto& a, const auto& b) { return a.first < b.first; });

        int kUse = std::min(kKNN, (int)dists.size());
        for (int aj = 0; aj < kUse; ++aj) {
            int j = dists[aj].second;
            for (int ak = aj + 1; ak < kUse; ++ak) {
                int k = dists[ak].second;
                float area = TriangleArea(
                    speakers_[i].x, speakers_[i].y, speakers_[i].z,
                    speakers_[j].x, speakers_[j].y, speakers_[j].z,
                    speakers_[k].x, speakers_[k].y, speakers_[k].z);
                if (area < kAreaEps) continue;

                int a = i, b = j, c = k;
                if (a > b) std::swap(a, b);
                if (b > c) std::swap(b, c);
                if (a > b) std::swap(a, b);
                if (!seen.insert({a, b, c}).second) continue;

                triangles_.push_back({a, b, c});
            }
        }
    }

    std::cout << "built " << triangles_.size() << " tris\n";
}

void VBAPRenderer::UpdateSourcePosition(float x, float y, float z) {
    UpdateSourcePosition(0, x, y, z);
}

void VBAPRenderer::UpdateSourcePosition(int sourceId, float x, float y, float z) {
    if (sourceId < 0 || sourceId >= kMaxSources) return;
    ComputeVBAP(sourceId, x, y, z);
}

const std::vector<float>& VBAPRenderer::GetGainsForSource(int sourceId) const {
    static const std::vector<float> kEmpty;
    if (sourceId < 0 || sourceId >= static_cast<int>(smoothedGains_.size())) return kEmpty;
    return smoothedGains_[sourceId];
}

std::vector<float> VBAPRenderer::CopyGainsForSource(int sourceId) const {
    if (sourceId < 0 || sourceId >= kMaxSources) return {};
    int idx = rawIdx_[sourceId].load(std::memory_order_acquire);
    return rawGainsBuf_[idx][sourceId];
}

void VBAPRenderer::ComputeVBAP(int sourceId, float x, float y, float z) {
    int cur = rawIdx_[sourceId].load(std::memory_order_relaxed);
    int back = 1 - cur;
    auto& gains = rawGainsBuf_[back][sourceId];
    std::fill(gains.begin(), gains.end(), 0.0f);

    float nx = x, ny = y, nz = z;
    if (!Normalize3(nx, ny, nz)) {
        nx = 1.0f; ny = 0.0f; nz = 0.0f;
    }

    int bestTriangle = FindBestTriangle(nx, ny, nz);
    if (bestTriangle >= 0) {
        ComputeTriangleGains(sourceId, bestTriangle, nx, ny, nz, back);
    }

    bool allZero = true;
    for (float g : gains) {
        if (std::fabs(g) > 1e-6f) { allZero = false; break; }
    }
    if (allZero) {
        /* fallback: no feasible triangle, pick nearest speaker */
        int bestIdx = 0;
        float maxDot = -2.0f;
        for (size_t i = 0; i < speakers_.size(); ++i) {
            float sx = speakers_[i].x;
            float sy = speakers_[i].y;
            float sz = speakers_[i].z;
            if (!Normalize3(sx, sy, sz)) continue;
            float d = Dot(nx, ny, nz, sx, sy, sz);
            if (d > maxDot) {
                maxDot = d;
                bestIdx = static_cast<int>(i);
            }
        }
        std::fill(gains.begin(), gains.end(), 0.0f);
        gains[bestIdx] = 1.0f;
        static int s_fallbackCount = 0;
        if (++s_fallbackCount % 500 == 0) {
            std::cerr << "fb spk" << bestIdx << " src" << sourceId << "\n";
        }
    }

    rawIdx_[sourceId].store(back, std::memory_order_release);
}

int VBAPRenderer::FindBestTriangle(float x, float y, float z) {
    int bestIdx = -1;
    float bestScore = -1.0f;

    for (size_t ti = 0; ti < triangles_.size(); ++ti) {
        const auto& tri = triangles_[ti];
        if (tri.size() < 3) continue;

        float L[3][3] = {
            {speakers_[tri[0]].x, speakers_[tri[0]].y, speakers_[tri[0]].z},
            {speakers_[tri[1]].x, speakers_[tri[1]].y, speakers_[tri[1]].z},
            {speakers_[tri[2]].x, speakers_[tri[2]].y, speakers_[tri[2]].z}
        };
        bool ok = true;
        for (int r = 0; r < 3; ++r) {
            if (!Normalize3(L[r][0], L[r][1], L[r][2])) { ok = false; break; }
        }
        if (!ok) continue;

        float g1, g2, g3;
        if (!SolveLg(L, x, y, z, g1, g2, g3)) continue;

        if (g1 < -kFeasEps || g2 < -kFeasEps || g3 < -kFeasEps) continue;

        float score = std::min(std::min(g1, g2), g3);
        if (score > bestScore) {
            bestScore = score;
            bestIdx = static_cast<int>(ti);
        }
    }
    return bestIdx;
}

void VBAPRenderer::ComputeTriangleGains(int sourceId, int triangleIdx, float x, float y, float z, int back) {
    if (sourceId < 0 || sourceId >= kMaxSources) return;
    if (triangleIdx < 0 || triangleIdx >= static_cast<int>(triangles_.size())) return;

    std::vector<float>& gains = rawGainsBuf_[back][sourceId];
    const auto& tri = triangles_[triangleIdx];
    if (tri.size() < 3) return;

    float L[3][3] = {
        {speakers_[tri[0]].x, speakers_[tri[0]].y, speakers_[tri[0]].z},
        {speakers_[tri[1]].x, speakers_[tri[1]].y, speakers_[tri[1]].z},
        {speakers_[tri[2]].x, speakers_[tri[2]].y, speakers_[tri[2]].z}
    };
    bool ok = true;
    for (int r = 0; r < 3; ++r) {
        if (!Normalize3(L[r][0], L[r][1], L[r][2])) { ok = false; break; }
    }
    if (!ok) return;

    float g1, g2, g3;
    if (!SolveLg(L, x, y, z, g1, g2, g3)) return;

    g1 = std::max(0.0f, g1);
    g2 = std::max(0.0f, g2);
    g3 = std::max(0.0f, g3);

    float norm = std::sqrt(g1 * g1 + g2 * g2 + g3 * g3);
    if (norm > 1e-4f) {
        g1 /= norm; g2 /= norm; g3 /= norm;
    }

    gains[tri[0]] = g1;
    gains[tri[1]] = g2;
    gains[tri[2]] = g3;
}

void VBAPRenderer::UpdateSmoothing(float dt) {
    float alpha = dt / (smoothingTime_ + dt);

    for (int s = 0; s < kMaxSources; ++s) {
        int idx = rawIdx_[s].load(std::memory_order_acquire);
        const auto& raw = rawGainsBuf_[idx][s];
        for (size_t i = 0; i < smoothedGains_[s].size(); ++i) {
            smoothedGains_[s][i] = alpha * raw[i] + (1.0f - alpha) * smoothedGains_[s][i];
        }
    }
}

void VBAPRenderer::Normalize(float& x, float& y, float& z) {
    float len = std::sqrt(x * x + y * y + z * z);
    if (len > 0.0001f) {
        x /= len;
        y /= len;
        z /= len;
    }
}

float VBAPRenderer::Dot(float x1, float y1, float z1, float x2, float y2, float z2) {
    return x1 * x2 + y1 * y2 + z1 * z2;
}
