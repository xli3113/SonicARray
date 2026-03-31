using System.Collections.Generic;
using UnityEngine;

public class SpatialSource : MonoBehaviour {
    // Palette used to auto-assign per-source colors (index = sourceId)
    public static readonly Color[] SourceColorPalette = new Color[] {
        new Color(1f, 0.3f, 0.3f),   // 0: red
        new Color(0.3f, 0.6f, 1f),   // 1: blue
        new Color(0.3f, 1f, 0.4f),   // 2: green
        new Color(1f, 0.8f, 0.1f),   // 3: yellow
        new Color(1f, 0.4f, 1f),     // 4: magenta
        new Color(0.3f, 1f, 1f),     // 5: cyan
        new Color(1f, 0.55f, 0.1f),  // 6: orange
        new Color(0.7f, 0.3f, 1f),   // 7: purple
    };

    // Backend hard limit — must match kMaxSources in VBAPRenderer.h
    public const int kMaxSources = 8;

    // Assigned in Awake(). Resets to 0 when all sources are destroyed (via SpeakerManager).
    private static int s_nextAutoId = 0;

    /// <summary>Called by SpeakerManager when the last source is destroyed, so the
    /// next ball created starts from ID 0 again instead of accumulating forever.</summary>
    public static void ResetIdCounter() { s_nextAutoId = 0; }

    // Debug counter: total OSC position packets sent across all sources
    public static int TotalOSCSent = 0;

    [Header("Source Identity")]
    [Tooltip("Assigned automatically at runtime (0, 1, 2 …). Read-only at runtime.")]
    public int sourceId = 0; // display only; overwritten in Awake()
    [Tooltip("Color for this source's lines and speaker contribution. " +
             "Leave as black to auto-pick from SourceColorPalette based on sourceId.")]
    public Color sourceColor = Color.black;

    [Header("OSC Settings")]
    public bool enableOSC = true; // 是否启用 OSC 发送（无后端时可禁用）
    public string oscIP = "192.168.137.1";
    public int oscPort = 7000;
    public float updateRate = 60.0f; // Updates per second

    [Header("Visual Settings")]
    public Material sourceMaterial;
    public float sourceScale = 0.15f;
    [HideInInspector] public Color lineColor = Color.red; // set from sourceColor in Start
    public float lineWidth = 0.02f;
    
    private OSCReceiver oscReceiver;  // single socket sends AND receives on port 7002
    private SpeakerManager speakerManager;
    private List<LineRenderer> lineRenderers = new List<LineRenderer>();
    private float lastUpdateTime = 0f;
    private Vector3 lastSentPosition = Vector3.zero;
    
    void Awake() {
        // Always overwrite serialized value. Wrap at kMaxSources so IDs never exceed
        // what the backend supports — prevents silent drop of all position updates.
        sourceId = s_nextAutoId % kMaxSources;
        s_nextAutoId++;
    }

    void Start() {
        // Assign color from palette based on the runtime sourceId set in Awake()
        if (sourceColor == Color.black) {
            int paletteIdx = sourceId % SourceColorPalette.Length;
            sourceColor = SourceColorPalette[paletteIdx];
        }
        lineColor = sourceColor;

        // Find SpeakerManager first so we can read its backendIP
        speakerManager = FindObjectOfType<SpeakerManager>();
        if (speakerManager == null) {
            Debug.LogError("SpeakerManager not found!");
        } else {
            // Register this source's color so speakers can tint by dominant source
            speakerManager.RegisterSourceColor(sourceId, sourceColor);
        }

        // Use OSCReceiver's single socket (port 7002) for sending so that
        // outgoing packets leave from quest:7002 → pc:7000, creating the conntrack
        // entry that allows pc:7000 → quest:7002 replies through Android's firewall.
        if (enableOSC) {
            oscReceiver = FindObjectOfType<OSCReceiver>();
            if (oscReceiver == null)
                Debug.LogError("[SpatialSource] OSCReceiver not found — no OSC sending");
            else
                Debug.Log($"[SpatialSource] OSC src={sourceId} via OSCReceiver socket :{oscReceiver.listenPort}");
        } else {
            Debug.Log("[SpatialSource] OSC disabled");
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
                // Instance the material so each source gets its own color
                renderer.material = new Material(sourceMaterial);
                renderer.material.color = sourceColor;
            } else {
                // Default to this source's palette color
                Material defaultMat = new Material(Shader.Find("Standard"));
                defaultMat.color = sourceColor;
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
        
        // Mouse drag is handled centrally by HandGestureController (PC fallback mode)
    }
    
    void Update() {
        // Send OSC position to backend
        float timeSinceLastUpdate = Time.time - lastUpdateTime;
        if (timeSinceLastUpdate >= 1.0f / updateRate) {
            SendOSCPosition();
            lastUpdateTime = Time.time;
        }

        // Draw lines to active speakers using gains from backend (via SpeakerManager)
        UpdateLineRenderers();
    }
    
    void SendOSCPosition() {
        if (!enableOSC || oscReceiver == null) return;

        // Use the speaker array center as the VBAP listener reference.
        // The C++ backend treats YAML (0,0,0) as the listener; speaker positions
        // are in that frame. Computing rel from the array center (SpeakerManager)
        // keeps directions correct regardless of arrayOriginOffset or camera position.
        Vector3 listenerPos = speakerManager != null
            ? speakerManager.transform.position
            : Vector3.zero;
        Vector3 rel = transform.position - listenerPos;
        // Swap y and z: Unity y=height,z=forward → C++ y=forward,z=height
        // Sends FROM port 7002 (OSCReceiver socket) → creates conntrack entry
        oscReceiver.SendOSC("/spatial/source_pos", sourceId, rel.x, rel.z, rel.y);
        TotalOSCSent = OSCReceiver.TotalOSCSent; // mirror the shared counter

        #if UNITY_EDITOR
        Vector3 pos = transform.position;
        float moved = Vector3.Distance(pos, lastSentPosition);
        Debug.Log($"[OSC SEND] rel=({rel.x:F3}, {rel.z:F3}, {rel.y:F3})  moved={moved:F4}m  target={oscIP}:{oscPort}");
        lastSentPosition = pos;
        #endif
    }
    
    void UpdateLineRenderers() {
        if (speakerManager == null) return;

        List<SpeakerData> speakers = speakerManager.GetSpeakers();
        if (speakers == null || speakers.Count == 0) return;

        // Use THIS source's own gains only — not the merged max across all sources.
        // Merged gains would show speakers activated by other balls too, breaking the
        // "exactly 3 speakers per source" visual and making multi-source display wrong.
        List<int> activeSpeakerIds = new List<int>();
        foreach (SpeakerData spk in speakers) {
            if (speakerManager.GetSpeakerGainForSource(spk.id, sourceId) > 0.01f) {
                activeSpeakerIds.Add(spk.id);
            }
        }

        #if UNITY_EDITOR
        if (activeSpeakerIds.Count > 0) {
            var gainStrs = new System.Text.StringBuilder();
            foreach (int sid in activeSpeakerIds)
                gainStrs.Append($"spk{sid}={speakerManager.GetSpeakerGainForSource(sid, sourceId):F3}  ");
            Debug.Log($"[src{sourceId}] Lines={activeSpeakerIds.Count} gains: {gainStrs}");
        }
        #endif

        // Sync line renderer count
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
        while (lineRenderers.Count > activeSpeakerIds.Count) {
            LineRenderer lr = lineRenderers[lineRenderers.Count - 1];
            lineRenderers.RemoveAt(lineRenderers.Count - 1);
            Destroy(lr.gameObject);
        }

        // Draw lines to active speakers; width reflects backend gain
        Vector3 sourcePos = transform.position;
        for (int i = 0; i < activeSpeakerIds.Count; ++i) {
            int speakerId = activeSpeakerIds[i];
            SpeakerData speaker = speakers.Find(s => s.id == speakerId);
            if (speaker == null) continue;

            float gain = speakerManager.GetSpeakerGainForSource(speakerId, sourceId);
            LineRenderer lr = lineRenderers[i];
            lr.SetPosition(0, sourcePos);
            lr.SetPosition(1, speakerManager.GetSpeakerWorldPosition(speakerId));
            float width = lineWidth * (0.5f + gain * 0.5f);
            lr.startWidth = width;
            lr.endWidth = width * 0.5f;
        }
    }
    
    void OnDestroy() {
        // Tell backend this source no longer exists (position 0,0,0 = inactive)
        if (enableOSC && oscReceiver != null)
            oscReceiver.SendOSC("/spatial/source_pos", sourceId, 0f, 0f, 0f);

        if (speakerManager != null)
            speakerManager.ClearSourceGains(sourceId);
    }
}
