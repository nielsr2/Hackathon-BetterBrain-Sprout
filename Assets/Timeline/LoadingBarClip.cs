using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>A loading-bar run: title, fill curve over the clip (stalls look authentic), status messages.</summary>
[Serializable, DisplayName("Loading Bar")]
public sealed class LoadingBarClip : PlayableAsset, ITimelineClipAsset
{
    public string title = "CALIBRATING NEURAL BASELINE";
    [Tooltip("Bar fill (0..1) over the clip's normalised time.")]
    public AnimationCurve progress = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.22f, 0.27f), new Keyframe(0.33f, 0.29f),
        new Keyframe(0.58f, 0.66f), new Keyframe(0.68f, 0.67f), new Keyframe(0.93f, 1f), new Keyframe(1f, 1f));
    [Tooltip("Shown in order as the bar fills; the last one appears at 100%.")]
    public string[] statusLines =
    {
        "INITIALIZING ELECTRODE ARRAY",
        "MEASURING ALPHA RHYTHM",
        "MEASURING THETA RHYTHM",
        "ESTABLISHING RESTING BASELINE",
        "BASELINE LOCKED",
    };

    public ClipCaps clipCaps => ClipCaps.Blending;

    public string StatusAt(float p)
    {
        if (statusLines == null || statusLines.Length == 0) return "";
        if (p >= 1f) return statusLines[^1];
        int n = Mathf.Max(1, statusLines.Length - 1);
        return statusLines[Mathf.Clamp(Mathf.FloorToInt(p * n), 0, n - 1)];
    }

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<LoadingBarClipBehaviour>.Create(graph);
        p.GetBehaviour().clip = this;
        return p;
    }
}
