using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Ray-casting hand interaction — aims a ray from the hand/finger forward direction.
/// Works exactly like a controller laser pointer but with hands.
///
/// RIGHT HAND:
///   Aim ray at empty space, pinch and hold 0.5 s  → create source at ray endpoint
///   Aim ray at existing source, pinch             → grab and drag (maintains distance)
///
/// LEFT HAND:
///   Aim ray at existing source, pinch and release → delete source
///
/// BOTH HANDS simultaneously pinch + hold 3 s     → calibrate speaker array to head
///   Stand at the reference speaker (the one with the cross sticker), then hold the
///   gesture until "CALIBRATED" appears. The virtual array snaps to your position.
///
/// Visual ray: white when idle, green when hovering a source, yellow when creating
/// </summary>
public class HandGestureController : MonoBehaviour
{
    [Header("Controller / CameraRig")]
    public OVRCameraRig cameraRig;

    [Header("Source Settings")]
    public GameObject spatialSourcePrefab;
    [Tooltip("Default ray length when no source is hit (metres)")]
    public float defaultRayLength = 2.0f;
    [Tooltip("Hold time (seconds) before a source is created")]
    public float createHoldTime = 0.5f;
    [Tooltip("Max perpendicular distance from ray to count as a hit (metres)")]
    public float rayHitRadius = 0.12f;

    [Header("Ray Length Control")]
    [Tooltip("Minimum ray length in metres")]
    public float minRayLength = 0.5f;
    [Tooltip("Maximum ray length in metres")]
    public float maxRayLength = 8.0f;
    [Tooltip("Left-hand vertical sensitivity for depth adjustment (m/m)")]
    public float depthSensitivity = 4.0f;

    [Header("Calibration")]
    [Tooltip("Speaker ID that has the cross sticker — must match calibrationReferenceSpeakerId in SpeakerManager")]
    public int calibrationReferenceSpeakerId = 1;
    [Tooltip("Seconds both hands must pinch to trigger calibration")]
    public float calibrationHoldTime = 3.0f;

    [Header("Visual Feedback")]
    public GameObject createIndicatorPrefab;

    // kept public for debug UI
    [HideInInspector] public OVRHand rightHand;
    [HideInInspector] public OVRHand leftHand;
    [HideInInspector] public OVRSkeleton rightHandSkeleton;
    [HideInInspector] public OVRSkeleton leftHandSkeleton;

    // ── internal ───────────────────────────────────────────────────
    private List<GameObject> allSources = new List<GameObject>();

    // GUI read-outs
    public int   SourceCount => allSources.Count;
    public float RayDepth    => defaultRayLength;

    // Right hand state
    private bool  rPinching          = false;
    private bool  rCreatedThisPinch   = false;
    private float rPinchStartTime     = 0f;
    private GameObject rGrabbed       = null;
    private float      rGrabDistance  = 0f;   // distance along ray at grab moment

    // Left hand state
    private bool lPinching          = false;
    private GameObject lTargetSource = null;
    // Depth-adjust mode (left pinch on empty space)
    private bool  lDepthAdjust      = false;
    private float lDepthAdjustStartY = 0f;   // left hand Y when pinch started
    private float lDepthAdjustStartLen = 0f; // defaultRayLength when pinch started

    // Controller fallback
    private bool controllerGrabbing         = false;
    private GameObject controllerGrabSource = null;
    private float      controllerGrabDist   = 0f;

    // Calibration state
    private float        calibrationTimer   = 0f;
    private bool         calibrationDone    = false;
    private SpeakerManager speakerManager   = null;
    private TextMesh     calibrationLabel   = null;

    // Mouse fallback
    private GameObject mouseGrabbed   = null;
    private Plane      mouseDragPlane;
    private Vector3    mouseGrabOffset = Vector3.zero;
    private Camera     mainCamera;

    // Prev-pinch state for GetDown / GetUp emulation
    private bool _prevRPinch = false;
    private bool _prevLPinch = false;

    // Visuals
    private GameObject   indicatorInstance;
    private LineRenderer rRay;
    private LineRenderer lRay;
    private GameObject   rRayDot;
    private GameObject   lRayDot;

    private bool ovrRunning = false;

    // ── colours ────────────────────────────────────────────────────
    static readonly Color COL_IDLE   = new Color(1f, 1f, 1f, 0.6f);
    static readonly Color COL_HIT    = new Color(0f, 1f, 0.3f, 0.9f);
    static readonly Color COL_CREATE = new Color(1f, 0.9f, 0f, 0.9f);
    static readonly Color COL_DELETE = new Color(1f, 0.2f, 0.2f, 0.9f);

    // ── lifecycle ──────────────────────────────────────────────────
    void Start()
    {
        foreach (var s in FindObjectsOfType<SpatialSource>())
            allSources.Add(s.gameObject);

        if (cameraRig == null)
            cameraRig = FindObjectOfType<OVRCameraRig>();

        speakerManager = FindObjectOfType<SpeakerManager>();
        if (speakerManager != null)
            speakerManager.calibrationReferenceSpeakerId = calibrationReferenceSpeakerId;

        mainCamera = Camera.main;
        calibrationLabel = CreateCalibrationLabel();

        indicatorInstance = createIndicatorPrefab != null
            ? Instantiate(createIndicatorPrefab)
            : null;
        if (indicatorInstance != null) indicatorInstance.SetActive(false);

        rRay    = CreateRay("RightRay");
        lRay    = CreateRay("LeftRay");
        rRayDot = CreateDot("RightDot");
        lRayDot = CreateDot("LeftDot");
    }

    void Update()
    {
        ovrRunning = UnityEngine.XR.XRSettings.enabled;

        HandleCalibrationGesture();

        bool rOn = ovrRunning && OVRInput.IsControllerConnected(OVRInput.Controller.RHand);
        bool lOn = ovrRunning && OVRInput.IsControllerConnected(OVRInput.Controller.LHand);

        rRay.enabled    = rOn;
        lRay.enabled    = lOn;
        rRayDot.SetActive(rOn);
        lRayDot.SetActive(lOn);

        if (rOn || lOn)
        {
            if (rOn) HandleRightRay();
            else     ResetRight();

            if (lOn) HandleLeftRay();
            else     lPinching = false;
        }
        else if (ovrRunning)
        {
            bool ctrlActive = OVRInput.GetConnectedControllers() != OVRInput.Controller.None;
            if (ctrlActive) HandleControllerRay();
        }

        HandleMouseInput();
        HandleKeyboardInput();
        allSources.RemoveAll(s => s == null);
    }

    // ── right hand ray ─────────────────────────────────────────────
    void HandleRightRay()
    {
        Ray    ray      = GetHandRay(OVRInput.Controller.RHand);
        bool   pinchNow = PluginPinch(OVRPlugin.Hand.HandRight);
        bool   pinchDown = pinchNow && !_prevRPinch;
        bool   pinchUp   = !pinchNow && _prevRPinch;
        _prevRPinch = pinchNow;

        GameObject hit     = FindSourceOnRay(ray, rayHitRadius);
        bool       hasHit  = hit != null;

        // — Pinch start —
        if (pinchDown && !rPinching)
        {
            rPinching        = true;
            rCreatedThisPinch = false;
            rPinchStartTime  = Time.time;

            if (hasHit)
            {
                rGrabbed     = hit;
                rGrabDistance = Vector3.Dot(hit.transform.position - ray.origin, ray.direction);
            }
        }

        // — Pinch end —
        if (pinchUp && rPinching)
        {
            ResetRight();
            return;
        }

        // — While pinching —
        if (rPinching)
        {
            if (rGrabbed != null)
            {
                rGrabbed.transform.position = ray.origin + ray.direction * rGrabDistance;
                DrawRay(rRay, rRayDot, ray, rGrabDistance, COL_HIT);
            }
            else if (!rCreatedThisPinch)
            {
                float progress = Mathf.Clamp01((Time.time - rPinchStartTime) / createHoldTime);
                float endDist  = defaultRayLength;

                DrawRay(rRay, rRayDot, ray, endDist, COL_CREATE);

                if (progress >= 1f)
                {
                    CreateSource(ray.origin + ray.direction * endDist);
                    rCreatedThisPinch = true;
                }
            }
        }
        else
        {
            float dist = hasHit
                ? Vector3.Dot(hit.transform.position - ray.origin, ray.direction)
                : defaultRayLength;
            DrawRay(rRay, rRayDot, ray, dist, hasHit ? COL_HIT : COL_IDLE);
        }
    }

    void ResetRight()
    {
        rPinching        = false;
        rGrabbed         = null;
        rCreatedThisPinch = false;
        if (indicatorInstance != null) indicatorInstance.SetActive(false);
    }

    // ── left hand ray ──────────────────────────────────────────────
    void HandleLeftRay()
    {
        Ray  ray      = GetHandRay(OVRInput.Controller.LHand);
        bool pinchNow  = PluginPinch(OVRPlugin.Hand.HandLeft);
        bool pinchDown = pinchNow && !_prevLPinch;
        bool pinchUp   = !pinchNow && _prevLPinch;
        _prevLPinch = pinchNow;

        GameObject hit = FindSourceOnRay(ray, rayHitRadius);

        if (pinchDown && !lPinching)
        {
            lPinching     = true;
            lTargetSource = hit;
            if (hit == null)
            {
                // No source targeted → depth-adjust mode
                lDepthAdjust       = true;
                lDepthAdjustStartY  = ray.origin.y;
                lDepthAdjustStartLen = defaultRayLength;
            }
        }

        // Depth adjust: left hand pinching on empty space
        if (lDepthAdjust && lPinching)
        {
            float deltaY = ray.origin.y - lDepthAdjustStartY;
            defaultRayLength = Mathf.Clamp(
                lDepthAdjustStartLen + deltaY * depthSensitivity,
                minRayLength, maxRayLength);
        }

        if (pinchUp && lPinching)
        {
            lPinching    = false;
            lDepthAdjust = false;
            if (lTargetSource != null) { DeleteSource(lTargetSource); lTargetSource = null; }
        }

        bool  hasHit = hit != null;
        Color col    = lTargetSource != null ? COL_DELETE : (hasHit ? COL_DELETE : COL_IDLE);
        float dist   = hasHit
            ? Vector3.Dot(hit.transform.position - ray.origin, ray.direction)
            : defaultRayLength;
        DrawRay(lRay, lRayDot, ray, dist, col);
    }

    // ── controller fallback (ray-based too) ────────────────────────
    void HandleControllerRay()
    {
        Ray  ray      = GetHandRay(OVRInput.Controller.RTouch);
        bool btnDown  = OVRInput.GetDown(OVRInput.Button.One);
        bool btnUp    = OVRInput.GetUp(OVRInput.Button.One);

        if (btnDown)
        {
            GameObject hit = FindSourceOnRay(ray, rayHitRadius);
            if (hit != null)
            {
                controllerGrabSource = hit;
                controllerGrabDist   = Vector3.Dot(hit.transform.position - ray.origin, ray.direction);
                controllerGrabbing   = true;
            }
            else
            {
                CreateSource(ray.origin + ray.direction * defaultRayLength);
            }
        }
        if (btnUp) { controllerGrabSource = null; controllerGrabbing = false; }
        if (controllerGrabbing && controllerGrabSource != null)
            controllerGrabSource.transform.position = ray.origin + ray.direction * controllerGrabDist;

        if (OVRInput.GetDown(OVRInput.Button.Two))
        {
            GameObject hit = FindSourceOnRay(ray, rayHitRadius);
            if (hit != null) DeleteSource(hit);
        }
    }

    // ── keyboard fallback (PC debug) ───────────────────────────────
    void HandleKeyboardInput()
    {
        if (mainCamera == null) return;
        // Space → create ball 2m in front of camera
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Vector3 pos = mainCamera.transform.position + mainCamera.transform.forward * 2f;
            CreateSource(pos);
        }
        // Delete → remove last ball
        if (Input.GetKeyDown(KeyCode.Delete) && allSources.Count > 0)
        {
            DeleteSource(allSources[allSources.Count - 1]);
        }
    }

    // ── mouse fallback ─────────────────────────────────────────────
    void HandleMouseInput()
    {
        if (mainCamera == null) mainCamera = Camera.main ?? FindObjectOfType<Camera>();
        if (mainCamera == null) return;

        bool mouseDown, mouseUp, mouseHeld;
        Vector2 mousePos;
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        if (mouse != null)
        {
            mouseDown = mouse.leftButton.wasPressedThisFrame;
            mouseUp   = mouse.leftButton.wasReleasedThisFrame;
            mouseHeld = mouse.leftButton.isPressed;
            mousePos  = mouse.position.ReadValue();
        }
        else { mouseDown = Input.GetMouseButtonDown(0); mouseUp = Input.GetMouseButtonUp(0); mouseHeld = Input.GetMouseButton(0); mousePos = Input.mousePosition; }
#else
        mouseDown = Input.GetMouseButtonDown(0); mouseUp = Input.GetMouseButtonUp(0); mouseHeld = Input.GetMouseButton(0); mousePos = Input.mousePosition;
#endif
        if (mouseDown)
        {
            Ray ray = mainCamera.ScreenPointToRay(mousePos);
            GameObject hit = FindSourceOnRay(ray, 0.25f);
            if (hit != null)
            {
                mouseGrabbed   = hit;
                mouseDragPlane = new Plane(-mainCamera.transform.forward, hit.transform.position);
                if (mouseDragPlane.Raycast(ray, out float e)) mouseGrabOffset = hit.transform.position - ray.GetPoint(e);
            }
        }
        if (mouseUp || !mouseHeld) mouseGrabbed = null;
        if (mouseGrabbed != null)
        {
            Ray ray = mainCamera.ScreenPointToRay(mousePos);
            if (mouseDragPlane.Raycast(ray, out float e))
                mouseGrabbed.transform.position = ray.GetPoint(e) + mouseGrabOffset;
        }
    }

    // ── helpers ────────────────────────────────────────────────────

    Ray GetHandRay(OVRInput.Controller ctrl)
    {
        Vector3    localPos = OVRInput.GetLocalControllerPosition(ctrl);
        Quaternion localRot = OVRInput.GetLocalControllerRotation(ctrl);
        Vector3    worldPos = cameraRig != null ? cameraRig.trackingSpace.TransformPoint(localPos) : localPos;
        Vector3    worldFwd = cameraRig != null
            ? cameraRig.trackingSpace.rotation * (localRot * Vector3.forward)
            : localRot * Vector3.forward;
        return new Ray(worldPos, worldFwd);
    }

    static bool PluginPinch(OVRPlugin.Hand hand)
    {
        var state = new OVRPlugin.HandState();
        OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state);
        return (state.Pinches & OVRPlugin.HandFingerPinch.Index) != 0;
    }

    GameObject FindSourceOnRay(Ray ray, float maxDist)
    {
        float best = maxDist;
        GameObject result = null;
        foreach (var s in allSources)
        {
            if (s == null) continue;
            Vector3 toS   = s.transform.position - ray.origin;
            float   along = Vector3.Dot(toS, ray.direction);
            if (along < 0f) continue;
            float   perp  = Vector3.Distance(ray.origin + ray.direction * along, s.transform.position);
            if (perp < best) { best = perp; result = s; }
        }
        return result;
    }

    void CreateSource(Vector3 position)
    {
        if (allSources.Count >= SpatialSource.kMaxSources)
        {
            Debug.LogWarning($"[HandGesture] Max sources ({SpatialSource.kMaxSources}) reached — cannot create more.");
            return;
        }
        GameObject src = spatialSourcePrefab != null
            ? Instantiate(spatialSourcePrefab, position, Quaternion.identity)
            : new GameObject("SpatialSource_Runtime");
        if (spatialSourcePrefab == null) { src.transform.position = position; src.AddComponent<SpatialSource>(); }
        src.name = $"SpatialSource_{allSources.Count}";
        allSources.Add(src);
        Debug.Log($"[HandGesture] Created source @ {position:F2}");
    }

    void DeleteSource(GameObject src)
    {
        if (src == null) return;
        allSources.Remove(src);
        Destroy(src);
    }

    void DrawRay(LineRenderer lr, GameObject dot, Ray ray, float length, Color col)
    {
        lr.SetPosition(0, ray.origin);
        lr.SetPosition(1, ray.origin + ray.direction * length);
        lr.startColor = lr.endColor = col;
        dot.transform.position = ray.origin + ray.direction * length;
        dot.GetComponent<Renderer>().material.color = col;
    }

    LineRenderer CreateRay(string name)
    {
        GameObject go = new GameObject(name);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        // Plain solid material — no texture, no wave effect
        Material mat = new Material(Shader.Find("Sprites/Default"));
        lr.material      = mat;
        lr.startWidth    = 0.004f;
        lr.endWidth      = 0.004f;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.enabled       = false;
        return lr;
    }

    GameObject CreateDot(string name)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = name;
        obj.transform.localScale = Vector3.one * 0.03f;
        Destroy(obj.GetComponent<Collider>());
        obj.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default"));
        obj.SetActive(false);
        return obj;
    }

    // ── calibration ────────────────────────────────────────────────

    /// <summary>
    /// Both hands pinch simultaneously → calibration timer counts up.
    /// Release either hand → timer resets.
    /// Held for calibrationHoldTime → CalibrateToHead() fires once.
    /// This gesture is impossible to trigger accidentally during normal
    /// source creation/deletion (which only ever uses one hand at a time).
    /// </summary>
    void HandleCalibrationGesture()
    {
        bool rPinch = PluginPinch(OVRPlugin.Hand.HandRight);
        bool lPinch = PluginPinch(OVRPlugin.Hand.HandLeft);
        bool bothPinching = rPinch && lPinch;

#if UNITY_EDITOR
        // Editor fallback: hold both mouse buttons
        bothPinching = Input.GetMouseButton(0) && Input.GetMouseButton(1);
#endif

        if (bothPinching)
        {
            calibrationTimer += Time.deltaTime;
            calibrationDone = false;

            float progress = Mathf.Clamp01(calibrationTimer / calibrationHoldTime);
            ShowCalibrationLabel($"CALIBRATING {Mathf.RoundToInt(progress * 100)}%", Color.yellow);

            if (calibrationTimer >= calibrationHoldTime && !calibrationDone)
            {
                calibrationDone = true;
                calibrationTimer = 0f;

                if (speakerManager != null)
                    speakerManager.CalibrateToHead();

                ShowCalibrationLabel("CALIBRATED ✓", Color.green);
                Invoke(nameof(HideCalibrationLabel), 2.0f);
                Debug.Log("[Calibration] Speaker array aligned to head position.");
            }
        }
        else
        {
            if (calibrationTimer > 0f && !calibrationDone)
                HideCalibrationLabel();
            calibrationTimer = 0f;
        }
    }

    void ShowCalibrationLabel(string text, Color color)
    {
        if (calibrationLabel == null || mainCamera == null) return;
        calibrationLabel.gameObject.SetActive(true);
        calibrationLabel.text  = text;
        calibrationLabel.color = color;
        // Float 0.6 m in front of the camera, slightly above centre
        Transform cam = mainCamera.transform;
        calibrationLabel.transform.position = cam.position + cam.forward * 0.6f + cam.up * 0.08f;
        calibrationLabel.transform.rotation = cam.rotation;
    }

    void HideCalibrationLabel()
    {
        if (calibrationLabel != null)
            calibrationLabel.gameObject.SetActive(false);
    }

    TextMesh CreateCalibrationLabel()
    {
        GameObject go = new GameObject("CalibrationLabel");
        TextMesh tm   = go.AddComponent<TextMesh>();
        tm.fontSize      = 60;
        tm.characterSize = 0.006f;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = Color.yellow;
        go.SetActive(false);
        return tm;
    }

    GameObject CreateDefaultIndicator()
    {
        GameObject obj    = new GameObject("CreateIndicator");
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.SetParent(obj.transform);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale    = Vector3.one;
        Destroy(sphere.GetComponent<Collider>());
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(1f, 1f, 1f, 0.4f);
        mat.SetFloat("_Mode", 3);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3000;
        sphere.GetComponent<Renderer>().material = mat;
        return obj;
    }
}
