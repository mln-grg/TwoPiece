using System;
using UnityEngine;

public enum SailState { NoSail, HalfSail, FullSail }

public class ShipController : MonoBehaviour
{
[Header("Speeds")]
    public float halfSailSpeed = 8f;
    public float fullSailSpeed = 18f;
    public float acceleration = 1.5f;
    
    [Header("Steering & Inertia")]
    public float turnPower = 20f;      // How hard the rudder pushes
    public float turnInertia = 0.95f;  // 0 to 1 (Higher = more drift/sliding turn)
    public float maxTurnSpeed = 25f;   // Speed cap for rotation

    [Header("Ship Lean (Heel)")]
    public float leanAmount = 15f;     // Max degrees the ship tilts during a turn
    public float leanSmoothing = 2f;   // How fast the ship tilts

    [Header("Current State")]
    public SailState currentSail = SailState.NoSail;
    
    private float currentForwardSpeed = 0f;
    private float targetSpeed = 0f;
    private float currentAngularVelocity = 0f; // This creates the inertia
    private float currentLean = 0f;

    void Update()
    {
        HandleInput();
        ApplyMovement();
    }

    void HandleInput()
    {
        // Sail Toggles (W/S)
        if (Input.GetKeyDown(KeyCode.W))
        {
            if (currentSail == SailState.NoSail) currentSail = SailState.HalfSail;
            else if (currentSail == SailState.HalfSail) currentSail = SailState.FullSail;
        }
        else if (Input.GetKeyDown(KeyCode.S))
        {
            if (currentSail == SailState.FullSail) currentSail = SailState.HalfSail;
            else if (currentSail == SailState.HalfSail) currentSail = SailState.NoSail;
        }
    }

    void ApplyMovement()
    {
        // 1. Forward Momentum
        switch (currentSail)
        {
            case SailState.NoSail: targetSpeed = 0f; break;
            case SailState.HalfSail: targetSpeed = halfSailSpeed; break;
            case SailState.FullSail: targetSpeed = fullSailSpeed; break;
        }
        currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetSpeed, acceleration * Time.deltaTime);
        transform.position += transform.forward * currentForwardSpeed * Time.deltaTime;

        // 2. Rotation Inertia (The "Heavy" Turning)
        float horizontalInput = Input.GetAxis("Horizontal");
        
        // If moving, apply force to our angular velocity
        if (currentForwardSpeed > 0.5f)
        {
            // We turn slower at high speeds (Black Flag style)
            float speedTurnPenalty = Mathf.Lerp(1.2f, 0.5f, currentForwardSpeed / fullSailSpeed);
            currentAngularVelocity += horizontalInput * turnPower * speedTurnPenalty * Time.deltaTime;
        }

        // Apply friction/drag to the rotation (This makes it eventually stop)
        currentAngularVelocity *= turnInertia; 
        currentAngularVelocity = Mathf.Clamp(currentAngularVelocity, -maxTurnSpeed, maxTurnSpeed);

        // Apply the rotation to the transform
        transform.Rotate(0, currentAngularVelocity * Time.deltaTime, 0);

        // 3. Ship Lean (Visual Heel)
        // Calculate target lean based on how fast we are currently rotating
        float targetLean = -(currentAngularVelocity / maxTurnSpeed) * leanAmount;
        currentLean = Mathf.Lerp(currentLean, targetLean, leanSmoothing * Time.deltaTime);

        // Apply lean to the ship's visual rotation
        // Note: We apply this to the Z axis (Roll)
        transform.rotation *= Quaternion.Euler(0, 0, currentLean);
    }
}
