using System;

namespace Relaxation
{
    /// <summary>
    /// Per-metric resting baseline (Welford running mean/std) collected over a fixed duration,
    /// then used to z-score live values. Absolute EEG power varies hugely with impedance and
    /// session, so every metric is judged relative to the user's own baseline.
    /// </summary>
    public sealed class RelaxationBaseline
    {
        readonly int _count;
        readonly double[] _mean, _m2;
        int _n;
        float _elapsed;

        public float DurationSeconds;
        public float MinStd = 1e-6f;

        public RelaxationBaseline(int count, float durationSeconds)
        {
            _count = count;
            _mean = new double[count];
            _m2 = new double[count];
            DurationSeconds = durationSeconds;
        }

        public bool IsReady { get; private set; }
        public int SampleCount => _n;
        public float Progress => IsReady || DurationSeconds <= 0f ? 1f : Math.Min(1f, _elapsed / DurationSeconds);

        public void Reset()
        {
            Array.Clear(_mean, 0, _count);
            Array.Clear(_m2, 0, _count);
            _n = 0;
            _elapsed = 0f;
            IsReady = false;
        }

        /// <summary>Advances the collection clock; call only while a signal is present.</summary>
        public void Tick(float dt)
        {
            if (IsReady) return;
            _elapsed += dt;
            if (_elapsed >= DurationSeconds && _n >= 2) IsReady = true;
        }

        public void Add(float[] values)
        {
            if (IsReady) return;
            _n++;
            for (int i = 0; i < _count; i++)
            {
                double x = values[i];
                double d = x - _mean[i];
                _mean[i] += d / _n;
                _m2[i] += d * (x - _mean[i]);
            }
        }

        public float Mean(int i) => (float)_mean[i];

        /// <summary>Sample standard deviation.</summary>
        public float Std(int i) => _n < 2 ? 0f : (float)Math.Sqrt(_m2[i] / (_n - 1));

        public float ZScore(int i, float x) => (x - Mean(i)) / Math.Max(Std(i), MinStd);
    }
}
