using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// Adds a one-click menu toggle under "Test" to switch between PC mode and XR Simulator mode.
///
/// PC Mode    (XR off): Play in Editor works normally with mouse — no headset needed.
/// XR Mode    (XR on) : Play in Editor starts the Meta XR Simulator with A/B controller input.
///
/// Usage: Test > PC Mode (No XR)  ←→  Test > XR Simulator Mode
/// The current mode is shown with a checkmark.
/// </summary>
[InitializeOnLoad]
public static class XRModeToggle
{
    const string kMenuPC  = "Test/PC Mode (No XR) %#p";   // Ctrl+Shift+P
    const string kMenuXR  = "Test/XR Simulator Mode %#x"; // Ctrl+Shift+X

    static XRModeToggle()
    {
        EditorApplication.delayCall += SyncCheckmarks;
    }

    // ── Menu items ────────────────────────────────────────────────

    [MenuItem(kMenuPC, priority = 1)]
    static void EnablePCMode()
    {
        SetXRInitOnStart(false);
        SyncCheckmarks();
        Debug.Log("[XRMode] PC Mode — XR disabled. Mouse drag active.");
    }

    [MenuItem(kMenuXR, priority = 2)]
    static void EnableXRMode()
    {
        SetXRInitOnStart(true);
        SyncCheckmarks();
        Debug.Log("[XRMode] XR Simulator Mode — OpenXR enabled. Use A/B buttons.");
    }

    // Prevent the menu items from being greyed out
    [MenuItem(kMenuPC, true)]
    static bool ValidatePC() => true;
    [MenuItem(kMenuXR, true)]
    static bool ValidateXR() => true;

    // ── Helpers ───────────────────────────────────────────────────

    static void SyncCheckmarks()
    {
        bool xrOn = GetXRInitOnStart();
        Menu.SetChecked(kMenuPC, !xrOn);
        Menu.SetChecked(kMenuXR,  xrOn);
    }

    static XRGeneralSettings GetStandaloneXRSettings()
    {
        // Load all sub-assets from the XR settings file and find the Standalone one
        var assets = AssetDatabase.LoadAllAssetsAtPath("Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
        foreach (var asset in assets)
        {
            if (asset is XRGeneralSettings xrSettings && asset.name == "Standalone Settings")
                return xrSettings;
        }
        return null;
    }

    static bool GetXRInitOnStart()
    {
        var settings = GetStandaloneXRSettings();
        return settings != null && settings.InitManagerOnStart;
    }

    static void SetXRInitOnStart(bool value)
    {
        var settings = GetStandaloneXRSettings();
        if (settings == null)
        {
            Debug.LogWarning("[XRMode] Could not find Standalone XR General Settings in Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            return;
        }
        settings.InitManagerOnStart = value;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }
}
