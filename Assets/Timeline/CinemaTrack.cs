using System;
using System.ComponentModel;
using Cinema;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Camera track bound to the <see cref="CinemaDirector"/>. Clip types:
/// <see cref="CinemaShotClip"/> (one of the director's shots), <see cref="OrbitClip"/> (an orbit with
/// its own settings), <see cref="FreeCameraClip"/> (a keyframed <see cref="FilmCameraRig"/>) and
/// <see cref="DirectorAutoClip"/> (growth-based switching). Overlapping clips crossfade; gaps hold.
/// </summary>
[TrackColor(0.95f, 0.75f, 0.2f)]
[TrackBindingType(typeof(CinemaDirector))]
[TrackClipType(typeof(CinemaShotClip))]
[TrackClipType(typeof(OrbitClip))]
[TrackClipType(typeof(FreeCameraClip))]
[TrackClipType(typeof(DirectorAutoClip))]
[DisplayName("Cinema/Cinema Track")]
public sealed class CinemaTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        => ScriptPlayable<CinemaMixer>.Create(graph, inputCount);

    // Lets Timeline preview restore the camera after scrubbing instead of leaving the scene changed.
    public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
    {
        var d = director.GetGenericBinding(this) as CinemaDirector;
        if (d == null || d.targetCamera == null) return;
        var cam = d.targetCamera;
        TimelinePreview.AddTransform(driver, cam.gameObject);
        driver.AddFromName<Camera>(cam.gameObject, "field of view");
        driver.AddFromName<Camera>(cam.gameObject, "near clip plane");
    }
}

/// <summary>Per-clip lens: field of view eased from start to end over the clip.</summary>
[Serializable]
public struct LensOverride
{
    public bool enabled;
    [Range(5f, 120f)] public float fovStart;
    [Range(5f, 120f)] public float fovEnd;

    public static LensOverride Default => new LensOverride { fovStart = 35f, fovEnd = 35f };

    public void Apply(ref CinemaPose pose, float u)
    {
        if (enabled) pose.fieldOfView = Mathf.Lerp(fovStart, fovEnd, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u)));
    }
}

public enum CinemaClipKind { Shot, Auto, Orbit, Free }

public sealed class CinemaClipBehaviour : PlayableBehaviour
{
    public CinemaClipKind kind;
    public Shot shot;
    public OrbitClip orbit;
    public FilmCameraRig rig;
    public LensOverride lens;
}

public sealed class CinemaMixer : PlayableBehaviour
{
    CinemaDirector _director;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        _director = playerData as CinemaDirector;
        if (_director == null) return;

        // The two strongest camera clips (a crossfade has at most two) plus any auto weight.
        int a = -1, b = -1;
        float wa = 0f, wb = 0f, autoWeight = 0f;
        for (int i = 0; i < playable.GetInputCount(); i++)
        {
            float w = playable.GetInputWeight(i);
            if (w <= 0f) continue;
            if (Clip(playable, i).kind == CinemaClipKind.Auto) { autoWeight += w; continue; }
            if (w > wa) { b = a; wb = wa; a = i; wa = w; }
            else if (w > wb) { b = i; wb = w; }
        }

        if (a < 0 && autoWeight <= 0f) return; // gap: hold
        if (a < 0 || autoWeight >= Mathf.Max(wa, 0.5f))
        {
            _director.TimelineAuto();
        }
        else
        {
            float dt = (float)info.deltaTime;
            var pa = PoseOf(playable, a, dt, out var dominant);
            if (b >= 0)
            {
                var pb = PoseOf(playable, b, dt, out var shotB);
                // Blend from the outgoing clip (further into its local time) to the incoming one.
                bool aOutgoing = playable.GetInput(a).GetTime() >= playable.GetInput(b).GetTime();
                pa = aOutgoing ? CinemaPose.Lerp(pa, pb, wb / (wa + wb)) : CinemaPose.Lerp(pb, pa, wa / (wa + wb));
                if (wb > wa) dominant = shotB;
            }
            _director.TimelineDrive(pa, dominant);
        }

        if (!Application.isPlaying) _director.ApplyTimelineNow();
    }

    CinemaPose PoseOf(Playable mixer, int i, float dt, out Shot? shot)
    {
        var input = mixer.GetInput(i);
        var c = Clip(mixer, i);
        float t = (float)input.GetTime();
        double dur = input.GetDuration();
        shot = null;

        CinemaPose pose;
        switch (c.kind)
        {
            case CinemaClipKind.Shot:
                shot = c.shot;
                pose = _director.EvaluateShot(c.shot, t, dt, snap: !Application.isPlaying);
                break;
            case CinemaClipKind.Orbit:
                pose = c.orbit != null ? c.orbit.PoseAt(_director, t) : default;
                break;
            default:
                pose = c.rig != null ? c.rig.Pose : default;
                break;
        }
        c.lens.Apply(ref pose, dur > 0 ? (float)(t / dur) : 1f);
        return pose;
    }

    static CinemaClipBehaviour Clip(Playable mixer, int i)
        => ((ScriptPlayable<CinemaClipBehaviour>)mixer.GetInput(i)).GetBehaviour();

    public override void OnPlayableDestroy(Playable playable)
    {
        if (_director != null) _director.ReleaseTimeline();
    }
}

/// <summary>Shared helpers for the tracks' edit-mode preview.</summary>
static class TimelinePreview
{
    public static void AddTransform(IPropertyCollector driver, GameObject go)
    {
        foreach (var c in new[] { "x", "y", "z" }) driver.AddFromName<Transform>(go, "m_LocalPosition." + c);
        foreach (var c in new[] { "x", "y", "z", "w" }) driver.AddFromName<Transform>(go, "m_LocalRotation." + c);
    }
}
