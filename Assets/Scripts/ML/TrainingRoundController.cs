using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Runs offline Q-learning training episodes using a synthetic player.
/// After training completes, saves the profile as a base template and a
/// comprehensive training report (JSON + CSV for plotting).
/// </summary>
public class TrainingRoundController : MonoBehaviour
{
    [Header("References")]
    public AdaptiveSpawnController adaptiveController;
    public ShooterRoundManager     roundManager;
    public ShooterTargetManager    targetManager;
    public ShooterEventLog         eventLog;
    public SyntheticPlayer         syntheticPlayer;

    [Header("Training settings")]
    public float trainingTimeScale = 6f;
    public int   roundsToTrain     = 200;
    public float interRoundDelay   = 0.05f;
    public PlayerSkillLevel skillLevel = PlayerSkillLevel.Intermediate;

    int   _roundsCompleted;
    bool  _training = true;
    float _totalReward;
    float _bestReward = float.NegativeInfinity;
    int   _bestRound;

    readonly List<TrainingRoundRecord> _history = new List<TrainingRoundRecord>();
    static int s_activeControllers;

    void Awake()
    {
        s_activeControllers++;
        Time.timeScale             = trainingTimeScale;
        Application.targetFrameRate = -1;
    }

    void OnDestroy()
    {
        s_activeControllers = Mathf.Max(0, s_activeControllers - 1);
    }

    void Start()
    {
        string profileName = $"_training_{skillLevel}";
        var profile = PlayerSkillProfile.CreateNew(profileName, skillLevel);
        adaptiveController.activeProfile = profile;
        StartCoroutine(TrainingLoop());
    }

    IEnumerator TrainingLoop()
    {
        yield return null;

        while (_training)
        {
            while (roundManager.CurrentState != RoundState.Idle)
                yield return null;

            yield return new WaitForSecondsRealtime(interRoundDelay);

            roundManager.StartRound();

            while (roundManager.CurrentState == RoundState.Active)
                yield return null;

            OnRoundComplete();

            if (_roundsCompleted >= roundsToTrain)
            {
                FinishTraining();
                yield break;
            }
        }
    }

    void OnRoundComplete()
    {
        _roundsCompleted++;

        var profile = adaptiveController.activeProfile;
        if (profile == null) return;

        var s = roundManager.stats;
        float hitRate = s.HitRate;
        float avgTTH = ComputeAvgTimeToHit();
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

        if (_roundsCompleted % 25 == 0)
        {
            Debug.Log($"[Training:{skillLevel}] {_roundsCompleted}/{roundsToTrain} | " +
                      $"avg_reward={_totalReward / _roundsCompleted:F2} | " +
                      $"hit={hitRate:F2} TTH={avgTTH:F1}s ppt={ppt:F1} | " +
                      $"ε={profile.epsilon:F3} state={profile.GetCurrentState()} | " +
                      $"action={adaptiveController.currentAction} ({adaptiveController.currentActionDescription})");
        }
    }

    void FinishTraining()
    {
        _training = false;
        s_activeControllers = Mathf.Max(0, s_activeControllers - 1);
        if (s_activeControllers == 0)
            Time.timeScale = 1f;

        var profile = adaptiveController.activeProfile;
        if (profile != null)
        {
            profile.SaveAsBase(skillLevel);
            float avg = _totalReward / _roundsCompleted;
            Debug.Log($"[Training:{skillLevel}] DONE — {_roundsCompleted} rounds, " +
                      $"avg reward={avg:F2}, best={_bestReward:F2} at round {_bestRound}.");

            SaveReport();
        }
    }

    void SaveReport()
    {
        string dir = Path.Combine(Application.persistentDataPath, "shooter_training_reports");
        Directory.CreateDirectory(dir);

        string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string baseName = $"training_{skillLevel.ToString().ToLower()}_{ts}";

        // JSON report
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

        Debug.Log($"[Training:{skillLevel}] Report → {jsonPath}");
        Debug.Log($"[Training:{skillLevel}] CSV    → {csvPath}");
    }

    float ComputeAvgTimeToHit()
    {
        if (eventLog == null) return 2.5f;
        var events = eventLog.Events;
        float sum = 0f; int count = 0;
        for (int i = Mathf.Max(0, events.Count - 30); i < events.Count; i++)
        {
            if (events[i].eventType == ShooterEventType.ShotHit.ToString() && events[i].timeToHit > 0f)
            { sum += events[i].timeToHit; count++; }
        }
        return count > 0 ? sum / count : 2.5f;
    }
}

[Serializable]
public class TrainingReport
{
    public string  skillLevel;
    public int     totalRounds;
    public float   avgReward;
    public float   bestReward;
    public int     bestRound;
    public TrainingRoundRecord[] rounds;
}

[Serializable]
public struct TrainingRoundRecord
{
    public int    round;
    public int    action;
    public int    emphasisType;
    public int    rotationLevel;
    public int    spawnPace;
    public string actionDesc;
    public float  hitRate;
    public float  avgTimeToHit;
    public float  pointsPerTarget;
    public int    totalPoints;
    public int    targetsSpawned;
    public int    expired;
    public float  hitRateStat;
    public float  hitRateMov;
    public float  hitRateErr;
    public float  hitRateRot;
    public float  ptsPerHitStat;
    public float  ptsPerHitMov;
    public float  ptsPerHitErr;
    public float  reward;
    public float  epsilon;
    public int    qState;
}
