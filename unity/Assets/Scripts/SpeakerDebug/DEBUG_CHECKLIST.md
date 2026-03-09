# SpeakerDebug 快速验收 Checklist 与排错表

## 一、修改文件清单

| 文件 | 修改原因 |
|------|----------|
| **SimpleUI.cs** | 1) gainProvider 使用 `FindObjectOfType<MockGainProvider>()` 不再 cast 成 IGainProvider，避免类型错误<br>2) SetGainMode/SetPaused 直接操作 MockGainProvider<br>3) 录制时排除回放状态 `!_isPlayingBack`<br>4) 增加 SetSmoothTau，录制默认 10s/30Hz |
| **GainInterfaces.cs** | 1) attack/release 改为 smoothTau，指数平滑 alpha=1-exp(-dt/tau) |
| **SpeakerVisualizer.cs** | 1) 抽 Rebuild() 供 Bootstrap 在 wiring 后调用<br>2) emission 仅在 HasProperty 时 EnableKeyword（修反逻辑）<br>3) 平滑使用 smoothTau + alpha 公式<br>4) 增加 useSqrtScale 可选 sqrt 映射 |
| **SpeakerDebugBootstrap.cs** | 1) Start→Awake，确保先于其他 Start 执行<br>2) wiring 后调用 visualizer.Rebuild() |
| **RoomAnchorManager.cs** | 1) 增加 AnchorState 状态机与 State/StateMessage<br>2) 各流程 SetState 记录状态与失败原因<br>3) 本地化失败时提示“缓慢环顾或重新创建” |
| **SpeakerYamlLoader.cs** | 1) LogLoadResult 打印条目数、id 范围、xyz min/max<br>2) ResolvePath 支持 StreamingAssets + persistentDataPath<br>3) 主文件解析失败时 try 备选扩展名 (.json/.yaml) |
| **AnchorStatusDisplay.cs** | 新建，在 UI Text 上显示锚点状态 |
| **CreateSpeakerDebugScene.cs** | 增加 StatusText + AnchorStatusDisplay |

---

## 二、快速验收 Checklist

### Editor（FakeAnchor，无后端）
- [ ] **SingleScan**：球/立方体按 id 轮播点亮，顺序可见
- [ ] **移动 FakeAnchor**：整体扬声器跟随
- [ ] **SineBreath**：全体同步呼吸
- [ ] **RandomPulse**：固定 seed 可复现
- [ ] **Hotspot**：调整 virtualSourcePosition，距离衰减可见

### 真机（OVRSpatialAnchor）
- [ ] **Create & Save**：点按钮创建锚点，控制台打印 UUID、保存成功
- [ ] **重启**：自动 Load & Localize，speakers 贴在锚点
- [ ] **UI 状态**：StatusText 显示 Ready / Creating / Failed 等

### 录制/回放
- [ ] **录制**：Record → 若干秒 → Stop，Console 打印帧数
- [ ] **回放**：Playback 时完全忽略实时 provider，仅用录制数据
- [ ] **导出/导入**：SaveRecordingToFile → 重启 → LoadRecordingFromFile，回放一致

---

## 三、常见报错排查路径

| 报错/现象 | 可能原因 | 排查步骤 |
|-----------|----------|----------|
| `OVR` / `OVRSpatialAnchor` 不存在 | Meta XR SDK 未装或宏未定义 | 1) 安装 Meta XR Core SDK<br>2) Project Settings > Scripting Define Symbols 添加 `META_XR_SDK_PRESENT`<br>3) 或保持不定义，使用 FakeAnchor 调试 |
| 扬声器不显示 | 1) yaml 解析 0 条<br>2) Visualizer 未 init | 1) 检查 Console 是否打印 "解析到 0 个扬声器"<br>2) 确认 StreamingAssets/speakers.yaml 或 speakers.json 存在<br>3) 确认 Bootstrap 在 Awake 中调用 visualizer.Rebuild()<br>4) 检查 spawner.Instances.Count > 0 |
| Emission 不亮 | 1) 材质无 _EmissionColor<br>2) 启用逻辑写反 | 1) 使用 Standard 材质并确认支持 Emission<br>2) 仅在 HasProperty("_EmissionColor") 时 EnableKeyword("_EMISSION") |
| 锚点权限错误 | Quest 未开启 Anchor 权限 | OVRManager > Quest Features > Anchor Support 勾选；AndroidManifest 加 `com.oculus.permission.USE_ANCHOR_API` |
| 本地化失败 | 环境未扫描到锚点区域 | 1) 缓慢环顾房间<br>2) 删除 UUID 后重新 Create & Save |
| 宏走错分支 | META_XR_SDK_PRESENT 与 SDK 安装状态不一致 | Editor 调试可不定义宏，强制走 FakeAnchor；真机需安装 SDK 并定义宏 |
| YAML 解析为空 | 格式不匹配或路径错误 | 1) 使用 speakers.json 作为替代<br>2) 检查 LogLoadResult 输出的路径<br>3) 支持 StreamingAssets 与 persistentDataPath |

---

## 四、Editor 强制 FakeAnchor

- 默认 `RoomAnchorManager.useFakeAnchorInEditor = true`
- 在 Editor 中不定义 `META_XR_SDK_PRESENT` 时，始终走 FakeAnchor 分支
- FakeAnchor 可在 Hierarchy 中拖动，验证几何与可视化
