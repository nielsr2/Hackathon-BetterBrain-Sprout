using System;
using System.ComponentModel;
using Cinema;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Timeline;

/// <summary>
/// Fog, exposure and colour grade as clips. Bind the scene's base Volume (Post Volume): the mixer
/// layers a runtime-only global Volume one priority above it, so the profile asset is never touched.
/// Anything a clip doesn't override falls back to the base volume's value. Gaps hold the last look.
/// </summary>
[TrackColor(0.45f, 0.65f, 0.95f)]
[TrackBindingType(typeof(Volume))]
[TrackClipType(typeof(AtmosphereClip))]
[DisplayName("Cinema/Atmosphere Track")]
public sealed class AtmosphereTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        foreach (var c in GetClips())
            if (c.asset is AtmosphereClip a && !string.IsNullOrEmpty(a.label)) c.displayName = a.label;
        return ScriptPlayable<AtmosphereMixer>.Create(graph, inputCount);
    }
}

public sealed class AtmosphereClipBehaviour : PlayableBehaviour
{
    public AtmosphereClip clip;
}

public sealed class AtmosphereMixer : PlayableBehaviour
{
    Volume _volume;
    VolumeProfile _profile;
    Fog _fog;
    Exposure _exposure;
    ColorAdjustments _grade;

    // Base look, read from the bound volume when the graph starts.
    float _baseMfp = 700f, _baseEv = 14f, _basePost, _baseContrast, _baseSat;
    Color _baseAlbedo = Color.white, _baseFilter = Color.white;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (playerData is not Volume baseVolume) return;
        if (_volume == null) Create(baseVolume);

        WeightedMix mfp = default, albedo = default, ev = default, post = default, contrast = default, sat = default, filter = default;
        float total = 0f;
        for (int i = 0; i < playable.GetInputCount(); i++)
        {
            float w = playable.GetInputWeight(i);
            if (w <= 0f) continue;
            total += w;
            var c = ((ScriptPlayable<AtmosphereClipBehaviour>)playable.GetInput(i)).GetBehaviour().clip;
            mfp.Add(c.fog ? c.meanFreePath : _baseMfp, w);
            albedo.Add((Vector4)(c.fog ? c.fogAlbedo : _baseAlbedo), w);
            ev.Add(c.exposure ? c.fixedExposure : _baseEv, w);
            post.Add(c.grade ? c.postExposure : _basePost, w);
            contrast.Add(c.grade ? c.contrast : _baseContrast, w);
            sat.Add(c.grade ? c.saturation : _baseSat, w);
            filter.Add((Vector4)(c.grade ? c.colorFilter : _baseFilter), w);
        }
        if (total <= 0f) return; // gap: hold

        _fog.meanFreePath.Override(mfp.Value);
        _fog.albedo.Override((Color)albedo.Vector);
        _exposure.fixedExposure.Override(ev.Value);
        _grade.postExposure.Override(post.Value);
        _grade.contrast.Override(contrast.Value);
        _grade.saturation.Override(sat.Value);
        _grade.colorFilter.Override((Color)filter.Vector);
        _volume.weight = Mathf.Clamp01(total);
    }

    void Create(Volume baseVolume)
    {
        var p = baseVolume.sharedProfile;
        if (p != null)
        {
            if (p.TryGet(out Fog f))
            {
                if (f.meanFreePath.overrideState) _baseMfp = f.meanFreePath.value;
                if (f.albedo.overrideState) _baseAlbedo = f.albedo.value;
            }
            if (p.TryGet(out Exposure e) && e.fixedExposure.overrideState) _baseEv = e.fixedExposure.value;
            if (p.TryGet(out ColorAdjustments g))
            {
                _basePost = g.postExposure.value;
                _baseContrast = g.contrast.value;
                _baseSat = g.saturation.value;
                _baseFilter = g.colorFilter.value;
            }
        }

        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "Atmosphere (timeline)";
        _profile.hideFlags = HideFlags.DontSave;
        _fog = _profile.Add<Fog>();
        _fog.enabled.Override(true);
        _exposure = _profile.Add<Exposure>();
        _exposure.mode.Override(ExposureMode.Fixed);
        _grade = _profile.Add<ColorAdjustments>();

        var go = new GameObject("Atmosphere Volume (timeline)") { hideFlags = HideFlags.HideAndDontSave };
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = baseVolume.priority + 1f;
        _volume.sharedProfile = _profile;
        _volume.weight = 0f;
    }

    public override void OnPlayableDestroy(Playable playable)
    {
        if (_volume != null) Kill(_volume.gameObject);
        if (_profile != null)
        {
            foreach (var c in _profile.components) Kill(c);
            Kill(_profile);
        }
        _volume = null;
        _profile = null;
    }

    static void Kill(UnityEngine.Object o)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(o);
        else UnityEngine.Object.DestroyImmediate(o);
    }
}
