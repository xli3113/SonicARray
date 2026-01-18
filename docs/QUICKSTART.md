# 快速开始指南

## 5 分钟快速测试

### 1. 准备 C++ 后端（约 2 分钟）

```bash
# 克隆依赖
cd cpp
git clone https://github.com/RossBencina/oscpack.git third_party/oscpack

# 下载 PortAudio（或使用包管理器）
# Windows: 下载预编译版本
# Linux: sudo apt-get install libportaudio-dev
# macOS: brew install portaudio

# 构建
mkdir build && cd build
cmake ..
make  # 或 Visual Studio 打开 .sln

# 运行（使用粉红噪声）
./SoundARray ../speakers.yaml
```

### 2. 准备 Unity 前端（约 3 分钟）

1. **创建 Unity 项目**
   - Unity Hub → New Project → 3D (URP)
   - 项目名称: SoundARrayTest

2. **导入 Meta XR SDK**
   - Window → Package Manager → My Assets
   - 导入 Oculus Integration

3. **复制脚本**
   ```bash
   # 将 unity/Assets/Scripts/ 复制到 Unity 项目的 Assets/Scripts/
   ```

4. **设置场景**
   - 创建空 GameObject "SpeakerManager"
     - Add Component → SpeakerManager
   - 创建空 GameObject "SpatialSource"
     - Add Component → SpatialSource
     - 位置设置为 (0, 1.5, 2)
   - 复制 `speakers.yaml` 到 `Assets/StreamingAssets/`

5. **测试运行**
   - 点击 Play
   - 在 Scene 视图中移动 SpatialSource
   - 观察 C++ 控制台的 OSC 消息

## 完整设置

### C++ 后端详细设置

参见 [BUILD.md](./BUILD.md)

### Unity 前端详细设置

参见 [../unity/README.md](../unity/README.md) 和 [../unity/SCENE_SETUP.md](../unity/SCENE_SETUP.md)

## 验证安装

### 检查清单

- [ ] C++ 后端编译成功
- [ ] C++ 后端可以读取 `speakers.yaml`
- [ ] C++ 后端启动并监听 OSC 端口 7000
- [ ] Unity 项目可以运行
- [ ] Unity 场景中显示 28 个扬声器
- [ ] Unity 可以发送 OSC 消息（查看 Console）
- [ ] C++ 端接收到 OSC 消息（查看控制台输出）

### 常见问题

**Q: C++ 端找不到 PortAudio**
- A: 检查 `CMakeLists.txt` 中的路径设置
- A: 确认 PortAudio 已正确安装

**Q: Unity 端看不到扬声器**
- A: 检查 `speakers.yaml` 是否在 `StreamingAssets` 文件夹
- A: 检查 YAML 文件格式是否正确
- A: 查看 Unity Console 的错误信息

**Q: OSC 消息没有收到**
- A: 检查防火墙设置
- A: 确认 IP 地址正确（127.0.0.1 用于本地测试）
- A: 检查端口号是否匹配（默认 7000）

## 下一步

1. **自定义扬声器布局**: 编辑 `speakers.yaml`
2. **添加音频文件**: `./SoundARray speakers.yaml your_audio.wav`
3. **调整可视化**: 修改 Unity 脚本中的材质和颜色
4. **替换渲染算法**: 修改 `VBAPRenderer.cpp` 或创建新的渲染器类

## 获取帮助

- 查看 [../readme.md](../readme.md) 了解项目概述
- 查看 [PROJECT_STRUCTURE.md](./PROJECT_STRUCTURE.md) 了解代码结构
- 查看 [BUILD.md](./BUILD.md) 了解详细构建步骤
