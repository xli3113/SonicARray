#pragma once

#include <vector>
#include "Speaker.h"

/// <summary>
/// 空间音频渲染器抽象基类
/// 所有空间音频算法（VBAP、Ambisonics、HOA等）都应实现此接口
/// </summary>
class SpatialRenderer {
public:
    virtual ~SpatialRenderer() = default;
    
    /// <summary>
    /// 初始化渲染器
    /// </summary>
    /// <param name="speakers">扬声器配置</param>
    /// <returns>是否成功</returns>
    virtual bool Initialize(const std::vector<Speaker>& speakers) = 0;
    
    /// <summary>
    /// 更新声源位置并计算增益
    /// </summary>
    /// <param name="x">声源 X 坐标</param>
    /// <param name="y">声源 Y 坐标</param>
    /// <param name="z">声源 Z 坐标</param>
    virtual void UpdateSourcePosition(float x, float y, float z) = 0;
    
    /// <summary>
    /// 获取当前增益（平滑后）
    /// </summary>
    /// <returns>增益数组，大小为扬声器数量</returns>
    virtual const std::vector<float>& GetGains() const = 0;
    
    /// <summary>
    /// 获取原始计算的增益（平滑前）
    /// </summary>
    virtual const std::vector<float>& GetRawGains() const = 0;
    
    /// <summary>
    /// 更新平滑处理（在音频回调中定期调用）
    /// </summary>
    /// <param name="dt">时间步长（秒）</param>
    virtual void UpdateSmoothing(float dt) = 0;
    
    /// <summary>
    /// 设置平滑时间常数
    /// </summary>
    /// <param name="time">时间常数（秒）</param>
    virtual void SetSmoothingTime(float time) = 0;
    
    /// <summary>
    /// 获取渲染器名称（用于调试和日志）
    /// </summary>
    virtual const char* GetName() const = 0;
    
    /// <summary>
    /// 获取扬声器数量
    /// </summary>
    virtual int GetNumSpeakers() const = 0;
};
