using UnityEngine;

public enum SailState { NoSail, HalfSail, FullSail }

public class ShipController : MonoBehaviour
{
    public float halfSailSpeed = 10f;
    public float fullSailSpeed = 20f;
    
    public float acceleration = 4.0f;
    
    public float deceleration = 2.5f;

    [Header("Steering")]
    [Tooltip("Turn speed at low velocity")]
    public float lowSpeedTurnRate = 45f;
    
    [Tooltip("Turn speed at high velocity")]
    public float highSpeedTurnRate = 28f;
    
    public float steeringResponsiveness = 8f;

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
    float currentTurnSpeed;
    float smoothedSteeringInput;
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
        
        // Smooth steering input for less twitchy feel
        smoothedSteeringInput = Mathf.Lerp(
            smoothedSteeringInput, 
            steeringInput, 
            steeringResponsiveness * Time.deltaTime
        );

        // Speed-based turn rate (ships turn BETTER at moderate speed)
        float speedRatio = currentForwardSpeed / fullSailSpeed;
        
        // Create a curve where mid-speed has best turning
        // 0 speed = poor turning, mid speed = best, high speed = moderate
        float turnCurve = Mathf.Sin(speedRatio * Mathf.PI) * 1.2f;
        turnCurve = Mathf.Max(0.3f, turnCurve); // Minimum turn ability even when stopped
        
        float effectiveTurnRate = Mathf.Lerp(lowSpeedTurnRate, highSpeedTurnRate, speedRatio) * turnCurve;

        // Apply turning
        currentTurnSpeed = smoothedSteeringInput * effectiveTurnRate;
        transform.Rotate(0f, currentTurnSpeed * Time.deltaTime, 0f, Space.World);
        

        float steeringIntensity = Mathf.Abs(smoothedSteeringInput);
        float speedFactor = Mathf.Clamp01(currentForwardSpeed / fullSailSpeed);
        
        // More lean at higher speeds when turning
        float targetLean = -smoothedSteeringInput * (leanAmount + turnLeanBonus * steeringIntensity * speedFactor);

        currentLean = Mathf.Lerp(currentLean, targetLean, leanSmoothing * Time.deltaTime);
        
        // Apply lean as local rotation
        Vector3 currentEuler = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(currentEuler.x, currentEuler.y, currentLean);
    }
}
