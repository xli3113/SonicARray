using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>
/// 一键创建 SoundARray 场景：Camera、灯光、SpeakerManager、SpatialSource 及默认材质。
/// 菜单：SoundARray -> Create Scene
/// </summary>
public static class CreateSoundARrayScene {
    const string ScenePath = "Assets/Scenes/MainScene.unity";
    const string MaterialsPath = "Assets/Materials";
    const string StreamingAssetsPath = "Assets/StreamingAssets";

    [MenuItem("SoundARray/Create Scene")]
    public static void CreateScene() {
        // 确保目录存在
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) {
            AssetDatabase.CreateFolder("Assets", "Scenes");
        }
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }
        if (!AssetDatabase.IsValidFolder(StreamingAssetsPath)) {
            AssetDatabase.CreateFolder("Assets", "StreamingAssets");
        }

        // 复制 speakers.yaml 到 StreamingAssets（若项目根或上级有）
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string yamlInRoot = Path.Combine(projectRoot, "speakers.yaml");
        string yamlParent = Path.Combine(projectRoot, "..", "speakers.yaml");
        string destDir = Path.Combine(Application.dataPath, "StreamingAssets");
        string destYaml = Path.Combine(destDir, "speakers.yaml");
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
        if (File.Exists(yamlInRoot)) {
            File.Copy(yamlInRoot, destYaml, true);
            AssetDatabase.Refresh();
        } else if (File.Exists(yamlParent)) {
            File.Copy(yamlParent, destYaml, true);
            AssetDatabase.Refresh();
        }

        // 新建场景
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 查找或创建 Main Camera 并调整
        var cam = Camera.main;
        if (cam != null) {
            cam.transform.position = new Vector3(0, 2, -5);
            cam.transform.rotation = Quaternion.Euler(15, 0, 0);
        }

        // 查找或创建 Directional Light
        var lightObj = GameObject.Find("Directional Light");
        if (lightObj == null) {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
        } else {
            lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        // 创建材质
        Material speakerDefault = GetOrCreateMaterial("SpeakerDefault", new Color(0.5f, 0.7f, 1f));
        Material speakerActive = GetOrCreateMaterial("SpeakerActive", new Color(1f, 0.2f, 0.2f), true);
        Material sourceMat = GetOrCreateMaterial("SourceMaterial", new Color(1f, 0.8f, 0.2f));

        // SpeakerManager（长方体扬声器，尺寸调大以便看清）
        GameObject speakerManagerGo = new GameObject("SpeakerManager");
        var sm = speakerManagerGo.AddComponent<SpeakerManager>();
        sm.yamlFilePath = "speakers.yaml";
        sm.speakerScale = 0.8f;
        sm.defaultMaterial = speakerDefault;
        sm.activeMaterial = speakerActive;
        sm.speakerBaseSize = new Vector3(0.2f, 0.15f, 0.2f);  // 长方体 (宽, 高, 深)
        sm.maxHeightMultiplier = 3f;
        sm.highlightThickness = 0.02f;

        // SpatialSource
        GameObject spatialSourceGo = new GameObject("SpatialSource");
        spatialSourceGo.transform.position = new Vector3(0, 1.5f, 2);
        var ss = spatialSourceGo.AddComponent<SpatialSource>();
        ss.enableOSC = false;
        ss.oscIP = "127.0.0.1";
        ss.oscPort = 7000;
        ss.updateRate = 60f;
        ss.sourceMaterial = sourceMat;
        ss.sourceScale = 0.3f;
        ss.lineColor = Color.red;
        ss.lineWidth = 0.02f;

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        // 加入 Build Settings
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath))
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        EditorUtility.DisplayDialog("SoundARray", "场景已创建并保存为 " + ScenePath + "。请点击 Play 测试。", "确定");
    }

    static Material GetOrCreateMaterial(string name, Color color, bool emission = false) {
        string path = MaterialsPath + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) {
            mat = new Material(Shader.Find("Standard"));
            mat.name = name;
            mat.color = color;
            if (emission) {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 2f);
            }
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            AssetDatabase.CreateAsset(mat, path);
        } else {
            mat.color = color;
            if (emission) {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 2f);
            }
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }
}
