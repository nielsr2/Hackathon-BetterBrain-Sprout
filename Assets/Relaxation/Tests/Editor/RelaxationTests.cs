using System.Globalization;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Relaxation.Tests
{
    public class RelaxationTests
    {
        static BandPowers Sample() => new BandPowers
        {
            delta = 8f, theta = 4f, alpha = 6f, betaLow = 1f, betaMid = 0.5f, betaHigh = 0.5f, gamma = 1f, fzTheta = 3f,
        };

        [Test]
        public void Metrics_MatchFormulas()
        {
            var b = Sample(); // β = 2, total = 21
            Assert.AreEqual(3f, RelaxationMetrics.Compute(RelaxationMetric.AlphaBeta, b), 1e-5f);
            Assert.AreEqual(5f, RelaxationMetrics.Compute(RelaxationMetric.AlphaThetaOverBeta, b), 1e-5f);
            Assert.AreEqual(6f / 21f, RelaxationMetrics.Compute(RelaxationMetric.RelativeAlpha, b), 1e-5f);
            Assert.AreEqual(1f, RelaxationMetrics.Compute(RelaxationMetric.AlphaOverThetaBeta, b), 1e-5f);
            Assert.AreEqual(4f, RelaxationMetrics.Compute(RelaxationMetric.WeightedRelaxation, b), 1e-5f);
            Assert.AreEqual(3f, RelaxationMetrics.Compute(RelaxationMetric.FrontalTheta, b), 1e-5f);
            Assert.AreEqual(4f / 6f, RelaxationMetrics.Compute(RelaxationMetric.ThetaAlpha, b), 1e-5f);
            Assert.AreEqual(2f, RelaxationMetrics.Compute(RelaxationMetric.ThetaBeta, b), 1e-5f);
        }

        [Test]
        public void Metrics_ZeroBeta_StaysFinite()
        {
            var b = Sample();
            b.betaLow = b.betaMid = b.betaHigh = 0f;
            var all = new float[RelaxationMetrics.Count];
            RelaxationMetrics.ComputeAll(b, all);
            Assert.That(all.All(v => !float.IsNaN(v) && !float.IsInfinity(v)));
        }

        [Test]
        public void Baseline_WelfordMatchesHandComputed()
        {
            var bl = new RelaxationBaseline(1, 1f);
            foreach (var x in new[] { 2f, 4f, 4f, 4f, 5f, 5f, 7f, 9f }) bl.Add(new[] { x });
            Assert.AreEqual(5f, bl.Mean(0), 1e-5f);
            Assert.AreEqual(2.13809f, bl.Std(0), 1e-4f); // sample std
            Assert.AreEqual(1f / 2.13809f, bl.ZScore(0, 6f), 1e-4f);
        }

        [Test]
        public void Baseline_ReadyOnlyAfterDurationAndSamples()
        {
            var bl = new RelaxationBaseline(1, 1f);
            bl.Tick(2f);
            Assert.IsFalse(bl.IsReady, "no samples yet");
            bl.Add(new[] { 1f });
            bl.Add(new[] { 2f });
            bl.Tick(0.01f);
            Assert.IsTrue(bl.IsReady);
            bl.Add(new[] { 100f });
            Assert.AreEqual(1.5f, bl.Mean(0), 1e-5f, "samples after ready are ignored");
        }

        [Test]
        public void Growth_FullRelaxationReachesMaxInConfiguredTime()
        {
            float g = 0.1f;
            for (int i = 0; i < 1000; i++)
            {
                float r = GrowthModel.Rate(1f, 0.25f, 0.1f, 0.9f, 10f, 0f);
                g = GrowthModel.Step(g, r, 0.01f, 0.1f, 0.9f);
            }
            Assert.AreEqual(0.9f, g, 1e-4f);
        }

        [Test]
        public void Growth_NoRecedeOnlyPauses_AndStaysClamped()
        {
            Assert.AreEqual(0f, GrowthModel.Rate(0f, 0.25f, 0f, 1f, 10f, 0f));
            Assert.Less(GrowthModel.Rate(0f, 0.25f, 0f, 1f, 10f, 5f), 0f);
            Assert.AreEqual(0.2f, GrowthModel.Step(0.2f, -10f, 1f, 0.2f, 0.8f));
            Assert.AreEqual(0.8f, GrowthModel.Step(0.7f, 10f, 1f, 0.2f, 0.8f));
        }

        [Test]
        public void Parse_IsCultureInvariant()
        {
            var values = Enumerable.Range(0, 63).Select(i => (i + 0.5f).ToString(CultureInfo.InvariantCulture));
            string packet = string.Join(",", values);
            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("da-DK");
                Assert.IsTrue(UnicornBandReceiver.TryParse(packet, 56, 8, false, out var b));
                Assert.AreEqual(56.5f, b.delta, 1e-4f);
                Assert.AreEqual(57.5f, b.theta, 1e-4f);
                Assert.AreEqual(62.5f, b.gamma, 1e-4f);
                Assert.AreEqual(8.5f, b.fzTheta, 1e-4f);
            }
            finally { Thread.CurrentThread.CurrentCulture = prev; }
        }

        [Test]
        public void Parse_RejectsShortOrMalformed()
        {
            Assert.IsFalse(UnicornBandReceiver.TryParse("1,2,3", 56, 8, false, out _));
            Assert.IsFalse(UnicornBandReceiver.TryParse("", 56, 8, false, out _));
            string bad = string.Join(",", Enumerable.Repeat("x", 63));
            Assert.IsFalse(UnicornBandReceiver.TryParse(bad, 56, 8, false, out _));
        }

        [Test]
        public void Parse_DecibelsConvertToLinear()
        {
            string packet = string.Join(",", Enumerable.Repeat("10", 63));
            Assert.IsTrue(UnicornBandReceiver.TryParse(packet, 56, 8, true, out var b));
            Assert.AreEqual(10f, b.alpha, 1e-4f);
        }

        [Test]
        public void ParseChannels_BandMajorAndChannelMajor()
        {
            string packet = string.Join(",", Enumerable.Range(0, 63).Select(i => i.ToString(CultureInfo.InvariantCulture)));
            var into = new float[56];
            Assert.IsTrue(UnicornBandReceiver.TryParseChannels(packet, false, false, into));
            Assert.AreEqual(8f, into[0 * 7 + 1], "band-major: theta ch1 at index 8");
            Assert.AreEqual(2f, into[2 * 7 + 0], "band-major: delta ch3 at index 2");
            Assert.IsTrue(UnicornBandReceiver.TryParseChannels(packet, true, false, into));
            Assert.AreEqual(1f, into[0 * 7 + 1]);
            Assert.AreEqual(14f, into[2 * 7 + 0]);
            Assert.IsFalse(UnicornBandReceiver.TryParseChannels("1,2,3", false, false, into));
        }

        static float[] Channels(System.Func<int, int, float> f)
        {
            var a = new float[56];
            for (int ch = 0; ch < 8; ch++) for (int b = 0; b < 7; b++) a[ch * 7 + b] = f(ch, b);
            return a;
        }

        [Test]
        public void Quality_DetectsFlatNoisyAndMuscle()
        {
            float[] typical = { 8f, 5f, 6f, 3f, 2.5f, 2f, 1f }; // gamma 1/27.5, delta 8/27.5
            var bands = Channels((ch, b) =>
                ch == 1 ? 0f :                          // C3 flat
                ch == 2 ? typical[b] * 10f :            // Cz 10x the rest
                ch == 3 && b == 6 ? 20f :               // C4 gamma-dominated
                typical[b]);
            var q = new SignalQuality();
            var scores = new float[8];
            var totals = new float[8];
            q.Assess(bands, scores, totals);
            Assert.AreEqual((float)ChannelQuality.Good, scores[0]);
            Assert.AreEqual((float)ChannelQuality.Flat, scores[1]);
            Assert.AreEqual((float)ChannelQuality.Bad, scores[2]);
            Assert.AreEqual((float)ChannelQuality.Bad, scores[3]);
            Assert.AreEqual((float)ChannelQuality.Good, scores[4]);
        }

        [Test]
        public void Quality_SmoothsAndReportsWorst()
        {
            float[] typical = { 8f, 5f, 6f, 3f, 2.5f, 2f, 1f };
            var q = new SignalQuality { smoothingSeconds = 1f };
            q.Update(Channels((ch, b) => typical[b]), 0f);
            Assert.AreEqual(ChannelQuality.Good, q.Overall);
            q.Update(Channels((ch, b) => ch == 5 ? 0f : typical[b]), 0.1f); // one short dropout
            Assert.AreEqual(ChannelQuality.Good, q.Quality(5), "one packet must not flip the electrode");
            for (int i = 0; i < 50; i++) q.Update(Channels((ch, b) => ch == 5 ? 0f : typical[b]), 0.1f);
            Assert.AreEqual(ChannelQuality.Flat, q.Overall);
            Assert.AreEqual(1, q.CountAtLeast(ChannelQuality.Bad));
        }

        [Test]
        public void Algorithms_BehaveAsDocumented()
        {
            const float min = 0f, max = 1f;
            Assert.AreEqual(0.7f, GrowthModel.NextTarget(GrowthAlgorithm.Direct, 0.2f, 0.7f, 0.25f, min, max, 10f, 0f, 0.1f), 1e-5f);
            Assert.AreEqual(0.2f, GrowthModel.NextTarget(GrowthAlgorithm.Direct, 0.9f, 0.2f, 0.25f, min, max, 10f, 0f, 0.1f), 1e-5f);
            Assert.AreEqual(0.9f, GrowthModel.NextTarget(GrowthAlgorithm.Ratchet, 0.9f, 0.2f, 0.25f, min, max, 10f, 0f, 0.1f), 1e-5f, "never shrinks");
            Assert.AreEqual(0.95f, GrowthModel.NextTarget(GrowthAlgorithm.Ratchet, 0.9f, 0.95f, 0.25f, min, max, 10f, 0f, 0.1f), 1e-5f);
            Assert.AreEqual(0.51f, GrowthModel.NextTarget(GrowthAlgorithm.Accumulate, 0.5f, 0.3f, 0.25f, min, max, 10f, 0f, 0.1f), 1e-5f, "speed independent of how relaxed");
            Assert.AreEqual(0.51f, GrowthModel.NextTarget(GrowthAlgorithm.Accumulate, 0.5f, 1f, 0.25f, min, max, 10f, 0f, 0.1f), 1e-5f);
            Assert.AreEqual(0.5f, GrowthModel.NextTarget(GrowthAlgorithm.Accumulate, 0.5f, 0.1f, 0.25f, min, max, 10f, 5f, 0.1f), 1e-5f, "never recedes");
            float r = GrowthModel.Rate(1f, 0.25f, min, max, 10f, 0f);
            Assert.AreEqual(GrowthModel.Step(0.5f, r, 0.1f, min, max),
                GrowthModel.NextTarget(GrowthAlgorithm.Integrate, 0.5f, 1f, 0.25f, min, max, 10f, 0f, 0.1f), 1e-6f);
        }

        static byte[] RawPacket(params float[] values)
        {
            var bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++) System.BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 4);
            return bytes;
        }

        [Test]
        public void ParseRaw_ReadsUnicornUdpSample()
        {
            // Captured from the Unicorn UDP app: 8 EEG µV, accel, gyro, battery, counter, validation.
            var packet = RawPacket(683735.31f, 683486.69f, 710949.06f, 293308.47f, 685668.69f, 679294.94f, 292796.06f, 680607.62f,
                -0.10f, 0.85f, -0.14f, 24.69f, -2.59f, -11.84f, 60f, 53814f, 1f);
            Assert.AreEqual(68, packet.Length);
            Assert.IsFalse(UnicornBandReceiver.LooksLikeText(packet, packet.Length), "binary must not be mistaken for CSV");
            var v = new float[17];
            Assert.IsTrue(UnicornBandReceiver.TryParseRaw(packet, packet.Length, v));
            Assert.AreEqual(710949.06f, v[2], 0.1f);
            Assert.AreEqual(60f, v[UnicornBandReceiver.RawBattery]);
            Assert.AreEqual(53814f, v[UnicornBandReceiver.RawCounter]);
            Assert.IsFalse(UnicornBandReceiver.TryParseRaw(packet, 40, v), "short packet");
            var csv = System.Text.Encoding.ASCII.GetBytes("1.5,2.5,-3\r\n");
            Assert.IsTrue(UnicornBandReceiver.LooksLikeText(csv, csv.Length));
        }

        /// <summary>Feeds <paramref name="seconds"/> of a DC-offset sine at <paramref name="hz"/> on every channel.</summary>
        static RawEegProcessor Feed(float hz, float amplitude, float seconds, float dc = 680000f, float drift = 0f)
        {
            var proc = new RawEegProcessor();
            var sample = new float[17];
            int n = (int)(seconds * RawEegProcessor.SampleRate);
            for (int i = 0; i < n; i++)
            {
                float t = i / RawEegProcessor.SampleRate;
                for (int c = 0; c < 8; c++) sample[c] = dc + drift * t + amplitude * UnityEngine.Mathf.Sin(2f * UnityEngine.Mathf.PI * hz * t);
                proc.Push(sample);
            }
            return proc;
        }

        [Test]
        public void RawProcessor_PutsSinePowerInTheRightBand()
        {
            var alpha = Feed(10f, 20f, 3f, drift: 500f); // large offset and drift must be removed
            Assert.IsTrue(alpha.Ready);
            var b = alpha.Average;
            Assert.Greater(b.alpha, 20f * (b.delta + b.theta + b.Beta + b.gamma), "10 Hz lands in alpha");
            Assert.AreEqual(20f / UnityEngine.Mathf.Sqrt(2f), alpha.Rms[0], 1f, "RMS of the detrended sine");
            Assert.AreEqual(680000f, alpha.Dc[0], 1000f);

            var beta = Feed(22f, 20f, 3f).Average;
            Assert.Greater(beta.betaHigh, 20f * (beta.alpha + beta.theta), "22 Hz lands in beta-high");
        }

        [Test]
        public void RawProcessor_DetectsMainsHum()
        {
            var hum = Feed(50f, 500f, 3f);
            Assert.Greater(hum.LineNoise[0], 0.9f);
            var q = new SignalQuality();
            var scores = new float[8];
            q.Assess(hum.ChannelBands, scores, new float[8], hum);
            Assert.AreEqual((float)ChannelQuality.Bad, scores[0], "mains-dominated, high-RMS channel is bad");

            var clean = Feed(10f, 15f, 3f);
            q.Assess(clean.ChannelBands, scores, new float[8], clean);
            Assert.AreEqual((float)ChannelQuality.Good, scores[0]);

            var railed = Feed(10f, 0f, 3f, dc: 750000f);
            q.Assess(railed.ChannelBands, scores, new float[8], railed);
            Assert.AreEqual((float)ChannelQuality.Flat, scores[0], "railed/constant is flat");
        }

        [Test]
        public void Fft_SingleBinSine()
        {
            var re = new double[64];
            var im = new double[64];
            for (int i = 0; i < 64; i++) re[i] = System.Math.Cos(2 * System.Math.PI * 4 * i / 64);
            Fft.Transform(re, im);
            Assert.AreEqual(32.0, re[4], 1e-9);
            Assert.AreEqual(0.0, re[5], 1e-9);
        }
    }
}
