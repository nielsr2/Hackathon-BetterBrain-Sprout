using UnityEngine;

namespace Cinema
{
    /// <summary>
    /// Establishing shot: orbits the tree once along an eased arc — sweeping, dollying and craning
    /// between a start and an end pose — then holds still on the end pose.
    /// Angles are measured around the tree from its −Z side (0° = looking toward +Z).
    /// </summary>
    [AddComponentMenu("Cinema/Orbit Shot")]
    public sealed class OrbitShot : CinemaShot
    {
        [Header("Look target")]
        [Tooltip("Orbit/look centre; empty = the director's tree root.")]
        public Transform center;
        [Tooltip("Added to the centre for the look-at point (e.g. up the trunk).")]
        public Vector3 lookOffset = new Vector3(0f, 3f, 0f);

        [Header("Arc")]
        public float startAngle = -70f;
        [Tooltip("Degrees travelled around the tree; negative orbits the other way.")]
        public float sweepDegrees = 110f;
        [Min(0.1f)] public float startRadius = 38f;
        [Min(0.1f)] public float endRadius = 20f;
        [Tooltip("Camera height above the centre at the start / end of the orbit.")]
        public float startHeight = 16f;
        public float endHeight = 5f;

        [Header("Timing")]
        [Min(0f)] public float durationSeconds = 18f;
        [Tooltip("Progress 0..1 over the orbit. The default eases out so it glides to a stop.")]
        public AnimationCurve easing = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));

        [Header("Edit-mode preview")]
        [Tooltip("Where along the orbit the gizmo sits outside Play mode (1 = settled pose).")]
        [Range(0f, 1f)] public float previewProgress = 1f;

        /// <summary>True once the orbit has come to rest.</summary>
        public bool Settled => ShotTime >= durationSeconds;
        /// <summary>Seconds spent at rest so far.</summary>
        public float HeldSeconds => Mathf.Max(0f, ShotTime - durationSeconds);

        CinemaDirector _director;

        protected override void Compute(CinemaDirector director, float dt, bool snap)
        {
            if (director != null) _director = director;
            Vector3 c = center != null ? center.position
                : _director != null && _director.tree != null ? _director.tree.transform.position
                : transform.position;

            float t = Application.isPlaying || TimeIsDriven
                ? (durationSeconds > 0f ? Mathf.Clamp01(ShotTime / durationSeconds) : 1f)
                : previewProgress;
            SetPose(PositionAt(c, startAngle, sweepDegrees, startRadius, endRadius, startHeight, endHeight, easing.Evaluate(t)),
                c + lookOffset);
        }

        /// <summary>Camera position at eased orbit progress <paramref name="u"/> (shared with Timeline orbit clips).</summary>
        public static Vector3 PositionAt(Vector3 center, float startAngle, float sweepDegrees,
            float startRadius, float endRadius, float startHeight, float endHeight, float u)
        {
            float angle = startAngle + sweepDegrees * u;
            float radius = Mathf.LerpUnclamped(startRadius, endRadius, u);
            float height = Mathf.LerpUnclamped(startHeight, endHeight, u);
            return center + Quaternion.Euler(0f, angle, 0f) * Vector3.back * radius + Vector3.up * height;
        }
    }
}
