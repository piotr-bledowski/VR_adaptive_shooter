using UnityEngine;

/// <summary>
/// Gun controller for the Shooter scene.
///
///   Right index trigger  — fires ONE bullet per press (trigger must be released first).
///   Aiming beam          — red; turns yellow on target; flashes white on fire.
///   Each shot reports to ShooterRoundManager for stat tracking + near-miss detection.
///   Muzzle sparks + point light play on every shot.
/// </summary>
public class ShooterGun : MonoBehaviour
{
    [Header("Bullet")]
    public GameObject          bulletPrefab;
    public float               bulletSpeed   = 400f;
    public float               bulletLifetime = 3f;

    [Header("Aiming beam")]
    public float maxRange     = 200f;
    public float beamWidth    = 0.003f;
    public Color beamNormal   = new Color(1f,   0.2f, 0.2f, 0.60f);
    public Color beamOnTarget = new Color(1f,   1f,   0.1f, 0.95f);
    public Color beamFired    = new Color(1f,   1f,   1f,   1.00f);

    [Header("Muzzle flash")]
    public float muzzleFlashTime      = 0.07f;
    public float muzzleLightIntensity = 9f;
    public float muzzleLightRange     = 5f;

    [Header("References")]
    public Transform           muzzlePoint;
    public ShooterRoundManager roundManager;

    [Header("Muzzle tuning (Inspector)")]
    [Tooltip("Extra offset in MuzzlePoint local space (metres). X=right, Y=up, Z=forward along barrel.")]
    public Vector3 muzzleLocalOffset = new Vector3(0f, 0.04775f, 0f);
    [Tooltip("Extra rotation on top of MuzzlePoint, in local Euler degrees.")]
    public Vector3 muzzleLocalRotation = Vector3.zero;

    // ── Private ───────────────────────────────────────────────────────────────

    private LineRenderer  _lr;
    private Material      _mat;
    private float         _flashTimer;
    private bool          _triggerWasUp  = true;
    private bool          _beamOnTarget; // cached from last RefreshAimBeam
    private ParticleSystem _muzzlePS;

    void Start()
    {
        if (muzzlePoint == null) muzzlePoint = transform;

        if (roundManager == null)
            roundManager = FindObjectOfType<ShooterRoundManager>();

        BuildAimBeam();
        BuildMuzzleParticles();
    }

    void Update()
    {
        _flashTimer -= Time.deltaTime;
        RefreshAimBeam();
        HandleFire();
    }

    // ── Aiming beam ───────────────────────────────────────────────────────────

    void RefreshAimBeam()
    {
        if (_lr == null) return;

        GetMuzzlePose(out Vector3 origin, out Vector3 dir);
        Vector3 end = origin + dir * maxRange;

        _beamOnTarget = false;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxRange))
        {
            end = hit.point;
            if (hit.collider.GetComponentInParent<ShooterTarget>() != null)
                _beamOnTarget = true;
        }

        Color col = _flashTimer > 0f ? beamFired
                  : _beamOnTarget    ? beamOnTarget
                                     : beamNormal;
        _mat.color = col;
        _lr.SetPosition(0, origin);
        _lr.SetPosition(1, end);
    }

    // ── Fire ──────────────────────────────────────────────────────────────────

    void HandleFire()
    {
        float trig = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger);
        bool  down = trig > 0.7f;

        if (down && _triggerWasUp)
        {
            _triggerWasUp = false;
            Fire();
        }
        else if (!down)
        {
            _triggerWasUp = true;
        }
    }

    void Fire()
    {
        _flashTimer = muzzleFlashTime;

        GetMuzzlePose(out Vector3 origin, out Vector3 dir);
        Quaternion muzzleRot = Quaternion.LookRotation(dir);

        // ── Report shot to round manager (stats + near-miss check) ────────────
        roundManager?.HandleShotFired(origin, dir, _beamOnTarget);

        // ── Spawn bullet ──────────────────────────────────────────────────────
        if (bulletPrefab != null)
        {
            GameObject bGo = Object.Instantiate(bulletPrefab, origin, muzzleRot);
            ShooterBullet b = bGo.GetComponent<ShooterBullet>();
            if (b != null)
            {
                if (roundManager == null)
                    roundManager = FindObjectOfType<ShooterRoundManager>();
                b.lifetime     = bulletLifetime;
                b.roundManager = roundManager;
                b.Init(dir, bulletSpeed);
            }
        }

        // ── Muzzle FX ─────────────────────────────────────────────────────────
        if (_muzzlePS != null) _muzzlePS.Play();
        ShooterBullet.SpawnFlash(origin, muzzleLightIntensity, muzzleLightRange, muzzleFlashTime);
    }

    // ── Build helpers ─────────────────────────────────────────────────────────

    void GetMuzzlePose(out Vector3 position, out Vector3 forward)
    {
        Transform m = muzzlePoint != null ? muzzlePoint : transform;
        Quaternion rot = m.rotation * Quaternion.Euler(muzzleLocalRotation);
        position = m.position + rot * muzzleLocalOffset;
        forward  = rot * Vector3.forward;
    }

    void BuildAimBeam()
    {
        var go = new GameObject("GunBeam");
        go.transform.SetParent(transform, false);

        _lr = go.AddComponent<LineRenderer>();
        _lr.positionCount     = 2;
        _lr.startWidth        = beamWidth;
        _lr.endWidth          = beamWidth * 0.4f;
        _lr.useWorldSpace     = true;
        _lr.alignment         = LineAlignment.View;
        _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lr.receiveShadows    = false;

        _mat = new Material(Shader.Find("Sprites/Default")) { color = beamNormal };
        _lr.material = _mat;
    }

    void BuildMuzzleParticles()
    {
        var go = new GameObject("MuzzleSparks");
        go.transform.SetParent(muzzlePoint, false);
        go.transform.localPosition = Vector3.zero;

        _muzzlePS = go.AddComponent<ParticleSystem>();

        var main = _muzzlePS.main;
        main.duration        = 0.08f;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(2f, 8f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
        main.startColor      = new Color(1f, 0.85f, 0.2f);
        main.gravityModifier = 0.1f;
        main.maxParticles    = 20;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;

        var emission = _muzzlePS.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 15) });

        var shape = _muzzlePS.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = 25f;
        shape.radius    = 0.005f;

        var sizeOL = _muzzlePS.sizeOverLifetime;
        sizeOL.enabled = true;
        sizeOL.size    = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.renderMode  = ParticleSystemRenderMode.Stretch;
        psr.lengthScale = 2f;
        psr.material    = new Material(Shader.Find("Sprites/Default"))
                              { color = new Color(1f, 0.85f, 0.2f) };
    }
}
