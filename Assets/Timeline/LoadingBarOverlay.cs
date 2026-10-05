using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Retro terminal loading bar (green phosphor, segmented bar, blinking cursor, scanlines) drawn as a
/// screen-space overlay. Driven by <see cref="LoadingBarTrack"/> (film take 1b), or by script.
/// The UI is built on enable and never saved into the scene.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Sequence/Loading Bar Overlay")]
public sealed class LoadingBarOverlay : MonoBehaviour
{
    [Header("State (driven)")]
    [Range(0f, 1f)] public float visibility;
    [Range(0f, 1f)] public float progress;
    public string title = "CALIBRATING NEURAL BASELINE";
    public string status = "INITIALIZING ELECTRODE ARRAY";

    [Header("Look")]
    [Tooltip("Leave empty for Unity's built-in font; a monospace font sells the terminal look.")]
    public Font font;
    public Color phosphor = new Color(0.25f, 1f, 0.42f);
    [Min(4)] public int segments = 28;
    [Range(0f, 1f)] public float scanlineStrength = 0.35f;
    [Range(0f, 0.3f)] public float flicker = 0.06f;
    [Tooltip("Vertical position of the panel: 0 = bottom, 1 = top.")]
    [Range(0f, 1f)] public float verticalAnchor = 0.18f;

    GameObject _root;
    CanvasGroup _group;
    Text _title, _status, _percent;
    Image[] _cells;
    RawImage _scan;
    Texture2D _scanTex;

    void OnEnable() => Build();
    void OnDisable() => Teardown();

    void Update()
    {
        if (_root == null) Build();
        float t = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartupAsDouble;

        float jitter = 1f - flicker * Mathf.PerlinNoise(t * 9f, 0.37f);
        _group.alpha = visibility * jitter;
        bool cursor = Mathf.Repeat(t, 1f) < 0.55f;

        _title.text = "> " + title + (cursor ? "_" : " ");
        _status.text = status;
        _percent.text = $"{Mathf.FloorToInt(progress * 100f):000}%";

        float filled = progress * _cells.Length;
        for (int i = 0; i < _cells.Length; i++)
        {
            float on = i < Mathf.FloorToInt(filled) ? 1f
                : i == Mathf.FloorToInt(filled) && progress < 1f ? (cursor ? 0.55f : 0.15f)
                : 0.08f;
            _cells[i].color = new Color(phosphor.r, phosphor.g, phosphor.b, on);
        }
        _scan.color = new Color(0f, 0f, 0f, scanlineStrength);
        _scan.uvRect = new Rect(0f, t * 0.6f, 1f, 270f);
    }

    void Build()
    {
        Teardown();
        var f = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        _root = new GameObject("Loading Bar (runtime)") { hideFlags = HideFlags.HideAndDontSave };
        _root.transform.SetParent(transform, false);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        _group = _root.AddComponent<CanvasGroup>();
        _group.interactable = _group.blocksRaycasts = false;

        // Panel: dark glass band with a phosphor frame.
        var panel = Rect("Panel", _root.transform, new Vector2(0.5f, verticalAnchor), new Vector2(1100f, 210f));
        panel.gameObject.AddComponent<Image>().color = new Color(0f, 0.04f, 0.01f, 0.72f);
        var frame = panel.gameObject.AddComponent<Outline>();
        frame.effectColor = new Color(phosphor.r, phosphor.g, phosphor.b, 0.6f);
        frame.effectDistance = new Vector2(2f, -2f);

        _title = Label("Title", panel, f, 34, new Vector2(0f, 62f), TextAnchor.MiddleLeft);
        _status = Label("Status", panel, f, 24, new Vector2(0f, -66f), TextAnchor.MiddleLeft);
        _percent = Label("Percent", panel, f, 34, new Vector2(0f, 62f), TextAnchor.MiddleRight);

        // Segmented bar.
        var bar = Rect("Bar", panel, new Vector2(0.5f, 0.5f), new Vector2(1020f, 44f));
        bar.anchoredPosition = new Vector2(0f, -2f);
        var barFrame = bar.gameObject.AddComponent<Image>();
        barFrame.color = new Color(phosphor.r, phosphor.g, phosphor.b, 0.12f);
        _cells = new Image[segments];
        float gap = 6f, w = (1020f - 12f - gap * (segments - 1)) / segments;
        for (int i = 0; i < segments; i++)
        {
            var cell = Rect("Cell" + i, bar, new Vector2(0f, 0.5f), new Vector2(w, 30f));
            cell.pivot = new Vector2(0f, 0.5f);
            cell.anchoredPosition = new Vector2(6f + i * (w + gap), 0f);
            _cells[i] = cell.gameObject.AddComponent<Image>();
        }

        // Scanlines over everything in the panel.
        _scanTex = new Texture2D(1, 4, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point,
        };
        _scanTex.SetPixels(new[] { Color.white, Color.white, Color.clear, Color.clear });
        _scanTex.Apply();
        var scan = Rect("Scanlines", panel, new Vector2(0.5f, 0.5f), Vector2.zero);
        scan.anchorMin = Vector2.zero;
        scan.anchorMax = Vector2.one;
        scan.sizeDelta = Vector2.zero;
        _scan = scan.gameObject.AddComponent<RawImage>();
        _scan.texture = _scanTex;
        _scan.raycastTarget = false;
    }

    RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.sizeDelta = size;
        return rt;
    }

    Text Label(string name, Transform parent, Font f, int size, Vector2 offset, TextAnchor align)
    {
        var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(1020f, 50f));
        rt.anchoredPosition = offset;
        var t = rt.gameObject.AddComponent<Text>();
        t.font = f;
        t.fontSize = size;
        t.alignment = align;
        t.color = phosphor;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var glow = rt.gameObject.AddComponent<Shadow>();
        glow.effectColor = new Color(phosphor.r, phosphor.g, phosphor.b, 0.35f);
        glow.effectDistance = new Vector2(0f, 0f);
        return t;
    }

    void Teardown()
    {
        if (_root != null) Kill(_root);
        if (_scanTex != null) Kill(_scanTex);
        _root = null;
        _scanTex = null;
    }

    static void Kill(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
