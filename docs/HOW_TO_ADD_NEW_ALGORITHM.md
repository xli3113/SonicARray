# 如何添加新的空间音频算法

## 概述

项目使用**可插拔的渲染器架构**，所有空间音频算法都实现 `SpatialRenderer` 接口。这使得添加新算法变得非常简单。

## 架构说明

```
SpatialRenderer (抽象基类)
    ├── VBAPRenderer (当前实现)
    ├── AmbisonicsRenderer (示例)
    ├── HOARenderer (示例)
    └── YourNewRenderer (你的新算法)
```

## 添加新算法的步骤

### 步骤 1：创建新的渲染器类

创建 `cpp/src/YourNewRenderer.h`:

```cpp
#pragma once

#include "SpatialRenderer.h"
#include <vector>
#include "Speaker.h"

class YourNewRenderer : public SpatialRenderer {
public:
    YourNewRenderer();
    ~YourNewRenderer() override;
    
    // 实现所有必需的接口方法
    bool Initialize(const std::vector<Speaker>& speakers) override;
    void UpdateSourcePosition(float x, float y, float z) override;
    const std::vector<float>& GetGains() const override;
    const std::vector<float>& GetRawGains() const override;
    void UpdateSmoothing(float dt) override;
    void SetSmoothingTime(float time) override;
    const char* GetName() const override;
    int GetNumSpeakers() const override;

private:
    // 你的算法特定方法
    void ComputeYourAlgorithm(float x, float y, float z);
    
    // 成员变量
    std::vector<Speaker> speakers_;
    std::vector<float> rawGains_;
    std::vector<float> smoothedGains_;
    float smoothingTime_;
    // ... 其他算法特定的变量
};
```

### 步骤 2：实现渲染器类

创建 `cpp/src/YourNewRenderer.cpp`:

```cpp
#include "YourNewRenderer.h"
#include <cmath>
#include <iostream>

YourNewRenderer::YourNewRenderer()
    : smoothingTime_(0.05f) {
}

YourNewRenderer::~YourNewRenderer() {
}

bool YourNewRenderer::Initialize(const std::vector<Speaker>& speakers) {
    speakers_ = speakers;
    
    if (speakers_.empty()) {
        std::cerr << "YourNewRenderer: Need at least 1 speaker" << std::endl;
        return false;
    }
    
    rawGains_.resize(speakers_.size(), 0.0f);
    smoothedGains_.resize(speakers_.size(), 0.0f);
    
    // 你的初始化逻辑
    // ...
    
    std::cout << "YourNewRenderer initialized with " << speakers_.size() << " speakers" << std::endl;
    return true;
}

void YourNewRenderer::UpdateSourcePosition(float x, float y, float z) {
    ComputeYourAlgorithm(x, y, z);
}

void YourNewRenderer::ComputeYourAlgorithm(float x, float y, float z) {
    // 重置增益
    std::fill(rawGains_.begin(), rawGains_.end(), 0.0f);
    
    // 实现你的算法逻辑
    // 例如：基于距离的简单增益计算
    float sourceLen = std::sqrt(x*x + y*y + z*z);
    if (sourceLen < 0.0001f) return;
    
    for (size_t i = 0; i < speakers_.size(); ++i) {
        // 计算到扬声器的距离
        float dx = speakers_[i].x - x;
        float dy = speakers_[i].y - y;
        float dz = speakers_[i].z - z;
        float dist = std::sqrt(dx*dx + dy*dy + dz*dz);
        
        // 简单的距离衰减（你可以实现更复杂的算法）
        rawGains_[i] = 1.0f / (1.0f + dist * dist);
    }
    
    // 归一化（可选，取决于算法）
    float sum = 0.0f;
    for (float g : rawGains_) {
        sum += g * g;
    }
    if (sum > 0.0001f) {
        float norm = std::sqrt(sum);
        for (float& g : rawGains_) {
            g /= norm;
        }
    }
}

const std::vector<float>& YourNewRenderer::GetGains() const {
    return smoothedGains_;
}

const std::vector<float>& YourNewRenderer::GetRawGains() const {
    return rawGains_;
}

void YourNewRenderer::UpdateSmoothing(float dt) {
    float alpha = dt / (smoothingTime_ + dt);
    
    for (size_t i = 0; i < smoothedGains_.size(); ++i) {
        smoothedGains_[i] = alpha * rawGains_[i] + (1.0f - alpha) * smoothedGains_[i];
    }
}

void YourNewRenderer::SetSmoothingTime(float time) {
    smoothingTime_ = time;
}

const char* YourNewRenderer::GetName() const {
    return "YourNewAlgorithm";
}

int YourNewRenderer::GetNumSpeakers() const {
    return static_cast<int>(speakers_.size());
}
```

### 步骤 3：在工厂中注册

修改 `cpp/src/RendererFactory.h`:

```cpp
enum class Type {
    VBAP,
    YOUR_NEW_ALGORITHM,  // 添加新类型
};
```

修改 `cpp/src/RendererFactory.cpp`:

```cpp
#include "YourNewRenderer.h"  // 添加头文件

std::unique_ptr<SpatialRenderer> RendererFactory::Create(Type type) {
    switch (type) {
        case Type::VBAP:
            return std::make_unique<VBAPRenderer>();
        
        case Type::YOUR_NEW_ALGORITHM:  // 添加 case
            return std::make_unique<YourNewRenderer>();
        
        default:
            return std::make_unique<VBAPRenderer>();
    }
}

std::unique_ptr<SpatialRenderer> RendererFactory::Create(const std::string& typeName) {
    if (typeName == "VBAP" || typeName == "vbap") {
        return Create(Type::VBAP);
    }
    else if (typeName == "YourNewAlgorithm" || typeName == "yournewalgorithm") {  // 添加
        return Create(Type::YOUR_NEW_ALGORITHM);
    }
    
    return Create(Type::VBAP);
}

std::vector<std::string> RendererFactory::GetAvailableTypes() {
    return {
        "VBAP",
        "YourNewAlgorithm",  // 添加
    };
}
```

### 步骤 4：更新 CMakeLists.txt

在 `cpp/CMakeLists.txt` 中添加新文件：

```cmake
set(SOURCES
    src/main.cpp
    src/ConfigLoader.cpp
    src/VBAPRenderer.cpp
    src/YourNewRenderer.cpp  # 添加
    src/OSCReceiver.cpp
    src/AudioEngine.cpp
    src/RendererFactory.cpp
)
```

### 步骤 5：使用新算法

#### 方法 1：在代码中切换

```cpp
// 在 main.cpp 或 AudioEngine::Initialize 中
auto renderer = RendererFactory::Create(RendererFactory::Type::YOUR_NEW_ALGORITHM);
engine.SetRenderer(std::move(renderer));
```

#### 方法 2：通过配置文件（未来扩展）

可以添加配置文件支持：

```yaml
# config.yaml
renderer:
  type: "YourNewAlgorithm"  # 或 "VBAP"
  smoothing_time: 0.05
```

## 示例：Ambisonics 渲染器框架

如果你想实现 Ambisonics，可以参考这个框架：

```cpp
class AmbisonicsRenderer : public SpatialRenderer {
private:
    int order_;  // Ambisonics 阶数
    std::vector<std::vector<float>> decoderMatrix_;  // 解码矩阵
    
    void ComputeAmbisonics(float x, float y, float z);
    void BuildDecoderMatrix();
};
```

## Unity 端需要修改吗？

**不需要！** Unity 端只做可视化，使用简单的距离计算即可。真正的算法在 C++ 端执行。

如果你想让 Unity 的可视化更准确，可以：
1. 通过 OSC 从 C++ 端接收增益值
2. 在 Unity 中根据增益值更新可视化

但这通常不是必需的，因为可视化只是用于反馈，不需要与音频完全一致。

## 测试新算法

1. **编译项目**
   ```bash
   cd cpp/build
   cmake .. && make
   ```

2. **运行测试**
   ```bash
   ./SoundARray speakers.yaml
   ```

3. **验证**
   - 检查控制台输出：应该显示 "Using renderer: YourNewAlgorithm"
   - 发送 OSC 消息测试
   - 检查增益计算是否正确

## 注意事项

1. **性能**：确保算法在音频回调中足够快（< 1ms）
2. **线程安全**：如果使用多线程，注意同步
3. **增益范围**：确保增益值在合理范围内（0-1 或更大，取决于算法）
4. **平滑**：始终使用平滑处理，避免音频突变

## 常见算法参考

- **VBAP**: 当前实现，基于三角形
- **Ambisonics**: 球谐函数展开
- **HOA (Higher Order Ambisonics)**: 高阶 Ambisonics
- **Wave Field Synthesis**: 波场合成
- **Distance-based**: 简单距离衰减（示例）

## 总结

添加新算法只需要：
1. ✅ 创建新类继承 `SpatialRenderer`
2. ✅ 实现所有接口方法
3. ✅ 在工厂中注册
4. ✅ 更新 CMakeLists.txt

就这么简单！🎉
