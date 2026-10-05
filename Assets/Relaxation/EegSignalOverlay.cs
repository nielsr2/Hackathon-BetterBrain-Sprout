using UnityEngine;
using UnityEngine.InputSystem;

namespace Relaxation
{
    /// <summary>
    /// IMGUI electrode overlay (top right): per-electrode signal quality on a head map, a
    /// collapsible waveform inspector (channel power, band power, or the relaxation pipeline
    /// scrolling over time). Click the status row to collapse the panel.
    /// Keys: O overlay · W inspector. Growth/simulation controls live in <see cref="EegDebugWindow"/>.
    /// </summary>
    [AddComponentMenu("Relaxation/EEG Signal Overlay")]
    public sealed class EegSignalOverlay : MonoBehaviour
    {
        public enum InspectorMode { RawEeg, Channels, Bands, Relaxation }

        public UnicornBandReceiver receiver;
        public RelaxationTreeDriver driver;
        public SignalQuality quality = new SignalQuality();

        [Header("Display")]
        public bool visible = true;
        [Tooltip("Show only the status row; click it to expand.")]
        public bool collapsed;
        public bool inspectorOpen;
        public InspectorMode mode = InspectorMode.RawEeg;
        [Tooltip("Raw EEG tab: ± range per row in µV (1 Hz high-pass, 50 Hz notch).")]
        [Range(10f, 500f)] public float rawRangeMicrovolts = 50f;
        [Range(0.5f, 3f)] public float scale = 1f;
        [Tooltip("Samples kept in the inspector (one per packet); also the plot width in pixels.")]
        [Range(100, 1000)] public int historyLength = 400;
        [Range(16, 60)] public int rowHeight = 26;

        const float PanelWidth = 380f;
        const int MaxRows = UnicornBandReceiver.Channels;

        static readonly Color32[] QualityColors =
        {
            new Color32(90, 220, 120, 255),  // Good
            new Color32(240, 200, 70, 255),  // Fair
            new Color32(240, 80, 70, 255),   // Bad
            new Color32(120, 120, 130, 255), // Flat
        };
        static readonly string[] QualityNames = { "GOOD", "FAIR", "BAD", "FLAT" };
        static readonly Color32[] BandColors =
        {
            new Color32(150, 120, 255, 255), new Color32(90, 170, 255, 255), new Color32(90, 230, 160, 255),
            new Color32(230, 230, 90, 255), new Color32(250, 170, 70, 255), new Color32(250, 110, 80, 255),
            new Color32(230, 90, 200, 255),
        };
        static readonly string[] RelaxRows = { "z (smoothed)", "relaxation", "growth" };

        // Top-view electrode positions in a unit head box, nose up (10-20 approximations).
        static readonly Vector2[] Positions =
        {
            new Vector2(0.50f, 0.28f), new Vector2(0.24f, 0.50f), new Vector2(0.50f, 0.50f), new Vector2(0.76f, 0.50f),
            new Vector2(0.50f, 0.70f), new Vector2(0.27f, 0.80f), new Vector2(0.50f, 0.88f), new Vector2(0.73f, 0.80f),
        };

        // History: ring buffer [sample, row] for each mode.
        float[,] _chHist, _bandHist, _relaxHist;
        int _head, _filled;
        float _lastSampleTime = -1f;
        Texture2D _plot, _disk, _ring;
        Color32[] _pixels;
        bool _dirty;
        float _lastRawRender;

        void OnEnable()
        {
            if (receiver == null) receiver = FindAnyObjectByType<UnicornBandReceiver>();
            if (driver == null) driver = FindAnyObjectByType<RelaxationTreeDriver>();
            AllocateHistory();
            if (receiver != null) receiver.Sampled += OnSample;
        }

        void OnDisable()
        {
            if (receiver != null) receiver.Sampled -= OnSample;
        }

        void OnDestroy()
        {
            if (_plot != null) Destroy(_plot);
            if (_disk != null) Destroy(_disk);
            if (_ring != null) Destroy(_ring);
        }

        void AllocateHistory()
        {
            _chHist = new float[historyLength, UnicornBandReceiver.Channels];
            _bandHist = new float[historyLength, UnicornBandReceiver.Bands];
            _relaxHist = new float[historyLength, RelaxRows.Length];
            _head = _filled = 0;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.oKey.wasPressedThisFrame) visible = !visible;
            if (kb.wKey.wasPressedThisFrame) inspectorOpen = !inspectorOpen;
        }

        void OnSample(BandPowers b)
        {
            if (_chHist == null || _chHist.GetLength(0) != historyLength) AllocateHistory();

            float now = Time.unscaledTime;
            float dt = _lastSampleTime < 0f ? 0f : now - _lastSampleTime;
            _lastSampleTime = now;

            if (receiver.HasChannelData)
            {
                quality.Update(receiver.ChannelBands, dt, receiver.HasRawData ? receiver.Raw : null);
                for (int ch = 0; ch < UnicornBandReceiver.Channels; ch++)
                    _chHist[_head, ch] = Log(quality.TotalPower(ch));
            }

            float[] bands = { b.delta, b.theta, b.alpha, b.betaLow, b.betaMid, b.betaHigh, b.gamma };
            for (int i = 0; i < bands.Length; i++) _bandHist[_head, i] = Log(bands[i]);

            if (driver != null)
            {
                _relaxHist[_head, 0] = driver.smoothedZ;
                _relaxHist[_head, 1] = driver.relaxation;
                _relaxHist[_head, 2] = driver.displayedGrowth;
            }

            _head = (_head + 1) % historyLength;
            _filled = Mathf.Min(_filled + 1, historyLength);
            _dirty = true;
        }

        static float Log(float v) => Mathf.Log10(Mathf.Max(v, 1e-9f));

        // ------------------------------------------------------------------ GUI

        void OnGUI()
        {
            if (!visible) return;
            EnsureTextures();
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float x = Screen.width / scale - PanelWidth - 10f;
            GUILayout.BeginArea(new Rect(x, 10f, PanelWidth, Screen.height / scale - 20f));
            GUILayout.BeginVertical(GUI.skin.box);

            if (DrawHeader())
            {
                DrawHeadMap();
                DrawInspector();
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>Status row, clickable to collapse. Returns true when the body should be drawn.</summary>
        bool DrawHeader()
        {
            var prev = GUI.color;
            string status;
            Color c;
            if (receiver == null) { status = "NO RECEIVER"; c = QualityColors[2]; }
            else if (!receiver.HasSignal) { status = "NO SIGNAL"; c = QualityColors[3]; }
            else if (!receiver.HasChannelData) { status = "NO PER-CHANNEL DATA"; c = QualityColors[1]; }
            else
            {
                var o = quality.Overall;
                int bad = quality.CountAtLeast(ChannelQuality.Bad);
                status = o == ChannelQuality.Good ? "SIGNAL GOOD"
                    : o == ChannelQuality.Fair ? "SIGNAL FAIR"
                    : $"{bad} ELECTRODE{(bad == 1 ? "" : "S")} BAD";
                c = QualityColors[(int)o];
            }
            GUILayout.BeginHorizontal();
            GUI.color = c;
            if (GUILayout.Button($"{(collapsed ? "▸" : "▾")} ● EEG  {status}", Bold())) collapsed = !collapsed;
            GUI.color = prev;
            GUILayout.FlexibleSpace();
            if (receiver != null)
                GUILayout.Label($"{(receiver.simulate ? "SIM " : "")}{receiver.PacketsPerSecond:0} Hz");
            GUILayout.EndHorizontal();
            if (collapsed) return false;
            if (receiver != null && receiver.HasSignal)
            {
                string fmt = receiver.ActiveFormat == UnicornBandReceiver.PacketFormat.RawBinary ? "raw 250 Hz → FFT band powers" : "band-power CSV";
                string bat = receiver.BatteryPercent >= 0f ? $" · battery {receiver.BatteryPercent:0}%" : "";
                string drop = receiver.DroppedSamples > 0 ? $" · dropped {receiver.DroppedSamples}" : "";
                GUILayout.Label(fmt + bat + drop, Small());
            }
            return true;
        }

        void DrawHeadMap()
        {
            const float size = 150f;
            GUILayout.BeginHorizontal();
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            var prev = GUI.color;

            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            var head = new Rect(r.x + 10f, r.y + 14f, size - 20f, size - 20f);
            GUI.DrawTexture(head, _ring);
            GUI.DrawTexture(new Rect(head.center.x - 6f, head.y - 10f, 12f, 12f), _ring); // nose

            bool live = receiver != null && receiver.HasSignal && receiver.HasChannelData;
            for (int ch = 0; ch < UnicornBandReceiver.Channels; ch++)
            {
                var p = new Vector2(head.x + Positions[ch].x * head.width, head.y + Positions[ch].y * head.height);
                GUI.color = live ? (Color)QualityColors[(int)quality.Quality(ch)] : (Color)QualityColors[3];
                GUI.DrawTexture(new Rect(p.x - 9f, p.y - 9f, 18f, 18f), _disk);
                GUI.color = Color.white;
                GUI.Label(new Rect(p.x - 18f, p.y + 7f, 36f, 16f), UnicornBandReceiver.ChannelLabels[ch], Centered());
            }
            GUI.color = prev;

            // Legend / per-channel table.
            GUILayout.BeginVertical();
            for (int ch = 0; ch < UnicornBandReceiver.Channels; ch++)
            {
                string q = live ? QualityNames[(int)quality.Quality(ch)] : "–";
                GUI.color = live ? (Color)QualityColors[(int)quality.Quality(ch)] : prev;
                string detail = !live ? ""
                    : receiver.HasRawData ? $"{receiver.Raw.Rms[ch],5:0.0}µV {receiver.Raw.Dc[ch] / 1000f,5:0}mV"
                    : quality.TotalPower(ch).ToString("0.0");
                GUILayout.Label($"{UnicornBandReceiver.ChannelLabels[ch],-4} {q,-4} {detail}", Mono());
            }
            GUI.color = prev;
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        void DrawInspector()
        {
            if (GUILayout.Button((inspectorOpen ? "▾" : "▸") + " Waveform inspector  (W)", GUI.skin.label)) inspectorOpen = !inspectorOpen;
            if (!inspectorOpen) return;

            var next = (InspectorMode)GUILayout.Toolbar((int)mode, new[] { "Raw EEG", "Ch power", "Bands", "Relaxation" });
            if (mode == InspectorMode.RawEeg && Time.unscaledTime - _lastRawRender > 1f / 30f) { _dirty = true; _lastRawRender = Time.unscaledTime; }
            if (next != mode) { mode = next; _dirty = true; }

            int rows = RowCount();
            int h = rows * rowHeight;
            var r = GUILayoutUtility.GetRect(PanelWidth - 12f, h, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                if (_dirty) RenderPlot(rows);
                GUI.DrawTexture(r, _plot, ScaleMode.StretchToFill);
                for (int i = 0; i < rows; i++)
                    GUI.Label(new Rect(r.x + 3f, r.y + i * (r.height / rows), 120f, 16f), RowLabel(i), Small());
            }
            if (mode == InspectorMode.RawEeg)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"±{rawRangeMicrovolts:0} µV · 4 s · 1 Hz HP + 50 Hz notch", Small());
                if (GUILayout.Button("−", GUILayout.Width(24))) { rawRangeMicrovolts = Mathf.Max(10f, rawRangeMicrovolts / 2f); _dirty = true; }
                if (GUILayout.Button("+", GUILayout.Width(24))) { rawRangeMicrovolts = Mathf.Min(500f, rawRangeMicrovolts * 2f); _dirty = true; }
                GUILayout.EndHorizontal();
                if (receiver == null || !receiver.HasRawData)
                    GUILayout.Label("No raw stream (band-power CSV input has no waveform).", Small());
            }
            else GUILayout.Label(mode == InspectorMode.Relaxation
                ? "z: lines at zLow/zHigh · relaxation: line at neutral"
                : $"log10 power, auto-scaled per row · {historyLength} estimates (10/s)", Small());
        }

        int RowCount() =>
            mode == InspectorMode.Channels || mode == InspectorMode.RawEeg ? UnicornBandReceiver.Channels
            : mode == InspectorMode.Bands ? UnicornBandReceiver.Bands
            : RelaxRows.Length;

        string RowLabel(int i) =>
            mode == InspectorMode.Channels || mode == InspectorMode.RawEeg ? UnicornBandReceiver.ChannelLabels[i]
            : mode == InspectorMode.Bands ? UnicornBandReceiver.BandLabels[i]
            : RelaxRows[i];

        // ------------------------------------------------------------------ plotting

        void RenderPlot(int rows)
        {
            _dirty = false;
            int w = historyLength, h = rows * rowHeight;
            if (_plot == null || _plot.width != w || _plot.height != h)
            {
                if (_plot != null) Destroy(_plot);
                _plot = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                _pixels = new Color32[w * h];
            }

            var bg = new Color32(12, 14, 18, 230);
            var sep = new Color32(45, 50, 60, 255);
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = bg;

            var hist = mode == InspectorMode.Channels ? _chHist : mode == InspectorMode.Bands ? _bandHist : _relaxHist;
            for (int row = 0; row < rows; row++)
            {
                // Texture y runs bottom-up; row 0 is drawn at the top.
                int y0 = h - (row + 1) * rowHeight;
                HLine(y0, sep, w);

                if (mode == InspectorMode.RawEeg)
                {
                    RenderRawRow(row, y0, w);
                    continue;
                }

                float lo, hi;
                RowRange(hist, row, out lo, out hi);
                if (mode == InspectorMode.Relaxation && driver != null)
                {
                    var guide = new Color32(80, 80, 95, 255);
                    if (row == 0) { HLine(Y(driver.zLow, lo, hi, y0), guide, w); HLine(Y(driver.zHigh, lo, hi, y0), guide, w); }
                    if (row == 1) HLine(Y(driver.neutralPoint, lo, hi, y0), guide, w);
                }

                var col = RowColor(row);
                int prevY = -1;
                for (int i = 0; i < _filled; i++)
                {
                    int x = w - _filled + i;
                    int idx = (_head - _filled + i + historyLength) % historyLength;
                    int y = Y(hist[idx, row], lo, hi, y0);
                    if (prevY < 0) prevY = y;
                    int a = Mathf.Min(prevY, y), b = Mathf.Max(prevY, y);
                    for (int yy = a; yy <= b; yy++) _pixels[yy * w + x] = col;
                    prevY = y;
                }
            }
            _plot.SetPixels32(_pixels);
            _plot.Apply(false);
        }

        /// <summary>Filtered raw trace, newest on the right; min/max per pixel column so spikes survive decimation.</summary>
        void RenderRawRow(int ch, int y0, int w)
        {
            var raw = receiver != null && receiver.HasRawData ? receiver.Raw : null;
            HLine(Y(0f, -rawRangeMicrovolts, rawRangeMicrovolts, y0), new Color32(35, 40, 48, 255), w);
            if (raw == null) return;
            var col = RowColor(ch);
            int n = raw.DisplayCount;
            float perCol = RawEegProcessor.DisplaySamples / (float)w;
            for (int x = 0; x < w; x++)
            {
                int newest = Mathf.FloorToInt((w - 1 - x) * perCol), oldest = Mathf.FloorToInt((w - x) * perCol) - 1;
                if (newest >= n) continue;
                oldest = Mathf.Min(Mathf.Max(oldest, newest), n - 1);
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                for (int age = newest; age <= oldest; age++)
                {
                    float v = raw.Display(ch, age);
                    lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
                }
                int a = Y(lo, -rawRangeMicrovolts, rawRangeMicrovolts, y0), b = Y(hi, -rawRangeMicrovolts, rawRangeMicrovolts, y0);
                for (int yy = a; yy <= b; yy++) _pixels[yy * w + x] = col;
            }
        }

        int Y(float v, float lo, float hi, int y0)
        {
            float t = hi > lo ? Mathf.Clamp01((v - lo) / (hi - lo)) : 0.5f;
            return y0 + 2 + Mathf.RoundToInt(t * (rowHeight - 5));
        }

        void HLine(int y, Color32 c, int w)
        {
            if (y < 0 || y * w >= _pixels.Length) return;
            for (int x = 0; x < w; x++) _pixels[y * w + x] = c;
        }

        void RowRange(float[,] hist, int row, out float lo, out float hi)
        {
            if (mode == InspectorMode.Relaxation && row > 0) { lo = 0f; hi = 1f; return; }
            lo = float.PositiveInfinity; hi = float.NegativeInfinity;
            for (int i = 0; i < _filled; i++)
            {
                float v = hist[(_head - _filled + i + historyLength) % historyLength, row];
                lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
            }
            if (mode == InspectorMode.Relaxation && driver != null) { lo = Mathf.Min(lo, driver.zLow); hi = Mathf.Max(hi, driver.zHigh); }
            if (_filled == 0) { lo = 0f; hi = 1f; }
            float pad = Mathf.Max((hi - lo) * 0.1f, 0.05f);
            lo -= pad; hi += pad;
        }

        Color32 RowColor(int row)
        {
            switch (mode)
            {
                case InspectorMode.Channels:
                case InspectorMode.RawEeg:
                    return receiver != null && receiver.HasChannelData ? QualityColors[(int)quality.Quality(row)] : QualityColors[3];
                case InspectorMode.Bands:
                    return BandColors[row];
                default:
                    return row == 2 ? new Color32(205, 165, 75, 255) : new Color32(140, 255, 150, 255);
            }
        }

        // ------------------------------------------------------------------ assets & styles

        void EnsureTextures()
        {
            if (_disk == null) _disk = Circle(32, filled: true);
            if (_ring == null) _ring = Circle(128, filled: false);
        }

        static Texture2D Circle(int size, bool filled)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f, rOuter = size * 0.5f - 1f, rInner = rOuter - 2.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = Mathf.Clamp01(rOuter - d + 0.5f);
                if (!filled) a *= Mathf.Clamp01(d - rInner + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            t.SetPixels32(px);
            t.Apply(false);
            return t;
        }

        GUIStyle _bold, _mono, _small, _centered;
        GUIStyle Bold() => _bold ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        GUIStyle Mono() => _mono ??= new GUIStyle(GUI.skin.label) { font = Font.CreateDynamicFontFromOSFont("Consolas", 12), fontSize = 12, margin = new RectOffset(4, 4, 0, 0) };
        GUIStyle Small() => _small ??= new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = new Color(1f, 1f, 1f, 0.7f) } };
        GUIStyle Centered() => _centered ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 10 };
    }
}
