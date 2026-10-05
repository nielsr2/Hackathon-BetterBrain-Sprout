using UnityEngine;

namespace Relaxation
{
    /// <summary>Relaxation → growth-rate mapping. Relaxation above the neutral point grows the tree, below it recedes (or pauses).</summary>
    public static class GrowthModel
    {
        /// <param name="relaxation">Conditioned relaxation 0..1.</param>
        /// <param name="neutral">Relaxation at which growth neither advances nor recedes.</param>
        /// <param name="secondsToFull">Seconds from min to max growth at relaxation 1. ≤0 disables growing.</param>
        /// <param name="recedeSecondsToMin">Seconds from max to min growth at relaxation 0. ≤0 means pause only.</param>
        /// <returns>Growth units per second.</returns>
        public static float Rate(float relaxation, float neutral, float min, float max, float secondsToFull, float recedeSecondsToMin)
        {
            float span = Mathf.Max(0f, max - min);
            relaxation = Mathf.Clamp01(relaxation);
            neutral = Mathf.Clamp01(neutral);
            if (relaxation >= neutral)
            {
                if (secondsToFull <= 0f || neutral >= 1f) return 0f;
                return (relaxation - neutral) / (1f - neutral) * span / secondsToFull;
            }
            if (recedeSecondsToMin <= 0f) return 0f;
            return -(neutral - relaxation) / neutral * span / recedeSecondsToMin;
        }

        public static float Step(float growth, float rate, float dt, float min, float max) =>
            Mathf.Clamp(growth + rate * dt, min, max);

        public static string Label(GrowthAlgorithm a)
        {
            switch (a)
            {
                case GrowthAlgorithm.Integrate:  return "Integrate (rate, recedes)";
                case GrowthAlgorithm.Direct:     return "Direct (growth = relaxation)";
                case GrowthAlgorithm.Ratchet:    return "Ratchet (high-water mark)";
                case GrowthAlgorithm.Accumulate: return "Accumulate (time relaxed)";
                default:                         return a.ToString();
            }
        }

        /// <summary>
        /// Next target growth under <paramref name="algorithm"/>. Call only while the baseline is
        /// ready and a signal is present; otherwise hold the current target.
        /// </summary>
        public static float NextTarget(GrowthAlgorithm algorithm, float growth, float relaxation, float neutral,
            float min, float max, float secondsToFull, float recedeSecondsToMin, float dt)
        {
            relaxation = Mathf.Clamp01(relaxation);
            float mapped = Mathf.Lerp(min, max, relaxation);
            switch (algorithm)
            {
                case GrowthAlgorithm.Direct:
                    return Mathf.Clamp(mapped, min, max);
                case GrowthAlgorithm.Ratchet:
                    return Mathf.Clamp(Mathf.Max(growth, mapped), min, max);
                case GrowthAlgorithm.Accumulate:
                    // Constant speed while at/above neutral, regardless of how far above; never recedes.
                    float speed = secondsToFull > 0f ? Mathf.Max(0f, max - min) / secondsToFull : 0f;
                    return Step(growth, relaxation >= neutral ? speed : 0f, dt, min, max);
                default:
                    return Step(growth, Rate(relaxation, neutral, min, max, secondsToFull, recedeSecondsToMin), dt, min, max);
            }
        }
    }

    /// <summary>How relaxation turns into tree growth. Cycled with G.</summary>
    public enum GrowthAlgorithm
    {
        Integrate,  // relaxation above neutral → growth rate; below recedes (or pauses)
        Direct,     // growth tracks relaxation 1:1 (most responsive, can shrink fast)
        Ratchet,    // growth = best relaxation so far; never shrinks
        Accumulate, // constant growth while relaxed; total = time spent relaxed
    }
}
