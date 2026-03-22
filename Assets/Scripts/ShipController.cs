using UnityEngine;

public enum SailState { NoSail, HalfSail, FullSail }

public class ShipController : MonoBehaviour
{
    public float halfSailSpeed = 10f;
    public float fullSailSpeed = 20f;
    
    public float acceleration = 4.0f;
    
    public float deceleration = 2.5f;

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
    public SailState currentSail = SailState.NoSail;

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
    public float MaxSpeed => fullSailSpeed;

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
            if (currentSail == SailState.NoSail) currentSail = SailState.HalfSail;
            else if (currentSail == SailState.HalfSail) currentSail = SailState.FullSail;
        }
        else
        {
            if (currentSail == SailState.FullSail) currentSail = SailState.HalfSail;
            else if (currentSail == SailState.HalfSail) currentSail = SailState.NoSail;
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
            currentSail == SailState.FullSail ? fullSailSpeed :
            currentSail == SailState.HalfSail ? halfSailSpeed :
            0f;

        if (!isDashing)
        {
            // Smooth acceleration/deceleration with curves
            float accel = targetSpeed > currentForwardSpeed ? acceleration : deceleration;
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetSpeed, accel * Time.deltaTime);
        }

        // Forward motion
        transform.position += transform.forward * currentForwardSpeed * Time.deltaTime;

        // Speed-dependent max turn rate: slower = easier to turn, faster = harder
        float speedRatio = Mathf.Clamp01(currentForwardSpeed / fullSailSpeed);
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
        float speedFactor = Mathf.Clamp01(currentForwardSpeed / fullSailSpeed);

        // More lean at higher speeds when turning
        float targetLean = -normalizedTurn * (leanAmount + turnLeanBonus * steeringIntensity * speedFactor);

        currentLean = Mathf.Lerp(currentLean, targetLean, leanSmoothing * Time.deltaTime);
        
        // Apply lean as local rotation
        Vector3 currentEuler = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(currentEuler.x, currentEuler.y, currentLean);
    }
}
