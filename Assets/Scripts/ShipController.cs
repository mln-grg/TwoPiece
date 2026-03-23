using UnityEngine;

public enum GearState { Idle, Gear1, Gear2, Gear3 }

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

    [Header("Dash/Boost")]
    public float dashSpeed = 35f;
    public float dashDuration = 0.6f;
    public float dashCooldown = 4f;

    bool isDashing;
    float dashTimer;
    float dashCooldownTimer;

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

    public float CurrentSpeed => currentForwardSpeed;
    public float MaxSpeed => gear3Speed;

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
        if (sailDelta == 0 || sailsDestroyed || hullDisabled)
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

    public void TryDash()
    {
        if (isDashing || dashCooldownTimer > 0f || sailsDestroyed || hullDisabled || destroyed)
            return;

        isDashing = true;
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
    }

    void ApplyDash()
    {
        if (!isDashing)
            return;

        dashTimer -= Time.deltaTime;
        currentForwardSpeed = dashSpeed;

        if (dashTimer <= 0f)
            isDashing = false;
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

        // Forward motion
        transform.position += transform.forward * currentForwardSpeed * Time.deltaTime;

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
        float normalizedTurn = maxTurnRate > 0f ? currentRotationSpeed / maxTurnRate : 0f;
        float steeringIntensity = Mathf.Abs(normalizedTurn);
        float speedFactor = Mathf.Clamp01(currentForwardSpeed / gear3Speed);

        // More lean at higher speeds when turning
        float targetLean = -normalizedTurn * (leanAmount + turnLeanBonus * steeringIntensity * speedFactor);

        currentLean = Mathf.Lerp(currentLean, targetLean, leanSmoothing * Time.deltaTime);
        
        // Apply lean as local rotation
        Vector3 currentEuler = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(currentEuler.x, currentEuler.y, currentLean);
    }
}
