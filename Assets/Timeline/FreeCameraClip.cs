using System;
using System.ComponentModel;
using Cinema;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Puts the camera on a <see cref="FilmCameraRig"/> you keyframe yourself (Animation track on the
/// rig, record button). The rig's own Field Of View is keyable too; Lens here overrides it.
/// </summary>
[Serializable, DisplayName("Free Camera")]
public sealed class FreeCameraClip : PlayableAsset, ITimelineClipAsset
{
    public ExposedReference<FilmCameraRig> rig;
    public LensOverride lens;

    public ClipCaps clipCaps => ClipCaps.Blending;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<CinemaClipBehaviour>.Create(graph);
        var b = p.GetBehaviour();
        b.kind = CinemaClipKind.Free;
        b.rig = rig.Resolve(graph.GetResolver());
        b.lens = lens;
        return p;
    }
}
