using UnityEngine;

namespace SonicARray.SpeakerDebug {

/// <summary>供 UI Button 序列化回调，避免 lambda 无法保存。</summary>
public class ButtonBridge : MonoBehaviour {
    public enum Action {
        CreateAnchor, LoadAnchor, DeleteUuid, SaveOffset,
        StartRecord, StopRecord, StartPlayback, StopPlayback
    }
    public Action action;
    public SimpleUI simpleUI;

    public void OnClick() {
        if (simpleUI == null) simpleUI = FindObjectOfType<SimpleUI>();
        if (simpleUI == null) return;
        switch (action) {
            case Action.CreateAnchor: simpleUI.OnCreateAndSaveAnchor(); break;
            case Action.LoadAnchor: simpleUI.OnLoadAnchor(); break;
            case Action.DeleteUuid: simpleUI.OnDeleteLocalUuid(); break;
            case Action.SaveOffset: simpleUI.OnSaveTransformOffset(); break;
            case Action.StartRecord: simpleUI.OnStartRecording(); break;
            case Action.StopRecord: simpleUI.OnStopRecording(); break;
            case Action.StartPlayback: simpleUI.OnStartPlayback(); break;
            case Action.StopPlayback: simpleUI.OnStopPlayback(); break;
        }
    }
}

}
