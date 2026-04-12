using UnityEngine;

/// <summary>
/// Simple follow camera — smoothly tracks a target with a configurable offset.
///
/// All aim-mode methods and properties are kept as stubs so PlayerShipInput
/// and ShipPhysicsBody continue to compile without changes.
/// Replace the stubs when you build out a proper camera system later.
/// </summary>
public class ShipCamera : MonoBehaviour
{
    // ── Aim-mode enum — required by PlayerShipInput ───────────────────────────
    public enum AimSide { None, Left, Right, Front, Back }

    // ── Follow settings ───────────────────────────────────────────────────────
    [Header("Follow")]
    [Tooltip("The transform this camera tracks")]
    public Transform target;

    [Tooltip("Offset from the target.\n" +
             "World-space when rotateWithTarget is false; local to target yaw when true.")]
    public Vector3 offset = new Vector3(0f, 12f, -10f);

    [Tooltip("When true the offset rotates with the target's yaw — camera stays behind the ship.\n" +
             "When false the offset is world-space — good for a fixed top-down or isometric view.")]
    public bool rotateWithTarget = true;

    [Tooltip("How fast the camera position catches up to the target")]
    public float positionSmoothing = 6f;

    [Tooltip("How fast the camera rotates to look at the target")]
    public float rotationSmoothing = 6f;

    // ── Stubs — PlayerShipInput and ShipPhysicsBody require these ────────────
    public AimSide CurrentAimSide  => AimSide.None;
    public float   CameraYaw       => transform.eulerAngles.y;
    public bool    IsAiming        => false;

    public void EnterAimMode()      { }
    public void ExitAimMode()       { }
    public void EnterFreeAimMode()  { }
    public void ExitFreeAimMode()   { }
    public void Shake(float intensity) { }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (!target) return;

        // Position
        Vector3 desiredPosition = rotateWithTarget
            ? target.position + Quaternion.Euler(0f, target.eulerAngles.y, 0f) * offset
            : target.position + offset;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            positionSmoothing * Time.deltaTime);

        // Rotation — always look at the target
        Vector3 toTarget = target.position - transform.position;
        if (toTarget.sqrMagnitude > 0.001f)
        {
            Quaternion desired = Quaternion.LookRotation(toTarget, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desired,
                rotationSmoothing * Time.deltaTime);
        }
    }
}
