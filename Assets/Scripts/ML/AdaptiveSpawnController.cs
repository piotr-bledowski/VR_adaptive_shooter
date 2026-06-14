using UnityEngine;

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

    int _lastState;
    int _roundShotCount;
    float _roundStartTime;
    int _shotsStat, _shotsMov, _shotsErr, _shotsRotating;

    int _emphasisType;
    int _rotationLevel;
    int _spawnPace;

    static readonly string[] TYPE_NAMES = {"Stationary", "Moving", "Erratic"};
    static readonly string[] ROT_NAMES  = {"No rotation", "Slow rotation", "Medium rotation", "Fast rotation"};
    static readonly string[] PACE_NAMES = {"Fast (1-2s)", "Medium (3-4s)", "Slow (5-6s)"};

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called once at the start of every round.
    /// The Q-learning agent picks an action here; those parameters are then
    /// held fixed for the entire round — no mid-round overrides.
    /// </summary>
    public void OnRoundStart()
    {
        if (activeProfile == null) return;

        _lastState     = activeProfile.GetCurrentState();
        previousAction = currentAction;
        currentAction  = activeProfile.SelectAction();

        PlayerSkillProfile.DecodeAction(currentAction, out _emphasisType, out _rotationLevel, out _spawnPace);
        ConfigureNextSpawn();

        currentActionDescription = $"Emphasis: {TYPE_NAMES[_emphasisType]} | " +
                                   $"{ROT_NAMES[_rotationLevel]} | " +
                                   $"Pace: {PACE_NAMES[_spawnPace]}";

        _roundShotCount  = 0;
        _shotsStat = _shotsMov = _shotsErr = _shotsRotating = 0;
        _roundStartTime  = Time.time;
    }

    /// <summary>
    /// Called by ShooterTargetManager before every target spawn.
    /// Uses the fixed action parameters chosen at round start.
    /// </summary>
    public void ConfigureNextSpawn()
    {
        if (targetManager == null) return;

        float r = Random.value;
        if (r < 0.6f)
            targetManager.nextTargetType = (TargetType)_emphasisType;
        else if (r < 0.8f)
            targetManager.nextTargetType = (TargetType)((_emphasisType + 1) % 3);
        else
            targetManager.nextTargetType = (TargetType)((_emphasisType + 2) % 3);

        targetManager.nextRotation = (RotationSpeed)_rotationLevel;

        switch (_spawnPace)
        {
            case 0: targetManager.spawnDelay = Random.Range(1f, 2f); break;
            case 1: targetManager.spawnDelay = Random.Range(3f, 4f); break;
            case 2: targetManager.spawnDelay = Random.Range(5f, 6f); break;
        }
    }

    public void OnRoundEnd()
    {
        if (activeProfile == null) return;

        var s = roundManager.stats;
        float hitRate     = s.HitRate;
        float avgTTH      = ComputeAvgTimeToHit();
        float duration    = Time.time - _roundStartTime;
        float shotsPerSec = duration > 0.1f ? _roundShotCount / duration : 0f;

        float hrStat = s.stationary.HitRate;
        float hrMov  = s.moving.HitRate;
        float hrErr  = s.erratic.HitRate;
        float tthStat = s.stationary.AvgTimeToHit;
        float tthMov  = s.moving.AvgTimeToHit;
        float tthErr  = s.erratic.AvgTimeToHit;
        float ptsStat = s.stationary.AvgPointsPerHit;
        float ptsMov  = s.moving.AvgPointsPerHit;
        float ptsErr  = s.erratic.AvgPointsPerHit;
        float hrRot   = s.rotating.HitRate;
        float ptsPerTarget = s.totalTargetsSpawned > 0
            ? (float)s.totalPoints / s.totalTargetsSpawned : 0f;

        float reward = PlayerSkillProfile.ComputeFlowReward(
            hitRate, avgTTH, ptsPerTarget, hrStat, hrMov, hrErr);

        previousAction = currentAction;
        activeProfile.UpdateAfterRound(
            hitRate, avgTTH, shotsPerSec,
            hrStat, hrMov, hrErr,
            tthStat, tthMov, tthErr,
            ptsStat, ptsMov, ptsErr,
            hrRot, ptsPerTarget,
            currentAction, reward);

        int nextAction = activeProfile.SelectAction();
        lastUpdateExplanation = PlayerSkillProfile.ExplainActionChange(currentAction, nextAction);

        lastRoundSummary =
            $"Hit: {hitRate:P0} | TTH: {avgTTH:F1}s | Pts/target: {ptsPerTarget:F1}\n" +
            $"Stat: {hrStat:P0} | Mov: {hrMov:P0} | Err: {hrErr:P0}\n" +
            $"Rot: {hrRot:P0} | Reward: {reward:F2} | \u03b5: {activeProfile.epsilon:F2}";

        bool isTraining = activeProfile.playerName != null &&
                          activeProfile.playerName.StartsWith("_training_");
        if (!isTraining)
            activeProfile.Save();

        Debug.Log($"[Adaptive] {lastRoundSummary}\n{lastUpdateExplanation}");
    }

    public void OnShotFired(TargetType targetType, bool wasRotating)
    {
        _roundShotCount++;
        switch (targetType)
        {
            case TargetType.Stationary: _shotsStat++;    break;
            case TargetType.Moving:     _shotsMov++;     break;
            case TargetType.Erratic:    _shotsErr++;     break;
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
