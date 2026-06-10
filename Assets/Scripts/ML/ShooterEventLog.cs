using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Types of game events recorded in the time-series log.
/// </summary>
public enum ShooterEventType
{
    RoundStart,
    RoundEnd,
    TargetSpawned,
    TargetDespawned,
    ShotFired,
    ShotHit,
    ShotMiss,
    CloseMiss,
    AgentDecision
}

/// <summary>
/// Single timestamped event in the shooter round.
/// Serialised to JSON for ML training data.
/// </summary>
[Serializable]
public class ShooterEvent
{
    public float          timestamp;
    public string         eventType;
    public string         targetType;
    public int            points;
    public float          timeToHit;
    public Vector3Ser     targetPosition;
    public Vector3Ser     playerPosition;
    public int            activeTargetCount;
    public float          hitRate;

    [Serializable]
    public struct Vector3Ser
    {
        public float x, y, z;
        public Vector3Ser(Vector3 v) { x = v.x; y = v.y; z = v.z; }
        public Vector3 ToVector3() => new Vector3(x, y, z);
    }
}

/// <summary>
/// Accumulates a time-series of game events for the current round.
/// Used by ShooterFlowAgent as observations and saved per-player for offline training.
/// </summary>
public class ShooterEventLog : MonoBehaviour
{
    public List<ShooterEvent> Events { get; private set; } = new List<ShooterEvent>();

    private float _roundStartTime;
    private int   _shotsFired;
    private int   _shotsHit;

    public int   ActiveTargetCount { get; set; }
    public float CurrentHitRate => _shotsFired > 0 ? (float)_shotsHit / _shotsFired : 0f;

    public void BeginRound()
    {
        Events.Clear();
        _roundStartTime = Time.time;
        _shotsFired = 0;
        _shotsHit   = 0;
        ActiveTargetCount = 0;
        Log(ShooterEventType.RoundStart);
    }

    public void EndRound()
    {
        Log(ShooterEventType.RoundEnd);
    }

    public void LogTargetSpawned(TargetType type, Vector3 position)
    {
        ActiveTargetCount++;
        Log(ShooterEventType.TargetSpawned, type, position: position);
    }

    public void LogTargetDespawned(TargetType type)
    {
        ActiveTargetCount = Mathf.Max(0, ActiveTargetCount - 1);
        Log(ShooterEventType.TargetDespawned, type);
    }

    public void LogShotFired(Vector3 playerPosition)
    {
        _shotsFired++;
        Log(ShooterEventType.ShotFired, playerPos: playerPosition);
    }

    public void LogShotHit(TargetType type, int points, float timeToHit, Vector3 targetPos)
    {
        _shotsHit++;
        Log(ShooterEventType.ShotHit, type, points, timeToHit, targetPos);
    }

    public void LogShotMiss(Vector3 playerPosition)
    {
        Log(ShooterEventType.ShotMiss, playerPos: playerPosition);
    }

    public void LogCloseMiss(TargetType type)
    {
        Log(ShooterEventType.CloseMiss, type);
    }

    public void LogAgentDecision(TargetType type, Vector3 position)
    {
        Log(ShooterEventType.AgentDecision, type, position: position);
    }

    void Log(ShooterEventType eventType, TargetType targetType = TargetType.Stationary,
             int points = 0, float timeToHit = 0f, Vector3 position = default,
             Vector3 playerPos = default)
    {
        Events.Add(new ShooterEvent
        {
            timestamp         = Time.time - _roundStartTime,
            eventType         = eventType.ToString(),
            targetType        = targetType.ToString(),
            points            = points,
            timeToHit         = timeToHit,
            targetPosition    = new ShooterEvent.Vector3Ser(position),
            playerPosition    = new ShooterEvent.Vector3Ser(playerPos),
            activeTargetCount = ActiveTargetCount,
            hitRate           = CurrentHitRate
        });
    }

    /// <summary>Save event log to disk for offline training.</summary>
    public void SaveToDisk(string playerName)
    {
        string dir = Path.Combine(Application.persistentDataPath, "shooter_events", playerName);
        Directory.CreateDirectory(dir);

        string file = $"events_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        string path = Path.Combine(dir, file);

        var wrapper = new EventLogWrapper { events = Events };
        File.WriteAllText(path, JsonUtility.ToJson(wrapper, true));
        Debug.Log($"[ShooterEventLog] Saved {Events.Count} events → {path}");
    }

    [Serializable]
    private class EventLogWrapper
    {
        public List<ShooterEvent> events;
    }
}
