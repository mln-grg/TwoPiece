using UnityEngine;

// Legacy enum kept for AI compatibility (PatrolState, CombatState, DockingStation).
// Player movement no longer uses gears — see throttleInput / brakeInput below.
public enum GearState { Idle, Gear1, Gear2, Gear3 }

public class ShipController : MonoBehaviour
{
    [Header("Speed")]
    [Tooltip("Constant minimum forward drift when no input is held (the ship never fully stops in idle)")]
    public float idleSpeed = 2f;
    public float gear1Speed = 7f;
    public float gear2Speed = 13f;
    public float gear3Speed = 20f;

    [Header("Throttle Feel (Player)")]
    [Tooltip("How fast speed builds when W is held (u/s²) — keep low for a heavy, truck-like feel")]
    public float throttleAcceleration = 5f;
    [Tooltip("How fast speed bleeds back to idle after W is released (u/s²)")]
    public float passiveDeceleration = 4f;
    [Tooltip("How fast the ship decelerates when S / brake is held (u/s²)")]
    public float brakeDeceleration = 14f;

    [Header("Gear Acceleration (AI)")]
    [Tooltip("Acceleration rate when an AI ship shifts up a gear")]
    public float acceleration = 4.0f;
    [Tooltip("Deceleration rate when an AI ship shifts down a gear")]
    public float gearDownDeceleration = 25f;

    [Header("Steering")]
    [Tooltip("Max turn rate at zero speed (easiest turning)")]
    public float lowSpeedTurnRate = 45f;
    [Tooltip("Max turn rate at full speed (hardest turning)")]
    public float highSpeedTurnRate = 18f;
    [Tooltip("How fast rotation speed builds up or bleeds off (deg/s²)")]
    public float turnAcceleration = 72f;

    [Header("Ship Lean (Heel)")]
    public float leanAmount = 12f;
    public float leanSmoothing = 3f;
    [Tooltip("Additional lean when turning hard at speed")]
    public float turnLeanBonus = 8f;

    [Header("Dash / Boost")]
    public float dashSpeed = 35f;
    public float dashDuration = 0.6f;
    public float dashCooldown = 4f;

    bool isDashing;
    float dashTimer;
    float dashCooldownTimer;

    [Header("State")]
    public GearState currentGear = GearState.Idle; // used by AI states

    [Header("Control Input")]
    [Range(-1f, 1f)] public float steeringInput;
    public int sailDelta;                            // AI gear stepping (+1 / -1 per frame)
    [Range(0f, 1f)]  public float throttleInput;    // player: W held = 1
    [Range(0f, 1f)]  public float brakeInput;       // player: S held = 1

    [Header("Health")]
    public HealthComponent sailHealth;
    public HealthComponent hullHealth;
    [Range(0f, 1f)] public float sailDamageToHullRatio = 1f;
    public float hullDisableThreshold = 30f;

    float currentForwardSpeed;
    float currentRotationSpeed;
    float currentLean;

    bool sailsDestroyed;
    bool hullDisabled;
    bool destroyed;

    public float CurrentSpeed  => currentForwardSpeed;
    public float MaxSpeed      => gear3Speed;
    /// <summary>0–1 fraction of top speed; used by PlayerShipInput for camera triggers.</summary>
    public float SpeedRatio    => Mathf.Clamp01(currentForwardSpeed / gear3Speed);

    /// <summary>
    /// Called by ShipPhysicsBody every frame the hull is in contact with an obstacle.
    /// Zeroes the velocity component going into the wall; the along-wall component
    /// survives — so the ship slides but cannot push through.
    /// </summary>
    public void ApplyWallSlide(Vector3 wallNormal)
    {
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
    // GEAR STEPPING  (AI only — player uses throttle/brake)
    // =====================================================

    void ApplySailChange()
    {
        if (sailDelta == 0 || sailsDestroyed || hullDisabled) return;

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
        if (isDashing || dashCooldownTimer > 0f || sailsDestroyed || hullDisabled || destroyed) return;
        isDashing   = true;
        dashTimer   = dashDuration;
        dashCooldownTimer = dashCooldown;
    }

    void ApplyDash()
    {
        if (!isDashing) return;
        dashTimer -= Time.deltaTime;
        currentForwardSpeed = dashSpeed;
        if (dashTimer <= 0f) isDashing = false;
    }

    // =====================================================
    // MOVEMENT & STEERING
    // =====================================================

    void ApplyMovement()
    {
        if (!isDashing)
        {
            float targetSpeed;
            float accelRate;

            if (sailsDestroyed || hullDisabled)
            {
                // Disabled — bleed to a full stop
                targetSpeed = 0f;
                accelRate   = brakeDeceleration;
            }
            else if (brakeInput > 0f)
            {
                // Player braking: push speed toward zero
                targetSpeed = 0f;
                accelRate   = brakeDeceleration;
            }
            else if (throttleInput > 0f)
            {
                // Player accelerating: heavy build-up toward max speed
                targetSpeed = gear3Speed;
                accelRate   = throttleAcceleration;
            }
            else if (currentGear != GearState.Idle)
            {
                // AI gear control: step toward the requested gear speed
                float gearTarget =
                    currentGear == GearState.Gear3 ? gear3Speed :
                    currentGear == GearState.Gear2 ? gear2Speed :
                    gear1Speed;
                accelRate   = gearTarget > currentForwardSpeed ? acceleration : gearDownDeceleration;
                targetSpeed = gearTarget;
            }
            else
            {
                // Idle: coast back down to the minimum always-on drift speed
                targetSpeed = idleSpeed;
                accelRate   = passiveDeceleration;
            }

            currentForwardSpeed = Mathf.MoveTowards(
                currentForwardSpeed, targetSpeed, accelRate * Time.deltaTime);
        }

        // Forward motion
        transform.position += transform.forward * currentForwardSpeed * Time.deltaTime;

        // Speed-dependent max turn rate: slower = easier to turn, faster = harder
        float speedRatio   = Mathf.Clamp01(currentForwardSpeed / gear3Speed);
        float maxTurnRate  = Mathf.Lerp(lowSpeedTurnRate, highSpeedTurnRate, speedRatio);

        float targetRotationSpeed = steeringInput * maxTurnRate;

        currentRotationSpeed = Mathf.MoveTowards(
            currentRotationSpeed, targetRotationSpeed, turnAcceleration * Time.deltaTime);

        transform.Rotate(0f, currentRotationSpeed * Time.deltaTime, 0f, Space.World);

        // Lean based on how much of the max turn rate is actually being used
        float normalizedTurn   = maxTurnRate > 0f ? currentRotationSpeed / maxTurnRate : 0f;
        float steeringIntensity = Mathf.Abs(normalizedTurn);
        float speedFactor      = Mathf.Clamp01(currentForwardSpeed / gear3Speed);

        float targetLean = -normalizedTurn * (leanAmount + turnLeanBonus * steeringIntensity * speedFactor);
        currentLean = Mathf.Lerp(currentLean, targetLean, leanSmoothing * Time.deltaTime);

        Vector3 currentEuler = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(currentEuler.x, currentEuler.y, currentLean);
    }
}
