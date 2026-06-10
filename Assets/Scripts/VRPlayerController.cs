using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Selects what the right index trigger does.
/// </summary>
public enum TriggerMode
{
    SpawnBall,  // TestScene — fires a physics ball from the HMD
    None,       // Seal scene — trigger handled by VRGrabSystem
}

/// <summary>
/// Meta Quest 3 locomotion controller.
/// Attach to the OVRCameraRig root alongside a CharacterController.
///
/// Controls (right controller):
///   Thumbstick        — walk / strafe (HMD-relative)
///   Grip (middle)     — jump
///   Index trigger     — SpawnBall: fire a ball | None: do nothing
///   A / X             — quit application
///   B / Y             — restart scene
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(OVRCameraRig))]
public class VRPlayerController : MonoBehaviour
{
    // ── Movement ──────────────────────────────────────────────────────────────

    [Header("Movement")]
    public float moveSpeed = 2.5f;

    [Header("Jump")]
    public float jumpForce = 5f;

    // ── Trigger mode ──────────────────────────────────────────────────────────

    [Header("Trigger Mode")]
    public TriggerMode triggerMode = TriggerMode.SpawnBall;

    // ── Ball spawning ─────────────────────────────────────────────────────────

    [Header("Ball — SpawnBall mode")]
    public GameObject ballPrefab;
    public float      ballLaunchSpeed = 10f;
    public float      spawnDistance   = 0.5f;

    // ── Private state ─────────────────────────────────────────────────────────

    private CharacterController _cc;
    private OVRCameraRig        _rig;
    private float               _verticalVelocity;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Start()
    {
        _cc  = GetComponent<CharacterController>();
        _rig = GetComponent<OVRCameraRig>();
    }

    void Update()
    {
        HandleMovement();
        HandleTrigger();
        HandleSceneButtons();
    }

    // ── Movement + jump ───────────────────────────────────────────────────────

    void HandleMovement()
    {
        if (_cc.isGrounded)
        {
            _verticalVelocity = -1f;

            if (OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger))
                _verticalVelocity = jumpForce;
        }
        else
        {
            _verticalVelocity += Physics.gravity.y * Time.deltaTime;
        }

        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);

        if (stick.sqrMagnitude < 0.01f)
        {
            _cc.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
            return;
        }

        Transform hmd     = _rig.centerEyeAnchor;
        Vector3   forward = Vector3.ProjectOnPlane(hmd.forward, Vector3.up).normalized;
        Vector3   right   = Vector3.ProjectOnPlane(hmd.right,   Vector3.up).normalized;

        Vector3 horizontal = (forward * stick.y + right * stick.x) * moveSpeed;
        _cc.Move(new Vector3(horizontal.x, _verticalVelocity, horizontal.z) * Time.deltaTime);
    }

    // ── Trigger ───────────────────────────────────────────────────────────────

    void HandleTrigger()
    {
        if (triggerMode == TriggerMode.SpawnBall)
            HandleBallSpawn();
        // TriggerMode.None: VRGrabSystem handles the index triggers in the Seal scene
    }

    void HandleBallSpawn()
    {
        if (!OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger)) return;

        if (ballPrefab == null)
        {
            Debug.LogWarning("[VRPlayerController] ballPrefab not assigned.");
            return;
        }

        Transform  hmd  = _rig.centerEyeAnchor;
        GameObject ball = Instantiate(ballPrefab,
                                      hmd.position + hmd.forward * spawnDistance,
                                      Quaternion.identity);

        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
            rb.velocity = hmd.forward * ballLaunchSpeed;

        Destroy(ball, 10f);
    }

    // ── Scene management (works in all scenes) ────────────────────────────────

    void HandleSceneButtons()
    {
        // B (right controller) or Y (left controller) → restart current scene
        if (OVRInput.GetDown(OVRInput.Button.Two) ||
            OVRInput.GetDown(OVRInput.Button.Four))
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

        // A (right controller) or X (left controller) → quit
        if (OVRInput.GetDown(OVRInput.Button.One) ||
            OVRInput.GetDown(OVRInput.Button.Three))
            Application.Quit();
    }
}
