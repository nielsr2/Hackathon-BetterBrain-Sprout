using Gtec.UnityInterface;
using Relaxation;
using TMPro;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Video;

/// <summary>
/// Makes <c>StayContinue.unity</c>: a looping video filling an orthographic camera, with the g.tec
/// "BCI Visual ERP 2D" rig on top trimmed to two flash tags labelled STAY and CONTINUE (plus the
/// g.tec training tag). The video goes through a RenderTexture onto an HDRP/Unlit quad, with a
/// fixed-exposure, no-tonemap volume so the footage shows at its own brightness.
/// Re-running rebuilds the scene from scratch.
/// </summary>
public static class StayContinueSetup
{
    const string Folder = "Assets/StayContinue";
    const string ScenePath = Folder + "/StayContinue.unity";
    const string VideoPath = "Assets/Videos/Film_1a_Orbit.mp4";
    const string BciPrefabPath = "Assets/g.tec/Unity Interface/Prefabs/BCI/BCI Visual ERP 2D.prefab";
    const string EegPipelinePrefabPath = "Assets/g.tec/Unity Interface/Prefabs/Pipelines/EEGData/EEGDataPipeline.prefab";
    const string RenderTexturePath = Folder + "/VideoRT.renderTexture";
    const string MaterialPath = Folder + "/M_VideoScreen.mat";
    const string VolumePath = Folder + "/StayContinueVolume.asset";
    const string LabelMaterialPath = Folder + "/M_ButtonLabel.mat";

    const float OrthoSize = 5f;
    const float ButtonY = -2.2f;
    const float ButtonX = 5f;
    const int TrainingClass = 1, StayClass = 2, ContinueClass = 3;

    [MenuItem("Tools/BCI/Create Stay-Continue Scene")]
    public static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPath);
        var bciPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BciPrefabPath);
        if (clip == null) { Debug.LogError($"[StayContinue] No VideoClip at {VideoPath}."); return; }
        if (bciPrefab == null) { Debug.LogError($"[StayContinue] No prefab at {BciPrefabPath}."); return; }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildCamera();
        BuildVolume();
        BuildVideo(clip);
        var (bci, stay, cont) = BuildButtons(bciPrefab);
        BuildEegBridge(bci);
        BuildEventSystem();

        var choice = new GameObject("Stay Continue Choice").AddComponent<StayContinueChoice>();
        choice.stay = stay;
        choice.continueTag = cont;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[StayContinue] {ScenePath} ready: {clip.name} under STAY / CONTINUE g.tec flash tags.");
    }

    static void BuildCamera()
    {
        var go = new GameObject("Main Camera") { tag = "MainCamera" };
        go.transform.position = new Vector3(0, 0, -10);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = OrthoSize;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        go.AddComponent<AudioListener>();
        var hd = go.AddComponent<HDAdditionalCameraData>();
        hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        hd.backgroundColorHDR = Color.black;
    }

    static void BuildVolume()
    {
        AssetDatabase.DeleteAsset(VolumePath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, VolumePath);

        var exposure = profile.Add<Exposure>(true);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(0f);
        profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
        profile.Add<Fog>(true).enabled.Override(false);
        foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
        AssetDatabase.SaveAssets();

        var go = new GameObject("Video Volume");
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100;
        volume.sharedProfile = profile;
    }

    static void BuildVideo(VideoClip clip)
    {
        AssetDatabase.DeleteAsset(RenderTexturePath);
        var rt = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "VideoRT" };
        AssetDatabase.CreateAsset(rt, RenderTexturePath);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("HDRP/Unlit"));
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        mat.SetTexture("_UnlitColorMap", rt);
        mat.SetColor("_UnlitColor", Color.white);
        HDMaterial.ValidateMaterial(mat);
        EditorUtility.SetDirty(mat);

        // The quad sits behind the flash tags and fills the camera's height; width follows the clip.
        var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "Video Screen";
        Object.DestroyImmediate(screen.GetComponent<Collider>());
        float height = OrthoSize * 2f;
        screen.transform.position = new Vector3(0, 0, 10);
        screen.transform.localScale = new Vector3(height * clip.width / clip.height, height, 1);
        screen.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var player = screen.AddComponent<VideoPlayer>();
        player.source = VideoSource.VideoClip;
        player.clip = clip;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = rt;
        player.isLooping = true;
        player.playOnAwake = true;
    }

    static (GameObject bci, ERPTag stay, ERPTag cont) BuildButtons(GameObject bciPrefab)
    {
        var bci = (GameObject)PrefabUtility.InstantiatePrefab(bciPrefab);
        PrefabUtility.UnpackPrefabInstance(bci, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        ERPTag stay = null, cont = null;
        foreach (var tag in bci.GetComponentsInChildren<ERPFlashTag2D>(true))
        {
            if (tag.IsTrainingObject || tag.ClassId == TrainingClass)
            {
                tag.transform.position = new Vector3(0, ButtonY, 0);
                tag.gameObject.name = "Training Tag";
            }
            else if (tag.ClassId == StayClass) stay = MakeButton(tag, "STAY", new Vector3(-ButtonX, ButtonY, 0));
            else if (tag.ClassId == ContinueClass) cont = MakeButton(tag, "CONTINUE", new Vector3(ButtonX, ButtonY, 0));
            else Object.DestroyImmediate(tag.gameObject);
        }

        if (stay == null || cont == null)
            Debug.LogError("[StayContinue] Expected flash tags with ClassId 2 and 3 in the g.tec prefab.");
        return (bci, stay, cont);
    }

    // The headset takes one client: the g.tec Device owns it, and an EEGDataPipeline beside the ERP
    // pipeline feeds the relaxation receiver in-process instead of the Unicorn UDP app.
    static void BuildEegBridge(GameObject bci)
    {
        var pipelinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EegPipelinePrefabPath);
        if (pipelinePrefab == null) { Debug.LogError($"[StayContinue] No prefab at {EegPipelinePrefabPath}."); return; }

        var sibling = bci.GetComponentInChildren<SignalQualityPipeline>(true);
        var parent = sibling != null ? sibling.transform.parent : bci.transform;
        var pipelineGo = (GameObject)PrefabUtility.InstantiatePrefab(pipelinePrefab, parent);
        var pipeline = pipelineGo.GetComponent<EEGDataPipeline>();
        pipeline.Mode = Gtec.Chain.Common.SignalProcessingPipelines.DataPipelineMode.Raw; // RawEegProcessor does its own filtering

        var go = new GameObject("EEG Receiver");
        var receiver = go.AddComponent<UnicornBandReceiver>();
        receiver.source = UnicornBandReceiver.Source.External;
        var bridge = go.AddComponent<GtecEegBridge>();
        bridge.pipeline = pipeline;
        bridge.receiver = receiver;
        go.AddComponent<EegSignalOverlay>().receiver = receiver;
    }

    internal static ERPTag MakeButton(ERPFlashTag2D tag, string text, Vector3 position)
    {
        tag.gameObject.name = $"{text} Button";
        tag.transform.position = position;

        var label = new GameObject("Label");
        label.transform.SetParent(tag.transform, false);
        label.transform.localPosition = new Vector3(0, -1.5f, -0.1f);
        var tmp = label.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 8;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.fontSharedMaterial = LabelMaterial(tmp.font);
        tmp.rectTransform.sizeDelta = new Vector2(6, 1.5f);
        return tag;
    }

    // Shared outline preset: setting outlineWidth on the component would instance a material per label.
    static Material LabelMaterial(TMP_FontAsset font)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(LabelMaterialPath);
        if (mat == null)
        {
            mat = new Material(font.material) { name = "M_ButtonLabel" };
            AssetDatabase.CreateAsset(mat, LabelMaterialPath);
        }
        mat.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.25f);
        mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void BuildEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }
}
