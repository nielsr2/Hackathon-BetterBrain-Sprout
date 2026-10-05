using Cinema;
using Nib.ProcTree;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds a <see cref="CinemaDirector"/> with its four shots (orbit, seedling close-up, medium,
/// full tree) to the open scene and points it at the camera that owns the Kino volume.
/// Re-running keeps existing shots and their tuning; only missing pieces are created.
/// </summary>
public static class CinemaSetup
{
    const string CameraName = "Demo Camera";

    [MenuItem("Tools/Cinema/Setup In Open Scene")]
    public static void Setup()
    {
        var director = Object.FindAnyObjectByType<CinemaDirector>();
        if (director == null)
        {
            var go = new GameObject("Cinema");
            Undo.RegisterCreatedObjectUndo(go, "Add Cinema");
            director = go.AddComponent<CinemaDirector>();
        }

        var camGo = GameObject.Find(CameraName);
        director.targetCamera = camGo != null ? camGo.GetComponent<Camera>() : Camera.main;
        director.tree = Object.FindAnyObjectByType<ProceduralTree>();
        director.sequence = Object.FindAnyObjectByType<SequenceController>();
        if (director.targetCamera == null) Debug.LogWarning("[Cinema] No camera found to drive.");
        if (director.tree == null) Debug.LogWarning("[Cinema] No ProceduralTree in the open scene.");

        director.orbit = Shot<OrbitShot>(director, "Shot 1 - Orbit", created: s =>
        {
            s.fieldOfView = 38f;
        });
        director.closeUp = Shot<TrackTreeShot>(director, "Shot 2 - Seedling Close-Up", created: s =>
        {
            s.frame = TrackTreeShot.Frame.LiveTree;
            s.fieldOfView = 30f;
            s.nearClip = 0.01f;
            s.padding = 1.6f;
            s.minRadius = 0.15f;
            s.aimHeight = 0.45f;
            s.azimuth = 20f;
            s.elevation = 8f;
            s.driftDegreesPerSecond = 1.5f;
            s.smoothSeconds = 1.2f;
        });
        director.medium = Shot<TrackTreeShot>(director, "Shot 3 - Medium", created: s =>
        {
            s.frame = TrackTreeShot.Frame.LiveTree;
            s.fieldOfView = 35f;
            s.nearClip = 0.05f;
            s.padding = 2.2f;
            s.minRadius = 1.5f;
            s.aimHeight = 0.45f;
            s.azimuth = -25f;
            s.elevation = 12f;
            s.driftDegreesPerSecond = 1f;
            s.smoothSeconds = 2f;
        });
        director.fullTree = Shot<TrackTreeShot>(director, "Shot 4 - Full Tree", created: s =>
        {
            s.frame = TrackTreeShot.Frame.MatureTree;
            s.fieldOfView = 40f;
            s.nearClip = 0.1f;
            s.padding = 1.15f;
            s.aimHeight = 0.5f;
            s.azimuth = 0f;
            s.elevation = 6f;
            s.smoothSeconds = 2f;
        });

        EditorUtility.SetDirty(director);
        EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
        Selection.activeObject = director.gameObject;
        Debug.Log($"[Cinema] Director on '{director.name}' driving '{(director.targetCamera != null ? director.targetCamera.name : "none")}'. Keys in Play: F1–F4 shots, F5 auto.");
    }

    static T Shot<T>(CinemaDirector director, string name, System.Action<T> created) where T : CinemaShot
    {
        var t = director.transform.Find(name);
        if (t != null && t.TryGetComponent(out T existing)) return existing;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Add Cinema Shot");
        go.transform.SetParent(director.transform, false);
        var shot = go.AddComponent<T>();
        created(shot);
        return shot;
    }
}
