# Unity AR 前端

## 设置步骤

1. **创建 Unity 项目**
   - Unity 版本: 2021.3 LTS 或更高
   - 平台: Android (Meta Quest)

2. **导入 Meta XR SDK**
   - 从 Asset Store 或 Meta 官网下载 Oculus Integration
   - 导入到项目中

3. **复制脚本**
   - 将 `Assets/Scripts/` 中的所有 `.cs` 文件复制到 Unity 项目的 `Assets/Scripts/` 目录

4. **配置 StreamingAssets**
   - 创建 `Assets/StreamingAssets/` 文件夹（如果不存在）
   - 将 `speakers.yaml` 复制到此文件夹

5. **创建场景**
   - 创建新场景或使用现有场景
   - 添加空 GameObject，命名为 "SpeakerManager"
     - 添加 `SpeakerManager` 组件
     - 配置 YAML 文件路径（默认: "speakers.yaml"）
   - 添加空 GameObject，命名为 "SpatialSource"
     - 添加 `SpatialSource` 组件
     - 配置 OSC IP（默认: 127.0.0.1）和端口（默认: 7000）

6. **配置 Meta XR**
   - 按照 Meta XR SDK 文档配置 XR 设置
   - 确保启用了手部追踪或控制器支持

## 抓取交互设置

在 `SpatialSource.cs` 中，根据你的 Meta XR SDK 版本调整抓取代码：

### Meta XR SDK v50+
```csharp
using Meta.XR.InteractionSystem;

var grabInteractable = gameObject.AddComponent<GrabInteractable>();
```

### Meta XR SDK v40 或更早
```csharp
using Oculus.Interaction;

var grabInteractable = gameObject.AddComponent<GrabInteractable>();
```

## 构建到 Quest

1. 在 Build Settings 中选择 Android 平台
2. 配置 Player Settings：
   - Minimum API Level: Android 7.0 (API 24)
   - Target API Level: 最新
   - XR Settings: 启用 Oculus
3. 连接 Quest 设备
4. 点击 Build and Run

## 调试

- 查看 Unity Console 中的 OSC 发送日志
- 检查 C++ 后端是否接收到 OSC 消息
- 使用 Unity Profiler 检查性能
