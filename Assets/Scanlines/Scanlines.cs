using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Scrolling CRT-style scanlines. Drives Resources/Scanlines.shader ("Hidden/Custom/Scanlines");
/// neutral until Line Intensity is raised above 0.
/// </summary>
[System.Serializable, VolumeComponentMenu("Post-processing/Custom/Scanlines")]
public sealed class Scanlines : CustomPostProcessVolumeComponent, IPostProcessComponent
{
    public ClampedFloatParameter lineIntensity = new ClampedFloatParameter(0, 0, 1);
    [Tooltip("Sine frequency across the screen height (radians); ~2·π·lines for a given line count.")]
    public FloatParameter lineDensity = new FloatParameter(800);
    public FloatParameter scrollSpeed = new FloatParameter(2);

    Material _material;

    public bool IsActive() => _material != null && lineIntensity.value > 0;

    public override CustomPostProcessInjectionPoint injectionPoint =>
        CustomPostProcessInjectionPoint.AfterPostProcess;

    public override void Setup()
    {
        _material = CoreUtils.CreateEngineMaterial("Hidden/Custom/Scanlines");
    }

    public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle srcRT, RTHandle destRT)
    {
        if (_material == null) return;
        _material.SetFloat("_LineIntensity", lineIntensity.value);
        _material.SetFloat("_LineDensity", lineDensity.value);
        _material.SetFloat("_ScrollSpeed", scrollSpeed.value);
        _material.SetTexture("_InputTexture", srcRT);
        HDUtils.DrawFullScreen(cmd, _material, destRT);
    }

    public override void Cleanup()
    {
        CoreUtils.Destroy(_material);
    }
}
