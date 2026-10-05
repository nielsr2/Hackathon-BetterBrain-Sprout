using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>Hands the camera back to the director's growth-based switching (close-up → medium → full tree).</summary>
[Serializable, DisplayName("Director Auto")]
public sealed class DirectorAutoClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<CinemaClipBehaviour>.Create(graph);
        p.GetBehaviour().kind = CinemaClipKind.Auto;
        return p;
    }
}
