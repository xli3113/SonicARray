using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

/// <summary>
/// Floating world-space HUD for SonicARray (Quest / VR).
///
/// Layout (always visible, follows camera):
///
///   SonicARray
///   ==================
///   OSC  [LIVE]  127.0.0.1
///   Src  2    Depth  1.5 m
///   ==================
///   spk 0  ########--  0.82
///   spk 3  ######----  0.61
///   spk 7  ####------  0.38
///   ==================
///
/// Attach to any GameObject. No prefabs or extra assets required.
/// </summary>
public class VRStatusPanel : MonoBehaviour
{
    [Header("Position")]
    [Tooltip("Distance ahead of camera (metres)")]
    public float distance = 1.2f;
    [Tooltip("Right / Up offset from the camera forward point (metres)")]
    public Vector2 offset = new Vector2(0.30f, -0.16f);

    [Header("Panel")]
    public float panelWidth  = 0.42f;
    public float panelHeight = 0.32f;

    [Header("Refresh")]
    [Tooltip("Seconds between text refreshes (keeps GPU quiet)")]
    public float refreshInterval = 0.35f;

    // ── scene refs (found automatically) ────────────────────────────
    private OSCReceiver         _osc;
    private HandGestureController _hand;
    private SpeakerManager      _speakers;
    private Transform           _cam;

    // ── visuals ─────────────────────────────────────────────────────
    private GameObject _root;
    private TextMesh   _text;
    private Renderer   _bgRenderer;
    private float      _nextRefresh;

    // ── colours ─────────────────────────────────────────────────────
    static readonly Color BG_IDLE       = new Color(0.05f, 0.06f, 0.10f, 0.84f);
    static readonly Color BG_CONNECTED  = new Color(0.04f, 0.10f, 0.06f, 0.84f);
    static readonly Color TEXT_COLOR    = new Color(0.88f, 0.92f, 1.00f);
    static readonly Color TEXT_WARN     = new Color(1.00f, 0.88f, 0.30f);

    // ── constants ───────────────────────────────────────────────────
    const int   BAR_WIDTH    = 10;   // chars in gain bar
    const float LIVE_THRESH  = 3.0f; // seconds without gains packet → not live
    const int   MAX_SPK_SHOW = 5;    // max speaker rows in panel

    // ───────────────────────────────────────────────────────────────
    void Start()
    {
        _osc      = FindObjectOfType<OSCReceiver>();
        _hand     = FindObjectOfType<HandGestureController>();
        _speakers = FindObjectOfType<SpeakerManager>();

        BuildPanel();
    }

    void LateUpdate()
    {
        // Resolve camera (safe even if OVRCameraRig is added late)
        if (_cam == null)
        {
            OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
                _cam = rig.centerEyeAnchor;
            else if (Camera.main != null)
                _cam = Camera.main.transform;
        }
        if (_cam == null || _root == null) return;

        // Float in front of the camera
        _root.transform.position =
            _cam.position
            + _cam.forward * distance
            + _cam.right   * offset.x
            + _cam.up      * offset.y;
        _root.transform.rotation = _cam.rotation;

        // Throttled text + colour refresh
        if (Time.time < _nextRefresh) return;
        _nextRefresh = Time.time + refreshInterval;

        bool live    = _osc != null && (Time.time - _osc.LastPacketTime) < LIVE_THRESH;
        bool sending = _hand != null && _hand.SourceCount > 0;
        _bgRenderer.material.color = live ? BG_CONNECTED : BG_IDLE;
        _text.color = live ? TEXT_COLOR : TEXT_WARN;
        _text.text  = BuildText(live, sending);
    }

    // ── text builder ────────────────────────────────────────────────
    string BuildText(bool live, bool sending)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("  SonicARray  DEBUG");
        sb.AppendLine("  ==================");

        // ── Quest's own IP (tells us if it's on the right subnet) ──
        sb.AppendLine($"  MyIP  {GetLocalIP()}");

        // ── backend target ──
        string backendIP = _speakers != null ? _speakers.backendIP : "?";
        sb.AppendLine($"  -> PC  {backendIP}:7000");

        // ── packets sent by SpatialSource ──
        sb.AppendLine($"  Tx  {OSCReceiver.TotalOSCSent} pkts sent");

        // ── packets received from backend ──
        int    rxParsed = _osc != null ? _osc.TotalPackets : 0;
        int    rxRaw    = _osc != null ? _osc.RawPackets   : 0;
        float  secAgo   = _osc != null ? (Time.time - _osc.LastPacketTime) : 999f;
        string bindErr  = _osc != null ? _osc.BindError    : "no OSCReceiver";
        sb.AppendLine($"  UDP raw={rxRaw} parsed={rxParsed}");
        if (rxParsed > 0)
            sb.AppendLine($"  Last rx {secAgo:F1}s ago");
        if (!string.IsNullOrEmpty(bindErr))
            sb.AppendLine($"  !BIND {bindErr}");

        // ── link status ──
        string linkStatus;
        if (live)         linkStatus = "[LIVE]";
        else if (sending) linkStatus = "[SENDING-NO REPLY]";
        else              linkStatus = "[NO SIGNAL]";
        sb.AppendLine($"  Status  {linkStatus}");

        sb.AppendLine("  ==================");

        // ── component check ──
        if (_osc      == null) sb.AppendLine("  ! OSCReceiver missing");
        if (_hand     == null) sb.AppendLine("  ! HandGesture missing");
        if (_speakers == null) sb.AppendLine("  ! SpeakerMgr missing");

        // ── speaker gains (only when live) ──
        if (live && _speakers != null)
        {
            List<SpeakerData> spks = _speakers.GetSpeakers();
            var active = new List<(int id, float gain)>();
            foreach (var s in spks)
            {
                float g = _speakers.GetSpeakerGain(s.id);
                if (g > 0.01f) active.Add((s.id, g));
            }
            active.Sort((a, b) => b.gain.CompareTo(a.gain));
            int show = Mathf.Min(active.Count, MAX_SPK_SHOW);
            for (int i = 0; i < show; i++)
            {
                var (id, gain) = active[i];
                sb.AppendLine($"  spk{id,2}  {GainBar(gain)}  {gain:F2}");
            }
            if (active.Count == 0) sb.AppendLine("  -- no active speakers --");
        }

        return sb.ToString().TrimEnd('\n', '\r');
    }

    // Returns the first non-loopback IPv4 address of this device
    static string GetLocalIP()
    {
        try {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !ua.Address.ToString().StartsWith("127.") &&
                        !ua.Address.ToString().StartsWith("169."))
                        return ua.Address.ToString();
                }
            }
        } catch { }
        return "unknown";
    }

    // ── ASCII bargraph ──────────────────────────────────────────────
    static string GainBar(float gain)
    {
        int filled = Mathf.Clamp(Mathf.RoundToInt(gain * BAR_WIDTH), 0, BAR_WIDTH);
        return new string('#', filled) + new string('-', BAR_WIDTH - filled);
    }

    // ── panel construction ─────────────────────────────────────────
    void BuildPanel()
    {
        _root = new GameObject("VRStatusPanel");

        // Semi-transparent background quad
        GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "PanelBG";
        bg.transform.SetParent(_root.transform, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.002f); // slightly behind text
        bg.transform.localScale    = new Vector3(panelWidth, panelHeight, 1f);
        Destroy(bg.GetComponent<Collider>());

        Material bgMat = new Material(Shader.Find("Sprites/Default"));
        bgMat.color = BG_IDLE;
        _bgRenderer = bg.GetComponent<Renderer>();
        _bgRenderer.material = bgMat;

        // Text mesh
        GameObject textGo = new GameObject("PanelText");
        textGo.transform.SetParent(_root.transform, false);
        // Anchor top-left: offset by half panel size
        textGo.transform.localPosition =
            new Vector3(-panelWidth * 0.47f, panelHeight * 0.44f, 0f);

        _text = textGo.AddComponent<TextMesh>();
        _text.fontSize      = 48;
        _text.characterSize = 0.0040f;
        _text.color         = TEXT_COLOR;
        _text.anchor        = TextAnchor.UpperLeft;
        _text.text          = "  SonicARray\n  Loading...";
    }
}
