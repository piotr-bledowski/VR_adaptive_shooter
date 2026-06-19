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
    public Text   agentText;
    public GameObject statsPanel;
    public GameObject gameplayHudRoot;

    [Header("Stamina bar")]
    public bool  showStaminaBar = false;
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
            if (!showStaminaBar)
            {
                // Hide the whole bar (background + fill) and stop tracking it.
                Transform barRoot = staminaFill.transform.parent != null
                    ? staminaFill.transform.parent : staminaFill.transform;
                barRoot.gameObject.SetActive(false);
                staminaFill = null;
            }
            else
            {
                staminaFill.color = staminaColorNormal;
                _staminaFillRt  = staminaFill.rectTransform;
                _staminaFillRt.anchorMax = Vector2.one;
            }
        }
        if (timerText  != null) timerText.gameObject.SetActive(false);
        if (statsPanel != null) statsPanel.SetActive(false);
        EnsureSplitResultsLayout();
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

        if (showStaminaBar && staminaFill != null && _staminaFillRt != null)
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

        if (state == RoundState.Results && stats != null)
            UpdateResultsPanel(stats, agentSummary, agentExplanation);
    }

    void UpdateResultsPanel(ShooterStats stats, string agentSummary, string agentExplanation)
    {
        if (statsText != null)
            statsText.text = FormatRoundStats(stats);

        string agentPanel = FormatAgentPanel(agentSummary, agentExplanation);
        if (agentText != null)
            agentText.text = agentPanel;
        else if (statsText != null)
            statsText.text += "\n\n" + agentPanel;
    }

    /// <summary>
    /// Older scenes had one tall stats column; upgrade to a left/right split at runtime.
    /// </summary>
    void EnsureSplitResultsLayout()
    {
        if (statsPanel == null || statsText == null || agentText != null) return;

        RectTransform panelRt = statsPanel.GetComponent<RectTransform>();
        if (panelRt != null)
        {
            panelRt.sizeDelta        = new Vector2(720f, 420f);
            panelRt.anchoredPosition = new Vector2(-55f, -10f);
        }

        RectTransform leftRt = statsText.rectTransform;
        leftRt.anchorMin = new Vector2(0f, 0f);
        leftRt.anchorMax = new Vector2(0.48f, 1f);
        leftRt.offsetMin = new Vector2(12f, 12f);
        leftRt.offsetMax = new Vector2(-6f, -12f);
        statsText.alignment = TextAnchor.UpperLeft;
        statsText.fontSize  = 18;
        statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statsText.verticalOverflow   = VerticalWrapMode.Overflow;

        GameObject agentObj = new GameObject("AgentText");
        agentObj.transform.SetParent(statsPanel.transform, false);
        agentText = agentObj.AddComponent<Text>();
        agentText.font      = statsText.font;
        agentText.fontSize  = 18;
        agentText.alignment = TextAnchor.UpperLeft;
        agentText.color     = new Color(0.82f, 0.94f, 1f);
        agentText.horizontalOverflow = HorizontalWrapMode.Wrap;
        agentText.verticalOverflow   = VerticalWrapMode.Overflow;

        RectTransform rightRt = agentObj.GetComponent<RectTransform>();
        rightRt.anchorMin = new Vector2(0.52f, 0f);
        rightRt.anchorMax = new Vector2(1f, 1f);
        rightRt.offsetMin = new Vector2(6f, 12f);
        rightRt.offsetMax = new Vector2(-12f, -12f);
    }

    public void OnScoreChanged(int newTotal, int bonus) => UpdateScore(newTotal, bonus);

    static string FormatRoundStats(ShooterStats s)
    {
        string txt =
            "ROUND RESULTS\n\n" +
            $"Shots: {s.totalShots}  Hits: {s.totalHits} ({s.HitRate:P0})\n" +
            $"Expired: {s.totalExpired}\n" +
            $"Points: {s.totalPoints}  Avg/target: " +
            $"{(s.totalTargetsSpawned > 0 ? (float)s.totalPoints / s.totalTargetsSpawned : 0f):F1}\n\n" +
            FormatType("STATIONARY", s.stationary) +
            FormatType("MOVING", s.moving) +
            FormatType("ERRATIC", s.erratic);

        if (s.rotating.hits > 0 || s.rotating.expired > 0)
            txt += FormatType("ROTATING", s.rotating);

        return txt;
    }

    static string FormatAgentPanel(string agentSummary, string agentExplanation)
    {
        if (string.IsNullOrEmpty(agentSummary))
        {
            return "HOW DID THAT FEEL?\n\n" +
                   "Shoot a rating button\n" +
                   "along the firing line:\n\n" +
                   "Too Easy · Easy · Perfect\n" +
                   "Hard · Too Hard";
        }

        string txt = "AGENT UPDATE\n\n" + agentSummary;
        if (!string.IsNullOrEmpty(agentExplanation))
            txt += "\n\n" + agentExplanation;
        txt += "\n\nPress START for next round";
        return txt;
    }

    static string FormatStats(ShooterStats s, string agentSummary, string agentExplanation)
    {
        return FormatRoundStats(s) + "\n\n" + FormatAgentPanel(agentSummary, agentExplanation);
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
