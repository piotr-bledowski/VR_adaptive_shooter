using System;
using UnityEngine;

/// <summary>
/// Manages typed text for the player-name entry screen.
/// Keys call AppendChar / Backspace / Clear via VRKeyboardKey components.
/// </summary>
public class VRKeyboard : MonoBehaviour
{
    [Header("Settings")]
    public int maxLength = 16;

    public string Text { get; private set; } = "";

    public event Action<string> OnTextChanged;

    public void SetText(string value, bool notify = true)
    {
        Text = string.IsNullOrEmpty(value)
            ? ""
            : value.Substring(0, Mathf.Min(value.Length, maxLength));

        if (notify)
            OnTextChanged?.Invoke(Text);
    }

    public void AppendChar(char c)
    {
        if (Text.Length >= maxLength) return;
        SetText(Text + char.ToUpperInvariant(c));
    }

    public void Backspace()
    {
        if (Text.Length == 0) return;
        SetText(Text.Substring(0, Text.Length - 1));
    }

    public void Clear()
    {
        SetText("");
    }
}
