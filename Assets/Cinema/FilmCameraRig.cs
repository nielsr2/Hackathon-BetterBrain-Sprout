using UnityEngine;

namespace Cinema
{
    /// <summary>
    /// A hand-animated camera for film takes: key its transform and lens with Timeline's record
    /// button (Animation track bound to the Animator on this object's parent). A Free Camera clip on
    /// the Cinema track puts the real camera here.
    /// </summary>
    [AddComponentMenu("Cinema/Film Camera Rig")]
    public sealed class FilmCameraRig : MonoBehaviour
    {
        [Range(5f, 120f)] public float fieldOfView = 35f;
        [Min(0.001f)] public float nearClip = 0.05f;

        public CinemaPose Pose => new CinemaPose
        {
            position = transform.position,
            rotation = transform.rotation,
            fieldOfView = fieldOfView,
            nearClip = nearClip,
        };

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, fieldOfView, 3f, nearClip, 16f / 9f);
        }
    }
}
