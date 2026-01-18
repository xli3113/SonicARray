# 无 Quest 头显时的调试指南

在没有 Quest 头显的情况下，你仍然可以调试和测试项目的**大部分功能**。以下是详细的调试清单和方法。

## ✅ 可以完全调试的部分

### 1. C++ 后端（100% 可调试）

#### 测试项目：
- ✅ 配置文件加载（YAML 解析）
- ✅ OSC 消息接收
- ✅ VBAP 算法计算
- ✅ 音频输出（粉红噪声或音频文件）
- ✅ 增益平滑
- ✅ 调试输出（激活扬声器显示）

#### 测试方法：

```bash
# 1. 编译项目
cd cpp
mkdir build && cd build
cmake ..
make

# 2. 测试配置加载
./SoundARray ../speakers.yaml
# 应该看到：Loaded 28 speakers from ...

# 3. 测试 OSC 接收（需要发送测试消息）
# 使用 Python 或其他工具发送 OSC 消息
```

#### 使用 Python 测试 OSC 发送：

创建 `../tools/test_osc_sender.py` 或使用已提供的工具:
```python
import socket
import struct
import time

def send_osc(ip, port, address, *args):
    """发送 OSC 消息"""
    # 构建 OSC 消息
    msg = address.encode('ascii')
    msg += b'\x00' * (4 - (len(msg) % 4))  # 对齐到 4 字节
    
    # 类型标签
    type_tag = ',' + ''.join(['f' if isinstance(a, float) else 'i' for a in args])
    msg += type_tag.encode('ascii')
    msg += b'\x00' * (4 - (len(type_tag) % 4))
    
    # 参数
    for arg in args:
        if isinstance(arg, float):
            msg += struct.pack('>f', arg)
        elif isinstance(arg, int):
            msg += struct.pack('>i', arg)
    
    # 发送
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.sendto(msg, (ip, port))
    sock.close()

# 测试：发送声源位置
if __name__ == '__main__':
    print("发送 OSC 测试消息...")
    for i in range(100):
        x = 2.0 * (i / 100.0 - 0.5)  # -1.0 到 1.0
        y = 1.5
        z = 2.0
        send_osc('127.0.0.1', 7000, '/spatial/source_pos', float(x), float(y), float(z))
        print(f"发送位置: ({x:.2f}, {y:.2f}, {z:.2f})")
        time.sleep(0.1)  # 10 Hz
```

运行：
```bash
python3 ../tools/test_osc_sender.py
# 观察 C++ 控制台的输出，应该看到激活的扬声器变化
```

### 2. Unity Editor 调试（90% 可调试）

#### 可以测试的功能：
- ✅ 扬声器可视化（28 个长方体）
- ✅ YAML 配置加载
- ✅ 声源小球显示
- ✅ 扬声器高亮和高度动画
- ✅ LineRenderer 连接线
- ✅ OSC 消息发送（到本地 C++ 后端）
- ✅ 距离计算和增益估算
- ✅ 标签显示

#### 不能测试的功能：
- ❌ Meta XR SDK 抓取交互（需要 Quest）
- ❌ AR 空间定位（需要 Quest）
- ❌ 手柄控制器（需要 Quest）

#### 测试步骤：

1. **在 Unity Editor 中运行场景**
   ```
   - 打开场景
   - 点击 Play 按钮
   - 在 Scene 视图或 Game 视图中查看
   ```

2. **测试扬声器可视化**
   - 检查 28 个长方体是否正确显示
   - 检查 ID 标签是否正确
   - 检查位置是否与 YAML 配置一致

3. **测试声源移动**
   - 在 Hierarchy 中选择 `SpatialSource`
   - 在 Inspector 中修改 Transform 的 Position
   - 观察：
     - 最近的 3 个扬声器是否高亮
     - 连接线是否正确显示
     - 扬声器高度是否变化

4. **测试 OSC 通信**
   - 启动 C++ 后端
   - 在 Unity 中移动声源
   - 检查 C++ 控制台是否收到 OSC 消息

5. **使用 SimpleDrag 脚本测试交互**
   - `SpatialSource` 已包含 `SimpleDrag` 组件（Editor 模式）
   - 在 Game 视图中用鼠标拖拽声源小球
   - 观察实时反馈

### 3. OSC 通信测试（100% 可调试）

#### 双向通信测试：

**Unity → C++**:
- Unity 发送位置
- C++ 接收并打印

**验证方法**:
```bash
# 在 C++ 控制台应该看到：
OSC Receiver listening on port 7000
# 当 Unity 发送消息时，应该看到激活扬声器变化
```

### 4. VBAP 算法验证（100% 可调试）

#### 测试方法：

创建测试脚本验证 VBAP 计算：

```cpp
// 在 main.cpp 中添加测试函数
void TestVBAP() {
    std::vector<Speaker> speakers;
    ConfigLoader::LoadSpeakers("speakers.yaml", speakers);
    
    VBAPRenderer renderer(speakers);
    
    // 测试不同位置
    float testPositions[][3] = {
        {0, 0, 1},    // 前方
        {1, 0, 0},    // 右侧
        {-1, 0, 0},  // 左侧
        {0, 1, 0},   // 上方
    };
    
    for (auto& pos : testPositions) {
        renderer.UpdateSourcePosition(pos[0], pos[1], pos[2]);
        const auto& gains = renderer.GetGains();
        
        std::cout << "Position (" << pos[0] << ", " << pos[1] << ", " << pos[2] << "):\n";
        for (size_t i = 0; i < gains.size(); ++i) {
            if (gains[i] > 0.01f) {
                std::cout << "  Speaker " << speakers[i].id << ": " << gains[i] << "\n";
            }
        }
    }
}
```

## 🔧 调试工具和脚本

### 1. OSC 测试工具

#### 使用 Python（推荐）：
```bash
pip install python-osc
```

创建 `test_osc.py`:
```python
from pythonosc import udp_client
import time

client = udp_client.SimpleUDPClient("127.0.0.1", 7000)

# 发送测试位置
positions = [
    (0, 1.5, 2),    # 前方
    (1, 1.5, 2),    # 右侧
    (-1, 1.5, 2),   # 左侧
    (0, 2.5, 2),    # 上方
]

for x, y, z in positions:
    client.send_message("/spatial/source_pos", [x, y, z])
    print(f"Sent: ({x}, {y}, {z})")
    time.sleep(1)
```

#### 使用命令行工具（如果已安装）：
```bash
# 使用 oscsend (需要安装 liblo-tools)
oscsend localhost 7000 /spatial/source_pos f 0.0 f 1.5 f 2.0
```

### 2. Unity Editor 调试技巧

#### 实时修改参数：
1. 在 Play 模式下修改 `SpatialSource` 的 Transform
2. 观察 `SpeakerManager` 的实时更新
3. 检查 Console 中的 OSC 发送日志

#### 添加调试日志：
在 `SpatialSource.cs` 的 `SendOSCPosition()` 中添加：
```csharp
Debug.Log($"Sending OSC: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
```

在 `SpeakerManager.cs` 的 `UpdateSpeakerState()` 中添加：
```csharp
Debug.Log($"Speaker {speakerId} gain: {gain:F3}, height: {newHeight:F3}");
```

### 3. 可视化验证清单

#### Unity Editor 中检查：
- [ ] 28 个扬声器长方体都显示
- [ ] 每个扬声器都有 ID 标签
- [ ] 声源小球显示（黄色/绿色）
- [ ] 移动声源时，最近的 3 个扬声器高亮
- [ ] 高亮扬声器高度增加
- [ ] 连接线正确显示
- [ ] 连接线粗细随增益变化

#### C++ 后端检查：
- [ ] 成功加载 28 个扬声器
- [ ] OSC 端口监听正常
- [ ] 收到 OSC 消息时打印激活扬声器
- [ ] 增益值合理（0-1 范围）

## 📋 完整测试流程

### 阶段 1：独立测试 C++ 后端

```bash
# 1. 编译
cd cpp/build
cmake .. && make

# 2. 测试配置加载
./SoundARray ../speakers.yaml
# 应该看到：Loaded 28 speakers...

# 3. 测试 OSC 接收（使用 Python 脚本）
python3 ../tools/test_osc_sender.py
# 观察控制台输出
```

### 阶段 2：独立测试 Unity 前端

1. 打开 Unity 项目
2. 运行场景（Play 模式）
3. 在 Scene 视图中：
   - 检查扬声器位置
   - 手动移动 `SpatialSource`
   - 观察可视化反馈
4. 检查 Console：
   - OSC 发送日志
   - 错误信息

### 阶段 3：集成测试

1. **启动 C++ 后端**
   ```bash
   ./SoundARray speakers.yaml
   ```

2. **启动 Unity（Play 模式）**
   - 在 Game 视图中用鼠标拖拽声源
   - 观察：
     - Unity 中的可视化反馈
     - C++ 控制台的激活扬声器输出

3. **验证一致性**
   - Unity 显示的高亮扬声器
   - C++ 打印的激活扬声器
   - 应该匹配（允许 1-2 个差异，因为算法可能略有不同）

## 🎯 重点测试项目

### 必须测试（无 Quest 也能完成）：

1. ✅ **配置加载** - YAML 文件解析
2. ✅ **OSC 通信** - Unity ↔ C++ 消息传递
3. ✅ **VBAP 计算** - 增益计算逻辑
4. ✅ **可视化基础** - 扬声器和声源显示
5. ✅ **距离计算** - Unity 端的距离和增益估算
6. ✅ **音频输出** - C++ 端的音频渲染（如果有音频设备）

### 需要 Quest 才能测试：

1. ❌ **AR 空间定位** - 真实空间中的位置
2. ❌ **手柄抓取** - Meta XR SDK 交互
3. ❌ **手部追踪** - 如果使用手部追踪
4. ❌ **Quest 特定优化** - 性能优化

## 💡 建议的调试顺序

1. **第一周**：C++ 后端完整测试
   - 配置加载
   - OSC 接收
   - VBAP 算法验证
   - 音频输出测试

2. **第二周**：Unity 前端测试
   - 可视化显示
   - OSC 发送
   - 距离计算
   - 增益估算

3. **第三周**：集成测试
   - 端到端通信
   - 数据一致性验证
   - 性能测试

4. **拿到 Quest 后**：AR 交互测试
   - 抓取交互
   - 空间定位
   - 最终优化

## 🐛 常见问题排查

### C++ 端收不到 OSC 消息
- 检查防火墙：`sudo ufw allow 7000/udp`
- 检查端口占用：`netstat -tulpn | grep 7000`
- 检查 IP 地址：Unity 中设置为 `127.0.0.1`

### Unity 中看不到扬声器
- 检查 YAML 文件路径：应该在 `StreamingAssets/`
- 检查 Camera 位置：可能需要调整
- 检查 Scale：扬声器可能太小或太大

### 可视化不更新
- 检查 `UpdateSpeakerState()` 是否被调用
- 检查增益值：是否 > 0.01
- 检查材质：是否正确设置

## 📝 测试记录模板

```
测试日期: ___________
测试环境: Linux / Windows / macOS
Unity 版本: ___________
C++ 编译器: ___________

测试项目：
[ ] C++ 配置加载
[ ] C++ OSC 接收
[ ] Unity 可视化显示
[ ] Unity OSC 发送
[ ] 集成通信测试
[ ] VBAP 算法验证

问题记录：
1. ___________
2. ___________

下一步计划：
___________
```
