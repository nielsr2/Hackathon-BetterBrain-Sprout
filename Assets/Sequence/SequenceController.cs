using System.Collections;
using Relaxation;
using Sequence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Video;

/// <summary>
/// Runs the session: intro video → fade → EEG tree sequence → glitch → outro video → quit.
/// Videos render into a RenderTexture shown through the Kino Overlay on <see cref="GlitchTransition"/>'s
/// FX volume, so they sit inside the post stack. An empty clip slot skips that video (with a warning).
/// With a tree-phase <see cref="timeline"/>, the timeline plays during the tree phase and its
/// <see cref="SequenceMarker"/>s start the baseline, hand growth to the EEG, and fire the timeout.
/// Keys: N skips the current phase (debug).
/// </summary>
[AddComponentMenu("Sequence/Sequence Controller")]
[RequireComponent(typeof(GlitchTransition))]
public sealed class SequenceController : MonoBehaviour, INotificationReceiver
{
    public enum Phase { Intro, Tree, OutroTransition, Outro, Done }

    [Header("References")]
    public RelaxationTreeDriver driver;
    public RelaxationHud hud;
    public GlitchTransition glitch;
    [Tooltip("Optional tree-phase Timeline (on this GameObject so it receives the markers). Empty = automatic tree phase.")]
    public PlayableDirector timeline;

    [Header("Videos (leave empty to skip)")]
    [Tooltip("Played in order before the tree phase (e.g. 1a orbit, 1b loading pan).")]
    public VideoClip[] introPlaylist = System.Array.Empty<VideoClip>();
    [Tooltip("Legacy single intro; used only when the playlist is empty.")]
    public VideoClip introClip;
    [Tooltip("Index in the intro playlist whose start begins EEG baseline collection (-1 = only via timeline marker).")]
    public int baselineWithIntroClip = 1;
    public VideoClip outroClip;
    [Range(0f, 1f)] public float videoVolume = 1f;

    [Header("Timing")]
    [Min(0f)] public float introFadeSeconds = 1f;
    [Tooltip("Max seconds of tree sequence before the outro starts anyway. 0 = no timeout.")]
    [Min(0f)] public float treeTimeoutSeconds = 300f;
    [Tooltip("Tree counts as fully grown within this distance of the driver's growthMax.")]
    [Min(0f)] public float fullGrowthEpsilon = 0.005f;
    [Tooltip("Seconds held on black before quitting when there is no outro clip.")]
    [Min(0f)] public float quitDelaySeconds = 1f;

    [Header("Debug")]
    public Key skipKey = Key.N;

    [Header("Live (read-only)")]
    public Phase phase;
    public float phaseElapsed;
    public bool baselineStarted;
    public bool interactive;
    public bool timeoutHit;

    VideoPlayer _introPlayer, _outroPlayer;
    bool _skip;

    bool UsesTimeline => timeline != null && timeline.playableAsset != null;

    void Awake()
    {
        if (glitch == null) glitch = GetComponent<GlitchTransition>();
        if (driver == null) driver = FindAnyObjectByType<RelaxationTreeDriver>();
        if (hud == null) hud = FindAnyObjectByType<RelaxationHud>();
        if (timeline == null) timeline = GetComponent<PlayableDirector>();
        if (timeline != null)
        {
            timeline.playOnAwake = false;
            timeline.extrapolationMode = DirectorWrapMode.Hold;
        }
        // Hold the tree (and its baseline) until the intro is over.
        if (driver != null)
        {
            driver.enabled = false;
            if (UsesTimeline) driver.interactive = false; // the timeline owns growth until the Interactive marker
            if (driver.demoAnimator != null) driver.demoAnimator.enabled = false;
        }
    }

    /// <summary>Timeline markers. Only act during the tree phase of a running session.</summary>
    public void OnNotify(Playable origin, INotification notification, object context)
    {
        if (!Application.isPlaying || phase != Phase.Tree || notification is not SequenceMarker m) return;        switch (m.kind)
        {
            case SequenceMarker.Kind.StartBaseline: StartBaseline(); break;
            case SequenceMarker.Kind.Interactive: BeginInteractive(); break;
            case SequenceMarker.Kind.Timeout:
                timeoutHit = true;
                Debug.Log("[Sequence] Timeout marker → glitch.");
                break;
        }
    }

    void StartBaseline()
    {
        if (baselineStarted || driver == null) return;
        baselineStarted = true;
        driver.enabled = true; // collects the baseline; growth stays with the timeline
        Debug.Log("[Sequence] Baseline collection started.");
    }

    void BeginInteractive()
    {
        if (interactive) return;
        StartBaseline(); // a timeline without a baseline marker still works, just later
        interactive = true;
        if (driver != null) driver.BeginInteractive();
        Debug.Log("[Sequence] Interactive: EEG now drives growth.");
    }

    void Start() => StartCoroutine(Run());

    void Update()
    {
        phaseElapsed += Time.deltaTime;
        var kb = Keyboard.current;
        if (kb != null && kb[skipKey].wasPressedThisFrame) _skip = true;
    }

    IEnumerator Run()
    {
        // ── Intro ──
        Enter(Phase.Intro);
        var intro = IntroClips();
        if (intro.Count > 0)
        {
            SetHud(false);
            VideoPlayer next = CreatePlayer(intro[0], "Intro 0");
            for (int i = 0; i < intro.Count; i++)
            {
                _introPlayer = next;
                yield return PrepareAndPlay(_introPlayer);
                glitch.Overlay.opacity.Override(1f);
                if (i == baselineWithIntroClip) StartBaseline(); // fake bar on screen, real baseline underneath

                // Prepare the following clip while this one plays, so the cut has no black gap.
                next = i + 1 < intro.Count ? CreatePlayer(intro[i + 1], $"Intro {i + 1}") : null;
                if (next != null) next.Prepare();
                yield return WaitForEnd(_introPlayer);

                if (next == null)
                {
                    for (float t = 0f; t < introFadeSeconds; t += Time.deltaTime)
                    {
                        glitch.Overlay.opacity.Override(1f - t / introFadeSeconds);
                        yield return null;
                    }
                    glitch.Overlay.opacity.Override(0f);
                }
                DisposePlayer(ref _introPlayer);
            }
        }
        else Debug.LogWarning("[Sequence] No intro clips assigned; starting with the tree.", this);

        // ── Tree ──
        Enter(Phase.Tree);
        SetHud(true);
        if (UsesTimeline)
        {
            timeline.time = 0;
            timeline.Play();
        }
        else if (driver != null) driver.enabled = true;
        if (outroClip != null)
        {
            _outroPlayer = CreatePlayer(outroClip, "Outro");
            _outroPlayer.Prepare(); // ready by the time the tree is done
        }
        while (!_skip && !TreeDone()) yield return null;

        // ── Glitch into the outro ──
        Enter(Phase.OutroTransition);
        if (driver != null) driver.enabled = false; // freeze the tree as the glitch hits
        if (UsesTimeline) timeline.Pause();         // ...and the camera, sun and atmosphere
        if (_outroPlayer != null && !_outroPlayer.isPrepared)
            yield return PrepareAndPlay(_outroPlayer, playWhenReady: false);

        bool glitchDone = false;
        glitch.Play(onCut: () =>
            {
                SetHud(false);
                if (_outroPlayer != null)
                {
                    glitch.Overlay.texture.Override(_outroPlayer.targetTexture);
                    _outroPlayer.Play();
                }
                else
                {
                    // No clip: the cut lands on black.
                    glitch.Overlay.texture.Override(Texture2D.blackTexture);
                }
            },
            onComplete: () => glitchDone = true);
        while (!glitchDone) yield return null;

        // ── Outro ──
        Enter(Phase.Outro);
        if (_outroPlayer != null) yield return WaitForEnd(_outroPlayer);
        else
        {
            Debug.LogWarning("[Sequence] No outro clip assigned; holding black, then quitting.", this);
            yield return new WaitForSeconds(quitDelaySeconds);
        }

        Enter(Phase.Done);
        Quit();
    }

    System.Collections.Generic.List<VideoClip> IntroClips()
    {
        var list = new System.Collections.Generic.List<VideoClip>();
        if (introPlaylist != null) foreach (var c in introPlaylist) if (c != null) list.Add(c);
        if (list.Count == 0 && introClip != null) list.Add(introClip);
        return list;
    }

    bool TreeDone() => UsesTimeline
        ? SequenceRules.TreePhaseOver(interactive, driver != null ? driver.displayedGrowth : 0f,
            driver != null ? driver.growthMax : 1f, fullGrowthEpsilon, timeoutHit)
        : driver == null
        ? SequenceRules.TreeFinished(0f, 1f, 0f, phaseElapsed, treeTimeoutSeconds)
        : SequenceRules.TreeFinished(driver.displayedGrowth, driver.growthMax, fullGrowthEpsilon, phaseElapsed, treeTimeoutSeconds);

    void Enter(Phase p)
    {
        phase = p;
        phaseElapsed = 0f;
        _skip = false;
        Debug.Log($"[Sequence] → {p}");
    }

    void SetHud(bool on)
    {
        if (hud != null) hud.enabled = on;
    }

    VideoPlayer CreatePlayer(VideoClip clip, string label)
    {
        var rt = new RenderTexture((int)clip.width, (int)clip.height, 0, RenderTextureFormat.ARGB32)
        {
            name = $"{label} Video RT",
            hideFlags = HideFlags.DontSave,
        };
        rt.Create();

        var vp = gameObject.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = false;
        vp.source = VideoSource.VideoClip;
        vp.clip = clip;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = rt;
        vp.audioOutputMode = VideoAudioOutputMode.Direct;
        vp.SetDirectAudioVolume(0, videoVolume);
        vp.skipOnDrop = true;
        return vp;
    }

    IEnumerator PrepareAndPlay(VideoPlayer vp, bool playWhenReady = true)
    {
        vp.Prepare();
        while (!vp.isPrepared) yield return null;
        glitch.Overlay.texture.Override(vp.targetTexture);
        if (playWhenReady) vp.Play();
    }

    IEnumerator WaitForEnd(VideoPlayer vp)
    {
        bool ended = false;
        void OnEnd(VideoPlayer _) => ended = true;
        vp.loopPointReached += OnEnd;
        while (!ended && !_skip) yield return null;
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
        DisposePlayer(ref _introPlayer);
        DisposePlayer(ref _outroPlayer);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
