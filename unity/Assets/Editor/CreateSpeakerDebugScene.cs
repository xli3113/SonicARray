using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

namespace SonicARray.SpeakerDebug {

/// <summary>一键创建 SpeakerDebug 场景结构。</summary>
public static class CreateSpeakerDebugScene {
    const string ScenePath = "Assets/Scenes/SpeakerDebug.unity";

    [MenuItem("SonicARray/SpeakerDebug/Create Scene")]
    public static void Create() {
        EnsureFolders();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var cam = Camera.main;
        if (cam != null) cam.transform.position = new Vector3(0, 2, -5);

        TryAddOVRCameraRig();

        var lightGo = GameObject.Find("Directional Light");
        if (lightGo == null) {
            lightGo = new GameObject("Directional Light");
            lightGo.AddComponent<Light>().type = LightType.Directional;
        }
        lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);

        var roomAnchor = new GameObject("RoomAnchor");
        var anchorMgr = roomAnchor.AddComponent<RoomAnchorManager>();
        var fakeAnchor = new GameObject("FakeAnchor");
        fakeAnchor.transform.SetParent(roomAnchor.transform);
        fakeAnchor.transform.localPosition = Vector3.zero;
        anchorMgr.fakeAnchorTransform = fakeAnchor.transform;
        anchorMgr.roomAnchorTransform = fakeAnchor.transform;
        anchorMgr.useFakeAnchorInEditor = true;

        var speakerRig = new GameObject("SpeakerRig");
        speakerRig.transform.SetParent(fakeAnchor.transform);
        speakerRig.transform.localPosition = Vector3.zero;
        anchorMgr.speakerRigParent = speakerRig.transform;

        var loader = new GameObject("SpeakerLoader").AddComponent<SpeakerYamlLoader>();
        loader.fileName = "speakers.yaml";

        var spawnerGo = new GameObject("SpeakerSpawner");
        var spawner = spawnerGo.AddComponent<SpeakerSpawner>();
        spawner.loader = loader;
        spawner.speakerRigParent = speakerRig.transform;
        spawner.primitiveType = PrimitiveType.Cube;
        spawner.baseScale = new Vector3(0.15f, 0.15f, 0.15f);

        var gainGo = new GameObject("MockGainProvider");
        var gainProvider = gainGo.AddComponent<MockGainProvider>();
        gainProvider.speakerCount = 28;

        var visGo = new GameObject("SpeakerVisualizer");
        var visualizer = visGo.AddComponent<SpeakerVisualizer>();
        visualizer.gainProvider = gainProvider;
        visualizer.spawner = spawner;

        var uiGo = new GameObject("SimpleUI");
        var simpleUI = uiGo.AddComponent<SimpleUI>();
        simpleUI.anchorManager = anchorMgr;
        simpleUI.gainProvider = gainProvider;
        simpleUI.visualizer = visualizer;

        var bootstrapGo = new GameObject("SpeakerDebugBootstrap");
        var bootstrap = bootstrapGo.AddComponent<SpeakerDebugBootstrap>();
        bootstrap.loader = loader;
        bootstrap.spawner = spawner;
        bootstrap.anchorManager = anchorMgr;
        bootstrap.gainProvider = gainProvider;
        bootstrap.visualizer = visualizer;
        bootstrap.simpleUI = simpleUI;

        CreateCanvas(simpleUI, anchorMgr);

        EditorSceneManager.SaveScene(scene, ScenePath);
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath)) list.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        EditorUtility.DisplayDialog("SonicARray SpeakerDebug", "场景已创建: " + ScenePath, "确定");
    }

    static void CreateCanvas(SimpleUI simpleUI, RoomAnchorManager anchorMgr) {
        var canvasGo = new GameObject("UICanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        var panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGo.transform, false);
        var rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(10, -10);
        rect.sizeDelta = new Vector2(200, 400);

        CreateButton(panel.transform, "Create Anchor", 0, ButtonBridge.Action.CreateAnchor, simpleUI);
        CreateButton(panel.transform, "Load Anchor", 1, ButtonBridge.Action.LoadAnchor, simpleUI);
        CreateButton(panel.transform, "Delete UUID", 2, ButtonBridge.Action.DeleteUuid, simpleUI);
        CreateButton(panel.transform, "Save Offset", 3, ButtonBridge.Action.SaveOffset, simpleUI);
        CreateButton(panel.transform, "Record", 4, ButtonBridge.Action.StartRecord, simpleUI);
        CreateButton(panel.transform, "Stop Record", 5, ButtonBridge.Action.StopRecord, simpleUI);
        CreateButton(panel.transform, "Playback", 6, ButtonBridge.Action.StartPlayback, simpleUI);
        CreateButton(panel.transform, "Stop Playback", 7, ButtonBridge.Action.StopPlayback, simpleUI);

        var statusGo = new GameObject("StatusText");
        statusGo.transform.SetParent(panel.transform, false);
        var statusRect = statusGo.AddComponent<RectTransform>();
        statusRect.anchoredPosition = new Vector2(0, -320);
        statusRect.sizeDelta = new Vector2(180, 60);
        var statusText = statusGo.AddComponent<UnityEngine.UI.Text>();
        statusText.text = "-";
        statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusText.fontSize = 12;
        var statusDisplay = statusGo.AddComponent<AnchorStatusDisplay>();
        statusDisplay.anchorManager = anchorMgr;
        statusDisplay.statusText = statusText;
    }

    static void CreateButton(Transform parent, string label, int index, ButtonBridge.Action action, SimpleUI simpleUI) {
        var go = new GameObject("Btn_" + label.Replace(" ", ""));
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(0, -index * 42);
        rect.sizeDelta = new Vector2(180, 36);
        var btn = go.AddComponent<UnityEngine.UI.Button>();
        var bridge = go.AddComponent<ButtonBridge>();
        bridge.action = action;
        bridge.simpleUI = simpleUI;
        btn.onClick.AddListener(bridge.OnClick);
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        var text = textGo.AddComponent<UnityEngine.UI.Text>();
        text.text = label;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14;
    }

    static void EnsureFolders() {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
    }

    static void TryAddOVRCameraRig() {
        var paths = new[] {
            "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab",
            "Packages/com.meta.xr.sdk.core/Runtime/Prefabs/OVRCameraRig.prefab",
        };
        GameObject prefab = null;
        foreach (var p in paths) {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (prefab != null) break;
        }
        if (prefab == null) return;
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.name = "OVRCameraRig";
        var mainCam = Camera.main;
        if (mainCam != null) {
            var rc = rig.GetComponentInChildren<Camera>();
            if (rc != null && mainCam != rc) mainCam.gameObject.SetActive(false);
        }
    }
}

}
