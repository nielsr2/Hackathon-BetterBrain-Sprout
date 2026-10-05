using System;
using System.ComponentModel;
using Cinema;
using Nib.ProcTree;
using Relaxation;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Scripted tree growth. Each clip maps its normalised time through a curve to growth 0..1.
/// Once the <see cref="RelaxationTreeDriver"/> is interactive (Interactive marker) the EEG owns growth
/// and this track stops writing. Gaps hold the last value.
/// </summary>
[TrackColor(0.35f, 0.8f, 0.3f)]
[TrackBindingType(typeof(ProceduralTree))]
[TrackClipType(typeof(GrowthClip))]
[DisplayName("Cinema/Tree Growth Track")]
public sealed class TreeGrowthTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        => ScriptPlayable<GrowthMixer>.Create(graph, inputCount);

    public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
    {
        if (director.GetGenericBinding(this) is ProceduralTree tree)
            driver.AddFromName<ProceduralTree>(tree.gameObject, "growth");
    }
}

public sealed class GrowthClipBehaviour : PlayableBehaviour
{
    public AnimationCurve growth;
}

public sealed class GrowthMixer : PlayableBehaviour
{
    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (playerData is not ProceduralTree tree) return;
        if (Application.isPlaying && tree.TryGetComponent(out RelaxationTreeDriver driver)
            && driver.isActiveAndEnabled && driver.interactive) return; // the EEG has it

        var mix = new WeightedMix();
        for (int i = 0; i < playable.GetInputCount(); i++)
        {
            float w = playable.GetInputWeight(i);
            if (w <= 0f) continue;
            var input = (ScriptPlayable<GrowthClipBehaviour>)playable.GetInput(i);
            double dur = input.GetDuration();
            float t = dur > 0 ? (float)(input.GetTime() / dur) : 1f;
            mix.Add(input.GetBehaviour().growth.Evaluate(Mathf.Clamp01(t)), w);
        }
        if (mix.HasValue) tree.Growth = mix.Value;
    }
}
