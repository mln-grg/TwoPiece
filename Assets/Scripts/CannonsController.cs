using UnityEngine;

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
    public int cannonballsPerSide = 4;

    [Header("Ballistics")]
    public float minHeight = 2f;
    public float maxHeight = 15f;
    public float heightSensitivity = 10f;

    [Tooltip("Horizontal distance the arc should land at")]
    public float shotDistance = 40f;

    float currentHeight;

    bool aimingLeft;
    bool aimingRight;
    bool wasAimingLeft;
    bool wasAimingRight;

    BallisticSolution currentSolution;

    void Update()
    {
        HandleInput();
        UpdateArc();
    }

    void HandleInput()
    {
        bool leftNow  = Input.GetMouseButton(0);
        bool rightNow = Input.GetMouseButton(1);

        // --- RELEASE ---
        if (wasAimingLeft && !leftNow)
            FireBroadside(leftCannonOrigin);

        if (wasAimingRight && !rightNow)
            FireBroadside(rightCannonOrigin);

        aimingLeft  = leftNow;
        aimingRight = rightNow;

        wasAimingLeft  = aimingLeft;
        wasAimingRight = aimingRight;

        if (!aimingLeft && !aimingRight)
        {
            arcMesh.gameObject.SetActive(false);
            return;
        }

        arcMesh.gameObject.SetActive(true);

        // Mouse controls APEX HEIGHT
        float mouseY = Input.GetAxis("Mouse Y");
        currentHeight += mouseY * heightSensitivity * Time.deltaTime;
        currentHeight = Mathf.Clamp(currentHeight, minHeight, maxHeight);
    }

    void UpdateArc()
    {
        if (aimingLeft)
            BuildSolutionAndPreview(leftCannonOrigin);

        else if (aimingRight)
            BuildSolutionAndPreview(rightCannonOrigin);
    }

    void BuildSolutionAndPreview(Transform origin)
    {
        Vector3 forward = origin.forward;

        currentSolution = BuildSolution(
            origin.position,
            forward,
            shotDistance,
            currentHeight
        );

        // Draw preview using SAME solution
        arcMesh.BuildFromBallisticSolution(currentSolution);
    }

    void FireBroadside(Transform origin)
    {
        Vector3 right =
            Vector3.Cross(Vector3.up, origin.forward).normalized;

        float halfWidth = arcMesh.meshWidth * 0.5f;

        for (int i = 0; i < cannonballsPerSide; i++)
        {
            float t = cannonballsPerSide == 1
                ? 0.5f
                : (float)i / (cannonballsPerSide - 1);

            float lateral =
                Mathf.Lerp(-halfWidth, halfWidth, t);

            Vector3 spawnPos =
                origin.position + right * lateral;

            BallisticSolution sol = currentSolution;
            sol.origin = spawnPos;

            Fire(sol);
        }
    }

    void Fire(BallisticSolution sol)
    {
        GameObject ball =
            Instantiate(cannonballPrefab, sol.origin, Quaternion.identity);

        Rigidbody rb = ball.GetComponent<Rigidbody>();
        rb.linearVelocity = sol.velocity;   // 🔑 SAME velocity as preview
        rb.useGravity = true;
    }

    BallisticSolution BuildSolution(
        Vector3 origin,
        Vector3 forward,
        float distance,
        float apexHeight
    )
    {
        float g = Physics.gravity.magnitude;

        float tUp = Mathf.Sqrt(2f * apexHeight / g);
        float totalTime = tUp * 2f;

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