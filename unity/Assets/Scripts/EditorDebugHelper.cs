using UnityEngine;

/// <summary>
/// Unity Editor 调试辅助工具
/// 在没有 Quest 的情况下，帮助测试和调试
/// </summary>
public class EditorDebugHelper : MonoBehaviour {
    [Header("调试设置")]
    public bool enableDebugLogs = true;
    public bool showGizmos = true;
    public Color gizmoColor = Color.yellow;
    
    [Header("自动测试")]
    public bool autoMoveSource = false;
    public float moveSpeed = 1.0f;
    public Vector3 moveDirection = new Vector3(1, 0, 0);
    public float moveRange = 2.0f;
    
    private Vector3 startPosition;
    private SpatialSource spatialSource;
    private float moveTime = 0f;
    
    void Start() {
        spatialSource = FindObjectOfType<SpatialSource>();
        if (spatialSource != null) {
            startPosition = spatialSource.transform.position;
        }
        
        if (enableDebugLogs) {
            Debug.Log("[EditorDebugHelper] 调试模式已启用");
            Debug.Log("[EditorDebugHelper] 提示：在 Scene 视图中选择 SpatialSource 并移动它来测试");
        }
    }
    
    void Update() {
        if (autoMoveSource && spatialSource != null) {
            // 自动移动声源（用于测试）
            moveTime += Time.deltaTime * moveSpeed;
            Vector3 offset = moveDirection.normalized * Mathf.Sin(moveTime) * moveRange;
            spatialSource.transform.position = startPosition + offset;
        }
    }
    
    void OnDrawGizmos() {
        if (!showGizmos || spatialSource == null) return;
        
        // 绘制声源位置
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(spatialSource.transform.position, 0.2f);
        
        // 绘制移动范围（如果启用自动移动）
        if (autoMoveSource) {
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.3f);
            Gizmos.DrawLine(
                startPosition - moveDirection.normalized * moveRange,
                startPosition + moveDirection.normalized * moveRange
            );
        }
    }
    
    [ContextMenu("测试 OSC 发送")]
    void TestOSCSend() {
        if (spatialSource == null) {
            Debug.LogError("未找到 SpatialSource");
            return;
        }
        
        Vector3 pos = spatialSource.transform.position;
        Debug.Log($"[测试] 发送 OSC 位置: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
        
        // 触发一次 OSC 发送
        spatialSource.SendMessage("SendOSCPosition", SendMessageOptions.DontRequireReceiver);
    }
    
    [ContextMenu("重置声源位置")]
    void ResetSourcePosition() {
        if (spatialSource != null) {
            spatialSource.transform.position = new Vector3(0, 1.5f, 2f);
            Debug.Log("[测试] 声源位置已重置");
        }
    }
    
    [ContextMenu("打印所有扬声器状态")]
    void PrintAllSpeakerStates() {
        SpeakerManager manager = FindObjectOfType<SpeakerManager>();
        if (manager == null) {
            Debug.LogError("未找到 SpeakerManager");
            return;
        }
        
        Debug.Log("=== 扬声器状态 ===");
        // 这里可以添加更多调试信息
    }
}
