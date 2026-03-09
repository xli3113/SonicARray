using UnityEngine;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>修复 Quest 上 UI 按钮点不动：添加 OVRInputModule + OVRRaycaster。</summary>
public static class FixVRUIInput {

    [MenuItem("SonicARray/Quest/Fix VR UI Input")]
    public static void Fix() {
        var eventSystem = Object.FindObjectOfType<EventSystem>();
        if (eventSystem == null) {
            var esGo = new GameObject("EventSystem");
            eventSystem = esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        var ovrInputType = System.Type.GetType("UnityEngine.EventSystems.OVRInputModule, Oculus.VR")
            ?? System.Type.GetType("OVR.OVRInputModule, Oculus.VR");
        var standalone = eventSystem.GetComponent<StandaloneInputModule>();
        Component ovrInput = ovrInputType != null ? eventSystem.GetComponent(ovrInputType) : null;
        if (ovrInputType != null && ovrInput == null) {
            ovrInput = eventSystem.gameObject.AddComponent(ovrInputType);
        }
        if (ovrInput != null && standalone != null) {
            Object.DestroyImmediate(standalone);
        }

        var canvas = Object.FindObjectOfType<Canvas>();
        if (canvas != null) {
            var ovrRayType = System.Type.GetType("UnityEngine.EventSystems.OVRRaycaster, Oculus.VR")
                ?? System.Type.GetType("OVR.OVRRaycaster, Oculus.VR");
            if (ovrRayType != null && canvas.GetComponent(ovrRayType) == null) {
                canvas.gameObject.AddComponent(ovrRayType);
            }

            var rig = GameObject.Find("OVRCameraRig");
            if (rig != null && ovrInput != null) {
                var trackingSpace = rig.transform.Find("TrackingSpace");
                if (trackingSpace != null) {
                    var rightHand = trackingSpace.Find("RightHandAnchor");
                    if (rightHand == null) rightHand = trackingSpace.Find("RightControllerAnchor");
                    if (rightHand != null) {
                        var so = new SerializedObject(ovrInput);
                        var rayTransform = so.FindProperty("rayTransform");
                        if (rayTransform != null) {
                            rayTransform.objectReferenceValue = rightHand;
                            so.ApplyModifiedProperties();
                        }
                    }
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Fix VR UI Input", ovrInputType != null
            ? "已添加 OVRInputModule 和 OVRRaycaster。保存场景后重新运行。"
            : "未找到 OVRInputModule。请手动：1) 选 EventSystem 移除 Standalone Input Module；2) Add Component 搜索 OVR Input Module；3) 选 Canvas Add OVR Raycaster。", "确定");
    }
}
