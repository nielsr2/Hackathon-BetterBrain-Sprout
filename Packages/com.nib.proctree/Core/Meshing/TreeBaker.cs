using System;
using System.Numerics;

namespace Nib.ProcTree.Core.Meshing
{
    /// <summary>
    /// Turns a simulated <see cref="TreeHistory"/> into GPU-ready arrays (segments, radius keys,
    /// birth-sorted leaves, bark mesh, leaf mesh). Pure C#: runs on the worker thread right after
    /// the simulation.
    /// </summary>
    public static class TreeBaker
    {
        /// <summary>Finite stand-in for "never dies" (shaders never see infinity).</summary>
        public const float Never = 1e6f;

        /// <summary>Simulates and bakes in one call.</summary>
        public static TreeBakeData SimulateAndBake(TreeParams p, int seed, Action<float> progress = null,
                                                   System.Threading.CancellationToken cancel = default)
        {
            var h = Simulation.GrowthSimulator.Run(p, seed, f => progress?.Invoke(f * 0.85f), cancel);
            cancel.ThrowIfCancellationRequested();
            var clamped = p.Clone(); clamped.Clamp();
            var bake = Bake(h, clamped);
            progress?.Invoke(1f);
            return bake;
        }

        /// <summary>Bakes <paramref name="h"/> (which was simulated with <paramref name="p"/>).</summary>
        public static TreeBakeData Bake(TreeHistory h, TreeParams p)
        {
            var b = new TreeBakeData
            {
                years = h.years,
                keyYears = (float[])h.KeyYears.Clone(),
                radiusKeys = (float[])h.radiusKeys.Clone(),
                deathFadeYears = p.deathFadeYears,
                boundsMin = h.boundsMin,
                boundsMax = h.boundsMax,
            };

            b.segments = new SegmentGpu[h.segments.Count];
            for (int i = 0; i < h.segments.Count; i++)
            {
                var s = h.segments[i];
                var ax = h.axes[s.axis];
                b.segments[i] = new SegmentGpu
                {
                    start = s.start, end = s.end, birth = s.birth, growDuration = MathF.Max(1e-4f, s.growDuration),
                    death = Finite(s.death), axisBaseSegment = ax.firstSegment, axisPhase = ax.phase, axisOrder = ax.order,
                };
            }

            // Birth-sorted (stable on ties via the original index) for the compactor's prefix scan.
            int n = h.leaves.Count;
            var order = new int[n];
            var keys = new (float, int)[n];
            for (int i = 0; i < n; i++) { order[i] = i; keys[i] = (h.leaves[i].birth, i); }
            Array.Sort(keys, order);
            b.leaves = new LeafGpu[n];
            for (int k = 0; k < n; k++)
            {
                var l = h.leaves[order[k]];
                b.leaves[k] = new LeafGpu
                {
                    position = l.position, size = l.size,
                    rotation = new Vector4(l.rotation.X, l.rotation.Y, l.rotation.Z, l.rotation.W),
                    birth = l.birth, death = Finite(l.death), segment = l.segment, tint = l.tint, phase = l.phase,
                    flush = p.leafFlushYears,
                };
            }

            b.maxLiveLeaves = MaxLive(b.leaves, b.years);
            b.bark = BarkMeshBuilder.Build(h, p);
            b.leafMesh = LeafMeshBuilder.Build();
            return b;
        }

        // Slot count for the leaf mesh: the peak live count over a fine year sweep, plus 5%.
        static int MaxLive(LeafGpu[] leaves, int years)
        {
            if (leaves.Length == 0) return 0;
            var c = new LiveLeafCompactor(leaves);
            int max = 0;
            const int steps = 800;
            for (int i = 0; i <= steps; i++) max = Math.Max(max, c.Compact(years * (float)i / steps));
            return Math.Min(leaves.Length, (int)(max * 1.05f) + 16);
        }

        static float Finite(float v) => float.IsFinite(v) ? v : Never;
    }
}
