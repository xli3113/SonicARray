#include "VBAPRenderer.h"
#include <cmath>
#include <algorithm>
#include <limits>
#include <iostream>
#include <vector>

namespace {
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
    const int n = static_cast<int>(speakers_.size());
    if (n < 3) { std::cerr << "not enough spk\n"; return; }

    // Normalise speaker positions onto the unit sphere
    std::vector<std::array<float,3>> d(n);
    for (int i = 0; i < n; ++i) {
        float x = speakers_[i].x, y = speakers_[i].y, z = speakers_[i].z;
        float len = std::sqrt(x*x + y*y + z*z);
        if (len > 0) { x/=len; y/=len; z/=len; }
        d[i] = {x, y, z};
    }

    // Spherical Delaunay = 3D convex hull (brute-force O(n^4), fine for n<=64)
    // A triple (i,j,k) is a hull face iff all other points lie on ONE side
    // of the plane through i,j,k.
    for (int i = 0; i < n-2; ++i) {
        for (int j = i+1; j < n-1; ++j) {
            for (int k = j+1; k < n; ++k) {
                // Plane normal = (j-i) × (k-i)
                float ax = d[j][0]-d[i][0], ay = d[j][1]-d[i][1], az = d[j][2]-d[i][2];
                float bx = d[k][0]-d[i][0], by = d[k][1]-d[i][1], bz = d[k][2]-d[i][2];
                float nx = ay*bz - az*by;
                float ny = az*bx - ax*bz;
                float nz = ax*by - ay*bx;
                float nlen = std::sqrt(nx*nx + ny*ny + nz*nz);
                if (nlen < 1e-6f) continue; // degenerate / collinear

                float offset = nx*d[i][0] + ny*d[i][1] + nz*d[i][2];

                bool allPos = true, allNeg = true;
                for (int l = 0; l < n; ++l) {
                    if (l==i || l==j || l==k) continue;
                    float sd = nx*d[l][0] + ny*d[l][1] + nz*d[l][2] - offset;
                    if (sd >  1e-6f) allNeg = false;
                    if (sd < -1e-6f) allPos = false;
                    if (!allPos && !allNeg) break; // early exit
                }
                if (!allPos && !allNeg) continue; // not a hull face

                // Outward normal check: dot(N, d[i]) > 0 means normal points away from origin
                float outward = nx*d[i][0] + ny*d[i][1] + nz*d[i][2];
                if (outward > 0)
                    triangles_.push_back({i, j, k});
                else
                    triangles_.push_back({i, k, j}); // flip winding
            }
        }
    }

    std::cout << "built " << triangles_.size() << " tris (convex hull / spherical delaunay)\n";
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
        // Zero-length vector = source removed/inactive; leave gains as zero
        rawIdx_[sourceId].store(back, std::memory_order_release);
        return;
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
    float bestScore = -1e9f;
    bool foundFeasible = false;

    for (size_t ti = 0; ti < triangles_.size(); ++ti) {
        const auto& tri = triangles_[ti];
        if (tri.size() < 3) continue;

        // VBAP: p = L*g where L has speaker directions as COLUMNS (L[row][col] = row-th
        // component of col-th speaker). Transposing this (rows=speakers) would solve
        // the wrong system and produce incorrect gains.
        float L[3][3] = {
            {speakers_[tri[0]].x, speakers_[tri[1]].x, speakers_[tri[2]].x},
            {speakers_[tri[0]].y, speakers_[tri[1]].y, speakers_[tri[2]].y},
            {speakers_[tri[0]].z, speakers_[tri[1]].z, speakers_[tri[2]].z}
        };
        // Normalize each column (speaker direction)
        bool ok = true;
        for (int c = 0; c < 3; ++c) {
            if (!Normalize3(L[0][c], L[1][c], L[2][c])) { ok = false; break; }
        }
        if (!ok) continue;

        float g1, g2, g3;
        if (!SolveLg(L, x, y, z, g1, g2, g3)) continue;

        float score = std::min(std::min(g1, g2), g3);
        bool feasible = (g1 >= -kFeasEps && g2 >= -kFeasEps && g3 >= -kFeasEps);

        if (foundFeasible && !feasible) continue; // already have a feasible, skip non-feasible
        if (!foundFeasible && feasible) {
            // first feasible found: reset best
            bestScore = score;
            bestIdx = static_cast<int>(ti);
            foundFeasible = true;
        } else if (score > bestScore) {
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

    // Columns = speaker directions (same fix as FindBestTriangle)
    float L[3][3] = {
        {speakers_[tri[0]].x, speakers_[tri[1]].x, speakers_[tri[2]].x},
        {speakers_[tri[0]].y, speakers_[tri[1]].y, speakers_[tri[2]].y},
        {speakers_[tri[0]].z, speakers_[tri[1]].z, speakers_[tri[2]].z}
    };
    // Normalize each column (speaker direction)
    bool ok = true;
    for (int c = 0; c < 3; ++c) {
        if (!Normalize3(L[0][c], L[1][c], L[2][c])) { ok = false; break; }
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
