using System;
using UnityEngine;

[Serializable]
public class TargetTypeStats
{
    public int   hits;
    public int   shots;
    public int   closeMisses;
    public int   expired;
    public int   points;
    public float totalTimeToHitSeconds;

    public float AvgPointsPerHit    => hits > 0 ? (float)points / hits : 0f;
    public float AvgPointsPerTarget => (hits + expired) > 0 ? (float)points / (hits + expired) : 0f;
    public float HitRate            => shots > 0 ? (float)hits / shots : 0f;
    /// <summary>Mean seconds from spawn to hit. 0 when no hits this round.</summary>
    public float AvgTimeToHit       => hits > 0 ? totalTimeToHitSeconds / hits : 0f;

    public void Reset()
    {
        hits = 0;
        shots = 0;
        closeMisses = 0;
        expired = 0;
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
    public int totalExpired;
    public int totalTargetsSpawned;

    public TargetTypeStats stationary = new TargetTypeStats();
    public TargetTypeStats moving     = new TargetTypeStats();
    public TargetTypeStats erratic    = new TargetTypeStats();
    public TargetTypeStats rotating   = new TargetTypeStats();

    // ── Computed properties ───────────────────────────────────────────────

    public int   TotalMisses     => totalShots - totalHits;
    public float HitRate         => totalShots > 0 ? (float)totalHits / totalShots : 0f;
    public float AvgPointsPerHit => totalHits  > 0 ? (float)totalPoints / totalHits : 0f;
    public float ExpiredRate     => totalTargetsSpawned > 0 ? (float)totalExpired / totalTargetsSpawned : 0f;

    // ── Mutators ────────────────────────────────────────────────────────────

    public void Reset()
    {
        totalShots = totalHits = totalPoints = totalExpired = totalTargetsSpawned = 0;
        stationary.Reset();
        moving.Reset();
        erratic.Reset();
        rotating.Reset();
    }

    public void RecordShot(TargetType type, bool wasRotating)
    {
        totalShots++;
        ForType(type).shots++;
        if (wasRotating) rotating.shots++;
    }

    public void RecordHit(TargetType type, bool wasRotating, int pts, float timeToHitSeconds)
    {
        totalHits++;
        totalPoints += pts;

        var ts = ForType(type);
        ts.hits++;
        ts.points += pts;
        if (timeToHitSeconds > 0f)
            ts.totalTimeToHitSeconds += timeToHitSeconds;

        if (wasRotating)
        {
            rotating.hits++;
            rotating.points += pts;
            if (timeToHitSeconds > 0f)
                rotating.totalTimeToHitSeconds += timeToHitSeconds;
        }
    }

    public void RecordCloseMiss(TargetType type, bool wasRotating)
    {
        ForType(type).closeMisses++;
        if (wasRotating) rotating.closeMisses++;
    }

    public void RecordExpired(TargetType type, bool wasRotating)
    {
        totalExpired++;
        ForType(type).expired++;
        if (wasRotating) rotating.expired++;
    }

    public void RecordTargetSpawned(bool wasRotating)
    {
        totalTargetsSpawned++;
    }

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
