using System;
using System.Numerics;

namespace Nib.ProcTree.Core
{
    /// <summary>
    /// Deterministic RNG (xorshift32), copied from ProcFoliage. Byte-stable across platforms — the
    /// whole simulation draws only from this, so the same seed always grows the same tree.
    /// Not for security.
    /// </summary>
    public sealed class SeededRng
    {
        uint _state;

        /// <summary>Creates a stream from <paramref name="seed"/>; adjacent seeds diverge fast.</summary>
        public SeededRng(int seed)
        {
            uint s = (uint)seed;
            s ^= 2747636419u;
            s *= 2654435769u;
            s ^= s >> 16;
            s *= 2654435769u;
            s ^= s >> 16;
            _state = s == 0u ? 0x9E3779B9u : s;
        }

        uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform in [0,1). 24-bit mantissa for a clean float.</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1.0f / 16777216.0f);

        /// <summary>Uniform in [min,max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int RangeInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint span = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % span);
        }

        /// <summary>True with probability <paramref name="p"/>.</summary>
        public bool Chance(float p) => NextFloat() < p;

        /// <summary>Uniform in [-amount, amount).</summary>
        public float Symmetric(float amount) => Range(-amount, amount);

        /// <summary>Uniform point inside the unit ball (rejection sampling).</summary>
        public Vector3 InsideUnitSphere()
        {
            while (true)
            {
                var v = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
                if (v.LengthSquared() <= 1f) return v;
            }
        }

        /// <summary>Uniform direction on the unit sphere.</summary>
        public Vector3 UnitVector()
        {
            while (true)
            {
                var v = InsideUnitSphere();
                float l2 = v.LengthSquared();
                if (l2 > 1e-4f) return v / MathF.Sqrt(l2);
            }
        }

        /// <summary>Deterministic child stream (state + salt), for stable sub-streams.</summary>
        public SeededRng Fork(int salt) => new SeededRng((int)(_state ^ (uint)(salt * 0x9E3779B1)));
    }
}
