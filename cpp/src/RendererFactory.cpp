#include "RendererFactory.h"
#include "VBAPRenderer.h"
// 未来添加其他渲染器：
// #include "AmbisonicsRenderer.h"
// #include "HOARenderer.h"

std::unique_ptr<SpatialRenderer> RendererFactory::Create(Type type) {
    switch (type) {
        case Type::VBAP:
            return std::make_unique<VBAPRenderer>();
        
        // 未来添加：
        // case Type::AMBISONICS:
        //     return std::make_unique<AmbisonicsRenderer>();
        // case Type::HOA:
        //     return std::make_unique<HOARenderer>();
        
        default:
            std::cerr << "Unknown renderer type, using VBAP as default" << std::endl;
            return std::make_unique<VBAPRenderer>();
    }
}

std::unique_ptr<SpatialRenderer> RendererFactory::Create(const std::string& typeName) {
    if (typeName == "VBAP" || typeName == "vbap") {
        return Create(Type::VBAP);
    }
    // 未来添加：
    // else if (typeName == "Ambisonics" || typeName == "ambisonics") {
    //     return Create(Type::AMBISONICS);
    // }
    
    std::cerr << "Unknown renderer type: " << typeName << ", using VBAP as default" << std::endl;
    return Create(Type::VBAP);
}

std::vector<std::string> RendererFactory::GetAvailableTypes() {
    return {
        "VBAP",
        // 未来添加：
        // "Ambisonics",
        // "HOA",
        // "WaveField"
    };
}
