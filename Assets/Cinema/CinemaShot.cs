using UnityEngine;

namespace Cinema
{
    /// <summary>Where a shot wants the camera this frame.</summary>
    public struct CinemaPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public float fieldOfView;
        public float nearClip;

        public static CinemaPose Lerp(in CinemaPose a, in CinemaPose b, float t) => new CinemaPose
        {
            position = Vector3.LerpUnclamped(a.position, b.position, t),
            rotation = Quaternion.SlerpUnclamped(a.rotation, b.rotation, t),
            fieldOfView = Mathf.LerpUnclamped(a.fieldOfView, b.fieldOfView, t),
            nearClip = Mathf.LerpUnclamped(a.nearClip, b.nearClip, t),
        };
    }

    /// <summary>
    /// A virtual camera: computes a <see cref="CinemaPose"/> every frame, and <see cref="CinemaDirector"/>
    /// moves the one real camera onto it (so the Kino/post stack on that camera keeps working). The
    /// shot's own transform is not used; its pose is drawn as a frustum gizmo when selected.
    /// </summary>
    public abstract class CinemaShot : MonoBehaviour
    {
        [Header("Lens")]
        [Range(5f, 120f)] public float fieldOfView = 40f;
        [Tooltip("Near clip while this shot is live; keep it small for close-ups so the seedling is not clipped.")]
        [Min(0.001f)] public float nearClip = 0.1f;

        /// <summary>Seconds since this shot was last (re)started by the director, or the Timeline clip's local time.</summary>
        public float ShotTime { get; private set; }
        /// <summary>True while a Timeline clip sets <see cref="ShotTime"/> (also when scrubbing in edit mode).</summary>
        public bool TimeIsDriven { get; private set; }
        public CinemaPose Pose => _pose;

        protected CinemaPose _pose;

        /// <summary>Restart the shot's clock and snap any smoothing to its current target.</summary>
        public void Restart()
        {
            ShotTime = 0f;
            TimeIsDriven = false;
            Compute(null, 0f, snap: true);
        }

        /// <summary>Evaluate at an explicit clip-local <paramref name="time"/> (Timeline).</summary>
        public void Evaluate(CinemaDirector director, float time, float dt, bool snap)
        {
            // A clip that just became active snaps its smoothing instead of easing in from a stale pose.
            bool fresh = _lastEvaluatedFrame < Time.frameCount - 1;
            _lastEvaluatedFrame = Time.frameCount;
            ShotTime = time;
            TimeIsDriven = true;
            Compute(director, dt, snap || fresh);
        }

        int _lastEvaluatedFrame = int.MinValue;

        /// <summary>Called by the director each frame. <paramref name="live"/> advances the shot's clock.</summary>
        public void Tick(CinemaDirector director, float dt, bool live, bool snap)
        {
            if (live) ShotTime += dt;
            if (live) TimeIsDriven = false;
            Compute(director, live ? dt : 0f, snap);
        }

        /// <summary>Update <see cref="_pose"/>. <paramref name="director"/> may be null on Restart (use the cached one).</summary>
        protected abstract void Compute(CinemaDirector director, float dt, bool snap);

        protected void SetPose(Vector3 position, Vector3 lookAt)
        {
            var fwd = lookAt - position;
            _pose = new CinemaPose
            {
                position = position,
                rotation = fwd.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(fwd, Vector3.up) : _pose.rotation,
                fieldOfView = fieldOfView,
                nearClip = nearClip,
            };
        }

        protected virtual void OnDrawGizmosSelected()
        {
            if (_pose.rotation == default) return;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.matrix = Matrix4x4.TRS(_pose.position, _pose.rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, _pose.fieldOfView, 3f, _pose.nearClip, 16f / 9f);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
