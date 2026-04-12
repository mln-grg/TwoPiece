using System.Collections;
using UnityEngine;

// ── Kept for AI script compilation compatibility ───────────────────────────────
public enum GearState    { Idle, Gear1, Gear2, Gear3 }
public enum DashDirection { Forward, Left, Right }

/// <summary>
/// Rigidbody hover-vehicle controller.
///
/// Five downward raycasts (centre + 4 corners) run a spring-damper against the
/// ground, keeping the ship floating at <see cref="hoverHeight"/> regardless of
/// terrain shape.  All movement is applied as forces / torques so Unity's physics
/// provides momentum and inertia for free.
///
/// Banking is derived from the physics yaw-rate each fixed step: the faster the
/// ship is turning, the more it rolls into the curve.  Pitch and roll from physics
/// are zeroed every step so only the calculated bank and yaw remain.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ShipController : MonoBehaviour
{
    // ── Hover ─────────────────────────────────────────────────────────────────
    [Header("Hover")]
    [Tooltip("Desired height above the ground surface")]
    public float hoverHeight = 2f;

    [Tooltip("Spring strength — the 'thrust-to-gravity' knob. " +
             "Higher = tighter hover. Lower = floatier / more oscillation.")]
    public float hoverForce = 50f;

    [Tooltip("Spring damping — higher = quicker settle, less bounce")]
    public float hoverDamping = 8f;

    [Tooltip("Half-extent for the four corner ray origins (local XZ)")]
    public float hoverRayExtent = 1.2f;

    [Tooltip("Max downward ray length; must be >= hoverHeight")]
    public float groundCheckDistance = 6f;

    public LayerMask groundMask = ~0;

    // ── Thrust ────────────────────────────────────────────────────────────────
    [Header("Thrust")]
    [Tooltip("Forward propulsion force")]
    public float forwardForce = 25f;

    [Tooltip("Reverse / braking force (S key / LT)")]
    public float reverseForce = 15f;

    [Tooltip("Max horizontal speed (units/s)")]
    public float maxSpeed = 20f;

    // ── Steering ──────────────────────────────────────────────────────────────
    [Header("Steering")]
    [Tooltip("Yaw torque strength — how hard the ship tries to turn")]
    public float turnTorque = 8f;

    [Tooltip("Rigidbody angular drag — how quickly yaw momentum bleeds off")]
    public float angularDragAmount = 3f;

    // ── Banking ───────────────────────────────────────────────────────────────
    [Header("Banking")]
    [Tooltip("Degrees of roll per rad/s of yaw rate")]
    public float bankFactor = 20f;

    [Tooltip("Maximum roll angle in either direction")]
    public float maxBankAngle = 30f;

    [Tooltip("How quickly the bank angle lerps toward its target (higher = snappier)")]
    public float bankSmoothing = 6f;

    // ── Air resistance ────────────────────────────────────────────────────────
    [Header("Air Resistance")]
    [Tooltip("Rigidbody linear drag — 0 = slides on air, higher = more atmosphere")]
    public float linearDrag = 0.5f;

    // ── Dash ──────────────────────────────────────────────────────────────────
    [Header("Dash")]
    public float forwardDashDistance = 12f;
    public float sideDashDistance    = 7f;
    public float dashDuration        = 0.25f;
    public float dashCooldown        = 3f;

    // ── Control Input (set each frame by PlayerShipInput or AI) ───────────────
    [Header("Control Input")]
    [Range( 0f, 1f)] public float thrusterInput;  // W / Shift / RT  → forward
    [Range(-1f, 1f)] public float steeringInput;  // A-D / Left Stick → yaw
    [Range( 0f, 1f)] public float brakeInput;     // S / E / X btn   → reverse

    // ── Legacy fields — kept so other scripts continue to compile ─────────────
    [HideInInspector] public GearState currentGear  = GearState.Gear1;
    [HideInInspector] public int       sailDelta;
    [HideInInspector] public float     pitchInput;
    [HideInInspector] public float     strafeInput;

    // ── Health ────────────────────────────────────────────────────────────────
    [Header("Health")]
    public HealthComponent sailHealth;
    public HealthComponent hullHealth;

    [Range(0f, 1f)]
    public float sailDamageToHullRatio = 1f;
    public float hullDisableThreshold  = 30f;

    // ── Private state ─────────────────────────────────────────────────────────
    Rigidbody rb;
    float     currentBankAngle;

    bool    isDashing;
    float   dashElapsed;
    float   dashCooldownTimer;
    Vector3 dashWorldDir;
    float   dashTotalDist;

    bool sailsDestroyed;
    bool hullDisabled;
    bool destroyed;

    // ── Public read-only ──────────────────────────────────────────────────────
    public float CurrentSpeed => rb
        ? new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude
        : 0f;
    public float MaxSpeed        => maxSpeed;
    public bool  IsDestroyed     => destroyed;
    public bool  IsDashing       => isDashing;
    public float ThrusterPercent => thrusterInput;

    // ── Wall collision ────────────────────────────────────────────────────────

    /// <summary>No-op — the Rigidbody handles collision response now.</summary>
    public void ApplyWallSlide(Vector3 wallNormal) { }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        rb               = GetComponent<Rigidbody>();
        rb.isKinematic   = false;   // must be dynamic — forces & torques power all movement
        rb.useGravity    = true;    // gravity is what the hover spring works against
        rb.linearDamping = linearDrag;
        rb.angularDamping = angularDragAmount;

        // Physics owns yaw (Y). We manually write pitch and roll every FixedUpdate,
        // so freeze them to stop the physics engine from accumulating its own values.
        rb.constraints = RigidbodyConstraints.FreezeRotationX
                       | RigidbodyConstraints.FreezeRotationZ;
    }

    void Start()
    {
        if (hullHealth == null || sailHealth == null)
        {
            foreach (ShipCollision sc in GetComponentsInChildren<ShipCollision>())
            {
                HealthComponent hc = sc.GetComponent<HealthComponent>();
                if (hc == null) continue;
                if (sc.collisionType == ShipCollisionType.Hull && hullHealth == null) hullHealth = hc;
                if (sc.collisionType == ShipCollisionType.Sail && sailHealth == null) sailHealth = hc;
            }
        }

        if (hullHealth != null)
        {
            hullHealth.OnDamaged   += OnHullDamaged;
            hullHealth.OnDestroyed += OnHullDestroyed;
        }
        if (sailHealth != null)
            sailHealth.OnDestroyed += OnSailDestroyed;
    }

    void OnDestroy()
    {
        if (hullHealth != null)
        {
            hullHealth.OnDamaged   -= OnHullDamaged;
            hullHealth.OnDestroyed -= OnHullDestroyed;
        }
        if (sailHealth != null)
            sailHealth.OnDestroyed -= OnSailDestroyed;
    }

    // ── Health handlers ───────────────────────────────────────────────────────

    void OnHullDamaged(DamageInfo info)
    {
        if (!hullDisabled && hullHealth.currentHealth <= hullDisableThreshold)
            hullDisabled = true;
    }

    void OnHullDestroyed()
    {
        destroyed = true;
        StartCoroutine(DestroyShipAfterDelay(5f));
    }

    void OnSailDestroyed() => sailsDestroyed = true;

    IEnumerator DestroyShipAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }

    // ── Update / FixedUpdate ──────────────────────────────────────────────────

    void Update()
    {
        dashCooldownTimer -= Time.deltaTime;
    }

    void FixedUpdate()
    {
        if (destroyed) return;

        ApplyDash();
        ApplyHover();
        ApplyThrust();
        ApplySteering();
        ApplyBanking();
    }

    // =========================================================================
    //  HOVER — spring-damper per ray
    // =========================================================================

    void ApplyHover()
    {
        foreach (Vector3 origin in GetHoverRayOrigins())
        {
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundCheckDistance, groundMask))
                continue;

            // Spring: pushes up when below target, resists when above
            float heightError = hoverHeight - hit.distance;
            float vertVel     = rb.linearVelocity.y;
            float force       = heightError * hoverForce - vertVel * hoverDamping;

            rb.AddForceAtPosition(Vector3.up * force, origin);
        }
    }

    Vector3[] GetHoverRayOrigins()
    {
        float e = hoverRayExtent;
        return new[]
        {
            transform.position,                        // centre
            transform.TransformPoint( e, 0f,  e),      // front-right
            transform.TransformPoint(-e, 0f,  e),      // front-left
            transform.TransformPoint( e, 0f, -e),      // rear-right
            transform.TransformPoint(-e, 0f, -e),      // rear-left
        };
    }

    // =========================================================================
    //  THRUST — forward / reverse force along current heading
    // =========================================================================

    void ApplyThrust()
    {
        if (isDashing || sailsDestroyed || hullDisabled) return;

        rb.AddRelativeForce(Vector3.forward *  thrusterInput * forwardForce, ForceMode.Force);
        rb.AddRelativeForce(Vector3.forward * -brakeInput    * reverseForce, ForceMode.Force);

        // Clamp horizontal speed without affecting vertical (hover spring owns Y)
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (flatVel.magnitude > maxSpeed)
        {
            flatVel     = flatVel.normalized * maxSpeed;
            rb.linearVelocity = new Vector3(flatVel.x, rb.linearVelocity.y, flatVel.z);
        }
    }

    // =========================================================================
    //  STEERING — yaw torque
    // =========================================================================

    void ApplySteering()
    {
        if (isDashing) return;
        rb.AddRelativeTorque(Vector3.up * steeringInput * turnTorque, ForceMode.Force);
    }

    // =========================================================================
    //  BANKING — roll derived from physics yaw-rate
    // =========================================================================

    void ApplyBanking()
    {
        // Physics yaw-rate (rad/s) → target roll angle (degrees)
        float targetBank = Mathf.Clamp(
            -rb.angularVelocity.y * bankFactor,
            -maxBankAngle, maxBankAngle);

        currentBankAngle = Mathf.Lerp(currentBankAngle, targetBank,
                                      bankSmoothing * Time.fixedDeltaTime);

        // Preserve yaw from physics, zero pitch, write calculated bank.
        // We use transform.rotation directly (reliable for dynamic Rigidbodies
        // when X/Z rotation is frozen via constraints).
        float yaw = transform.eulerAngles.y;
        transform.rotation = Quaternion.Euler(0f, yaw, currentBankAngle);
    }

    // =========================================================================
    //  DASH — ease-out positional burst
    // =========================================================================

    public void TryDash(DashDirection dir)
    {
        if (isDashing || dashCooldownTimer > 0f || sailsDestroyed || hullDisabled || destroyed)
            return;

        dashWorldDir = dir == DashDirection.Forward ? transform.forward
                     : dir == DashDirection.Left    ? -transform.right
                     :                                 transform.right;

        dashWorldDir.y    = 0f;
        dashWorldDir      = dashWorldDir.normalized;
        dashTotalDist     = dir == DashDirection.Forward ? forwardDashDistance : sideDashDistance;
        isDashing         = true;
        dashElapsed       = 0f;
        dashCooldownTimer = dashCooldown;
    }

    void ApplyDash()
    {
        if (!isDashing) return;

        float tPrev  = dashElapsed / dashDuration;
        dashElapsed += Time.fixedDeltaTime;
        if (dashElapsed >= dashDuration) { dashElapsed = dashDuration; isDashing = false; }

        float tNext  = dashElapsed / dashDuration;
        float a      = 1f - tPrev;
        float b      = 1f - tNext;
        float delta  = dashTotalDist * (a * a - b * b); // ease-out quadratic

        rb.MovePosition(rb.position + dashWorldDir * delta);
    }
}
