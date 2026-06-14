using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VR-friendly HUD canvas (world-space, parented to centerEyeAnchor).
///
///   Upper-centre — round timer (red when ≤10 s)
///   Centre       — score  N  (+M last hit bonus)
///   Centre panel — round results + agent explainability
///   Bottom       — stamina bar
/// </summary>
public class ShooterHUD : MonoBehaviour
{
    [Header("References")]
    public Image  staminaFill;
    public Text   scoreText;
    public Text   playerNameText;
    public Text   timerText;
    public Text   statsText;
    public GameObject statsPanel;
    public GameObject gameplayHudRoot;

    [Header("Stamina bar colours")]
    public Color staminaColorNormal = new Color(0.15f, 0.55f, 1f);
    public Color staminaColorLow    = new Color(0.9f,  0.15f, 0.1f);
    public float lowStaminaThreshold = 0.25f;

    [Header("Score display")]
    public float bonusDisplayTime = 2f;

    ShooterPlayerController _player;
    RectTransform           _staminaFillRt;
    int   _displayedScore;
    int   _lastBonus;
    float _lastBonusTime;

    void Start()
    {
        _player = GetComponentInParent<ShooterPlayerController>();
        if (_player == null) _player = FindObjectOfType<ShooterPlayerController>();

        if (staminaFill != null)
        {
            staminaFill.color = staminaColorNormal;
            _staminaFillRt  = staminaFill.rectTransform;
            _staminaFillRt.anchorMax = Vector2.one;
        }
        if (timerText  != null) timerText.gameObject.SetActive(false);
        if (statsPanel != null) statsPanel.SetActive(false);
        SetSessionPhase(SessionPhase.PlayerSelect, "");
    }

    public void SetSessionPhase(SessionPhase phase, string playerName)
    {
        bool inGame = phase == SessionPhase.Gameplay;

        if (gameplayHudRoot != null)
            gameplayHudRoot.SetActive(inGame);

        if (playerNameText != null)
            playerNameText.text = inGame ? playerName : "";

        if (!inGame)
        {
            if (timerText != null) timerText.gameObject.SetActive(false);
            if (statsPanel != null) statsPanel.SetActive(false);
        }
    }

    void Update()
    {
        if (_player == null)
        {
            _player = FindObjectOfType<ShooterPlayerController>();
            if (_player == null) return;
        }

        if (staminaFill != null && _staminaFillRt != null)
        {
            float ratio = Mathf.Clamp01(_player.currentStamina / _player.maxStamina);
            _staminaFillRt.anchorMax = new Vector2(ratio, 1f);
            staminaFill.color = ratio < lowStaminaThreshold
                ? staminaColorLow : staminaColorNormal;
        }

        if (scoreText != null)
        {
            scoreText.text = (Time.time - _lastBonusTime < bonusDisplayTime && _lastBonus > 0)
                ? $"{_displayedScore}  (+{_lastBonus})"
                : _displayedScore.ToString();
        }
    }

    public void SetTimer(float seconds)
    {
        if (timerText == null) return;
        int s = Mathf.CeilToInt(seconds);
        timerText.text  = s.ToString("D2");
        timerText.color = seconds <= 10f ? Color.red : Color.white;
    }

    public void UpdateScore(int total, int bonus)
    {
        _displayedScore = total;
        _lastBonus      = bonus;
        _lastBonusTime  = Time.time;
    }

    /// <summary>Transition HUD to a new round state with optional agent explainability.</summary>
    public void SetRoundState(RoundState state, ShooterStats stats,
                              string agentSummary, string agentExplanation)
    {
        bool timerVisible = state == RoundState.Active;
        bool statsVisible = state == RoundState.Results;
        if (state == RoundState.Idle && statsPanel != null && statsPanel.activeSelf)
            statsVisible = true;

        if (timerText  != null) timerText.gameObject.SetActive(timerVisible);
        if (statsPanel != null) statsPanel.SetActive(statsVisible);

        if (state == RoundState.Active)
        {
            _displayedScore = 0;
            _lastBonus      = 0;
            if (scoreText != null) scoreText.text = "0";
        }

        if (state == RoundState.Results && stats != null && statsText != null)
            statsText.text = FormatStats(stats, agentSummary, agentExplanation);
    }

    public void OnScoreChanged(int newTotal, int bonus) => UpdateScore(newTotal, bonus);

    static string FormatStats(ShooterStats s, string agentSummary, string agentExplanation)
    {
        string txt =
            "══ ROUND RESULTS ══\n\n" +
            $"Shots: {s.totalShots}  Hits: {s.totalHits} ({s.HitRate:P0})" +
            $"  Expired: {s.totalExpired}\n" +
            $"Points: {s.totalPoints}  Avg/target: {(s.totalTargetsSpawned > 0 ? (float)s.totalPoints / s.totalTargetsSpawned : 0f):F1}\n\n" +
            FormatType("STATIONARY", s.stationary) +
            FormatType("MOVING", s.moving) +
            FormatType("ERRATIC", s.erratic);

        if (s.rotating.hits > 0 || s.rotating.expired > 0)
            txt += FormatType("ROTATING (all types)", s.rotating);

        if (!string.IsNullOrEmpty(agentSummary))
            txt += "\n── AGENT ──\n" + agentSummary + "\n";

        if (!string.IsNullOrEmpty(agentExplanation))
            txt += "\n── NEXT ROUND ──\n" + agentExplanation + "\n";

        txt += "\n<Press START for next round>";
        return txt;
    }

    static string FormatType(string label, TargetTypeStats ts)
    {
        return $"── {label} ──\n" +
            $" Hit: {ts.hits}/{ts.shots} ({ts.HitRate:P0})" +
            $"  Expired: {ts.expired}\n" +
            $" Pts: {ts.points}  Avg/hit: {ts.AvgPointsPerHit:F1}" +
            $"  TTH: {ts.AvgTimeToHit:F2}s\n\n";
    }
}
