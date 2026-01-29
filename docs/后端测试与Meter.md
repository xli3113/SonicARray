# Backend 测试与可视化 Meter

## 如何测试 Backend

### 1. 启动 Backend

```bash
cd cpp
mkdir build && cd build
cmake ..
make   # Windows 可用 Visual Studio 打开生成的 .sln 编译
```

运行（在项目根或 build 目录）：

```bash
./SoundARray speakers.yaml
# 或指定路径
./SoundARray ../speakers.yaml
```

Backend 会：
- 监听 OSC 端口 **7000**，等待 `/spatial/source_pos` (x, y, z)
- 使用粉红噪声（或第二个参数指定的 WAV 文件）做多通道输出
- 在控制台每约 500ms 打印当前**激活扬声器**及增益，例如：`Active Speakers: [3: 0.45] [4: 0.38] [5: 0.22]`

### 2. 用命令行工具发 OSC

```bash
# 项目根目录
python tools/test_osc_sender.py manual
# 然后输入位置，例如: 0 1.5 2

# 或自动模式
python tools/test_osc_sender.py line    # 直线运动
python tools/test_osc_sender.py circle  # 圆周运动
python tools/test_osc_sender.py random  # 随机位置
```

观察 Backend 控制台输出是否随位置变化。

### 3. 用可视化 Meter 测试（推荐）

带 **28 路电平条** 的 GUI，可拖滑块移动声源并看增益条变化：

```bash
# 项目根目录
python tools/backend_meter.py
# 或指定 speakers.yaml
python tools/backend_meter.py speakers.yaml
```

**步骤**：
1. 先启动 Backend（见上文）
2. 再运行 `python tools/backend_meter.py`
3. 拖动 **X / Y / Z** 滑块
4. 观察：
   - Meter 上 **28 根竖条**：当前为**模拟增益**（基于距离的近似，与 Unity 端算法类似）
   - Backend 控制台：**真实 VBAP 增益** 对应的激活扬声器

**说明**：Meter 上的竖条是本地根据声源位置和 `speakers.yaml` 算的**模拟值**，用于直观看到“大概哪几路会亮”；Backend 内部用的是 VBAP，数值以控制台打印为准。若要看到 Backend **真实**增益条，需要 Backend 支持向外发送增益 OSC（见下文“可选：真实增益 Meter”）。

### 4. 用 Unity 测试

1. Unity 场景里 **SpatialSource** 勾选 **Enable OSC**
2. OSC 目标设为 `127.0.0.1`、端口 `7000`
3. 先启动 Backend，再运行 Unity Play
4. 在 Game 视图里拖拽声源小球，看 Backend 控制台激活扬声器是否变化

---

## 可视化 Meter 使用说明

### 运行

- 依赖：Python 3 + **tkinter**（Windows/macOS 一般自带；Linux: `sudo apt install python3-tk`）
- 在项目根执行：`python tools/backend_meter.py [speakers.yaml]`

### 界面

- **声源位置**：三个滑块 X、Y、Z，范围 -3～3
- **28 路电平条**：对应 28 个扬声器，竖条高度表示**模拟增益**（基于距离）
- 声源位置会以约 12 Hz 发送到 Backend（`/spatial/source_pos`）

### 与 Backend 的对应关系

| 你看到的           | 含义 |
|--------------------|------|
| Meter 竖条         | 本机根据位置算的**模拟**增益（哪几路该亮） |
| Backend 控制台输出 | Backend **真实** VBAP 增益（以控制台为准） |

两者应大致一致（同一位置下，亮的路数接近）；若差异大，可检查 `speakers.yaml` 是否与 Backend 使用同一份。

---

## 可选：真实增益 Meter（Backend 发送增益）

若希望 Meter 显示 Backend **真实** VBAP 增益，需要：

1. **Backend 支持**：在 Backend 中定期（例如每 100ms）向某端口（如 `127.0.0.1:7001`）发送 OSC 消息，例如：
   - 地址：`/spatial/gains`
   - 参数：28 个 `float`（各扬声器增益）
2. **Meter 支持**：在 `backend_meter.py` 中增加一个 OSC 接收端，监听该端口；收到 `/spatial/gains` 时用这 28 个值更新 28 路竖条，而不是用本地模拟值。

当前仓库的 Backend 尚未实现增益 OSC 输出；若你加上上述发送逻辑，再在 Meter 里加上接收与刷新即可得到“真实增益 Meter”。

---

## 故障排查

- **Backend 收不到 OSC**  
  - 检查防火墙是否放行 UDP 7000  
  - 确认 Meter 或 Unity 的 OSC 目标为 `127.0.0.1:7000`

- **Meter 无界面 / 报错 tkinter**  
  - 安装 tk：`sudo apt install python3-tk`（Linux）或使用自带 Python 的 Windows/macOS

- **Meter 找不到 speakers.yaml**  
  - 在项目根执行：`python tools/backend_meter.py`  
  - 或显式指定：`python tools/backend_meter.py /path/to/speakers.yaml`
