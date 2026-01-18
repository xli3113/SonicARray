# SoundARray - AR 空间音频系统

基于 Meta Quest 的 AR 空间音频研究项目，支持 28 通道扬声器阵列的 VBAP 空间音频渲染。

## 项目结构

```
SoundARray/
├── cpp/                    # C++ 后端音频引擎
│   ├── src/                # 源代码
│   └── CMakeLists.txt      # CMake 构建配置
├── unity/                   # Unity 前端 (Meta Quest AR)
│   └── Assets/Scripts/     # Unity 脚本
├── docs/                    # 文档目录
│   ├── QUICKSTART.md       # 快速开始指南
│   ├── BUILD.md            # 构建指南
│   ├── DEBUG_WITHOUT_QUEST.md # 调试指南
│   ├── ARCHITECTURE_DESIGN.md # 架构设计
│   ├── HOW_TO_ADD_NEW_ALGORITHM.md # 添加新算法
│   └── PROJECT_STRUCTURE.md # 项目结构
├── tools/                   # 工具脚本
│   └── test_osc_sender.py  # OSC 测试工具
├── speakers.yaml           # 扬声器配置文件
└── readme.md              # 本文档（项目入口）
```

## 技术栈

### 后端 (C++)
- **PortAudio**: 多通道音频输出
- **oscpack**: OSC 消息接收
- **YAML**: 配置文件解析（简单实现）

### 前端 (Unity)
- **Meta XR SDK**: Quest AR 交互
- **OSC**: 与后端通信
- **LineRenderer**: 可视化反馈

## 构建说明

### C++ 后端

#### 依赖项
1. **PortAudio**: 下载并编译 PortAudio
   ```bash
   # 下载 PortAudio
   git clone https://github.com/PortAudio/portaudio.git
   cd portaudio
   ./configure && make
   ```

2. **oscpack**: 下载 oscpack
   ```bash
   git clone https://github.com/RossBencina/oscpack.git third_party/oscpack
   ```

#### 构建步骤
```bash
cd cpp
mkdir build
cd build
cmake ..
make  # 或 Visual Studio 打开生成的 .sln
```

#### 运行
```bash
./SoundARray [speakers.yaml] [audio_file.wav]
# 如果不提供音频文件，将使用粉红噪声
```

### Unity 前端

#### 设置步骤
1. 创建新的 Unity 项目（Unity 2021.3+）
2. 导入 Meta XR SDK (Oculus Integration)
3. 将 `unity/Assets/Scripts/` 中的脚本复制到 Unity 项目的 `Assets/Scripts/`
4. 将 `speakers.yaml` 复制到 `Assets/StreamingAssets/`
5. 创建场景：
   - 添加空 GameObject，附加 `SpeakerManager` 组件
   - 添加空 GameObject，附加 `SpatialSource` 组件
   - 配置 OSC IP 和端口（默认 127.0.0.1:7000）

#### Meta XR SDK 集成
在 `SpatialSource.cs` 中，需要根据你的 Meta XR SDK 版本调整抓取交互代码：

```csharp
// 示例：使用 Meta XR SDK 的 GrabInteractable
using Meta.XR.InteractionSystem;

var grabInteractable = gameObject.AddComponent<GrabInteractable>();
```

## 配置

### speakers.yaml
YAML 格式的扬声器位置配置：
```yaml
speakers:
  - id: 0
    x: -2.0
    y: 1.5
    z: 0.0
  # ... 更多扬声器
```

## 使用流程

1. **启动 C++ 后端**
   ```bash
   ./SoundARray speakers.yaml
   ```

2. **启动 Unity 项目**（在 Quest 上运行或通过 Link）

3. **交互**
   - 在 AR 空间中，使用 Quest 手柄抓取并移动声源小球
   - 观察最近的 3 个扬声器高亮显示
   - 观察连接线粗细随增益变化

4. **调试**
   - C++ 端会实时打印激活的扬声器 ID 和增益值
   - Unity 端显示可视化反馈

## 无 Quest 头显时的调试

**重要**：即使没有 Quest 头显，你仍然可以调试项目的**大部分功能**！

- ✅ C++ 后端（100% 可调试）
- ✅ Unity Editor 可视化（90% 可调试）
- ✅ OSC 通信（100% 可调试）
- ✅ VBAP 算法（100% 可调试）

详细调试指南请参考：[docs/DEBUG_WITHOUT_QUEST.md](docs/DEBUG_WITHOUT_QUEST.md)

**快速测试工具**：
- `tools/test_osc_sender.py` - Python OSC 测试工具（用于测试 C++ 后端）
- `unity/Assets/Scripts/EditorDebugHelper.cs` - Unity Editor 调试辅助脚本

## OSC 协议

### 发送 (Unity → C++)
- **路径**: `/spatial/source_pos`
- **参数**: `float x, float y, float z`
- **频率**: 60 Hz（可配置）

### 接收 (C++ → Unity)
当前版本为单向通信（Unity → C++），可根据需要扩展。

## VBAP 算法

VBAP (Vector Base Amplitude Panning) 算法已封装在 `VBAPRenderer` 类中。核心计算逻辑在 `ComputeVBAP()` 方法中，可以方便地替换为其他渲染算法（如 Ambisonics、HOA 等）。

### 增益平滑
增益更新使用指数平滑，时间常数默认 50ms，可通过 `SetSmoothingTime()` 调整。

## 扩展与定制

### 替换渲染算法
详细步骤请参考：[docs/HOW_TO_ADD_NEW_ALGORITHM.md](docs/HOW_TO_ADD_NEW_ALGORITHM.md)

架构设计说明：[docs/ARCHITECTURE_DESIGN.md](docs/ARCHITECTURE_DESIGN.md)

### 添加更多可视化
在 `SpatialSource.cs` 的 `UpdateVisualFeedback()` 方法中添加更多视觉效果。

## Linux 系统说明

**重要**: 在 Linux 系统上编译后，生成的是**可执行文件**（无 .exe 扩展名），可以直接运行。

```bash
# Linux 编译
cd cpp
mkdir build && cd build
cmake ..
make

# 运行（生成的文件名为 SoundARray，不是 SoundARray.exe）
./SoundARray ../speakers.yaml
```

详细构建说明请参考 [docs/BUILD.md](docs/BUILD.md)

## 故障排除

### C++ 端无法接收 OSC
- 检查防火墙设置
- 确认端口 7000 未被占用
- 检查 Unity 端的 OSC IP 配置

### Unity 端无法连接
- 确认 C++ 后端已启动
- 检查 IP 地址和端口配置
- 查看 Unity Console 的错误信息

### 音频输出问题
- 检查 PortAudio 设备配置
- 确认系统支持多通道输出
- 检查音频驱动

## 许可证

本项目为研究用途，请根据实际需求选择合适的许可证。

## 贡献

欢迎提交 Issue 和 Pull Request！
