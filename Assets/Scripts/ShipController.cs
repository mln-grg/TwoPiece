using System;
using UnityEngine;

public class ShipController : MonoBehaviour
{
    public enum SailState
    {
        NoSail = 0,
        HalfSail = 1,
        FullSail = 2
    }

    [Header("Sails (Speed Only)")]
    public SailState sailState = SailState.NoSail;
    public float halfSailSpeed = 6f;
    public float fullSailSpeed = 12f;
    public float sailAcceleration = 3f;
    public float waterDrag = 1.2f;

    [Header("Rotation")]
    public float turnAcceleration = 120f;   // how fast yaw builds
    public float maxTurnSpeed = 90f;        // deg/sec
    public float turnDamping = 2f;           // how slowly rotation bleeds off

    float currentSpeed;
    float currentTurnSpeed;

    void Update()
    {
        HandleSails();
        HandleRotation();
        ApplyMovement();
    }

    void HandleSails()
    {
        if (Input.GetKeyDown(KeyCode.W))
            sailState = (SailState)Mathf.Min((int)sailState + 1, 2);

        if (Input.GetKeyDown(KeyCode.S))
            sailState = (SailState)Mathf.Max((int)sailState - 1, 0);
    }

    void HandleRotation()
    {
        float steer = Input.GetAxisRaw("Horizontal");

        // Build angular velocity
        currentTurnSpeed += steer * turnAcceleration * Time.deltaTime;

        // Clamp max yaw speed
        currentTurnSpeed = Mathf.Clamp(
            currentTurnSpeed,
            -maxTurnSpeed,
            maxTurnSpeed
        );

        // Rotational inertia (bleeds off slowly)
        if (Mathf.Abs(steer) < 0.01f)
        {
            currentTurnSpeed = Mathf.MoveTowards(
                currentTurnSpeed,
                0f,
                turnDamping * Time.deltaTime * maxTurnSpeed
            );
        }
    }

    void ApplyMovement()
    {
        float targetSpeed = sailState switch
        {
            SailState.NoSail => 0f,
            SailState.HalfSail => halfSailSpeed,
            SailState.FullSail => fullSailSpeed,
            _ => 0f
        };

        // Speed inertia
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            sailAcceleration * Time.deltaTime
        );

        // Passive water drag
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            0f,
            waterDrag * Time.deltaTime
        );

        // ROTATION FIRST (momentum-based)
        transform.Rotate(
            Vector3.up,
            currentTurnSpeed * Time.deltaTime,
            Space.World
        );

        // THEN movement
        transform.position += transform.forward * currentSpeed * Time.deltaTime;
    }
}
