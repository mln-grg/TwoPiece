using System.Collections;
using UnityEngine;

// ── Kept for AI script compilation compatibility ───────────────────────────────
public enum GearState    { Idle, Gear1, Gear2, Gear3 }
public enum DashDirection { Forward, Left, Right }

/// <summary>
/// AC7-style sky ship flight controller.
///
/// Speed   — driven by thrusterInput [0-1] (RT / Left Shift).
///           At zero thrust the ship coasts at minThrusterSpeed; at full thrust
///           it accelerates to maxThrusterSpeed.
///
/// Pitch   — driven by pitchInput [-1..+1] (Left Stick Y, inverted flight
///           convention: +1 = nose up).  Ship actually climbs / dives in 3-D.
///
/// Yaw     — derived from bank/roll (AC7 Standard): steeringInput [-1..+1]
///           (Left Stick X / A-D) banks the ship; yaw rate scales with bank angle
///           and is wider at higher speeds (larger turn radius).
/// </summary>
public class ShipController : MonoBehaviour
{
    // ── Thruster ──────────────────────────────────────────────────────────────
    [Header("Thruster")]
    [Tooltip("Forward speed when thruster is at zero — ship always has minimum momentum")]
    public float minThrusterSpeed = 5f;

    [Tooltip("Forward speed at full thruster (RT fully pressed)")]
    public float maxThrusterSpeed = 30f;

    [Tooltip("Units/s² gained per second when applying thrust")]
    public float thrusterAcceleration = 8f;

    [Tooltip("Units/s² lost per second when thrust is cut (RT released)")]
    public float thrusterDeceleration = 4f;

    // ── Pitch ─────────────────────────────────────────────────────────────────
    [Header("Pitch (Left Stick Y / W-S)")]
    [Tooltip("Maximum nose-up / nose-down angle in degrees")]
    public float maxPitchAngle = 35f;

    [Tooltip("How quickly pitch builds / bleeds — lower = heavier, more inertia")]
    public float pitchResponseSpeed = 3f;

    [Tooltip("Speed units/s² lost per second at full pitch (climbing costs thrust)")]
    public float pitchSpeedBleed = 1.5f;

    // ── Steering — Bank / Roll drives Yaw (AC7 Standard) ─────────────────────
    [Header("Steering (Left Stick X / A-D)")]
    [Tooltip("Max turn rate at minimum speed (tightest circle)")]
    public float lowSpeedTurnRate = 50f;

    [Tooltip("Max turn rate at maximum speed (widest arc)")]
    public float highSpeedTurnRate = 18f;

    [Header("Bank / Roll")]
    [Tooltip("Maximum bank angle in degrees — this IS the turn input")]
    public float maxBankAngle = 35f;

    [Tooltip("How fast the bank builds and bleeds — lower = heavier feel")]
    public float bankResponseSpeed = 3.5f;

    [Tooltip("Speed units/s² lost per second at full bank")]
    public float bankSpeedBleed = 2.5f;

    // ── Dash ──────────────────────────────────────────────────────────────────
    [Header("Dash")]
    [Tooltip("Distance covered by a forward dash (units)")]
    public float forwardDashDistance = 12f;

    [Tooltip("Distance covered by a left/right side dash (units)")]
    public float sideDashDistance = 7f;

    [Tooltip("Duration of a single dash in seconds")]
    public float dashDuration = 0.25f;

    [Tooltip("Seconds before another dash is allowed")]
    public float dashCooldown = 3f;

    // ── Control Input (set each frame by PlayerShipInput or AI) ───────────────
    [Header("Control Input")]
    [Range(-1f, 1f)] public float steeringInput;   // roll / bank → yaw
    [Range(-1f, 1f)] public float pitchInput;       // +1 = nose up, -1 = nose down
    [Range( 0f, 1f)] public float thrusterInput;    // 0 = idle coast, 1 = full thrust (RT)

    // ── Legacy fields — kept so AI scripts continue to compile ────────────────
    [HideInInspector] public GearState currentGear = GearState.Gear1;
    [HideInInspector] public int sailDelta;

    // ── Health ────────────────────────────────────────────────────────────────
    [Header("Health")]
    public HealthComponent sailHealth;
    public HealthComponent hullHealth;

    [Range(0f, 1f)]
    public float sailDamageToHullRatio = 1f;

    public float hullDisableThreshold = 30f;

    // ── Private state ─────────────────────────────────────────────────────────
    float currentForwardSpeed;
    float currentRotationSpeed;  // degrees/sec — derived each frame from bank angle
    float currentLean;           // current bank angle in degrees (Z-axis)
    float currentPitch;          // current pitch angle in degrees (X-axis, neg = nose up)

    bool  isDashing;
    float dashElapsed;
    float dashCooldownTimer;
    Vector3 dashWorldDir;
    float   dashTotalDist;

    bool sailsDestroyed;
    bool hullDisabled;
    bool destroyed;

    // ── Public read-only properties ───────────────────────────────────────────
    public float CurrentSpeed      => currentForwardSpeed;
    public float MaxSpeed          => maxThrusterSpeed;
    public bool  IsDestroyed       => destroyed;
    public bool  IsDashing         => isDashing;

    /// <summary>Normalised thruster 0-1. Use for HUD and camera speed effects.</summary>
    public float ThrusterPercent   => thrusterInput;

    // ── Wall collision ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ShipPhysicsBody when the hull is in contact with a wall.
    /// Bleeds the component of velocity going into the wall so the ship slides.
    /// </summary>
    public void ApplyWallSlide(Vector3 wallNormal)
    {
        float slideFactor = Vector3.ProjectOnPlane(transform.forward, wallNormal).magnitude;
        currentForwardSpeed *= slideFactor;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Start()
    {
        // Auto-discover health components when Inspector references are not set.
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

        // Always start with minimum flight speed — sky ships don't sit still
        currentForwardSpeed = minThrusterSpeed;
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

    // ── Health handlers ────────────────────────────────────────────────────────

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

    void OnSailDestroyed()
    {
        sailsDestroyed = true;
    }

    IEnumerator DestroyShipAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        dashCooldownTimer -= Time.deltaTime;
        ApplyDash();
        ApplyMovement();
    }

    // =========================================================================
    // DASH
    // =========================================================================

    public void TryDash(DashDirection dir)
    {
        if (isDashing || dashCooldownTimer > 0f || sailsDestroyed || hullDisabled || destroyed)
            return;

        dashWorldDir = dir == DashDirection.Forward ? transform.forward
                     : dir == DashDirection.Left    ? -transform.right
                     :                                 transform.right;

        dashTotalDist     = dir == DashDirection.Forward ? forwardDashDistance : sideDashDistance;
        isDashing         = true;
        dashElapsed       = 0f;
        dashCooldownTimer = dashCooldown;

        // Snap rotation speed so the ship flies straight through the dash
        currentRotationSpeed = 0f;
    }

    void ApplyDash()
    {
        if (!isDashing) return;

        float tPrev  = dashElapsed / dashDuration;
        dashElapsed += Time.deltaTime;

        if (dashElapsed >= dashDuration)
        {
            dashElapsed = dashDuration;
            isDashing   = false;
        }

        float tNext = dashElapsed / dashDuration;

        // Ease-out quadratic: position(t) = dist × (1 − (1−t)²)
        float a     = 1f - tPrev;
        float b     = 1f - tNext;
        float delta = dashTotalDist * (a * a - b * b);

        transform.position += dashWorldDir * delta;
    }

    // =========================================================================
    // MOVEMENT — Thruster · Pitch · Bank-driven Yaw
    // =========================================================================

    void ApplyMovement()
    {
        if (destroyed) return;

        // ── Thruster speed ────────────────────────────────────────────────────
        // Dead engines: bleed to zero.  Otherwise: RT maps 0→min, 1→max speed.
        float targetSpeed = (sailsDestroyed || hullDisabled)
            ? 0f
            : Mathf.Lerp(minThrusterSpeed, maxThrusterSpeed, thrusterInput);

        if (!isDashing)
        {
            float accel = targetSpeed > currentForwardSpeed ? thrusterAcceleration : thrusterDeceleration;
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetSpeed, accel * Time.deltaTime);
        }

        // Move forward — transform.forward now includes pitch, so the ship
        // genuinely climbs/dives in world space.
        transform.position += transform.forward * currentForwardSpeed * Time.deltaTime;

        // ── Dash stabilisation ────────────────────────────────────────────────
        if (isDashing)
        {
            // Level bank and pitch visually during a dash burst
            currentLean  = Mathf.Lerp(currentLean,  0f, bankResponseSpeed  * Time.deltaTime);
            currentPitch = Mathf.Lerp(currentPitch, 0f, pitchResponseSpeed * Time.deltaTime);
            ApplyRotation();
            return;
        }

        // ── Pitch (nose up / down) ────────────────────────────────────────────
        // Unity X euler: negative = nose up.  pitchInput +1 = nose up → targetPitch negative.
        float targetPitch = -pitchInput * maxPitchAngle;
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, pitchResponseSpeed * Time.deltaTime);

        // ── Bank / Roll → Yaw (AC7 Standard) ─────────────────────────────────
        float targetLean = -steeringInput * maxBankAngle;
        currentLean = Mathf.Lerp(currentLean, targetLean, bankResponseSpeed * Time.deltaTime);

        // Wider turn arc at higher speeds (mirrors AC7's speed-radius relationship)
        float speedRatio     = Mathf.Clamp01(currentForwardSpeed / maxThrusterSpeed);
        float maxTurnRate    = Mathf.Lerp(lowSpeedTurnRate, highSpeedTurnRate, speedRatio);
        float bankRatio      = currentLean / -maxBankAngle;  // -1..+1
        currentRotationSpeed = bankRatio * maxTurnRate;

        // Yaw is applied in world space so pitch/bank don't pollute heading
        transform.Rotate(0f, currentRotationSpeed * Time.deltaTime, 0f, Space.World);

        // ── Speed bleed from hard manoeuvres ──────────────────────────────────
        // Only bleed above minimum; MoveTowards will recover next frame if thrust is on.
        if (currentForwardSpeed > minThrusterSpeed)
        {
            float bleed = (Mathf.Abs(bankRatio)   * bankSpeedBleed
                         + Mathf.Abs(pitchInput)  * pitchSpeedBleed)
                         * Time.deltaTime;

            currentForwardSpeed = Mathf.Max(currentForwardSpeed - bleed, minThrusterSpeed);
        }

        ApplyRotation();
    }

    /// <summary>
    /// Writes the final world rotation by preserving the current world yaw (already
    /// updated this frame by Rotate) and overlaying our controlled pitch and bank.
    /// </summary>
    void ApplyRotation()
    {
        Vector3 euler = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(currentPitch, euler.y, currentLean);
    }
}
