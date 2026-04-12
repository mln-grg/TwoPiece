using UnityEngine;

/// <summary>
/// Smooth follow camera for the hover-car.  Anti-jitter approach:
///   1. Runs exclusively in LateUpdate (after physics and animation).
///   2. Requires Rigidbody.interpolation = Interpolate on the target
///      (HoverCarController sets this automatically in Awake).
///   3. Uses Vector3.SmoothDamp for position (handles variable frame-rate well).
///   4. Uses Quaternion.Slerp for rotation (stable, no gimbal issues).
///   5. Tracks only the target's yaw — camera doesn't bank/roll with the ship,
///      which prevents motion sickness and avoids most rotation-jitter cases.
/// </summary>
[RequireComponent(typeof(Camera))]
public class HoverCarCamera : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Inspector
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Target")]
    [Tooltip("The ship's root GameObject (the physics Rigidbody)")]
    public Transform target;

    [Header("Position Offset")]
    [Tooltip("Local-space offset from the target.  x = right,  y = up,  z = forward.\n" +
             "Default (0, 4.5, -9) sits behind and above the ship.")]
    public Vector3 positionOffset = new Vector3(0f, 4.5f, -9f);

    [Header("Rotation Offset")]
    [Tooltip("Additional Euler angles applied after the camera looks at the target.\n" +
             "Positive X = tilt down (look at terrain below ship).\n" +
             "Leave at (0,0,0) for a natural look-at, adjust to taste.")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("Position Smoothing")]
    [Tooltip("SmoothDamp time in seconds.  Lower = snappier follow.  ~0.1 is responsive, ~0.25 is floaty.")]
    [Range(0.01f, 0.5f)]
    public float positionSmoothTime = 0.12f;

    [Header("Rotation Smoothing")]
    [Tooltip("Slerp speed.  Higher = camera snaps to the ship's heading faster.")]
    [Range(1f, 25f)]
    public float rotationSpeed = 8f;

    [Header("Look-ahead")]
    [Tooltip("Shifts the look-at point forward based on velocity, so you see more of where you're going.")]
    public float lookAheadDistance = 3.5f;
    [Tooltip("How quickly the look-ahead offset interpolates.")]
    public float lookAheadSpeed = 3f;

    // ═══════════════════════════════════════════════════════════════════════
    //  Runtime state
    // ═══════════════════════════════════════════════════════════════════════

    Vector3 posVelocity      = Vector3.zero;  // SmoothDamp velocity ref
    Vector3 currentLookAhead = Vector3.zero;  // smoothed look-ahead world offset
    Rigidbody targetRb;

    // ═══════════════════════════════════════════════════════════════════════
    //  Unity callbacks
    // ═══════════════════════════════════════════════════════════════════════

    void Start()
    {
        if (target != null)
        {
            targetRb = target.GetComponent<Rigidbody>();

            // Snap to position immediately on start — avoids a visible swoop
            // from the scene-origin to the ship on the first frame.
            transform.position = target.position + Quaternion.Euler(0f, target.eulerAngles.y, 0f)
                                  * positionOffset;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        // ── Look-ahead ────────────────────────────────────────────────────
        // Shift the look-at point forward so the player can see ahead.
        if (targetRb != null)
        {
            float   speed      = Mathf.Min(targetRb.linearVelocity.magnitude, lookAheadDistance);
            Vector3 wantAhead  = target.forward * speed;
            currentLookAhead   = Vector3.Lerp(currentLookAhead, wantAhead,
                                              Time.deltaTime * lookAheadSpeed);
        }

        // ── Desired position ──────────────────────────────────────────────
        // Only use the target's yaw to compute the orbit position.
        // This prevents the camera from rolling/pitching with the ship
        // (the main cause of camera jitter on physics objects).
        Quaternion yawOnly   = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        Vector3    desiredPos = target.position + yawOnly * positionOffset;

        transform.position = Vector3.SmoothDamp(
            transform.position, desiredPos,
            ref posVelocity, positionSmoothTime);

        // ── Desired rotation ──────────────────────────────────────────────
        // Look from the camera's smoothed position toward the target
        // (offset slightly upward so we look at the ship, not the ground).
        Vector3 lookPoint  = target.position + currentLookAhead + Vector3.up * 0.5f;
        Vector3 lookDir    = lookPoint - transform.position;

        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion baseLook   = Quaternion.LookRotation(lookDir, Vector3.up);
            Quaternion desiredRot = baseLook * Quaternion.Euler(rotationOffset);

            transform.rotation = Quaternion.Slerp(
                transform.rotation, desiredRot,
                Time.deltaTime * rotationSpeed);
        }
    }

    // Draw an indicator so you can see the camera offset in the editor
    void OnDrawGizmosSelected()
    {
        if (target == null) return;

        Quaternion yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        Vector3    pos = target.position + yaw * positionOffset;

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(pos, 0.3f);
        Gizmos.DrawLine(target.position, pos);
    }
}
