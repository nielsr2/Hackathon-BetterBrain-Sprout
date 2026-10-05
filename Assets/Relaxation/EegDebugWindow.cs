using UnityEngine;
using UnityEngine.InputSystem;

namespace Relaxation
{
    /// <summary>
    /// Draggable IMGUI debug window (bottom left): switch between headset and synthetic EEG, drive the
    /// simulation (relaxation level, noise, electrode fault, dropout) and poke the relaxation → growth
    /// pipeline (metric, algorithm, baseline, growth). Click the title row or press ` to collapse.
    /// </summary>
    [AddComponentMenu("Relaxation/EEG Debug Window")]
    public sealed class EegDebugWindow : MonoBehaviour
    {
        public UnicornBandReceiver receiver;
        public RelaxationTreeDriver driver;

        [Header("Display")]
        public bool visible = true;
        public bool collapsed = true;
        [Range(0.5f, 3f)] public float scale = 1f;

        const float Width = 340f, LabelWidth = 110f;
        const int WindowId = 0x5EED;
        static readonly Color Warn = new Color(1f, 0.6f, 0.35f);

        Rect _rect = new Rect(10f, -1f, Width, 0f);
        string[] _faultNames, _metricNames, _algorithmNames;
        GUIStyle _section;

        void OnEnable()
        {
            if (receiver == null) receiver = FindAnyObjectByType<UnicornBandReceiver>();
            if (driver == null) driver = FindAnyObjectByType<RelaxationTreeDriver>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.backquoteKey.wasPressedThisFrame) collapsed = !collapsed;
        }

        void OnGUI()
        {
            if (!visible) return;
            EnsureNames();
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float screenH = Screen.height / scale, screenW = Screen.width / scale;
            if (_rect.y < 0f) _rect.y = screenH - 40f; // first frame: bottom left, grows upward below
            _rect.height = 0f; // let GUILayout fit the content, so collapsing shrinks the window
            _rect = GUILayout.Window(WindowId, _rect, DrawWindow, GUIContent.none, GUI.skin.box, GUILayout.Width(Width));

            // Keep it on screen: re-anchor upward when expanding near the bottom edge.
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, screenW - _rect.width));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, screenH - _rect.height - 10f));
        }

        void DrawWindow(int id)
        {
            string summary = receiver == null ? "no receiver"
                : receiver.simulate ? $"SIM relax {receiver.SimulatedRelax:0.00}{(receiver.simDropout ? " · dropout" : "")}"
                : "live headset";
            if (CollapseHeader.Draw(ref collapsed, "Debug / simulate  (`)", summary))
            {
                DrawSource();
                DrawPipeline();
                DrawGrowth();
            }
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        // ------------------------------------------------------------------ sections

        void DrawSource()
        {
            Section("Signal source");
            if (receiver == null) { Colored("No UnicornBandReceiver in scene.", Warn); return; }

            int src = GUILayout.Toolbar(receiver.simulate ? 1 : 0, new[] { "Headset (" + receiver.source + ")", "Simulated EEG" });
            receiver.simulate = src == 1;
            if (!receiver.simulate)
            {
                GUILayout.Label(receiver.HasSignal ? $"Live: {receiver.PacketsPerSecond:0} packets/s" : "Live: no signal");
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Relax input", GUILayout.Width(LabelWidth));
            receiver.simRelaxManual = GUILayout.Toolbar(receiver.simRelaxManual ? 1 : 0, new[] { "Hold Space", "Slider" }) == 1;
            GUILayout.EndHorizontal();
            if (receiver.simRelaxManual)
                receiver.simRelaxTarget = Slider("Relax target", receiver.simRelaxTarget, 0f, 1f, "0.00");
            Meter("Relax now", receiver.SimulatedRelax, new Color(0.55f, 1f, 0.6f));
            receiver.simRelaxRampSeconds = Slider("Ramp (s)", receiver.simRelaxRampSeconds, 0f, 10f, "0.0");
            receiver.simNoiseMicrovolts = Slider("Noise (µV)", receiver.simNoiseMicrovolts, 0f, 50f, "0");

            GUILayout.Label("Electrode fault (F cycles)");
            receiver.simulatedBadChannel = GUILayout.SelectionGrid(receiver.simulatedBadChannel + 1, _faultNames, 5) - 1;

            receiver.simDropout = GUILayout.Toggle(receiver.simDropout, " Dropout (stop sending samples)");
        }

        void DrawPipeline()
        {
            if (driver == null) return;
            Section("Pipeline");

            GUILayout.Label("Metric (1–8)");
            driver.SelectMetric((RelaxationMetric)GUILayout.SelectionGrid((int)driver.activeMetric, _metricNames, 2));

            GUILayout.Label("Growth algorithm (G)");
            driver.algorithm = (GrowthAlgorithm)GUILayout.SelectionGrid((int)driver.algorithm, _algorithmNames, 2);

            var bl = driver.Baseline;
            if (bl != null)
                Meter(bl.IsReady ? "Baseline ready" : "Baseline", bl.Progress, Color.cyan);
            driver.baselineSeconds = Slider("Baseline (s)", driver.baselineSeconds, 1f, 60f, "0");
            if (GUILayout.Button("Re-baseline (B)")) driver.Recalibrate();
        }

        void DrawGrowth()
        {
            if (driver == null) return;
            Section("Growth");

            bool interactive = GUILayout.Toggle(driver.interactive, " EEG drives growth (off: Timeline owns it)");
            if (interactive && !driver.interactive) driver.BeginInteractive();
            driver.interactive = interactive;

            GUI.changed = false;
            float g = Slider("Growth", driver.displayedGrowth, driver.growthMin, driver.growthMax, "0.000");
            if (GUI.changed && driver.interactive) driver.SetGrowth(g);
            Meter("Relaxation", driver.relaxation, new Color(0.55f, 1f, 0.6f), driver.neutralPoint);
            driver.neutralPoint = Slider("Neutral point", driver.neutralPoint, 0f, 1f, "0.00");
            driver.secondsToFullGrowth = Slider("Full grow (s)", driver.secondsToFullGrowth, 5f, 300f, "0");

            if (GUILayout.Button("Reset growth (R)")) driver.ResetGrowth();
        }

        // ------------------------------------------------------------------ widgets

        void EnsureNames()
        {
            if (_faultNames == null)
            {
                _faultNames = new string[UnicornBandReceiver.Channels + 1];
                _faultNames[0] = "none";
                for (int i = 0; i < UnicornBandReceiver.Channels; i++) _faultNames[i + 1] = UnicornBandReceiver.ChannelLabels[i];
            }
            if (_metricNames == null)
            {
                _metricNames = new string[RelaxationMetrics.Count];
                for (int i = 0; i < _metricNames.Length; i++) _metricNames[i] = $"{i + 1} {RelaxationMetrics.Label((RelaxationMetric)i)}";
            }
            if (_algorithmNames == null)
            {
                var values = (GrowthAlgorithm[])System.Enum.GetValues(typeof(GrowthAlgorithm));
                _algorithmNames = new string[values.Length];
                for (int i = 0; i < values.Length; i++) _algorithmNames[i] = GrowthModel.Label(values[i]);
            }
        }

        void Section(string title)
        {
            _section ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 1f, 1f, 0.6f) } };
            GUILayout.Space(6);
            GUILayout.Label(title.ToUpperInvariant(), _section);
        }

        static float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(value.ToString(format), GUILayout.Width(44));
            GUILayout.EndHorizontal();
            return value;
        }

        static void Meter(string label, float value, Color fill, float marker = -1f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            var r = GUILayoutUtility.GetRect(1f, 10f, GUILayout.ExpandWidth(true));
            r.y += 5f;
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), Texture2D.whiteTexture);
            if (marker >= 0f)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(r.x + r.width * Mathf.Clamp01(marker) - 1f, r.y - 2f, 2f, r.height + 4f), Texture2D.whiteTexture);
            }
            GUI.color = prev;
            GUILayout.Label(value.ToString("0.00"), GUILayout.Width(44));
            GUILayout.EndHorizontal();
        }

        static void Colored(string text, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUILayout.Label(text);
            GUI.color = prev;
        }
    }
}
