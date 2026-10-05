using Nib.ProcTree;
using Nib.ProcTree.Core.Meshing;
using UnityEngine;

namespace Cinema
{
    /// <summary>
    /// Single-shot camera for the BCI scene: frames the part of the tree drawn right now, so the
    /// view opens tight on the seedling and pulls back smoothly as relaxation grows it. No director,
    /// no Timeline — the same framing maths as <see cref="TrackTreeShot"/>.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Cinema/Seedling Focus Camera")]
    public sealed class SeedlingFocusCamera : MonoBehaviour
    {
        public ProceduralTree tree;

        [Header("Lens")]
        [Range(10f, 90f)] public float fieldOfView = 30f;
        [Min(0.001f)] public float nearClip = 0.01f;

        [Header("Framing")]
        [Tooltip("Bounding-sphere multiplier: 1 = touching the frame edge, larger = more room.")]
        [Min(0.5f)] public float padding = 1.6f;
        [Tooltip("Smallest radius framed (m), so a just-sprouted seedling is not filmed from millimetres away.")]
        [Min(0.01f)] public float minRadius = 0.15f;
        [Tooltip("Largest radius framed (m); beyond it the camera stops pulling back. 0 = follow the whole tree.")]
        [Min(0f)] public float maxRadius;
        [Tooltip("Where to aim inside the bounds: 0 = bottom, 0.5 = centre, 1 = top.")]
        [Range(0f, 1f)] public float aimHeight = 0.45f;

        [Header("Direction")]
        [Tooltip("Degrees around the tree from its −Z side.")]
        public float azimuth = 20f;
        [Range(-20f, 85f)] public float elevation = 8f;
        [Tooltip("Slow orbit (deg/s); 0 = locked off.")]
        public float driftDegreesPerSecond = 1.5f;

        [Header("Motion")]
        [Tooltip("SmoothDamp time on aim point and distance, so growth spurts don't jolt the camera.")]
        [Min(0f)] public float smoothSeconds = 1.2f;

        Camera _cam;
        Vector3 _aim, _aimVel;
        float _dist, _distVel, _drift;
        bool _primed;
        TreeBakeData _cachedBake;
        float _cachedYear = float.NaN;
        Bounds _localLive;

        void OnEnable()
        {
            _cam = GetComponent<Camera>();
            if (tree == null) tree = FindAnyObjectByType<ProceduralTree>();
            _primed = false;
        }

        void LateUpdate()
        {
            if (tree == null || _cam == null) return;
            _cam.fieldOfView = fieldOfView;
            _cam.nearClipPlane = nearClip;

            var b = LiveBounds();
            float radius = Mathf.Max(b.extents.magnitude, minRadius);
            if (maxRadius > 0f) radius = Mathf.Min(radius, maxRadius);
            radius *= padding;
            float aspect = _cam.aspect > 0f ? _cam.aspect : 16f / 9f;
            float dist = TreeFraming.FitDistance(radius, fieldOfView, aspect);
            var aim = new Vector3(b.center.x, Mathf.Lerp(b.min.y, b.max.y, aimHeight), b.center.z);

            float dt = Time.deltaTime;
            if (!_primed || smoothSeconds <= 0f || dt <= 0f)
            {
                _aim = aim; _dist = dist; _aimVel = Vector3.zero; _distVel = 0f; _primed = true;
            }
            else
            {
                _aim = Vector3.SmoothDamp(_aim, aim, ref _aimVel, smoothSeconds, Mathf.Infinity, dt);
                _dist = Mathf.SmoothDamp(_dist, dist, ref _distVel, smoothSeconds, Mathf.Infinity, dt);
            }
            _drift += driftDegreesPerSecond * dt;

            var dir = Quaternion.Euler(elevation, azimuth + _drift, 0f) * Vector3.forward;
            transform.SetPositionAndRotation(_aim - dir * _dist, Quaternion.LookRotation(dir));
        }

        Bounds LiveBounds()
        {
            var root = tree.transform.position;
            var bake = tree.Bake;
            if (bake == null) return new Bounds(root + Vector3.up * 0.1f, Vector3.one * 0.2f);

            if (!ReferenceEquals(bake, _cachedBake) || !Mathf.Approximately(tree.Year, _cachedYear))
            {
                _cachedBake = bake;
                _cachedYear = tree.Year;
                if (!TreeFraming.LiveBounds(bake, tree.Year, out _localLive))
                    _localLive = new Bounds(Vector3.up * 0.05f, Vector3.one * 0.1f); // nothing sprouted: frame the root
            }
            return TreeFraming.ToWorld(_localLive, tree.transform.localToWorldMatrix);
        }
    }
}
