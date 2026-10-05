using UnityEngine;

namespace Sequence
{
    /// <summary>Pure timing rules for the intro → tree → outro sequence (EditMode-testable).</summary>
    public static class SequenceRules
    {
        /// <summary>The tree phase ends at full growth, or once the timeout elapses (timeout ≤ 0 = none).</summary>
        public static bool TreeFinished(float growth, float growthMax, float epsilon, float elapsed, float timeout)
            => growth >= growthMax - epsilon || (timeout > 0f && elapsed >= timeout);

        /// <summary>
        /// Timeline-driven tree phase: ends at the Timeout marker, or at full growth once the EEG has
        /// taken over (Interactive marker) — scripted growth before that never ends the phase.
        /// </summary>
        public static bool TreePhaseOver(bool interactive, float growth, float growthMax, float epsilon, bool timeoutHit)
            => timeoutHit || (interactive && growth >= growthMax - epsilon);

        /// <summary>
        /// Glitch envelope at normalised time t: rises 0→1 up to <paramref name="peakAt"/>
        /// (ease-in, so it builds), then decays 1→0 by t = 1 (ease-out). 0 outside [0, 1].
        /// </summary>
        public static float GlitchEnvelope(float t, float peakAt)
        {
            if (t <= 0f || t >= 1f) return 0f;
            peakAt = Mathf.Clamp(peakAt, 0.01f, 0.99f);
            if (t <= peakAt)
            {
                float u = t / peakAt;
                return u * u;
            }
            float d = 1f - (t - peakAt) / (1f - peakAt);
            return d * d * (3f - 2f * d);
        }

        /// <summary>True on the single step where the timeline crosses the peak.</summary>
        public static bool CrossedPeak(float previousT, float t, float peakAt) => previousT < peakAt && t >= peakAt;
    }
}
