#pragma once

#include "SpatialRenderer.h"
#include <string>
#include <memory>

/// <summary>
/// 渲染器工厂类
/// 用于创建不同类型的空间音频渲染器
/// </summary>
class RendererFactory {
public:
    enum class Type {
        VBAP,
        // 未来可以添加：
        // AMBISONICS,
        // HOA,
        // WAVEFIELD
    };
    
    /// <summary>
    /// 从类型创建渲染器
    /// </summary>
    static std::unique_ptr<SpatialRenderer> Create(Type type);
    
    /// <summary>
    /// 从字符串创建渲染器（用于配置文件）
    /// </summary>
    static std::unique_ptr<SpatialRenderer> Create(const std::string& typeName);
    
    /// <summary>
    /// 获取所有可用的渲染器类型名称
    /// </summary>
    static std::vector<std::string> GetAvailableTypes();
};
