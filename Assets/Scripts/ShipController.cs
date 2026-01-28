using UnityEngine;

public enum SailState { NoSail, HalfSail, FullSail }

public class ShipController : MonoBehaviour
{
    [Header("Speeds")]
    public float halfSailSpeed = 8f;
    public float fullSailSpeed = 18f;
    public float acceleration = 1.5f;

    [Header("Steering & Inertia")]
    public float turnPower = 20f;
    public float turnInertia = 0.95f;
    public float maxTurnSpeed = 25f;

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
    public int sailDelta; // -1 down, +1 up

    [Header("Health")]
    public HealthComponent sailHealth;
    public HealthComponent hullHealth;

    [Range(0f, 1f)]
    public float sailDamageToHullRatio = 1f;
    
    [Tooltip("Below this hull health, ship becomes dead in the water")]
    public float hullDisableThreshold = 30f;

    float currentForwardSpeed;
    float currentAngularVelocity;
    float currentLean;

    bool sailsDestroyed;
    bool hullDisabled;
    private bool destroyed;

    void Awake()
    {
        if (sailHealth)
        {
            sailHealth.OnDestroyed += OnSailsDestroyed;
            sailHealth.OnDamaged += OnSailsDamaged;
        }

        if (hullHealth)
        {
            hullHealth.OnDestroyed += OnHullDestroyed;
            hullHealth.OnDamaged += OnHullDamaged;
        }
    }
    
    public void TryDash()
    {
        if (isDashing)
            return;

        if (dashCooldownTimer > 0f)
            return;

        if (sailsDestroyed || hullDisabled || destroyed)
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

        // Force forward speed during dash
        currentForwardSpeed = dashSpeed;

        if (dashTimer <= 0f)
        {
            isDashing = false;
        }
    }

    void OnDestroy()
    {
        if (sailHealth)
        {
            sailHealth.OnDestroyed -= OnSailsDestroyed;
            sailHealth.OnDamaged -= OnSailsDamaged;
        }

        if (hullHealth)
        {
            hullHealth.OnDestroyed -= OnHullDestroyed;
            hullHealth.OnDamaged -= OnHullDamaged;
        }
    }

    void Update()
    {
        dashCooldownTimer -= Time.deltaTime;

        ApplySailChange();
        ApplyDash();
        ApplyMovement();
    }
    
    void OnSailsDamaged(float dmg)
    {
        if (sailsDestroyed && !destroyed && hullHealth)
        {
            hullHealth.TakeDamage(new DamageInfo
            {
                amount = dmg * sailDamageToHullRatio,
                source = gameObject
            });
        }
    }

    void OnSailsDestroyed()
    {
        sailsDestroyed = true;
        currentSail = SailState.NoSail;
    }

    void OnHullDamaged(float dmg)
    {
        if (hullHealth.currentHealth <= hullDisableThreshold)
            hullDisabled = true;
    }

    void OnHullDestroyed()
    {
        // Ship death
        destroyed = true;
        Destroy(gameObject);
    }

    void ApplySailChange()
    {
        if (sailDelta == 0)
            return;

        if (sailsDestroyed || hullDisabled)
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

    void ApplyMovement()
    {
        if (!isDashing)
        {
            if (sailsDestroyed || hullDisabled)
            {
                currentForwardSpeed = Mathf.MoveTowards(
                    currentForwardSpeed, 0f, acceleration * Time.deltaTime);
            }
            else
            {
                float targetSpeed =
                    currentSail == SailState.FullSail ? fullSailSpeed :
                    currentSail == SailState.HalfSail ? halfSailSpeed :
                    0f;

                currentForwardSpeed =
                    Mathf.MoveTowards(currentForwardSpeed, targetSpeed, acceleration * Time.deltaTime);
            }
        }

        transform.position += transform.forward * currentForwardSpeed * Time.deltaTime;

        float speed01 = Mathf.Clamp01(currentForwardSpeed / fullSailSpeed);

// Even at zero speed, allow weak turning
        float turnEffectiveness = Mathf.Lerp(0.25f, 1f, speed01);

        float speedPenalty =
            Mathf.Lerp(1.2f, 0.5f, speed01);

        currentAngularVelocity +=
            steeringInput * turnPower * speedPenalty * turnEffectiveness * Time.deltaTime;

        currentAngularVelocity *= turnInertia;
        currentAngularVelocity =
            Mathf.Clamp(currentAngularVelocity, -maxTurnSpeed, maxTurnSpeed);

        transform.Rotate(0f, currentAngularVelocity * Time.deltaTime, 0f);

        float targetLean =
            -(currentAngularVelocity / maxTurnSpeed) * leanAmount;

        currentLean =
            Mathf.Lerp(currentLean, targetLean, leanSmoothing * Time.deltaTime);

        transform.rotation *= Quaternion.Euler(0f, 0f, currentLean);
    }
}