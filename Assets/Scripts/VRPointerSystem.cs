using UnityEngine;

/// <summary>
/// Dual-hand VR pointer used during the player-select lobby.
/// Both controllers show beams; either index trigger clicks the hovered target.
/// Disabled once gameplay begins (gun + ShooterInteractSystem take over).
/// </summary>
[RequireComponent(typeof(OVRCameraRig))]
public class VRPointerSystem : MonoBehaviour
{
    [Header("Beam")]
    public float beamLength = 12f;
    public float beamWidth  = 0.003f;
    public Color colorNormal = new Color(0.2f, 0.75f, 1f, 0.65f);
    public Color colorHover  = new Color(1f, 0.9f, 0.2f, 0.95f);

    private OVRCameraRig _rig;

    private struct HandState
    {
        public LineRenderer beam;
        public Material     mat;
        public IVRPointerTarget hovered;
        public bool triggerWasUp;
    }

    private HandState _left;
    private HandState _right;

    public bool IsActive { get; private set; } = true;

    void Start()
    {
        _rig = GetComponent<OVRCameraRig>();
        _left  = CreateHandBeam("LeftPointerBeam",  colorNormal);
        _right = CreateHandBeam("RightPointerBeam", colorNormal);
    }

    void Update()
    {
        if (!IsActive || _rig == null) return;

        UpdateHand(_rig.leftControllerAnchor,  ref _left,  OVRInput.Axis1D.PrimaryIndexTrigger);
        UpdateHand(_rig.rightControllerAnchor, ref _right, OVRInput.Axis1D.SecondaryIndexTrigger);
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        if (_left.beam  != null) _left.beam.enabled  = active;
        if (_right.beam != null) _right.beam.enabled = active;

        if (!active)
        {
            ClearHover(ref _left);
            ClearHover(ref _right);
        }
    }

    void UpdateHand(Transform anchor, ref HandState hand, OVRInput.Axis1D triggerAxis)
    {
        if (anchor == null || hand.beam == null) return;

        Vector3 origin = anchor.position;
        Vector3 dir    = anchor.forward;
        Vector3 end    = origin + dir * beamLength;

        IVRPointerTarget hit = null;
        if (Physics.Raycast(origin, dir, out RaycastHit rh, beamLength))
        {
            hit = rh.collider.GetComponent<IVRPointerTarget>()
               ?? rh.collider.GetComponentInParent<IVRPointerTarget>();
            if (hit != null) end = rh.point;
        }

        if (hit != hand.hovered)
        {
            hand.hovered?.SetPointerHovered(false);
            hand.hovered = hit;
            hand.hovered?.SetPointerHovered(true);
        }

        hand.mat.color = hit != null ? colorHover : colorNormal;
        hand.beam.SetPosition(0, origin);
        hand.beam.SetPosition(1, end);

        float trig = OVRInput.Get(triggerAxis);
        if (trig > 0.7f && hand.triggerWasUp)
        {
            hand.triggerWasUp = false;
            hand.hovered?.OnPointerClick();
        }
        else if (trig < 0.1f)
        {
            hand.triggerWasUp = true;
        }
    }

    static void ClearHover(ref HandState hand)
    {
        hand.hovered?.SetPointerHovered(false);
        hand.hovered = null;
    }

    HandState CreateHandBeam(string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount     = 2;
        lr.startWidth        = beamWidth;
        lr.endWidth          = beamWidth * 0.3f;
        lr.useWorldSpace     = true;
        lr.alignment         = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;

        var mat = new Material(Shader.Find("Sprites/Default")) { color = color };
        lr.material = mat;

        return new HandState
        {
            beam = lr,
            mat  = mat,
            triggerWasUp = true
        };
    }
}
