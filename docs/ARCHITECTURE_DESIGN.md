# 架构设计：空间音频算法位置与扩展

## 当前架构分析

### ✅ 正确的设计

**C++ 后端**：
- 执行真正的 VBAP 算法
- 计算 28 路精确增益
- 实时音频渲染
- 增益平滑处理

**Unity 前端**：
- 只做简化的距离计算（用于可视化）
- 发送位置到 C++ 后端
- 不执行真正的音频算法

### 🎯 推荐架构

**VBAP 逻辑应该放在 C++ 后端**，原因：

1. **性能**：实时音频处理需要低延迟，C++ 性能更好
2. **精度**：音频渲染需要精确计算，C++ 更适合
3. **集成**：与 PortAudio 在同一进程，减少延迟
4. **专业性**：可以集成专业音频库（如 Ambisonics 库）
5. **分离关注点**：Unity 负责交互和可视化，C++ 负责音频处理

**Unity 端只做可视化估算**：
- 使用简单的距离计算
- 用于视觉反馈（高亮、高度变化）
- 不需要与 C++ 端完全一致

## 可扩展性设计

为了便于替换算法，应该设计一个**可插拔的渲染器接口**。

### 设计模式：策略模式（Strategy Pattern）

```
SpatialRenderer (接口/基类)
    ├── VBAPRenderer (当前实现)
    ├── AmbisonicsRenderer (未来)
    ├── HOARenderer (未来)
    └── WaveFieldRenderer (未来)
```

### 接口设计

所有渲染器需要实现：
- `UpdateSourcePosition(x, y, z)` - 更新声源位置
- `GetGains()` - 获取增益数组
- `UpdateSmoothing(dt)` - 更新平滑

## 实现方案

### ✅ 已实现：抽象基类 + 工厂模式

**优点**：
- ✅ 类型安全（编译时检查）
- ✅ 性能好（无虚函数开销）
- ✅ 可以运行时切换算法
- ✅ 便于测试和调试
- ✅ 易于扩展新算法

**架构**：
```
SpatialRenderer (抽象基类)
    ├── VBAPRenderer (当前实现)
    └── [你的新算法] (只需实现接口)

RendererFactory (工厂类)
    └── Create() - 创建不同类型的渲染器

AudioEngine
    └── 使用 SpatialRenderer* (多态)
```

## 文件结构

- `cpp/src/SpatialRenderer.h` - 抽象基类接口
- `cpp/src/VBAPRenderer.h/cpp` - VBAP 实现（继承 SpatialRenderer）
- `cpp/src/RendererFactory.h/cpp` - 工厂类
- `cpp/src/AudioEngine.h/cpp` - 使用接口，不依赖具体实现

## 如何添加新算法

详细步骤请参考：[HOW_TO_ADD_NEW_ALGORITHM.md](./HOW_TO_ADD_NEW_ALGORITHM.md)

**简单总结**：
1. 创建新类继承 `SpatialRenderer`
2. 实现所有接口方法
3. 在 `RendererFactory` 中注册
4. 更新 `CMakeLists.txt`

**示例**：
```cpp
class AmbisonicsRenderer : public SpatialRenderer {
    // 实现所有接口方法
};
```
