using System;
using System.Collections.Generic;
using UnityEngine;

namespace SonicARray.SpeakerDebug {

/// <summary>Mock 增益模式。</summary>
public enum MockGainMode {
    SingleScan,         // 单点扫描，按 id 轮播
    SineBreath,         // 正弦呼吸，全体同步
    RandomPulse,        // 随机脉冲
    DirectionalHotspot  // 方向性热点：虚拟声源位置 + 距离衰减
}

public class MockGainProvider : MonoBehaviour, IGainProvider {
    [Header("Configuration")]
    public int speakerCount = 28;
    public MockGainMode mode = MockGainMode.SingleScan;
    public bool isPaused;
    [Range(0.1f, 2f)] public float intensity = 1f;
    [Range(0.01f, 0.5f)] public float smoothTau = 0.08f;
    [Header("SingleScan")]
    public float scanSpeed = 2f; // speakers per second
    [Header("SineBreath")]
    public float breathFreq = 0.5f;
    [Header("RandomPulse")]
    public int randomSeed = 42;
    public float pulseInterval = 0.3f;
    public float pulseWidth = 0.1f;
    [Header("DirectionalHotspot")]
    public Vector3 virtualSourcePosition = Vector3.zero;
    public float falloffDistance = 2f;
    public float maxGainAtSource = 1f;
    [Tooltip("可选：从 SpeakerSpawner 注入真实位置；否则用简化网格")]
    public List<Vector3> speakerPositionsOverride;

    float[] _lastGains;
    float[] _smoothedGains;
    float _scanPhase;
    System.Random _rng;
    float _lastPulseTime;

    public int SpeakerCount => speakerCount;
    public bool IsPaused { get => isPaused; set => isPaused = value; }

    void Awake() {
        _lastGains = new float[64];
        _smoothedGains = new float[64];
        _rng = new System.Random(randomSeed);
    }

    public void SetSpeakerCount(int count) {
        speakerCount = Mathf.Clamp(count, 1, 64);
    }

    public void GetGains(float[] gainsOut) {
        if (gainsOut == null || gainsOut.Length < speakerCount) return;
        if (_lastGains == null || _lastGains.Length < 64) {
            _lastGains = new float[64];
            _smoothedGains = new float[64];
            _rng = new System.Random(randomSeed);
        }
        int n = speakerCount;

        if (isPaused) {
            for (int i = 0; i < n; i++) gainsOut[i] = _smoothedGains[i];
            return;
        }

        float t = Time.time;
        float dt = Time.deltaTime;

        switch (mode) {
            case MockGainMode.SingleScan:
                _scanPhase += scanSpeed * dt;
                int idx = (int)_scanPhase % n;
                if (idx < 0) idx += n;
                for (int i = 0; i < n; i++)
                    _lastGains[i] = (i == idx ? intensity : 0f);
                break;
            case MockGainMode.SineBreath:
                float v = (Mathf.Sin(t * breathFreq * Mathf.PI * 2f) + 1f) * 0.5f * intensity;
                for (int i = 0; i < n; i++) _lastGains[i] = v;
                break;
            case MockGainMode.RandomPulse:
                if (t - _lastPulseTime >= pulseInterval) {
                    _lastPulseTime = t;
                    int id = _rng.Next(n);
                    for (int i = 0; i < n; i++) _lastGains[i] = (i == id ? intensity : 0f);
                }
                for (int i = 0; i < n; i++) {
                    float decay = Mathf.Exp(-(t - _lastPulseTime) / pulseWidth);
                    if (_lastGains[i] > 0) _lastGains[i] = intensity * decay;
                }
                break;
            case MockGainMode.DirectionalHotspot:
                for (int i = 0; i < n; i++) {
                    Vector3 sp;
                    if (speakerPositionsOverride != null && i < speakerPositionsOverride.Count)
                        sp = speakerPositionsOverride[i];
                    else {
                        float sx = (i % 8) - 3.5f;
                        float sy = (i / 8) - 1.5f;
                        sp = new Vector3(sx * 0.5f, sy * 0.5f, 0f);
                    }
                    float d = Vector3.Distance(virtualSourcePosition, sp);
                    float g = maxGainAtSource * Mathf.Exp(-d / falloffDistance) * intensity;
                    _lastGains[i] = Mathf.Clamp01(g);
                }
                break;
            default:
                for (int i = 0; i < n; i++) _lastGains[i] = 0f;
                break;
        }

        // 指数平滑: alpha = 1 - exp(-dt/tau)
        float alpha = 1f - Mathf.Exp(-dt / smoothTau);
        for (int i = 0; i < n; i++) {
            float target = _lastGains[i];
            float cur = _smoothedGains[i];
            _smoothedGains[i] = Mathf.Lerp(cur, target, alpha);
            gainsOut[i] = _smoothedGains[i];
        }
    }

    public float GetGain(int speakerId) {
        if (speakerId < 0 || speakerId >= speakerCount) return 0f;
        float[] g = new float[speakerCount];
        GetGains(g);
        return g[speakerId];
    }

    public void ResetRandomSeed() {
        _rng = new System.Random(randomSeed);
    }
}

}
