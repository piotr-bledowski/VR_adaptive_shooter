using System;
using System.IO;
using UnityEngine;

[Serializable]
public class TargetTypeMetrics
{
    public string type;
    public int    hits;
    public int    shots;
    public int    expired;
    public int    closeMisses;
    public int    points;
    public float  hitRate;
    public float  avgPointsPerHit;
    public float  avgTimeToHitSeconds;
    public float  totalTimeToHitSeconds;

    public static TargetTypeMetrics From(string label, TargetTypeStats stats)
    {
        return new TargetTypeMetrics
        {
            type                    = label,
            hits                    = stats.hits,
            shots                   = stats.shots,
            expired                 = stats.expired,
            closeMisses             = stats.closeMisses,
            points                  = stats.points,
            hitRate                 = stats.HitRate,
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
    public int   totalExpired;
    public int   totalTargetsSpawned;
    public float hitRate;
    public int   totalPoints;
    public float avgPointsPerHit;
    public float avgPointsPerTarget;

    public TargetTypeMetrics stationary;
    public TargetTypeMetrics moving;
    public TargetTypeMetrics erratic;
    public TargetTypeMetrics rotating;

    public static ShooterRoundReport FromStats(ShooterStats stats, string playerName,
        float roundDurationSeconds)
    {
        float ppt = stats.totalTargetsSpawned > 0
            ? (float)stats.totalPoints / stats.totalTargetsSpawned : 0f;

        return new ShooterRoundReport
        {
            playerName            = playerName ?? "",
            timestampUtc          = DateTime.UtcNow.ToString("o"),
            roundDurationSeconds  = roundDurationSeconds,
            totalShots            = stats.totalShots,
            totalHits             = stats.totalHits,
            totalMisses           = stats.TotalMisses,
            totalExpired          = stats.totalExpired,
            totalTargetsSpawned   = stats.totalTargetsSpawned,
            hitRate               = stats.HitRate,
            totalPoints           = stats.totalPoints,
            avgPointsPerHit       = stats.AvgPointsPerHit,
            avgPointsPerTarget    = ppt,
            stationary            = TargetTypeMetrics.From("stationary", stats.stationary),
            moving                = TargetTypeMetrics.From("moving",     stats.moving),
            erratic               = TargetTypeMetrics.From("erratic",    stats.erratic),
            rotating              = TargetTypeMetrics.From("rotating",   stats.rotating)
        };
    }

    public string ToJson(bool pretty = true) =>
        JsonUtility.ToJson(this, pretty);

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
    }
}
