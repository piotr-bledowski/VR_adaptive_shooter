using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ultrakill-inspired VR locomotion for the Shooter scene.
///
///   Right joystick  — move (HMD-relative)
///   Right grip      — jump (costs stamina, same as dash)
///   Left joystick   — dash (0.5 s cooldown, costs stamina)
///   Left grip       — sprint (hold; drains stamina while moving; stops at 0)
///   B / Y           — restart scene
///   A / X           — quit
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ShooterPlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed   = 5f;
    public float sprintSpeed = 9f;
    public float gravity     = -20f;
    public float jumpForce   = 7f;

    [Header("Dash")]
    public float dashSpeed    = 22f;
    public float dashDuration = 0.15f;
    public float dashCooldown = 0.5f;
    public float dashStaminaCost = 25f;

    [Header("Stamina")]
    public float maxStamina       = 100f;
    public float staminaRegenRate = 20f;
    public float sprintDrainRate  = 18f;
    [Tooltip("Seconds after last drain before regen begins.")]
    public float regenDelay       = 0.6f;

    // Exposed for HUD reading
    [HideInInspector] public float currentStamina;

    /// <summary>Disabled during player-select lobby.</summary>
    [HideInInspector] public bool movementEnabled = true;

    private CharacterController _cc;
    private OVRCameraRig        _rig;
    private Transform           _head;
    private Vector3             _velocity;

    private float _lastDashTime   = -10f;
    private float _dashTimer;
    private Vector3 _dashDir;

    private float _lastDrainTime = -10f;

    void Start()
    {
        _cc  = GetComponent<CharacterController>();
        _rig = GetComponent<OVRCameraRig>();
        if (_rig == null) _rig = GetComponentInChildren<OVRCameraRig>();
        currentStamina = maxStamina;
        TryResolveHead();
    }

    void TryResolveHead()
    {
        if (_head != null) return;
        if (_rig != null && _rig.centerEyeAnchor != null)
            _head = _rig.centerEyeAnchor;
    }

    void Update()
    {
        if (_head == null)
        {
            TryResolveHead();
            if (_head == null) return;
        }

        // Scene management always available
        if (OVRInput.GetDown(OVRInput.Button.Two) || OVRInput.GetDown(OVRInput.Button.Four))
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        if (OVRInput.GetDown(OVRInput.Button.One) || OVRInput.GetDown(OVRInput.Button.Three))
            Application.Quit();

        if (!movementEnabled) return;

        bool grounded = _cc.isGrounded;

        // ── Sprint state (drops to walk speed the moment stamina hits 0) ────────
        bool wantsSprint = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger); // left grip
        bool hasStamina  = currentStamina > 0f;
        bool sprinting   = wantsSprint && hasStamina;

        // ── Dash input ──────────────────────────────────────────────────────────
        Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        if (leftStick.magnitude > 0.6f && Time.time - _lastDashTime > dashCooldown
            && currentStamina >= dashStaminaCost && _dashTimer <= 0f)
        {
            Vector3 dashInput = HeadRelativeDirection(leftStick);
            if (dashInput.sqrMagnitude > 0.01f)
            {
                _dashDir      = dashInput.normalized;
                _dashTimer    = dashDuration;
                _lastDashTime = Time.time;
                DrainStamina(dashStaminaCost);
            }
        }

        // ── Dash movement ───────────────────────────────────────────────────────
        if (_dashTimer > 0f)
        {
            _dashTimer -= Time.deltaTime;
            _cc.Move(_dashDir * dashSpeed * Time.deltaTime);
            return; // during a dash, skip normal movement
        }

        // ── Normal movement ─────────────────────────────────────────────────────
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
        Vector3 moveDir    = HeadRelativeDirection(rightStick);

        float speed = sprinting ? sprintSpeed : walkSpeed;
        Vector3 move = moveDir * speed;

        // Stamina drain from sprinting
        if (sprinting && moveDir.sqrMagnitude > 0.01f)
            DrainStamina(sprintDrainRate * Time.deltaTime);

        // ── Gravity + Jump ──────────────────────────────────────────────────────
        if (grounded && _velocity.y < 0f)
            _velocity.y = -2f;

        if (grounded && hasStamina && currentStamina >= dashStaminaCost
            && OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger)) // right grip
        {
            _velocity.y = jumpForce;
            DrainStamina(dashStaminaCost);
        }

        _velocity.y += gravity * Time.deltaTime;
        move.y = _velocity.y;

        _cc.Move(move * Time.deltaTime);

        // ── Stamina regen ───────────────────────────────────────────────────────
        if (Time.time - _lastDrainTime > regenDelay && currentStamina < maxStamina)
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenRate * Time.deltaTime);
        }

    }

    Vector3 HeadRelativeDirection(Vector2 stick)
    {
        if (stick.sqrMagnitude < 0.01f) return Vector3.zero;

        Vector3 forward = _head.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = _head.right;
        right.y = 0f;
        right.Normalize();

        return (forward * stick.y + right * stick.x).normalized * stick.magnitude;
    }

    void DrainStamina(float amount)
    {
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        _lastDrainTime = Time.time;
    }
}
