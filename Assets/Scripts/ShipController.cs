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

    [Header("State")]
    public SailState currentSail = SailState.NoSail;

    [Header("Control Input (AI / Player)")]
    [Range(-1f, 1f)] public float steeringInput;
    public int sailDelta; // -1 down, +1 up

    float currentForwardSpeed;
    float currentAngularVelocity;
    float currentLean;

    void Update()
    {
        ApplySailChange();
        ApplyMovement();
    }

    void ApplySailChange()
    {
        if (sailDelta == 0) return;

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
        float targetSpeed =
            currentSail == SailState.FullSail ? fullSailSpeed :
            currentSail == SailState.HalfSail ? halfSailSpeed :
            0f;

        currentForwardSpeed =
            Mathf.MoveTowards(currentForwardSpeed, targetSpeed, acceleration * Time.deltaTime);

        transform.position += transform.forward * currentForwardSpeed * Time.deltaTime;

        if (currentForwardSpeed > 0.2f)
        {
            float speedPenalty =
                Mathf.Lerp(1.2f, 0.5f, currentForwardSpeed / fullSailSpeed);

            currentAngularVelocity +=
                steeringInput * turnPower * speedPenalty * Time.deltaTime;
        }

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