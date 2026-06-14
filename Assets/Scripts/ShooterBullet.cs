using UnityEngine;

/// <summary>
/// Bullet projectile fired by ShooterGun.
/// Uses per-frame SphereCast (tunneling-safe at high speed) plus trigger fallback.
/// Hits targets (score + respawn) and solid scenery (impact FX only).
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class ShooterBullet : MonoBehaviour
{
    [HideInInspector] public float               speed    = 80f;
    [HideInInspector] public float               lifetime = 3f;
    [HideInInspector] public ShooterRoundManager roundManager;

    [HideInInspector] public Color  sparkColor                 = new Color(1f, 0.85f, 0.1f);
    [HideInInspector] public float  impactLightIntensityMiss      = 7f;
    [HideInInspector] public float  impactLightIntensityTarget     = 18f;
    [HideInInspector] public float  impactLightIntensityBullseye   = 36f;
    [HideInInspector] public float  impactLightDuration           = 0.1f;
    [HideInInspector] public float  impactLightRange              = 7f;
    [Tooltip("Place impact light this far in front of the surface (along bullet path).")]
    [HideInInspector] public float  impactLightFrontOffset        = 0.04f;

    [Tooltip("SphereCast radius in metres (world space).")]
    public float hitRadius = 0.02f;

    private bool    _hit;
    private Vector3 _velocity;

    public void Init(Vector3 direction, float spd)
    {
        speed     = spd;
        _velocity = direction.normalized * speed;
    }

    void Start()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic        = true;
            rb.useGravity         = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        if (roundManager == null)
            roundManager = FindObjectOfType<ShooterRoundManager>();

        if (_velocity.sqrMagnitude < 0.01f)
            _velocity = transform.forward * speed;

        SphereCollider sc = GetComponent<SphereCollider>();
        if (sc != null && hitRadius > 0f)
            sc.radius = hitRadius / Mathf.Max(transform.lossyScale.x, 0.001f);

        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        if (_hit) return;

        Vector3 move = _velocity * Time.deltaTime;
        float   dist = move.magnitude;
        if (dist < 0.0001f) return;

        Vector3 dir = move / dist;

        // Cast from slightly behind current pos so we don't miss when grazing a surface.
        Vector3 castOrigin = transform.position - dir * hitRadius;
        float   castDist   = dist + hitRadius;

        RaycastHit bestHit = default;
        bool       found   = false;
        float      bestD   = float.MaxValue;

        RaycastHit[] hits = Physics.SphereCastAll(castOrigin, hitRadius, dir, castDist,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            if (ShouldIgnore(h.collider)) continue;
            if (h.distance < bestD)
            {
                bestD   = h.distance;
                bestHit = h;
                found   = true;
            }
        }

        if (found)
        {
            transform.position = bestHit.point + bestHit.normal * 0.002f;
            ResolveHit(bestHit.collider, bestHit.point, bestHit.normal);
            return;
        }

        transform.position += move;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_hit || ShouldIgnore(other)) return;
        Vector3 n = (transform.position - other.bounds.center).normalized;
        ResolveHit(other, transform.position, n);
    }

    void ResolveHit(Collider col, Vector3 point, Vector3 normal)
    {
        if (_hit) return;
        _hit = true;

        ShooterTarget target = col.GetComponentInParent<ShooterTarget>();
        int points = 0;
        if (target != null)
        {
            points = target.CalculatePoints(point);
            if (roundManager != null)
                roundManager.HandleBulletHit(target, points);
        }

        Vector3 fxNormal = normal.sqrMagnitude > 0.01f ? normal : -_velocity.normalized;
        SpawnImpactFX(point, fxNormal, target, points);
        Destroy(gameObject);
    }

    static bool ShouldIgnore(Collider col)
    {
        if (col == null) return true;
        if (col.GetComponent<ShooterBullet>() != null) return true;
        if (col.GetComponentInParent<ShooterBullet>() != null) return true;
        if (col.GetComponent<ShooterGun>() != null) return true;
        if (col.GetComponentInParent<ShooterGun>() != null) return true;
        if (col.GetComponent<CharacterController>() != null) return true;
        if (col.GetComponentInParent<CharacterController>() != null) return true;
        // VR UI / interact beams — no mesh colliders typically
        return false;
    }

    void SpawnImpactFX(Vector3 point, Vector3 surfaceNormal, ShooterTarget target, int points)
    {
        Color  sparkColor = this.sparkColor;
        Color  lightColor = new Color(1f, 0.75f, 0.2f);
        float  intensity  = impactLightIntensityMiss;

        if (target != null)
        {
            if (points >= target.centerPoints) // bullseye (10)
            {
                intensity  = impactLightIntensityBullseye;
                lightColor = new Color(1f, 0.06f, 0.02f);
                sparkColor = new Color(1f, 0.15f, 0.05f);
            }
            else if (points >= target.innerPoints) // inner ring (5)
            {
                intensity  = impactLightIntensityTarget;
                lightColor = new Color(1f, 0.6f, 0.05f);
                sparkColor = new Color(1f, 0.65f, 0.10f);
            }
            else
            {
                intensity  = impactLightIntensityTarget * 0.6f;
                lightColor = new Color(1f, 0.95f, 0.12f);
                sparkColor = new Color(1f, 0.92f, 0.18f);
            }
        }

        SpawnImpactSparks(point, surfaceNormal, sparkColor);

        Vector3 approach = -_velocity.normalized;
        Vector3 lightPos = approach.sqrMagnitude > 0.01f
            ? point + approach * impactLightFrontOffset
            : point;
        SpawnFlash(lightPos, intensity, impactLightRange, impactLightDuration, lightColor);
    }

    void SpawnImpactSparks(Vector3 point, Vector3 normal, Color color)
    {
        GameObject go = new GameObject("ImpactSparks");
        go.transform.position = point;
        if (normal.sqrMagnitude > 0.01f)
            go.transform.rotation = Quaternion.LookRotation(normal);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration        = 0.12f;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(4f, 14f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        main.startColor      = color;
        main.gravityModifier = 0.6f;
        main.maxParticles    = 40;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = 55f;
        shape.radius    = 0.01f;

        var sizeOL = ps.sizeOverLifetime;
        sizeOL.enabled = true;
        sizeOL.size    = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.renderMode  = ParticleSystemRenderMode.Stretch;
        psr.lengthScale = 2.5f;
        psr.material    = new Material(Shader.Find("Sprites/Default")) { color = color };

        ps.Play();
        Destroy(go, 1f);
    }

    public static void SpawnFlash(Vector3 pos, float intensity, float range, float duration,
        Color? color = null)
    {
        GameObject go = new GameObject("PointFlash");
        go.transform.position = pos;

        Light l = go.AddComponent<Light>();
        l.type      = LightType.Point;
        l.color     = color ?? new Color(1f, 0.75f, 0.2f);
        l.intensity = intensity;
        l.range     = range;

        Destroy(go, duration);
    }
}
