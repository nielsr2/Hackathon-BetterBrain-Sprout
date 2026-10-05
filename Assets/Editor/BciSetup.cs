using Cinema;
using Nib.ProcTree;
using Relaxation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes <c>oak_bci.unity</c>: a copy of oak2 stripped of the cinematic layer (Sequence, Cinema,
/// Film and their Timelines) so it is only the EEG → tree loop: the camera frames the seedling,
/// the relaxation driver owns growth from the first frame, and the EEG signal overlay is added.
/// Re-running on an existing copy re-applies the setup without re-copying.
/// </summary>
public static class BciSetup
{
    const string SourcePath = "Assets/oak2.unity";
    const string ScenePath = "Assets/oak_bci.unity";
    const string CameraName = "Demo Camera";
    static readonly string[] CinematicRoots = { "Sequence", "Cinema", "Film" };

    [MenuItem("Tools/BCI/Create BCI Scene (copy of oak2)")]
    public static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(SourcePath, ScenePath))
        {
            Debug.LogError($"[BCI] Could not copy {SourcePath} to {ScenePath}.");
            return;
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Apply();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[BCI] {ScenePath} ready. Play: seedling close-up, overlay top right (O/W), R or button resets growth.");
    }

    [MenuItem("Tools/BCI/Apply BCI Setup To Open Scene")]
    public static void Apply()
    {
        foreach (var name in CinematicRoots)
        {
            var go = GameObject.Find(name);
            if (go != null) { Undo.DestroyObjectImmediate(go); Debug.Log($"[BCI] Removed '{name}'."); }
        }

        // oak2 has a second MainCamera-tagged camera; keep only the one with the Kino volume.
        var camGo = GameObject.Find(CameraName);
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
        {
            if (cam.gameObject == camGo || !cam.CompareTag("MainCamera")) continue;
            Undo.RecordObject(cam.gameObject, "Disable extra camera");
            cam.gameObject.SetActive(false);
            Debug.Log($"[BCI] Disabled extra camera '{cam.name}'.");
        }

        var tree = Object.FindAnyObjectByType<ProceduralTree>();
        if (camGo == null) Debug.LogWarning($"[BCI] No '{CameraName}' found; add a Seedling Focus Camera by hand.");
        else
        {
            var focus = camGo.GetComponent<SeedlingFocusCamera>();
            if (focus == null) focus = Undo.AddComponent<SeedlingFocusCamera>(camGo);
            focus.tree = tree;
        }

        var driver = Object.FindAnyObjectByType<RelaxationTreeDriver>();
        if (driver == null) Debug.LogWarning("[BCI] No RelaxationTreeDriver in the scene.");
        else
        {
            Undo.RecordObject(driver, "BCI driver");
            driver.interactive = true; // no Timeline here: the driver owns growth from the start
            driver.tree = tree;
            EditorUtility.SetDirty(driver);
        }

        var receiver = Object.FindAnyObjectByType<UnicornBandReceiver>();
        if (receiver == null) Debug.LogWarning("[BCI] No UnicornBandReceiver in the scene.");
        else
        {
            var overlay = receiver.GetComponent<EegSignalOverlay>();
            if (overlay == null) overlay = Undo.AddComponent<EegSignalOverlay>(receiver.gameObject);
            overlay.receiver = receiver;
            overlay.driver = driver;
            EditorUtility.SetDirty(overlay);
        }
    }
}
