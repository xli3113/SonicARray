using System.Collections.Generic;
using UnityEngine;

namespace SonicARray.SpeakerDebug {

/// <summary>根据 SpeakerYamlLoader 结果在 SpeakerRig 下生成虚拟扬声器对象。</summary>
public class SpeakerSpawner : MonoBehaviour {
    [Header("References")]
    public SpeakerYamlLoader loader;
    public Transform speakerRigParent;

    [Header("Visual")]
    public PrimitiveType primitiveType = PrimitiveType.Cube;
    public Vector3 baseScale = new Vector3(0.15f, 0.15f, 0.15f);
    public Material defaultMaterial;
    [Tooltip("无材质时使用默认 Standard")]
    public bool createDefaultMaterialIfNull = true;

    [Header("Labels")]
    public bool showLabels = true;
    public int labelFontSize = 24;

    private readonly List<GameObject> _instances = new List<GameObject>();
    private readonly List<Vector3> _localPositions = new List<Vector3>();

    public IReadOnlyList<GameObject> Instances => _instances;
    public IReadOnlyList<Vector3> LocalPositions => _localPositions;

    void Start() {
        if (loader != null) {
            loader.OnLoaded += OnLoaderLoaded;
            if (loader.GetSpeakers().Count > 0) SpawnAll();
        }
    }

    void OnDestroy() {
        if (loader != null) loader.OnLoaded -= OnLoaderLoaded;
    }

    void OnLoaderLoaded(int count) {
        SpawnAll();
    }

    /// <summary>根据当前 loader 数据重新生成所有扬声器。</summary>
    public void SpawnAll() {
        Clear();

        var list = loader != null ? loader.GetSpeakers() : new List<SpeakerEntry>();
        if (list == null || list.Count == 0) {
            Debug.LogWarning("[SpeakerSpawner] 无扬声器数据，跳过生成");
            return;
        }

        Transform parent = speakerRigParent != null ? speakerRigParent : transform;
        Material mat = defaultMaterial != null ? defaultMaterial : (createDefaultMaterialIfNull ? CreateDefaultMaterial() : null);

        foreach (var s in list) {
            var go = GameObject.CreatePrimitive(primitiveType);
            go.name = $"Speaker_{s.id}";
            go.transform.SetParent(parent);
            go.transform.localPosition = new Vector3(s.x, s.y, s.z);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = baseScale;

            _localPositions.Add(new Vector3(s.x, s.y, s.z));

            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;

            if (showLabels) {
                var labelGo = new GameObject($"Label_{s.id}");
                labelGo.transform.SetParent(go.transform);
                labelGo.transform.localPosition = Vector3.up * (baseScale.y * 0.6f);
                var tm = labelGo.AddComponent<TextMesh>();
                tm.text = s.id.ToString();
                tm.fontSize = labelFontSize;
                tm.anchor = TextAnchor.MiddleCenter;
            }

            _instances.Add(go);
        }

        Debug.Log($"[SpeakerSpawner] 生成 {_instances.Count} 个扬声器");
    }

    public void Clear() {
        foreach (var g in _instances) {
            if (g != null) Destroy(g);
        }
        _instances.Clear();
        _localPositions.Clear();
    }

    static Material CreateDefaultMaterial() {
        var m = new Material(Shader.Find("Standard"));
        m.color = new Color(0.4f, 0.6f, 1f);
        return m;
    }

    public GameObject GetSpeaker(int id) {
        foreach (var g in _instances) {
            if (g != null && g.name == $"Speaker_{id}") return g;
        }
        return null;
    }
}

}
