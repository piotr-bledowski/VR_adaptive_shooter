using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum RoundState { Idle, Active, Results }

/// <summary>
/// Central game-loop controller for the Shooter scene.
///
///   Idle    — waiting for player to press the start button; shows previous stats.
///   Active  — 30-second round, targets live, shots counted.
///   Results — targets despawn, stats panel shown, button re-enables.
///
/// Also owns the ShooterStats instance and handles near-miss detection
/// (checked synchronously at fire time, before the bullet travels).
/// </summary>
public class ShooterRoundManager : MonoBehaviour
{
    [Header("Round settings")]
    public float roundDuration  = 30f;
    [Tooltip("Seconds after round ends before targets disappear.")]
    public float despawnDelay   = 1.5f;

    [Header("Training mode")]
    [Tooltip("When true, skips disk writes and session-phase checks (used by training scene).")]
    public bool trainingMode = false;

    [Header("Near-miss")]
    [Tooltip("Half-angle of the miss cone in degrees. "
           + "Shots within this angle of a target count as a close miss.")]
    public float nearMissAngleDeg = 8f;

    [Header("References")]
    public ShooterTargetManager    targetManager;
    public ShooterHUD              hud;
    public ShooterButton           startButton;
    public ShooterEventLog         eventLog;
    public ShooterFlowAgent        flowAgent;
    public AdaptiveSpawnController adaptiveController;

    [Header("Live stats (read-only in Inspector)")]
    public ShooterStats stats = new ShooterStats();

    // ── State ─────────────────────────────────────────────────────────────────

    public RoundState CurrentState  { get; set; } = RoundState.Idle;
    public float      TimeRemaining { get; set; }

    /// <summary>All targets currently active — used for near-miss detection.</summary>
    public IReadOnlyList<ShooterTarget> ActiveTargets =>
        targetManager != null ? targetManager.ActiveTargetsList
                              : _empty;

    private static readonly List<ShooterTarget> _empty = new List<ShooterTarget>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Start()
    {
        if (startButton != null)
            startButton.OnActivated += StartRound;
        // CanActivate is owned by PlayerSessionManager (lobby) and this class (during rounds).
        hud?.SetRoundState(RoundState.Idle, null);
    }

    private float _lastMidRoundCheck;

    void Update()
    {
        if (CurrentState != RoundState.Active) return;

        TimeRemaining -= Time.deltaTime;
        hud?.SetTimer(Mathf.Max(0f, TimeRemaining));

        // Request agent decisions during active round
        flowAgent?.RequestAgentDecision();

        // Update event log active count
        if (eventLog != null && targetManager != null)
            eventLog.ActiveTargetCount = targetManager.ActiveTargetsList.Count;

        // Adaptive mid-round check every 5 seconds
        if (adaptiveController != null && Time.time - _lastMidRoundCheck >= 5f)
        {
            _lastMidRoundCheck = Time.time;
            adaptiveController.MidRoundCheck();
        }

        if (TimeRemaining <= 0f)
            EndRound();
    }

    // ── Public API ────────────────────────────────────────────────────────────

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
        _lastMidRoundCheck = Time.time;

        SetStartButtonVisible(false);
        eventLog?.BeginRound();

        // Let adaptive controller set difficulty before spawning starts
        adaptiveController?.OnRoundStart();

        targetManager?.BeginRoundSpawning();
        hud?.SetRoundState(RoundState.Active, null);
    }

    /// <summary>
    /// Called by ShooterGun immediately when the trigger is pressed.
    /// Records a shot and performs the close-miss heuristic.
    /// aimOnTarget = true means the gun's aiming beam was already pointing at
    /// a target when fired, so we skip the miss-proximity scan.
    /// </summary>
    public void HandleShotFired(Vector3 origin, Vector3 direction, bool aimOnTarget)
    {
        if (CurrentState != RoundState.Active) return;

        stats.RecordShot();
        eventLog?.LogShotFired(origin);
        adaptiveController?.OnShotFired();

        if (!aimOnTarget)
        {
            var targets = ActiveTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                ShooterTarget t = targets[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;

                Vector3 toTarget = t.transform.position - origin;
                float   angle    = Vector3.Angle(direction, toTarget);
                if (angle < nearMissAngleDeg)
                {
                    stats.RecordCloseMiss(t.targetType);
                    eventLog?.LogCloseMiss(t.targetType);
                }
            }
        }
    }

    /// <summary>
    /// Called by ShooterBullet on collision with a target.
    /// Records the hit and either schedules respawn (if round active) or
    /// simply deactivates the target.
    /// </summary>
    public void HandleBulletHit(ShooterTarget target, int points)
    {
        if (CurrentState == RoundState.Active)
        {
            float timeToHit = Mathf.Max(0f, Time.time - target.LastSpawnTime);
            stats.RecordHit(target.targetType, points, timeToHit);
            eventLog?.LogShotHit(target.targetType, points, timeToHit, target.transform.position);
            hud?.UpdateScore(stats.totalPoints, points);
            targetManager?.ScheduleRespawn(target);
        }
        else
        {
            // Round over — just hide the target
            target.gameObject.SetActive(false);
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    void EndRound()
    {
        CurrentState = RoundState.Results;
        targetManager?.StopSpawning();
        eventLog?.EndRound();
        flowAgent?.OnRoundEnd();
        adaptiveController?.OnRoundEnd();
        ExportRoundReport();
        hud?.SetRoundState(RoundState.Results, stats);
        SetStartButtonVisible(true);
        StartCoroutine(DespawnThenIdle());
    }

    void ExportRoundReport()
    {
        if (trainingMode) return; // avoid disk spam during offline training

        var session = FindObjectOfType<PlayerSessionManager>();
        string player = session != null ? session.CurrentPlayerName : "";

        ShooterRoundReport report = ShooterRoundReport.FromStats(stats, player, roundDuration);
        report.SaveToDisk();
        eventLog?.SaveToDisk(string.IsNullOrEmpty(player) ? "anonymous" : player);
        Debug.Log("[Shooter] Round report:\n" + report.ToJson());
    }

    IEnumerator DespawnThenIdle()
    {
        yield return new WaitForSeconds(despawnDelay);
        targetManager?.DespawnAllTargets();

        if (!trainingMode)
        {
            // Small grace period so the results panel is clearly shown
            yield return new WaitForSeconds(0.5f);
        }

        CurrentState = RoundState.Idle;
        SetStartButtonVisible(true);
    }

    void SetStartButtonVisible(bool visible)
    {
        if (startButton == null) return;
        startButton.gameObject.SetActive(visible);
        startButton.CanActivate = visible;
        startButton.RefreshVisual();
    }
}
