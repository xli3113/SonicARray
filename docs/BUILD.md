# 构建指南

## 快速开始

### 1. C++ 后端

#### Windows（后端在 Windows 运行）

**1. 准备依赖（只需做一次）**

在项目根目录（`SonicARray`）下执行，把 **PortAudio** 和 **oscpack** 放到 `cpp/third_party/`，CMake 会一起编译 PortAudio，无需单独安装：

```bash
cd cpp
mkdir -p third_party
cd third_party
git clone https://github.com/PortAudio/portaudio.git portaudio
git clone https://github.com/RossBencina/oscpack.git oscpack
cd ..
```

**2. 构建与运行**

**方式一：Visual Studio**

1. 安装 **Visual Studio 2019 或 2022**，并勾选 **“使用 C++ 的桌面开发”**。
2. 打开 **“x64 本机工具命令提示符”**（开始菜单 → Visual Studio → x64 Native Tools Command Prompt），执行：
   ```cmd
   cd path\to\SonicARray\cpp
   mkdir build
   cd build
   cmake .. -G "Visual Studio 17 2022" -A x64
   ```
   若用 VS 2019：`cmake .. -G "Visual Studio 16 2019" -A x64`
3. 打开生成的 `SoundARray.sln`，选 **Release | x64**，生成解决方案。
4. 运行后端：
   ```cmd
   Release\SoundARray.exe ..\..\speakers.yaml
   ```
   或在项目根目录有 `speakers.yaml` 时：`Release\SoundARray.exe speakers.yaml`（需把 `speakers.yaml` 复制到 `cpp/build` 或改路径）。

**方式二：MinGW（Git Bash / MSYS2）**

1. 安装 **MinGW-w64** 或 **MSYS2**，确保 `gcc`、`g++`、`make` 在 PATH。
2. 在 **Git Bash** 或 **MSYS2 MinGW 64-bit** 中：
   ```bash
   cd cpp
   rm -rf build
   mkdir build && cd build
   cmake .. -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER=gcc -DCMAKE_CXX_COMPILER=g++
   mingw32-make
   ```
3. 运行：
   ```bash
   ./SoundARray.exe ../speakers.yaml
   ```
   若 `speakers.yaml` 在项目根：`./SoundARray.exe ../../speakers.yaml`

**说明**：PortAudio 已通过 `add_subdirectory` 随项目一起编译，无需单独下载预编译库；未安装 VS 时请用方式二（MinGW）。

#### Linux

```bash
# 安装依赖
sudo apt-get install libportaudio2 libportaudio-dev cmake build-essential

# 下载 oscpack
cd cpp
git clone https://github.com/RossBencina/oscpack.git third_party/oscpack

# 构建
mkdir build && cd build
cmake ..
make
```

#### macOS

```bash
# 安装依赖
brew install portaudio cmake

# 下载 oscpack
cd cpp
git clone https://github.com/RossBencina/oscpack.git third_party/oscpack

# 构建
mkdir build && cd build
cmake ..
make
```

### 2. Unity 前端

1. 打开 Unity Hub，创建新项目（Unity 2021.3 LTS 或更高）

2. 导入 Meta XR SDK：
   - Window → Package Manager → My Assets
   - 找到 Oculus Integration，点击 Import

3. 复制脚本：
   ```bash
   # 将 unity/Assets/Scripts/ 复制到 Unity 项目的 Assets/Scripts/
   ```

4. 配置场景：
   - 创建空 GameObject "SpeakerManager"，添加 `SpeakerManager` 组件
   - 创建空 GameObject "SpatialSource"，添加 `SpatialSource` 组件
   - 将 `speakers.yaml` 复制到 `Assets/StreamingAssets/`

5. 构建到 Quest：
   - File → Build Settings → Android
   - Player Settings → XR Plug-in Management → Oculus (启用)
   - Build and Run

## 测试

### 测试 C++ 后端

**Linux / macOS / MinGW：**
```bash
./SoundARray speakers.yaml
./SoundARray speakers.yaml test_audio.wav
```

**Windows (Visual Studio)：**
```cmd
Release\SoundARray.exe speakers.yaml
Release\SoundARray.exe speakers.yaml test_audio.wav
```
（`speakers.yaml` 需在当前目录或写绝对路径；项目根目录的 `speakers.yaml` 会在配置时复制到 `build`。）

### 测试 Unity 连接

1. 启动 C++ 后端
2. 在 Unity Editor 中运行场景（或部署到 Quest）
3. 移动 SpatialSource GameObject
4. 观察 C++ 控制台的 OSC 消息输出

## 故障排除

### PortAudio 找不到设备

- Windows: 检查音频驱动
- Linux: 检查 ALSA/PulseAudio 配置
- macOS: 检查系统音频权限

### OSC 连接失败

- 检查防火墙设置
- 确认端口 7000 未被占用
- 验证 IP 地址配置（Unity 端和 C++ 端）

### Unity 构建失败

- 确认 Meta XR SDK 已正确导入
- 检查 Android SDK 和 NDK 配置
- 查看 Unity Console 的错误信息
