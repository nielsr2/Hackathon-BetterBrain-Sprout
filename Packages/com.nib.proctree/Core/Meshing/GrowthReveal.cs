using System.Numerics;

namespace Nib.ProcTree.Core.Meshing
{
    /// <summary>
    /// CPU reference of the GPU growth reveal. <c>Shaders/Include/TreeGrowth.hlsl</c> mirrors this
    /// line for line; the tests run against this copy.
    /// </summary>
    public static class GrowthReveal
    {
        /// <summary>Years a dropped leaf takes to shrink away.</summary>
        public const float LeafDropYears = 0.15f;

        /// <summary>Radius of segment <paramref name="s"/> at <paramref name="year"/> (shared key grid).</summary>
        public static float Radius(TreeBakeData b, int s, float year)
            => TreeHistory.EvalKeys(b.radiusKeys, s * TreeHistory.KeyCount, b.keyYears, year);

        /// <summary>Revealed position of bark vertex <paramref name="v"/> at <paramref name="year"/>.</summary>
        public static Vector3 BarkVertex(TreeBakeData b, int v, float year)
        {
            var bark = b.bark;
            int si = (int)bark.uv1[v].X;
            float fraction = bark.uv1[v].Y;
            float radialScale = bark.uv2[v].X;
            var seg = b.segments[si];

            if (year <= seg.birth) return seg.start;                       // not yet born: collapsed

            float progress = MathHelpers.Clamp01((year - seg.birth) / seg.growDuration);
            float t = fraction < progress ? fraction : progress;
            var c = Vector3.Lerp(seg.start, seg.end, t);

            float r = Radius(b, si, year) * radialScale;
            if (fraction > progress) r = 0f;                               // growing tip: a point
            if (year > seg.death)
                r *= 1f - MathHelpers.Clamp01((year - seg.death) / b.deathFadeYears);

            return c + bark.radial[v] * r;
        }

        /// <summary>Leaf scale at <paramref name="year"/>: unfolds over its flush, shrinks after it drops.</summary>
        public static float LeafScale(LeafGpu l, float year)
        {
            if (year <= l.birth) return 0f;
            float unfold = MathHelpers.Smooth01((year - l.birth) / l.flush);
            float drop = 1f - MathHelpers.Smooth01((year - l.death) / LeafDropYears);
            return unfold * drop;
        }

        /// <summary>True if the leaf has any size at <paramref name="year"/>.</summary>
        public static bool LeafLive(LeafGpu l, float year) => year > l.birth && year < l.death + LeafDropYears;
    }
}
