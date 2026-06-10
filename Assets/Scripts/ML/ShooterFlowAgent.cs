using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

/// <summary>
/// ML-Agents Agent that controls target spawning to maintain player "flow".
///
/// OBSERVATIONS (per step, fed to LSTM):
///   - Time remaining (normalised 0–1)
///   - Active target count (normalised 0–1 over maxTargets)
///   - Player hit rate this round (0–1)
///   - Avg time-to-hit recent window (normalised)
///   - Recent event buffer: last 10 events encoded as [type, targetType, timeToHit, posX, posZ]
///   - Total observations: 4 + 10*5 = 54 floats
///
/// ACTIONS (multi-discrete):
///   Branch 0: Spawn? (0=no, 1=yes)
///   Branch 1: Target type (0=stationary, 1=moving, 2=erratic)
///   Branch 2: X zone (0=left, 1=center-left, 2=center, 3=center-right, 4=right)
///   Branch 3: Z zone (0=front, 1=middle, 2=back)
///   Branch 4: Y zone (0=low, 1=mid, 2=high)
///
/// REWARD (per step):
///   - Positive when active target count is near idealTargetCount (3)
///   - Bonus for player hitting targets in a reasonable time window
///   - Penalty for overwhelming (too many targets) or trivial (too few)
///   - End-of-episode bonus for balanced hit rate (40–70%)
/// </summary>
public class ShooterFlowAgent : Agent
{
    [Header("References")]
    public ShooterTargetManager targetManager;
    public ShooterRoundManager  roundManager;
    public ShooterEventLog      eventLog;

    [Header("Flow parameters")]
    [Tooltip("Ideal number of active targets. Agent is rewarded for maintaining this.")]
    public int   idealTargetCount    = 3;
    public float idealHitRate        = 0.55f;
    public float idealTimeToHitSec   = 2.5f;
    public float decisionIntervalSec = 1.0f;

    [Header("Spawn grid (world-space bounds)")]
    public Vector3 spawnAreaMin = new Vector3(-11f, 1.5f, 12f);
    public Vector3 spawnAreaMax = new Vector3( 11f, 6f,   32f);

    [Header("Agent state")]
    public bool isTraining = false;

    private float _lastDecisionTime;
    private float _episodeRewardAccum;
    private int   _stepsThisEpisode;
    private List<ShooterEvent> _recentEvents = new List<ShooterEvent>();

    const int RECENT_EVENT_WINDOW = 10;
    const int EVENT_FEATURES      = 5;

    // ── ML-Agents Lifecycle ───────────────────────────────────────────────────

    public override void OnEpisodeBegin()
    {
        _lastDecisionTime    = Time.time;
        _episodeRewardAccum  = 0f;
        _stepsThisEpisode    = 0;
        _recentEvents.Clear();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        float timeRemaining = roundManager != null ? roundManager.TimeRemaining / 30f : 0f;
        float activeCount   = eventLog != null ? eventLog.ActiveTargetCount / 12f : 0f;
        float hitRate       = eventLog != null ? eventLog.CurrentHitRate : 0f;
        float avgTimeToHit  = ComputeRecentAvgTimeToHit() / 10f;

        sensor.AddObservation(timeRemaining);
        sensor.AddObservation(activeCount);
        sensor.AddObservation(hitRate);
        sensor.AddObservation(avgTimeToHit);

        var events = eventLog != null ? eventLog.Events : _recentEvents;
        int start = Mathf.Max(0, events.Count - RECENT_EVENT_WINDOW);
        for (int i = 0; i < RECENT_EVENT_WINDOW; i++)
        {
            int idx = start + i;
            if (idx < events.Count)
            {
                ShooterEvent e = events[idx];
                sensor.AddObservation(EncodeEventType(e.eventType) / 8f);
                sensor.AddObservation(EncodeTargetType(e.targetType) / 2f);
                sensor.AddObservation(Mathf.Clamp01(e.timeToHit / 10f));
                sensor.AddObservation((e.targetPosition.x - spawnAreaMin.x) /
                    Mathf.Max(1f, spawnAreaMax.x - spawnAreaMin.x));
                sensor.AddObservation((e.targetPosition.z - spawnAreaMin.z) /
                    Mathf.Max(1f, spawnAreaMax.z - spawnAreaMin.z));
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        _stepsThisEpisode++;

        int shouldSpawn  = actions.DiscreteActions[0]; // 0=no, 1=yes
        int targetTypeId = actions.DiscreteActions[1]; // 0–2
        int xZone        = actions.DiscreteActions[2]; // 0–4
        int zZone        = actions.DiscreteActions[3]; // 0–2
        int yZone        = actions.DiscreteActions[4]; // 0–2

        if (shouldSpawn == 1 && targetManager != null)
        {
            TargetType type = (TargetType)Mathf.Clamp(targetTypeId, 0, 2);
            Vector3 position = GridToWorldPosition(xZone, yZone, zZone);

            bool spawned = SpawnTargetAtPosition(type, position);
            if (spawned)
                eventLog?.LogAgentDecision(type, position);
        }

        // ── Reward computation ────────────────────────────────────────────────
        float reward = ComputeStepReward();
        _episodeRewardAccum += reward;
        AddReward(reward);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        int activeCount = eventLog != null ? eventLog.ActiveTargetCount : 0;

        d[0] = activeCount < idealTargetCount ? 1 : 0;
        d[1] = Random.Range(0, 3);
        d[2] = Random.Range(0, 5);
        d[3] = Random.Range(0, 3);
        d[4] = Random.Range(0, 3);
    }

    // ── Called by game systems ─────────────────────────────────────────────────

    public void OnRoundEnd()
    {
        float endReward = ComputeEndOfRoundReward();
        AddReward(endReward);
        EndEpisode();
    }

    /// <summary>Should be called at regular intervals by the round manager.</summary>
    public void RequestAgentDecision()
    {
        if (Time.time - _lastDecisionTime >= decisionIntervalSec)
        {
            _lastDecisionTime = Time.time;
            RequestDecision();
        }
    }

    // ── Reward helpers ────────────────────────────────────────────────────────

    float ComputeStepReward()
    {
        float reward = 0f;
        int active = eventLog != null ? eventLog.ActiveTargetCount : 0;

        int diff = Mathf.Abs(active - idealTargetCount);
        if (diff == 0)      reward += 0.1f;
        else if (diff == 1) reward += 0.03f;
        else if (diff >= 3) reward -= 0.05f * diff;

        float avgTTH = ComputeRecentAvgTimeToHit();
        if (avgTTH > 0.1f && avgTTH < 5f)
        {
            float timeDiff = Mathf.Abs(avgTTH - idealTimeToHitSec);
            if (timeDiff < 1f)      reward += 0.05f;
            else if (timeDiff > 3f) reward -= 0.03f;
        }

        return reward;
    }

    float ComputeEndOfRoundReward()
    {
        float hitRate = eventLog != null ? eventLog.CurrentHitRate : 0f;
        float hitDiff = Mathf.Abs(hitRate - idealHitRate);

        if (hitDiff < 0.1f) return 2.0f;
        if (hitDiff < 0.2f) return 1.0f;
        if (hitDiff > 0.3f) return -1.0f;
        return 0f;
    }

    float ComputeRecentAvgTimeToHit()
    {
        if (eventLog == null || eventLog.Events.Count == 0) return 0f;

        float sum = 0f;
        int count = 0;
        var events = eventLog.Events;
        int start = Mathf.Max(0, events.Count - 20);
        for (int i = start; i < events.Count; i++)
        {
            if (events[i].eventType == ShooterEventType.ShotHit.ToString() &&
                events[i].timeToHit > 0f)
            {
                sum += events[i].timeToHit;
                count++;
            }
        }
        return count > 0 ? sum / count : 0f;
    }

    // ── Spawn logic ───────────────────────────────────────────────────────────

    bool SpawnTargetAtPosition(TargetType type, Vector3 position)
    {
        if (targetManager == null) return false;
        return targetManager.SpawnSingleTarget(type, position);
    }

    Vector3 GridToWorldPosition(int xZone, int yZone, int zZone)
    {
        float xNorm = xZone / 4f;
        float yNorm = yZone / 2f;
        float zNorm = zZone / 2f;

        return new Vector3(
            Mathf.Lerp(spawnAreaMin.x, spawnAreaMax.x, xNorm),
            Mathf.Lerp(spawnAreaMin.y, spawnAreaMax.y, yNorm),
            Mathf.Lerp(spawnAreaMin.z, spawnAreaMax.z, zNorm));
    }

    // ── Encoding helpers ──────────────────────────────────────────────────────

    static float EncodeEventType(string type)
    {
        switch (type)
        {
            case "RoundStart":      return 0;
            case "RoundEnd":        return 1;
            case "TargetSpawned":   return 2;
            case "TargetDespawned": return 3;
            case "ShotFired":       return 4;
            case "ShotHit":         return 5;
            case "ShotMiss":        return 6;
            case "CloseMiss":       return 7;
            case "AgentDecision":   return 8;
            default:                return 0;
        }
    }

    static float EncodeTargetType(string type)
    {
        switch (type)
        {
            case "Stationary": return 0;
            case "Moving":     return 1;
            case "Erratic":    return 2;
            default:           return 0;
        }
    }
}
