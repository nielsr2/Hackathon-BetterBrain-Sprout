using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>Tree growth (0 nothing … 1 mature) over the clip's normalised time.</summary>
[Serializable, DisplayName("Growth")]
public sealed class GrowthClip : PlayableAsset, ITimelineClipAsset
{
    [Tooltip("Growth (0 nothing … 1 mature) over the clip's normalised time.")]
    public AnimationCurve growth = AnimationCurve.EaseInOut(0f, 0.02f, 1f, 0.12f);
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<GrowthClipBehaviour>.Create(graph);
        p.GetBehaviour().growth = growth;
        return p;
    }
}
