using System;
using UnityEngine;

/// <summary>
/// Place on the start-button GameObject.
/// ShooterInteractSystem finds this via raycast and calls TryActivate().
/// </summary>
public class ShooterButton : MonoBehaviour, IVRPointerTarget
{
    [Header("Colours")]
    public Color idleColor    = new Color(0.15f, 0.65f, 0.15f);
    public Color hoverColor   = new Color(1f,    0.90f, 0.10f);
    public Color disabledColor = new Color(0.5f, 0.15f, 0.10f);

    /// <summary>Set false while a round is in progress.</summary>
    public bool CanActivate { get; set; } = true;

    public event Action OnActivated;

    private Material _mat;

    void Start()
    {
        Renderer r = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();
        if (r != null)
        {
            _mat = r.material; // instance copy
            ApplyColor(false);
        }
    }

    /// <summary>Called by ShooterInteractSystem every frame with current hover state.</summary>
    public void SetHovered(bool hovered) => SetPointerHovered(hovered);

    public void SetPointerHovered(bool hovered) => ApplyColor(hovered);

    /// <summary>Called by ShooterInteractSystem on trigger press.</summary>
    public void TryActivate() => OnPointerClick();

    public void OnPointerClick()
    {
        // Always notify listeners — they validate (e.g. empty name, round already active).
        // CanActivate only controls the visual disabled state.
        OnActivated?.Invoke();
    }

    public void RefreshVisual() => ApplyColor(_hovered);

    bool _hovered;

    void ApplyColor(bool hovered)
    {
        _hovered = hovered;
        if (_mat == null) return;
        _mat.color = !CanActivate ? disabledColor
                   : hovered      ? hoverColor
                                  : idleColor;
    }
}
