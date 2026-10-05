using Nib.ProcTree.Core.Meshing;
using UnityEngine;

namespace Cinema
{
    /// <summary>Pure framing maths for the cinema shots (EditMode-testable).</summary>
    public static class TreeFraming
    {
        /// <summary>
        /// Tree-local bounds of what is drawn at <paramref name="year"/>: every segment that has been
        /// born (extended linearly over its grow duration, as TreeGrowth.hlsl does) and not yet faded,
        /// plus every live leaf padded by its blade length. False if nothing is alive yet.
        /// </summary>
        public static bool LiveBounds(TreeBakeData bake, float year, out Bounds bounds)
        {
            bool any = false;
            Vector3 min = default, max = default;

            foreach (var s in bake.segments)
            {
                if (s.birth > year || year >= s.death + bake.deathFadeYears) continue;
                float p = s.growDuration > 0f ? Mathf.Clamp01((year - s.birth) / s.growDuration) : 1f;
                var a = new Vector3(s.start.X, s.start.Y, s.start.Z);
                var b = Vector3.LerpUnclamped(a, new Vector3(s.end.X, s.end.Y, s.end.Z), p);
                Add(ref any, ref min, ref max, a, 0f);
                Add(ref any, ref min, ref max, b, 0f);
            }

            foreach (var l in bake.leaves)
            {
                if (l.birth > year) break; // sorted by birth
                if (year >= l.death) continue;
                Add(ref any, ref min, ref max, new Vector3(l.position.X, l.position.Y, l.position.Z), l.size);
            }

            bounds = any ? new Bounds((min + max) * 0.5f, max - min) : default;
            return any;
        }

        /// <summary>Camera distance at which a sphere of <paramref name="radius"/> just fits both FOV axes.</summary>
        public static float FitDistance(float radius, float verticalFovDeg, float aspect)
        {
            float halfV = Mathf.Clamp(verticalFovDeg, 1f, 179f) * 0.5f * Mathf.Deg2Rad;
            float halfH = Mathf.Atan(Mathf.Tan(halfV) * Mathf.Max(aspect, 0.01f));
            return radius / Mathf.Sin(Mathf.Min(halfV, halfH));
        }

        /// <summary>World AABB of a local AABB under <paramref name="localToWorld"/>.</summary>
        public static Bounds ToWorld(Bounds local, Matrix4x4 localToWorld)
        {
            var c = localToWorld.MultiplyPoint3x4(local.center);
            var e = local.extents;
            var x = localToWorld.MultiplyVector(new Vector3(e.x, 0f, 0f));
            var y = localToWorld.MultiplyVector(new Vector3(0f, e.y, 0f));
            var z = localToWorld.MultiplyVector(new Vector3(0f, 0f, e.z));
            var ext = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(c, ext * 2f);
        }

        static void Add(ref bool any, ref Vector3 min, ref Vector3 max, Vector3 p, float pad)
        {
            var lo = p - Vector3.one * pad;
            var hi = p + Vector3.one * pad;
            if (!any) { min = lo; max = hi; any = true; return; }
            min = Vector3.Min(min, lo);
            max = Vector3.Max(max, hi);
        }
    }
}
