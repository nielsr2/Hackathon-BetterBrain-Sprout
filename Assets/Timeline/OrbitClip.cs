using System;
using System.ComponentModel;
using Cinema;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// An orbit around the tree with its own settings: sweep, distance and height from start to end,
/// eased over <see cref="settleSeconds"/> (the slow-down), then held still for the rest of the clip.
/// Angles are measured around the tree from its −Z side.
/// </summary>
[Serializable, DisplayName("Orbit")]
public sealed class OrbitClip : PlayableAsset, ITimelineClipAsset
{
    [Header("Look target (relative to the tree root)")]
    public Vector3 lookOffset = new Vector3(0f, 6f, 0f);

    [Header("Arc")]
    public float startAngle = -40f;
    [Tooltip("Degrees travelled around the tree; negative orbits the other way.")]
    public float sweepDegrees = 70f;
    [Min(0.1f)] public float startRadius = 30f;
    [Min(0.1f)] public float endRadius = 24f;
    [Tooltip("Camera height above the tree root at the start / end.")]
    public float startHeight = 4f;
    public float endHeight = 3f;

    [Header("Speed")]
    [Tooltip("Seconds from the clip start until the orbit comes to rest; the camera then holds still.")]
    [Min(0.1f)] public float settleSeconds = 20f;
    [Tooltip("Progress 0..1 over the settle time. A long flat tail = a long, gentle slow-down.")]
    public AnimationCurve easing = new AnimationCurve(new Keyframe(0f, 0f, 0f, 1.6f), new Keyframe(1f, 1f, 0f, 0f));

    [Header("Lens")]
    [Range(5f, 120f)] public float fieldOfView = 35f;
    [Min(0.001f)] public float nearClip = 0.1f;
    public LensOverride lens = LensOverride.Default;

    public ClipCaps clipCaps => ClipCaps.Blending;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var p = ScriptPlayable<CinemaClipBehaviour>.Create(graph);
        var b = p.GetBehaviour();
        b.kind = CinemaClipKind.Orbit;
        b.orbit = this;
        b.lens = lens;
        return p;
    }

    /// <summary>Camera pose <paramref name="time"/> seconds into the clip.</summary>
    public CinemaPose PoseAt(CinemaDirector director, float time)
    {
        var c = director.tree != null ? director.tree.transform.position : director.transform.position;
        float u = easing.Evaluate(Mathf.Clamp01(time / settleSeconds));
        var pos = OrbitShot.PositionAt(c, startAngle, sweepDegrees, startRadius, endRadius, startHeight, endHeight, u);
        return new CinemaPose
        {
            position = pos,
            rotation = Quaternion.LookRotation(c + lookOffset - pos, Vector3.up),
            fieldOfView = fieldOfView,
            nearClip = nearClip,
        };
    }
}
