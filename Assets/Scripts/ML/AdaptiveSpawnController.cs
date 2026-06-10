using UnityEngine;

/// <summary>
/// Translates Q-learning action indices into concrete spawn parameters.
/// Sits between PlayerSkillProfile (policy) and ShooterTargetManager (execution).
/// Updates live during gameplay — no Python or external training needed.
/// </summary>
public class AdaptiveSpawnController : MonoBehaviour
{
    [Header("References")]
    public ShooterTargetManager targetManager;
    public ShooterRoundManager  roundManager;
    public ShooterEventLog      eventLog;

    [Header("Runtime (assigned by PlayerSessionManager)")]
    public PlayerSkillProfile activeProfile;

    [Header("Debug")]
    public int    currentAction;
    public string currentPresetName;

    int _lastState;
    int _roundShotCount;
    float _roundStartTime;

    // ── Difficulty presets (action 0–6) ────────────────────────────────────

    struct SpawnPreset
    {
        public string name;
        public int    maxConcurrent;
        public float  minInterval, maxInterval;
        public float  stationaryRatio, movingRatio, erraticRatio;
        public float  respawnDelay;
    }

    static readonly SpawnPreset[] PRESETS = new SpawnPreset[]
    {
        new SpawnPreset { name="VeryEasy",   maxConcurrent=3,  minInterval=2.5f, maxInterval=4.0f, stationaryRatio=0.9f, movingRatio=0.1f, erraticRatio=0f,   respawnDelay=7f },
        new SpawnPreset { name="Easy",       maxConcurrent=4,  minInterval=2.0f, maxInterval=3.5f, stationaryRatio=0.7f, movingRatio=0.2f, erraticRatio=0.1f, respawnDelay=6f },
        new SpawnPreset { name="MedEasy",    maxConcurrent=5,  minInterval=1.5f, maxInterval=3.0f, stationaryRatio=0.5f, movingRatio=0.35f, erraticRatio=0.15f, respawnDelay=5f },
        new SpawnPreset { name="Medium",     maxConcurrent=6,  minInterval=1.2f, maxInterval=2.5f, stationaryRatio=0.4f, movingRatio=0.4f, erraticRatio=0.2f, respawnDelay=5f },
        new SpawnPreset { name="MedHard",    maxConcurrent=7,  minInterval=1.0f, maxInterval=2.0f, stationaryRatio=0.3f, movingRatio=0.4f, erraticRatio=0.3f, respawnDelay=4f },
        new SpawnPreset { name="Hard",       maxConcurrent=9,  minInterval=0.8f, maxInterval=1.5f, stationaryRatio=0.2f, movingRatio=0.4f, erraticRatio=0.4f, respawnDelay=3.5f },
        new SpawnPreset { name="VeryHard",   maxConcurrent=10, minInterval=0.6f, maxInterval=1.2f, stationaryRatio=0.1f, movingRatio=0.35f, erraticRatio=0.55f, respawnDelay=3f },
    };

    // ── Public API ──────────────────────────────────────────────────────────

    public void OnRoundStart()
    {
        if (activeProfile == null) return;

        _lastState = activeProfile.GetCurrentState();
        currentAction = activeProfile.SelectAction();
        ApplyPreset(currentAction);
        _roundShotCount = 0;
        _roundStartTime = Time.time;
    }

    public void OnRoundEnd()
    {
        if (activeProfile == null || eventLog == null) return;

        float hitRate = eventLog.CurrentHitRate;
        float avgTTH = ComputeAvgTimeToHit();
        float duration = Time.time - _roundStartTime;
        float shotsPerSec = duration > 0.1f ? _roundShotCount / duration : 0f;

        float reward = PlayerSkillProfile.ComputeFlowReward(hitRate, avgTTH);
        activeProfile.UpdateAfterRound(hitRate, avgTTH, shotsPerSec, currentAction, reward);

        // Skip per-round disk writes during offline training (training controller saves at end)
        bool isTrainingProfile = activeProfile.playerName != null &&
                                 activeProfile.playerName.StartsWith("_training_");
        if (!isTrainingProfile)
            activeProfile.Save();

        Debug.Log($"[Adaptive] Round end: hitRate={hitRate:F2}, TTH={avgTTH:F1}s, " +
                  $"reward={reward:F2}, action={currentAction} ({currentPresetName}), " +
                  $"epsilon={activeProfile.epsilon:F2}, rounds={activeProfile.roundsPlayed}");
    }

    public void OnShotFired()
    {
        _roundShotCount++;
    }

    /// <summary>
    /// Mid-round adjustment: if player is clearly too overwhelmed or bored,
    /// nudge the difficulty immediately (within the round).
    /// Called periodically (every 5s) during active round.
    /// </summary>
    public void MidRoundCheck()
    {
        if (activeProfile == null || eventLog == null) return;

        float elapsed = Time.time - _roundStartTime;
        if (elapsed < 8f) return; // need at least 8s of data

        float hitRate = eventLog.CurrentHitRate;

        // Emergency adjustments (don't wait for round end)
        if (hitRate < 0.15f && currentAction > 0)
        {
            currentAction--;
            ApplyPreset(currentAction);
            Debug.Log($"[Adaptive] Mid-round ease: action→{currentAction} ({currentPresetName})");
        }
        else if (hitRate > 0.85f && currentAction < PRESETS.Length - 1)
        {
            currentAction++;
            ApplyPreset(currentAction);
            Debug.Log($"[Adaptive] Mid-round push: action→{currentAction} ({currentPresetName})");
        }
    }

    // ── Internals ───────────────────────────────────────────────────────────

    void ApplyPreset(int actionIdx)
    {
        actionIdx = Mathf.Clamp(actionIdx, 0, PRESETS.Length - 1);
        var preset = PRESETS[actionIdx];
        currentPresetName = preset.name;

        if (targetManager == null) return;
        targetManager.maxConcurrentTargets = preset.maxConcurrent;
        targetManager.minSpawnInterval     = preset.minInterval;
        targetManager.maxSpawnInterval     = preset.maxInterval;
        targetManager.respawnDelay         = preset.respawnDelay;

        // Adjust pool ratios (keep total pool size, change active mix)
        int total = targetManager.stationaryCount + targetManager.movingCount + targetManager.erraticCount;
        targetManager.stationaryCount = Mathf.Max(1, Mathf.RoundToInt(total * preset.stationaryRatio));
        targetManager.movingCount     = Mathf.Max(1, Mathf.RoundToInt(total * preset.movingRatio));
        targetManager.erraticCount    = Mathf.Max(1, Mathf.RoundToInt(total * preset.erraticRatio));
    }

    float ComputeAvgTimeToHit()
    {
        if (eventLog == null) return 2.5f;
        var events = eventLog.Events;
        float sum = 0f;
        int count = 0;
        for (int i = Mathf.Max(0, events.Count - 30); i < events.Count; i++)
        {
            if (events[i].eventType == ShooterEventType.ShotHit.ToString() && events[i].timeToHit > 0f)
            {
                sum += events[i].timeToHit;
                count++;
            }
        }
        return count > 0 ? sum / count : 2.5f;
    }
}
