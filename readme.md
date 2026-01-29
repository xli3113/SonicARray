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
│   ├── test_osc_sender.py  # OSC 测试工具
│   └── backend_meter.py    # 可视化 Meter（28 路电平条）
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

2. **oscpack** 与 **PortAudio**（推荐用子项目，Windows 也可用）：
   ```bash
   cd cpp
   mkdir -p third_party && cd third_party
   git clone https://github.com/RossBencina/oscpack.git oscpack
   git clone https://github.com/PortAudio/portaudio.git portaudio
   ```
   之后 CMake 会一起编译 PortAudio，无需单独安装。

#### 构建步骤
```bash
cd cpp
mkdir build
cd build
cmake ..
make  # 或 Visual Studio 打开生成的 .sln
```

**Windows 提示**：若在 Git Bash 中报错 `No CMAKE_C_COMPILER could be found`，请二选一：① 使用 **“x64 本机工具命令提示符”**（Visual Studio 菜单）再运行 `cmake ..`；② 使用 MinGW：`cmake .. -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER=gcc -DCMAKE_CXX_COMPILER=g++`，然后 `mingw32-make`。详见 [docs/BUILD.md](docs/BUILD.md)。

#### 运行
```bash
./SoundARray [speakers.yaml] [audio_file.wav]
# 如果不提供音频文件，将使用粉红噪声
```
**Windows (Visual Studio)**：在 `build` 目录下运行 `Release\SoundARray.exe speakers.yaml`（`speakers.yaml` 在项目根时可用 `..\..\speakers.yaml`）。

#### 测试 Backend 与可视化 Meter

- **命令行发 OSC**：`python tools/test_osc_sender.py [manual|line|circle|random]`
- **可视化 Meter**：带 28 路电平条的 GUI，拖滑块发 OSC、看模拟增益  
  ```bash
  python tools/backend_meter.py
  ```
  依赖：Python 3 + tkinter（Windows/macOS 一般自带；Linux: `sudo apt install python3-tk`）

详细说明见：[docs/后端测试与Meter.md](docs/后端测试与Meter.md)

### Unity 前端

#### 快速开始（无 Meta XR SDK）

**如果你没有 Meta Quest 或 Meta XR SDK，可以跳过 XR 相关步骤！**

1. **打开 Unity 项目**：用 Unity Editor 打开 `unity` 文件夹
2. **一键创建场景**：菜单栏选择 **SoundARray** → **Create Scene**，等待提示完成
3. **测试**：点击 Play，在 Game 视图中用鼠标拖拽声源小球

（也可按 [docs/从零创建Unity场景.md](docs/从零创建Unity场景.md) 手动创建场景。）

#### 设置步骤（完整版）

1. 创建新的 Unity 项目（Unity 2021.3+）
2. （可选）导入 Meta XR SDK (Oculus Integration) - **仅在有 Quest 时需要**
3. 将 `unity/Assets/Scripts/` 中的脚本复制到 Unity 项目的 `Assets/Scripts/`
4. 将 `speakers.yaml` 复制到 `Assets/StreamingAssets/`
5. 创建场景：
   - 添加空 GameObject，附加 `SpeakerManager` 组件
   - 添加空 GameObject，附加 `SpatialSource` 组件
   - 配置 OSC IP 和端口（默认 127.0.0.1:7000）
   - **详细步骤**：参考 [docs/从零创建Unity场景.md](docs/从零创建Unity场景.md)

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

### 🎮 无后端调试 Unity（推荐）

**Unity 可视化部分完全独立于后端**，可以在没有后端的情况下运行！

**快速步骤**：
1. 在 Unity 中打开场景
2. 选择 `SpatialSource` GameObject
3. 在 Inspector 中**取消勾选 `Enable OSC`**（禁用 OSC）
4. 点击 Play 运行场景
5. 在 Game 视图中**用鼠标拖拽声源小球**测试

**可以测试的功能**：
- ✅ 扬声器可视化（28 个长方体）
- ✅ 声源移动和可视化反馈
- ✅ 扬声器高亮和高度动画
- ✅ 连接线显示
- ✅ 距离计算和增益估算

**详细中文指南**：
- [docs/无后端调试指南.md](docs/无后端调试指南.md) - 无后端调试说明
- [docs/UNITY测试指南.md](docs/UNITY测试指南.md) - **完整的 Unity 测试步骤** ⭐

**快速测试工具**：
- `tools/test_osc_sender.py` - Python OSC 测试工具（用于测试 C++ 后端）
- `unity/Assets/Scripts/EditorDebugHelper.cs` - Unity Editor 调试辅助脚本
- `unity/Assets/Scripts/SimpleDrag.cs` - 鼠标拖拽脚本（Editor 模式自动启用）

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
