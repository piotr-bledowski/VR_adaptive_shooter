using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single key on the VR keyboard. Implements IVRPointerTarget for dual-hand clicking.
/// </summary>
public class VRKeyboardKey : MonoBehaviour, IVRPointerTarget
{
    public enum KeyAction { Character, Backspace, Space, Clear }

    [Header("Key")]
    public KeyAction action = KeyAction.Character;
    public char character = 'A';

    [Header("References")]
    public VRKeyboard keyboard;

    [Header("Colours")]
    public Color idleColor  = new Color(0.22f, 0.24f, 0.30f);
    public Color hoverColor = new Color(0.35f, 0.55f, 0.85f);
    public Color pressColor = new Color(0.15f, 0.85f, 0.35f);

    private Material  _mat;
    private TextMesh  _meshLabel;
    private Text      _uiLabel;
    private bool      _hovered;

    void Awake()
    {
        if (keyboard == null)
            keyboard = GetComponentInParent<VRKeyboard>();

        Renderer r = GetComponentInChildren<Renderer>();
        if (r != null)
        {
            _mat = r.material;
            ApplyColor(idleColor);
        }

        _meshLabel = GetComponentInChildren<TextMesh>();
        _uiLabel   = GetComponentInChildren<Text>();
        if (action == KeyAction.Character)
            SetLabelText(character.ToString());
    }

    void SetLabelText(string value)
    {
        if (_meshLabel != null) _meshLabel.text = value;
        if (_uiLabel   != null) _uiLabel.text   = value;
    }

    public void SetPointerHovered(bool hovered)
    {
        _hovered = hovered;
        ApplyColor(hovered ? hoverColor : idleColor);
    }

    public void OnPointerClick()
    {
        if (keyboard == null) return;

        ApplyColor(pressColor);

        switch (action)
        {
            case KeyAction.Character: keyboard.AppendChar(character); break;
            case KeyAction.Backspace: keyboard.Backspace();          break;
            case KeyAction.Space:     keyboard.AppendChar(' ');      break;
            case KeyAction.Clear:     keyboard.Clear();              break;
        }

        ApplyColor(_hovered ? hoverColor : idleColor);
    }

    void ApplyColor(Color c)
    {
        if (_mat != null) _mat.color = c;
    }
}
