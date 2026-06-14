using UnityEngine;

public enum TargetType { Stationary, Moving, Erratic }

public enum RotationSpeed { None = 0, Slow = 1, Medium = 2, Fast = 3 }

public class ShooterTarget : MonoBehaviour
{
    [Header("Identity")]
    public TargetType targetType = TargetType.Moving;

    [Header("Scoring (distance from centre → points)")]
    public int   centerPoints = 10;
    public int   innerPoints  =  5;
    public int   middlePoints =  2;
    public int   outerPoints  =  1;
    public float targetRadius = 0.5f;

    [Header("Rotation")]
    public RotationSpeed rotationSpeed = RotationSpeed.None;

    [Header("Lifespan")]
    public float lifespan = 5f;

    public System.Action<ShooterTarget> OnLifespanExpired;

    // ── Moving ──────────────────────────────────────────────────────────────
    [Header("Moving")]
    public Vector3 pointA;
    public Vector3 pointB;
    public float   moveSpeed = 2f;

    // ── Erratic ─────────────────────────────────────────────────────────────
    [Header("Erratic")]
    [Tooltip("World-space centre of the erratic trajectory.")]
    public Vector3 erraticCenter;
    [Tooltip("Half-amplitude on each axis (metres).")]
    public Vector3 erraticAmplitude = new Vector3(4f, 1.5f, 3f);
    [Tooltip("Angular frequency on each axis (radians per second).")]
    public Vector3 erraticFrequency = new Vector3(0.9f, 0.6f, 0.4f);
    [Tooltip("Phase offset on each axis (randomised on enable).")]
    public Vector3 erraticPhase;

    // ── Private state ───────────────────────────────────────────────────────
    private float _t;
    private int   _dir = 1;
    private float _time;
    private float _rotationDegreesPerSec;

    public float LastSpawnTime { get; private set; }

    // ── Unity events ────────────────────────────────────────────────────────

    void OnEnable()
    {
        LastSpawnTime = Time.time;

        _t    = Random.Range(0f, 1f);
        _dir  = Random.value > 0.5f ? 1 : -1;
        _time = Random.Range(0f, 200f);

        erraticPhase = new Vector3(
            Random.Range(0f, Mathf.PI * 2f),
            Random.Range(0f, Mathf.PI * 2f),
            Random.Range(0f, Mathf.PI * 2f));

        transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        SetRotation(rotationSpeed);
    }

    void Update()
    {
        if (Time.time - LastSpawnTime >= lifespan)
        {
            OnLifespanExpired?.Invoke(this);
            gameObject.SetActive(false);
            return;
        }

        switch (targetType)
        {
            case TargetType.Moving:  UpdateMoving();  break;
            case TargetType.Erratic: UpdateErratic(); break;
        }

        if (rotationSpeed != RotationSpeed.None)
            transform.Rotate(0f, _rotationDegreesPerSec * Time.deltaTime, 0f, Space.Self);
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

    // ── Public API ──────────────────────────────────────────────────────────

    public void SetRotation(RotationSpeed speed)
    {
        rotationSpeed = speed;
        switch (speed)
        {
            case RotationSpeed.Slow:   _rotationDegreesPerSec = 45f;  break;
            case RotationSpeed.Medium: _rotationDegreesPerSec = 120f; break;
            case RotationSpeed.Fast:   _rotationDegreesPerSec = 240f; break;
            default:                   _rotationDegreesPerSec = 0f;   break;
        }
    }

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

    public void SetMovingPath(Vector3 a, Vector3 b, float speed)
    {
        pointA = a; pointB = b; moveSpeed = speed;
        _t = Random.Range(0f, 1f);
        _dir = Random.value > 0.5f ? 1 : -1;
        transform.position = Vector3.Lerp(a, b, _t);
    }

    public void SetErraticParams(Vector3 centre, Vector3 amplitude, Vector3 frequency)
    {
        erraticCenter    = centre;
        erraticAmplitude = amplitude;
        erraticFrequency = frequency;
        erraticPhase = new Vector3(
            Random.Range(0f, Mathf.PI * 2f),
            Random.Range(0f, Mathf.PI * 2f),
            Random.Range(0f, Mathf.PI * 2f));
    }
}
