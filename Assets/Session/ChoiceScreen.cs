using System.Collections.Generic;
using Gtec.Chain.Common.Templates.Utilities;
using Gtec.UnityInterface;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The final choice on the sync screen: the two g.tec class tags get the CSV labels and sit over the
/// video (a full-screen quad seen only by the sync camera), the training tag is hidden. A selection
/// comes from the ERP pipeline (<see cref="ERPTag.OnTagSelected"/>, possibly off the main thread)
/// or a mouse click on a tag's collider — the tags' own OnMouseDown never fires with Input System only.
/// </summary>
[AddComponentMenu("Session/Choice Screen")]
public sealed class ChoiceScreen : MonoBehaviour
{
    public ERPTag left;
    public ERPTag right;
    [Tooltip("The g.tec training tag: shown for calibration, hidden for the choice.")]
    public GameObject trainingTag;
    public Renderer videoQuad;
    public Camera syncCamera;
    public string videoTextureProperty = "_UnlitColorMap";

    /// <summary>0 = left, 1 = right, -1 = nothing selected yet.</summary>
    public int Selected => _selected;
    public bool Choosing { get; private set; }

    volatile int _selected = -1;
    TMP_Text _leftLabel, _rightLabel;
    Material _videoMat;

    void Awake()
    {
        if (left != null) _leftLabel = left.GetComponentInChildren<TMP_Text>(true);
        if (right != null) _rightLabel = right.GetComponentInChildren<TMP_Text>(true);
        if (videoQuad != null) _videoMat = videoQuad.material; // runtime instance; the asset stays untouched
        SetLabels("", "");
        HideVideo();
    }

    void OnEnable()
    {
        if (left != null) left.OnTagSelected.AddListener(OnLeft);
        if (right != null) right.OnTagSelected.AddListener(OnRight);
    }

    void OnDisable()
    {
        if (left != null) left.OnTagSelected.RemoveListener(OnLeft);
        if (right != null) right.OnTagSelected.RemoveListener(OnRight);
    }

    void OnDestroy()
    {
        if (_videoMat != null) Destroy(_videoMat);
    }

    public void SetLabels(string leftText, string rightText)
    {
        if (_leftLabel != null) _leftLabel.text = leftText;
        if (_rightLabel != null) _rightLabel.text = rightText;
    }

    /// <summary>Labels on, training tag off, <paramref name="video"/> filling the sync camera behind the tags.</summary>
    public void Show(string leftText, string rightText, Texture video)
    {
        SetLabels(leftText, rightText);
        SetTrainingTagVisible(false);
        if (videoQuad == null || video == null) return;
        _videoMat.SetTexture(videoTextureProperty, video);
        if (syncCamera != null)
        {
            // Cover the view: fit the height, unless the video is narrower than the screen, then fit the width.
            float h = syncCamera.orthographicSize * 2f, w = h * syncCamera.aspect;
            float a = video.height > 0 ? (float)video.width / video.height : 16f / 9f;
            videoQuad.transform.localScale = a >= syncCamera.aspect ? new Vector3(h * a, h, 1f) : new Vector3(w, w / a, 1f);
        }
        videoQuad.enabled = true;
    }

    public void Hide()
    {
        Choosing = false;
        SetLabels("", "");
        SetTrainingTagVisible(true);
        HideVideo();
    }

    public void BeginChoosing()
    {
        _selected = -1;
        Choosing = true;
    }

    /// <summary>Pick a side by script (debug keys, skip).</summary>
    public void Select(int side)
    {
        if (Choosing && _selected < 0) _selected = side;
    }

    void Update()
    {
        if (!Choosing || _selected >= 0 || syncCamera == null || !syncCamera.isActiveAndEnabled) return;
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        Vector2 p = syncCamera.ScreenToWorldPoint(mouse.position.ReadValue());
        var hit = Physics2D.OverlapPoint(p, 1 << syncCamera.gameObject.layer);
        var tag = hit != null ? hit.GetComponentInParent<ERPTag>() : null;
        if (tag != null && tag == left) Select(0);
        else if (tag != null && tag == right) Select(1);
    }

    void OnLeft(ERPPipeline p, ClassSelection s, List<ERPPipeline> all) => Select(0);
    void OnRight(ERPPipeline p, ClassSelection s, List<ERPPipeline> all) => Select(1);

    void HideVideo()
    {
        if (videoQuad != null) videoQuad.enabled = false;
    }

    void SetTrainingTagVisible(bool on)
    {
        if (trainingTag == null) return;
        foreach (var r in trainingTag.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
    }
}
