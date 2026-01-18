#pragma once

#include "SpatialRenderer.h"
#include <vector>
#include "Speaker.h"

/// <summary>
/// VBAP (Vector Base Amplitude Panning) 渲染器实现
/// 这是当前使用的空间音频算法
/// </summary>
class VBAPRenderer : public SpatialRenderer {
public:
    VBAPRenderer();
    ~VBAPRenderer() override;
    
    // SpatialRenderer 接口实现
    bool Initialize(const std::vector<Speaker>& speakers) override;
    void UpdateSourcePosition(float x, float y, float z) override;
    const std::vector<float>& GetGains() const override { return smoothedGains_; }
    const std::vector<float>& GetRawGains() const override { return rawGains_; }
    void UpdateSmoothing(float dt) override;
    void SetSmoothingTime(float time) override { smoothingTime_ = time; }
    const char* GetName() const override { return "VBAP"; }
    int GetNumSpeakers() const override { return static_cast<int>(speakers_.size()); }

private:
    // Core VBAP computation (can be replaced with other algorithms)
    void ComputeVBAP(float x, float y, float z);
    
    // Find the best triangle for VBAP
    int FindBestTriangle(float x, float y, float z);
    
    // Compute gains for a triangle
    void ComputeTriangleGains(int triangleIdx, float x, float y, float z);
    
    // Helper: normalize vector
    void Normalize(float& x, float& y, float& z);
    
    // Helper: dot product
    float Dot(float x1, float y1, float z1, float x2, float y2, float z2);
    
    std::vector<Speaker> speakers_;
    std::vector<float> rawGains_;
    std::vector<float> smoothedGains_;
    float smoothingTime_;
    
    // Pre-computed triangles (for 2D/3D layouts)
    std::vector<std::vector<int>> triangles_;
    
    void BuildTriangles();
};
