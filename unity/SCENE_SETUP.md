# Unity 场景设置指南

## 场景结构

```
Scene Hierarchy:
├── XR Origin (Meta XR SDK)
│   ├── Camera Offset
│   │   └── Main Camera
│   └── Left/Right Controllers
├── SpeakerManager (Empty GameObject)
│   └── SpeakerManager Component
├── SpatialSource (Empty GameObject)
│   └── SpatialSource Component
└── Directional Light (默认)
```

## 详细步骤

### 1. 设置 XR Origin

1. 在 Hierarchy 中右键 → XR → XR Origin (VR)
2. 这会自动创建 XR Origin 和必要的组件

### 2. 创建 SpeakerManager

1. 右键 Hierarchy → Create Empty，命名为 "SpeakerManager"
2. 选中 SpeakerManager，在 Inspector 中添加 `SpeakerManager` 组件
3. 配置组件：
   - **YAML File Path**: `speakers.yaml` (相对于 StreamingAssets)
   - **Speaker Prefab**: (可选) 创建自定义预制体，或留空使用默认球体
   - **Default Material**: 创建材质，设置为蓝色或灰色
   - **Active Material**: 创建材质，设置为红色（用于高亮）
   - **Speaker Scale**: 0.1
   - **Show Labels**: ✓

### 3. 创建 SpatialSource

1. 右键 Hierarchy → Create Empty，命名为 "SpatialSource"
2. 设置初始位置，例如 (0, 1.5, 2)
3. 选中 SpatialSource，在 Inspector 中添加 `SpatialSource` 组件
4. 配置组件：
   - **OSC IP**: `127.0.0.1` (本地) 或 C++ 后端所在机器的 IP
   - **OSC Port**: `7000`
   - **Update Rate**: `60`
   - **Source Material**: 创建材质，设置为黄色或绿色
   - **Source Scale**: 0.15
   - **Line Color**: 红色
   - **Line Width**: 0.02

### 4. 配置 Meta XR 抓取交互

根据你的 Meta XR SDK 版本，在 `SpatialSource.cs` 中调整：

#### 方法 1: 使用 GrabInteractable (推荐)

1. 选中 SpatialSource GameObject
2. 添加 Component → Oculus → Interaction → Grab Interactable
3. 配置 Grab Interactable：
   - **Interaction Layer**: Default
   - **Grab Type**: One Handed
   - **Movement Type**: Instant

#### 方法 2: 手动添加脚本

在 `SpatialSource.cs` 的 `SetupGrabInteraction()` 方法中，取消注释并调整：

```csharp
// 对于 Meta XR SDK v50+
using Meta.XR.InteractionSystem;
var grabInteractable = gameObject.AddComponent<GrabInteractable>();

// 或对于旧版本
using Oculus.Interaction;
var grabInteractable = gameObject.AddComponent<GrabInteractable>();
```

### 5. 创建材质

1. **Default Speaker Material**:
   - Create → Material，命名为 "SpeakerDefault"
   - Albedo: 浅蓝色 (0.5, 0.7, 1.0)
   - Metallic: 0.3
   - Smoothness: 0.5

2. **Active Speaker Material**:
   - Create → Material，命名为 "SpeakerActive"
   - Albedo: 红色 (1.0, 0.2, 0.2)
   - Emission: 启用，颜色为红色
   - Intensity: 2.0

3. **Source Material**:
   - Create → Material，命名为 "SourceMaterial"
   - Albedo: 黄色 (1.0, 0.8, 0.2)
   - Metallic: 0.5
   - Smoothness: 0.7

### 6. 配置 StreamingAssets

1. 在 Project 窗口，右键 Assets 文件夹
2. Create → Folder，命名为 "StreamingAssets"
3. 将 `speakers.yaml` 文件复制到此文件夹

### 7. 测试场景

1. 确保 C++ 后端已启动
2. 在 Unity Editor 中点击 Play
3. 使用 Scene 视图或 Game 视图观察：
   - 28 个扬声器小球应该出现在场景中
   - 声源小球应该可见
   - 移动 SpatialSource 的位置，观察最近的 3 个扬声器高亮

### 8. 构建到 Quest

1. File → Build Settings
2. 选择 Android 平台
3. Player Settings:
   - **Minimum API Level**: Android 7.0 (API 24)
   - **Target API Level**: 最新
   - **XR Plug-in Management**: 启用 Oculus
4. 连接 Quest 设备（USB 或无线）
5. Build and Run

## 调试提示

- 如果扬声器不显示，检查 YAML 文件路径和格式
- 如果 OSC 不工作，检查 Unity Console 的错误信息
- 使用 Unity Profiler 检查性能
- 在 Quest 上运行时，使用 Oculus Developer Hub 查看日志
