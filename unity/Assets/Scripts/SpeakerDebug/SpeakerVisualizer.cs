using System.Collections.Generic;
using UnityEngine;

namespace SonicARray.SpeakerDebug {

/// <summary>用 gain 驱动扬声器可视化：颜色蓝->红、Emission、缩放。含平滑。</summary>
public class SpeakerVisualizer : MonoBehaviour {
    [Header("References")]
    public IGainProvider gainProvider;
    public SpeakerSpawner spawner;

    [Header("Visual Mapping")]
    [Tooltip("低 gain 颜色")]
    public Color colorLow = new Color(0.2f, 0.3f, 1f);
    [Tooltip("高 gain 颜色")]
    public Color colorHigh = new Color(1f, 0.2f, 0.2f);
    [Range(0f, 5f)] public float emissionMultiplier = 2f;
    [Range(0.9f, 1.5f)] public float scaleMin = 0.95f;
    [Range(1f, 2f)] public float scaleMax = 1.3f;

    [Header("Smoothing")]
    [Range(0.01f, 0.5f)] public float smoothTau = 0.08f;
    [Tooltip("true=线性映射 scale，false=sqrt 更自然")]
    public bool useSqrtScale = true;

    float[] _gains;
    float[] _smoothed;
    Material[] _materials;
    Vector3[] _baseScales;
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    [SerializeField, HideInInspector] bool _initialized;

    void Start() {
        Rebuild();
    }

    /// <summary>由 Bootstrap 或外部在 wiring 完成后调用，确保缓存 renderer/material/baseScale。</summary>
    public void Rebuild() {
        if (spawner == null) spawner = GetComponent<SpeakerSpawner>();
        if (spawner == null || gainProvider == null) return;

        int n = gainProvider.SpeakerCount;
        var instances = spawner.Instances;
        if (instances == null || instances.Count == 0) return;

        _gains = new float[Mathf.Max(n, instances.Count)];
        _smoothed = new float[_gains.Length];
        _materials = new Material[instances.Count];
        _baseScales = new Vector3[instances.Count];

        for (int i = 0; i < instances.Count; i++) {
            var r = instances[i].GetComponent<Renderer>();
            if (r != null) {
                _materials[i] = r.material;
                if (_materials[i] != null && _materials[i].HasProperty(EmissionColorId)) {
                    _materials[i].EnableKeyword("_EMISSION");
                }
            }
            _baseScales[i] = instances[i].transform.localScale;
        }
        _initialized = true;
    }

    void Update() {
        if (!_initialized) {
            Rebuild();
            return;
        }
        if (gainProvider == null || spawner == null || _gains == null) return;

        int n = Mathf.Min(gainProvider.SpeakerCount, _gains.Length, spawner.Instances?.Count ?? 0);
        gainProvider.GetGains(_gains);
        float dt = Time.deltaTime;
        float alpha = 1f - Mathf.Exp(-dt / smoothTau);

        var instances = spawner.Instances;
        for (int i = 0; i < n && i < instances.Count; i++) {
            float g = Mathf.Clamp01(_gains[i]);
            _smoothed[i] = Mathf.Lerp(_smoothed[i], g, alpha);

            var go = instances[i];
            if (go == null) continue;

            float s = Mathf.Clamp01(_smoothed[i]);
            float t = s;
            Color c = Color.Lerp(colorLow, colorHigh, t);
            if (_materials != null && i < _materials.Length && _materials[i] != null) {
                _materials[i].color = c;
                if (_materials[i].HasProperty(EmissionColorId))
                    _materials[i].SetColor(EmissionColorId, c * (1f + t * emissionMultiplier));
            }

            float scaleT = useSqrtScale ? Mathf.Sqrt(s) : s;
            float scaleFactor = Mathf.Lerp(scaleMin, scaleMax, scaleT);
            if (i < _baseScales.Length)
                go.transform.localScale = _baseScales[i] * scaleFactor;
        }
    }

    public float GetSmoothedGain(int speakerId) {
        if (_smoothed == null || speakerId < 0 || speakerId >= _smoothed.Length) return 0f;
        return _smoothed[speakerId];
    }
}

}
