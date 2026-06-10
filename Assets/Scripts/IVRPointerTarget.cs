/// <summary>
/// Anything the VR pointer can hover over and click (keyboard keys, buttons, list entries).
/// </summary>
public interface IVRPointerTarget
{
    void SetPointerHovered(bool hovered);
    void OnPointerClick();
}
