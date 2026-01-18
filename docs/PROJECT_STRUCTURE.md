# 项目结构说明

## 目录结构

```
SoundARray/
├── cpp/                          # C++ 后端音频引擎
│   ├── src/
│   │   ├── main.cpp             # 主程序入口，包含调试输出
│   │   ├── ConfigLoader.h/cpp   # YAML 配置文件加载器
│   │   ├── VBAPRenderer.h/cpp   # VBAP 空间音频渲染算法
│   │   ├── OSCReceiver.h/cpp    # OSC 消息接收模块
│   │   ├── AudioEngine.h/cpp    # PortAudio 音频引擎封装
│   │   └── Speaker.h            # 扬声器数据结构
│   ├── CMakeLists.txt           # CMake 构建配置
│   ├── README.md                # C++ 构建说明
│   └── .gitignore
│
├── unity/                        # Unity 前端 (Meta Quest AR)
│   └── Assets/Scripts/
│       ├── SpeakerManager.cs           # 扬声器可视化管理
│       ├── SpatialSource.cs            # 声源交互与 OSC 发送
│       ├── OSCClient.cs                # OSC 客户端实现
│       ├── SpatialAudioController.cs   # 主控制器（可选）
│       └── SimpleDrag.cs               # Editor 测试用拖拽脚本
│   ├── README.md                # Unity 设置说明
│   ├── SCENE_SETUP.md          # 场景设置详细指南
│   └── .gitignore
│
├── speakers.yaml                # 28 个扬声器的位置配置
├── readme.md                    # 项目主文档
├── BUILD.md                     # 构建指南
├── PROJECT_STRUCTURE.md         # 本文档
└── .gitignore
```

## 核心模块说明

### C++ 后端

#### ConfigLoader
- **功能**: 解析 `speakers.yaml`，加载 28 个扬声器的位置信息
- **输入**: YAML 文件路径
- **输出**: `std::vector<Speaker>`

#### OSCReceiver
- **功能**: 监听 UDP 端口，接收 Unity 发送的声源位置
- **协议**: OSC `/spatial/source_pos` (float x, float y, float z)
- **端口**: 默认 7000（可配置）
- **线程**: 独立监听线程

#### VBAPRenderer
- **功能**: 实现 VBAP 算法，计算 28 路增益
- **核心方法**: `ComputeVBAP()` - 可替换为其他算法
- **平滑**: 指数平滑，防止增益突变
- **接口**: 
  - `UpdateSourcePosition(x, y, z)` - 更新声源位置
  - `GetGains()` - 获取平滑后的增益
  - `GetRawGains()` - 获取原始计算的增益

#### AudioEngine
- **功能**: PortAudio 封装，28 通道音频输出
- **音频源**: 
  - 粉红噪声（默认）
  - WAV 文件（单声道，16-bit PCM）
- **回调**: `AudioCallback` - 实时应用增益并输出

### Unity 前端

#### SpeakerManager
- **功能**: 
  - 读取 `speakers.yaml`
  - 在 AR 空间中生成 28 个扬声器可视化小球
  - 显示扬声器 ID 标签
  - 高亮激活的扬声器

#### SpatialSource
- **功能**:
  - 声源可视化（小球）
  - Meta XR SDK 抓取交互
  - 实时计算与各扬声器的距离
  - 高亮最近的 3 个扬声器
  - 使用 LineRenderer 显示连接线（粗细随增益变化）
  - OSC 发送声源位置（60 Hz）

#### OSCClient
- **功能**: 轻量级 OSC 客户端实现
- **协议**: UDP，支持 float/int/string 类型
- **使用**: `Send(address, ...values)`

## 数据流

```
Unity (Quest AR)
    │
    │ 用户移动声源 (手柄抓取)
    ▼
SpatialSource.cs
    │
    │ 计算位置
    ▼
OSCClient.cs
    │
    │ UDP /spatial/source_pos (x, y, z)
    ▼
C++ OSCReceiver
    │
    │ 回调
    ▼
AudioEngine
    │
    │ 更新位置
    ▼
VBAPRenderer
    │
    │ 计算增益
    ▼
AudioEngine (PortAudio Callback)
    │
    │ 应用增益 + 平滑
    ▼
28 通道音频输出
```

## 扩展点

### 替换渲染算法
1. 创建新类（如 `AmbisonicsRenderer`）
2. 实现相同接口：`UpdateSourcePosition()`, `GetGains()`
3. 在 `AudioEngine::Initialize()` 中替换

### 添加更多 OSC 消息
- 在 `OSCReceiver.cpp` 的 `ProcessMessage()` 中添加新的地址模式
- 在 Unity 端使用 `OSCClient.Send()` 发送

### 增强可视化
- 在 `SpatialSource.cs` 的 `UpdateVisualFeedback()` 中添加效果
- 使用 Unity Particle System、Trail Renderer 等

## 配置文件

### speakers.yaml
- **格式**: YAML
- **结构**: 
  ```yaml
  speakers:
    - id: 0
      x: -2.0
      y: 1.5
      z: 0.0
  ```
- **位置**: 
  - C++: 项目根目录或命令行参数
  - Unity: `Assets/StreamingAssets/speakers.yaml`

## 调试工具

### C++ 端
- 命令行实时输出激活扬声器 ID 和增益值
- 在 `main.cpp` 的 `PrintActiveSpeakers()` 中实现

### Unity 端
- Scene 视图可视化
- Console 日志
- Inspector 实时查看组件状态
