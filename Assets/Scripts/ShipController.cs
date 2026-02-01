using UnityEngine;

public enum SailState { NoSail, HalfSail, FullSail }

public class ShipController : MonoBehaviour
{
    [Header("Speeds")]
    public float halfSailSpeed = 8f;
    public float fullSailSpeed = 18f;

    [Tooltip("How fast the ship accelerates when increasing sail")]
    public float acceleration = 1.2f;

    [Tooltip("How fast the ship slows down when reducing sail")]
    public float deceleration = 3.0f;

    [Header("Steering & Inertia")]
    public float turnPower = 25f;
    public float turnInertia = 0.92f;
    public float maxTurnSpeed = 20f;

    [Header("Ship Lean (Heel)")]
    public float leanAmount = 15f;
    public float leanSmoothing = 2f;

    [Header("Dash")]
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
    float currentAngularVelocity;
    float currentLean;

    bool sailsDestroyed;
    bool hullDisabled;
    bool destroyed;

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
            float accel =
                targetSpeed > currentForwardSpeed
                    ? acceleration     // speeding up
                    : deceleration;    // slowing down

            currentForwardSpeed =
                Mathf.MoveTowards(currentForwardSpeed, targetSpeed, accel * Time.deltaTime);
        }

        // Forward motion
        transform.position += transform.forward * currentForwardSpeed * Time.deltaTime;

        // -------------------------------------------------
        // SPEED-DEPENDENT STEERING (THIS IS THE KEY CHANGE)
        // -------------------------------------------------

        float speed01 = Mathf.Clamp01(currentForwardSpeed / fullSailSpeed);

        // Non-linear drop in steering authority at speed
        float steeringAuthority = Mathf.Lerp(1.0f, 0.25f, speed01 * speed01);

        currentAngularVelocity +=
            steeringInput * turnPower * steeringAuthority * Time.deltaTime;

        currentAngularVelocity *= turnInertia;
        currentAngularVelocity =
            Mathf.Clamp(currentAngularVelocity, -maxTurnSpeed, maxTurnSpeed);

        transform.Rotate(0f, currentAngularVelocity * Time.deltaTime, 0f);

        // -------------------------------------------------
        // LEAN (HEEL)
        // -------------------------------------------------

        float targetLean =
            -(currentAngularVelocity / maxTurnSpeed) * leanAmount;

        currentLean =
            Mathf.Lerp(currentLean, targetLean, leanSmoothing * Time.deltaTime);

        transform.rotation *= Quaternion.Euler(0f, 0f, currentLean);
    }
}
