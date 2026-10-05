using System;
using UnityEngine;

namespace Relaxation
{
    public enum ChannelQuality { Good, Fair, Bad, Flat }

    /// <summary>
    /// Per-electrode signal-quality heuristic from band powers alone (the Unicorn UDP stream carries
    /// no raw EEG or impedance). Each channel is judged against the median channel, since absolute
    /// power depends on device scaling:
    /// <list type="bullet">
    /// <item>Flat — near-zero power: electrode off / not connected.</item>
    /// <item>Bad — power far above the other channels, or dominated by γ (muscle/EMG) or δ (movement, blinks).</item>
    /// <item>Fair — moderately out of line on either count.</item>
    /// </list>
    /// Scores are smoothed so a single blink does not flash the overlay red.
    /// </summary>
    [Serializable]
    public sealed class SignalQuality
    {
        [Tooltip("Total power below this (device units) counts as flat/disconnected.")]
        public float flatPower = 1e-3f;
        [Tooltip("Channel/median total-power ratio above which a channel is Fair (or below 1/x).")]
        public float fairRatio = 2.5f;
        [Tooltip("Channel/median total-power ratio above which a channel is Bad (or below 1/x).")]
        public float badRatio = 6f;
        [Tooltip("γ share of total power above which a channel is Fair / Bad (muscle artefact).")]
        public float gammaFair = 0.2f, gammaBad = 0.35f;
        [Tooltip("δ share of total power above which a channel is Fair / Bad (movement, eye blinks).")]
        public float deltaFair = 0.6f, deltaBad = 0.8f;
        [Header("Raw-signal checks (Unicorn UDP raw stream only)")]
        [Tooltip("Detrended RMS (µV) below this: flat / no contact.")]
        public float rawFlatRms = 0.5f;
        [Tooltip("Detrended RMS (µV) above which a channel is Fair / Bad (movement, poor contact).")]
        public float rawFairRms = 50f, rawBadRms = 100f;
        [Tooltip("|DC offset| (µV) at which the amplifier is saturated (Unicorn input range ±750 mV).")]
        public float railMicrovolts = 740000f;
        [Tooltip("48–52 Hz share of 1–60 Hz power above which a channel is Fair / Bad (mains pickup: high impedance).")]
        public float lineFair = 0.3f, lineBad = 0.6f;

        [Tooltip("Smoothing time constant on the per-channel score (s).")]
        [Min(0f)] public float smoothingSeconds = 1f;

        readonly float[] _score = new float[UnicornBandReceiver.Channels];
        readonly float[] _instant = new float[UnicornBandReceiver.Channels];
        readonly float[] _totals = new float[UnicornBandReceiver.Channels];
        readonly float[] _sorted = new float[UnicornBandReceiver.Channels];
        bool _primed;

        /// <summary>Smoothed score per channel: 0 Good … 3 Flat (fractional while changing).</summary>
        public float Score(int ch) => _score[ch];
        public ChannelQuality Quality(int ch) => (ChannelQuality)Mathf.Clamp(Mathf.RoundToInt(_score[ch]), 0, 3);
        public float TotalPower(int ch) => _totals[ch];

        /// <summary>Worst smoothed quality across channels.</summary>
        public ChannelQuality Overall
        {
            get
            {
                var worst = ChannelQuality.Good;
                for (int i = 0; i < _score.Length; i++) if (Quality(i) > worst) worst = Quality(i);
                return worst;
            }
        }

        public int CountAtLeast(ChannelQuality q)
        {
            int n = 0;
            for (int i = 0; i < _score.Length; i++) if (Quality(i) >= q) n++;
            return n;
        }

        public void Reset()
        {
            Array.Clear(_score, 0, _score.Length);
            _primed = false;
        }

        /// <summary>Feeds one sample of channel-major band powers; <paramref name="dt"/> since the previous one.</summary>
        public void Update(float[] channelBands, float dt, RawEegProcessor raw = null)
        {
            Assess(channelBands, _instant, _totals, raw);
            float k = !_primed || smoothingSeconds <= 0f ? 1f : 1f - Mathf.Exp(-Mathf.Max(0f, dt) / smoothingSeconds);
            for (int i = 0; i < _score.Length; i++) _score[i] = Mathf.Lerp(_score[i], _instant[i], k);
            _primed = true;
        }

        /// <summary>Unsmoothed per-channel assessment (as 0..3 floats) — the testable core.</summary>
        public void Assess(float[] channelBands, float[] scoresOut, float[] totalsOut, RawEegProcessor raw = null)
        {
            const int B = UnicornBandReceiver.Bands;
            int n = UnicornBandReceiver.Channels;
            for (int ch = 0; ch < n; ch++)
            {
                float t = 0f;
                for (int b = 0; b < B; b++) t += Mathf.Max(0f, channelBands[ch * B + b]);
                totalsOut[ch] = t;
            }

            Array.Copy(totalsOut, _sorted, n);
            Array.Sort(_sorted);
            float median = Mathf.Max(0.5f * (_sorted[n / 2 - 1] + _sorted[n / 2]), 1e-9f);

            for (int ch = 0; ch < n; ch++)
            {
                float t = totalsOut[ch];
                if (t < flatPower) { scoresOut[ch] = (float)ChannelQuality.Flat; continue; }

                float ratio = t / median;
                float spread = Mathf.Max(ratio, 1f / Mathf.Max(ratio, 1e-9f));
                float gamma = channelBands[ch * B + 6] / t;
                float delta = channelBands[ch * B + 0] / t;

                var q = ChannelQuality.Good;
                if (spread > fairRatio || gamma > gammaFair || delta > deltaFair) q = ChannelQuality.Fair;
                if (spread > badRatio || gamma > gammaBad || delta > deltaBad) q = ChannelQuality.Bad;
                scoresOut[ch] = (float)q;
            }

            if (raw == null) return;
            for (int ch = 0; ch < n; ch++)
            {
                float rms = raw.Rms[ch], line = raw.LineNoise[ch];
                // Raw RMS / rail decide flatness; tiny 1–45 Hz power alone may just mean pure mains hum.
                if (scoresOut[ch] >= (float)ChannelQuality.Flat) scoresOut[ch] = (float)ChannelQuality.Good;
                var q = ChannelQuality.Good;
                if (rms > rawFairRms || line > lineFair) q = ChannelQuality.Fair;
                if (rms > rawBadRms || line > lineBad) q = ChannelQuality.Bad;
                if (rms < rawFlatRms || Mathf.Abs(raw.Dc[ch]) >= railMicrovolts) q = ChannelQuality.Flat;
                scoresOut[ch] = Mathf.Max(scoresOut[ch], (float)q);
            }
        }
    }
}
