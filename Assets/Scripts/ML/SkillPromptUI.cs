using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VR skill prompt shown when a new player enters their name for the first time.
/// Asks "How experienced are you?" with 3 buttons.
/// The chosen level seeds the initial EMA estimates and Q-table bias
/// (so the agent starts in a reasonable region of action space), but the
/// Q-table is always freshly initialised — it learns purely from this player's play.
/// </summary>
public class SkillPromptUI : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject panelRoot;
    public Text       titleText;
    public Text       descriptionText;
    public ShooterButton beginnerButton;
    public ShooterButton intermediateButton;
    public ShooterButton advancedButton;

    public event Action<PlayerSkillLevel> OnSkillSelected;

    string _pendingPlayerName;
    Action<PlayerSkillLevel> _callback;

    void Awake()
    {
        // Hide the canvas panel on wake; the SkillPromptUI component itself stays enabled
        // so Start() always runs and wires the button listeners correctly.
        if (panelRoot != null) panelRoot.SetActive(false);

        // Hook up button listeners here (not in Start) so they are ready the same frame
        // that Show() is called, regardless of script-execution-order edge cases.
        if (beginnerButton != null)
            beginnerButton.OnActivated += () => Select(PlayerSkillLevel.Beginner);
        if (intermediateButton != null)
            intermediateButton.OnActivated += () => Select(PlayerSkillLevel.Intermediate);
        if (advancedButton != null)
            advancedButton.OnActivated += () => Select(PlayerSkillLevel.Advanced);
    }

    void Start() { /* listeners already wired in Awake */ }

    public void Show(string playerName, Action<PlayerSkillLevel> callback)
    {
        _pendingPlayerName = playerName;
        _callback = callback;

        if (titleText != null)
            titleText.text = $"Welcome, {playerName}!";
        if (descriptionText != null)
            descriptionText.text = "How experienced are you with shooting games?\nThis helps us set the right difficulty.";

        if (panelRoot != null) panelRoot.SetActive(true);
        SetButtonsActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    void Select(PlayerSkillLevel level)
    {
        SetButtonsActive(false);
        Hide();

        Debug.Log($"[SkillPrompt] Player '{_pendingPlayerName}' selected: {level}");
        OnSkillSelected?.Invoke(level);
        _callback?.Invoke(level);
        _callback = null;
    }

    void SetButtonsActive(bool active)
    {
        if (beginnerButton != null)     { beginnerButton.CanActivate = active; beginnerButton.RefreshVisual(); }
        if (intermediateButton != null)  { intermediateButton.CanActivate = active; intermediateButton.RefreshVisual(); }
        if (advancedButton != null)     { advancedButton.CanActivate = active; advancedButton.RefreshVisual(); }
    }
}
