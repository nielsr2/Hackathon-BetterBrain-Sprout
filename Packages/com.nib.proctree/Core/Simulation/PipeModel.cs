using System;

namespace Nib.ProcTree.Core.Simulation
{
    /// <summary>
    /// Thickness over time (spec §4.4): a segment is as thick as the living shoot tips it feeds,
    /// r = minTwigRadius · tips^(1/pipeExponent), sampled at the 16 shared key years. A running max
    /// keeps wood from ever shrinking, and the trunk base gets a buttress flare.
    /// </summary>
    public static class PipeModel
    {
        /// <summary>Fills <see cref="TreeHistory.radiusKeys"/>.</summary>
        public static void Compute(TreeHistory h, TreeParams p)
        {
            const int K = TreeHistory.KeyCount;
            int n = h.segments.Count;
            var counts = new float[n * K];
            var keys = h.KeyYears;

            // Each axis contributes 1 tip while alive, entered at its last segment; summing children
            // into parents (reverse creation order) hands it to every ancestor.
            for (int a = 0; a < h.axes.Count; a++)
            {
                var ax = h.axes[a];
                if (ax.firstSegment < 0) continue;
                int last = ax.firstSegment;
                while (h.segments[last].mainChild >= 0) last = h.segments[last].mainChild;
                float born = h.segments[ax.firstSegment].birth;
                for (int k = 0; k < K; k++)
                    if (born <= keys[k] && keys[k] < ax.death) counts[last * K + k] += 1f;
            }
            for (int s = n - 1; s >= 0; s--)
            {
                int par = h.segments[s].parent;
                if (par < 0) continue;
                for (int k = 0; k < K; k++) counts[par * K + k] += counts[s * K + k];
            }

            float inv = 1f / p.pipeExponent;
            var r = new float[n * K];
            for (int s = 0; s < n; s++)
            {
                var seg = h.segments[s];
                float flare = 1f;
                if (seg.axis == 0 && seg.start.Y < p.rootFlareHeight)
                {
                    float t = 1f - seg.start.Y / p.rootFlareHeight;
                    flare += p.rootFlare * t * t;
                }
                float running = 0f;
                for (int k = 0; k < K; k++)
                {
                    float c = MathF.Max(1f, counts[s * K + k]);
                    float v = p.minTwigRadius * MathF.Pow(c, inv) * flare;
                    running = MathF.Max(running, v);
                    r[s * K + k] = running;
                }
            }
            h.radiusKeys = r;
        }
    }
}
