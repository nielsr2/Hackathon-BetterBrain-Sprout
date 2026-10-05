using System;
using System.ComponentModel;
using Cinema;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Sun keyframes as clips: each clip is a sun state; crossfade two clips to move between them
/// (dawn → noon → golden hour). Elevation/azimuth are where the sun <i>is</i> in the sky;
/// the light points the opposite way. Gaps hold the last state.
/// </summary>
[TrackColor(1f, 0.55f, 0.15f)]
[TrackBindingType(typeof(Light))]
[TrackClipType(typeof(SunClip))]
[DisplayName("Cinema/Sun Track")]
public sealed class SunTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        foreach (var c in GetClips())
            if (c.asset is SunClip s && !string.IsNullOrEmpty(s.label)) c.displayName = s.label;
        return ScriptPlayable<SunMixer>.Create(graph, inputCount);
    }

    public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
    {
        if (director.GetGenericBinding(this) is not Light light) return;
        TimelinePreview.AddTransform(driver, light.gameObject);
        driver.AddFromName<Light>(light.gameObject, "m_Intensity");
        driver.AddFromName<Light>(light.gameObject, "m_ColorTemperature");
        driver.AddFromName<Light>(light.gameObject, "m_Color");
    }
}

public sealed class SunClipBehaviour : PlayableBehaviour
{
    public SunClip clip;
}

public sealed class SunMixer : PlayableBehaviour
{
    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (playerData is not Light light) return;
        WeightedMix elev = default, azim = default, lux = default, kelvin = default, tint = default;
        for (int i = 0; i < playable.GetInputCount(); i++)
        {
            float w = playable.GetInputWeight(i);
            if (w <= 0f) continue;
            var c = ((ScriptPlayable<SunClipBehaviour>)playable.GetInput(i)).GetBehaviour().clip;
            elev.Add(c.elevation, w);
            azim.AddAngle(c.azimuth, w);
            lux.Add(c.intensity, w);
            kelvin.Add(c.colorTemperature, w);
            tint.Add((Vector4)c.tint, w);
        }
        if (!elev.HasValue) return;

        // Light forward points from the sun toward the ground: pitch down by the elevation, face away from the azimuth.
        light.transform.rotation = Quaternion.Euler(elev.Value, azim.Angle + 180f, 0f);
        light.intensity = lux.Value;
        light.useColorTemperature = true;
        light.colorTemperature = kelvin.Value;
        light.color = (Color)tint.Vector;
    }
}
