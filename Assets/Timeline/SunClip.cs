using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>One sun state; crossfade two clips to move the sun between them.</summary>
[Serializable, DisplayName("Sun")]
public sealed class SunClip : PlayableAsset, ITimelineClipAsset
{
    [Tooltip("Shown on the clip in the Timeline window.")]
    public string label = "Sun";
    [Tooltip("Degrees above the horizon (negative = below).")]
    [Range(-10f, 90f)] public float elevation = 20f;
    [Tooltip("Compass direction of the sun, degrees (0 = the sun sits toward +Z).")]
    public float azimuth = 25f;
    [Tooltip("Illuminance in lux.")]
    [Min(0f)] public float intensity = 100000f;
    [Range(1500f, 20000f)] public float colorTemperature = 5200f;
    public Color tint = Color.white;

    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<SunClipBehaviour>.Create(graph);
        p.GetBehaviour().clip = this;
        return p;
    }
}
