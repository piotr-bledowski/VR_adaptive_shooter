using UnityEngine;

/// <summary>
/// Attach to OVRCameraRig.
/// Projects a green beam from the left controller (left index trigger to activate).
/// Finds ShooterButton objects in the scene on hover; activates on trigger press.
/// Sprint is on left grip — no input conflict.
/// </summary>
[RequireComponent(typeof(OVRCameraRig))]
public class ShooterInteractSystem : MonoBehaviour
{
    [Header("Beam")]
    public float beamLength = 14f;
    public float beamWidth  = 0.003f;
    public Color colorNormal   = new Color(0.15f, 0.9f,  0.25f, 0.55f);
    public Color colorOnButton = new Color(1f,    0.90f, 0.10f, 0.95f);

    private OVRCameraRig  _rig;
    private LineRenderer  _lr;
    private Material      _mat;

    private ShooterButton _hovered;
    private bool          _triggerWasUp = true;

    void Start()
    {
        _rig = GetComponent<OVRCameraRig>();
        BuildBeam();
    }

    void Update()
    {
        if (_rig == null) return;

        Transform anchor = _rig.leftControllerAnchor;
        if (anchor == null) return;

        Vector3 origin = anchor.position;
        Vector3 dir    = anchor.forward;
        Vector3 end    = origin + dir * beamLength;

        // Raycast to find a button
        ShooterButton hitBtn = null;
        if (Physics.Raycast(origin, dir, out RaycastHit rh, beamLength))
        {
            hitBtn = rh.collider.GetComponent<ShooterButton>()
                  ?? rh.collider.GetComponentInParent<ShooterButton>();
            if (hitBtn != null) end = rh.point;
        }

        // Hover state change
        if (hitBtn != _hovered)
        {
            _hovered?.SetHovered(false);
            _hovered = hitBtn;
            _hovered?.SetHovered(true);
        }

        // Beam draw
        _mat.color = hitBtn != null ? colorOnButton : colorNormal;
        _lr.SetPosition(0, origin);
        _lr.SetPosition(1, end);

        // Input — left index trigger, single press
        float trig = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger);
        if (trig > 0.7f && _triggerWasUp)
        {
            _triggerWasUp = false;
            _hovered?.TryActivate();
        }
        else if (trig < 0.1f)
        {
            _triggerWasUp = true;
        }
    }

    void BuildBeam()
    {
        var go = new GameObject("LeftInteractBeam");
        go.transform.SetParent(transform, false);

        _lr = go.AddComponent<LineRenderer>();
        _lr.positionCount     = 2;
        _lr.startWidth        = beamWidth;
        _lr.endWidth          = beamWidth * 0.3f;
        _lr.useWorldSpace     = true;
        _lr.alignment         = LineAlignment.View;
        _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lr.receiveShadows    = false;

        _mat = new Material(Shader.Find("Sprites/Default")) { color = colorNormal };
        _lr.material = _mat;
    }
}
