using UnityEngine;

namespace Cinema
{
    /// <summary>
    /// Keeps the tree framed from a fixed direction: either the part that is drawn right now
    /// (follows the seedling as it grows) or the mature crown (the whole tree always in view).
    /// Distance is solved from the bounding sphere, the FOV and the camera aspect.
    /// </summary>
    [AddComponentMenu("Cinema/Track Tree Shot")]
    public sealed class TrackTreeShot : CinemaShot
    {
        public enum Frame { LiveTree, MatureTree }

        [Header("Framing")]
        [Tooltip("LiveTree follows growth; MatureTree keeps the full-grown tree in view.")]
        public Frame frame = Frame.LiveTree;
        [Tooltip("Bounding-sphere multiplier: 1 = touching the frame edge, larger = more room around the tree.")]
        [Min(0.5f)] public float padding = 1.4f;
        [Tooltip("Smallest radius framed (m), so a just-sprouted seedling is not filmed from millimetres away.")]
        [Min(0.01f)] public float minRadius = 0.15f;
        [Tooltip("Where to aim inside the bounds: 0 = bottom, 0.5 = centre, 1 = top.")]
        [Range(0f, 1f)] public float aimHeight = 0.5f;

        [Header("Direction")]
        [Tooltip("Degrees around the tree from its −Z side.")]
        public float azimuth;
        [Tooltip("Degrees above the horizon, looking down at the tree.")]
        [Range(-20f, 85f)] public float elevation = 10f;
        [Tooltip("Slow orbit while the shot is live (deg/s); 0 = locked off.")]
        public float driftDegreesPerSecond;

        [Header("Motion")]
        [Tooltip("SmoothDamp time on aim point and distance, so growth spurts don't jolt the camera.")]
        [Min(0f)] public float smoothSeconds = 1f;

        CinemaDirector _director;
        Vector3 _aim, _aimVel;
        float _dist, _distVel, _drift;

        protected override void Compute(CinemaDirector director, float dt, bool snap)
        {
            if (director != null) _director = director;
            if (_director == null) return;
            if (snap) _drift = 0f;

            var b = frame == Frame.LiveTree ? _director.LiveBounds : _director.MatureBounds;
            float radius = Mathf.Max(b.extents.magnitude, minRadius) * padding;
            float dist = TreeFraming.FitDistance(radius, fieldOfView, _director.Aspect);
            var aim = new Vector3(b.center.x, Mathf.Lerp(b.min.y, b.max.y, aimHeight), b.center.z);

            if (snap || smoothSeconds <= 0f || dt <= 0f) // not live: sit on target so the gizmo stays current
            {
                _aim = aim; _dist = dist; _aimVel = Vector3.zero; _distVel = 0f;
            }
            else
            {
                _aim = Vector3.SmoothDamp(_aim, aim, ref _aimVel, smoothSeconds, Mathf.Infinity, dt);
                _dist = Mathf.SmoothDamp(_dist, dist, ref _distVel, smoothSeconds, Mathf.Infinity, dt);
            }
            _drift += driftDegreesPerSecond * dt;

            var dir = Quaternion.Euler(elevation, azimuth + _drift, 0f) * Vector3.forward;
            SetPose(_aim - dir * _dist, _aim);
        }
    }
}
