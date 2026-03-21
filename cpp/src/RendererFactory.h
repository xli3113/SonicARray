#pragma once

#include "SpatialRenderer.h"
#include <string>
#include <memory>

class RendererFactory {
public:
    enum class Type { VBAP };
    
    static std::unique_ptr<SpatialRenderer> Create(Type type);
    static std::unique_ptr<SpatialRenderer> Create(const std::string& typeName);
    static std::vector<std::string> GetAvailableTypes();
};
