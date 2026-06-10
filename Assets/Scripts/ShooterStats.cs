using System;
using UnityEngine;

/// <summary>Per-target-type statistics bucket.</summary>
[Serializable]
public class TargetTypeStats
{
    public int   hits;
    public int   closeMisses;
    public int   points;
    public float totalTimeToHitSeconds;

    public float AvgPointsPerHit => hits > 0 ? (float)points / hits : 0f;
    /// <summary>Mean seconds from spawn to hit. 0 when no hits this round.</summary>
    public float AvgTimeToHit    => hits > 0 ? totalTimeToHitSeconds / hits : 0f;

    public void Reset()
    {
        hits = 0;
        closeMisses = 0;
        points = 0;
        totalTimeToHitSeconds = 0f;
    }
}

/// <summary>
/// All statistics for one round.
/// Owned by ShooterRoundManager; exported as ShooterRoundReport for ML training.
/// </summary>
[Serializable]
public class ShooterStats
{
    public int totalShots;
    public int totalHits;
    public int totalPoints;

    public TargetTypeStats stationary = new TargetTypeStats();
    public TargetTypeStats moving     = new TargetTypeStats();
    public TargetTypeStats erratic    = new TargetTypeStats();

    // ── Computed properties ───────────────────────────────────────────────────

    public int   TotalMisses     => totalShots - totalHits;
    public float HitRate         => totalShots > 0 ? (float)totalHits / totalShots : 0f;
    public float AvgPointsPerHit => totalHits  > 0 ? (float)totalPoints / totalHits : 0f;

    // ── Mutators ──────────────────────────────────────────────────────────────

    public void Reset()
    {
        totalShots = totalHits = totalPoints = 0;
        stationary.Reset();
        moving.Reset();
        erratic.Reset();
    }

    public void RecordShot() => totalShots++;

    public void RecordHit(TargetType type, int pts, float timeToHitSeconds)
    {
        totalHits++;
        totalPoints += pts;
        var ts = ForType(type);
        ts.hits++;
        ts.points += pts;
        if (timeToHitSeconds > 0f)
            ts.totalTimeToHitSeconds += timeToHitSeconds;
    }

    public void RecordCloseMiss(TargetType type) => ForType(type).closeMisses++;

    public TargetTypeStats ForType(TargetType t)
    {
        switch (t)
        {
            case TargetType.Stationary: return stationary;
            case TargetType.Moving:     return moving;
            case TargetType.Erratic:    return erratic;
            default:                    return stationary;
        }
    }
}
