using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VR-friendly HUD canvas (world-space, parented to centerEyeAnchor).
///
///   Upper-centre — round timer (shown only during active round, red when ≤10 s)
///   Centre       — score  N  (+M last hit bonus)
///   Centre panel — round results / stats (shown in Results state, stays visible in Idle)
///   Bottom-centre — stamina bar  (blue → red when low)
/// </summary>
public class ShooterHUD : MonoBehaviour
{
    [Header("References")]
    public Image  staminaFill;
    public Text   scoreText;
    public Text   playerNameText;
    public Text   timerText;
    public Text   statsText;
    public GameObject statsPanel;   // background panel that shows/hides
    public GameObject gameplayHudRoot; // score + stamina; hidden during player select

    [Header("Stamina bar colours")]
    public Color staminaColorNormal = new Color(0.15f, 0.55f, 1f);
    public Color staminaColorLow    = new Color(0.9f,  0.15f, 0.1f);
    [Tooltip("Ratio below which bar turns red (matches dashStaminaCost/maxStamina).")]
    public float lowStaminaThreshold = 0.25f;

    [Header("Score display")]
    public float bonusDisplayTime = 2f;

    // ── Private ───────────────────────────────────────────────────────────────
    private ShooterPlayerController _player;
    private RectTransform           _staminaFillRt;
    private int   _displayedScore;
    private int   _lastBonus;
    private float _lastBonusTime;

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

    /// <summary>Show/hide gameplay HUD and display the active player name.</summary>
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

        // Stamina bar — width shrinks/grows via anchor (left-anchored fill)
        if (staminaFill != null && _staminaFillRt != null)
        {
            float ratio = Mathf.Clamp01(_player.currentStamina / _player.maxStamina);
            _staminaFillRt.anchorMax = new Vector2(ratio, 1f);
            staminaFill.color = ratio < lowStaminaThreshold
                ? staminaColorLow : staminaColorNormal;
        }

        // Score text
        if (scoreText != null)
        {
            scoreText.text = (Time.time - _lastBonusTime < bonusDisplayTime && _lastBonus > 0)
                ? $"{_displayedScore}  (+{_lastBonus})"
                : _displayedScore.ToString();
        }
    }

    // ── Called by ShooterRoundManager ─────────────────────────────────────────

    /// <summary>Called every frame during active round.</summary>
    public void SetTimer(float seconds)
    {
        if (timerText == null) return;
        int s = Mathf.CeilToInt(seconds);
        timerText.text  = s.ToString("D2");
        timerText.color = seconds <= 10f ? Color.red : Color.white;
    }

    /// <summary>Called when a hit is registered.</summary>
    public void UpdateScore(int total, int bonus)
    {
        _displayedScore = total;
        _lastBonus      = bonus;
        _lastBonusTime  = Time.time;
    }

    /// <summary>Transition HUD to a new round state.</summary>
    public void SetRoundState(RoundState state, ShooterStats stats)
    {
        bool timerVisible = state == RoundState.Active;
        bool statsVisible = state == RoundState.Results;
        // Keep stats panel visible in Idle only if there were previous results
        if (state == RoundState.Idle && statsPanel != null && statsPanel.activeSelf)
            statsVisible = true; // don't hide on idle transition

        if (timerText  != null) timerText.gameObject.SetActive(timerVisible);
        if (statsPanel != null) statsPanel.SetActive(statsVisible);

        if (state == RoundState.Active)
        {
            // Reset score display for new round
            _displayedScore = 0;
            _lastBonus      = 0;
            if (scoreText != null) scoreText.text = "0";
        }

        if (state == RoundState.Results && stats != null && statsText != null)
            statsText.text = FormatStats(stats);
    }

    // Called by VRPlayerController for the Seal scene (kept for compatibility)
    public void OnScoreChanged(int newTotal, int bonus) => UpdateScore(newTotal, bonus);

    // ── Stats formatter ───────────────────────────────────────────────────────

    static string FormatStats(ShooterStats s)
    {
        return
            "══ ROUND RESULTS ══\n\n" +
            $"Shots:    {s.totalShots}\n" +
            $"Hits:     {s.totalHits}  ({s.HitRate:P0})\n" +
            $"Misses:   {s.TotalMisses}\n" +
            $"Points:   {s.totalPoints}\n" +
            $"Avg/hit:  {s.AvgPointsPerHit:F1}\n\n" +
            "── STATIONARY ──\n" +
            $" Hits: {s.stationary.hits}  " +
            $" Close misses: {s.stationary.closeMisses}\n" +
            $" Points: {s.stationary.points}" +
            $"  Avg: {s.stationary.AvgPointsPerHit:F1}\n" +
            $" Avg spawn→hit: {s.stationary.AvgTimeToHit:F2}s\n\n" +
            "── MOVING ──\n" +
            $" Hits: {s.moving.hits}  " +
            $" Close misses: {s.moving.closeMisses}\n" +
            $" Points: {s.moving.points}" +
            $"  Avg: {s.moving.AvgPointsPerHit:F1}\n" +
            $" Avg spawn→hit: {s.moving.AvgTimeToHit:F2}s\n\n" +
            "── ERRATIC ──\n" +
            $" Hits: {s.erratic.hits}  " +
            $" Close misses: {s.erratic.closeMisses}\n" +
            $" Points: {s.erratic.points}" +
            $"  Avg: {s.erratic.AvgPointsPerHit:F1}\n" +
            $" Avg spawn→hit: {s.erratic.AvgTimeToHit:F2}s\n\n" +
            "<Press START button\n for next round>";
    }
}
