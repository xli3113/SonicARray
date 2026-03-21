# JACK Output Run Verification

## Linux 调试流程（meterbridge 作为虚拟呈现）

用 meterbridge 做“虚拟扬声器”输出，便于在没有物理 28/29 声道时调试：

### 1. 启动 JACK

```bash
jackd -d alsa -r 44100 -p 1024 -n 2
# 或用 qjackctl 点 Start
```

### 2. 启动 SonicARray 后端

```bash
cd build
./SoundARray speakers.yaml
```

此时应出现 `SonicARrayBackend:spk_1` … `spk_N` 端口（`jack_lsp` 可查）。

### 3. 启动 meterbridge 作为虚拟呈现

```bash
# N=speakers.yaml 中的通道数（如 29）
N=29
meterbridge -c 7 -n meter -t dpm $(seq -f 'SonicARrayBackend:spk_%.0f' 1 $N)
```

- `-c 7`：7 列显示，可按屏宽调整
- `-t dpm`：数字峰值表（可改为 `ppm` / `vu`）
- `-n meter`：JACK 客户端名为 `meter`，输入端口为 `meter:input_1` … `meter:input_N`

### 4. 在 qjackctl Graph 中连接

1. 打开 qjackctl → Graph
2. 将 `SonicARrayBackend` 的每个 `spk_X` 拖到 `meter` 的 `input_X`
3. 或全选后批量连接

连接后，meterbridge 会显示各通道电平，相当于“虚拟扬声器阵列”的监听。

### 5. 命令行直连（不用 qjackctl Graph）

```bash
# 将 SonicARrayBackend 的所有输出连到 meterbridge
for i in $(seq 1 $N); do
  jack_connect SonicARrayBackend:spk_$i meter:input_$i
done
```

### 6. 无物理多声道时的建议

若系统只有 stereo，可用 meterbridge 代替 `system:playback_*`：
- 不连到 system，只连到 meter
- 通过 meterbridge 观察各通道是否出声、VBAP 路由是否正确

---

## 通道数来源

- **通道数完全来自 `speakers.yaml`**：`numChannels = speakers.size()`
- **绝无硬编码 28**：端口数量、buffer 大小均根据 YAML 动态确定

## 端口命名规则

- 端口名：`spk_<speaker.id>`，直接从 YAML 的 `speaker.id` 来
- 支持非连续 id（如 id=1..29 且可能缺号）
- 示例：`SonicARrayBackend:spk_1`, `SonicARrayBackend:spk_2`, ..., `SonicARrayBackend:spk_29`

---

## 1. 编译

```bash
mkdir -p build && cd build
cmake -DUSE_JACK=ON -DUSE_PORTAUDIO=OFF ..
cmake --build . -j
```

若使用 PortAudio 回退（无 JACK）：

```bash
cmake -DUSE_JACK=OFF ..
cmake --build . -j
```

---

## 2. 运行前准备

1. **启动 JACK server**（qjackctl 或命令行）：
   ```bash
   jackd -d alsa -r 44100 -p 1024 -n 2
   ```
   或通过 qjackctl 启动。

2. **确认 sample rate 一致**：JACK 与 `speakers.yaml` 预期采样率匹配（建议 44100 Hz）。

---

## 3. 验证端口

运行程序后，另开终端：

```bash
jack_lsp
```

应能看到：

```
SonicARrayBackend:spk_1
SonicARrayBackend:spk_2
...
SonicARrayBackend:spk_<N>
```

**N = speakers.yaml 中的 speaker 数量**（如 29），**不是固定 28**。

---

## 4. meterbridge 命令（N 可变）

meterbridge 的参数必须是**完整 JACK 端口名**（如 `SonicARrayBackend:spk_1`）：

```bash
# 生成端口名并启动 meterbridge（N=通道数，如 29）
meterbridge -c 7 -n meter -t dpm $(seq -f 'SonicARrayBackend:spk_%.0f' 1 N)
```

示例（29 通道，7 列）：

```bash
meterbridge -c 7 -n meter -t dpm $(seq -f 'SonicARrayBackend:spk_%.0f' 1 29)
```

启动后需在 qjackctl Graph 中将 `SonicARrayBackend:spk_*` 连接到 `meter:input_*`。

---

## 5. qjackctl / Carla 中连接

在 **qjackctl** 的 Graph：

1. 找到 `SonicARrayBackend`
2. 将其 `spk_1`..`spk_N` 拖到：
   - `system:playback_1`..`system:playback_N`（真实扬声器）
   - 或 `meterbridge:input_1`..`meterbridge:input_N`（仪表监控）

在 **Carla** 中同理，将 SonicARrayBackend 的输出端口连接到目标输入端。

---

## 6. 常见问题排查

| 现象 | 可能原因 | 处理 |
|------|----------|------|
| `jack_client_open fail` | JACK server 未启动 | 先启动 qjackctl 或 `jackd` |
| 端口未出现 | 程序未成功启动 / JACK 连接失败 | 检查终端报错，确认 JACK 已启动 |
| sample rate 不一致 | JACK 与程序期望采样率不同 | 统一 JACK 与 speakers 配置为 44100 |
| xruns | 缓冲区过小 / CPU 过载 | 增大 JACK 缓冲（如 2048）、关闭其他负载 |
| 无声但 meter 有信号 | 未连接到 system playback | 在 Graph 中连接到 `system:playback_*` |
| 声道接错 | 端口名与物理扬声器 id 不对应 | 按 `spk_<id>` 与物理布局对应连接 |

---

## 7. 最小 main 流程说明

1. 读取 `speakers.yaml` → `ConfigLoader::LoadSpeakers()`
2. `numChannels = speakers.size()`
3. 创建 `AudioEngine`，`Initialize(speakers, sampleRate)`
4. 内部创建 `JackAudioOutput`，`Initialize(speakers, sampleRate)` 注册 `spk_<id>` 端口
5. `Start()` 时设置 process callback → 调用 `ProcessAudioPlanar()`
6. 停止时 `Stop()` → `jack_deactivate` + `jack_client_close`
