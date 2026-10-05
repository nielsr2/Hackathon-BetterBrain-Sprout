using Cinema;
using Nib.ProcTree;
using Relaxation;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEditor.Recorder.Timeline;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

/// <summary>
/// Builds the three timelines and wires them into the open scene:
///   Film 1a — orbit around the grown tree at golden hour (rendered for a title overlay);
///   Film 1b — slow pan to the sprout spot under the loading bar (rendered; plays while the baseline runs);
///   Session 2 — the live EEG part: close-up on the sprout, widening with growth.
/// Film takes carry a Recorder clip (1080p30 H.264 MP4 → Assets/Videos). Re-running never overwrites an
/// existing timeline asset; it only re-creates missing scene objects and re-binds tracks.
/// </summary>
public static class TimelineSetup
{
    const string Folder = "Assets/Timeline";
    const string Film1aPath = Folder + "/Film_1a_Orbit.playable";
    const string Film1bPath = Folder + "/Film_1b_LoadingPan.playable";
    const string SessionPath = Folder + "/Session_2_Interactive.playable";
    const string RigClipPath = Folder + "/Film_1b_RigPan.anim";
    const string VideoFolder = "Assets/Videos";
    const string RigRefName = "FilmRig1b";

    const float Film1aSeconds = 24f, Film1bSeconds = 32f, SessionTimeout = 300f;
    const int Fps = 30;
    const float TargetMbps = 50f;

    // ── Menus ───────────────────────────────────────────────────────────────

    [MenuItem("Tools/Timeline/Build Film + Session Timelines")]
    public static void Build()
    {
        var controller = Object.FindAnyObjectByType<SequenceController>();
        var cinema = Object.FindAnyObjectByType<CinemaDirector>();
        if (controller == null || cinema == null)
        {
            Debug.LogError("[Timeline] Needs a SequenceController and a CinemaDirector — run Tools/Sequence/Setup and Tools/Cinema/Setup first.");
            return;
        }
        if (!AssetDatabase.IsValidFolder(VideoFolder)) AssetDatabase.CreateFolder("Assets", "Videos");

        // Scene objects: Film root (controller + loading bar), one director per take, keyframed rig.
        var film = GameObject.Find("Film");
        if (film == null) film = Created(new GameObject("Film"));
        var filmController = GetOrAdd<FilmController>(film);
        var bar = GetOrAdd<LoadingBarOverlay>(film);
        var take1a = GetOrAdd<PlayableDirector>(Child(film.transform, "Take 1a - Orbit"));
        var take1b = GetOrAdd<PlayableDirector>(Child(film.transform, "Take 1b - Loading Pan"));
        var rigRoot = Child(film.transform, "Film Camera Rig");
        var rigAnimator = GetOrAdd<Animator>(rigRoot);
        var rig = GetOrAdd<FilmCameraRig>(Child(rigRoot.transform, "Camera"));

        var tree = Object.FindAnyObjectByType<ProceduralTree>();
        var sun = GameObject.Find("Sun")?.GetComponent<Light>();
        var post = GameObject.Find("Post Volume")?.GetComponent<Volume>();
        float lux = sun != null && sun.intensity > 0f ? sun.intensity : 100000f;
        Vector3 root = tree != null ? tree.transform.position : Vector3.zero;

        var tl1a = LoadOrCreate(Film1aPath, t => Populate1a(t, lux));
        var tl1b = LoadOrCreate(Film1bPath, t => Populate1b(t, lux, root));
        var tlSession = LoadOrCreate(SessionPath, t => PopulateSession(t, lux));
        UpdateRecorders(tl1a, "Film_1a_Orbit");
        UpdateRecorders(tl1b, "Film_1b_LoadingPan");
        AssetDatabase.SaveAssets();

        Setup(take1a, tl1a, film: true);
        Setup(take1b, tl1b, film: true);
        var sessionDirector = GetOrAdd<PlayableDirector>(controller.gameObject);
        Setup(sessionDirector, tlSession, film: false);
        controller.timeline = sessionDirector;

        foreach (var d in new[] { take1a, take1b, sessionDirector })
            Bind(d, cinema, tree, sun, post, bar, rigAnimator);
        take1b.SetReferenceValue(new PropertyName(RigRefName), rig);

        filmController.takes = new[] { take1a, take1b };
        if (filmController.take == null) filmController.take = take1a;
        filmController.sequence = controller;
        filmController.hud = Object.FindAnyObjectByType<RelaxationHud>();
        filmController.tree = tree;

        // Session intro playlist: the rendered takes, once they exist.
        var v1a = AssetDatabase.LoadAssetAtPath<UnityEngine.Video.VideoClip>(VideoFolder + "/Film_1a_Orbit.mp4");
        var v1b = AssetDatabase.LoadAssetAtPath<UnityEngine.Video.VideoClip>(VideoFolder + "/Film_1b_LoadingPan.mp4");
        if (v1a != null && v1b != null)
        {
            controller.introPlaylist = new[] { v1a, v1b };
            controller.baselineWithIntroClip = 1;
        }

        foreach (var o in new Object[] { filmController, controller, take1a, take1b, sessionDirector })
            EditorUtility.SetDirty(o);
        EditorSceneManager.MarkSceneDirty(film.scene);
        Debug.Log("[Timeline] Film 1a/1b + Session timelines bound. Render with Tools/Film/Render 1a or 1b; "
                  + (v1a != null && v1b != null ? "intro playlist set to the rendered takes." : "render both takes, then re-run this to fill the intro playlist."));
    }

    [MenuItem("Tools/Film/Render 1a - Orbit")]
    public static void Render1a() => Render("Take 1a - Orbit", "Film_1a_Orbit", Film1aSeconds);

    [MenuItem("Tools/Film/Render 1b - Loading Pan")]
    public static void Render1b() => Render("Take 1b - Loading Pan", "Film_1b_LoadingPan", Film1bSeconds);

    const string RenderFlag = "TimelineSetup.RenderingTake";
    const string RenderFileKey = "TimelineSetup.RenderFile";
    const string RenderSecondsKey = "TimelineSetup.RenderSeconds";

    static void Render(string takeName, string file, float seconds)
    {
        SessionState.SetString(RenderFileKey, file);
        SessionState.SetFloat(RenderSecondsKey, seconds);
        var fc = Object.FindAnyObjectByType<FilmController>();
        var take = GameObject.Find("Film/" + takeName)?.GetComponent<PlayableDirector>();
        if (fc == null || take == null) { Debug.LogError("[Film] Run Tools/Timeline/Build Film + Session Timelines first."); return; }
        Undo.RecordObject(fc, "Render take");
        fc.mode = FilmController.Mode.Film;
        fc.take = take;
        EditorUtility.SetDirty(fc);
        SessionState.SetBool(RenderFlag, true);
        EditorApplication.EnterPlaymode();
    }

    // After a render, put the scene back in Session mode so normal Play runs the experience.
    [InitializeOnLoadMethod]
    static void RestoreSessionModeAfterRender()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(RenderFlag, false)) return;
            SessionState.SetBool(RenderFlag, false);
            var fc = Object.FindAnyObjectByType<FilmController>();
            if (fc != null) { fc.mode = FilmController.Mode.Session; EditorUtility.SetDirty(fc); }
            AssetDatabase.Refresh(); // import the new video

            string path = $"{VideoFolder}/{SessionState.GetString(RenderFileKey, "")}.mp4";
            var info = new System.IO.FileInfo(path);
            if (!info.Exists)
                Debug.LogError($"[Film] Expected {path} but it is not there — check the console for a Recorder error.");
            else if (System.DateTime.Now - info.LastWriteTime > System.TimeSpan.FromMinutes(5))
                Debug.LogWarning($"[Film] {path} was not rewritten by this render (it is older) — the recording probably failed.");
            else
            {
                float expectedMB = SessionState.GetFloat(RenderSecondsKey, 0f) * TargetMbps / 8f;
                Debug.Log($"[Film] Render finished → {path} ({info.Length / 1e6f:0.0} MB, ~{expectedMB:0} MB expected at {TargetMbps} Mbit/s). Scene back in Session mode.");
            }
        };
    }

    // ── Timeline content ────────────────────────────────────────────────────

    static void Populate1a(TimelineAsset t, float lux)
    {
        var cams = t.CreateTrack<CinemaTrack>(null, "Cameras");
        var orbit = Clip<OrbitClip>(cams, "Orbit", 0, Film1aSeconds);
        orbit.startAngle = -45f;
        orbit.sweepDegrees = 75f;
        orbit.startRadius = 30f;
        orbit.endRadius = 23f;
        orbit.startHeight = 5f;
        orbit.endHeight = 2.5f;
        orbit.lookOffset = new Vector3(0f, 7f, 0f);
        orbit.settleSeconds = 20f;
        orbit.fieldOfView = 38f;

        var growth = t.CreateTrack<TreeGrowthTrack>(null, "Tree Growth");
        Clip<GrowthClip>(growth, "Fully grown", 0, Film1aSeconds).growth = AnimationCurve.Constant(0f, 1f, 1f);

        var sun = t.CreateTrack<SunTrack>(null, "Sun");
        Sun(sun, "Golden hour", 0, Film1aSeconds, 7f, 20f, lux * 0.5f, 3000f);

        var atmo = t.CreateTrack<AtmosphereTrack>(null, "Atmosphere");
        WarmGrade(Clip<AtmosphereClip>(atmo, "Warm haze", 0, Film1aSeconds));

        Recorder(t, "Film_1a_Orbit", Film1aSeconds);
    }

    static void Populate1b(TimelineAsset t, float lux, Vector3 root)
    {
        // Keyframed rig: wide over the hill → down to the sprout spot, ending on the session's close-up framing.
        var anim = AssetDatabase.LoadAssetAtPath<AnimationClip>(RigClipPath);
        if (anim == null)
        {
            anim = PanClip(root);
            AssetDatabase.CreateAsset(anim, RigClipPath);
        }
        var rigTrack = t.CreateTrack<AnimationTrack>(null, "Film Rig (keyframes)");
        var rc = rigTrack.CreateClip(anim);
        rc.start = 0;
        rc.duration = Film1bSeconds;
        rc.displayName = "Pan to sprout";

        var cams = t.CreateTrack<CinemaTrack>(null, "Cameras");
        var free = Clip<FreeCameraClip>(cams, "Free Camera (rig)", 0, Film1bSeconds);
        free.rig = new ExposedReference<FilmCameraRig> { exposedName = new PropertyName(RigRefName) };

        var growth = t.CreateTrack<TreeGrowthTrack>(null, "Tree Growth");
        Clip<GrowthClip>(growth, "Bare hill", 0, Film1bSeconds).growth = AnimationCurve.Constant(0f, 1f, 0f);

        var sun = t.CreateTrack<SunTrack>(null, "Sun");
        Sun(sun, "Morning", 0, Film1bSeconds, 18f, 40f, lux * 0.85f, 4800f);

        var atmo = t.CreateTrack<AtmosphereTrack>(null, "Atmosphere");
        var mist = Clip<AtmosphereClip>(atmo, "Morning mist", 0, Film1bSeconds);
        mist.meanFreePath = 160f;

        var barTrack = t.CreateTrack<LoadingBarTrack>(null, "Loading Bar");
        var bar = barTrack.CreateClip<LoadingBarClip>();
        bar.start = 1.5;
        bar.duration = Film1bSeconds - 2.5;
        bar.easeInDuration = 0.8;
        bar.easeOutDuration = 0.8;
        bar.displayName = "Calibrating";

        Recorder(t, "Film_1b_LoadingPan", Film1bSeconds);
    }

    static void PopulateSession(TimelineAsset t, float lux)
    {
        var cams = t.CreateTrack<CinemaTrack>(null, "Cameras");
        Clip<CinemaShotClip>(cams, "CloseUp", 0, 4).shot = Shot.CloseUp;
        Clip<DirectorAutoClip>(cams, "Director Auto (widen with growth)", 4, SessionTimeout - 4);

        var sun = t.CreateTrack<SunTrack>(null, "Sun");
        Sun(sun, "Morning", 0, 120, 18f, 40f, lux * 0.85f, 4800f);
        Sun(sun, "Golden hour", 60, SessionTimeout - 60, 7f, 20f, lux * 0.5f, 3000f); // 60 s crossfade

        var atmo = t.CreateTrack<AtmosphereTrack>(null, "Atmosphere");
        Clip<AtmosphereClip>(atmo, "Morning mist", 0, 120).meanFreePath = 160f;
        WarmGrade(Clip<AtmosphereClip>(atmo, "Warm haze", 60, SessionTimeout - 60));

        // Baseline already ran during the 1b video; the EEG drives growth from the first frame.
        t.CreateMarkerTrack();
        Marker(t, 0, SequenceMarker.Kind.Interactive);
        Marker(t, SessionTimeout, SequenceMarker.Kind.Timeout);
    }

    internal static void WarmGrade(AtmosphereClip a)
    {
        a.meanFreePath = 350f;
        a.fogAlbedo = new Color(1f, 0.88f, 0.74f);
        a.exposure = true;
        a.fixedExposure = 13.2f;
        a.grade = true;
        a.contrast = 8f;
        a.saturation = 12f;
        a.colorFilter = new Color(1f, 0.92f, 0.8f);
    }

    static AnimationClip PanClip(Vector3 root)
    {
        // Start wide, swing round, end on the close-up framing of a just-sprouted seedling.
        var aimEnd = root + Vector3.up * 0.05f;
        var dirEnd = Quaternion.Euler(8f, 20f, 0f) * Vector3.forward;
        var keys = new[]
        {
            (t: 0f, pos: root + Quaternion.Euler(0f, -35f, 0f) * Vector3.back * 26f + Vector3.up * 6f, look: root + Vector3.up * 2f, fov: 40f),
            (t: 18f, pos: root + Quaternion.Euler(0f, 0f, 0f) * Vector3.back * 9f + Vector3.up * 2.2f, look: root + Vector3.up * 0.4f, fov: 36f),
            (t: Film1bSeconds - 2f, pos: aimEnd - dirEnd * 0.95f, look: aimEnd, fov: 30f),
            (t: Film1bSeconds, pos: aimEnd - dirEnd * 0.95f, look: aimEnd, fov: 30f),
        };

        var clip = new AnimationClip { name = "Film_1b_RigPan", frameRate = Fps };
        var px = new AnimationCurve(); var py = new AnimationCurve(); var pz = new AnimationCurve();
        var rx = new AnimationCurve(); var ry = new AnimationCurve(); var rz = new AnimationCurve();
        var fov = new AnimationCurve();
        Vector3 prevEuler = Vector3.zero;
        for (int i = 0; i < keys.Length; i++)
        {
            var k = keys[i];
            var e = Quaternion.LookRotation(k.look - k.pos, Vector3.up).eulerAngles;
            if (i > 0) // keep angles continuous so the pan never spins the long way round
                e = new Vector3(prevEuler.x + Mathf.DeltaAngle(prevEuler.x, e.x), prevEuler.y + Mathf.DeltaAngle(prevEuler.y, e.y), 0f);
            prevEuler = e;
            px.AddKey(k.t, k.pos.x); py.AddKey(k.t, k.pos.y); pz.AddKey(k.t, k.pos.z);
            rx.AddKey(k.t, e.x); ry.AddKey(k.t, e.y); rz.AddKey(k.t, 0f);
            fov.AddKey(k.t, k.fov);
        }
        foreach (var c in new[] { px, py, pz, rx, ry, rz, fov })
            for (int i = 0; i < c.length; i++) AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);

        clip.SetCurve("Camera", typeof(Transform), "m_LocalPosition.x", px);
        clip.SetCurve("Camera", typeof(Transform), "m_LocalPosition.y", py);
        clip.SetCurve("Camera", typeof(Transform), "m_LocalPosition.z", pz);
        clip.SetCurve("Camera", typeof(Transform), "localEulerAnglesRaw.x", rx);
        clip.SetCurve("Camera", typeof(Transform), "localEulerAnglesRaw.y", ry);
        clip.SetCurve("Camera", typeof(Transform), "localEulerAnglesRaw.z", rz);
        clip.SetCurve("Camera", typeof(FilmCameraRig), "fieldOfView", fov);
        return clip;
    }

    static void Recorder(TimelineAsset t, string file, float seconds)
    {
        t.editorSettings.frameRate = Fps;
        var track = t.CreateTrack<RecorderTrack>(null, "Recorder");
        var clip = track.CreateClip<RecorderClip>();
        clip.start = 0;
        clip.duration = seconds;
        clip.displayName = file;

        var settings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        settings.name = file + " Recorder";
        AssetDatabase.AddObjectToAsset(settings, t);
        ((RecorderClip)clip.asset).settings = settings;
        ConfigureRecorder(settings, file);
    }

    /// <summary>
    /// Output and encoder for a film take. Applied on every Build so existing timelines pick up fixes.
    /// "High" quality alone is H.264 Constrained Baseline at ~8 Mbit/s — far too low for grass,
    /// foliage and scanlines — so the encoder is set explicitly.
    /// </summary>
    internal static void ConfigureRecorder(MovieRecorderSettings settings, string file,
        OutputPath.Root root = OutputPath.Root.AssetsFolder, string leaf = "Videos", bool audio = false)
    {
        settings.Enabled = true;
        settings.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Custom,
            EncodingProfile = CoreEncoderSettings.H264EncodingProfile.High,
            TargetBitRate = TargetMbps,
            GopSize = Fps,
            NumConsecutiveBFrames = 2,
        };
        settings.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
        settings.AudioInputSettings.PreserveAudio = audio;
        settings.FrameRate = Fps;
        settings.FrameRatePlayback = FrameRatePlayback.Constant;
        settings.CapFrameRate = true;

        // Let the Recorder resolve the folder itself. A relative path through OutputFile was stored
        // as "Absolute" and resolved against the working directory (it once targeted C:\).
        settings.FileNameGenerator.Root = root;
        settings.FileNameGenerator.Leaf = leaf;
        settings.FileNameGenerator.FileName = file;
        EditorUtility.SetDirty(settings);
    }

    static void UpdateRecorders(TimelineAsset t, string file)
    {
        foreach (var track in t.GetOutputTracks())
        {
            if (track is not RecorderTrack) continue;
            foreach (var c in track.GetClips())
                if (c.asset is RecorderClip rc && rc.settings is MovieRecorderSettings ms) ConfigureRecorder(ms, file);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static TimelineAsset LoadOrCreate(string path, System.Action<TimelineAsset> populate)
    {
        var t = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
        if (t != null) return t;
        t = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(t, path);
        populate(t);
        EditorUtility.SetDirty(t);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Timeline] Created {path}.");
        return t;
    }

    static void Setup(PlayableDirector d, TimelineAsset t, bool film)
    {
        d.playableAsset = t;
        d.playOnAwake = false;
        d.extrapolationMode = film ? DirectorWrapMode.None : DirectorWrapMode.Hold;
    }

    static void Bind(PlayableDirector director, CinemaDirector cinema, ProceduralTree tree, Light sun, Volume post,
        LoadingBarOverlay bar, Animator rig)
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
                LoadingBarTrack => bar,
                AnimationTrack => rig,
                _ => null, // markers, recorder: no binding
            };
            if (target != null) director.SetGenericBinding(track, target);
        }
    }

    internal static T Clip<T>(TrackAsset track, string label, double start, double duration) where T : PlayableAsset
    {
        var c = track.CreateClip<T>();
        c.start = start;
        c.duration = duration;
        c.displayName = label;
        return (T)c.asset;
    }

    internal static void Sun(SunTrack track, string label, double start, double duration, float elevation, float azimuth, float lux, float kelvin)
    {
        var s = Clip<SunClip>(track, label, start, duration);
        s.label = label;
        s.elevation = elevation;
        s.azimuth = azimuth;
        s.intensity = lux;
        s.colorTemperature = kelvin;
    }

    static void Marker(TimelineAsset timeline, double time, SequenceMarker.Kind kind)
        => timeline.markerTrack.CreateMarker<SequenceMarker>(time).kind = kind;

    // Not `GetComponent() ?? Add()`: in the editor a missing component is a fake-null object, so ?? never falls through.
    static T GetOrAdd<T>(GameObject go) where T : Component
        => go.TryGetComponent(out T c) ? c : Undo.AddComponent<T>(go);

    static GameObject Created(GameObject go)
    {
        Undo.RegisterCreatedObjectUndo(go, "Timeline setup");
        return go;
    }

    static GameObject Child(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t.gameObject;
        var go = Created(new GameObject(name));
        go.transform.SetParent(parent, false);
        return go;
    }
}
