using Nib.ProcTree;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Relaxation
{
    /// <summary>
    /// Drives a <see cref="ProceduralTree"/>'s growth from the selected EEG relaxation metric.
    /// Every metric is z-scored against a resting baseline; the active one is smoothed, mapped
    /// to relaxation 0..1 and integrated as a growth <i>rate</i> — relaxed grows the tree,
    /// tense pauses (or recedes) it.
    /// Keys: 1–8 select metric · G cycle growth algorithm · B re-baseline · R reset growth.
    /// </summary>
    [AddComponentMenu("Relaxation/Relaxation Tree Driver")]
    public sealed class RelaxationTreeDriver : MonoBehaviour
    {
        [Header("References")]
        public UnicornBandReceiver receiver;
        public ProceduralTree tree;
        [Tooltip("Demo ping-pong animator on the tree; disabled while this driver runs.")]
        public GrowthAnimator demoAnimator;

        [Tooltip("Off: collect the baseline and compute metrics, but leave tree growth to something else " +
                 "(the tree-phase Timeline). BeginInteractive() hands growth over.")]
        public bool interactive = true;

        [Header("Metric")]
        public RelaxationMetric activeMetric = RelaxationMetric.AlphaThetaOverBeta;
        [Tooltip("Per metric (in enum order): flip the sign so a falling value counts as relaxation.")]
        public bool[] invert = new bool[RelaxationMetrics.Count];
        [Tooltip("Crossfade between the old and new metric on a switch, so the tree never jumps.")]
        [Min(0f)] public float switchBlendSeconds = 0.75f;

        [Header("Baseline")]
        [Tooltip("Seconds of resting signal to collect before the tree responds.")]
        [Min(0.5f)] public float baselineSeconds = 30f;

        [Header("Signal conditioning")]
        [Tooltip("Exponential smoothing time constant on the z-score.")]
        [Min(0f)] public float smoothingSeconds = 1.5f;
        [Tooltip("z-score that maps to relaxation 0.")]
        public float zLow = -0.5f;
        [Tooltip("z-score that maps to relaxation 1.")]
        public float zHigh = 2f;
        [Tooltip("Relaxation below this recedes the tree, above it grows the tree.")]
        [Range(0f, 1f)] public float neutralPoint = 0.25f;
        [Tooltip("Shapes relaxation 0..1 after the z mapping.")]
        public AnimationCurve responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Growth")]
        public GrowthAlgorithm algorithm = GrowthAlgorithm.Integrate;
        [Range(0f, 1f)] public float growthMin = 0.05f;
        [Range(0f, 1f)] public float growthMax = 1f;
        [Range(0f, 1f)] public float startGrowth = 0.05f;
        [Tooltip("Seconds from min to max growth at full relaxation.")]
        [Min(0f)] public float secondsToFullGrowth = 120f;
        [Tooltip("Seconds from max to min at zero relaxation. 0 = never recede, only pause.")]
        [Min(0f)] public float recedeSecondsToMin;

        [Header("Tweening")]
        [Tooltip("SmoothDamp time from target to displayed growth; hides packet-rate stepping.")]
        [Min(0f)] public float tweenSeconds = 0.6f;
        [Tooltip("Max displayed growth change per second. 0 = unlimited.")]
        [Min(0f)] public float maxTweenSpeed;

        [Header("Live (read-only)")]
        public float rawValue;
        public float zScore;
        public float smoothedZ;
        public float relaxation;
        public float rate;
        public float targetGrowth;
        public float displayedGrowth;

        public RelaxationBaseline Baseline => _baseline;
        public float RawValue(int i) => _raw[i];
        public float SmoothedZ(int i) => _smoothedZ[i];
        public bool HasSample => _hasSample;

        RelaxationBaseline _baseline;
        readonly float[] _raw = new float[RelaxationMetrics.Count];
        readonly float[] _smoothedZ = new float[RelaxationMetrics.Count];
        bool _hasSample;
        RelaxationMetric _shownMetric, _blendFrom;
        float _blend = 1f;
        float _tweenVelocity;

        void Awake()
        {
            _baseline = new RelaxationBaseline(RelaxationMetrics.Count, baselineSeconds);
            _shownMetric = _blendFrom = activeMetric;
        }

        void OnEnable()
        {
            if (tree == null) tree = GetComponent<ProceduralTree>();
            if (demoAnimator == null) demoAnimator = GetComponent<GrowthAnimator>();
            if (demoAnimator != null) demoAnimator.enabled = false;
            if (receiver == null) receiver = FindAnyObjectByType<UnicornBandReceiver>();
            if (receiver != null) receiver.Sampled += OnSample;
            else Debug.LogWarning("[Relaxation] No UnicornBandReceiver found; the tree will hold at its start growth.", this);
            if (interactive) ResetGrowth();
        }

        /// <summary>Take over growth from wherever the tree is now (e.g. the Timeline's last value), without a jump.</summary>
        public void BeginInteractive()
        {
            float g = tree != null ? tree.Growth : startGrowth;
            targetGrowth = displayedGrowth = Mathf.Clamp(g, growthMin, growthMax);
            _tweenVelocity = 0f;
            interactive = true;
        }

        void OnDisable()
        {
            if (receiver != null) receiver.Sampled -= OnSample;
        }

        void OnValidate()
        {
            if (invert == null || invert.Length != RelaxationMetrics.Count) System.Array.Resize(ref invert, RelaxationMetrics.Count);
            growthMax = Mathf.Max(growthMax, growthMin);
            startGrowth = Mathf.Clamp(startGrowth, growthMin, growthMax);
            if (zHigh <= zLow) zHigh = zLow + 0.01f;
        }

        void OnSample(BandPowers b)
        {
            RelaxationMetrics.ComputeAll(b, _raw);
            _hasSample = true;
            _baseline.Add(_raw);
        }

        public void SelectMetric(RelaxationMetric m) => activeMetric = m;

        public void CycleAlgorithm() =>
            algorithm = (GrowthAlgorithm)(((int)algorithm + 1) % System.Enum.GetValues(typeof(GrowthAlgorithm)).Length);

        /// <summary>Restart baseline collection; the tree holds until it completes.</summary>
        public void Recalibrate()
        {
            _baseline.Reset();
            System.Array.Clear(_smoothedZ, 0, _smoothedZ.Length);
        }

        public void ResetGrowth() => SetGrowth(startGrowth);

        /// <summary>Jump growth to <paramref name="g"/> (clamped to min..max), no tween.</summary>
        public void SetGrowth(float g)
        {
            targetGrowth = displayedGrowth = Mathf.Clamp(g, growthMin, growthMax);
            _tweenVelocity = 0f;
            if (tree != null) tree.Growth = displayedGrowth;
        }

        void Update()
        {
            HandleKeys();
            float dt = Time.deltaTime;
            bool signal = receiver != null && receiver.HasSignal;

            _baseline.DurationSeconds = baselineSeconds;
            if (signal) _baseline.Tick(dt);

            if (activeMetric != _shownMetric)
            {
                _blendFrom = _shownMetric;
                _shownMetric = activeMetric;
                _blend = 0f;
            }
            _blend = switchBlendSeconds > 0f ? Mathf.Min(1f, _blend + dt / switchBlendSeconds) : 1f;

            int a = (int)activeMetric;
            rawValue = _raw[a];
            rate = 0f;
            if (_baseline.IsReady && _hasSample)
            {
                float k = smoothingSeconds > 0f ? 1f - Mathf.Exp(-dt / smoothingSeconds) : 1f;
                for (int i = 0; i < RelaxationMetrics.Count; i++)
                {
                    float z = _baseline.ZScore(i, _raw[i]);
                    if (invert[i]) z = -z;
                    _smoothedZ[i] = Mathf.Lerp(_smoothedZ[i], z, k);
                }
                zScore = _baseline.ZScore(a, _raw[a]) * (invert[a] ? -1f : 1f);
                smoothedZ = Mathf.Lerp(_smoothedZ[(int)_blendFrom], _smoothedZ[a], Mathf.SmoothStep(0f, 1f, _blend));
                relaxation = Mathf.Clamp01(responseCurve.Evaluate(Mathf.InverseLerp(zLow, zHigh, smoothedZ)));
            }
            else
            {
                zScore = smoothedZ = relaxation = 0f;
            }

            if (!interactive)
            {
                // Someone else owns growth; mirror it so the HUD and a later hand-over read the truth.
                rate = 0f;
                if (tree != null) targetGrowth = displayedGrowth = tree.Growth;
                return;
            }

            if (signal && _baseline.IsReady && _hasSample && dt > 0f)
            {
                float next = GrowthModel.NextTarget(algorithm, targetGrowth, relaxation, neutralPoint,
                    growthMin, growthMax, secondsToFullGrowth, recedeSecondsToMin, dt);
                rate = (next - targetGrowth) / dt;
                targetGrowth = next;
            }
            float maxSpeed = maxTweenSpeed > 0f ? maxTweenSpeed : Mathf.Infinity;
            displayedGrowth = tweenSeconds > 0f
                ? Mathf.SmoothDamp(displayedGrowth, targetGrowth, ref _tweenVelocity, tweenSeconds, maxSpeed, dt)
                : targetGrowth;
            displayedGrowth = Mathf.Clamp(displayedGrowth, growthMin, growthMax);
            if (tree != null) tree.Growth = displayedGrowth;
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            for (int i = 0; i < RelaxationMetrics.Count; i++)
            {
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame || kb[(Key)((int)Key.Numpad1 + i)].wasPressedThisFrame)
                    SelectMetric((RelaxationMetric)i);
            }
            if (kb.bKey.wasPressedThisFrame) Recalibrate();
            if (kb.gKey.wasPressedThisFrame) CycleAlgorithm();
            if (kb.rKey.wasPressedThisFrame && interactive) ResetGrowth();
        }
    }
}
