using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Flat, JSON-serialisable round summary for ML-Agents / offline analysis.
/// Produced at end of each round by ShooterRoundManager.
/// </summary>
[Serializable]
public class TargetTypeMetrics
{
    public string type;
    public int    hits;
    public int    closeMisses;
    public int    points;
    public float  avgPointsPerHit;
    public float  avgTimeToHitSeconds;
    public float  totalTimeToHitSeconds;

    public static TargetTypeMetrics From(TargetType type, TargetTypeStats stats)
    {
        return new TargetTypeMetrics
        {
            type                    = type.ToString().ToLowerInvariant(),
            hits                    = stats.hits,
            closeMisses             = stats.closeMisses,
            points                  = stats.points,
            avgPointsPerHit         = stats.AvgPointsPerHit,
            avgTimeToHitSeconds     = stats.AvgTimeToHit,
            totalTimeToHitSeconds   = stats.totalTimeToHitSeconds
        };
    }
}

[Serializable]
public class ShooterRoundReport
{
    public string playerName;
    public string timestampUtc;
    public float  roundDurationSeconds;

    public int   totalShots;
    public int   totalHits;
    public int   totalMisses;
    public float hitRate;
    public int   totalPoints;
    public float avgPointsPerHit;

    public TargetTypeMetrics stationary;
    public TargetTypeMetrics moving;
    public TargetTypeMetrics erratic;

    public static ShooterRoundReport FromStats(ShooterStats stats, string playerName,
        float roundDurationSeconds)
    {
        return new ShooterRoundReport
        {
            playerName            = playerName ?? "",
            timestampUtc          = DateTime.UtcNow.ToString("o"),
            roundDurationSeconds  = roundDurationSeconds,
            totalShots            = stats.totalShots,
            totalHits             = stats.totalHits,
            totalMisses           = stats.TotalMisses,
            hitRate               = stats.HitRate,
            totalPoints           = stats.totalPoints,
            avgPointsPerHit       = stats.AvgPointsPerHit,
            stationary            = TargetTypeMetrics.From(TargetType.Stationary, stats.stationary),
            moving                = TargetTypeMetrics.From(TargetType.Moving,     stats.moving),
            erratic               = TargetTypeMetrics.From(TargetType.Erratic,    stats.erratic)
        };
    }

    public string ToJson(bool pretty = true) =>
        JsonUtility.ToJson(this, pretty);

    /// <summary>Append round JSON to persistentDataPath/shooter_rounds/ for ML pipelines.</summary>
    public void SaveToDisk()
    {
        string dir = Path.Combine(Application.persistentDataPath, "shooter_rounds");
        Directory.CreateDirectory(dir);
        string safePlayer = string.IsNullOrEmpty(playerName) ? "anonymous" : playerName;
        foreach (char c in Path.GetInvalidFileNameChars())
            safePlayer = safePlayer.Replace(c, '_');

        string file = $"round_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{safePlayer}.json";
        string path = Path.Combine(dir, file);
        File.WriteAllText(path, ToJson());
        Debug.Log($"[Shooter] Round report saved → {path}");
    }
}
