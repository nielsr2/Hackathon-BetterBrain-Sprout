using System;
using System.Collections;
using Sequence;
using UnityEngine;
using UnityEngine.Rendering;
using Glitch = Kino.PostProcessing.Glitch;
using KinoOverlay = Kino.PostProcessing.Overlay;
using KinoUtility = Kino.PostProcessing.Utility;
using Slice = Kino.PostProcessing.Slice;
using Streak = Kino.PostProcessing.Streak;

/// <summary>
/// Owns a runtime-only global "SequenceFX" Volume (priority above the camera's Kino volume) and
/// plays a glitch transition on it: Glitch, Slice, Streak, Utility and Scanlines build up to a
/// peak and decay. Also exposes the volume's Kino Overlay, which <see cref="SequenceController"/>
/// uses to show video full-screen inside the post stack, so the glitch distorts video and scene alike.
/// The profile is created in memory and never saved — the camera's profile asset is untouched.
/// </summary>
[AddComponentMenu("Sequence/Glitch Transition")]
public sealed class GlitchTransition : MonoBehaviour
{
    [Header("Timing")]
    [Min(0.1f)] public float durationSeconds = 2.5f;
    [Tooltip("Normalised time of maximum glitch; the scene→video cut lands here.")]
    [Range(0.1f, 0.9f)] public float peakAt = 0.55f;
    [Tooltip("Seconds before the peak during which the overlay stutters between scene and video.")]
    [Min(0f)] public float stutterSeconds = 0.35f;

    [Header("Peak strengths")]
    [Range(0f, 1f)] public float glitchBlock = 0.8f;
    [Range(0f, 1f)] public float glitchDrift = 0.6f;
    [Range(0f, 1f)] public float glitchJitter = 0.7f;
    [Range(0f, 1f)] public float glitchJump = 0.4f;
    [Range(0f, 1f)] public float glitchShake = 0.3f;
    public float sliceDisplacement = 0.06f;
    public float sliceRowCount = 60f;
    [Range(0f, 1f)] public float streakIntensity = 0.6f;
    [Range(0f, 1f)] public float scanlineIntensity = 0.5f;
    public float scanlineScrollSpeed = 60f;
    [Range(0f, 1f)] public float hueJitter = 0.35f;
    [Tooltip("Chance per frame of an inverted-colour flash, scaled by the envelope.")]
    [Range(0f, 1f)] public float invertFlashChance = 0.25f;

    [Tooltip("Priority of the runtime FX volume; must exceed the camera's Kino volume (10).")]
    public float volumePriority = 20f;

    public bool IsPlaying { get; private set; }
    public KinoOverlay Overlay { get; private set; }
    public KinoUtility Utility { get; private set; }

    Volume _volume;
    VolumeProfile _profile;
    Glitch _glitch;
    Slice _slice;
    Streak _streak;
    Scanlines _scanlines;
    float _baseScanlines, _baseScanlineScroll;

    void Awake()
    {
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "SequenceFX (runtime)";
        _profile.hideFlags = HideFlags.DontSave;

        Overlay = _profile.Add<KinoOverlay>();
        Utility = _profile.Add<KinoUtility>();
        _glitch = _profile.Add<Glitch>();
        _slice = _profile.Add<Slice>();
        _streak = _profile.Add<Streak>();
        _scanlines = _profile.Add<Scanlines>();

        Overlay.sourceType.Override(KinoOverlay.SourceType.Texture);
        Overlay.blendMode.Override(KinoOverlay.BlendMode.Normal);
        Overlay.sourceAlpha.Override(false);
        Overlay.opacity.Override(0f);

        ReadBaseScanlines();

        var go = new GameObject("SequenceFX Volume") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = volumePriority;
        _volume.sharedProfile = _profile;

        ResetEffects();
    }

    void OnDestroy()
    {
        if (_profile != null) Destroy(_profile);
    }

    // Scanlines are on (subtly) in the camera profile; the glitch boosts from that level, not from 0.
    void ReadBaseScanlines()
    {
        _baseScanlines = 0f;
        _baseScanlineScroll = 2f;
        foreach (var v in FindObjectsByType<Volume>())
        {
            var p = v.sharedProfile;
            if (p == null || p == _profile || !p.TryGet(out Scanlines s)) continue;
            if (s.lineIntensity.overrideState) _baseScanlines = s.lineIntensity.value;
            if (s.scrollSpeed.overrideState) _baseScanlineScroll = s.scrollSpeed.value;
        }
    }

    /// <summary>Hand every glitch parameter back to the camera profile.</summary>
    public void ResetEffects()
    {
        foreach (var c in new VolumeComponent[] { _glitch, _slice, _streak, _scanlines })
            c.SetAllOverridesTo(false);
        Utility.hueShift.overrideState = false;
        Utility.invert.overrideState = false;
    }

    /// <summary>
    /// Plays the transition. <paramref name="onCut"/> fires once when the overlay starts
    /// stuttering (start the video there); from the peak on the overlay stays fully opaque.
    /// </summary>
    public void Play(Action onCut, Action onComplete = null)
    {
        StopAllCoroutines();
        StartCoroutine(Run(onCut, onComplete));
    }

    IEnumerator Run(Action onCut, Action onComplete)
    {
        IsPlaying = true;
        float cutAt = Mathf.Max(0f, peakAt - stutterSeconds / durationSeconds);
        bool cut = false;
        float prevT = 0f, elapsed = 0f;

        while (elapsed < durationSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / durationSeconds);

            if (!cut && SequenceRules.CrossedPeak(prevT, t, cutAt))
            {
                cut = true;
                onCut?.Invoke();
            }
            if (cut && t < peakAt)
                Overlay.opacity.Override(UnityEngine.Random.value < Mathf.InverseLerp(cutAt, peakAt, t) * 0.8f + 0.2f ? 1f : 0f);
            else if (cut)
                Overlay.opacity.Override(1f);

            Apply(SequenceRules.GlitchEnvelope(t, peakAt));
            prevT = t;
            yield return null;
        }

        ResetEffects();
        IsPlaying = false;
        onComplete?.Invoke();
    }

    void Apply(float e)
    {
        // Small random flutter keeps the effect alive instead of a smooth ramp.
        float n = Mathf.Lerp(0.7f, 1.15f, UnityEngine.Random.value);
        _glitch.block.Override(Mathf.Clamp01(glitchBlock * e * n));
        _glitch.drift.Override(Mathf.Clamp01(glitchDrift * e));
        _glitch.jitter.Override(Mathf.Clamp01(glitchJitter * e * n));
        _glitch.jump.Override(Mathf.Clamp01(glitchJump * e * e));
        _glitch.shake.Override(Mathf.Clamp01(glitchShake * e * n));

        _slice.rowCount.Override(sliceRowCount);
        _slice.displacement.Override(sliceDisplacement * e * n);
        _slice.randomSeed.Override(UnityEngine.Random.Range(0, 10000));

        _streak.intensity.Override(streakIntensity * e);

        _scanlines.lineIntensity.Override(Mathf.Lerp(_baseScanlines, scanlineIntensity, e));
        _scanlines.scrollSpeed.Override(Mathf.Lerp(_baseScanlineScroll, scanlineScrollSpeed, e));

        Utility.hueShift.Override(UnityEngine.Random.Range(-hueJitter, hueJitter) * e);
        Utility.invert.Override(UnityEngine.Random.value < invertFlashChance * e ? 1f : 0f);
    }
}
