using Nib.ProcTree;
using Relaxation;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Switches the scene between the live <b>Session</b> (intro videos → EEG tree → outro) and <b>Film</b>
/// mode, which plays one film take (a timeline with a Recorder clip) with the session, EEG and HUD
/// switched off, then leaves Play mode so the recording is finalised.
/// Runs before everything else so the session never starts in Film mode.
/// </summary>
[DefaultExecutionOrder(-1000)]
[AddComponentMenu("Sequence/Film Controller")]
public sealed class FilmController : MonoBehaviour
{
    public enum Mode { Session, Film }

    public Mode mode = Mode.Session;
    [Tooltip("The take played in Film mode.")]
    public PlayableDirector take;
    [Tooltip("All film takes in the scene (never auto-play in Session mode).")]
    public PlayableDirector[] takes = System.Array.Empty<PlayableDirector>();
    [Tooltip("Leave Play mode when the take ends (finalises the recording).")]
    public bool exitPlayModeWhenDone = true;

    [Header("Switched off in Film mode")]
    public SequenceController sequence;
    public RelaxationHud hud;
    public ProceduralTree tree;

    void Awake()
    {
        foreach (var t in takes) if (t != null) t.playOnAwake = false;
        if (mode != Mode.Film) return;

        if (sequence != null) sequence.gameObject.SetActive(false);
        if (hud != null) hud.enabled = false;
        if (tree != null)
        {
            if (tree.TryGetComponent(out RelaxationTreeDriver d)) d.enabled = false;
            if (tree.TryGetComponent(out GrowthAnimator g)) g.enabled = false;
        }
    }

    void Start()
    {
        if (mode != Mode.Film) return;
        if (take == null) { Debug.LogError("[Film] Film mode but no take assigned.", this); return; }
        Debug.Log($"[Film] Playing take '{take.name}' ({take.duration:0.0} s).");
        take.extrapolationMode = DirectorWrapMode.None; // so 'stopped' fires at the end
        take.stopped += OnTakeStopped;
        take.time = 0;
        take.Play();
    }

    void OnTakeStopped(PlayableDirector d)
    {
        d.stopped -= OnTakeStopped;
        Debug.Log($"[Film] Take '{d.name}' finished.");
#if UNITY_EDITOR
        if (exitPlayModeWhenDone) UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
