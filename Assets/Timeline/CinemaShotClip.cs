using System;
using System.ComponentModel;
using Cinema;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>One of the <see cref="CinemaDirector"/>'s shots; the clip's local time is the shot's time.</summary>
[Serializable, DisplayName("Cinema Shot")]
public sealed class CinemaShotClip : PlayableAsset, ITimelineClipAsset
{
    public Shot shot;
    public LensOverride lens = LensOverride.Default;

    public ClipCaps clipCaps => ClipCaps.Blending;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<CinemaClipBehaviour>.Create(graph);
        var b = p.GetBehaviour();
        b.kind = CinemaClipKind.Shot;
        b.shot = shot;
        b.lens = lens;
        return p;
    }
}
