using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Adds OVRHand + OVRSkeleton to the hand anchor GameObjects in the OVRCameraRig,
/// and attaches ARPassthroughSetup to the SpeakerManager so passthrough is enabled at runtime.
/// Menu: SonicARray/Quest/Setup Hand Tracking
/// </summary>
public static class SetupHandTracking
{
    [MenuItem("SonicARray/Quest/Setup Hand Tracking")]
    public static void Setup()
    {
        // Search the scene directly by name — avoids touching the immutable OVRCameraRig prefab
        Transform rightAnchor = FindSceneObject("RightHandAnchorDetached", "RightHandAnchor");
        Transform leftAnchor  = FindSceneObject("LeftHandAnchorDetached",  "LeftHandAnchor");

        if (rightAnchor == null || leftAnchor == null)
        {
            EditorUtility.DisplayDialog("Setup Hand Tracking",
                $"Could not find hand anchor GameObjects in scene.\n\n" +
                $"Right: {(rightAnchor == null ? "MISSING" : rightAnchor.name)}\n" +
                $"Left:  {(leftAnchor  == null ? "MISSING" : leftAnchor.name)}\n\n" +
                "Make sure OVRCameraRig is in the scene.", "OK");
            return;
        }

        bool changed = false;
        changed |= AddHandComponents(rightAnchor, isRight: true);
        changed |= AddHandComponents(leftAnchor,  isRight: false);

        // --- Attach ARPassthroughSetup if not already in scene ---
        if (Object.FindFirstObjectByType<ARPassthroughSetup>() == null)
        {
            // Prefer attaching to SpeakerManager, otherwise use OVRCameraRig
            OVRCameraRig rig = Object.FindFirstObjectByType<OVRCameraRig>();
            GameObject target = GameObject.Find("SpeakerManager") ?? (rig != null ? rig.gameObject : null);
            if (target == null) { Debug.LogWarning("[SetupHandTracking] No target found for ARPassthroughSetup."); return; }
            target.AddComponent<ARPassthroughSetup>();
            Debug.Log($"[SetupHandTracking] Added ARPassthroughSetup to '{target.name}'");
            changed = true;
        }

        // Always re-apply correct HandType / SkeletonType values in case they were wrong
        AddHandComponents(rightAnchor, isRight: true);
        AddHandComponents(leftAnchor,  isRight: false);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Setup Hand Tracking",
            $"Done!\n\n" +
            $"• OVRHand (Right=1, Left=0) and OVRSkeleton corrected on:\n" +
            $"  {rightAnchor.name}\n" +
            $"  {leftAnchor.name}\n\n" +
            $"• ARPassthroughSetup present for passthrough + floor tracking.\n\n" +
            $"Save the scene and rebuild to Quest.", "OK");
    }

    static bool AddHandComponents(Transform anchor, bool isRight)
    {
        bool changed = false;

        // OVRHand
        OVRHand hand = anchor.GetComponent<OVRHand>();
        if (hand == null)
        {
            hand = anchor.gameObject.AddComponent<OVRHand>();
            // HandType is internal [SerializeField]; use intValue to set exact enum integer
            // OVRPlugin.Hand: None=-1, HandLeft=0, HandRight=1
            SerializedObject so = new SerializedObject(hand);
            SerializedProperty prop = so.FindProperty("HandType");
            if (prop != null)
            {
                prop.intValue = isRight ? 1 : 0;
                so.ApplyModifiedProperties();
            }
            Debug.Log($"[SetupHandTracking] Added OVRHand ({(isRight ? "Right" : "Left")}) to '{anchor.name}'");
            changed = true;
        }

        // OVRSkeleton
        OVRSkeleton skeleton = anchor.GetComponent<OVRSkeleton>();
        if (skeleton == null)
        {
            skeleton = anchor.gameObject.AddComponent<OVRSkeleton>();
            // OVRPlugin.SkeletonType: None=-1, HandLeft=0, HandRight=1
            SerializedObject so = new SerializedObject(skeleton);
            SerializedProperty prop = so.FindProperty("_skeletonType");
            if (prop != null)
            {
                prop.intValue = isRight ? 1 : 0;
                so.ApplyModifiedProperties();
            }
            Debug.Log($"[SetupHandTracking] Added OVRSkeleton ({(isRight ? "Right" : "Left")}) to '{anchor.name}'");
            changed = true;
        }

        return changed;
    }

    // Finds a scene GameObject by name (tries each candidate in order)
    static Transform FindSceneObject(params string[] names)
    {
        foreach (string name in names)
        {
            GameObject go = GameObject.Find(name);
            if (go != null) return go.transform;
        }
        return null;
    }
}
