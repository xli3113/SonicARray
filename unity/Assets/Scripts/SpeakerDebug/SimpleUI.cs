using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace SonicARray.SpeakerDebug {

/// <summary>简单 UI：锚点操作、Gain 模式、录制/回放。需配合 Canvas 使用。</summary>
public class SimpleUI : MonoBehaviour {
    [Header("References")]
    public RoomAnchorManager anchorManager;
    public MockGainProvider gainProvider;
    public SpeakerVisualizer visualizer;

    [Header("Recording")]
    public int maxRecordingSeconds = 10;
    public float recordSampleRate = 30f;
    public string recordingFileName = "gain_recording.json";

    private List<GainRecordingFrame> _recordingFrames = new List<GainRecordingFrame>();
    private bool _isRecording;
    private bool _isPlayingBack;
    private PlaybackGainProvider _playbackProvider;

    void Start() {
        if (gainProvider == null) gainProvider = FindObjectOfType<MockGainProvider>();
        if (anchorManager == null) anchorManager = FindObjectOfType<RoomAnchorManager>();
        if (visualizer == null) visualizer = FindObjectOfType<SpeakerVisualizer>();
    }

    public void OnCreateAndSaveAnchor() {
        if (anchorManager != null) anchorManager.CreateAndSaveAnchor();
    }

    public void OnLoadAnchor() {
        if (anchorManager != null) anchorManager.LoadAnchor();
    }

    public void OnDeleteLocalUuid() {
        if (anchorManager != null) anchorManager.DeleteLocalUuid();
    }

    public void OnSaveTransformOffset() {
        if (anchorManager != null) anchorManager.SaveTransformOffset();
    }

    public void SetGainMode(int mode) {
        if (gainProvider != null) gainProvider.mode = (MockGainMode)Mathf.Clamp(mode, 0, 3);
    }

    public void SetPaused(bool paused) {
        if (gainProvider != null) gainProvider.IsPaused = paused;
    }

    public void OnStartRecording() {
        _recordingFrames.Clear();
        _isRecording = true;
        _isPlayingBack = false;
        if (gainProvider != null) gainProvider.IsPaused = false;
        Debug.Log("[SimpleUI] 开始录制");
    }

    public void OnStopRecording() {
        _isRecording = false;
        Debug.Log($"[SimpleUI] 停止录制，共 {_recordingFrames.Count} 帧");
    }

    public void OnStartPlayback() {
        if (_recordingFrames.Count == 0) {
            Debug.LogWarning("[SimpleUI] 无录制数据，无法回放");
            return;
        }
        if (_playbackProvider == null) _playbackProvider = new PlaybackGainProvider();
        _playbackProvider.recording = new GainRecording {
            frames = new List<GainRecordingFrame>(_recordingFrames),
            duration = _recordingFrames.Count / recordSampleRate,
            sampleRate = recordSampleRate
        };
        _playbackProvider.currentTime = 0f;
        _playbackProvider.loop = false;
        _playbackProvider.IsPaused = false;
        if (visualizer != null) visualizer.gainProvider = _playbackProvider;
        _isPlayingBack = true;
    }

    public void OnStopPlayback() {
        _isPlayingBack = false;
        if (visualizer != null && gainProvider != null) visualizer.gainProvider = gainProvider;
    }

    public void SaveRecordingToFile() {
        if (_recordingFrames.Count == 0) return;
        var rec = new GainRecording {
            duration = _recordingFrames.Count / recordSampleRate,
            sampleRate = recordSampleRate,
            frames = new List<GainRecordingFrame>(_recordingFrames)
        };
        string path = Path.Combine(Application.persistentDataPath, recordingFileName);
        File.WriteAllText(path, JsonUtility.ToJson(rec, true));
        Debug.Log($"[SimpleUI] 录制已保存: {path}");
    }

    public void LoadRecordingFromFile() {
        string path = Path.Combine(Application.persistentDataPath, recordingFileName);
        if (!File.Exists(path)) {
            Debug.LogWarning($"[SimpleUI] 文件不存在: {path}");
            return;
        }
        var rec = JsonUtility.FromJson<GainRecording>(File.ReadAllText(path));
        if (rec?.frames != null) {
            _recordingFrames = rec.frames;
            Debug.Log($"[SimpleUI] 已加载录制: {_recordingFrames.Count} 帧");
        }
    }

    void Update() {
        if (_isRecording && gainProvider != null && !_isPlayingBack) {
            float maxFrames = maxRecordingSeconds * recordSampleRate;
            if (_recordingFrames.Count >= maxFrames) {
                _isRecording = false;
                return;
            }
            var frame = new GainRecordingFrame {
                time = _recordingFrames.Count / recordSampleRate,
                gains = new List<SpeakerGainSnapshot>()
            };
            int n = gainProvider.SpeakerCount;
            var buf = new float[n];
            gainProvider.GetGains(buf);
            for (int i = 0; i < n; i++) {
                frame.gains.Add(new SpeakerGainSnapshot {
                    speakerId = i,
                    rawGain = buf[i],
                    smoothedGain = visualizer != null ? visualizer.GetSmoothedGain(i) : buf[i]
                });
            }
            _recordingFrames.Add(frame);
        }

        if (_isPlayingBack && _playbackProvider != null && visualizer != null) {
            _playbackProvider.currentTime += Time.deltaTime;
            if (_playbackProvider.currentTime >= _playbackProvider.recording.duration) {
                OnStopPlayback();
            }
        }
    }

    /// <summary>供 Slider.onValueChanged 绑定：平滑时间常数 tau。</summary>
    public void SetSmoothTau(float tau) {
        if (gainProvider != null) gainProvider.smoothTau = Mathf.Max(0.01f, tau);
    }

    /// <summary>供 Button.onClick 绑定的辅助：设置 Intensity。</summary>
    public void SetIntensity(float v) {
        if (gainProvider != null) gainProvider.intensity = v;
    }
}

}
