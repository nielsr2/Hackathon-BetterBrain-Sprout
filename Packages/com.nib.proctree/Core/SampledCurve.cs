using System;

namespace Nib.ProcTree.Core
{
    /// <summary>
    /// A curve over [0,1] stored as evenly spaced samples. The thread-safe stand-in for
    /// <c>AnimationCurve</c>: the main thread snapshots profile curves into these before the
    /// simulation runs on a worker thread.
    /// </summary>
    [Serializable]
    public sealed class SampledCurve
    {
        /// <summary>Evenly spaced values at t = i / (Length-1).</summary>
        public float[] samples;

        /// <summary>Wraps existing samples (at least one).</summary>
        public SampledCurve(float[] samples)
        {
            this.samples = (samples == null || samples.Length == 0) ? new[] { 0f } : samples;
        }

        /// <summary>Linear interpolation, clamped to [0,1].</summary>
        public float Evaluate(float t)
        {
            int n = samples.Length;
            if (n == 1 || float.IsNaN(t)) return samples[0];
            if (t <= 0f) return samples[0];
            if (t >= 1f) return samples[n - 1];
            float x = t * (n - 1);
            int i = (int)x;
            float f = x - i;
            return samples[i] + (samples[i + 1] - samples[i]) * f;
        }

        /// <summary>Samples <paramref name="f"/> at <paramref name="count"/> points.</summary>
        public static SampledCurve FromFunc(Func<float, float> f, int count = 64)
        {
            count = Math.Max(2, count);
            var s = new float[count];
            for (int i = 0; i < count; i++) s[i] = f((float)i / (count - 1));
            return new SampledCurve(s);
        }

        /// <summary>A flat curve.</summary>
        public static SampledCurve Constant(float v) => new SampledCurve(new[] { v, v });

        /// <summary>Deep copy.</summary>
        public SampledCurve Clone() => new SampledCurve((float[])samples.Clone());
    }
}
