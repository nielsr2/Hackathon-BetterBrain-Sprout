using System;
using System.Collections.Generic;
using System.Numerics;

namespace Nib.ProcTree.Core
{
    /// <summary>One internode of wood. Positions are final (tree-local); wood never moves once formed.</summary>
    public struct Segment
    {
        /// <summary>Base point (m) — equals the parent segment's end.</summary>
        public Vector3 start;
        /// <summary>Tip point (m) once fully extended.</summary>
        public Vector3 end;
        /// <summary>Owning axis index.</summary>
        public int axis;
        /// <summary>Parent segment (previous on this axis, or the segment this axis branches from); -1 for the trunk base.</summary>
        public int parent;
        /// <summary>Continuation on the same axis, or -1.</summary>
        public int mainChild;
        /// <summary>Year at which extension starts.</summary>
        public float birth;
        /// <summary>Years taken to extend from start to end.</summary>
        public float growDuration;
        /// <summary>Year the wood died (pruned), or <see cref="float.PositiveInfinity"/>.</summary>
        public float death;
        /// <summary>Year at which the segment is fully extended.</summary>
        public float GrownAt => birth + growDuration;
    }

    /// <summary>A branch axis: a chain of segments from its base to its tip.</summary>
    public struct Axis
    {
        /// <summary>First segment of the axis (its parent is on the parent axis).</summary>
        public int firstSegment;
        /// <summary>Segment the axis branches from (-1 for the trunk).</summary>
        public int parentSegment;
        /// <summary>Branching order: 0 trunk, 1 limb, 2 branch, ...</summary>
        public int order;
        /// <summary>Year the axis died, or <see cref="float.PositiveInfinity"/>.</summary>
        public float death;
        /// <summary>Random phase in [0,1) for wind.</summary>
        public float phase;
    }

    /// <summary>One leaf: +Z of <see cref="rotation"/> is the blade direction, +Y its face normal.</summary>
    public struct Leaf
    {
        /// <summary>Petiole attachment point (m).</summary>
        public Vector3 position;
        /// <summary>Blade orientation.</summary>
        public Quaternion rotation;
        /// <summary>Blade length (m).</summary>
        public float size;
        /// <summary>Year the leaf starts unfolding.</summary>
        public float birth;
        /// <summary>Year the leaf drops.</summary>
        public float death;
        /// <summary>Segment the leaf hangs on.</summary>
        public int segment;
        /// <summary>Colour jitter in [-1,1].</summary>
        public float tint;
        /// <summary>Wind phase in [0,1).</summary>
        public float phase;
    }

    /// <summary>The whole simulated life of a tree: every segment, axis and leaf with birth/death times.</summary>
    public sealed class TreeHistory
    {
        /// <summary>Number of radius keys per segment.</summary>
        public const int KeyCount = 16;

        /// <summary>Simulated years (growth 1 = this year).</summary>
        public int years;
        /// <summary>All segments in creation order (parents always precede children).</summary>
        public readonly List<Segment> segments = new List<Segment>();
        /// <summary>All axes.</summary>
        public readonly List<Axis> axes = new List<Axis>();
        /// <summary>All leaves that ever lived.</summary>
        public readonly List<Leaf> leaves = new List<Leaf>();
        /// <summary>Radius (m) per segment per key: <c>radiusKeys[s * KeyCount + k]</c>, at <see cref="KeyYears"/>.</summary>
        public float[] radiusKeys = Array.Empty<float>();
        /// <summary>Shared key year grid, quadratic (dense early): years · (k/15)².</summary>
        public float[] KeyYears = Array.Empty<float>();
        /// <summary>Mature bounds min (m), including leaves and radii.</summary>
        public Vector3 boundsMin;
        /// <summary>Mature bounds max (m), including leaves and radii.</summary>
        public Vector3 boundsMax;

        /// <summary>Builds the shared key-year grid for <paramref name="years"/>.</summary>
        public static float[] BuildKeyYears(int years)
        {
            var k = new float[KeyCount];
            for (int i = 0; i < KeyCount; i++)
            {
                float t = (float)i / (KeyCount - 1);
                k[i] = years * t * t;
            }
            return k;
        }

        /// <summary>Radius of segment <paramref name="s"/> at <paramref name="year"/> (linear between keys).</summary>
        public float RadiusAt(int s, float year) => EvalKeys(radiusKeys, s * KeyCount, KeyYears, year);

        /// <summary>Piecewise-linear evaluation of 16 keys on the shared grid (mirrored in HLSL).</summary>
        public static float EvalKeys(float[] keys, int offset, float[] keyYears, float year)
        {
            if (year <= keyYears[0]) return keys[offset];
            for (int k = 1; k < KeyCount; k++)
            {
                if (year <= keyYears[k])
                {
                    float t = (year - keyYears[k - 1]) / MathF.Max(1e-6f, keyYears[k] - keyYears[k - 1]);
                    return keys[offset + k - 1] + (keys[offset + k] - keys[offset + k - 1]) * t;
                }
            }
            return keys[offset + KeyCount - 1];
        }

        /// <summary>Stable 64-bit FNV-1a over all segments, axes, leaves and radii (determinism tests, bake staleness).</summary>
        public ulong ComputeHash()
        {
            ulong h = 14695981039346656037UL;
            void F(float f) { uint v = (uint)BitConverter.SingleToInt32Bits(f); for (int b = 0; b < 4; b++) { h ^= (v >> (b * 8)) & 0xFF; h *= 1099511628211UL; } }
            void I(int i) => F(BitConverter.Int32BitsToSingle(i));
            void V(Vector3 v) { F(v.X); F(v.Y); F(v.Z); }
            I(years);
            foreach (var s in segments) { V(s.start); V(s.end); I(s.axis); I(s.parent); I(s.mainChild); F(s.birth); F(s.growDuration); F(s.death); }
            foreach (var a in axes) { I(a.firstSegment); I(a.parentSegment); I(a.order); F(a.death); F(a.phase); }
            foreach (var l in leaves) { V(l.position); F(l.rotation.X); F(l.rotation.Y); F(l.rotation.Z); F(l.rotation.W); F(l.size); F(l.birth); F(l.death); I(l.segment); F(l.tint); F(l.phase); }
            foreach (var r in radiusKeys) F(r);
            return h;
        }
    }
}
