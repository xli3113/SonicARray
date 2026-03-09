using System;
using System.Collections.Generic;
using UnityEngine;

namespace SonicARray.SpeakerDebug {

/// <summary>增益提供者接口。外部可接入真实声学后端或使用 MockGainProvider。</summary>
public interface IGainProvider {
    /// <summary>扬声器 ID 数量（0-based 连续）。</summary>
    int SpeakerCount { get; }
    /// <summary>是否暂停输出（回放时由外部控制）。</summary>
    bool IsPaused { get; set; }
    /// <summary>获取当前帧所有扬声器的增益 [0..1]。</summary>
    void GetGains(float[] gainsOut);
    /// <summary>获取指定 ID 的增益。</summary>
    float GetGain(int speakerId);
}

/// <summary>回放用增益提供者。从 GainRecording 按时间输出。</summary>
public class PlaybackGainProvider : IGainProvider {
    public GainRecording recording;
    public float currentTime;
    public bool loop;

    public int SpeakerCount => recording?.frames != null && recording.frames.Count > 0
        ? GetMaxSpeakerId() + 1 : 0;
    public bool IsPaused { get; set; }

    int GetMaxSpeakerId() {
        int max = 0;
        foreach (var f in recording.frames)
            foreach (var g in f.gains)
                if (g.speakerId > max) max = g.speakerId;
        return max;
    }

    public void GetGains(float[] gainsOut) {
        if (gainsOut == null || recording?.frames == null || recording.frames.Count == 0) return;
        for (int i = 0; i < gainsOut.Length; i++) gainsOut[i] = 0f;
        int n = recording.frames.Count;
        float t = currentTime * recording.sampleRate;
        int idx = (int)t % n;
        if (idx < 0) idx += n;
        if (!loop && currentTime >= recording.duration) return;
        var frame = recording.frames[idx];
        foreach (var s in frame.gains) {
            if (s.speakerId < gainsOut.Length)
                gainsOut[s.speakerId] = s.smoothedGain;
        }
    }

    public float GetGain(int speakerId) {
        if (recording?.frames == null || speakerId < 0) return 0f;
        float[] g = new float[Mathf.Max(speakerId + 1, 1)];
        GetGains(g);
        return speakerId < g.Length ? g[speakerId] : 0f;
    }
}

}
