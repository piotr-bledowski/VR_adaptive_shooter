using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Synthetic players designed for clean Q-learning.
///
/// KEY BEHAVIOUR: the player does NOT fire into empty air.  When no target is
/// active the player waits; when a target appears they apply a reaction delay
/// before the first shot.  This means:
///
///   hit rate  =  hits / shots_at_targets  (not diluted by empty-air shots)
///
/// That makes hit rate equal to per-shot accuracy against the given mix of
/// targets, which is a clean, learnable signal.
///
/// ── Designed optimal actions ─────────────────────────────────────────────
///
/// Effective hit rate is computed as:
///   1 / (0.6 * (1/hrA) + 0.2 * (1/hrB) + 0.2 * (1/hrC))
/// where hrA/B/C are the base hit chances for the three target types weighted
/// by the agent's 60 / 20 / 20 emphasis mix.
///
///   Naive    Stationary, no rotation  →  ~46 %   (reward +2.0, in sweet zone)
///            Any other action         →  < 38 %  (reward ≤ +0.3)
///
///   Average  Stationary or Moving, no rotation  →  ~51-57 %   (+2.0)
///            Any rotation                        →  < 44 %    (+0.3 or −1.0)
///
///   Expert   Any emphasis, fast rotation         →  ~54-61 %   (+2.0)
///            Fast rotation clearly best; no rotation gives > 85 %  (−1.0)
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
    public float naiveFireRate   = 0.7f;
    public float averageFireRate = 1.5f;
    public float expertFireRate  = 3.5f;

    float _nextShotTime;
    bool  _roundActive;
    bool  _waitingForTarget;   // true when polled with no active target

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
            _nextShotTime     = Time.time; // start checking for targets immediately
        }

        if (Time.time >= _nextShotTime)
            TakeShot();
    }

    // ── Core shot logic ──────────────────────────────────────────────────────

    void TakeShot()
    {
        var activeTargets = targetManager?.ActiveTargetsList;

        // ── No target present: wait silently (don't record a shot) ───────────
        if (activeTargets == null || activeTargets.Count == 0)
        {
            _waitingForTarget = true;
            _nextShotTime     = Time.time + 0.05f; // fast poll
            return;
        }

        // ── Target just appeared after a wait: apply reaction delay ──────────
        if (_waitingForTarget)
        {
            _waitingForTarget = false;
            _nextShotTime     = Time.time + GetReactionDelay();
            return; // don't fire until reaction delay passes
        }

        // ── Fire at the oldest active target ─────────────────────────────────
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
            if (missAngle < GetNearMissAngle())
                eventLog?.LogCloseMiss(ttype);
            else
                eventLog?.LogShotMiss(playerPos);
        }

        // Schedule next shot
        float interval = 1f / GetFireRate();
        _nextShotTime  = Time.time + interval * GetFireIntervalJitter();
    }

    ShooterTarget PickOldestTarget(IReadOnlyList<ShooterTarget> targets)
    {
        if (targets.Count == 0) return null;
        ShooterTarget oldest = targets[0];
        for (int i = 1; i < targets.Count; i++)
        {
            if (targets[i].LastSpawnTime < oldest.LastSpawnTime)
                oldest = targets[i];
        }
        return oldest;
    }

    // ── Hit chance per target type and rotation ──────────────────────────────
    //
    // Verified effective hit rates per emphasis mix (60 / 20 / 20):
    //
    //   Naive   Stat+None  : 1/(0.6/0.75 + 0.2/0.42 + 0.2/0.22) = 45.8 %
    //           Mov+None   : 1/(0.2/0.75 + 0.6/0.42 + 0.2/0.22) = 38.4 %
    //           Err+None   : 1/(0.2/0.75 + 0.2/0.42 + 0.6/0.22) = 28.8 %
    //
    //   Average Stat+None  : 1/(0.6/0.88 + 0.2/0.55 + 0.2/0.28) = 56.8 %
    //           Mov+None   : 1/(0.2/0.88 + 0.6/0.55 + 0.2/0.28) = 49.2 %
    //           Stat+Slow  : 1/(0.6/0.70 + 0.2/0.44 + 0.2/0.22) = 43.8 %   (rot hurts)
    //           Any+Medium : ≈ 35 %   Any+Fast : ≈ 25 %   (clear worse)
    //
    //   Expert  Any+Fast   : ~56-62 %  (all near sweet spot)
    //           Any+Medium : ~67-75 %  (above sweet spot)
    //           Any+None   : ~82-91 %  (way above → bad reward)

    float GetHitChance(TargetType type, RotationSpeed rotation)
    {
        float base0;
        switch (profile)
        {
            case SkillProfile.Naive:
                base0 = type == TargetType.Stationary ? 0.75f
                      : type == TargetType.Moving     ? 0.42f
                                                      : 0.22f;
                break;
            case SkillProfile.Average:
                base0 = type == TargetType.Stationary ? 0.88f
                      : type == TargetType.Moving     ? 0.55f
                                                      : 0.28f;
                break;
            case SkillProfile.Expert:
                base0 = type == TargetType.Stationary ? 0.97f
                      : type == TargetType.Moving     ? 0.90f
                                                      : 0.76f;
                break;
            default: base0 = 0.5f; break;
        }
        return base0 * GetRotationMultiplier(rotation);
    }

    float GetRotationMultiplier(RotationSpeed rotation)
    {
        switch (profile)
        {
            case SkillProfile.Naive:
                switch (rotation)
                {
                    case RotationSpeed.Slow:   return 0.72f;
                    case RotationSpeed.Medium: return 0.52f;
                    case RotationSpeed.Fast:   return 0.33f;
                    default: return 1f;
                }
            case SkillProfile.Average:
                switch (rotation)
                {
                    case RotationSpeed.Slow:   return 0.80f;
                    case RotationSpeed.Medium: return 0.60f;
                    case RotationSpeed.Fast:   return 0.40f;
                    default: return 1f;
                }
            case SkillProfile.Expert:
                // Fast rotation is the only sweet-spot multiplier for Expert.
                // No rotation → ~85-91% hit rate → bad reward.
                // Fast rotation → ~56-62% hit rate → reward sweet zone.
                switch (rotation)
                {
                    case RotationSpeed.Slow:   return 0.92f;
                    case RotationSpeed.Medium: return 0.82f;
                    case RotationSpeed.Fast:   return 0.68f;
                    default: return 1f;
                }
            default: return 1f;
        }
    }

    // ── Point zone distribution ──────────────────────────────────────────────

    int SimulatePoints(ShooterTarget target)
    {
        float pen = GetRotationPointPenalty(target.rotationSpeed);
        float r   = Random.value + pen;   // pen shifts hits away from centre

        switch (profile)
        {
            case SkillProfile.Expert:
                if (r < 0.45f) return target.centerPoints;   // 10
                if (r < 0.77f) return target.innerPoints;    // 5
                if (r < 0.93f) return target.middlePoints;   // 2
                return target.outerPoints;                    // 1

            case SkillProfile.Average:
                if (r < 0.08f) return target.centerPoints;
                if (r < 0.28f) return target.innerPoints;
                if (r < 0.62f) return target.middlePoints;
                return target.outerPoints;

            default: // Naive
                if (r < 0.02f) return target.centerPoints;
                if (r < 0.08f) return target.innerPoints;
                if (r < 0.30f) return target.middlePoints;
                return target.outerPoints;
        }
    }

    float GetRotationPointPenalty(RotationSpeed rotation)
    {
        float pen;
        switch (rotation)
        {
            case RotationSpeed.Slow:   pen = 0.10f; break;
            case RotationSpeed.Medium: pen = 0.22f; break;
            case RotationSpeed.Fast:   pen = 0.38f; break;
            default:                   pen = 0f;    break;
        }
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

    /// <summary>Delay between a target appearing and the first shot at it.</summary>
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
