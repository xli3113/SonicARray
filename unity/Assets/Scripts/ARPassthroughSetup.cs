using UnityEngine;

/// <summary>
/// Enables Meta Quest passthrough (AR mode) at runtime and configures floor-level tracking.
/// Attach this to any persistent GameObject in the scene (e.g. SpeakerManager or a dedicated Setup object).
///
/// Requirements:
///   - OculusProjectConfig must have insightPassthroughEnabled = true (already done)
///   - OVRCameraRig must be in the scene
///   - This script sets up OVRPassthroughLayer, transparent camera background, and FloorLevel tracking
/// </summary>
public class ARPassthroughSetup : MonoBehaviour
{
    [Header("Passthrough")]
    [Tooltip("Enable passthrough (AR). If false, app runs as VR with black background.")]
    public bool enablePassthrough = true;

    [Header("Tracking")]
    [Tooltip("FloorLevel = y=0 is the floor (recommended for AR). EyeLevel = y=0 at head height.")]
    public OVRManager.TrackingOrigin trackingOrigin = OVRManager.TrackingOrigin.FloorLevel;

    void Start()
    {
        // 1. Set tracking origin + enable passthrough on OVRManager
        OVRManager mgr = FindObjectOfType<OVRManager>();
        if (mgr != null)
        {
            mgr.trackingOriginType = trackingOrigin;
            mgr.isInsightPassthroughEnabled = true;
            Debug.Log($"[ARPassthrough] OVRManager: trackingOrigin={trackingOrigin}, passthrough=true");
        }
        else
        {
            Debug.LogWarning("[ARPassthrough] OVRManager not found — is OVRCameraRig in scene?");
        }

        if (!enablePassthrough) return;

        // 2. Ensure OVRPassthroughLayer exists
        OVRPassthroughLayer layer = FindObjectOfType<OVRPassthroughLayer>();
        if (layer == null)
        {
            OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
            GameObject target = rig != null ? rig.gameObject : gameObject;
            layer = target.AddComponent<OVRPassthroughLayer>();
            Debug.Log($"[ARPassthrough] Added OVRPassthroughLayer to '{target.name}'");
        }
        layer.enabled = true;

        // 3. Set camera background to transparent black (required for passthrough blend)
        // OVRCameraRig has multiple cameras; set all of them
        OVRCameraRig cameraRig = FindObjectOfType<OVRCameraRig>();
        if (cameraRig != null)
        {
            foreach (Camera cam in cameraRig.GetComponentsInChildren<Camera>())
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.clear;
            }
            Debug.Log("[ARPassthrough] Camera backgrounds set to transparent.");
        }
        else if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = Color.clear;
            Debug.Log("[ARPassthrough] Main camera background set to transparent.");
        }
    }
}
