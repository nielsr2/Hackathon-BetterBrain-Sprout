using System;
using System.Numerics;

namespace Nib.ProcTree.Core
{
    /// <summary>Small numeric helpers shared by simulation and meshing (System.Numerics only).</summary>
    public static class MathHelpers
    {
        /// <summary>Clamps to [0,1].</summary>
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>Linear interpolation.</summary>
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Hermite smoothstep on [0,1].</summary>
        public static float Smooth01(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Normalizes, falling back to <paramref name="fallback"/> for near-zero vectors.</summary>
        public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            float l2 = v.LengthSquared();
            return l2 > 1e-12f ? v / MathF.Sqrt(l2) : fallback;
        }

        /// <summary>Any unit vector perpendicular to unit <paramref name="n"/>.</summary>
        public static Vector3 AnyPerpendicular(Vector3 n)
        {
            var a = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            return Vector3.Normalize(Vector3.Cross(n, a));
        }

        /// <summary>Rotates <paramref name="v"/> about unit <paramref name="axis"/> by <paramref name="radians"/>.</summary>
        public static Vector3 Rotate(Vector3 v, Vector3 axis, float radians)
            => Vector3.Transform(v, Quaternion.CreateFromAxisAngle(axis, radians));
    }
}
