using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pre-instantiates a target pool at Start() and keeps instances inactive.
/// Round start uses sequential spawning (random intervals) up to maxConcurrentTargets.
/// Per-target respawn runs while a round is active.
///
/// ML-Agents can tune counts, spawn area, movement/erratic ranges, and spawn pacing
/// via the public fields on this component.
/// </summary>
public class ShooterTargetManager : MonoBehaviour
{
    [Header("Spawn area")]
    public Vector3 areaCenter = new Vector3(0f, 3.5f, 22f);
    public Vector3 areaSize   = new Vector3(22f, 5f, 20f);

    [Header("Target pool (pre-instantiated per type)")]
    public int stationaryCount = 4;
    public int movingCount     = 4;
    public int erraticCount    = 4;

    [Header("Sequential spawn (round start)")]
    [Tooltip("Max targets active at once. Initial fill spawns one-by-one until this cap.")]
    public int   maxConcurrentTargets = 10;
    public float minSpawnInterval     = 0.8f;
    public float maxSpawnInterval     = 2.5f;

    [Header("Moving target settings")]
    public float minMoveSpeed  = 1.5f;
    public float maxMoveSpeed  = 5f;
    public float minPathLength = 3f;
    public float maxPathLength = 9f;

    [Header("Erratic target settings (amplitudes in metres, frequency in rad/s)")]
    public float erraticMinAmpX = 3f;  public float erraticMaxAmpX = 6f;
    public float erraticMinAmpY = 0.8f; public float erraticMaxAmpY = 2.2f;
    public float erraticMinAmpZ = 2f;  public float erraticMaxAmpZ = 6f;
    public float erraticMinFreq = 0.4f; public float erraticMaxFreq = 1.8f;

    [Header("Respawn")]
    public float respawnDelay = 5f;

    [Header("References")]
    public GameObject          targetPrefab;
    public ShooterRoundManager roundManager;

    public IReadOnlyList<ShooterTarget> ActiveTargetsList => _active;

    private readonly List<ShooterTarget> _all    = new List<ShooterTarget>();
    private readonly List<ShooterTarget> _active = new List<ShooterTarget>();
    private Coroutine _spawnCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Start()
    {
        if (targetPrefab == null)
        {
            Debug.LogError("[Shooter] TargetManager: no targetPrefab assigned.");
            return;
        }
        PreInstantiate(TargetType.Stationary, stationaryCount);
        PreInstantiate(TargetType.Moving,     movingCount);
        PreInstantiate(TargetType.Erratic,    erraticCount);
    }

    void PreInstantiate(TargetType type, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject go = Instantiate(targetPrefab, Vector3.zero,
                                        Quaternion.Euler(0f, 180f, 0f), transform);
            ShooterTarget st = go.GetComponent<ShooterTarget>();
            st.targetType = type;
            go.SetActive(false);
            _all.Add(st);
        }
    }

    // ── Round control ─────────────────────────────────────────────────────────

    /// <summary>Begin round: deactivate pool, then fill sequentially to maxConcurrentTargets.</summary>
    public void BeginRoundSpawning()
    {
        StopSpawning();
        DespawnAllTargets();
        _spawnCoroutine = StartCoroutine(SequentialSpawnRoutine());
    }

    /// <summary>Legacy immediate spawn — prefer BeginRoundSpawning.</summary>
    public void SpawnAllTargets()
    {
        StopSpawning();
        _active.Clear();
        foreach (var t in _all)
            t.gameObject.SetActive(false);

        int cap = Mathf.Min(maxConcurrentTargets, _all.Count);
        var shuffled = new List<ShooterTarget>(_all);
        Shuffle(shuffled);

        for (int i = 0; i < cap; i++)
            ActivateTarget(shuffled[i]);
    }

    public void DespawnAllTargets()
    {
        StopSpawning();
        foreach (var t in _all)
            t.gameObject.SetActive(false);
        _active.Clear();
    }

    public void ScheduleRespawn(ShooterTarget target)
    {
        target.gameObject.SetActive(false);
        _active.Remove(target);
        StartCoroutine(RespawnCoroutine(target));
    }

    /// <summary>
    /// Spawn a single target of the given type at a specific position.
    /// Used by ShooterFlowAgent to place targets precisely.
    /// Returns true if a target was successfully spawned.
    /// </summary>
    public bool SpawnSingleTarget(TargetType type, Vector3 position)
    {
        if (_active.Count >= maxConcurrentTargets) return false;

        var inactive = _all.FindAll(t => t.targetType == type && !t.gameObject.activeInHierarchy);
        if (inactive.Count == 0)
        {
            inactive = GetInactiveTargets();
            if (inactive.Count == 0) return false;
        }

        ShooterTarget target = inactive[Random.Range(0, inactive.Count)];

        switch (type)
        {
            case TargetType.Stationary:
                target.transform.position = ClampToArea(position);
                target.gameObject.SetActive(true);
                _active.Add(target);
                break;
            case TargetType.Moving:
                float pathLen = Random.Range(minPathLength, maxPathLength);
                Vector3 dir = Random.onUnitSphere;
                dir.y *= 0.3f;
                dir.Normalize();
                target.SetMovingPath(
                    ClampToArea(position - dir * pathLen * 0.5f),
                    ClampToArea(position + dir * pathLen * 0.5f),
                    Random.Range(minMoveSpeed, maxMoveSpeed));
                target.gameObject.SetActive(true);
                _active.Add(target);
                break;
            case TargetType.Erratic:
                float ampX = Random.Range(erraticMinAmpX, erraticMaxAmpX);
                float ampY = Random.Range(erraticMinAmpY, erraticMaxAmpY);
                float ampZ = Random.Range(erraticMinAmpZ, erraticMaxAmpZ);
                Vector3 clamped = ClampToArea(position);
                float baseFreq = Random.Range(erraticMinFreq, erraticMaxFreq);
                target.SetErraticParams(
                    clamped,
                    new Vector3(ampX, ampY, ampZ),
                    new Vector3(baseFreq, baseFreq * 0.618f, baseFreq * 1.414f));
                target.gameObject.SetActive(true);
                _active.Add(target);
                break;
        }

        return true;
    }

    public void StopSpawning()
    {
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }
    }

    // ── Sequential spawn ──────────────────────────────────────────────────────

    IEnumerator SequentialSpawnRoutine()
    {
        int cap = Mathf.Min(maxConcurrentTargets, _all.Count);

        while (_active.Count < cap)
        {
            if (roundManager != null && roundManager.CurrentState != RoundState.Active)
                yield break;

            var inactive = GetInactiveTargets();
            if (inactive.Count == 0)
                yield break;

            ShooterTarget pick = inactive[Random.Range(0, inactive.Count)];
            ActivateTarget(pick);

            float wait = Random.Range(minSpawnInterval, maxSpawnInterval);
            yield return new WaitForSeconds(wait);
        }

        _spawnCoroutine = null;
    }

    List<ShooterTarget> GetInactiveTargets()
    {
        var list = new List<ShooterTarget>();
        for (int i = 0; i < _all.Count; i++)
        {
            if (!_all[i].gameObject.activeInHierarchy)
                list.Add(_all[i]);
        }
        return list;
    }

    void ActivateTarget(ShooterTarget t)
    {
        switch (t.targetType)
        {
            case TargetType.Stationary:
                var sGroup = _all.FindAll(x => x.targetType == TargetType.Stationary);
                ActivateStationary(t, sGroup.IndexOf(t), sGroup.Count);
                break;
            case TargetType.Moving:
                ActivateMoving(t);
                break;
            case TargetType.Erratic:
                ActivateErratic(t);
                break;
        }
    }

    // ── Spawn helpers ─────────────────────────────────────────────────────────

    void ActivateStationary(ShooterTarget t, int index, int total)
    {
        float zone  = areaSize.x / Mathf.Max(1, total);
        float zoneL = (areaCenter.x - areaSize.x * 0.5f) + zone * index;
        float x     = Random.Range(zoneL + zone * 0.1f, zoneL + zone * 0.9f);
        float y     = Random.Range(areaCenter.y - areaSize.y * 0.4f,
                                   areaCenter.y + areaSize.y * 0.4f);
        float z     = Random.Range(areaCenter.z + areaSize.z * 0.05f,
                                   areaCenter.z + areaSize.z * 0.45f);

        t.transform.position = new Vector3(x, y, z);
        t.gameObject.SetActive(true);
        _active.Add(t);
    }

    void ActivateMoving(ShooterTarget t)
    {
        Vector3 centre  = RandomInArea();
        float   pathLen = Random.Range(minPathLength, maxPathLength);
        Vector3 dir     = Random.onUnitSphere;
        dir.y *= 0.3f;
        dir.Normalize();

        t.SetMovingPath(
            ClampToArea(centre - dir * pathLen * 0.5f),
            ClampToArea(centre + dir * pathLen * 0.5f),
            Random.Range(minMoveSpeed, maxMoveSpeed));

        t.gameObject.SetActive(true);
        _active.Add(t);
    }

    void ActivateErratic(ShooterTarget t)
    {
        float ampX = Random.Range(erraticMinAmpX, erraticMaxAmpX);
        float ampY = Random.Range(erraticMinAmpY, erraticMaxAmpY);
        float ampZ = Random.Range(erraticMinAmpZ, erraticMaxAmpZ);

        float cx = Mathf.Clamp(RandomInArea().x,
                               areaCenter.x - areaSize.x * 0.5f + ampX,
                               areaCenter.x + areaSize.x * 0.5f - ampX);
        float cy = Mathf.Clamp(RandomInArea().y,
                               areaCenter.y - areaSize.y * 0.5f + ampY,
                               areaCenter.y + areaSize.y * 0.5f - ampY);
        float cz = Mathf.Clamp(RandomInArea().z,
                               areaCenter.z - areaSize.z * 0.5f + ampZ,
                               areaCenter.z + areaSize.z * 0.5f - ampZ);

        float baseFreq = Random.Range(erraticMinFreq, erraticMaxFreq);
        t.SetErraticParams(
            new Vector3(cx, cy, cz),
            new Vector3(ampX, ampY, ampZ),
            new Vector3(baseFreq,
                        baseFreq * 0.618f,
                        baseFreq * 1.414f));

        t.gameObject.SetActive(true);
        _active.Add(t);
    }

    // ── Respawn coroutine ─────────────────────────────────────────────────────

    IEnumerator RespawnCoroutine(ShooterTarget target)
    {
        yield return new WaitForSeconds(respawnDelay);

        if (roundManager == null || roundManager.CurrentState != RoundState.Active)
            yield break;

        ActivateTarget(target);
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    Vector3 RandomInArea()
    {
        Vector3 h = areaSize * 0.5f;
        return areaCenter + new Vector3(
            Random.Range(-h.x, h.x),
            Random.Range(-h.y, h.y),
            Random.Range(-h.z, h.z));
    }

    Vector3 ClampToArea(Vector3 p)
    {
        Vector3 h = areaSize * 0.5f;
        p.x = Mathf.Clamp(p.x, areaCenter.x - h.x, areaCenter.x + h.x);
        p.y = Mathf.Clamp(p.y, areaCenter.y - h.y, areaCenter.y + h.y);
        p.z = Mathf.Clamp(p.z, areaCenter.z - h.z, areaCenter.z + h.z);
        return p;
    }
}
