using System.Collections;
using UnityEngine;

public enum GearState    { Idle, Gear1, Gear2, Gear3 }
public enum DashDirection { Forward, Left, Right }

public class ShipController : MonoBehaviour
{
    public float gear1Speed = 7f;
    public float gear2Speed = 13f;
    public float gear3Speed = 20f;
    
    public float acceleration = 4.0f;

    [Tooltip("Speed units/s lost per second when dropping a gear or going to idle — 25 drops full speed to zero in ~0.8s")]
    public float gearDownDeceleration = 25f;

    [Header("Steering")]
    [Tooltip("Max turn rate at zero speed (easiest turning)")]
    public float lowSpeedTurnRate = 45f;

    [Tooltip("Max turn rate at full speed (hardest turning)")]
    public float highSpeedTurnRate = 18f;

    [Tooltip("How fast rotation speed builds up or bleeds off (deg/s²) — like the steering wheel tightening")]
    public float turnAcceleration = 72f;

    [Header("Ship Lean (Heel)")]
    public float leanAmount = 12f;
    public float leanSmoothing = 3f;
    
    [Tooltip("Additional lean when turning hard")]
    public float turnLeanBonus = 8f;

    [Header("Dash")]
    [Tooltip("Distance covered by a forward dash (units).")]
    public float forwardDashDistance = 12f;

    [Tooltip("Distance covered by a left/right side dash (units).")]
    public float sideDashDistance = 7f;

    [Tooltip("Duration of a single dash in seconds — lower = snappier burst.")]
    public float dashDuration = 0.25f;

    [Tooltip("Seconds before another dash is allowed after one finishes.")]
    public float dashCooldown = 3f;

    bool     isDashing;
    float    dashElapsed;       // time into the current dash (0 → dashDuration)
    float    dashCooldownTimer;
    Vector3  dashWorldDir;      // world-space direction captured at launch
    float    dashTotalDist;     // total distance for the active dash

    [Header("State")]
    public GearState currentGear = GearState.Idle;

    [Header("Control Input (AI / Player)")]
    [Range(-1f, 1f)] public float steeringInput;
    public int sailDelta;

    [Header("Health")]
    public HealthComponent sailHealth;
    public HealthComponent hullHealth;

    [Range(0f, 1f)]
    public float sailDamageToHullRatio = 1f;

    public float hullDisableThreshold = 30f;
    
    float currentForwardSpeed;
    float currentRotationSpeed;
    float currentLean;

    bool sailsDestroyed;
    bool hullDisabled;
    bool destroyed;

    public float CurrentSpeed  => currentForwardSpeed;
    public float MaxSpeed      => gear3Speed;
    public bool  IsDestroyed   => destroyed;
    public bool  IsDashing     => isDashing;

    /// <summary>
    /// Called by ShipPhysicsBody every frame the hull is in contact with an obstacle.
    /// Zeroes the velocity component going into the wall; the along-wall component
    /// survives — so you slide but cannot push through.
    /// </summary>
    public void ApplyWallSlide(Vector3 wallNormal)
    {
        // ProjectOnPlane magnitude = sin(angle between forward and wallNormal)
        //   0 → heading straight into wall → full stop
        //   1 → heading parallel to wall   → no speed loss
        float slideFactor = Vector3.ProjectOnPlane(transform.forward, wallNormal).magnitude;
        currentForwardSpeed *= slideFactor;
    }

    void Start()
    {
        // Auto-discover health components when Inspector references are not set.
        // Walks children looking for ShipCollision + HealthComponent pairs.
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

    // ── Health handlers ────────────────────────────────────────────────────

    void OnHullDamaged(DamageInfo info)
    {
        // Cross the disable threshold: ship becomes sluggish and can't change gear
        if (!hullDisabled && hullHealth.currentHealth <= hullDisableThreshold)
            hullDisabled = true;
    }

    void OnHullDestroyed()
    {
        // Ship is dead — stop it immediately and destroy after 5 seconds
        destroyed = true;
        StartCoroutine(DestroyShipAfterDelay(5f));
    }

    void OnSailDestroyed()
    {
        // No sails — locked to idle, cannot move under own power
        sailsDestroyed = true;
    }

    IEnumerator DestroyShipAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }

    // ───────────────────────────────────────────────────────────────────────

    void Update()
    {
        dashCooldownTimer -= Time.deltaTime;

        ApplySailChange();
        ApplyDash();
        ApplyMovement();
    }
    
    
    // =====================================================
    // SAIL STATE
    // =====================================================
    
    
    void ApplySailChange()
    {
        if (sailDelta == 0 || sailsDestroyed || hullDisabled || isDashing)
            return;

        if (sailDelta > 0)
        {
            if      (currentGear == GearState.Idle)  currentGear = GearState.Gear1;
            else if (currentGear == GearState.Gear1) currentGear = GearState.Gear2;
            else if (currentGear == GearState.Gear2) currentGear = GearState.Gear3;
        }
        else
        {
            if      (currentGear == GearState.Gear3) currentGear = GearState.Gear2;
            else if (currentGear == GearState.Gear2) currentGear = GearState.Gear1;
            else if (currentGear == GearState.Gear1) currentGear = GearState.Idle;
        }

        sailDelta = 0;
    }

    // =====================================================
    // DASH
    // =====================================================

    /// <summary>
    /// Initiates a dash in the requested direction.
    /// Direction is captured from the ship's current orientation at the moment
    /// of the call, so it never drifts mid-dash.
    /// </summary>
    public void TryDash(DashDirection dir)
    {
        if (isDashing || dashCooldownTimer > 0f || sailsDestroyed || hullDisabled || destroyed)
            return;

        dashWorldDir  = dir == DashDirection.Forward ? transform.forward
                      : dir == DashDirection.Left    ? -transform.right
                      :                                 transform.right;

        dashTotalDist     = dir == DashDirection.Forward ? forwardDashDistance : sideDashDistance;
        isDashing         = true;
        dashElapsed       = 0f;
        dashCooldownTimer = dashCooldown;

        // Snap out of any ongoing turn so the ship flies straight through the dash
        currentRotationSpeed = 0f;
    }

    void ApplyDash()
    {
        if (!isDashing) return;

        float tPrev    = dashElapsed / dashDuration;
        dashElapsed   += Time.deltaTime;

        if (dashElapsed >= dashDuration)
        {
            dashElapsed = dashDuration;
            isDashing   = false;
        }

        float tNext = dashElapsed / dashDuration;

        // Ease-out quadratic: position(t) = dist × (1 − (1−t)²)
        // Frame delta       = dist × ((1−tPrev)² − (1−tNext)²)
        float a     = 1f - tPrev;
        float b     = 1f - tNext;
        float delta = dashTotalDist * (a * a - b * b);

        transform.position += dashWorldDir * delta;
    }

    // =====================================================
    // MOVEMENT & STEERING
    // =====================================================


    void ApplyMovement()
    {
        float targetSpeed =
            sailsDestroyed || hullDisabled ? 0f :
            currentGear == GearState.Gear3 ? gear3Speed :
            currentGear == GearState.Gear2 ? gear2Speed :
            currentGear == GearState.Gear1 ? gear1Speed :
            0f;

        if (!isDashing)
        {
            float accel = targetSpeed > currentForwardSpeed ? acceleration : gearDownDeceleration;
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetSpeed, accel * Time.deltaTime);
        }

        // Forward motion — always applied so momentum carries through a dash
        transform.position += transform.forward * currentForwardSpeed * Time.deltaTime;

        if (isDashing)
        {
            // Rotation and lean are frozen for the duration of the dash.
            // Lean bleeds back to level so the ship looks stable mid-burst.
            currentLean = Mathf.Lerp(currentLean, 0f, leanSmoothing * Time.deltaTime);
            Vector3 euler = transform.localEulerAngles;
            transform.localRotation = Quaternion.Euler(euler.x, euler.y, currentLean);
            return;
        }

        // Speed-dependent max turn rate: slower = easier to turn, faster = harder
        float speedRatio = Mathf.Clamp01(currentForwardSpeed / gear3Speed);
        float maxTurnRate = Mathf.Lerp(lowSpeedTurnRate, highSpeedTurnRate, speedRatio);

        // Target rotation speed from input — zero input bleeds rotation back to zero
        float targetRotationSpeed = steeringInput * maxTurnRate;

        // Accelerate toward target; reversing direction forces current speed through zero first,
        // naturally producing the "startup lag" and "longer in acceleration on reversal" feel
        currentRotationSpeed = Mathf.MoveTowards(
            currentRotationSpeed,
            targetRotationSpeed,
            turnAcceleration * Time.deltaTime
        );

        transform.Rotate(0f, currentRotationSpeed * Time.deltaTime, 0f, Space.World);

        // Lean based on how much of max turn rate we're actually using
        float normalizedTurn    = maxTurnRate > 0f ? currentRotationSpeed / maxTurnRate : 0f;
        float steeringIntensity = Mathf.Abs(normalizedTurn);
        float speedFactor       = Mathf.Clamp01(currentForwardSpeed / gear3Speed);

        float targetLean = -normalizedTurn * (leanAmount + turnLeanBonus * steeringIntensity * speedFactor);
        currentLean = Mathf.Lerp(currentLean, targetLean, leanSmoothing * Time.deltaTime);

        Vector3 currentEuler = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(currentEuler.x, currentEuler.y, currentLean);
    }
}
