using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShooterTargetManager : MonoBehaviour
{
    [Header("Spawn area")]
    public Vector3 areaCenter = new Vector3(0f, 3.5f, 22f);
    public Vector3 areaSize   = new Vector3(22f, 5f, 20f);

    [Header("Target pool")]
    public int poolSize = 12;

    [Header("Spawn control (set by AdaptiveSpawnController)")]
    public float spawnDelay = 3f;
    public TargetType nextTargetType = TargetType.Stationary;
    public RotationSpeed nextRotation = RotationSpeed.None;

    [Header("Moving target settings")]
    public float minMoveSpeed  = 1.5f;
    public float maxMoveSpeed  = 5f;
    public float minPathLength = 3f;
    public float maxPathLength = 9f;

    [Header("Erratic target settings")]
    public float erraticMinAmpX = 3f;  public float erraticMaxAmpX = 6f;
    public float erraticMinAmpY = 0.8f; public float erraticMaxAmpY = 2.2f;
    public float erraticMinAmpZ = 2f;  public float erraticMaxAmpZ = 6f;
    public float erraticMinFreq = 0.4f; public float erraticMaxFreq = 1.8f;

    [Header("References")]
    public GameObject targetPrefab;
    public ShooterRoundManager roundManager;
    public AdaptiveSpawnController adaptiveController;
    public ShooterEventLog eventLog;

    public IReadOnlyList<ShooterTarget> ActiveTargetsList => _active;
    public int ActiveCount => _active.Count;

    private readonly List<ShooterTarget> _all    = new List<ShooterTarget>();
    private readonly List<ShooterTarget> _active = new List<ShooterTarget>();
    private Coroutine _spawnCoroutine;

    void Start()
    {
        if (targetPrefab == null)
        {
            Debug.LogError("[Shooter] TargetManager: no targetPrefab assigned.");
            return;
        }
        PreInstantiatePool();
    }

    void PreInstantiatePool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = Instantiate(targetPrefab, Vector3.zero,
                                        Quaternion.Euler(0f, 180f, 0f), transform);
            ShooterTarget st = go.GetComponent<ShooterTarget>();
            go.SetActive(false);
            _all.Add(st);
        }
    }

    // ── Round control ───────────────────────────────────────────────────────────

    public void BeginRoundSpawning()
    {
        StopSpawning();
        DespawnAllTargets();
        _spawnCoroutine = StartCoroutine(SequentialSpawnRoutine());
    }

    public void DespawnAllTargets()
    {
        StopSpawning();
        foreach (var t in _all)
        {
            t.OnLifespanExpired = null;
            t.gameObject.SetActive(false);
        }
        _active.Clear();
    }

    public void StopSpawning()
    {
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }
    }

    public void ScheduleRespawn(ShooterTarget target)
    {
        target.OnLifespanExpired = null;
        target.gameObject.SetActive(false);
        _active.Remove(target);
    }

    public bool SpawnSingleTarget(TargetType type, RotationSpeed rotation, Vector3 position)
    {
        ShooterTarget target = FindInactive();
        if (target == null) return false;

        ConfigureAndActivate(target, type, rotation, position);
        return true;
    }

    // ── Sequential spawn ────────────────────────────────────────────────────────

    IEnumerator SequentialSpawnRoutine()
    {
        while (true)
        {
            if (roundManager != null && roundManager.CurrentState != RoundState.Active)
                yield break;

            ShooterTarget target = FindInactive();
            if (target == null)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            if (adaptiveController != null)
                adaptiveController.ConfigureNextSpawn();

            Vector3 pos = RandomInArea();
            ConfigureAndActivate(target, nextTargetType, nextRotation, pos);

            yield return new WaitForSeconds(spawnDelay);
        }
    }

    ShooterTarget FindInactive()
    {
        for (int i = 0; i < _all.Count; i++)
        {
            if (!_all[i].gameObject.activeInHierarchy)
                return _all[i];
        }
        return null;
    }

    // ── Configuration & activation ──────────────────────────────────────────────

    void ConfigureAndActivate(ShooterTarget t, TargetType type, RotationSpeed rotation, Vector3 position)
    {
        t.targetType = type;
        t.rotationSpeed = rotation;
        t.lifespan = 5f;

        switch (type)
        {
            case TargetType.Stationary:
                t.transform.position = ClampToArea(position);
                break;
            case TargetType.Moving:
                SetupMovingPath(t, position);
                break;
            case TargetType.Erratic:
                SetupErratic(t, position);
                break;
        }

        t.SetRotation(rotation);
        t.OnLifespanExpired = HandleLifespanExpired;
        t.gameObject.SetActive(true);
        _active.Add(t);

        if (roundManager != null)
            roundManager.stats.RecordTargetSpawned(rotation != RotationSpeed.None);

        eventLog?.LogTargetSpawned(type, t.transform.position);
    }

    void SetupMovingPath(ShooterTarget t, Vector3 centre)
    {
        float pathLen = Random.Range(minPathLength, maxPathLength);
        Vector3 dir = Random.onUnitSphere;
        dir.y *= 0.3f;
        dir.Normalize();

        t.SetMovingPath(
            ClampToArea(centre - dir * pathLen * 0.5f),
            ClampToArea(centre + dir * pathLen * 0.5f),
            Random.Range(minMoveSpeed, maxMoveSpeed));
    }

    void SetupErratic(ShooterTarget t, Vector3 position)
    {
        float ampX = Random.Range(erraticMinAmpX, erraticMaxAmpX);
        float ampY = Random.Range(erraticMinAmpY, erraticMaxAmpY);
        float ampZ = Random.Range(erraticMinAmpZ, erraticMaxAmpZ);

        Vector3 h = areaSize * 0.5f;
        float cx = Mathf.Clamp(position.x, areaCenter.x - h.x + ampX, areaCenter.x + h.x - ampX);
        float cy = Mathf.Clamp(position.y, areaCenter.y - h.y + ampY, areaCenter.y + h.y - ampY);
        float cz = Mathf.Clamp(position.z, areaCenter.z - h.z + ampZ, areaCenter.z + h.z - ampZ);

        float baseFreq = Random.Range(erraticMinFreq, erraticMaxFreq);
        t.SetErraticParams(
            new Vector3(cx, cy, cz),
            new Vector3(ampX, ampY, ampZ),
            new Vector3(baseFreq, baseFreq * 0.618f, baseFreq * 1.414f));
    }

    // ── Lifespan expiry ─────────────────────────────────────────────────────────

    void HandleLifespanExpired(ShooterTarget target)
    {
        target.OnLifespanExpired = null;
        target.gameObject.SetActive(false);
        _active.Remove(target);

        if (roundManager != null)
            roundManager.HandleTargetExpired(target);
    }

    // ── Utilities ───────────────────────────────────────────────────────────────

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
