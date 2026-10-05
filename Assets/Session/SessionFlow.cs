using System.Collections;
using Relaxation;
using Sequence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Runs the full session in <c>oak_session.unity</c>:
///   Intro — the opening film (<see cref="introClip"/>), full screen over black, with sound;
///   Logo — morning orbit round the grown tree, the sprout logo (alpha video, no audio) on top;
///   Connect — fade to the sync screen, the user connects their Unicorn in the g.tec bar;
///   Calibrate — "INITIAL CALIBRATION": the g.tec ERP training run;
///   Baseline — back to the orbit with the loading bar filling as the resting baseline is collected;
///   Sprout — quick fade to a fast orbit over the bare ground while a sprout pops up;
///   Interactive — the oak_bci loop with all overlays, until the tree reaches <see cref="growthGoal"/> or the timeout;
///   Glitch video, then a second glitch into the spooky video;
///   Choice — two ERP buttons (labels and videos from a CSV in StreamingAssets) over the end of the spooky
///   video; the selection (EEG or mouse click) glitches into that button's video.
/// The sync screen is a separate camera that sees only the g.tec rig's layer. Fades, the logo and
/// the sync titles are a runtime screen-space canvas; the end videos go through the Kino Overlay
/// on <see cref="GlitchTransition"/> so the glitch distorts them too. Key N skips the current step (at the choice: picks the left button).
/// Debug mode (F9, or the Inspector): simulated EEG, no sync screen without a headset, growth and
/// baseline <see cref="debugSpeed"/>× faster ([ and ] halve/double it), ← / → pick a choice button.
/// </summary>
[AddComponentMenu("Session/Session Flow")]
[RequireComponent(typeof(GlitchTransition))]
public sealed class SessionFlow : MonoBehaviour
{
    public enum Phase { Intro, Logo, Connect, Calibrate, Baseline, Sprout, Interactive, GlitchVideo, Spooky, Choice, Chosen, Done }

    [Header("References")]
    public RelaxationTreeDriver driver;
    public RelaxationHud hud;
    public EegSignalOverlay signalOverlay;
    public GlitchTransition glitch;
    public LoadingBarOverlay loadingBar;
    public GtecSync sync;
    public Camera treeCamera;
    public Camera syncCamera;
    [Tooltip("Looping orbit round the grown tree (logo + baseline).")]
    public PlayableDirector grownTimeline;
    [Tooltip("Fast orbit over the bare ground, sprout pop, then the interactive cameras and light.")]
    public PlayableDirector sproutTimeline;

    [Header("Videos")]
    [Tooltip("Opening film, played full screen with sound before everything else. None: start with the logo.")]
    public VideoClip introClip;
    [Tooltip("Transparent logo over the opening orbit; played without audio.")]
    public VideoClip logoClip;
    [Tooltip("The first glitch lands on this.")]
    public VideoClip glitchVideo;
    [Tooltip("The second glitch lands on this.")]
    public VideoClip spookyVideo;

    [Header("Final choice")]
    [Tooltip("ERP buttons over the end of the spooky video; each plays its own video.")]
    public ChoiceScreen choice;
    [Tooltip("Under StreamingAssets. Columns 'button' and 'video' (relative to the CSV's folder); the first two rows are used.")]
    public string choiceCsv = "Choices/choices.csv";
    [Tooltip("The buttons appear this many seconds before the spooky video ends.")]
    [Min(0f)] public float choiceLeadSeconds = 2f;
    [Range(0f, 1f)] public float videoVolume = 1f;

    [Header("Sync screen text")]
    public string connectTitle = "CONNECT YOUR UNICORN";
    public string connectHint = "Choose your headset in the bar at the top and press Connect";
    public string calibrateTitle = "INITIAL CALIBRATION";
    public string calibrateHint = "Look at the flashing symbol and silently count its flashes";

    [Header("Timing")]
    [Tooltip("Seconds of orbit before the logo starts.")]
    [Min(0f)] public float logoLeadIn = 1.5f;
    [Min(0f)] public float fadeSeconds = 1f;
    [Tooltip("The fade from the baseline orbit to the bare ground.")]
    [Min(0f)] public float quickFadeSeconds = 0.35f;
    [Tooltip("Start the ERP training by itself once the pipeline is ready (otherwise the g.tec button does it).")]
    public bool autoStartCalibration = true;
    [Min(0f)] public float calibrationCountdown = 3f;
    [Tooltip("Seconds the sync screen holds a 'done' message before moving on.")]
    [Min(0f)] public float confirmSeconds = 1.5f;
    [Tooltip("Seconds the full loading bar stays up after the baseline locks.")]
    [Min(0f)] public float baselineHoldSeconds = 1.5f;
    [Tooltip("Sprout-timeline time at which the EEG takes over growth (the sprout has popped).")]
    [Min(0f)] public float interactiveAt = 7f;

    [Header("End of the interactive part")]
    [Tooltip("Growth (0..1) that counts as 'grown enough'.")]
    [Range(0f, 1f)] public float growthGoal = 0.8f;
    [Tooltip("Seconds of interactive growth before the glitch comes anyway. 0 = no timeout.")]
    [Min(0f)] public float interactiveTimeout = 240f;
    [Tooltip("Seconds of the glitch video before the second glitch. 0 = play it to the end.")]
    [Min(0f)] public float glitchVideoSeconds;
    [Tooltip("Leave Play mode at the end (set by Tools/Session/Record Full Session so the recording is saved).")]
    public bool quitAtEnd;
    [Tooltip("With quitAtEnd: seconds held after the chosen video ends (without a choice: after the glitch into the spooky video, 0 = play it to the end).")]
    [Min(0f)] public float recordTailSeconds = 3f;

    [Header("Debug")]
    public Key skipKey = Key.N;
    [Tooltip("Simulated EEG instead of the headset (the sync steps are skipped while no Unicorn is connected), " +
             "and growth and baseline sped up by debugSpeed.")]
    public bool debugMode;
    public Key debugToggleKey = Key.F9;
    [Tooltip("Simulated EEG rests during the baseline and relaxes in the interactive part, so the tree grows hands-free. " +
             "Off: hold Space to relax.")]
    public bool debugAutoRelax = true;
    [Tooltip("Debug: growth and baseline run this many times faster.")]
    [Range(1f, 50f)] public float debugSpeed = 5f;
    public Key speedUpKey = Key.RightBracket;
    public Key speedDownKey = Key.LeftBracket;

    [Header("Live (read-only)")]
    public Phase phase;
    public float phaseElapsed;

    static readonly string[] BaselineLines =
    {
        "INITIALIZING ELECTRODE ARRAY",
        "MEASURING ALPHA RHYTHM",
        "MEASURING THETA RHYTHM",
        "ESTABLISHING RESTING BASELINE",
        "BASELINE LOCKED",
    };

    VideoPlayer _intro, _logo, _videoA, _spooky;
    readonly VideoPlayer[] _choiceVideos = new VideoPlayer[2];
    System.Collections.Generic.List<ChoiceOption> _choices = new System.Collections.Generic.List<ChoiceOption>();
    readonly string[] _choicePaths = new string[2];
    bool _skip;
    GameObject _ui;
    Image _fade;
    RawImage _logoImage, _introImage;
    AspectRatioFitter _logoFit, _introFit;
    Image _introBackdrop;
    Text _title, _hint, _debugLabel;
    float _baseSecondsToFull, _baseBaselineSeconds;
    bool _receiverSimulated;

    bool SyncSkipped => debugMode && (sync == null || !sync.Connected);

    void Awake()
    {
        if (glitch == null) glitch = GetComponent<GlitchTransition>();
        if (driver == null) driver = FindAnyObjectByType<RelaxationTreeDriver>();
        if (hud == null) hud = FindAnyObjectByType<RelaxationHud>();
        if (signalOverlay == null) signalOverlay = FindAnyObjectByType<EegSignalOverlay>();
        foreach (var d in new[] { grownTimeline, sproutTimeline })
            if (d != null) d.playOnAwake = false;

        // Nothing EEG-driven until the baseline phase.
        if (driver != null)
        {
            driver.enabled = false;
            driver.interactive = false;
            if (driver.demoAnimator != null) driver.demoAnimator.enabled = false;
            _baseSecondsToFull = driver.secondsToFullGrowth;
            _baseBaselineSeconds = driver.baselineSeconds;
            if (driver.receiver != null) _receiverSimulated = driver.receiver.simulate;
        }
        SetOverlays(false);
        BuildUi();
        _fade.color = Color.black;
    }

    void Start()
    {
        if (sync != null) sync.ShowUi(false);
        UseSyncCamera(false);
        LoadChoices();
        StartCoroutine(Run());
    }

    // A bad or missing CSV is reported and the session simply ends on the spooky video.
    void LoadChoices()
    {
        _choices.Clear();
        if (choice == null) return;
        string csvPath = System.IO.Path.Combine(Application.streamingAssetsPath, choiceCsv);
        try
        {
            if (!System.IO.File.Exists(csvPath)) throw new System.IO.FileNotFoundException($"No choice CSV at {csvPath}.");
            var options = ChoiceCsv.Parse(System.IO.File.ReadAllText(csvPath), 2, out var warnings);
            foreach (var w in warnings) Debug.LogWarning($"[Session] {w}", this);
            string folder = System.IO.Path.GetDirectoryName(csvPath);
            for (int i = 0; i < 2; i++)
            {
                string video = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, options[i].Video));
                if (!System.IO.File.Exists(video)) throw new System.IO.FileNotFoundException($"'{options[i].Label}': no video at {video}.");
                _choicePaths[i] = video;
            }
            _choices = options;
            Debug.Log($"[Session] Choice: '{options[0].Label}' / '{options[1].Label}' ({csvPath}).");
        }
        catch (System.Exception e) when (e is System.FormatException || e is System.IO.IOException)
        {
            _choices.Clear();
            Debug.LogError($"[Session] Choice disabled — {e.Message}", this);
        }
    }

    void Update()
    {
        phaseElapsed += Time.deltaTime;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[skipKey].wasPressedThisFrame) _skip = true;
            if (kb[debugToggleKey].wasPressedThisFrame) debugMode = !debugMode;
            if (debugMode && kb[speedUpKey].wasPressedThisFrame) debugSpeed = Mathf.Min(50f, debugSpeed * 2f);
            if (debugMode && kb[speedDownKey].wasPressedThisFrame) debugSpeed = Mathf.Max(1f, debugSpeed * 0.5f);
            if (debugMode && phase == Phase.Choice && choice != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame) choice.Select(0);
                if (kb.rightArrowKey.wasPressedThisFrame) choice.Select(1);
            }
        }
        ApplyDebug();
    }

    // Every frame, so Inspector edits and the keys take effect at once; switching off restores the scene's values.
    void ApplyDebug()
    {
        float speed = debugMode ? debugSpeed : 1f;
        if (driver != null)
        {
            driver.secondsToFullGrowth = _baseSecondsToFull / speed;
            driver.baselineSeconds = _baseBaselineSeconds / speed;
            var r = driver.receiver;
            if (r != null)
            {
                r.simulate = debugMode || _receiverSimulated;
                if (debugMode)
                {
                    r.simRelaxManual = debugAutoRelax;
                    r.simRelaxTarget = phase >= Phase.Interactive ? 1f : 0f; // rest for the baseline, then relax
                }
            }
        }
        if (_debugLabel != null)
        {
            _debugLabel.enabled = debugMode;
            if (debugMode)
                _debugLabel.text = $"DEBUG  simulated EEG ({(debugAutoRelax ? "auto relax" : "hold Space")})  ×{debugSpeed:0.#}   " +
                                   $"[{KeyName(speedDownKey)}] [{KeyName(speedUpKey)}] speed   {debugToggleKey} off   {skipKey} skip\n" +
                                   $"{phase}  growth {(driver != null ? driver.displayedGrowth : 0f):0.00} / {growthGoal:0.00}";
        }
    }

    static string KeyName(Key k) => k switch { Key.LeftBracket => "[", Key.RightBracket => "]", _ => k.ToString() };

    IEnumerator Run()
    {
        // ── Opening film ── (screen is black here)
        if (introClip != null)
        {
            Enter(Phase.Intro);
            _intro = CreatePlayer(introClip, "Intro", audio: true);
            _intro.Prepare();
            while (!_intro.isPrepared && !_skip) yield return null;
            _introImage.texture = _intro.targetTexture;
            _introFit.aspectRatio = (float)introClip.width / introClip.height;
            _introBackdrop.enabled = _introImage.enabled = true;
            _intro.Play();
            yield return Fade(1f, 0f, fadeSeconds);
            yield return WaitForEnd(_intro);
            yield return Fade(0f, 1f, fadeSeconds);
            _introBackdrop.enabled = _introImage.enabled = false;
            DisposePlayer(ref _intro);
        }

        // ── Logo over the grown tree ──
        Enter(Phase.Logo);
        Play(grownTimeline);
        if (logoClip != null)
        {
            _logo = CreatePlayer(logoClip, "Logo", audio: false);
            _logo.Prepare();
        }
        yield return Fade(1f, 0f, fadeSeconds);
        yield return Wait(logoLeadIn);
        if (_logo != null)
        {
            while (!_logo.isPrepared && !_skip) yield return null;
            _logoImage.texture = _logo.targetTexture;
            _logoFit.aspectRatio = (float)logoClip.width / logoClip.height;
            _logoImage.enabled = true;
            _logo.Play();
            yield return WaitForEnd(_logo);
        }
        else Debug.LogWarning("[Session] No logo clip assigned.", this);

        yield return Fade(0f, 1f, fadeSeconds);
        _logoImage.enabled = false;
        DisposePlayer(ref _logo);

        // Debug without a headset: no sync screen at all, straight to the (simulated) baseline.
        if (SyncSkipped) Debug.Log("[Session] Debug mode, no Unicorn connected: skipping the sync screen.");
        else
        {
            // ── Sync screen: connect ──
            UseSyncCamera(true);
            if (sync != null) sync.ShowUi(true);
            Enter(Phase.Connect);
            SetText(connectTitle, connectHint);
            yield return Fade(1f, 0f, fadeSeconds);
            if (sync == null) Debug.LogWarning("[Session] No GtecSync; press N to continue.", this);
            while (!_skip && !SyncSkipped && (sync == null || !sync.Connected)) yield return null;
            if (!_skip && !SyncSkipped) { SetText(connectTitle, "Connected"); yield return Wait(confirmSeconds); }

            // ── Sync screen: ERP calibration ──
            Enter(Phase.Calibrate);
            SetText(calibrateTitle, calibrateHint);
            if (autoStartCalibration && sync != null && !SyncSkipped)
            {
                while (!_skip && !SyncSkipped && !sync.PipelineReady && !sync.Calibrated) yield return null;
                for (float t = calibrationCountdown; t > 0f && !_skip && !SyncSkipped && !sync.ParadigmRunning; t -= Time.deltaTime)
                {
                    SetText(calibrateTitle, $"{calibrateHint}\nStarting in {Mathf.CeilToInt(t)}");
                    yield return null;
                }
                SetText(calibrateTitle, calibrateHint);
                if (!_skip && !SyncSkipped) sync.StartCalibration();
            }
            while (!_skip && !SyncSkipped && (sync == null || !sync.Calibrated)) yield return null;
            if (!_skip && !SyncSkipped) { SetText(calibrateTitle, $"Calibration: {sync.CalibrationQuality}"); yield return Wait(confirmSeconds); }
            yield return Fade(0f, 1f, fadeSeconds);
        }

        // ── Baseline under the loading bar, orbiting the grown tree ── (screen is black here)
        if (sync != null) sync.ShowUi(false);
        SetText("", "");
        UseSyncCamera(false);
        Enter(Phase.Baseline);
        if (driver != null)
        {
            driver.interactive = false; // the timeline keeps the tree grown
            driver.enabled = true;
            driver.Recalibrate();       // rest only: the ERP run is not a resting baseline
        }
        if (loadingBar != null)
        {
            loadingBar.title = "CALIBRATING NEURAL BASELINE";
            loadingBar.progress = 0f;
            loadingBar.visibility = 1f;
        }
        yield return Fade(1f, 0f, fadeSeconds);
        while (!_skip && driver != null && !driver.Baseline.IsReady)
        {
            UpdateBar(driver.Baseline.Progress, driver.receiver != null && driver.receiver.HasSignal);
            yield return null;
        }
        UpdateBar(1f, true);
        yield return Wait(baselineHoldSeconds);
        for (float t = 0f; loadingBar != null && t < 0.5f; t += Time.deltaTime)
        {
            loadingBar.visibility = 1f - t / 0.5f;
            yield return null;
        }
        if (loadingBar != null) loadingBar.visibility = 0f;

        // ── Quick cut to the bare ground: the sprout pops ──
        yield return Fade(0f, 1f, quickFadeSeconds);
        Enter(Phase.Sprout);
        if (grownTimeline != null) grownTimeline.Stop();
        Play(sproutTimeline);
        if (sproutTimeline != null) sproutTimeline.Evaluate(); // bare ground before the fade lifts
        yield return Fade(1f, 0f, quickFadeSeconds);
        while (!_skip && sproutTimeline != null && sproutTimeline.time < interactiveAt) yield return null;

        // ── Interactive: the EEG grows the tree ──
        Enter(Phase.Interactive);
        if (driver != null) driver.BeginInteractive();
        SetOverlays(true);
        if (glitchVideo != null) { _videoA = CreatePlayer(glitchVideo, "Glitch video", audio: true); _videoA.Prepare(); }
        if (spookyVideo != null) { _spooky = CreatePlayer(spookyVideo, "Spooky video", audio: true); _spooky.Prepare(); }
        for (int i = 0; i < _choices.Count; i++)
        {
            _choiceVideos[i] = CreatePlayer(null, $"Choice {i}", audio: true, url: _choicePaths[i]);
            _choiceVideos[i].Prepare();
        }
        while (!_skip && !SequenceRules.TreeFinished(driver != null ? driver.displayedGrowth : 0f, growthGoal, 0f,
                   phaseElapsed, interactiveTimeout))
            yield return null;
        Debug.Log($"[Session] Interactive over: growth {(driver != null ? driver.displayedGrowth : 0f):0.00}, {phaseElapsed:0} s.");

        // ── Glitch into the first video ──
        Enter(Phase.GlitchVideo);
        if (driver != null) driver.enabled = false;
        if (sproutTimeline != null) sproutTimeline.Pause();
        yield return GlitchTo(_videoA, () => SetOverlays(false));
        if (_videoA != null) yield return WaitForEnd(_videoA, glitchVideoSeconds);

        // ── Glitch into the spooky video ──
        Enter(Phase.Spooky);
        yield return GlitchTo(_spooky, () => { if (_videoA != null) _videoA.Pause(); });
        if (choice == null || _choiceVideos[0] == null)
        {
            // No choice configured: the spooky video is the end.
            if (_spooky != null) yield return WaitForEnd(_spooky, quitAtEnd ? recordTailSeconds : 0f);
            else if (quitAtEnd) yield return Wait(recordTailSeconds);
        }
        else
        {
            // ── ERP choice over the end of the spooky video, which then holds its last frame ──
            while (!_skip && _spooky != null && _spooky.isPlaying && _spooky.length - _spooky.time > choiceLeadSeconds)
                yield return null;
            Enter(Phase.Choice);
            choice.Show(_choices[0].Label, _choices[1].Label, _spooky != null ? _spooky.targetTexture : null);
            UseSyncCamera(true);
            if (sync != null && sync.Calibrated) sync.StartChoice();
            else Debug.Log("[Session] No calibrated classifier: choose with a mouse click (or ←/→ in debug mode).");
            choice.BeginChoosing();
            while (choice.Selected < 0)
            {
                if (_spooky != null && _spooky.isPlaying && _spooky.time >= _spooky.length - 0.1) _spooky.Pause();
                if (_skip) choice.Select(0);
                yield return null;
            }
            int pick = choice.Selected;
            Debug.Log($"[Session] Chose '{_choices[pick].Label}' → {_choices[pick].Video}.");
            if (sync != null) sync.StopParadigm();

            // ── Glitch into the chosen video ──
            Enter(Phase.Chosen);
            UseSyncCamera(false); // the Kino overlay underneath still shows the same frame
            choice.Hide();
            var chosen = _choiceVideos[pick];
            yield return GlitchTo(chosen, () => { if (_spooky != null) _spooky.Pause(); });
            yield return WaitForEnd(chosen);
            if (quitAtEnd) yield return Wait(recordTailSeconds);
        }

        Enter(Phase.Done);
        if (quitAtEnd) Quit();
    }

    // ── Steps ───────────────────────────────────────────────────────────────

    IEnumerator GlitchTo(VideoPlayer vp, System.Action atCut)
    {
        if (vp != null) while (!vp.isPrepared) yield return null;
        FitTarget(vp);
        bool done = false;
        glitch.Play(onCut: () =>
            {
                atCut?.Invoke();
                glitch.Overlay.texture.Override(vp != null ? vp.targetTexture : Texture2D.blackTexture);
                if (vp != null) vp.Play();
            },
            onComplete: () => done = true);
        while (!done) yield return null;
    }

    void UpdateBar(float p, bool signal)
    {
        if (loadingBar == null) return;
        loadingBar.progress = p;
        int n = BaselineLines.Length - 1;
        loadingBar.status = p >= 1f ? BaselineLines[n]
            : !signal ? "WAITING FOR SIGNAL"
            : BaselineLines[Mathf.Clamp(Mathf.FloorToInt(p * n), 0, n - 1)];
    }

    void Play(PlayableDirector d)
    {
        if (d == null) return;
        d.time = 0;
        d.Play();
    }

    void UseSyncCamera(bool on)
    {
        if (syncCamera != null) syncCamera.enabled = on;
        if (treeCamera != null) treeCamera.enabled = !on;
    }

    void SetOverlays(bool on)
    {
        if (hud != null) hud.enabled = on;
        if (signalOverlay != null) signalOverlay.enabled = on;
    }

    void Enter(Phase p)
    {
        phase = p;
        phaseElapsed = 0f;
        _skip = false;
        Debug.Log($"[Session] → {p}");
    }

    IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds && !_skip; t += Time.deltaTime) yield return null;
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            _fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
            yield return null;
        }
        _fade.color = new Color(0f, 0f, 0f, to);
    }

    // ── Video ───────────────────────────────────────────────────────────────

    /// <summary>A player for an imported <paramref name="clip"/>, or (clip null) a file at <paramref name="url"/>,
    /// whose size is only known once prepared — <see cref="FitTarget"/> resizes its texture then.</summary>
    VideoPlayer CreatePlayer(VideoClip clip, string label, bool audio, string url = null)
    {
        var vp = gameObject.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = false;
        if (clip != null)
        {
            vp.source = VideoSource.VideoClip;
            vp.clip = clip;
        }
        else
        {
            vp.source = VideoSource.Url;
            vp.url = url;
        }
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = NewTarget(clip != null ? (int)clip.width : 1920, clip != null ? (int)clip.height : 1080, label);
        vp.skipOnDrop = true;
        // Game time, not the wall clock: the Recorder steps time at a fixed rate and the video must keep pace.
        vp.timeUpdateMode = VideoTimeUpdateMode.GameTime;
        // Through an AudioSource (not Direct) so the sound reaches Unity's mixer and the Recorder.
        vp.audioOutputMode = audio ? VideoAudioOutputMode.AudioSource : VideoAudioOutputMode.None;
        if (audio)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.volume = videoVolume;
            vp.EnableAudioTrack(0, true);
            vp.SetTargetAudioSource(0, src);
        }
        return vp;
    }

    static RenderTexture NewTarget(int width, int height, string label)
    {
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            name = $"{label} RT",
            hideFlags = HideFlags.DontSave,
        };
        rt.Create();
        return rt;
    }

    // URL videos start on a 1080p placeholder texture; match it to the real size once prepared.
    static void FitTarget(VideoPlayer vp)
    {
        if (vp == null || vp.width == 0 || vp.height == 0) return;
        var old = vp.targetTexture;
        if (old != null && old.width == vp.width && old.height == vp.height) return;
        vp.targetTexture = NewTarget((int)vp.width, (int)vp.height, old != null ? old.name.Replace(" RT", "") : "Video");
        if (old != null) { old.Release(); Destroy(old); }
    }

    /// <summary>Until the clip ends (or after <paramref name="maxSeconds"/> &gt; 0), or N.</summary>
    IEnumerator WaitForEnd(VideoPlayer vp, float maxSeconds = 0f)
    {
        bool ended = false;
        void OnEnd(VideoPlayer _) => ended = true;
        vp.loopPointReached += OnEnd;
        for (float t = 0f; !ended && !_skip && (maxSeconds <= 0f || t < maxSeconds); t += Time.deltaTime) yield return null;
        vp.loopPointReached -= OnEnd;
        _skip = false;
    }

    void DisposePlayer(ref VideoPlayer vp)
    {
        if (vp == null) return;
        var rt = vp.targetTexture;
        vp.Stop();
        Destroy(vp);
        if (rt != null) { rt.Release(); Destroy(rt); }
        vp = null;
    }

    void OnDestroy()
    {
        DisposePlayer(ref _intro);
        DisposePlayer(ref _logo);
        DisposePlayer(ref _videoA);
        DisposePlayer(ref _spooky);
        for (int i = 0; i < _choiceVideos.Length; i++) DisposePlayer(ref _choiceVideos[i]);
        if (_ui != null) Destroy(_ui);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── Screen-space UI (runtime only) ──────────────────────────────────────

    void SetText(string title, string hint)
    {
        _title.text = title;
        _hint.text = hint;
    }

    void BuildUi()
    {
        _ui = new GameObject("Session UI (runtime)") { hideFlags = HideFlags.DontSave };
        _ui.transform.SetParent(transform, false);
        var canvas = _ui.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // above the g.tec bar and the loading bar
        var scaler = _ui.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        var group = _ui.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false; // never in the way of the g.tec buttons

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Opening film: letterboxed on black so the scene behind never shows.
        var intro = Stretch("Intro", _ui.transform);
        _introBackdrop = intro.gameObject.AddComponent<Image>();
        _introBackdrop.color = Color.black;
        _introBackdrop.raycastTarget = false;
        _introBackdrop.enabled = false;
        var introVideo = Stretch("Intro Video", intro);
        _introImage = introVideo.gameObject.AddComponent<RawImage>();
        _introImage.raycastTarget = false;
        _introImage.enabled = false;
        _introFit = introVideo.gameObject.AddComponent<AspectRatioFitter>();
        _introFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        var logo = Stretch("Logo", _ui.transform);
        _logoImage = logo.gameObject.AddComponent<RawImage>();
        _logoImage.enabled = false;
        _logoFit = logo.gameObject.AddComponent<AspectRatioFitter>();
        _logoFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        _title = Label("Title", font, 54, FontStyle.Bold, new Vector2(0.5f, 0.17f));
        _hint = Label("Hint", font, 28, FontStyle.Normal, new Vector2(0.5f, 0.1f));

        _debugLabel = Label("Debug", font, 20, FontStyle.Bold, new Vector2(0f, 0f));
        var dr = _debugLabel.rectTransform;
        dr.pivot = Vector2.zero;
        dr.anchoredPosition = new Vector2(16f, 12f);
        dr.sizeDelta = new Vector2(1400f, 60f);
        _debugLabel.alignment = TextAnchor.LowerLeft;
        _debugLabel.color = new Color(1f, 0.85f, 0.2f);
        _debugLabel.enabled = false;

        _fade = Stretch("Fade", _ui.transform).gameObject.AddComponent<Image>();
        _fade.raycastTarget = false;
        _debugLabel.transform.SetAsLastSibling(); // readable through fades
    }

    Text Label(string name, Font font, int size, FontStyle style, Vector2 anchor)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_ui.transform, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.sizeDelta = new Vector2(1600f, 140f);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var o = go.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.8f);
        o.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    static RectTransform Stretch(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        return rt;
    }
}
