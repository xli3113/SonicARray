#include "RendererFactory.h"
#include "VBAPRenderer.h"
#include <iostream>

std::unique_ptr<SpatialRenderer> RendererFactory::Create(Type type) {
    switch (type) {
        case Type::VBAP:
            return std::make_unique<VBAPRenderer>();
        default:
            std::cerr << "unknown renderer, vbap default\n";
            return std::make_unique<VBAPRenderer>();
    }
}

std::unique_ptr<SpatialRenderer> RendererFactory::Create(const std::string& typeName) {
    if (typeName == "VBAP" || typeName == "vbap") {
        return Create(Type::VBAP);
    }
    std::cerr << "unknown " << typeName << " vbap default\n";
    return Create(Type::VBAP);
}

std::vector<std::string> RendererFactory::GetAvailableTypes() {
    return {"VBAP"};
}
