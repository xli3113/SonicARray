using UnityEngine;

/// <summary>
/// Meta Quest 手柄输入控制器。
/// 依赖 OVRInput（com.meta.xr.sdk.core），场景中须有 OVRCameraRig 或 OVRManager。
///
/// 按键映射：
///   右手 A 键          → 在头部前方创建新声源
///   右手 B 键          → 删除当前选中声源
///   右摇杆 按下        → 循环切换选中声源
///   右摇杆 X/Y 轴      → 在水平面内移动选中声源（头朝向相对）
///   左摇杆 Y 轴        → 垂直移动选中声源（上下）
///   左手 X 键          → 同 A 键，备用创建绑定
///
/// Editor 降级：
///   Inspector 中启用 useKeyboardFallback 后可用键盘/鼠标测试：
///   Space=创建，Delete=删除，Tab=切换，WASD=水平移动，QE=上下
/// </summary>
public class XRSourceController : MonoBehaviour {

    // ──────────────────────────────────────────────────────────────────
    // Inspector 字段
    // ──────────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("多声源管理器，留空则自动查找")]
    public SourceManager sourceManager;
    [Tooltip("XR 头部 Transform（通常是 OVRCameraRig/CenterEyeAnchor），留空则用 Camera.main")]
    public Transform xrHead;

    [Header("移动参数")]
    [Tooltip("水平移动速度（米/秒）")]
    public float horizontalSpeed = 1.5f;
    [Tooltip("垂直移动速度（米/秒）")]
    public float verticalSpeed = 0.8f;
    [Tooltip("摇杆死区 [0,1]")]
    [Range(0f, 0.5f)]
    public float deadzone = 0.15f;

    [Header("创建参数")]
    [Tooltip("创建时声源距头部的距离（米）")]
    public float spawnDistance = 1.2f;
    [Tooltip("创建时额外的 Y 偏移（米），0 = 与眼睛等高")]
    public float spawnHeightOffset = 0f;

    [Header("Editor 降级（键盘）")]
    public bool useKeyboardFallback = true;

    // ──────────────────────────────────────────────────────────────────
    // 内部状态（按键边缘检测）
    // ──────────────────────────────────────────────────────────────────

    private bool _prevA, _prevB, _prevX, _prevRThumb;

    // ──────────────────────────────────────────────────────────────────
    // Unity 生命周期
    // ──────────────────────────────────────────────────────────────────

    void Start() {
        if (sourceManager == null)
            sourceManager = FindObjectOfType<SourceManager>();

        if (sourceManager == null)
            Debug.LogError("[XRSourceController] 未找到 SourceManager！请在场景中添加并配置 SourceManager。");

        if (xrHead == null && Camera.main != null)
            xrHead = Camera.main.transform;
    }

    void Update() {
#if !UNITY_EDITOR
        HandleOVRButtons();
        HandleOVRThumbsticks();
#else
        // Editor：同时支持 OVR（接真机）和键盘降级
        if (IsOVRAvailable()) {
            HandleOVRButtons();
            HandleOVRThumbsticks();
        }
        if (useKeyboardFallback) {
            HandleKeyboardFallback();
        }
#endif
    }

    // ──────────────────────────────────────────────────────────────────
    // OVR 手柄输入
    // ──────────────────────────────────────────────────────────────────

    void HandleOVRButtons() {
        // ── 右手 ──────────────────────────────────────────
        // A 键：创建声源
        bool a = OVRInput.Get(OVRInput.RawButton.A);
        if (a && !_prevA) DoCreateSource();
        _prevA = a;

        // B 键：删除选中声源
        bool b = OVRInput.Get(OVRInput.RawButton.B);
        if (b && !_prevB) DoDeleteSource();
        _prevB = b;

        // 右摇杆按下：循环切换选中声源
        bool rThumb = OVRInput.Get(OVRInput.RawButton.RThumbstick);
        if (rThumb && !_prevRThumb) DoCycleSelection();
        _prevRThumb = rThumb;

        // ── 左手 ──────────────────────────────────────────
        // X 键：备用创建
        bool x = OVRInput.Get(OVRInput.RawButton.X);
        if (x && !_prevX) DoCreateSource();
        _prevX = x;
    }

    void HandleOVRThumbsticks() {
        if (sourceManager == null) return;
        var src = sourceManager.SelectedSource;
        if (src == null) return;

        // 右摇杆：XZ 平面（相机朝向相对）
        Vector2 rStick = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
        // 左摇杆 Y：垂直
        Vector2 lStick = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick);

        ApplyMovement(src, rStick, lStick.y);
    }

    // ──────────────────────────────────────────────────────────────────
    // 键盘降级（仅 Editor）
    // ──────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    void HandleKeyboardFallback() {
        if (sourceManager == null) return;

        // 创建 / 删除 / 切换
        if (Input.GetKeyDown(KeyCode.Space))  DoCreateSource();
        if (Input.GetKeyDown(KeyCode.Delete)) DoDeleteSource();
        if (Input.GetKeyDown(KeyCode.Tab))    DoCycleSelection();

        var src = sourceManager.SelectedSource;
        if (src == null) return;

        // WASD：水平移动（使用模拟摇杆值）
        float h = 0f, v = 0f, vert = 0f;
        if (Input.GetKey(KeyCode.D)) h =  1f;
        if (Input.GetKey(KeyCode.A)) h = -1f;
        if (Input.GetKey(KeyCode.W)) v =  1f;
        if (Input.GetKey(KeyCode.S)) v = -1f;
        if (Input.GetKey(KeyCode.E)) vert =  1f;
        if (Input.GetKey(KeyCode.Q)) vert = -1f;

        ApplyMovement(src, new Vector2(h, v), vert);
    }

    static bool IsOVRAvailable() {
        // 检查 OVRInput 是否有有效控制器（在 Editor + Link 时为 true）
        try {
            return OVRInput.GetConnectedControllers() != OVRInput.Controller.None;
        } catch {
            return false;
        }
    }
#endif

    // ──────────────────────────────────────────────────────────────────
    // 共享逻辑
    // ──────────────────────────────────────────────────────────────────

    void ApplyMovement(SpatialSource src, Vector2 horizontal, float verticalAxis) {
        Vector3 delta = Vector3.zero;

        // 水平移动（相机朝向相对）
        if (horizontal.magnitude > deadzone && xrHead != null) {
            Vector3 fwd = xrHead.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            Vector3 right = xrHead.right;
            right.y = 0f;
            right.Normalize();

            delta += right * (horizontal.x * horizontalSpeed * Time.deltaTime);
            delta += fwd  * (horizontal.y * horizontalSpeed * Time.deltaTime);
        }

        // 垂直移动
        if (Mathf.Abs(verticalAxis) > deadzone) {
            delta += Vector3.up * (verticalAxis * verticalSpeed * Time.deltaTime);
        }

        if (delta != Vector3.zero)
            src.transform.position += delta;
    }

    void DoCreateSource() {
        if (sourceManager == null) return;

        Vector3 pos;
        if (xrHead != null) {
            pos = xrHead.position
                + xrHead.forward * spawnDistance
                + Vector3.up * spawnHeightOffset;
        } else {
            pos = Vector3.zero;
        }

        var created = sourceManager.CreateSource(pos);
        if (created == null) {
            // 已达上限，给玩家一个轻微的触觉反馈提示
#if !UNITY_EDITOR
            OVRInput.SetControllerVibration(0.3f, 0.3f, OVRInput.Controller.RTouch);
#endif
        }
    }

    void DoDeleteSource() {
        if (sourceManager == null) return;
        sourceManager.DeleteSelectedSource();
    }

    void DoCycleSelection() {
        if (sourceManager == null) return;
        sourceManager.CycleSelection();
    }
}
