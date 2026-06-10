using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Simulates a full online player session — the same adaptive Q-learning loop that runs
/// during real VR gameplay — using a SyntheticPlayer instead of a human.
///
/// Key differences from TrainingRoundController (which produces base profiles):
///   - Profile is initialised via CreateFromBase(), exactly as a new real player's profile is.
///   - Profile name does NOT start with "_training_", so AdaptiveSpawnController saves it to
///     disk after every round — identical to the live game.
///   - Only 25 rounds (a realistic session length) rather than 200.
///
/// Workflow:
///   1. Run ShooterTraining.unity first to produce _base_*.json profiles.
///   2. Open ShooterOnlineSim.unity and press Play.
///   3. Read the console logs or the JSON report in
///      Application.persistentDataPath/shooter_sim_reports/ to inspect how difficulty adapts.
///
/// If no base profiles exist yet, CreateFromBase falls back to hand-seeded defaults —
/// the simulation still runs, but will start from a cold Q-table.
/// </summary>
public class OnlineSimController : MonoBehaviour
{
    [Header("References")]
    public AdaptiveSpawnController adaptiveController;
    public ShooterRoundManager     roundManager;
    public ShooterEventLog         eventLog;

    [Header("Simulation settings")]
    [Tooltip("Time.timeScale during simulation (8–12 recommended for fast results).")]
    public float simulationTimeScale = 8f;
    [Tooltip("Number of rounds to simulate. 25 approximates a realistic play session.")]
    public int   roundsToSimulate    = 25;
    [Tooltip("Seconds real-time between rounds.")]
    public float interRoundDelay     = 0.05f;
    [Tooltip("Skill level this environment represents.")]
    public PlayerSkillLevel skillLevel = PlayerSkillLevel.Intermediate;

    // ── State ─────────────────────────────────────────────────────────────────

    int   _roundsCompleted;
    bool  _running = true;
    float _totalReward;
    float _bestReward  = float.NegativeInfinity;
    int   _bestRound;

    readonly List<RoundRecord> _history = new List<RoundRecord>();

    static int s_activeControllers;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

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
        // Initialise exactly as the real game does for a brand-new player.
        // CreateFromBase clones the trained Q-table from _base_<skill>.json;
        // if that file doesn't exist yet it falls back to hand-seeded defaults.
        string playerName = $"sim_{skillLevel.ToString().ToLower()}";
        var profile = PlayerSkillProfile.CreateFromBase(playerName, skillLevel);
        adaptiveController.activeProfile = profile;

        Debug.Log($"[OnlineSim:{skillLevel}] Starting {roundsToSimulate}-round session " +
                  $"as '{playerName}' (ε={profile.epsilon:F2}, " +
                  $"hitRate₀={profile.emaHitRate:F2}, TTH₀={profile.emaTimeToHit:F1}s)");

        StartCoroutine(SimLoop());
    }

    // ── Simulation loop ───────────────────────────────────────────────────────

    IEnumerator SimLoop()
    {
        yield return null; // let all Awake/Start hooks complete

        while (_running)
        {
            while (roundManager.CurrentState != RoundState.Idle)
                yield return null;

            yield return new WaitForSecondsRealtime(interRoundDelay);

            roundManager.StartRound();

            while (roundManager.CurrentState == RoundState.Active)
                yield return null;

            // Wait for DespawnThenIdle coroutine inside RoundManager to finish
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

        // By this point AdaptiveSpawnController.OnRoundEnd() has already:
        //   - computed hitRate and avgTTH from the event log
        //   - called profile.UpdateAfterRound() (Q-table Bellman update + EMA)
        //   - saved the profile to disk (because name != "_training_*")
        // This is identical to what happens after every round in the live game.

        var profile = adaptiveController.activeProfile;
        if (profile == null) return;

        float hitRate = eventLog != null ? eventLog.CurrentHitRate : 0f;
        float avgTTH  = AvgTimeToHit();
        float reward  = PlayerSkillProfile.ComputeFlowReward(hitRate, avgTTH);

        _totalReward += reward;
        if (reward > _bestReward) { _bestReward = reward; _bestRound = _roundsCompleted; }

        _history.Add(new RoundRecord
        {
            round        = _roundsCompleted,
            preset       = adaptiveController.currentPresetName,
            action       = adaptiveController.currentAction,
            hitRate      = profile.emaHitRate,
            avgTimeToHit = profile.emaTimeToHit,
            reward       = reward,
            epsilon      = profile.epsilon,
            qState       = profile.GetCurrentState()
        });

        Debug.Log($"[OnlineSim:{skillLevel}] {_roundsCompleted}/{roundsToSimulate} | " +
                  $"preset={adaptiveController.currentPresetName,-9} | " +
                  $"hitRate={profile.emaHitRate:F2}  TTH={profile.emaTimeToHit:F1}s | " +
                  $"reward={reward:F2}  ε={profile.epsilon:F3}  state={profile.GetCurrentState()}");
    }

    void Finish()
    {
        _running = false;
        s_activeControllers = Mathf.Max(0, s_activeControllers - 1);
        if (s_activeControllers == 0)
            Time.timeScale = 1f;

        float avg = _totalReward / Mathf.Max(1, _roundsCompleted);
        Debug.Log($"[OnlineSim:{skillLevel}] COMPLETE — {_roundsCompleted} rounds | " +
                  $"avg reward={avg:F2} | best={_bestReward:F2} at round {_bestRound}");

        SaveReport();
    }

    // ── Report ────────────────────────────────────────────────────────────────

    void SaveReport()
    {
        string dir = Path.Combine(Application.persistentDataPath, "shooter_sim_reports");
        Directory.CreateDirectory(dir);

        string ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(dir, $"sim_{skillLevel.ToString().ToLower()}_{ts}.json");

        var report = new SimReport
        {
            skillLevel  = skillLevel.ToString(),
            totalRounds = _roundsCompleted,
            avgReward   = _totalReward / Mathf.Max(1, _roundsCompleted),
            bestReward  = _bestReward,
            bestRound   = _bestRound,
            rounds      = _history.ToArray()
        };

        File.WriteAllText(path, JsonUtility.ToJson(report, true));
        Debug.Log($"[OnlineSim:{skillLevel}] Session report → {path}");
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

// ── Serialisable data types (outside MonoBehaviour for JsonUtility compatibility) ──

[Serializable]
public class SimReport
{
    public string   skillLevel;
    public int      totalRounds;
    public float    avgReward;
    public float    bestReward;
    public int      bestRound;
    public RoundRecord[] rounds;
}

[Serializable]
public struct RoundRecord
{
    public int    round;
    public string preset;
    public int    action;
    public float  hitRate;
    public float  avgTimeToHit;
    public float  reward;
    public float  epsilon;
    public int    qState;
}
