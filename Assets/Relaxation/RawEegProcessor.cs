using System;

namespace Relaxation
{
    /// <summary>
    /// Turns raw Unicorn samples (8 EEG channels, µV, 250 Hz) into band powers: every
    /// <see cref="HopSamples"/> it takes the last <see cref="WindowSamples"/> per channel, removes the
    /// linear trend (the Unicorn has large DC offsets), applies a Hann window and an FFT, and sums
    /// bin power into the seven Unicorn bands. Also keeps a filtered copy of each channel for display
    /// and per-channel RMS / DC offset / 50 Hz share for signal quality.
    /// </summary>
    public sealed class RawEegProcessor
    {
        public const int Channels = UnicornBandReceiver.Channels;
        public const float SampleRate = 250f;
        public const int WindowSamples = 500; // 2 s
        public const int FftSize = 512;
        public const int HopSamples = 25;     // 10 estimates per second
        public const int DisplaySamples = 1000; // 4 s of filtered signal

        /// <summary>Band edges in Hz (lower inclusive, upper exclusive): δ θ α βlow βmid βhigh γ. γ stops below 50 Hz mains.</summary>
        public static readonly float[] BandEdges = { 1f, 4f, 8f, 12f, 16f, 20f, 30f, 45f };

        readonly float[,] _raw = new float[Channels, WindowSamples];
        readonly float[,] _display = new float[Channels, DisplaySamples];
        readonly Biquad[] _hp = new Biquad[Channels], _notch = new Biquad[Channels];
        int _head, _filled, _sinceHop, _displayHead, _displayFilled;
        readonly double[] _re = new double[FftSize], _im = new double[FftSize];
        static readonly double[] Hann = BuildHann();

        /// <summary>Channel-major band powers [ch * 7 + band], µV².</summary>
        public readonly float[] ChannelBands = new float[Channels * UnicornBandReceiver.Bands];
        /// <summary>RMS of the detrended window (µV), mean (DC offset, µV) and 48–52 Hz share of 1–60 Hz power.</summary>
        public readonly float[] Rms = new float[Channels], Dc = new float[Channels], LineNoise = new float[Channels];
        public BandPowers Average;

        public RawEegProcessor(bool notch50 = true)
        {
            for (int c = 0; c < Channels; c++)
            {
                _hp[c] = Biquad.HighPass(SampleRate, 1f);
                _notch[c] = notch50 ? Biquad.Notch(SampleRate, 50f, 10f) : Biquad.Identity;
            }
        }

        public bool Ready => _filled >= WindowSamples;

        /// <summary>Filtered sample (1 Hz high-pass + 50 Hz notch) <paramref name="age"/> samples ago, for display.</summary>
        public float Display(int ch, int age) => _display[ch, (_displayHead - 1 - age + DisplaySamples * 2) % DisplaySamples];
        public int DisplayCount => _displayFilled;

        /// <summary>Adds one sample (first 8 values used). Returns true when a new band estimate is ready.</summary>
        public bool Push(float[] eeg)
        {
            for (int c = 0; c < Channels; c++)
            {
                if (_displayFilled == 0) _hp[c].Prime(eeg[c]);
                _raw[c, _head] = eeg[c];
                _display[c, _displayHead] = (float)_notch[c].Process(_hp[c].Process(eeg[c]));
            }
            _head = (_head + 1) % WindowSamples;
            _displayHead = (_displayHead + 1) % DisplaySamples;
            _filled = Math.Min(_filled + 1, WindowSamples);
            _displayFilled = Math.Min(_displayFilled + 1, DisplaySamples);

            if (++_sinceHop < HopSamples || !Ready) return false;
            _sinceHop = 0;
            Estimate();
            return true;
        }

        void Estimate()
        {
            const int B = UnicornBandReceiver.Bands;
            double binHz = SampleRate / FftSize;
            var avg = new double[B];

            for (int c = 0; c < Channels; c++)
            {
                // Least-squares linear detrend: x(t) ≈ a + b·t over t = 0..N-1 (oldest first).
                double sx = 0, stx = 0;
                int n = WindowSamples;
                for (int i = 0; i < n; i++)
                {
                    double x = _raw[c, (_head + i) % n];
                    sx += x; stx += i * x;
                }
                double tMean = (n - 1) * 0.5, mean = sx / n;
                double stt = n * (n * (double)n - 1) / 12.0; // Σ(t - t̄)²
                double slope = (stx - tMean * sx) / stt;

                double ss = 0;
                for (int i = 0; i < FftSize; i++)
                {
                    if (i < n)
                    {
                        double d = _raw[c, (_head + i) % n] - (mean + slope * (i - tMean));
                        ss += d * d;
                        _re[i] = d * Hann[i];
                    }
                    else _re[i] = 0;
                    _im[i] = 0;
                }
                Rms[c] = (float)Math.Sqrt(ss / n);
                Dc[c] = (float)mean;

                Fft.Transform(_re, _im);

                double line = 0, total = 0;
                for (int b = 0; b < B; b++) ChannelBands[c * B + b] = 0f;
                for (int k = 1; k < FftSize / 2; k++)
                {
                    double f = k * binHz;
                    double p = (_re[k] * _re[k] + _im[k] * _im[k]) * 2.0 / (FftSize * (double)n);
                    if (f >= 1 && f < 60) total += p;
                    if (f >= 48 && f <= 52) line += p;
                    int band = BandOf((float)f);
                    if (band >= 0) ChannelBands[c * B + band] += (float)p;
                }
                LineNoise[c] = total > 0 ? (float)(line / total) : 0f;
                for (int b = 0; b < B; b++) avg[b] += ChannelBands[c * B + b] / Channels;
            }

            Average = new BandPowers
            {
                delta = (float)avg[0], theta = (float)avg[1], alpha = (float)avg[2],
                betaLow = (float)avg[3], betaMid = (float)avg[4], betaHigh = (float)avg[5], gamma = (float)avg[6],
                fzTheta = ChannelBands[0 * B + 1], // channel 1 is Fz
            };
        }

        public static int BandOf(float hz)
        {
            for (int b = 0; b < BandEdges.Length - 1; b++)
                if (hz >= BandEdges[b] && hz < BandEdges[b + 1]) return b;
            return -1;
        }

        static double[] BuildHann()
        {
            var w = new double[WindowSamples];
            for (int i = 0; i < w.Length; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (w.Length - 1));
            return w;
        }
    }

    /// <summary>In-place iterative radix-2 complex FFT.</summary>
    public static class Fft
    {
        public static void Transform(double[] re, double[] im)
        {
            int n = re.Length;
            if ((n & (n - 1)) != 0) throw new ArgumentException("FFT length must be a power of two.");
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
                    }
                }
            }
        }
    }

    /// <summary>Direct-form-I biquad (RBJ cookbook coefficients).</summary>
    public struct Biquad
    {
        double _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

        public static Biquad Identity => new Biquad { _b0 = 1 };

        public static Biquad HighPass(float fs, float fc, float q = 0.7071f)
        {
            double w = 2 * Math.PI * fc / fs, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            return new Biquad
            {
                _b0 = (1 + cos) / 2 / a0, _b1 = -(1 + cos) / a0, _b2 = (1 + cos) / 2 / a0,
                _a1 = -2 * cos / a0, _a2 = (1 - alpha) / a0,
            };
        }

        public static Biquad Notch(float fs, float fc, float q)
        {
            double w = 2 * Math.PI * fc / fs, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            return new Biquad
            {
                _b0 = 1 / a0, _b1 = -2 * cos / a0, _b2 = 1 / a0,
                _a1 = -2 * cos / a0, _a2 = (1 - alpha) / a0,
            };
        }

        /// <summary>Settle on a constant input <paramref name="x"/> (output 0 for a high-pass), avoiding a start-up step.</summary>
        public void Prime(double x)
        {
            _x1 = _x2 = x;
            double dcGain = (_b0 + _b1 + _b2) / (1 + _a1 + _a2);
            _y1 = _y2 = dcGain * x;
        }

        public double Process(double x)
        {
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
            return y;
        }
    }
}
