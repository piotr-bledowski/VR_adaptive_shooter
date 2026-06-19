using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum RoundState { Idle, Active, Results }

/// <summary>
/// Central game-loop controller for the Shooter scene.
///
///   Idle    — waiting for player to press the start button.
///   Active  — 30-second round, targets spawn sequentially, shots counted.
///   Results — targets despawn, stats panel shown, button re-enables.
/// </summary>
public class ShooterRoundManager : MonoBehaviour
{
    [Header("Round settings")]
    public float roundDuration  = 30f;
    [Tooltip("Seconds after round ends before targets disappear.")]
    public float despawnDelay   = 1.5f;

    [Header("Training mode")]
    [Tooltip("When true, skips disk writes and session-phase checks.")]
    public bool trainingMode = false;

    [Header("Near-miss")]
    [Tooltip("Half-angle of the miss cone in degrees.")]
    public float nearMissAngleDeg = 8f;

    [Header("References")]
    public ShooterTargetManager    targetManager;
    public ShooterHUD              hud;
    public ShooterButton           startButton;
    public ShooterEventLog         eventLog;
    public ShooterFlowAgent        flowAgent;
    public AdaptiveSpawnController adaptiveController;
    public DifficultyFeedbackUI    feedbackUI;

    [Header("Live stats (read-only in Inspector)")]
    public ShooterStats stats = new ShooterStats();

    public RoundState CurrentState  { get; set; } = RoundState.Idle;
    public float      TimeRemaining { get; set; }

    /// <summary>True between round end and the player submitting a difficulty rating.</summary>
    public bool AwaitingFeedback { get; private set; }

    public IReadOnlyList<ShooterTarget> ActiveTargets =>
        targetManager != null ? targetManager.ActiveTargetsList
                              : _empty;

    static readonly List<ShooterTarget> _empty = new List<ShooterTarget>();

    void Start()
    {
        if (startButton != null)
            startButton.OnActivated += StartRound;
        hud?.SetRoundState(RoundState.Idle, null, null, null);
    }

    void Update()
    {
        if (CurrentState != RoundState.Active) return;

        TimeRemaining -= Time.deltaTime;
        hud?.SetTimer(Mathf.Max(0f, TimeRemaining));

        flowAgent?.RequestAgentDecision();

        if (eventLog != null && targetManager != null)
            eventLog.ActiveTargetCount = targetManager.ActiveTargetsList.Count;

        if (TimeRemaining <= 0f)
            EndRound();
    }

    // ── Public API ──────────────────────────────────────────────────────────

    public void StartRound()
    {
        if (CurrentState == RoundState.Active) return;

        if (!trainingMode)
        {
            var session = FindObjectOfType<PlayerSessionManager>();
            if (session != null && session.Phase != SessionPhase.Gameplay)
                return;
        }

        stats.Reset();
        TimeRemaining = roundDuration;
        CurrentState  = RoundState.Active;

        SetStartButtonVisible(false);
        eventLog?.BeginRound();
        adaptiveController?.OnRoundStart();
        targetManager?.BeginRoundSpawning();
        hud?.SetRoundState(RoundState.Active, null, null, null);
    }

    /// <summary>
    /// Called by ShooterGun when the trigger is pressed.
    /// In the new sequential model, we assume the player is shooting at the
    /// oldest active target for per-type shot attribution.
    /// </summary>
    public void HandleShotFired(Vector3 origin, Vector3 direction, bool aimOnTarget)
    {
        if (CurrentState != RoundState.Active) return;

        // Attribute shot to oldest target's type (sequential assumption)
        TargetType shotType = TargetType.Stationary;
        bool shotAtRotating = false;
        var targets = ActiveTargets;
        if (targets.Count > 0)
        {
            ShooterTarget oldest = targets[0];
            for (int i = 1; i < targets.Count; i++)
            {
                if (targets[i].LastSpawnTime < oldest.LastSpawnTime)
                    oldest = targets[i];
            }
            shotType = oldest.targetType;
            shotAtRotating = oldest.rotationSpeed != RotationSpeed.None;
        }

        stats.RecordShot(shotType, shotAtRotating);
        eventLog?.LogShotFired(origin);
        adaptiveController?.OnShotFired(shotType, shotAtRotating);

        if (!aimOnTarget)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                ShooterTarget t = targets[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;

                Vector3 toTarget = t.transform.position - origin;
                float   angle    = Vector3.Angle(direction, toTarget);
                if (angle < nearMissAngleDeg)
                {
                    stats.RecordCloseMiss(t.targetType, t.rotationSpeed != RotationSpeed.None);
                    eventLog?.LogCloseMiss(t.targetType);
                }
            }
        }
    }

    /// <summary>Called by ShooterBullet on collision with a target.</summary>
    public void HandleBulletHit(ShooterTarget target, int points)
    {
        if (CurrentState == RoundState.Active)
        {
            float timeToHit = Mathf.Max(0f, Time.time - target.LastSpawnTime);
            bool wasRotating = target.rotationSpeed != RotationSpeed.None;
            stats.RecordHit(target.targetType, wasRotating, points, timeToHit);
            eventLog?.LogShotHit(target.targetType, points, timeToHit, target.transform.position);
            hud?.UpdateScore(stats.totalPoints, points);
            targetManager?.ScheduleRespawn(target);
        }
        else
        {
            target.gameObject.SetActive(false);
        }
    }

    /// <summary>Called by ShooterTargetManager when a target's lifespan expires.</summary>
    public void HandleTargetExpired(ShooterTarget target)
    {
        if (CurrentState != RoundState.Active) return;
        bool wasRotating = target.rotationSpeed != RotationSpeed.None;
        stats.RecordExpired(target.targetType, wasRotating);
        eventLog?.LogTargetDespawned(target.targetType);
    }

    // ── Private ─────────────────────────────────────────────────────────────

    void EndRound()
    {
        CurrentState = RoundState.Results;
        targetManager?.StopSpawning();
        eventLog?.EndRound();
        flowAgent?.OnRoundEnd();

        // Snapshot the round's metrics, but DON'T learn yet — the Q-update waits
        // until the player rates the round's difficulty.
        adaptiveController?.CaptureRoundMetrics();

        // Show the round stats immediately; the agent's verdict is appended once rated.
        hud?.SetRoundState(RoundState.Results, stats, null, null);

        AwaitingFeedback = true;
        SetStartButtonVisible(false);

        if (!trainingMode)
            feedbackUI?.Show(this);
        // In training mode the offline controller calls SubmitDifficultyRating(...).
    }

    /// <summary>
    /// Submit the player's difficulty rating for the round that just ended.
    /// Triggers the deferred Q-learning update and re-opens the start button.
    /// Called by DifficultyFeedbackUI (VR) or the offline controllers (synthetic).
    /// </summary>
    public void SubmitDifficultyRating(DifficultyRating rating)
    {
        if (!AwaitingFeedback) return;
        AwaitingFeedback = false;

        adaptiveController?.ApplyFeedback(rating);

        if (!trainingMode)
        {
            feedbackUI?.Hide();
            string summary = adaptiveController?.lastRoundSummary;
            string explain = adaptiveController?.lastUpdateExplanation;
            hud?.SetRoundState(RoundState.Results, stats, summary, explain);
            ExportRoundReport();
        }

        targetManager?.DespawnAllTargets();
        CurrentState = RoundState.Idle;
        SetStartButtonVisible(true);
    }

    void ExportRoundReport()
    {
        if (trainingMode) return;

        var session = FindObjectOfType<PlayerSessionManager>();
        string player = session != null ? session.CurrentPlayerName : "";

        ShooterRoundReport report = ShooterRoundReport.FromStats(stats, player, roundDuration);
        report.SaveToDisk();
        eventLog?.SaveToDisk(string.IsNullOrEmpty(player) ? "anonymous" : player);
    }

    void SetStartButtonVisible(bool visible)
    {
        if (startButton == null) return;
        startButton.gameObject.SetActive(visible);
        startButton.CanActivate = visible;
        startButton.RefreshVisual();
    }
}
