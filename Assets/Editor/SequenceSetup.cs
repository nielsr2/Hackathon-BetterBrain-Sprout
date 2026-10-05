using Relaxation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds the intro/tree/outro <see cref="SequenceController"/> to the open scene and makes sure
/// Kino's Overlay runs before the glitch effects (so the glitch distorts the videos too).
/// </summary>
public static class SequenceSetup
{
    const string VideoFolder = "Assets/Videos";

    [MenuItem("Tools/Sequence/Setup In Open Scene")]
    public static void Setup()
    {
        KinoSetup.RegisterEffects();

        if (!AssetDatabase.IsValidFolder(VideoFolder)) AssetDatabase.CreateFolder("Assets", "Videos");

        var controller = Object.FindAnyObjectByType<SequenceController>();
        if (controller == null)
        {
            var go = new GameObject("Sequence");
            Undo.RegisterCreatedObjectUndo(go, "Add Sequence");
            controller = go.AddComponent<SequenceController>();
        }
        controller.glitch = controller.GetComponent<GlitchTransition>();
        controller.driver = Object.FindAnyObjectByType<RelaxationTreeDriver>();
        controller.hud = Object.FindAnyObjectByType<RelaxationHud>();
        if (controller.driver == null) Debug.LogWarning("[Sequence] No RelaxationTreeDriver in the open scene.");
        EditorUtility.SetDirty(controller);

        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Selection.activeObject = controller.gameObject;
        Debug.Log($"[Sequence] Set up on '{controller.name}'. Drop clips in {VideoFolder} and assign Intro/Outro Clip.");
    }
}
