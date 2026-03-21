#pragma once

#include "SpatialRenderer.h"
#include <vector>
#include <array>
#include <atomic>
#include "Speaker.h"

class VBAPRenderer : public SpatialRenderer {
public:
    VBAPRenderer();
    ~VBAPRenderer() override;
    
    bool Initialize(const std::vector<Speaker>& speakers) override;
    void UpdateSourcePosition(float x, float y, float z) override;
    void UpdateSourcePosition(int sourceId, float x, float y, float z) override;
    const std::vector<float>& GetGains() const override { return smoothedGains_[0]; }
    const std::vector<float>& GetRawGains() const override {
        int idx = rawIdx_[0].load(std::memory_order_acquire);
        return rawGainsBuf_[idx][0];
    }
    const std::vector<float>& GetGainsForSource(int sourceId) const override;
    std::vector<float> CopyGainsForSource(int sourceId) const override;
    int GetMaxSources() const override { return kMaxSources; }
    void UpdateSmoothing(float dt) override;
    void SetSmoothingTime(float time) override { smoothingTime_ = time; }
    const char* GetName() const override { return "VBAP"; }
    int GetNumSpeakers() const override { return static_cast<int>(speakers_.size()); }

private:
    static constexpr int kMaxSources = 8;
    static constexpr int kBuf = 2;

    void ComputeVBAP(int sourceId, float x, float y, float z);
    int FindBestTriangle(float x, float y, float z);
    void ComputeTriangleGains(int sourceId, int triangleIdx, float x, float y, float z, int back);
    void Normalize(float& x, float& y, float& z);
    float Dot(float x1, float y1, float z1, float x2, float y2, float z2);
    
    std::vector<Speaker> speakers_;
    std::array<std::array<std::vector<float>, kMaxSources>, kBuf> rawGainsBuf_;
    std::array<std::atomic<int>, kMaxSources> rawIdx_;
    std::vector<std::vector<float>> smoothedGains_;
    float smoothingTime_;
    std::vector<std::vector<int>> triangles_;
    
    void BuildTriangles();
};
