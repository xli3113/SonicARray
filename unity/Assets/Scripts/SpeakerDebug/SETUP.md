# SpeakerDebug 可视化调试系统 - 搭建说明

## 一、文件列表

| 文件 | 说明 |
|------|------|
| DataModels.cs | SpeakerEntry, TransformOffsetData, AnchorLocalConfig, GainRecording 等 |
| GainInterfaces.cs | IGainProvider, MockGainProvider, PlaybackGainProvider |
| SpeakerYamlLoader.cs | 解析 speakers.yaml / speakers.json |
| SpeakerSpawner.cs | 在 SpeakerRig 下生成虚拟扬声器 |
| RoomAnchorManager.cs | OVRSpatialAnchor + FakeAnchor 降级 |
| SpeakerVisualizer.cs | gain 驱动颜色 / Emission / 缩放 |
| SimpleUI.cs | UI 回调：锚点、模式、录制/回放 |
| IAnchorShareTransport.cs | SSA 传输接口 + Stub |
| SpeakerDebugBootstrap.cs | 启动串联与位置注入 |
| speakers.json | 示例 JSON（与 speakers.yaml 等价） |

## 二、GameObject 层级与组件

```
Scene
├── SpeakerDebugBootstrap          [SpeakerDebugBootstrap]
├── RoomAnchor                     [RoomAnchorManager, Transform]
│   └── FakeAnchor (可选)          [Transform，Editor 时作为虚拟锚点]
│   └── SpeakerRig                 [Transform]
│       └── Speaker_0, Speaker_1... (由 SpeakerSpawner 生成)
├── SpeakerLoader                  [SpeakerYamlLoader]
├── SpeakerSpawner                [SpeakerSpawner] ← speakerRigParent = SpeakerRig
├── MockGainProvider              [MockGainProvider]
├── SpeakerVisualizer             [SpeakerVisualizer]
├── SimpleUI                      [SimpleUI]
└── UICanvas                      [Canvas]
    └── Panel
        ├── BtnCreateAnchor       [Button] → SimpleUI.OnCreateAndSaveAnchor
        ├── BtnLoadAnchor         [Button] → SimpleUI.OnLoadAnchor
        ├── BtnDeleteUuid         [Button] → SimpleUI.OnDeleteLocalUuid
        ├── BtnSaveOffset         [Button] → SimpleUI.OnSaveTransformOffset
        ├── BtnRecord             [Button] → SimpleUI.OnStartRecording
        ├── BtnStopRecord         [Button] → SimpleUI.OnStopRecording
        ├── BtnPlayback           [Button] → SimpleUI.OnStartPlayback
        ├── BtnStopPlayback       [Button] → SimpleUI.OnStopPlayback
        ├── DropdownMode          [Dropdown] 0=SingleScan, 1=SineBreath, 2=RandomPulse, 3=DirectionalHotspot
        └── SliderIntensity      [Slider] → SimpleUI.SetIntensity
```

## 三、组件挂载关系

| GameObject | 组件 | 必填字段 |
|------------|------|----------|
| SpeakerDebugBootstrap | SpeakerDebugBootstrap | loader, spawner, anchorManager, gainProvider, visualizer, simpleUI |
| RoomAnchor | RoomAnchorManager | roomAnchorTransform=自身, speakerRigParent=SpeakerRig, fakeAnchorTransform=FakeAnchor |
| SpeakerLoader | SpeakerYamlLoader | fileName=speakers.yaml 或 speakers.json |
| SpeakerSpawner | SpeakerSpawner | loader, speakerRigParent=SpeakerRig |
| MockGainProvider | MockGainProvider | speakerCount=28 |
| SpeakerVisualizer | SpeakerVisualizer | gainProvider, spawner |
| SimpleUI | SimpleUI | anchorManager, gainProvider, visualizer |

## 四、Project Settings 勾选清单

- **XR Plug-in Management**：启用 Oculus / OpenXR，Android 勾选 Oculus Quest
- **Player > Android**：Minimum API Level 29+，Scripting Backend IL2CPP，Target Architectures ARM64
- **Meta XR（若已装）**：OVRManager > Quest Features > Anchor Support 勾选
- **Scripting Define**：如需真实锚点，添加 `META_XR_SDK_PRESENT`（安装 Meta XR Core SDK 后）

**添加 Meta XR Core SDK**：Window > Package Manager > + > Add package by name，填入 `com.meta.xr.sdk.core`；或从 Meta 开发者站获取最新包地址。

## 五、无头显 Editor 降级

- `RoomAnchorManager.useFakeAnchorInEditor = true`（默认）
- 将 `FakeAnchor` 子物体拖到 `fakeAnchorTransform`
- 在 Editor Play 时无 OVRSpatialAnchor，自动用 FakeAnchor 的 Transform，可手动拖动调整
- 扬声器可视化与 MockGainProvider 正常工作

## 六、常见错误排查

| 现象 | 可能原因 | 处理 |
|------|----------|------|
| 解析 0 个扬声器 | 路径错误或格式不对 | 确认 StreamingAssets/speakers.yaml 或 .json 存在；JSON 格式为 `{"speakers":[...]}` |
| SpeakerRig 下无物体 | Spawner 的 loader 未赋值 | 在 Bootstrap 或 Inspector 中绑定 loader |
| 无 gain 可视化 | gainProvider 未赋给 visualizer | Bootstrap 或手动绑定 |
| 锚点创建/加载失败 | 未装 Meta XR SDK 或未定义 META_XR_SDK_PRESENT | 使用 FakeAnchor 或安装 SDK 并添加 Define |
| UI 按钮无反应 | 未绑定 OnClick | 在 Button 的 On Click 中拖入 SimpleUI，选对应方法 |
