using System.Collections;
using UnityEngine;

/// <summary>
/// Runs offline Q-learning training episodes using a synthetic player.
/// Uses the same AdaptiveSpawnController + PlayerSkillProfile algorithm as live gameplay —
/// just accelerated (high timeScale, no VR, many rounds) so the Q-table converges.
///
/// After training completes, saves the profile as a base template for that skill level.
/// New real players get their Q-table initialized from this trained base.
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
    [Tooltip("Time.timeScale during training. Higher = faster (4–8 recommended).")]
    public float trainingTimeScale = 6f;
    [Tooltip("Number of rounds to train before saving the base profile.")]
    public int   roundsToTrain     = 200;
    [Tooltip("Seconds real-time between rounds.")]
    public float interRoundDelay   = 0.05f;
    [Tooltip("Which skill level this env represents. Sets the base profile on save.")]
    public PlayerSkillLevel skillLevel = PlayerSkillLevel.Intermediate;

    int   _roundsCompleted;
    bool  _training = true;
    float _totalReward;
    float _bestReward = float.NegativeInfinity;
    int   _bestRound;

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
        // Create a fresh profile for this training run
        string profileName = $"_training_{skillLevel}";
        var profile = PlayerSkillProfile.CreateNew(profileName, skillLevel);
        adaptiveController.activeProfile = profile;

        StartCoroutine(TrainingLoop());
    }

    IEnumerator TrainingLoop()
    {
        // Wait for scene to fully initialize
        yield return null;

        while (_training)
        {
            // Wait for round manager to reach Idle before starting next round
            // (DespawnThenIdle coroutine must complete first)
            while (roundManager.CurrentState != RoundState.Idle)
                yield return null;

            yield return new WaitForSecondsRealtime(interRoundDelay);

            StartTrainingRound();

            // Wait for round to finish (transition to Results or Idle)
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

    void StartTrainingRound()
    {
        // StartRound() calls eventLog.BeginRound(), adaptiveController.OnRoundStart(),
        // and targetManager.BeginRoundSpawning(). trainingMode=true bypasses session check.
        roundManager.StartRound();
    }

    void OnRoundComplete()
    {
        _roundsCompleted++;

        // The Q-learning update already happened inside adaptiveController.OnRoundEnd()
        // (called by ShooterRoundManager.EndRound). We just track progress here.
        var profile = adaptiveController.activeProfile;
        if (profile == null) return;

        float reward = PlayerSkillProfile.ComputeFlowReward(
            eventLog != null ? eventLog.CurrentHitRate : 0f,
            ComputeAvgTimeToHit());

        _totalReward += reward;

        if (reward > _bestReward) { _bestReward = reward; _bestRound = _roundsCompleted; }

        if (_roundsCompleted % 25 == 0)
        {
            Debug.Log($"[Training:{skillLevel}] {_roundsCompleted}/{roundsToTrain} | " +
                      $"avg_reward={_totalReward / _roundsCompleted:F2} | " +
                      $"hitRate={profile.emaHitRate:F2} TTH={profile.emaTimeToHit:F1}s | " +
                      $"ε={profile.epsilon:F3} state={profile.GetCurrentState()} " +
                      $"action={adaptiveController.currentAction} ({adaptiveController.currentPresetName})");
        }
    }

    void FinishTraining()
    {
        _training = false;
        s_activeControllers = Mathf.Max(0, s_activeControllers - 1);
        if (s_activeControllers == 0)
            Time.timeScale = 1f; // only reset time when all envs are done

        var profile = adaptiveController.activeProfile;
        if (profile != null)
        {
            profile.SaveAsBase(skillLevel);
            float avg = _totalReward / _roundsCompleted;
            Debug.Log($"[Training:{skillLevel}] DONE — {_roundsCompleted} rounds, " +
                      $"avg reward={avg:F2}, best={_bestReward:F2} at round {_bestRound}. " +
                      $"Base profile saved.");
        }
        else
        {
            Debug.LogWarning($"[Training:{skillLevel}] No profile to save.");
        }
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
