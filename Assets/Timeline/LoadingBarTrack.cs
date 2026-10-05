using System.ComponentModel;
using Cinema;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Drives the <see cref="LoadingBarOverlay"/>: clip ease-in/out fades it, the clip's curve fills the
/// bar, and the status line steps through the clip's messages as progress rises.
/// </summary>
[TrackColor(0.25f, 1f, 0.42f)]
[TrackBindingType(typeof(LoadingBarOverlay))]
[TrackClipType(typeof(LoadingBarClip))]
[DisplayName("Cinema/Loading Bar Track")]
public sealed class LoadingBarTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        => ScriptPlayable<LoadingBarMixer>.Create(graph, inputCount);
}

public sealed class LoadingBarClipBehaviour : PlayableBehaviour
{
    public LoadingBarClip clip;
}

public sealed class LoadingBarMixer : PlayableBehaviour
{
    LoadingBarOverlay _overlay;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        _overlay = playerData as LoadingBarOverlay;
        if (_overlay == null) return;

        var progress = new WeightedMix();
        LoadingBarClip top = null;
        float topW = 0f, total = 0f;
        for (int i = 0; i < playable.GetInputCount(); i++)
        {
            float w = playable.GetInputWeight(i);
            if (w <= 0f) continue;
            var input = (ScriptPlayable<LoadingBarClipBehaviour>)playable.GetInput(i);
            var c = input.GetBehaviour().clip;
            double dur = input.GetDuration();
            float u = dur > 0 ? Mathf.Clamp01((float)(input.GetTime() / dur)) : 1f;
            progress.Add(Mathf.Clamp01(c.progress.Evaluate(u)), w);
            total += w;
            if (w > topW) { topW = w; top = c; }
        }

        _overlay.visibility = Mathf.Clamp01(total);
        if (top == null) return;
        _overlay.progress = progress.Value;
        _overlay.title = top.title;
        _overlay.status = top.StatusAt(progress.Value);
    }

    public override void OnPlayableDestroy(Playable playable)
    {
        if (_overlay != null) _overlay.visibility = 0f;
    }
}
