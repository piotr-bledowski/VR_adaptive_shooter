using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Simulates a full online player session using SyntheticPlayer.
/// Profile initialised via CreateFromBase (same as a real new player).
/// Saves the profile after every round — identical to live gameplay.
/// </summary>
public class OnlineSimController : MonoBehaviour
{
    [Header("References")]
    public AdaptiveSpawnController adaptiveController;
    public ShooterRoundManager     roundManager;
    public ShooterEventLog         eventLog;

    [Header("Simulation settings")]
    public float simulationTimeScale = 8f;
    public int   roundsToSimulate    = 25;
    public float interRoundDelay     = 0.05f;
    public PlayerSkillLevel skillLevel = PlayerSkillLevel.Intermediate;

    int   _roundsCompleted;
    bool  _running = true;
    float _totalReward;
    float _bestReward  = float.NegativeInfinity;
    int   _bestRound;

    readonly List<TrainingRoundRecord> _history = new List<TrainingRoundRecord>();
    static int s_activeControllers;

    void Awake()
    {
        s_activeControllers++;
        Time.timeScale             = simulationTimeScale;
        Application.targetFrameRate = -1;
    }

    void OnDestroy()
    {
        s_activeControllers = Mathf.Max(0, s_activeControllers - 1);
    }

    void Start()
    {
        string playerName = $"sim_{skillLevel.ToString().ToLower()}";
        var profile = PlayerSkillProfile.CreateFromBase(playerName, skillLevel);
        adaptiveController.activeProfile = profile;

        Debug.Log($"[OnlineSim:{skillLevel}] Starting {roundsToSimulate}-round session " +
                  $"as '{playerName}' (ε={profile.epsilon:F2})");

        StartCoroutine(SimLoop());
    }

    IEnumerator SimLoop()
    {
        yield return null;

        while (_running)
        {
            while (roundManager.CurrentState != RoundState.Idle)
                yield return null;

            yield return new WaitForSecondsRealtime(interRoundDelay);

            roundManager.StartRound();

            while (roundManager.CurrentState == RoundState.Active)
                yield return null;

            while (roundManager.CurrentState != RoundState.Idle)
                yield return null;

            RecordRound();

            if (_roundsCompleted >= roundsToSimulate)
            {
                Finish();
                yield break;
            }
        }
    }

    void RecordRound()
    {
        _roundsCompleted++;

        var profile = adaptiveController.activeProfile;
        if (profile == null) return;

        var s = roundManager.stats;
        float hitRate = s.HitRate;
        float avgTTH = AvgTimeToHit();
        float ppt = s.totalTargetsSpawned > 0 ? (float)s.totalPoints / s.totalTargetsSpawned : 0f;

        float reward = PlayerSkillProfile.ComputeFlowReward(
            hitRate, avgTTH, ppt,
            s.stationary.HitRate, s.moving.HitRate, s.erratic.HitRate);

        _totalReward += reward;
        if (reward > _bestReward) { _bestReward = reward; _bestRound = _roundsCompleted; }

        PlayerSkillProfile.DecodeAction(adaptiveController.currentAction,
            out int emphType, out int rotLvl, out int pace);

        _history.Add(new TrainingRoundRecord
        {
            round              = _roundsCompleted,
            action             = adaptiveController.currentAction,
            emphasisType       = emphType,
            rotationLevel      = rotLvl,
            spawnPace          = pace,
            actionDesc         = adaptiveController.currentActionDescription ?? "",
            hitRate            = hitRate,
            avgTimeToHit       = avgTTH,
            pointsPerTarget    = ppt,
            totalPoints        = s.totalPoints,
            targetsSpawned     = s.totalTargetsSpawned,
            expired            = s.totalExpired,
            hitRateStat        = s.stationary.HitRate,
            hitRateMov         = s.moving.HitRate,
            hitRateErr         = s.erratic.HitRate,
            hitRateRot         = s.rotating.HitRate,
            ptsPerHitStat      = s.stationary.AvgPointsPerHit,
            ptsPerHitMov       = s.moving.AvgPointsPerHit,
            ptsPerHitErr       = s.erratic.AvgPointsPerHit,
            reward             = reward,
            epsilon            = profile.epsilon,
            qState             = profile.GetCurrentState()
        });

        Debug.Log($"[OnlineSim:{skillLevel}] {_roundsCompleted}/{roundsToSimulate} | " +
                  $"{adaptiveController.currentActionDescription} | " +
                  $"hit={hitRate:F2} TTH={avgTTH:F1}s ppt={ppt:F1} | " +
                  $"reward={reward:F2} ε={profile.epsilon:F3}");
    }

    void Finish()
    {
        _running = false;
        s_activeControllers = Mathf.Max(0, s_activeControllers - 1);
        if (s_activeControllers == 0)
            Time.timeScale = 1f;

        float avg = _totalReward / Mathf.Max(1, _roundsCompleted);
        Debug.Log($"[OnlineSim:{skillLevel}] COMPLETE — {_roundsCompleted} rounds, " +
                  $"avg reward={avg:F2}");

        SaveReport();
    }

    void SaveReport()
    {
        string dir = Path.Combine(Application.persistentDataPath, "shooter_sim_reports");
        Directory.CreateDirectory(dir);

        string ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string baseName = $"sim_{skillLevel.ToString().ToLower()}_{ts}";

        var report = new TrainingReport
        {
            skillLevel  = skillLevel.ToString(),
            totalRounds = _roundsCompleted,
            avgReward   = _totalReward / Mathf.Max(1, _roundsCompleted),
            bestReward  = _bestReward,
            bestRound   = _bestRound,
            rounds      = _history.ToArray()
        };

        string jsonPath = Path.Combine(dir, baseName + ".json");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true));

        // CSV for easy plotting
        string csvPath = Path.Combine(dir, baseName + ".csv");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("round,action,emphType,rotLevel,pace,hitRate,avgTTH,ppt,totalPts," +
                      "spawned,expired,hrStat,hrMov,hrErr,hrRot,pphStat,pphMov,pphErr," +
                      "reward,epsilon,qState");
        foreach (var r in _history)
        {
            sb.AppendLine($"{r.round},{r.action},{r.emphasisType},{r.rotationLevel},{r.spawnPace}," +
                          $"{r.hitRate:F4},{r.avgTimeToHit:F3},{r.pointsPerTarget:F3},{r.totalPoints}," +
                          $"{r.targetsSpawned},{r.expired}," +
                          $"{r.hitRateStat:F4},{r.hitRateMov:F4},{r.hitRateErr:F4},{r.hitRateRot:F4}," +
                          $"{r.ptsPerHitStat:F3},{r.ptsPerHitMov:F3},{r.ptsPerHitErr:F3}," +
                          $"{r.reward:F4},{r.epsilon:F4},{r.qState}");
        }
        File.WriteAllText(csvPath, sb.ToString());

        Debug.Log($"[OnlineSim:{skillLevel}] Report → {jsonPath}");
        Debug.Log($"[OnlineSim:{skillLevel}] CSV    → {csvPath}");
    }

    float AvgTimeToHit()
    {
        if (eventLog == null) return 2.5f;
        var events = eventLog.Events;
        float sum = 0f;
        int   cnt = 0;
        for (int i = Mathf.Max(0, events.Count - 30); i < events.Count; i++)
        {
            if (events[i].eventType == ShooterEventType.ShotHit.ToString() &&
                events[i].timeToHit > 0f)
            {
                sum += events[i].timeToHit;
                cnt++;
            }
        }
        return cnt > 0 ? sum / cnt : 2.5f;
    }
}
