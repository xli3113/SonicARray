using System.Collections.Generic;
using UnityEngine;

public class SpatialSource : MonoBehaviour {
    [Header("OSC Settings")]
    public bool enableOSC = false; // 是否启用 OSC 发送（无后端时可禁用）
    public string oscIP = "127.0.0.1";
    public int oscPort = 7000;
    public float updateRate = 60.0f; // Updates per second
    
    [Header("Visual Settings")]
    public Material sourceMaterial;
    public float sourceScale = 0.15f;
    public Color lineColor = Color.red;
    public float lineWidth = 0.02f;
    
    private OSCClient oscClient;
    private SpeakerManager speakerManager;
    private List<LineRenderer> lineRenderers = new List<LineRenderer>();
    private List<int> activeSpeakerIds = new List<int>();
    private float lastUpdateTime = 0f;
    
    void Start() {
        // Initialize OSC client (only if enabled)
        if (enableOSC) {
            oscClient = new OSCClient(oscIP, oscPort);
            #if UNITY_EDITOR
            Debug.Log($"[SpatialSource] OSC 已启用，目标: {oscIP}:{oscPort}");
            #endif
        } else {
            #if UNITY_EDITOR
            Debug.Log("[SpatialSource] OSC 已禁用（无后端模式）");
            #endif
        }
        
        // Find SpeakerManager
        speakerManager = FindObjectOfType<SpeakerManager>();
        if (speakerManager == null) {
            Debug.LogError("SpeakerManager not found!");
        }
        
        // Setup visual
        SetupVisual();
        
        // Setup grab interaction (Meta XR SDK)
        SetupGrabInteraction();
    }
    
    void SetupVisual() {
        // Create sphere for source
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "SourceVisual";
        sphere.transform.SetParent(transform);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale = Vector3.one * sourceScale;
        
        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null) {
            if (sourceMaterial != null) {
                renderer.material = sourceMaterial;
            } else {
                // 如果没有设置材质，创建默认黄色材质
                Material defaultMat = new Material(Shader.Find("Standard"));
                defaultMat.color = new Color(1f, 0.8f, 0.2f); // 黄色
                renderer.material = defaultMat;
                
                #if UNITY_EDITOR
                Debug.LogWarning("[SpatialSource] Source Material 未设置，使用默认黄色材质");
                #endif
            }
        }
        
        #if UNITY_EDITOR
        Debug.Log($"[SpatialSource] 声源小球已创建，位置: {transform.position}, 大小: {sourceScale}");
        #endif
        
        // Keep collider for interaction (will be used by grab system)
        // Don't destroy it here
    }
    
    void SetupGrabInteraction() {
        // Add collider for grabbing
        SphereCollider grabCollider = gameObject.AddComponent<SphereCollider>();
        grabCollider.radius = sourceScale * 1.5f;
        grabCollider.isTrigger = false; // Need non-trigger for physics interaction
        
        // Add Rigidbody for physics interaction
        Rigidbody rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true; // We'll control movement manually
        
        // Add Meta XR grab component
        // Uncomment and adjust based on your Meta XR SDK version:
        
        // For Meta XR SDK v50+:
        // #if UNITY_ANDROID && !UNITY_EDITOR
        // using Meta.XR.InteractionSystem;
        // var grabInteractable = gameObject.AddComponent<GrabInteractable>();
        // #endif
        
        // For older Meta XR SDK:
        // #if UNITY_ANDROID && !UNITY_EDITOR
        // using Oculus.Interaction;
        // var grabInteractable = gameObject.AddComponent<GrabInteractable>();
        // #endif
        
        // For testing in Editor, you can use simple mouse drag:
        #if UNITY_EDITOR
        gameObject.AddComponent<SimpleDrag>();
        #endif
    }
    
    void Update() {
        // Send OSC position
        float timeSinceLastUpdate = Time.time - lastUpdateTime;
        if (timeSinceLastUpdate >= 1.0f / updateRate) {
            SendOSCPosition();
            lastUpdateTime = Time.time;
        }
        
        // Update visual feedback
        UpdateVisualFeedback();
    }
    
    void SendOSCPosition() {
        if (!enableOSC || oscClient == null) return;
        
        Vector3 pos = transform.position;
        oscClient.Send("/spatial/source_pos", pos.x, pos.y, pos.z);
        
        // 调试日志（仅在 Editor 中显示，且仅在启用详细日志时）
        #if UNITY_EDITOR && false  // 设置为 true 可启用详细 OSC 日志
        Debug.Log($"[OSC] 发送位置: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
        #endif
    }
    
    void UpdateVisualFeedback() {
        if (speakerManager == null) return;
        
        List<SpeakerData> speakers = speakerManager.GetSpeakers();
        if (speakers == null || speakers.Count == 0) return;
        
        // Calculate distances and find closest 3 speakers
        List<(int id, float distance, float gain)> speakerDistances = new List<(int, float, float)>();
        
        Vector3 sourcePos = transform.position;
        
        foreach (SpeakerData speaker in speakers) {
            Vector3 speakerPos = new Vector3(speaker.x, speaker.y, speaker.z);
            float distance = Vector3.Distance(sourcePos, speakerPos);
            
            // Calculate gain based on distance (inverse square law approximation)
            float gain = 1.0f / (1.0f + distance * distance);
            
            speakerDistances.Add((speaker.id, distance, gain));
        }
        
        // Sort by distance
        speakerDistances.Sort((a, b) => a.distance.CompareTo(b.distance));
        
        // Get top 3
        List<int> newActiveSpeakers = new List<int>();
        for (int i = 0; i < Mathf.Min(3, speakerDistances.Count); ++i) {
            newActiveSpeakers.Add(speakerDistances[i].id);
        }
        
        // Update highlights and speaker states
        bool speakersChanged = !ListsEqual(activeSpeakerIds, newActiveSpeakers);
        
        if (speakersChanged) {
            // Clear old highlights
            foreach (int id in activeSpeakerIds) {
                speakerManager.UpdateSpeakerState(id, 0.0f);
            }
            
            // Set new highlights
            activeSpeakerIds = newActiveSpeakers;
        }
        
        // Update all speaker states with gains
        for (int i = 0; i < speakerDistances.Count; ++i) {
            int speakerId = speakerDistances[i].id;
            float gain = speakerDistances[i].gain;
            speakerManager.UpdateSpeakerState(speakerId, gain);
        }
        
        // Update line renderers
        UpdateLineRenderers();
    }
    
    void UpdateLineRenderers() {
        // Ensure we have enough line renderers
        while (lineRenderers.Count < activeSpeakerIds.Count) {
            GameObject lineObj = new GameObject($"Line_{lineRenderers.Count}");
            lineObj.transform.SetParent(transform);
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lineColor;
            lr.endColor = lineColor;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lineRenderers.Add(lr);
        }
        
        // Remove excess line renderers
        while (lineRenderers.Count > activeSpeakerIds.Count) {
            LineRenderer lr = lineRenderers[lineRenderers.Count - 1];
            lineRenderers.RemoveAt(lineRenderers.Count - 1);
            Destroy(lr.gameObject);
        }
        
        // Update line positions and widths
        Vector3 sourcePos = transform.position;
        List<SpeakerData> speakers = speakerManager.GetSpeakers();
        
        for (int i = 0; i < activeSpeakerIds.Count; ++i) {
            int speakerId = activeSpeakerIds[i];
            SpeakerData speaker = speakers.Find(s => s.id == speakerId);
            
            if (speaker != null) {
                Vector3 speakerPos = new Vector3(speaker.x, speaker.y, speaker.z);
                float distance = Vector3.Distance(sourcePos, speakerPos);
                float gain = 1.0f / (1.0f + distance * distance);
                
                LineRenderer lr = lineRenderers[i];
                lr.SetPosition(0, sourcePos);
                lr.SetPosition(1, speakerPos);
                
                // Adjust width based on gain
                float width = lineWidth * (0.5f + gain * 0.5f);
                lr.startWidth = width;
                lr.endWidth = width * 0.5f;
            }
        }
    }
    
    bool ListsEqual(List<int> list1, List<int> list2) {
        if (list1.Count != list2.Count) return false;
        for (int i = 0; i < list1.Count; ++i) {
            if (list1[i] != list2[i]) return false;
        }
        return true;
    }
    
    void OnDestroy() {
        if (oscClient != null) {
            oscClient.Close();
        }
    }
}
