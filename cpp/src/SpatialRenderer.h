#pragma once

#include <vector>
#include "Speaker.h"

class SpatialRenderer {
public:
    virtual ~SpatialRenderer() = default;
    
    virtual bool Initialize(const std::vector<Speaker>& speakers) = 0;
    virtual void UpdateSourcePosition(float x, float y, float z) = 0;
    virtual void UpdateSourcePosition(int sourceId, float x, float y, float z) = 0;
    virtual const std::vector<float>& GetGainsForSource(int sourceId) const = 0;
    virtual std::vector<float> CopyGainsForSource(int sourceId) const = 0;
    virtual int GetMaxSources() const = 0;
    virtual const std::vector<float>& GetGains() const = 0;
    virtual const std::vector<float>& GetRawGains() const = 0;
    virtual void UpdateSmoothing(float dt) = 0;
    virtual void SetSmoothingTime(float time) = 0;
    virtual const char* GetName() const = 0;
    virtual int GetNumSpeakers() const = 0;
};
