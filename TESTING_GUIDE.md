# SonicARray 测试指南

本文档说明如何在**没有物理 28 路扬声器阵列**的情况下测试系统，包括 VR 头显测试方法，以及在 Windows 和 Linux 上分别编译 C++ 后端的完整流程。

---

## 目录

1. [系统架构与数据流](#1-系统架构与数据流)
2. [能否用 VR 头显测试？](#2-能否用-vr-头显测试)
3. [Windows 编译流程（C++ 后端）](#3-windows-编译流程c-后端)
4. [Linux 编译流程（C++ 后端）](#4-linux-编译流程c-后端)
5. [Unity 前端配置与构建](#5-unity-前端配置与构建)
6. [完整测试场景：无扬声器 + VR 可视化](#6-完整测试场景无扬声器--vr-可视化)
7. [网络配置说明（Quest 独立模式）](#7-网络配置说明quest-独立模式)
8. [常见问题排查](#8-常见问题排查)

---

## 1. 系统架构与数据流

```
┌─────────────────────────────────────────────────────────┐
│  PC（Windows 或 Linux）                                  │
│                                                          │
│  C++ 后端 (SoundARray)                                   │
│  ┌─────────────────────────────────────────────────┐    │
│  │  OSCReceiver  ←── UDP:7000 ── [声源位置 xyz]    │    │
│  │  VBAPRenderer  →  Delaunay三角剖分 → 28路增益   │    │
│  │  OSCSender    ──→ UDP:9000 ── [/vbap/gains]     │    │
│  │  AudioOutput  →  JACK/PortAudio（可选，无硬件   │    │
│  │                  时自动跳过，后端继续运行）      │    │
│  └─────────────────────────────────────────────────┘    │
│            ↑ UDP:7000          ↓ UDP:9000                │
└────────────┼───────────────────┼─────────────────────────┘
             │                   │
    ┌────────┴───────────────────┴────────┐
    │  Unity 前端 (Meta Quest 或 Editor)  │
    │                                     │
    │  SpatialSource → 发送位置           │
    │  VBAPGainReceiver → 接收真实增益    │
    │  SpeakerManager → 颜色/高度可视化   │
    └─────────────────────────────────────┘
```

**无扬声器时**：C++ 后端音频输出初始化失败后打印 `audio output init fail, osc/vbap only`，然后继续运行——VBAP 计算和 OSC 收发完全正常，只是没有实际声音输出。

---

## 2. 能否用 VR 头显测试？

**可以。** 支持两种使用方式：

### 方式 A：Unity Editor + Quest Link（推荐开发阶段）

用 Meta Quest Link（USB 线）或 Air Link（WiFi）将 Quest 连接到 PC，在 Unity Editor 中直接运行：

- C++ 后端、Unity Editor、Quest 渲染**全部在同一台 PC** 上，网络通信走 `127.0.0.1` 本机回环，无需任何 IP 配置
- 可以戴着头显在 VR 空间中拖动声源，实时看到 28 个扬声器方块按真实 VBAP 增益变色/变高
- 无需编译 APK，调试方便

**操作步骤：**
1. 启动 Meta Quest Link 应用，连接头显
2. 在 PC 上启动 C++ 后端（见第 3/4 节）
3. 在 Unity Editor 中点击 Play
4. 戴上头显，场景会显示在 VR 中

### 方式 B：Quest 独立 APK（无线脱机）

将 Unity 打包成 Android APK 安装到 Quest，头显与 PC 通过 **同一 WiFi 局域网** 通信：

- PC 运行 C++ 后端，监听局域网上的 UDP:7000
- Quest 向 PC 的**局域网 IP**（如 `192.168.1.100`）发送声源位置
- C++ 后端向 Quest 的**局域网 IP** 回传 `/vbap/gains`

⚠️ **注意**：C++ 后端默认将增益反馈发回 `127.0.0.1`，独立模式下需要改成 Quest 的 IP（见[第 7 节](#7-网络配置说明quest-独立模式)）。

### 功能对比

| 功能 | Editor 单机 | Quest Link | Quest 独立 APK |
|------|:-----------:|:----------:|:--------------:|
| 不需要物理扬声器 | ✅ | ✅ | ✅ |
| 真实 VBAP 可视化 | ✅（需后端） | ✅（需后端） | ✅（需后端+网络） |
| 本地近似降级 | ✅ | ✅ | ✅ |
| 键盘控制声源 | ✅ | ✅ | ❌ |
| Quest 手柄控制 | ❌ | ✅ | ✅ |
| 无需配置 IP | ✅ | ✅ | ❌ |
| 无需 USB/WiFi 连接 | ✅ | ❌ | ❌ |

---

## 3. Windows 编译流程（C++ 后端）

### 前置依赖

| 工具 | 版本要求 | 下载 |
|------|----------|------|
| Visual Studio | 2019 或 2022（含 C++ 工作负载） | https://visualstudio.microsoft.com |
| CMake | ≥ 3.10 | https://cmake.org/download/ |
| Git | 任意版本 | https://git-scm.com |

> **音频输出库**：Windows 默认使用 PortAudio。若不需要实际播放声音（只测试 VBAP 可视化），PortAudio 初始化失败时后端仍可正常运行，也可跳过安装。

### 步骤

#### 1. 获取代码

```powershell
git clone <repo-url>
cd SonicARray
```

#### 2. 确认 oscpack 已存在

```powershell
# 应看到 cpp/third_party/oscpack/osc/ 目录
dir cpp\third_party\oscpack
```

如果缺失：
```powershell
cd cpp\third_party
git clone https://github.com/RossBencina/oscpack.git oscpack
cd ..\..
```

#### 3. （可选）获取 PortAudio

若希望实际播放音频：
```powershell
cd cpp\third_party
git clone https://github.com/PortAudio/portaudio.git portaudio
cd ..\..
```

#### 4. 配置 CMake

打开 **Developer PowerShell for VS 2022**（或 VS 2019），执行：

```powershell
cd cpp
mkdir build
cd build

# 不使用 PortAudio（仅 OSC + VBAP，无音频输出，适合无扬声器测试）
cmake -G "Visual Studio 17 2022" -A x64 -DUSE_JACK=OFF ..

# 或：使用 PortAudio（需已克隆 third_party/portaudio）
cmake -G "Visual Studio 17 2022" -A x64 -DUSE_JACK=OFF ..
```

> VS 2019 将 `"Visual Studio 17 2022"` 改为 `"Visual Studio 16 2019"`

#### 5. 编译

```powershell
# Release 版本
cmake --build . --config Release

# 或在 VS 中打开 SoundARray.sln，选 Release x64，按 Ctrl+Shift+B
```

#### 6. 运行

```powershell
cd Release
.\SoundARray.exe ..\..\..\speakers.yaml
```

预期输出：
```
=== SoundARray ===
renderer: VBAP
vbap 29 spk 8 src
built XXX tris
osc 7000
osc feedback -> 127.0.0.1:9000
audio output init fail, osc/vbap only   ← 无扬声器时正常
running, enter to quit, osc /spatial/source_pos
```

---

## 4. Linux 编译流程（C++ 后端）

### 前置依赖

```bash
# Ubuntu / Debian
sudo apt-get update
sudo apt-get install -y \
    build-essential \
    cmake \
    git \
    libjack-jackd2-dev \   # JACK（Linux 默认音频后端）
    pkg-config

# Arch Linux
sudo pacman -S base-devel cmake git jack2 pkg-config

# Fedora / RHEL
sudo dnf install gcc-c++ cmake git jack-audio-connection-kit-devel pkgconf
```

> **无 JACK / 仅测试 VBAP**：也可以用 `-DUSE_JACK=OFF` 跳过 JACK，这样音频初始化会失败但后端仍继续运行（OSC + VBAP 正常）。此时无需安装 `libjack-jackd2-dev`。

### 步骤

#### 1. 获取代码

```bash
git clone <repo-url>
cd SonicARray
```

#### 2. 确认 oscpack

```bash
ls cpp/third_party/oscpack/osc/
# 应能看到 OscTypes.h 等文件
```

如果缺失：
```bash
cd cpp/third_party
git clone https://github.com/RossBencina/oscpack.git oscpack
cd ../..
```

#### 3. 编译

```bash
cd cpp
mkdir -p build && cd build

# 推荐：JACK 输出（Linux 默认）
cmake -DUSE_JACK=ON ..
cmake --build . -j$(nproc)

# 替代：跳过音频输出（只测 VBAP 可视化，无需 JACK）
cmake -DUSE_JACK=OFF ..
cmake --build . -j$(nproc)
```

#### 4. 运行

```bash
# 方式一：直接运行（无 JACK，仅 OSC+VBAP）
./SoundARray ../../speakers.yaml

# 方式二：先启动 JACK，再运行（有音频输出）
jackd -d alsa -r 44100 -p 1024 -n 2 &
sleep 1
./SoundARray ../../speakers.yaml
```

预期输出同 Windows，`audio output init fail, osc/vbap only` 在无 JACK 时属正常现象。

#### 5. 虚拟扬声器监控（无物理硬件时）

用 meterbridge 作为"虚拟 28 路扬声器"观察各通道信号：

```bash
sudo apt-get install -y meterbridge

# 启动 meterbridge（N=通道数，通常为 29）
meterbridge -c 7 -n meter -t dpm $(seq -f 'SonicARrayBackend:spk_%.0f' 1 29)

# 用脚本批量连接 JACK 端口
for i in $(seq 1 29); do
    jack_connect SonicARrayBackend:spk_$i meter:input_$i
done
```

---

## 5. Unity 前端配置与构建

### 环境要求

| 工具 | 版本 |
|------|------|
| Unity | 2021.3 LTS 或更高（项目实测：2022.3） |
| Meta XR Core SDK | 85.0.0（已在 Packages/manifest.json 中） |
| Android Build Support | Unity 模块，Quest 打包时需要 |
| JDK / Android SDK | 随 Unity 安装 Android Build Support 自动获取 |

### 场景配置（必须）

在 Unity 场景中需要有以下 GameObject 及组件：

```
Scene
├── SourceManager      [SourceManager]         ← 多声源管理
├── SpeakerManager     [SpeakerManager]         ← 28路扬声器可视化
├── XRSourceController [XRSourceController]     ← 手柄/键盘输入
└── VBAPFeedback       [VBAPGainReceiver]       ← ← 新增：接收C++真实增益
```

**VBAPGainReceiver 是新增组件**，必须添加到场景中：
1. 场景中新建空 GameObject，命名为 `VBAPFeedback`
2. 添加 `VBAPGainReceiver` 组件
3. `listenPort` 保持 `9000`
4. `speakerManager` 字段留空（自动查找）或手动拖入

### Editor 内测试（无需打包）

直接点击 Play，使用键盘操作：

| 按键 | 功能 |
|------|------|
| `Space` | 创建新声源 |
| `Delete` | 删除当前声源 |
| `Tab` | 切换选中声源 |
| `W/A/S/D` | 水平移动声源 |
| `Q / E` | 垂直移动声源（上/下） |
| 鼠标拖拽 | 直接拖动声源球体 |

### Android APK 打包（Quest 独立模式）

1. File → Build Settings → 切换平台到 **Android**
2. Player Settings：
   - **Minimum API Level**：Android 10（API 29）
   - **Scripting Backend**：IL2CPP
   - **Target Architectures**：ARM64 ✅
3. XR Plug-in Management（Android）：勾选 **Meta OpenXR** 或 **Oculus**
4. 连接 Quest（USB，开启开发者模式）
5. Build and Run

---

## 6. 完整测试场景：无扬声器 + VR 可视化

以下是**完整的端到端测试流程**（以 Linux + Quest Link 为例，Windows 同理）：

### 步骤 1：启动 C++ 后端

```bash
cd cpp/build
./SoundARray ../../speakers.yaml
```

确认终端出现：
```
osc 7000
osc feedback -> 127.0.0.1:9000
```

### 步骤 2：启动 Unity（Quest Link 模式）

1. 连接 Quest，打开 Meta Quest Link 应用
2. 在 Unity Editor 中点击 Play
3. 戴上头显

### 步骤 3：操作与观察

- 用手柄 **A 键** 创建声源（或 Editor 中按 `Space`）
- 用右摇杆移动声源位置（或 WASD）
- 观察 28 个扬声器方块：
  - **激活的扬声器**（VBAP 三角形内的 2-3 个）：变亮、变高、显示彩色
  - **未激活的扬声器**：恢复灰色默认状态
  - 移动声源时，激活扬声器组合实时切换

### 预期视觉效果

```
声源在两扬声器中间  →  两个扬声器等亮、等高
声源靠近某扬声器    →  该扬声器更亮更高，另一个更暗
声源移到三角形边界  →  增益平滑过渡到相邻三角形
```

### 验证是否用了真实 VBAP

在 Unity Console 中，若 `VBAPGainReceiver` 正常工作，不会有 fallback 相关的 Warning。可在 `SpeakerManager.cs` 临时加日志：

```csharp
// 在 CommitGainFrame() 或 CommitCppGainFrame() 中加
Debug.Log($"[CPP gains] active={HasFreshCppGains}");
```

---

## 7. 网络配置说明（Quest 独立模式）

Quest 作为独立 APK 运行时，PC 和 Quest 在同一 WiFi 局域网，需要：

### Unity 侧配置

在场景的 `SpatialSource`（或 `SourceManager`）Inspector 中：
- `oscIP` 改为 **PC 的局域网 IP**（如 `192.168.1.100`）
- `oscPort` 保持 `7000`

在 `VBAPGainReceiver`：
- `listenPort` 保持 `9000`（Quest 本机监听）

### C++ 侧配置

默认发送反馈到 `127.0.0.1:9000`，独立模式需要改为 Quest 的局域网 IP。

在 `cpp/src/main.cpp` 中，`engine.Initialize()` 调用之前添加：

```cpp
engine.SetFeedbackTarget("192.168.1.XXX", 9000);  // 替换为 Quest 的 IP
```

Quest 的 IP 可在头显的 **Settings → WiFi → 当前网络 → 详情** 中查看。

> **提示**：Quest Link / Air Link 模式下一切走本机，不需要修改任何 IP。

---

## 8. 常见问题排查

### C++ 后端

| 现象 | 原因 | 处理 |
|------|------|------|
| `cant load speakers` | speakers.yaml 路径错误 | 传入正确路径或 cd 到含 speakers.yaml 的目录再运行 |
| `audio output init fail` | 无 JACK/PortAudio 或无多声道硬件 | **正常现象**，VBAP 和 OSC 仍工作 |
| `OSCSender init fail` | 端口被占用或网络权限 | 检查 9000 端口是否被其他程序占用 |
| `built 0 tris` | speakers.yaml 扬声器少于 3 个或坐标有误 | 检查 YAML 格式 |
| Windows 链接错误 `ws2_32` | CMakeLists.txt 未包含 | 确认使用本项目的 CMakeLists.txt（已包含） |

### Unity 前端

| 现象 | 原因 | 处理 |
|------|------|------|
| 扬声器不变色 | VBAPGainReceiver 未挂到场景 | 新建 GameObject → 添加 VBAPGainReceiver 组件 |
| 扬声器用近似值（颜色不准） | C++ 后端未启动或 IP 配置错误 | 启动后端，检查 oscIP 设置 |
| `Could not open port 9000` | 端口被占用 | 关闭占用 9000 端口的程序，或修改 listenPort |
| Quest 独立模式收不到反馈 | C++ 发回 127.0.0.1 | 用 SetFeedbackTarget 设置 Quest 的局域网 IP |
| Editor 键盘无响应 | `useKeyboardFallback` 未开启 | XRSourceController Inspector 中勾选 `useKeyboardFallback` |
| 打包后 speakers.yaml 找不到 | 未放入 StreamingAssets | 将 speakers.yaml 放到 `Assets/StreamingAssets/` |

---

*最后更新：2026-03-15*
