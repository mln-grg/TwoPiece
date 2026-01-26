using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public struct BallisticSolution
{
    public Vector3 origin;
    public Vector3 velocity;
    public float gravity;

    public Vector3 Evaluate(float t)
    {
        return origin
               + velocity * t
               + 0.5f * Vector3.down * gravity * t * t;
    }
}

public class CannonsController : MonoBehaviour
{
    [Header("References")]
    public FixedDistanceArcMesh arcMesh;
    public Transform leftCannonOrigin;
    public Transform rightCannonOrigin;

    [Header("Firing")]
    public GameObject cannonballPrefab;
    public int cannonballsPerSide = 8;
    public float muzzleSpeed = 32f;
    public float spread = 0.1f;
    
    [Header("Arc Settings")]
    public float minHeight = 2f;
    public float maxHeight = 15f;
    public float heightSensitivity = 10f;

    float currentHeight;
    bool aimingLeft;
    bool aimingRight;
    
    bool wasAimingLeft;
    bool wasAimingRight;

    void Update()
    {
        HandleInput();
        UpdateArc();
    }

    void HandleInput()
    {
        bool leftNow  = Input.GetMouseButton(0);
        bool rightNow = Input.GetMouseButton(1);

        // --- RELEASE DETECTION ---
        if (wasAimingLeft && !leftNow)
        {
            FireAlongArc(leftCannonOrigin.position);
        }

        if (wasAimingRight && !rightNow)
        {
            FireAlongArc(rightCannonOrigin.position);
        }

        aimingLeft  = leftNow;
        aimingRight = rightNow;

        wasAimingLeft  = aimingLeft;
        wasAimingRight = aimingRight;

        // --- ARC VISIBILITY ---
        if (!aimingLeft && !aimingRight)
        {
            arcMesh.gameObject.SetActive(false);
            return;
        }

        arcMesh.gameObject.SetActive(true);

        // --- HEIGHT CONTROL ---
        float mouseY = Input.GetAxis("Mouse Y");
        currentHeight += mouseY * heightSensitivity * Time.deltaTime;
        currentHeight = Mathf.Clamp(currentHeight, minHeight, maxHeight);

    }

    void UpdateArc()
    {
        if (aimingLeft)
        {
            arcMesh.height = currentHeight;
            arcMesh.BuildArc(
                leftCannonOrigin.position,
                leftCannonOrigin.forward
            );
        }
        else if (aimingRight)
        {
            arcMesh.height = currentHeight;
            arcMesh.BuildArc(
                rightCannonOrigin.position,
                rightCannonOrigin.forward
            );
        }
    }
    
    void FireAlongArc(Vector3 fireOrigin)
    {
        for (int i = 0; i < cannonballsPerSide; i++)
        {
            // Pick a point along the arc (avoid very start/end)
            int index = Random.Range(
                Mathf.RoundToInt(0.2f * arcMesh.resolution),
                Mathf.RoundToInt(0.8f * arcMesh.resolution)
            );

            arcMesh.GetArcSample(
                index,
                out Vector3 arcPos,
                out Vector3 arcRight
            );

            // Spread INSIDE the ribbon
            float halfWidth = arcMesh.meshWidth * 0.5f;
            float lateralOffset = Random.Range(-halfWidth, halfWidth);

            Vector3 target =
                arcPos + arcRight * lateralOffset;

            Vector3 dir =
                (target - fireOrigin).normalized;

            GameObject ball = Instantiate(
                cannonballPrefab,
                fireOrigin,
                Quaternion.identity
            );

            Rigidbody rb = ball.GetComponent<Rigidbody>();
            rb.linearVelocity = dir * muzzleSpeed;
        }
    }
    
    BallisticSolution BuildSolution(
        Vector3 origin,
        Vector3 forward,
        float distance,
        float apexHeight
    )
    {
        float g = Physics.gravity.magnitude;

        // Time to reach apex
        float tUp = Mathf.Sqrt(2f * apexHeight / g);

        // Total flight time (symmetric)
        float totalTime = tUp * 2f;

        // Horizontal speed needed to cover distance
        float horizontalSpeed = distance / totalTime;

        Vector3 horizontalVel =
            forward.normalized * horizontalSpeed;

        Vector3 verticalVel =
            Vector3.up * Mathf.Sqrt(2f * g * apexHeight);

        return new BallisticSolution
        {
            origin = origin,
            velocity = horizontalVel + verticalVel,
            gravity = g
        };
    }
}
