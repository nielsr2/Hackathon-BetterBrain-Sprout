using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Relaxation
{
    /// <summary>
    /// Receives Unicorn Hybrid Black data over UDP and raises <see cref="Sampled"/> with band powers.
    /// Two packet formats (auto-detected):
    /// <list type="bullet">
    /// <item>Raw binary — the Unicorn Suite "Unicorn UDP" app: one sample per packet, 17 little-endian
    /// float32 (8 EEG µV, accel xyz g, gyro xyz °/s, battery %, counter, validation) at 250 Hz.
    /// Band powers are computed here by <see cref="RawEegProcessor"/> (10 estimates/s).</item>
    /// <item>Band-power CSV — comma-separated floats: indices 0–55 per-channel band powers, 56–62 the
    /// 8-channel averages δ, θ, α, βlow, βmid, βhigh, γ.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Relaxation/Unicorn Band Receiver")]
    public sealed class UnicornBandReceiver : MonoBehaviour
    {
        public enum Source { Udp, External }

        [Tooltip("Udp: listen for the Unicorn UDP app. External: another component owns the headset (e.g. the g.tec Device in this scene) and calls PushRawSample; no socket is opened.")]
        public Source source = Source.Udp;

        [Header("UDP")]
        public int port = 1000;
        [Tooltip("Bind to 127.0.0.1 instead of all interfaces (Unicorn Suite running on this PC).")]
        public bool loopbackOnly;
        [Tooltip("No packet for this long counts as signal loss.")]
        [Min(0.1f)] public float signalTimeout = 1f;

        public enum PacketFormat { Auto, RawBinary, BandPowerCsv }

        [Header("Packet format")]
        [Tooltip("Auto: binary packets are raw samples (Unicorn UDP app), text packets are band-power CSV.")]
        public PacketFormat format = PacketFormat.Auto;
        [Tooltip("Raw: 50 Hz notch on the displayed waveform (band powers stop below 45 Hz regardless).")]
        public bool notch50Hz = true;

        [Header("Band-power CSV layout")]
        [Tooltip("Index of the averaged delta value; theta..gamma follow.")]
        public int averageStartIndex = 56;
        [Tooltip("Index of theta at Fz (channel 1). 8 if per-channel data is band-major (δ ch1–8, θ ch1–8, …), 1 if channel-major. Verify against the HUD.")]
        public int fzThetaIndex = 8;
        [Tooltip("Per-channel block (indices 0–55) order. Off: band-major (δ ch1–8, θ ch1–8, …). On: channel-major (ch1 δ–γ, ch2 δ–γ, …). Must agree with fzThetaIndex.")]
        public bool channelMajor;
        [Tooltip("Enable if Unicorn sends power in dB (negative values): converts 10^(x/10) so ratios are meaningful.")]
        public bool bandsAreDecibels;

        [Header("Synthetic signal (no headset)")]
        [Tooltip("Generate fake raw EEG at 250 Hz (same path as the headset). Hold Space to 'relax' (raises α/θ, lowers β).")]
        public bool simulate;
        [Tooltip("Simulated electrode fault for testing the quality overlay: -1 none. Press F (simulate) to cycle.")]
        [Range(-1, Channels - 1)] public int simulatedBadChannel = -1;
        [Tooltip("Drive simulated relaxation from simRelaxTarget instead of holding Space.")]
        public bool simRelaxManual;
        [Range(0f, 1f)] public float simRelaxTarget = 0.5f;
        [Tooltip("Seconds for simulated relaxation to move fully 0→1.")]
        [Min(0f)] public float simRelaxRampSeconds = 3f;
        [Tooltip("Broadband noise per channel in µV.")]
        [Range(0f, 50f)] public float simNoiseMicrovolts = 3f;
        [Tooltip("Stop emitting samples: tests signal loss without the headset.")]
        public bool simDropout;

        public const int Channels = 8, Bands = 7;
        /// <summary>Raw packet: float32 count and field offsets.</summary>
        public const int RawFields = 17, RawAccel = 8, RawGyro = 11, RawBattery = 14, RawCounter = 15, RawValidation = 16;
        /// <summary>Unicorn Hybrid Black electrode positions, channel 1–8.</summary>
        public static readonly string[] ChannelLabels = { "Fz", "C3", "Cz", "C4", "Pz", "PO7", "Oz", "PO8" };
        public static readonly string[] BandLabels = { "δ", "θ", "α", "βl", "βm", "βh", "γ" };

        public BandPowers Latest { get; private set; }
        /// <summary>Per-channel band powers, channel-major: [ch * Bands + band]. Valid when <see cref="HasChannelData"/>.</summary>
        public float[] ChannelBands => _channelBands;
        public bool HasChannelData { get; private set; }
        /// <summary>Band-power estimates emitted (raw: 10/s; CSV: one per packet).</summary>
        public long SampleCount { get; private set; }
        /// <summary>The format packets are actually arriving in (Auto resolved).</summary>
        public PacketFormat ActiveFormat { get; private set; }
        /// <summary>Raw-mode DSP state: filtered waveform, RMS, DC offset, 50 Hz share. Null until raw packets arrive.</summary>
        public RawEegProcessor Raw { get; private set; }
        public bool HasRawData => Raw != null && Raw.Ready && ActiveFormat == PacketFormat.RawBinary;
        public float BatteryPercent { get; private set; } = -1f;
        public Vector3 Accel { get; private set; }
        /// <summary>Samples missing according to the raw packet counter.</summary>
        public long DroppedSamples { get; private set; }
        public int MalformedCount { get; private set; }
        public float PacketsPerSecond { get; private set; }
        public bool SocketOpen => _socket != null;
        public bool HasSignal => _packets > 0 && Time.unscaledTime - _lastPacketTime <= signalTimeout;
        /// <summary>Simulated relaxation 0..1 (Space held), for the HUD.</summary>
        public float SimulatedRelax { get; private set; }

        /// <summary>Raised on the main thread for each received (or simulated) sample.</summary>
        public event Action<BandPowers> Sampled;

        Socket _socket;
        readonly byte[] _buffer = new byte[8192];
        EndPoint _remote = new IPEndPoint(IPAddress.Any, 0);
        float _lastPacketTime = float.NegativeInfinity;
        long _packets;
        float _lastCounter = -1f;
        readonly float[] _rawSample = new float[RawFields];
        float _rateWindowStart;
        int _rateWindowCount;
        float _simAccumulator, _simTime;
        readonly System.Random _rng = new System.Random(1234);
        readonly float[] _channelBands = new float[Channels * Bands];
        bool _warnedMalformed, _warnedSocket;

        void OnEnable()
        {
            _rateWindowStart = Time.unscaledTime;
            if (source == Source.Udp) OpenSocket();
        }

        /// <summary>Feeds one raw sample (17 fields, same layout as a Unicorn UDP packet) from an in-process source.</summary>
        public void PushRawSample(float[] sample)
        {
            if (sample == null || sample.Length < RawFields || simulate) return;
            OnRawSample(sample);
        }

        void OnDisable() => CloseSocket();

        void OpenSocket()
        {
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false, ReceiveBufferSize = 1 << 20 }; // raw stream is 250 packets/s: survive editor hitches
                _socket.Bind(new IPEndPoint(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port));
                Debug.Log($"[Relaxation] Listening for Unicorn data on UDP {(loopbackOnly ? "127.0.0.1" : "*")}:{port}", this);
            }
            catch (SocketException e)
            {
                Debug.LogError($"[Relaxation] Could not bind UDP port {port}: {e.Message}. Enable 'simulate' to test without the headset.", this);
                CloseSocket();
            }
        }

        void CloseSocket()
        {
            _socket?.Close();
            _socket = null;
        }

        void Update()
        {
            if (simulate) Simulate();
            else Drain();

            float window = Time.unscaledTime - _rateWindowStart;
            if (window >= 1f)
            {
                PacketsPerSecond = _rateWindowCount / window;
                _rateWindowCount = 0;
                _rateWindowStart = Time.unscaledTime;
            }
        }

        void Drain()
        {
            if (_socket == null) return;
            try
            {
                while (_socket.Available > 0)
                {
                    int n = _socket.ReceiveFrom(_buffer, ref _remote);
                    if (n <= 0) continue;
                    bool raw = format == PacketFormat.RawBinary || (format == PacketFormat.Auto && !LooksLikeText(_buffer, n));
                    if (raw)
                    {
                        if (TryParseRaw(_buffer, n, _rawSample)) OnRawSample(_rawSample);
                        else Malformed(n, $"expected {RawFields} float32 ({RawFields * 4} bytes)");
                        continue;
                    }
                    string message = Encoding.ASCII.GetString(_buffer, 0, n);
                    if (TryParse(message, averageStartIndex, fzThetaIndex, bandsAreDecibels, out var b))
                    {
                        NotePacket(PacketFormat.BandPowerCsv);
                        HasChannelData = TryParseChannels(message, channelMajor, bandsAreDecibels, _channelBands);
                        Emit(b);
                    }
                    else Malformed(n, $"\"{Truncate(message, 120)}\"");
                }
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.WouldBlock) return;
                if (!_warnedSocket)
                {
                    _warnedSocket = true;
                    Debug.LogWarning($"[Relaxation] UDP receive error: {e.SocketErrorCode} {e.Message}", this);
                }
            }
        }

        void Malformed(int bytes, string detail)
        {
            MalformedCount++;
            if (_warnedMalformed) return;
            _warnedMalformed = true;
            Debug.LogWarning($"[Relaxation] Malformed Unicorn packet ({bytes} bytes, format {format}): {detail}", this);
        }

        void NotePacket(PacketFormat f)
        {
            if (_packets == 0 || f != ActiveFormat) Debug.Log($"[Relaxation] Receiving Unicorn {f} packets.", this);
            ActiveFormat = f;
            _packets++;
            _rateWindowCount++;
            _lastPacketTime = Time.unscaledTime;
        }

        void OnRawSample(float[] v)
        {
            NotePacket(PacketFormat.RawBinary);
            if (Raw == null) Raw = new RawEegProcessor(notch50Hz);
            BatteryPercent = v[RawBattery];
            Accel = new Vector3(v[RawAccel], v[RawAccel + 1], v[RawAccel + 2]);
            float counter = v[RawCounter];
            if (_lastCounter >= 0f && counter > _lastCounter + 1f) DroppedSamples += (long)(counter - _lastCounter - 1f);
            _lastCounter = counter;

            if (!Raw.Push(v)) return;
            Array.Copy(Raw.ChannelBands, _channelBands, _channelBands.Length);
            HasChannelData = true;
            Emit(Raw.Average);
        }

        void Emit(in BandPowers b)
        {
            Latest = b;
            SampleCount++;
            Sampled?.Invoke(b);
        }

        /// <summary>True if every byte is printable ASCII (or tab/CR/LF): a CSV packet rather than binary floats.</summary>
        public static bool LooksLikeText(byte[] buffer, int n)
        {
            for (int i = 0; i < n; i++)
            {
                byte c = buffer[i];
                if ((c < 32 || c > 126) && c != 9 && c != 10 && c != 13) return false;
            }
            return n > 0;
        }

        /// <summary>Parses a raw Unicorn UDP sample (17 little-endian float32) into <paramref name="into"/>.</summary>
        public static bool TryParseRaw(byte[] buffer, int n, float[] into)
        {
            if (n < RawFields * 4 || n % 4 != 0 || into == null || into.Length < RawFields) return false;
            for (int i = 0; i < RawFields; i++)
            {
                float v = BitConverter.IsLittleEndian
                    ? BitConverter.ToSingle(buffer, i * 4)
                    : BitConverter.ToSingle(new[] { buffer[i * 4 + 3], buffer[i * 4 + 2], buffer[i * 4 + 1], buffer[i * 4] }, 0);
                if (float.IsNaN(v) || float.IsInfinity(v)) return false;
                into[i] = v;
            }
            return true;
        }

        /// <summary>Parses one Unicorn UDP packet. Culture-invariant ("12.5" parses the same on a da-DK machine).</summary>
        public static bool TryParse(string message, int averageStartIndex, int fzThetaIndex, bool decibels, out BandPowers bands)
        {
            bands = default;
            if (string.IsNullOrEmpty(message)) return false;
            var parts = message.Split(',');
            if (averageStartIndex < 0 || parts.Length < averageStartIndex + 7) return false;

            var v = new float[7];
            for (int i = 0; i < 7; i++)
                if (!TryFloat(parts[averageStartIndex + i], out v[i])) return false;

            // Fall back to the averaged theta if the per-channel value is missing.
            float fz = v[1];
            if (fzThetaIndex >= 0 && fzThetaIndex < parts.Length && TryFloat(parts[fzThetaIndex], out float f)) fz = f;

            if (decibels)
            {
                for (int i = 0; i < 7; i++) v[i] = FromDb(v[i]);
                fz = FromDb(fz);
            }

            bands = new BandPowers
            {
                delta = v[0], theta = v[1], alpha = v[2],
                betaLow = v[3], betaMid = v[4], betaHigh = v[5], gamma = v[6],
                fzTheta = fz,
            };
            return true;
        }

        /// <summary>
        /// Parses the 56 per-channel band powers into <paramref name="into"/> (channel-major,
        /// length ≥ 56), whichever order the packet uses.
        /// </summary>
        public static bool TryParseChannels(string message, bool channelMajor, bool decibels, float[] into)
        {
            if (string.IsNullOrEmpty(message) || into == null || into.Length < Channels * Bands) return false;
            var parts = message.Split(',');
            if (parts.Length < Channels * Bands) return false;
            for (int ch = 0; ch < Channels; ch++)
            for (int band = 0; band < Bands; band++)
            {
                int src = channelMajor ? ch * Bands + band : band * Channels + ch;
                if (!TryFloat(parts[src], out float v)) return false;
                into[ch * Bands + band] = decibels ? FromDb(v) : v;
            }
            return true;
        }

        static bool TryFloat(string s, out float value) =>
            float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        static float FromDb(float db) => Mathf.Pow(10f, db / 10f);

        static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

        // ---------------------------------------------------------------- synthetic signal

        /// <summary>Synthesises raw 250 Hz samples and feeds them through the same path as the headset.</summary>
        void Simulate()
        {
            var kb = Keyboard.current;
            bool relaxHeld = kb != null && kb.spaceKey.isPressed;
            if (kb != null && kb.fKey.wasPressedThisFrame)
                simulatedBadChannel = simulatedBadChannel + 1 >= Channels ? -1 : simulatedBadChannel + 1;
            float target = simRelaxManual ? simRelaxTarget : relaxHeld ? 1f : 0f;
            SimulatedRelax = simRelaxRampSeconds > 0f
                ? Mathf.MoveTowards(SimulatedRelax, target, Time.deltaTime / simRelaxRampSeconds)
                : target;

            if (simDropout) { _simAccumulator = 0f; return; }
            _simAccumulator += Time.deltaTime * RawEegProcessor.SampleRate;
            int count = Mathf.Min((int)_simAccumulator, 100); // cap catch-up after a hitch
            _simAccumulator -= (int)_simAccumulator;
            for (int i = 0; i < count; i++)
            {
                _simTime += 1f / RawEegProcessor.SampleRate;
                SyntheticRawSample(_simTime, SimulatedRelax, simulatedBadChannel, _rawSample);
                OnRawSample(_rawSample);
            }
        }

        void SyntheticRawSample(float t, float relax, int bad, float[] into)
        {
            const float TwoPi = 2f * Mathf.PI;
            for (int ch = 0; ch < Channels; ch++)
            {
                float phase = ch * 0.7f;
                float alpha = 10f * (1f + 1.5f * relax) * (0.8f + 0.4f * Mathf.PerlinNoise(t * 0.5f, ch));
                float theta = 6f * (1f + 0.8f * relax);
                float beta = 4f * (1f - 0.5f * relax);
                float v = 200000f + ch * 15000f                                   // DC offset, as on the real device
                        + alpha * Mathf.Sin(TwoPi * 10f * t + phase)
                        + theta * Mathf.Sin(TwoPi * 6f * t + phase * 1.3f)
                        + beta * Mathf.Sin(TwoPi * 20f * t + phase * 2.1f)
                        + 8f * Mathf.Sin(TwoPi * 2f * t + phase)                 // delta
                        + 2f * Mathf.Sin(TwoPi * 50f * t)                         // mains hum
                        + simNoiseMicrovolts * Gaussian();
                if (ch == bad)
                    v = Mathf.Repeat(t, 10f) < 5f ? 750000f : v + 150f * Gaussian(); // railed flat, then noisy
                into[ch] = v;
            }
            into[RawAccel] = 0f; into[RawAccel + 1] = 0f; into[RawAccel + 2] = 1f;
            into[RawGyro] = into[RawGyro + 1] = into[RawGyro + 2] = 0f;
            into[RawBattery] = 100f;
            into[RawCounter] = Mathf.Round(t * RawEegProcessor.SampleRate);
            into[RawValidation] = 1f;
        }

        float Gaussian()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
