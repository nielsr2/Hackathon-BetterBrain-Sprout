using Cinema;
using Gtec.UnityInterface;
using Nib.ProcTree;
using Relaxation;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Timeline;
using UnityEngine.Video;

/// <summary>
/// Makes <c>Assets/Session/oak_session.unity</c>, the full session run by <see cref="SessionFlow"/>:
/// a copy of oak2 without its old Sequence/Film layer, plus two timelines (looping golden-hour
/// orbit round the grown tree; fast orbit over the bare ground with the sprout pop, then the
/// interactive cameras), the g.tec rig on its own "SyncScreen" layer with an orthographic sync
/// camera, and the EEG receiver fed from the g.tec Device (the headset takes one client).
/// Re-running re-applies the setup to the existing copy; timeline assets are never overwritten.
/// </summary>
public static class SessionSceneSetup
{
    const string Folder = "Assets/Session";
    const string SourcePath = "Assets/oak2.unity";
    const string ScenePath = Folder + "/oak_session.unity";
    const string GrownPath = Folder + "/Session_GrownOrbit.playable";
    const string SproutPath = Folder + "/Session_SproutInteractive.playable";
    const string SyncVolumePath = Folder + "/SyncScreenVolume.asset";
    const string IntroPath = "Assets/Videos/UnityPart1.mov";
    const string LogoPath = "Assets/Videos/LogoSprout.webm";
    const string GlitchVideoPath = "Assets/Videos/Film_1b_LoadingPan.mp4"; // placeholder
    const string SpookyVideoPath = "Assets/Videos/Film_1a_Orbit.mp4";      // placeholder until the spooky clip exists
    const string BciPrefabPath = "Assets/g.tec/Unity Interface/Prefabs/BCI/BCI Visual ERP 2D.prefab";
    const string EegPipelinePrefabPath = "Assets/g.tec/Unity Interface/Prefabs/Pipelines/EEGData/EEGDataPipeline.prefab";
    const string CameraName = "Demo Camera";
    const string SyncLayerName = "SyncScreen";
    const int SyncLayerSlot = 8;
    static readonly Vector3 RigOrigin = new Vector3(0f, -1000f, 0f); // far below the hill, out of every shot
    static readonly string[] OldRoots = { "Sequence", "Film" };
    const string ChoiceVideoMaterialPath = Folder + "/M_ChoiceVideo.mat";
    const string ChoiceCsvPath = "Assets/StreamingAssets/Choices/choices.csv";
    const int TrainingClass = 1, LeftClass = 2, RightClass = 3;
    const float SyncOrthoSize = 5f, ButtonX = 5f, ButtonY = -1.2f, TrainingY = 0.8f;

    const float GrownSeconds = 90f, SproutSeconds = 7.5f, SessionSeconds = 300f;

    [MenuItem("Tools/Session/Create Session Scene (copy of oak2)")]
    public static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Session");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(SourcePath, ScenePath))
        {
            Debug.LogError($"[Session] Could not copy {SourcePath} to {ScenePath}.");
            return;
        }
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!Apply()) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettings();
        Debug.Log($"[Session] {ScenePath} ready. Play runs logo → connect → calibration → baseline → sprout → EEG tree → glitch videos. N skips a step.");
    }

    [MenuItem("Tools/Session/Apply Session Setup To Open Scene")]
    public static bool Apply()
    {
        int syncLayer = EnsureLayer();
        if (syncLayer < 0) return false;

        foreach (var name in OldRoots)
        {
            var go = GameObject.Find(name);
            if (go != null) { Undo.DestroyObjectImmediate(go); Debug.Log($"[Session] Removed '{name}'."); }
        }

        var treeCam = GameObject.Find(CameraName)?.GetComponent<Camera>();
        if (treeCam == null) { Debug.LogError($"[Session] No '{CameraName}' camera in the scene."); return false; }
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
        {
            if (cam == treeCam || !cam.CompareTag("MainCamera")) continue;
            cam.gameObject.SetActive(false);
            Debug.Log($"[Session] Disabled extra camera '{cam.name}'.");
        }
        treeCam.cullingMask &= ~(1 << syncLayer);
        if (treeCam.TryGetComponent(out HDAdditionalCameraData treeHd)) treeHd.volumeLayerMask &= ~(1 << syncLayer);

        var tree = Object.FindAnyObjectByType<ProceduralTree>();
        var cinema = Object.FindAnyObjectByType<CinemaDirector>();
        var driver = Object.FindAnyObjectByType<RelaxationTreeDriver>();
        var receiver = Object.FindAnyObjectByType<UnicornBandReceiver>();
        var hud = Object.FindAnyObjectByType<RelaxationHud>();
        var sun = GameObject.Find("Sun")?.GetComponent<Light>();
        var post = GameObject.Find("Post Volume")?.GetComponent<Volume>();
        if (tree == null || cinema == null || driver == null || receiver == null)
        {
            Debug.LogError("[Session] oak2 copy is missing the tree, CinemaDirector, RelaxationTreeDriver or UnicornBandReceiver.");
            return false;
        }
        cinema.sequence = null;
        cinema.targetCamera = treeCam;
        driver.tree = tree;
        driver.receiver = receiver;
        EditorUtility.SetDirty(cinema);
        EditorUtility.SetDirty(driver);

        // ── Session root ──
        var root = GameObject.Find("Session") ?? new GameObject("Session");
        var flow = GetOrAdd<SessionFlow>(root);
        var glitch = GetOrAdd<GlitchTransition>(root);
        var bar = GetOrAdd<LoadingBarOverlay>(Child(root.transform, "Loading Bar"));
        var grownDir = GetOrAdd<PlayableDirector>(Child(root.transform, "Grown Orbit"));
        var sproutDir = GetOrAdd<PlayableDirector>(Child(root.transform, "Sprout + Interactive"));

        float lux = sun != null && sun.intensity > 0f ? sun.intensity : 100000f;
        Setup(grownDir, LoadOrCreate(GrownPath, t => PopulateGrown(t, lux)), DirectorWrapMode.Loop);
        Setup(sproutDir, LoadOrCreate(SproutPath, t => PopulateSprout(t, lux)), DirectorWrapMode.Hold);
        foreach (var d in new[] { grownDir, sproutDir }) Bind(d, cinema, tree, sun, post);

        // ── EEG from the g.tec Device ──
        var (sync, eegPipeline) = BuildRig(syncLayer);
        receiver.source = UnicornBandReceiver.Source.External;
        var bridge = GetOrAdd<GtecEegBridge>(receiver.gameObject);
        bridge.receiver = receiver;
        bridge.pipeline = eegPipeline;
        var overlay = GetOrAdd<EegSignalOverlay>(receiver.gameObject);
        overlay.receiver = receiver;
        overlay.driver = driver;
        foreach (var o in new Object[] { receiver, bridge, overlay }) EditorUtility.SetDirty(o);

        var syncCam = BuildSyncCamera(syncLayer, sync);
        BuildEventSystem();

        // ── Wire the flow ──
        flow.driver = driver;
        flow.hud = hud;
        flow.signalOverlay = overlay;
        flow.glitch = glitch;
        flow.loadingBar = bar;
        flow.sync = sync;
        flow.choice = sync != null ? sync.GetComponent<ChoiceScreen>() : null;
        if (!System.IO.File.Exists(ChoiceCsvPath))
            Debug.LogWarning($"[Session] No {ChoiceCsvPath} yet; the session will end on the spooky video until it exists.");
        flow.treeCamera = treeCam;
        flow.syncCamera = syncCam;
        flow.grownTimeline = grownDir;
        flow.sproutTimeline = sproutDir;
        flow.interactiveAt = 7f;
        flow.introClip = AssetDatabase.LoadAssetAtPath<VideoClip>(IntroPath);
        if (flow.introClip == null) Debug.LogWarning($"[Session] No intro clip at {IntroPath}; the session starts with the logo.");
        flow.logoClip = LoadLogo();
        flow.glitchVideo = AssetDatabase.LoadAssetAtPath<VideoClip>(GlitchVideoPath);
        if (flow.spookyVideo == null) flow.spookyVideo = AssetDatabase.LoadAssetAtPath<VideoClip>(SpookyVideoPath);
        if (flow.logoClip == null) Debug.LogWarning($"[Session] No logo clip at {LogoPath}.");
        foreach (var o in new Object[] { flow, glitch, bar, grownDir, sproutDir }) EditorUtility.SetDirty(o);
        EditorSceneManager.MarkSceneDirty(root.scene);
        return true;
    }

    // ── Timelines ───────────────────────────────────────────────────────────

    [MenuItem("Tools/Session/Rebuild Session Timelines")]
    public static void RebuildTimelines()
    {
        if (!EditorUtility.DisplayDialog("Rebuild session timelines",
                $"Delete and recreate\n{GrownPath}\n{SproutPath}\nfrom SessionSceneSetup? Hand edits to them are lost.", "Rebuild", "Cancel"))
            return;
        AssetDatabase.DeleteAsset(GrownPath);
        AssetDatabase.DeleteAsset(SproutPath);
        if (Apply()) EditorSceneManager.SaveOpenScenes();
    }

    // Film 1a's framing as an endless, even orbit (radius and height constant so the loop is seamless).
    static void PopulateGrown(TimelineAsset t, float lux)
    {
        var cams = t.CreateTrack<CinemaTrack>(null, "Cameras");
        var orbit = TimelineSetup.Clip<OrbitClip>(cams, "Orbit (loop)", 0, GrownSeconds);
        orbit.startAngle = -45f;
        orbit.sweepDegrees = 360f;
        orbit.startRadius = orbit.endRadius = 27f;
        orbit.startHeight = orbit.endHeight = 4f;
        orbit.lookOffset = new Vector3(0f, 7f, 0f);
        orbit.settleSeconds = GrownSeconds;
        orbit.easing = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        orbit.fieldOfView = 38f;

        var growth = t.CreateTrack<TreeGrowthTrack>(null, "Tree Growth");
        TimelineSetup.Clip<GrowthClip>(growth, "Fully grown", 0, GrownSeconds).growth = AnimationCurve.Constant(0f, 1f, 1f);

        // Same morning light the interactive part opens on (golden hour read too warm here).
        var sun = t.CreateTrack<SunTrack>(null, "Sun");
        TimelineSetup.Sun(sun, "Morning", 0, GrownSeconds, 18f, 40f, lux * 0.85f, 4800f);

        var atmo = t.CreateTrack<AtmosphereTrack>(null, "Atmosphere");
        TimelineSetup.Clip<AtmosphereClip>(atmo, "Morning mist", 0, GrownSeconds).meanFreePath = 160f;
    }

    // A fast, low orbit closing in on the bare sprout spot; the sprout pops halfway, then the
    // interactive part (close-up → director auto, morning → golden hour, as in Session 2).
    static void PopulateSprout(TimelineAsset t, float lux)
    {
        var cams = t.CreateTrack<CinemaTrack>(null, "Cameras");
        var orbit = TimelineSetup.Clip<OrbitClip>(cams, "Bare ground orbit", 0, SproutSeconds);
        orbit.startAngle = 80f;
        orbit.sweepDegrees = -120f;
        orbit.startRadius = 7f;
        orbit.endRadius = 1.3f;
        orbit.startHeight = 2.5f;
        orbit.endHeight = 0.3f;
        orbit.lookOffset = new Vector3(0f, 0.12f, 0f);
        orbit.settleSeconds = SproutSeconds - 0.5f;
        orbit.fieldOfView = 32f;
        orbit.nearClip = 0.02f;
        var closeUp = cams.CreateClip<CinemaShotClip>();
        closeUp.start = SproutSeconds - 1.5;
        closeUp.duration = 4.5;
        closeUp.displayName = "CloseUp";
        ((CinemaShotClip)closeUp.asset).shot = Shot.CloseUp;
        TimelineSetup.Clip<DirectorAutoClip>(cams, "Director Auto (widen with growth)", SproutSeconds + 3, SessionSeconds - SproutSeconds - 3);

        // Growth: bare, then a quick pop with a little overshoot. The EEG takes over at SessionFlow.interactiveAt.
        var growth = t.CreateTrack<TreeGrowthTrack>(null, "Tree Growth");
        var pop = TimelineSetup.Clip<GrowthClip>(growth, "Sprout pop", 0, SproutSeconds);
        pop.growth = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0f), new Keyframe(0.6f, 0.065f),
            new Keyframe(0.68f, 0.05f), new Keyframe(1f, 0.05f));

        var sun = t.CreateTrack<SunTrack>(null, "Sun");
        TimelineSetup.Sun(sun, "Morning", 0, 120, 18f, 40f, lux * 0.85f, 4800f);
        // Only a little warmer than morning: Film 1a's golden hour (7°, 3000 K, warm grade) read far too warm here.
        TimelineSetup.Sun(sun, "Late afternoon", 60, SessionSeconds - 60, 12f, 30f, lux * 0.7f, 4000f);

        var atmo = t.CreateTrack<AtmosphereTrack>(null, "Atmosphere");
        TimelineSetup.Clip<AtmosphereClip>(atmo, "Morning mist", 0, 120).meanFreePath = 160f;
        SoftWarmth(TimelineSetup.Clip<AtmosphereClip>(atmo, "Soft warmth", 60, SessionSeconds - 60));
    }

    // A gentle version of TimelineSetup.WarmGrade: no exposure override, about a third of its grade.
    static void SoftWarmth(AtmosphereClip a)
    {
        a.meanFreePath = 260f;
        a.fogAlbedo = new Color(1f, 0.95f, 0.88f);
        a.grade = true;
        a.contrast = 3f;
        a.saturation = 4f;
        a.colorFilter = new Color(1f, 0.97f, 0.92f);
    }

    static TimelineAsset LoadOrCreate(string path, System.Action<TimelineAsset> populate)
    {
        var t = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
        if (t != null) return t;
        t = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(t, path);
        populate(t);
        EditorUtility.SetDirty(t);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Session] Created {path}.");
        return t;
    }

    static void Setup(PlayableDirector d, TimelineAsset t, DirectorWrapMode wrap)
    {
        d.playableAsset = t;
        d.playOnAwake = false;
        d.extrapolationMode = wrap;
    }

    static void Bind(PlayableDirector director, CinemaDirector cinema, ProceduralTree tree, Light sun, Volume post)
    {
        if (director.playableAsset is not TimelineAsset timeline) return;
        foreach (var track in timeline.GetOutputTracks())
        {
            Object target = track switch
            {
                CinemaTrack => cinema,
                TreeGrowthTrack => tree,
                SunTrack => sun,
                AtmosphereTrack => post,
                _ => null,
            };
            if (target != null) director.SetGenericBinding(track, target);
        }
    }

    // ── g.tec rig + sync camera ─────────────────────────────────────────────

    static (GtecSync sync, EEGDataPipeline eeg) BuildRig(int layer)
    {
        var existing = Object.FindAnyObjectByType<GtecSync>();
        if (existing != null && existing.TryGetComponent(out ChoiceScreen _))
            return (existing, existing.GetComponentInChildren<EEGDataPipeline>(true));
        if (existing != null)
        {
            // The first version kept all of the prefab's tags; the choice needs the trimmed layout.
            Undo.DestroyObjectImmediate(existing.gameObject);
            Debug.Log("[Session] Replaced the old g.tec rig with the two-button layout.");
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BciPrefabPath);
        var pipelinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EegPipelinePrefabPath);
        if (prefab == null || pipelinePrefab == null)
        {
            Debug.LogError($"[Session] Missing g.tec prefab ({BciPrefabPath} or {EegPipelinePrefabPath}).");
            return (null, null);
        }

        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        rig.transform.position = RigOrigin;

        // EEG stream for the relaxation loop, beside the ERP pipeline under the same Device.
        var sibling = rig.GetComponentInChildren<SignalQualityPipeline>(true);
        var pipelineGo = (GameObject)PrefabUtility.InstantiatePrefab(pipelinePrefab, sibling != null ? sibling.transform.parent : rig.transform);
        var eeg = pipelineGo.GetComponent<EEGDataPipeline>();
        eeg.Mode = Gtec.Chain.Common.SignalProcessingPipelines.DataPipelineMode.Raw; // RawEegProcessor filters itself

        var choice = BuildChoiceLayout(rig);

        foreach (var t in rig.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        var sync = rig.AddComponent<GtecSync>();
        sync.device = rig.GetComponentInChildren<Device>(true);
        sync.paradigm = rig.GetComponentInChildren<ERPParadigm>(true);
        sync.pipeline = rig.GetComponentInChildren<ERPPipeline>(true);
        sync.uiCanvases = rig.GetComponentsInChildren<Canvas>(true);
        if (sync.device == null || sync.paradigm == null || sync.pipeline == null)
            Debug.LogError("[Session] g.tec rig is missing its Device, ERPParadigm or ERPPipeline.");
        return (sync, eeg);
    }

    // The StayContinue layout: the training tag in the middle and two class tags left and right — the
    // classifier is trained on exactly the tags the final choice uses. Labels stay empty until the choice.
    // Behind them, a full-screen quad for the video under the choice.
    static ChoiceScreen BuildChoiceLayout(GameObject rig)
    {
        var choice = rig.AddComponent<ChoiceScreen>();
        foreach (var tag in rig.GetComponentsInChildren<ERPFlashTag2D>(true))
        {
            if (tag.IsTrainingObject || tag.ClassId == TrainingClass)
            {
                tag.transform.position = RigOrigin + new Vector3(0f, TrainingY, 0f);
                tag.gameObject.name = "Training Tag";
                choice.trainingTag = tag.gameObject;
            }
            else if (tag.ClassId == LeftClass)
                choice.left = StayContinueSetup.MakeButton(tag, "", RigOrigin + new Vector3(-ButtonX, ButtonY, 0f));
            else if (tag.ClassId == RightClass)
                choice.right = StayContinueSetup.MakeButton(tag, "", RigOrigin + new Vector3(ButtonX, ButtonY, 0f));
            else Object.DestroyImmediate(tag.gameObject);
        }
        if (choice.left == null || choice.right == null || choice.trainingTag == null)
            Debug.LogError("[Session] Expected the training tag and class tags 2 and 3 in the g.tec prefab.");
        if (choice.left != null) choice.left.gameObject.name = "Left Button";
        if (choice.right != null) choice.right.gameObject.name = "Right Button";

        var mat = AssetDatabase.LoadAssetAtPath<Material>(ChoiceVideoMaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("HDRP/Unlit"));
            mat.SetColor("_UnlitColor", Color.white);
            HDMaterial.ValidateMaterial(mat);
            AssetDatabase.CreateAsset(mat, ChoiceVideoMaterialPath);
        }
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Choice Video";
        Object.DestroyImmediate(quad.GetComponent<Collider>()); // never in the way of the tag clicks
        quad.transform.SetParent(rig.transform, false);
        quad.transform.position = RigOrigin + new Vector3(0f, 0f, 10f); // behind the tags, inside the far clip
        quad.transform.localScale = new Vector3(SyncOrthoSize * 2f * 16f / 9f, SyncOrthoSize * 2f, 1f);
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.enabled = false;
        choice.videoQuad = r;
        return choice;
    }

    static Camera BuildSyncCamera(int layer, GtecSync sync)
    {
        var go = GameObject.Find("Sync Camera") ?? new GameObject("Sync Camera");
        go.layer = layer;
        var cam = GetOrAdd<Camera>(go);
        var hd = GetOrAdd<HDAdditionalCameraData>(go);
        cam.orthographic = true;
        cam.cullingMask = 1 << layer;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        cam.enabled = false; // SessionFlow switches it on for the sync screen
        hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        hd.backgroundColorHDR = Color.black;
        hd.volumeLayerMask = 1 << layer;

        // Fixed framing for the two-button layout (titles sit below the buttons).
        cam.orthographicSize = SyncOrthoSize;
        go.transform.SetPositionAndRotation(RigOrigin + new Vector3(0f, 0f, -10f), Quaternion.identity);
        if (sync != null && sync.TryGetComponent(out ChoiceScreen choice))
        {
            choice.syncCamera = cam;
            EditorUtility.SetDirty(choice);
        }

        // Flash tags at their own brightness: fixed exposure, no tonemapping or fog, seen only by this camera.
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SyncVolumePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, SyncVolumePath);
            var exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(0f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
            profile.Add<Fog>(true).enabled.Override(false);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            AssetDatabase.SaveAssets();
        }
        var volGo = GameObject.Find("Sync Screen Volume") ?? new GameObject("Sync Screen Volume");
        volGo.layer = layer;
        var vol = GetOrAdd<Volume>(volGo);
        vol.isGlobal = true;
        vol.priority = 100;
        vol.sharedProfile = profile;
        return cam;
    }

    static int EnsureLayer()
    {
        int existing = LayerMask.NameToLayer(SyncLayerName);
        if (existing >= 0) return existing;
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        var slot = layers.GetArrayElementAtIndex(SyncLayerSlot);
        if (!string.IsNullOrEmpty(slot.stringValue))
        {
            Debug.LogError($"[Session] Layer {SyncLayerSlot} is taken ('{slot.stringValue}'); add a '{SyncLayerName}' layer by hand.");
            return -1;
        }
        slot.stringValue = SyncLayerName;
        tagManager.ApplyModifiedProperties();
        Debug.Log($"[Session] Added layer {SyncLayerSlot} '{SyncLayerName}'.");
        return SyncLayerSlot;
    }

    // ── Misc ────────────────────────────────────────────────────────────────

    // VP8 WebM with alpha: keep the alpha channel so the logo sits over the scene.
    static VideoClip LoadLogo()
    {
        if (AssetImporter.GetAtPath(LogoPath) is VideoClipImporter imp && !imp.keepAlpha)
        {
            imp.keepAlpha = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<VideoClip>(LogoPath);
    }

    static void BuildEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes;
        foreach (var s in scenes) if (s.path == ScenePath) return;
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes) { new EditorBuildSettingsScene(ScenePath, true) };
        EditorBuildSettings.scenes = list.ToArray();
    }

    // Not `GetComponent() ?? Add()`: in the editor a missing component is a fake-null object, so ?? never falls through.
    static T GetOrAdd<T>(GameObject go) where T : Component
        => go.TryGetComponent(out T c) ? c : Undo.AddComponent<T>(go);

    static GameObject Child(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t.gameObject;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }
}
