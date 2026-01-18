# 构建指南

## 快速开始

### 1. C++ 后端

#### Windows (Visual Studio)

1. 安装依赖：
   - 下载 PortAudio: https://www.portaudio.com/download.html
   - 解压到 `cpp/third_party/portaudio/`
   - 下载 oscpack: `git clone https://github.com/RossBencina/oscpack.git cpp/third_party/oscpack`

2. 使用 CMake 生成 Visual Studio 项目：
   ```powershell
   cd cpp
   mkdir build
   cd build
   cmake .. -G "Visual Studio 17 2022"
   ```

3. 打开生成的 `SoundARray.sln`，编译运行

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

```bash
# 使用粉红噪声测试
./SoundARray speakers.yaml

# 使用音频文件测试
./SoundARray speakers.yaml test_audio.wav
```

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
