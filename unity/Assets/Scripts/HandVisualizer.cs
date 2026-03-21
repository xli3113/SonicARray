using UnityEngine;

/// <summary>
/// Shows hand position in two ways:
///   1. Tries to load OVRHandPrefab from Meta SDK for a proper hand mesh.
///   2. Falls back to a coloured sphere at the wrist position.
/// No OVRHand/OVRSkeleton components needed — driven by OVRInput position.
/// </summary>
public class HandVisualizer : MonoBehaviour
{
    [Tooltip("Size of fallback sphere in metres")]
    public float sphereSize = 0.08f;

    private GameObject _rightVisual;
    private GameObject _leftVisual;
    private OVRCameraRig _rig;

    // Paths to OVRHandPrefab in Meta SDK package cache
    private static readonly string[] HandPrefabPaths =
    {
        "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab",
        "Packages/com.meta.xr.sdk.all-in-one/Prefabs/OVRHandPrefab.prefab",
    };

    void Start()
    {
        _rig = FindObjectOfType<OVRCameraRig>();

        GameObject handPrefab = null;
#if UNITY_EDITOR
        foreach (var path in HandPrefabPaths)
        {
            handPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (handPrefab != null) break;
        }
#endif
        if (handPrefab != null)
        {
            _rightVisual = Instantiate(handPrefab);
            _rightVisual.name = "RightHandVisual";
            _leftVisual  = Instantiate(handPrefab);
            _leftVisual.name  = "LeftHandVisual";
            Debug.Log("[HandVisualizer] Using OVRHandPrefab for hand mesh.");
        }
        else
        {
            _rightVisual = CreateSphere(Color.cyan);
            _leftVisual  = CreateSphere(Color.green);
            Debug.Log("[HandVisualizer] OVRHandPrefab not found, using sphere fallback.");
        }
    }

    void Update()
    {
        bool rConn = OVRInput.IsControllerConnected(OVRInput.Controller.RHand);
        bool lConn = OVRInput.IsControllerConnected(OVRInput.Controller.LHand);

        _rightVisual.SetActive(rConn);
        _leftVisual.SetActive(lConn);

        if (rConn)
        {
            _rightVisual.transform.position = ToWorld(OVRInput.GetLocalControllerPosition(OVRInput.Controller.RHand));
            _rightVisual.transform.rotation = ToWorldRot(OVRInput.GetLocalControllerRotation(OVRInput.Controller.RHand));
        }
        if (lConn)
        {
            _leftVisual.transform.position = ToWorld(OVRInput.GetLocalControllerPosition(OVRInput.Controller.LHand));
            _leftVisual.transform.rotation = ToWorldRot(OVRInput.GetLocalControllerRotation(OVRInput.Controller.LHand));
        }
    }

    Vector3 ToWorld(Vector3 local)
    {
        if (_rig == null) _rig = FindObjectOfType<OVRCameraRig>();
        return _rig != null ? _rig.trackingSpace.TransformPoint(local) : local;
    }

    Quaternion ToWorldRot(Quaternion local)
    {
        if (_rig == null) _rig = FindObjectOfType<OVRCameraRig>();
        return _rig != null ? _rig.trackingSpace.rotation * local : local;
    }

    GameObject CreateSphere(Color color)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.transform.localScale = Vector3.one * sphereSize;
        Destroy(obj.GetComponent<Collider>());
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        obj.GetComponent<Renderer>().material = mat;
        return obj;
    }
}
