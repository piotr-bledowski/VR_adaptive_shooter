using UnityEngine;

/// <summary>
/// Bridges the Q-learning <see cref="PlayerSkillProfile"/> to the spawn system.
///
///  • OnRoundStart()        — pick one action; its parameters are fixed for the round.
///  • ConfigureNextSpawn()  — apply those parameters before every spawn.
///  • CaptureRoundMetrics() — snapshot the round's measured stats (no learning yet).
///  • ApplyFeedback(rating) — turn the player's difficulty rating into a reward and
///                            run the Q-update. Called once the rating is known
///                            (from the human in VR, or the synthetic player offline).
/// </summary>
public class AdaptiveSpawnController : MonoBehaviour
{
    [Header("References")]
    public ShooterTargetManager targetManager;
    public ShooterRoundManager  roundManager;
    public ShooterEventLog      eventLog;

    [Header("Runtime")]
    public PlayerSkillProfile activeProfile;

    [Header("Debug (read-only in Inspector)")]
    public int    currentAction;
    public string currentActionDescription;
    public int    previousAction = -1;
    public string lastUpdateExplanation;
    public string lastRoundSummary;
    public float  lastReward;
    public int    lastRatingValue = -1;
    public string lastRatingLabel = "";

    int _stateAtSelection;
    int _roundShotCount;
    float _roundStartTime;
    int _shotsStat, _shotsMov, _shotsErr, _shotsRotating;

    int _emphasisType;
    int _rotationLevel;
    int _spawnPace;

    // Captured at round end, consumed by ApplyFeedback.
    float _capHitRate, _capAvgTTH, _capPtsPerTarget, _capShotsPerSec;
    bool  _metricsCaptured;

    static readonly string[] TYPE_NAMES = { "Stationary", "Moving", "Erratic" };
    static readonly string[] ROT_NAMES  = { "No rotation", "Slow rotation", "Medium rotation", "Fast rotation" };
    static readonly string[] PACE_NAMES = { "Fast (1-2s)", "Medium (3-4s)", "Slow (5-6s)" };

    // ── Round lifecycle ───────────────────────────────────────────────────────

    public void OnRoundStart()
    {
        if (activeProfile == null) return;

        _stateAtSelection = activeProfile.GetCurrentState();
        previousAction    = currentAction;
        currentAction     = activeProfile.SelectAction();

        PlayerSkillProfile.DecodeAction(currentAction, out _emphasisType, out _rotationLevel, out _spawnPace);
        ConfigureNextSpawn();

        currentActionDescription = $"Emphasis: {TYPE_NAMES[_emphasisType]} | " +
                                   $"{ROT_NAMES[_rotationLevel]} | Pace: {PACE_NAMES[_spawnPace]}";

        _roundShotCount = 0;
        _shotsStat = _shotsMov = _shotsErr = _shotsRotating = 0;
        _roundStartTime  = Time.time;
        _metricsCaptured = false;
    }

    /// <summary>Applies the round-fixed action parameters before each spawn.</summary>
    public void ConfigureNextSpawn()
    {
        if (targetManager == null) return;

        float r = Random.value;
        if (r < 0.6f)      targetManager.nextTargetType = (TargetType)_emphasisType;
        else if (r < 0.8f) targetManager.nextTargetType = (TargetType)((_emphasisType + 1) % 3);
        else               targetManager.nextTargetType = (TargetType)((_emphasisType + 2) % 3);

        targetManager.nextRotation = (RotationSpeed)_rotationLevel;

        switch (_spawnPace)
        {
            case 0: targetManager.spawnDelay = Random.Range(1f, 2f); break;
            case 1: targetManager.spawnDelay = Random.Range(3f, 4f); break;
            case 2: targetManager.spawnDelay = Random.Range(5f, 6f); break;
        }
    }

    /// <summary>Snapshot the round's measured metrics. No Q-update happens here.</summary>
    public void CaptureRoundMetrics()
    {
        if (activeProfile == null || roundManager == null) return;

        var s = roundManager.stats;
        _capHitRate      = s.HitRate;
        _capAvgTTH       = ComputeAvgTimeToHit();
        _capPtsPerTarget = s.totalTargetsSpawned > 0 ? (float)s.totalPoints / s.totalTargetsSpawned : 0f;
        float duration   = Time.time - _roundStartTime;
        _capShotsPerSec  = duration > 0.1f ? _roundShotCount / duration : 0f;
        _metricsCaptured = true;
    }

    /// <summary>
    /// Apply the player's difficulty rating: compute reward, run the Q-update,
    /// build the explainability strings, and persist the profile.
    /// </summary>
    public void ApplyFeedback(DifficultyRating rating)
    {
        if (activeProfile == null) return;
        if (!_metricsCaptured) CaptureRoundMetrics();

        float reward = PlayerSkillProfile.ComputeRatingReward(rating);
        lastReward      = reward;
        lastRatingValue = (int)rating;
        lastRatingLabel = PlayerSkillProfile.RatingLabel(rating);

        int actionJustPlayed = currentAction;

        activeProfile.UpdateAfterRound(
            _stateAtSelection, actionJustPlayed, rating, reward,
            _capHitRate, _capAvgTTH);

        // What will change next round (greedy intent for the new state).
        int nextAction = activeProfile.GreedyAction();
        lastUpdateExplanation = PlayerSkillProfile.ExplainActionChange(actionJustPlayed, nextAction);

        string direction = reward > 0f ? "reinforcing" : "discouraging";
        string guidance  = activeProfile.lastGuidanceNote;
        lastRoundSummary =
            $"You rated: {lastRatingLabel}\n" +
            $"Hit rate {_capHitRate:P0} | avg {_capAvgTTH:F1}s | {_capPtsPerTarget:F1} pts/target\n" +
            $"Agent {direction} \u201c{PlayerSkillProfile.DescribeAction(actionJustPlayed)}\u201d " +
            $"(reward {reward:+0.0;-0.0})" +
            (string.IsNullOrEmpty(guidance) ? "" : $"\n{guidance}");

        bool isTraining = activeProfile.playerName != null &&
                          activeProfile.playerName.StartsWith("_training_");
        if (!isTraining)
            activeProfile.Save();

        Debug.Log($"[Adaptive] {lastRoundSummary}\n{lastUpdateExplanation} | \u03b5={activeProfile.epsilon:F2}");
    }

    public void OnShotFired(TargetType targetType, bool wasRotating)
    {
        _roundShotCount++;
        switch (targetType)
        {
            case TargetType.Stationary: _shotsStat++; break;
            case TargetType.Moving:     _shotsMov++;  break;
            case TargetType.Erratic:    _shotsErr++;  break;
        }
        if (wasRotating) _shotsRotating++;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    float ComputeAvgTimeToHit()
    {
        if (eventLog == null) return 2.5f;
        var events = eventLog.Events;
        float sum  = 0f;
        int count  = 0;
        for (int i = Mathf.Max(0, events.Count - 30); i < events.Count; i++)
        {
            if (events[i].eventType == ShooterEventType.ShotHit.ToString() &&
                events[i].timeToHit > 0f)
            {
                sum += events[i].timeToHit;
                count++;
            }
        }
        return count > 0 ? sum / count : 2.5f;
    }
}
