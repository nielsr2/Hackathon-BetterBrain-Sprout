using UnityEngine;
using UnityEngine.InputSystem;

namespace Relaxation
{
    /// <summary>
    /// IMGUI overlay: signal status, baseline progress, every metric (raw | z) with clickable
    /// rows to switch, and the growth bar. Click the header to collapse; H hides it.
    /// </summary>
    [AddComponentMenu("Relaxation/Relaxation HUD")]
    public sealed class RelaxationHud : MonoBehaviour
    {
        public RelaxationTreeDriver driver;
        public UnicornBandReceiver receiver;
        public bool visible = true;
        [Tooltip("Show only the header row; click it to expand.")]
        public bool collapsed;
        [Range(0.5f, 3f)] public float scale = 1f;

        static readonly Color Active = new Color(0.55f, 1f, 0.6f);
        static readonly Color Warn = new Color(1f, 0.6f, 0.35f);

        void OnEnable()
        {
            if (driver == null) driver = FindAnyObjectByType<RelaxationTreeDriver>();
            if (receiver == null) receiver = FindAnyObjectByType<UnicornBandReceiver>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible || driver == null || driver.Baseline == null) return;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUILayout.BeginArea(new Rect(10, 10, 460, Screen.height / scale - 20f));
            GUILayout.BeginVertical(GUI.skin.box);

            if (CollapseHeader.Draw(ref collapsed, "Relaxation", $"{RelaxationMetrics.Label(driver.activeMetric)} · growth {driver.displayedGrowth:0.00}"))
            {
                DrawStatus();
                DrawBaseline();
                GUILayout.Space(4);
                DrawMetrics();
                GUILayout.Space(4);
                DrawGrowth();
                GUILayout.Label("1–8 metric · G algorithm · B re-baseline · R reset growth · H hide · O/W overlay · ` debug" + (receiver != null && receiver.simulate ? " · Space relax" : ""));
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void DrawStatus()
        {
            string status;
            var prev = GUI.color;
            if (receiver == null) { status = "NO RECEIVER"; GUI.color = Warn; }
            else if (receiver.simulate) { status = $"SIMULATED  {receiver.PacketsPerSecond:0.0} Hz  relax {receiver.SimulatedRelax:0.00}"; GUI.color = Warn; }
            else if (!receiver.SocketOpen) { status = $"UDP {receiver.port} NOT BOUND"; GUI.color = Warn; }
            else if (!receiver.HasSignal) { status = $"NO SIGNAL on UDP {receiver.port}  (malformed: {receiver.MalformedCount})"; GUI.color = Warn; }
            else status = $"UDP {receiver.port}  {receiver.ActiveFormat}  {receiver.PacketsPerSecond:0} packets/s";
            GUILayout.Label(status);
            GUI.color = prev;

            if (receiver != null && receiver.SampleCount > 0)
            {
                var b = receiver.Latest;
                GUILayout.Label($"δ {b.delta:0.00}  θ {b.theta:0.00}  α {b.alpha:0.00}  β {b.Beta:0.00}  γ {b.gamma:0.00}  Fzθ {b.fzTheta:0.00}");
            }
        }

        void DrawBaseline()
        {
            var bl = driver.Baseline;
            if (bl.IsReady) { GUILayout.Label($"Baseline ready ({bl.SampleCount} samples)"); return; }
            GUILayout.Label($"Collecting baseline… {bl.Progress * driver.baselineSeconds:0}/{driver.baselineSeconds:0} s — sit still and relax");
            Bar(bl.Progress, 0f, Color.cyan);
        }

        void DrawMetrics()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Metric", GUILayout.Width(230));
            GUILayout.Label("raw", GUILayout.Width(90));
            GUILayout.Label("z (smoothed)", GUILayout.Width(100));
            GUILayout.EndHorizontal();

            var prev = GUI.color;
            for (int i = 0; i < RelaxationMetrics.Count; i++)
            {
                var m = (RelaxationMetric)i;
                bool active = driver.activeMetric == m;
                GUI.color = active ? Active : prev;
                GUILayout.BeginHorizontal();
                string inv = driver.invert != null && i < driver.invert.Length && driver.invert[i] ? " (inv)" : "";
                if (GUILayout.Button($"{i + 1}  {RelaxationMetrics.Label(m)}  {RelaxationMetrics.Formula(m)}{inv}", GUILayout.Width(230)))
                    driver.SelectMetric(m);
                GUILayout.Label(driver.HasSample ? driver.RawValue(i).ToString("0.000") : "–", GUILayout.Width(90));
                GUILayout.Label(driver.Baseline.IsReady ? driver.SmoothedZ(i).ToString("+0.00;-0.00") : "–", GUILayout.Width(100));
                GUILayout.EndHorizontal();
            }
            GUI.color = prev;
        }

        void DrawGrowth()
        {
            GUILayout.Label($"Relaxation {driver.relaxation:0.00}  (neutral {driver.neutralPoint:0.00})   rate {driver.rate * 60f:+0.000;-0.000}/min");
            Bar(driver.relaxation, driver.neutralPoint, Active);
            GUILayout.Label($"Algorithm  {GrowthModel.Label(driver.algorithm)}");
            GUILayout.Label($"Growth {driver.displayedGrowth:0.000}  → target {driver.targetGrowth:0.000}   [{driver.growthMin:0.00}–{driver.growthMax:0.00}]");
            Bar(driver.displayedGrowth, driver.targetGrowth, new Color(0.8f, 0.65f, 0.3f));
        }

        /// <summary>Filled bar for <paramref name="value"/> 0..1 with a tick at <paramref name="marker"/>.</summary>
        static void Bar(float value, float marker, Color fill)
        {
            var r = GUILayoutUtility.GetRect(1f, 10f, GUILayout.ExpandWidth(true));
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), Texture2D.whiteTexture);
            if (marker > 0f)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(r.x + r.width * Mathf.Clamp01(marker) - 1f, r.y - 2f, 2f, r.height + 4f), Texture2D.whiteTexture);
            }
            GUI.color = prev;
        }
    }
}
