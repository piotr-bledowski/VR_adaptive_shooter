using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Synthetic player for OFFLINE Q-learning experiments only.
///
/// Two jobs:
///   1. Shoot targets with a profile-dependent, type/rotation-dependent accuracy.
///      The player never fires into empty air, so observed hit rate == per-shot
///      accuracy against the spawned mix — a clean signal for the agent.
///   2. Rate the round on the 5-point difficulty scale via <see cref="RateRound"/>,
///      according to that persona's preferences. This rating drives the reward.
///
/// ── Persona preferences (what makes them say "Perfect") ────────────────────
///
///   Naive  / Beginner     : likes STATIONARY + SLOW, no rotation; comfortable
///                           accuracy band 0.50–0.80. Hates rotation and lots of
///                           moving/erratic targets (→ Hard / Too Hard).
///
///   Average / Intermediate: dislikes STATIONARY emphasis (boring); wants accuracy
///                           near 0.50; indifferent to rotation.
///
///   Expert / Advanced     : wants a real challenge — accuracy near 0.30 is Perfect;
///                           anything comfortable reads as "Too Easy".
///
/// Hit rates below are tuned so that for every persona at least one agent action
/// lands accuracy inside that persona's Perfect band (see header maths in
/// <see cref="EmphasisWeightedBase"/>).
/// </summary>
public class SyntheticPlayer : MonoBehaviour
{
    public enum SkillProfile { Naive, Average, Expert }

    [Header("Configuration")]
    public SkillProfile profile = SkillProfile.Average;

    [Header("References")]
    public ShooterTargetManager targetManager;
    public ShooterRoundManager  roundManager;
    public ShooterEventLog      eventLog;

    [Header("Fire rate (shots per second)")]
    public float naiveFireRate   = 0.8f;
    public float averageFireRate = 1.6f;
    public float expertFireRate  = 3.2f;

    float _nextShotTime;
    bool  _roundActive;
    bool  _waitingForTarget;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    void Update()
    {
        if (roundManager == null || roundManager.CurrentState != RoundState.Active)
        {
            _roundActive      = false;
            _waitingForTarget = false;
            return;
        }

        if (!_roundActive)
        {
            _roundActive      = true;
            _waitingForTarget = true;
            _nextShotTime     = Time.time;
        }

        if (Time.time >= _nextShotTime)
            TakeShot();
    }

    // ── Core shot logic ──────────────────────────────────────────────────────

    void TakeShot()
    {
        var activeTargets = targetManager?.ActiveTargetsList;

        if (activeTargets == null || activeTargets.Count == 0)
        {
            _waitingForTarget = true;
            _nextShotTime     = Time.time + 0.05f;
            return;
        }

        if (_waitingForTarget)
        {
            _waitingForTarget = false;
            _nextShotTime     = Time.time + GetReactionDelay();
            return;
        }

        ShooterTarget target = PickOldestTarget(activeTargets);
        if (target == null) { _nextShotTime = Time.time + 0.05f; return; }

        bool       isRotating = target.rotationSpeed != RotationSpeed.None;
        TargetType ttype      = target.targetType;
        Vector3    playerPos  = transform.position;

        eventLog?.LogShotFired(playerPos);
        roundManager?.stats.RecordShot(ttype, isRotating);
        roundManager?.adaptiveController?.OnShotFired(ttype, isRotating);

        float hitChance = GetHitChance(ttype, target.rotationSpeed);
        bool  isHit     = Random.value < hitChance;

        if (isHit)
        {
            float timeToHit = Time.time - target.LastSpawnTime;
            int   points    = SimulatePoints(target);
            eventLog?.LogShotHit(ttype, points, timeToHit, target.transform.position);
            roundManager?.HandleBulletHit(target, points);
        }
        else
        {
            float missAngle = Random.Range(0f, 15f);
            if (missAngle < GetNearMissAngle()) eventLog?.LogCloseMiss(ttype);
            else                                eventLog?.LogShotMiss(playerPos);
        }

        float interval = 1f / GetFireRate();
        _nextShotTime  = Time.time + interval * GetFireIntervalJitter();
    }

    ShooterTarget PickOldestTarget(IReadOnlyList<ShooterTarget> targets)
    {
        if (targets.Count == 0) return null;
        ShooterTarget oldest = targets[0];
        for (int i = 1; i < targets.Count; i++)
            if (targets[i].LastSpawnTime < oldest.LastSpawnTime) oldest = targets[i];
        return oldest;
    }

    // ── Hit chance model ──────────────────────────────────────────────────────

    float BaseHitRate(SkillProfile p, TargetType type)
    {
        switch (p)
        {
            case SkillProfile.Naive:
                return type == TargetType.Stationary ? 0.70f
                     : type == TargetType.Moving     ? 0.45f : 0.30f;
            case SkillProfile.Average:
                return type == TargetType.Stationary ? 0.85f
                     : type == TargetType.Moving     ? 0.58f : 0.42f;
            case SkillProfile.Expert:
                return type == TargetType.Stationary ? 0.96f
                     : type == TargetType.Moving     ? 0.82f : 0.58f;
            default: return 0.5f;
        }
    }

    float RotationMultiplier(SkillProfile p, RotationSpeed rot)
    {
        switch (p)
        {
            case SkillProfile.Naive:
                return rot == RotationSpeed.None ? 1f : rot == RotationSpeed.Slow ? 0.75f
                     : rot == RotationSpeed.Medium ? 0.55f : 0.40f;
            case SkillProfile.Average:
                return rot == RotationSpeed.None ? 1f : rot == RotationSpeed.Slow ? 0.82f
                     : rot == RotationSpeed.Medium ? 0.66f : 0.50f;
            case SkillProfile.Expert:
                return rot == RotationSpeed.None ? 1f : rot == RotationSpeed.Slow ? 0.88f
                     : rot == RotationSpeed.Medium ? 0.70f : 0.46f;
            default: return 1f;
        }
    }

    float GetHitChance(TargetType type, RotationSpeed rotation)
        => BaseHitRate(profile, type) * RotationMultiplier(profile, rotation);

    /// <summary>
    /// Expected accuracy for a given emphasis (the agent spawns 60/20/20 across the
    /// emphasised type and the two others) at a given rotation level. Used to verify
    /// each persona has a reachable Perfect band; mirrors the live shot distribution.
    /// </summary>
    float EmphasisWeightedBase(int emphasis)
    {
        float s = BaseHitRate(profile, TargetType.Stationary);
        float m = BaseHitRate(profile, TargetType.Moving);
        float e = BaseHitRate(profile, TargetType.Erratic);
        switch (emphasis)
        {
            case 0:  return 0.6f * s + 0.2f * m + 0.2f * e;
            case 1:  return 0.6f * m + 0.2f * e + 0.2f * s;
            default: return 0.6f * e + 0.2f * s + 0.2f * m;
        }
    }

    // ── Difficulty rating (the reward signal) ──────────────────────────────────

    /// <summary>
    /// Rate the round just played according to this persona's preferences.
    /// Reads the agent's committed action (emphasis / rotation / pace) and the
    /// measured hit rate.
    /// </summary>
    public DifficultyRating RateRound(ShooterStats stats)
    {
        int emphasis = 1, rotation = 0, pace = 1;
        var asc = roundManager != null ? roundManager.adaptiveController : null;
        if (asc != null)
            PlayerSkillProfile.DecodeAction(asc.currentAction, out emphasis, out rotation, out pace);

        float hr = stats != null ? stats.HitRate : 0f;

        switch (profile)
        {
            case SkillProfile.Naive:   return RateBeginner(hr, emphasis, rotation, pace);
            case SkillProfile.Average: return RateIntermediate(hr, emphasis);
            case SkillProfile.Expert:  return RateAdvanced(hr);
            default:                   return DifficultyRating.Perfect;
        }
    }

    // Beginner: stationary + slow + no rotation, accuracy 0.50–0.80.
    DifficultyRating RateBeginner(float hr, int emphasis, int rotation, int pace)
    {
        // Strong dislikes push toward Hard / Too Hard regardless of accuracy.
        if (rotation >= 3)                       return DifficultyRating.TooHard; // fast rotation hated
        if (rotation >= 1 && emphasis != 0)      return DifficultyRating.TooHard; // rotating + non-stationary
        if (rotation >= 1)                       return DifficultyRating.Hard;    // any rotation
        if (emphasis == 2)                       return DifficultyRating.TooHard; // lots of erratic
        if (emphasis == 1)                       return DifficultyRating.Hard;    // lots of moving

        // Stationary + no rotation: judge by accuracy, then pace comfort.
        if (hr > 0.85f) return DifficultyRating.TooEasy;
        if (hr < 0.45f) return DifficultyRating.TooHard;
        if (hr >= 0.50f && hr <= 0.80f)
            return pace == 0 ? DifficultyRating.Hard   // fast spawns feel rushed for a beginner
                             : DifficultyRating.Perfect;
        return hr > 0.80f ? DifficultyRating.Easy : DifficultyRating.Hard;
    }

    // Intermediate: dislikes stationary; wants accuracy ≈ 0.50; ignores rotation.
    DifficultyRating RateIntermediate(float hr, int emphasis)
    {
        if (emphasis == 0)
            return hr > 0.55f ? DifficultyRating.TooEasy : DifficultyRating.Hard; // boring

        float d = hr - 0.50f;
        if (Mathf.Abs(d) <= 0.12f) return DifficultyRating.Perfect;   // 0.38 – 0.62
        if (d > 0f) return hr > 0.72f ? DifficultyRating.TooEasy : DifficultyRating.Easy;
        return hr < 0.28f ? DifficultyRating.TooHard : DifficultyRating.Hard;
    }

    // Advanced: wants to struggle; accuracy ≈ 0.30 is Perfect.
    DifficultyRating RateAdvanced(float hr)
    {
        if (hr >= 0.20f && hr <= 0.42f) return DifficultyRating.Perfect;
        if (hr < 0.20f)  return hr < 0.12f ? DifficultyRating.TooHard : DifficultyRating.Hard;
        if (hr > 0.60f)  return DifficultyRating.TooEasy;
        return DifficultyRating.Easy;   // 0.42 – 0.60 : a touch too comfortable
    }

    // ── Point zone distribution ──────────────────────────────────────────────

    int SimulatePoints(ShooterTarget target)
    {
        float pen = GetRotationPointPenalty(target.rotationSpeed);
        float r   = Random.value + pen;

        switch (profile)
        {
            case SkillProfile.Expert:
                if (r < 0.45f) return target.centerPoints;
                if (r < 0.77f) return target.innerPoints;
                if (r < 0.93f) return target.middlePoints;
                return target.outerPoints;
            case SkillProfile.Average:
                if (r < 0.08f) return target.centerPoints;
                if (r < 0.28f) return target.innerPoints;
                if (r < 0.62f) return target.middlePoints;
                return target.outerPoints;
            default:
                if (r < 0.02f) return target.centerPoints;
                if (r < 0.08f) return target.innerPoints;
                if (r < 0.30f) return target.middlePoints;
                return target.outerPoints;
        }
    }

    float GetRotationPointPenalty(RotationSpeed rotation)
    {
        float pen = rotation == RotationSpeed.Slow ? 0.10f
                  : rotation == RotationSpeed.Medium ? 0.22f
                  : rotation == RotationSpeed.Fast ? 0.38f : 0f;
        if (profile == SkillProfile.Expert) pen *= 0.5f;
        return pen;
    }

    // ── Timing helpers ───────────────────────────────────────────────────────

    float GetFireRate()
    {
        switch (profile)
        {
            case SkillProfile.Expert:  return expertFireRate;
            case SkillProfile.Average: return averageFireRate;
            default:                   return naiveFireRate;
        }
    }

    float GetReactionDelay()
    {
        switch (profile)
        {
            case SkillProfile.Expert:  return Random.Range(0.05f, 0.15f);
            case SkillProfile.Average: return Random.Range(0.15f, 0.40f);
            default:                   return Random.Range(0.35f, 0.75f);
        }
    }

    float GetFireIntervalJitter()
    {
        switch (profile)
        {
            case SkillProfile.Expert:  return Random.Range(0.93f, 1.07f);
            case SkillProfile.Average: return Random.Range(0.85f, 1.15f);
            default:                   return Random.Range(0.78f, 1.22f);
        }
    }

    float GetNearMissAngle()
    {
        switch (profile)
        {
            case SkillProfile.Expert:  return 3f;
            case SkillProfile.Average: return 7f;
            default:                   return 12f;
        }
    }
}
