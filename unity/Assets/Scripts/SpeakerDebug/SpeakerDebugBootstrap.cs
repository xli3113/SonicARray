using UnityEngine;

namespace SonicARray.SpeakerDebug {

/// <summary>启动时串联：Loader -> Spawner -> RoomAnchor -> MockGainProvider，注入 speaker 位置到 DirectionalHotspot。</summary>
public class SpeakerDebugBootstrap : MonoBehaviour {
    [Header("References")]
    public SpeakerYamlLoader loader;
    public SpeakerSpawner spawner;
    public RoomAnchorManager anchorManager;
    public MockGainProvider gainProvider;
    public SpeakerVisualizer visualizer;
    public SimpleUI simpleUI;

    void Awake() {
        if (loader != null) {
            if (loader.GetSpeakers().Count == 0) loader.Load();
        }
        if (spawner != null) {
            spawner.loader = loader;
            spawner.SpawnAll();
        }

        if (spawner != null && gainProvider != null && spawner.LocalPositions != null && spawner.LocalPositions.Count > 0) {
            gainProvider.speakerPositionsOverride = new System.Collections.Generic.List<Vector3>();
            foreach (var p in spawner.LocalPositions)
                gainProvider.speakerPositionsOverride.Add(p);
            gainProvider.SetSpeakerCount(gainProvider.speakerPositionsOverride.Count);
        }

        if (visualizer != null) {
            visualizer.gainProvider = gainProvider;
            visualizer.spawner = spawner;
            visualizer.Rebuild();
        }

        if (simpleUI != null) {
            simpleUI.gainProvider = gainProvider;
            simpleUI.anchorManager = anchorManager;
            simpleUI.visualizer = visualizer;
        }

        if (anchorManager != null && spawner != null)
            anchorManager.speakerRigParent = spawner.speakerRigParent;
    }
}

}
