using System;
using System.ComponentModel;
using Cinema;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Timeline;


/// <summary>One fog / exposure / grade look; anything left un-ticked falls back to the base volume.</summary>
[Serializable, DisplayName("Atmosphere")]
public sealed class AtmosphereClip : PlayableAsset, ITimelineClipAsset
{
    public string label = "Atmosphere";

    [Header("Fog")]
    public bool fog = true;
    [Tooltip("Metres light travels before scattering: small = thick fog (base look is 700).")]
    [Min(1f)] public float meanFreePath = 700f;
    public Color fogAlbedo = new Color(1f, 0.97f, 0.92f);

    [Header("Exposure")]
    public bool exposure;
    [Tooltip("Fixed exposure EV (base look is 14). Lower = brighter image.")]
    public float fixedExposure = 14f;

    [Header("Colour grade")]
    public bool grade;
    public float postExposure;
    [Range(-100f, 100f)] public float contrast;
    [Range(-100f, 100f)] public float saturation;
    [ColorUsage(false, true)] public Color colorFilter = Color.white;

    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<AtmosphereClipBehaviour>.Create(graph);
        p.GetBehaviour().clip = this;
        return p;
    }
}
