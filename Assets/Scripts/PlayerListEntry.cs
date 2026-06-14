using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One saved-player button in the lobby list. Clicking selects that name.
/// </summary>
public class PlayerListEntry : MonoBehaviour, IVRPointerTarget
{
    public string playerName;

    [Header("Colours")]
    public Color idleColor    = new Color(0.18f, 0.22f, 0.32f);
    public Color hoverColor   = new Color(0.30f, 0.50f, 0.80f);
    public Color selectedColor = new Color(0.15f, 0.65f, 0.25f);

    public event Action<string> OnSelected;

    private Material _mat;
    private TextMesh _meshLabel;
    private Text     _uiLabel;
    private bool     _hovered;
    private bool     _selected;

    void Awake()
    {
        Renderer r = GetComponentInChildren<Renderer>();
        if (r != null)
        {
            _mat = r.material;
            ApplyColor(idleColor);
        }

        _meshLabel = GetComponentInChildren<TextMesh>();
        _uiLabel   = GetComponentInChildren<Text>();
        FixLabelMirror(playerName);
        if (!string.IsNullOrEmpty(playerName))
            SetLabelText(playerName);
    }

    public void Setup(string name, Action<string> onSelected)
    {
        playerName = name;
        OnSelected = onSelected;
        FixLabelMirror(name);
        SetLabelText(name);
        SetSelected(false);
    }

    /// <summary>
    /// Label on the +Z face of the button body. The list container uses Euler(0,180,0) so
    /// its local +X = world -X.  A 180° Y rotation on the label then makes its -Z face the
    /// player AND its +X map back to world +X, giving correct left-to-right text without
    /// any additional -X scale flip.
    /// </summary>
    void FixLabelMirror(string nameForSizing = null)
    {
        if (_meshLabel == null) return;

        Transform body = transform.Find("Body");
        float depth = body != null ? body.localScale.z : 0.05f;
        float width = body != null ? body.localScale.x : 0.75f;

        Transform label = _meshLabel.transform;
        label.localPosition = new Vector3(0f, 0f, depth * 0.5f + 0.004f);
        label.localRotation = Quaternion.Euler(0f, 180f, 0f);
        label.localScale    = Vector3.one;

        string text = nameForSizing ?? _meshLabel.text;
        if (!string.IsNullOrEmpty(text))
        {
            int   len      = Mathf.Max(1, text.Length);
            float fitSize  = (width * 0.72f) / (len * 0.58f);
            _meshLabel.characterSize = Mathf.Min(0.012f, fitSize);
        }
    }

    void SetLabelText(string value)
    {
        if (_meshLabel != null) _meshLabel.text = value;
        if (_uiLabel   != null) _uiLabel.text   = value;
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        ApplyColor(_selected ? selectedColor : (_hovered ? hoverColor : idleColor));
    }

    public void SetPointerHovered(bool hovered)
    {
        _hovered = hovered;
        if (!_selected)
            ApplyColor(hovered ? hoverColor : idleColor);
    }

    public void OnPointerClick()
    {
        OnSelected?.Invoke(playerName);
    }

    void ApplyColor(Color c)
    {
        if (_mat != null) _mat.color = c;
    }
}
