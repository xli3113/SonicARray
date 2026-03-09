using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>为 Quest Build 添加 OVR Camera Rig，修复“白光/无内容”问题。</summary>
public static class AddOVRCameraRigForQuest {

    static readonly string[] OVRCameraRigPaths = {
        "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab",
        "Packages/com.meta.xr.sdk.core/Runtime/Prefabs/OVRCameraRig.prefab",
        "Packages/com.meta.xr.sdk.all-in-one/Prefabs/OVRCameraRig.prefab",
    };

    [MenuItem("SonicARray/Quest/Add OVR Camera Rig")]
    public static void AddOVRCameraRig() {
        GameObject prefab = null;
        foreach (var path in OVRCameraRigPaths) {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) break;
        }
        if (prefab == null) {
            EditorUtility.DisplayDialog("Add OVR Camera Rig", 
                "未找到 OVRCameraRig 预制体。\n\n请确认已安装 Meta XR Core SDK。\n可在 Hierarchy 右键 > XR > OVR Camera Rig 手动添加。", 
                "确定");
            return;
        }

        if (GameObject.Find("OVRCameraRig") != null) {
            EditorUtility.DisplayDialog("Add OVR Camera Rig", "场景中已有 OVRCameraRig。", "确定");
            return;
        }

        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.name = "OVRCameraRig";

        var mainCam = Camera.main;
        if (mainCam != null) {
            var rigCam = rig.GetComponentInChildren<Camera>();
            if (rigCam != null && mainCam != rigCam) {
                mainCam.gameObject.SetActive(false);
                Debug.Log("[AddOVRCameraRig] 已禁用默认 Main Camera");
            }
        }

        var canvas = Object.FindObjectOfType<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay) {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            var rigCam = rig.GetComponentInChildren<Camera>();
            if (rigCam != null) canvas.worldCamera = rigCam;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Add OVR Camera Rig", "已添加 OVRCameraRig。\n请保存场景后重新 Build And Run 到 Quest。", "确定");
    }
}
