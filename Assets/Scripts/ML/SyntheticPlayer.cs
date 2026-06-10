using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simulates a human player at a given skill level for ML agent training.
/// Does not require VR — runs headless in the training scene.
///
/// Three profiles:
///   Naive   — low accuracy (~20%), slow reaction (4–6 s), struggles with moving/erratic.
///   Average — medium accuracy (~50%), medium reaction (2–3 s), some trouble with erratic.
///   Expert  — high accuracy (~80%), fast reaction (0.5–1.5 s), handles all types.
///
/// The synthetic player "shoots" at active targets with probabilistic hit/miss based on
/// the target's type and its profile. Time-to-hit is sampled from a distribution per type.
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
    public float averageFireRate = 1.5f;
    public float expertFireRate  = 2.5f;

    private float _nextShotTime;
    private bool  _roundActive;

    void Update()
    {
        if (roundManager == null || roundManager.CurrentState != RoundState.Active)
        {
            _roundActive = false;
            return;
        }

        if (!_roundActive)
        {
            _roundActive = true;
            _nextShotTime = Time.time + Random.Range(0.2f, 0.8f);
        }

        if (Time.time >= _nextShotTime)
        {
            TakeShot();
            float interval = 1f / GetFireRate();
            _nextShotTime = Time.time + interval * Random.Range(0.7f, 1.3f);
        }
    }

    void TakeShot()
    {
        Vector3 playerPos = transform.position;
        eventLog?.LogShotFired(playerPos);

        if (roundManager != null)
            roundManager.stats.RecordShot();

        var activeTargets = targetManager?.ActiveTargetsList;
        if (activeTargets == null || activeTargets.Count == 0)
        {
            eventLog?.LogShotMiss(playerPos);
            return;
        }

        ShooterTarget target = PickTarget(activeTargets);
        if (target == null)
        {
            eventLog?.LogShotMiss(playerPos);
            return;
        }

        float hitChance = GetHitChance(target.targetType);
        bool isHit = Random.value < hitChance;

        if (isHit)
        {
            float timeToHit = Time.time - target.LastSpawnTime;
            int points = SimulatePointsForProfile(target);

            eventLog?.LogShotHit(target.targetType, points, timeToHit, target.transform.position);
            roundManager?.HandleBulletHit(target, points);
        }
        else
        {
            float missAngle = Random.Range(0f, 15f);
            if (missAngle < GetNearMissAngle())
                eventLog?.LogCloseMiss(target.targetType);
            else
                eventLog?.LogShotMiss(playerPos);
        }
    }

    ShooterTarget PickTarget(IReadOnlyList<ShooterTarget> targets)
    {
        if (targets.Count == 0) return null;

        // Higher-skill players pick the oldest target (longest alive); naive pick random
        if (profile == SkillProfile.Expert)
        {
            ShooterTarget oldest = targets[0];
            for (int i = 1; i < targets.Count; i++)
            {
                if (targets[i].LastSpawnTime < oldest.LastSpawnTime)
                    oldest = targets[i];
            }
            return oldest;
        }

        return targets[Random.Range(0, targets.Count)];
    }

    int SimulatePointsForProfile(ShooterTarget target)
    {
        float r = Random.value;
        switch (profile)
        {
            case SkillProfile.Expert:
                if (r < 0.30f) return target.centerPoints;
                if (r < 0.60f) return target.innerPoints;
                if (r < 0.85f) return target.middlePoints;
                return target.outerPoints;

            case SkillProfile.Average:
                if (r < 0.10f) return target.centerPoints;
                if (r < 0.35f) return target.innerPoints;
                if (r < 0.65f) return target.middlePoints;
                return target.outerPoints;

            default: // Naive
                if (r < 0.05f) return target.centerPoints;
                if (r < 0.15f) return target.innerPoints;
                if (r < 0.40f) return target.middlePoints;
                return target.outerPoints;
        }
    }

    float GetHitChance(TargetType type)
    {
        switch (profile)
        {
            case SkillProfile.Naive:
                return type == TargetType.Stationary ? 0.30f
                     : type == TargetType.Moving     ? 0.15f
                                                     : 0.08f;
            case SkillProfile.Average:
                return type == TargetType.Stationary ? 0.65f
                     : type == TargetType.Moving     ? 0.45f
                                                     : 0.30f;
            case SkillProfile.Expert:
                return type == TargetType.Stationary ? 0.90f
                     : type == TargetType.Moving     ? 0.75f
                                                     : 0.60f;
            default: return 0.5f;
        }
    }

    float GetFireRate()
    {
        switch (profile)
        {
            case SkillProfile.Naive:   return naiveFireRate;
            case SkillProfile.Average: return averageFireRate;
            case SkillProfile.Expert:  return expertFireRate;
            default: return averageFireRate;
        }
    }

    float GetNearMissAngle()
    {
        switch (profile)
        {
            case SkillProfile.Naive:   return 12f;
            case SkillProfile.Average: return 8f;
            case SkillProfile.Expert:  return 5f;
            default: return 8f;
        }
    }
}
