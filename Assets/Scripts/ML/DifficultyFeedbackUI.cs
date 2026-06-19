using UnityEngine;

/// <summary>
/// After every VR round the player rates the difficulty on a 5-point scale by
/// shooting one of five buttons. The choice is forwarded to the round manager,
/// which runs the deferred Q-learning update.
///
/// Buttons are ordered left→right: Too Easy · Easy · Perfect · Hard · Too Hard.
/// </summary>
public class DifficultyFeedbackUI : MonoBehaviour
{
    [Header("Root (shown only while awaiting a rating)")]
    public GameObject panelRoot;

    [Header("Rating buttons (left → right)")]
    public ShooterButton tooEasyButton;
    public ShooterButton easyButton;
    public ShooterButton perfectButton;
    public ShooterButton hardButton;
    public ShooterButton tooHardButton;

    ShooterRoundManager _roundManager;

    void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);

        Wire(tooEasyButton, DifficultyRating.TooEasy);
        Wire(easyButton,    DifficultyRating.Easy);
        Wire(perfectButton, DifficultyRating.Perfect);
        Wire(hardButton,    DifficultyRating.Hard);
        Wire(tooHardButton, DifficultyRating.TooHard);
    }

    void Wire(ShooterButton btn, DifficultyRating rating)
    {
        if (btn != null) btn.OnActivated += () => Choose(rating);
    }

    public void Show(ShooterRoundManager roundManager)
    {
        _roundManager = roundManager;
        if (panelRoot != null) panelRoot.SetActive(true);
        SetButtonsActive(true);
    }

    public void Hide()
    {
        SetButtonsActive(false);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    void Choose(DifficultyRating rating)
    {
        if (_roundManager == null) return;
        SetButtonsActive(false);
        _roundManager.SubmitDifficultyRating(rating);
    }

    void SetButtonsActive(bool active)
    {
        SetBtn(tooEasyButton, active);
        SetBtn(easyButton,    active);
        SetBtn(perfectButton, active);
        SetBtn(hardButton,    active);
        SetBtn(tooHardButton, active);
    }

    static void SetBtn(ShooterButton b, bool active)
    {
        if (b == null) return;
        b.CanActivate = active;
        b.RefreshVisual();
    }
}
