using UnityEngine;

/// <summary>
/// Floating "EXIT" button that quits the app when you look at it for 2 seconds.
/// No controller needed — works with hand tracking or gaze only.
/// Attach to any GameObject, or just drop it in the scene.
/// </summary>
public class GazeExitButton : MonoBehaviour
{
    [Header("Position")]
    [Tooltip("Distance ahead of camera")]
    public float distance = 1.2f;
    [Tooltip("Offset from camera forward point (right, up)")]
    public Vector2 offset = new Vector2(-0.30f, -0.16f);

    [Header("Gaze")]
    [Tooltip("Half-angle (degrees) — how precisely the user must look at button")]
    public float gazeAngleDeg = 8f;
    [Tooltip("Seconds of continuous gaze to trigger exit")]
    public float gazeHoldTime = 2.0f;

    private Transform _cam;
    private GameObject _root;
    private TextMesh   _label;
    private GameObject _progressBar;
    private Transform  _progressFill;
    private Renderer   _bgRenderer;

    private float _gazeTimer = 0f;
    private bool  _exiting   = false;

    static readonly Color COL_IDLE    = new Color(0.55f, 0.08f, 0.08f, 0.88f);
    static readonly Color COL_ACTIVE  = new Color(0.90f, 0.20f, 0.10f, 0.95f);
    static readonly Color COL_TEXT    = new Color(1f, 0.9f, 0.9f, 1f);

    void Start()
    {
        BuildButton();
    }

    void LateUpdate()
    {
        // Resolve camera
        if (_cam == null)
        {
            OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null) _cam = rig.centerEyeAnchor;
            else if (Camera.main != null) _cam = Camera.main.transform;
        }
        if (_cam == null || _root == null || _exiting) return;

        // Float in front of camera
        _root.transform.position =
            _cam.position
            + _cam.forward * distance
            + _cam.right   * offset.x
            + _cam.up      * offset.y;
        _root.transform.rotation = _cam.rotation;

        // Gaze detection
        Vector3 toButton = (_root.transform.position - _cam.position).normalized;
        float angle = Vector3.Angle(_cam.forward, toButton);
        bool gazing = angle < gazeAngleDeg;

        if (gazing)
        {
            _gazeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_gazeTimer / gazeHoldTime);
            UpdateProgress(t);

            if (_gazeTimer >= gazeHoldTime)
            {
                _exiting = true;
                _label.text = "  BYE  ";
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }
        else
        {
            _gazeTimer = Mathf.Max(0f, _gazeTimer - Time.deltaTime * 2f); // decay faster
            UpdateProgress(Mathf.Clamp01(_gazeTimer / gazeHoldTime));
        }
    }

    void UpdateProgress(float t)
    {
        _bgRenderer.material.color = Color.Lerp(COL_IDLE, COL_ACTIVE, t);
        // Scale progress fill bar
        if (_progressFill != null)
        {
            Vector3 s = _progressFill.localScale;
            s.x = Mathf.Max(0.01f, t);
            _progressFill.localScale = s;
            _progressFill.localPosition = new Vector3((t - 1f) * 0.5f * 0.13f, -0.012f, -0.001f);
        }
        // Update label with countdown dots
        if (t > 0.05f)
        {
            int dots = Mathf.CeilToInt(t * 4);
            _label.text = "EXIT " + new string('.', dots);
        }
        else
        {
            _label.text = "EXIT";
        }
    }

    void BuildButton()
    {
        _root = new GameObject("GazeExitButton");

        // Background quad
        GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "ExitBG";
        bg.transform.SetParent(_root.transform, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        bg.transform.localScale    = new Vector3(0.16f, 0.06f, 1f);
        Destroy(bg.GetComponent<Collider>());
        Material bgMat = new Material(Shader.Find("Sprites/Default"));
        bgMat.color = COL_IDLE;
        _bgRenderer = bg.GetComponent<Renderer>();
        _bgRenderer.material = bgMat;

        // Progress fill bar (child of bg, scales 0→1 on x)
        _progressBar = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _progressBar.name = "ProgressFill";
        _progressBar.transform.SetParent(_root.transform, false);
        _progressBar.transform.localScale    = new Vector3(0.001f, 0.006f, 1f);
        _progressBar.transform.localPosition = new Vector3(-0.065f, -0.012f, -0.001f);
        Destroy(_progressBar.GetComponent<Collider>());
        _progressFill = _progressBar.transform;
        Material fillMat = new Material(Shader.Find("Sprites/Default"));
        fillMat.color = new Color(1f, 0.5f, 0.2f, 0.9f);
        _progressBar.GetComponent<Renderer>().material = fillMat;

        // Text
        GameObject textGo = new GameObject("ExitLabel");
        textGo.transform.SetParent(_root.transform, false);
        textGo.transform.localPosition = new Vector3(-0.055f, 0.005f, 0f);
        _label = textGo.AddComponent<TextMesh>();
        _label.text          = "EXIT";
        _label.fontSize      = 48;
        _label.characterSize = 0.0022f;
        _label.color         = COL_TEXT;
        _label.anchor        = TextAnchor.MiddleLeft;
    }
}
