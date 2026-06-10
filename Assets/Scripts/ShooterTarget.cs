using UnityEngine;

public enum TargetType { Stationary, Moving, Erratic }

/// <summary>
/// A shooting-range target.  Three movement modes:
///   Stationary — sits still at its placed position.
///   Moving     — slides back-and-forth along a straight path.
///   Erratic    — Lissajous-style 3-axis sine trajectory: circles, curves,
///                depth changes — full 3D freedom.
/// Managed (spawn / despawn / respawn) by ShooterTargetManager.
/// </summary>
public class ShooterTarget : MonoBehaviour
{
    [Header("Identity")]
    public TargetType targetType = TargetType.Moving;

    [Header("Scoring (distance from centre → points)")]
    public int   centerPoints = 100;
    public int   innerPoints  =  60;
    public int   middlePoints =  30;
    public int   outerPoints  =  10;
    public float targetRadius = 0.5f;

    // ── Moving ────────────────────────────────────────────────────────────────
    [Header("Moving")]
    public Vector3 pointA;
    public Vector3 pointB;
    public float   moveSpeed = 2f;

    // ── Erratic ───────────────────────────────────────────────────────────────
    [Header("Erratic")]
    [Tooltip("World-space centre of the erratic trajectory.")]
    public Vector3 erraticCenter;
    [Tooltip("Half-amplitude on each axis (metres).")]
    public Vector3 erraticAmplitude = new Vector3(4f, 1.5f, 3f);
    [Tooltip("Angular frequency on each axis (radians per second).")]
    public Vector3 erraticFrequency = new Vector3(0.9f, 0.6f, 0.4f);
    [Tooltip("Phase offset on each axis (randomised on enable).")]
    public Vector3 erraticPhase;

    // ── Private state ─────────────────────────────────────────────────────────
    private float _t;
    private int   _dir = 1;
    private float _time;

    /// <summary>Time.time when this target was last activated (spawn or respawn).</summary>
    public float LastSpawnTime { get; private set; }

    // ── Unity events ──────────────────────────────────────────────────────────

    void OnEnable()
    {
        LastSpawnTime = Time.time;

        // Randomise phase/position so targets don't all start at the same point
        _t    = Random.Range(0f, 1f);
        _dir  = Random.value > 0.5f ? 1 : -1;
        _time = Random.Range(0f, 200f);

        erraticPhase = new Vector3(
            Random.Range(0f, Mathf.PI * 2f),
            Random.Range(0f, Mathf.PI * 2f),
            Random.Range(0f, Mathf.PI * 2f));
    }

    void Update()
    {
        switch (targetType)
        {
            case TargetType.Moving:  UpdateMoving();  break;
            case TargetType.Erratic: UpdateErratic(); break;
            // Stationary: nothing
        }
    }

    void UpdateMoving()
    {
        float dist = Mathf.Max(0.01f, Vector3.Distance(pointA, pointB));
        _t += _dir * moveSpeed * Time.deltaTime / dist;
        if      (_t >= 1f) { _t = 1f; _dir = -1; }
        else if (_t <= 0f) { _t = 0f; _dir =  1; }
        transform.position = Vector3.Lerp(pointA, pointB, _t);
    }

    void UpdateErratic()
    {
        _time += Time.deltaTime;
        transform.position = new Vector3(
            erraticCenter.x + erraticAmplitude.x * Mathf.Sin(erraticFrequency.x * _time + erraticPhase.x),
            erraticCenter.y + erraticAmplitude.y * Mathf.Sin(erraticFrequency.y * _time + erraticPhase.y),
            erraticCenter.z + erraticAmplitude.z * Mathf.Sin(erraticFrequency.z * _time + erraticPhase.z));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public int CalculatePoints(Vector3 hitWorldPos)
    {
        Vector3 local = transform.InverseTransformPoint(hitWorldPos);
        float   dist  = new Vector2(local.x, local.y).magnitude;
        float   norm  = dist / targetRadius;

        if (norm < 0.15f) return centerPoints;
        if (norm < 0.40f) return innerPoints;
        if (norm < 0.70f) return middlePoints;
        return outerPoints;
    }

    /// <summary>Configure a moving path (called by target manager at spawn).</summary>
    public void SetMovingPath(Vector3 a, Vector3 b, float speed)
    {
        pointA = a; pointB = b; moveSpeed = speed;
        _t = Random.Range(0f, 1f);
        _dir = Random.value > 0.5f ? 1 : -1;
        transform.position = Vector3.Lerp(a, b, _t);
    }

    /// <summary>Configure erratic parameters (called by target manager at spawn).</summary>
    public void SetErraticParams(Vector3 centre, Vector3 amplitude, Vector3 frequency)
    {
        erraticCenter    = centre;
        erraticAmplitude = amplitude;
        erraticFrequency = frequency;
        // Phase is randomised again in OnEnable, but also set it here defensively
        erraticPhase = new Vector3(
            Random.Range(0f, Mathf.PI * 2f),
            Random.Range(0f, Mathf.PI * 2f),
            Random.Range(0f, Mathf.PI * 2f));
    }
}
