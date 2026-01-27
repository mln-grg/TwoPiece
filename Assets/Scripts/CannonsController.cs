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
    [Header("Shot Variation")]
    public float lateralSpread = 0.5f;
    public float verticalSpread = 0.3f;
    public float speedVariance = 0.05f;

    [Header("Firing Count")]
    public int shotsPerBroadside = 8;
    public int cannonballsPerSide = 4;

    [Header("References")]
    public Transform leftCannonOrigin;
    public Transform rightCannonOrigin;
    public GameObject cannonballPrefab;

    [Tooltip("Only used for PLAYER preview")]
    public FixedDistanceArcMesh arcMesh;

    [Header("Ballistics")]
    public float shotDistance = 40f;
    public float minHeight = 2f;
    public float maxHeight = 15f;

    // =====================================================
    // PUBLIC API — FIRING
    // =====================================================

    public void FireLeftBroadside()
    {
        FireBroadside(leftCannonOrigin, GetDefaultApex());
    }

    public void FireRightBroadside()
    {
        FireBroadside(rightCannonOrigin, GetDefaultApex());
    }

    public void FireLeftBroadsideAI()
    {
        FireBroadside(leftCannonOrigin, GetAIApex());
    }

    public void FireRightBroadsideAI()
    {
        FireBroadside(rightCannonOrigin, GetAIApex());
    }

    // =====================================================
    // PUBLIC API — PREVIEW (PLAYER ONLY)
    // =====================================================

    public void PreviewLeft(float apexHeight)
    {
        BuildPreview(leftCannonOrigin, apexHeight);
    }

    public void PreviewRight(float apexHeight)
    {
        BuildPreview(rightCannonOrigin, apexHeight);
    }

    public void HidePreview()
    {
        if (arcMesh)
            arcMesh.gameObject.SetActive(false);
    }

    // =====================================================
    // INTERNAL — PREVIEW
    // =====================================================

    void BuildPreview(Transform origin, float apexHeight)
    {
        if (!arcMesh)
            return;

        BallisticSolution sol = BuildSolution(
            origin.position,
            origin.forward,
            shotDistance,
            apexHeight
        );

        arcMesh.gameObject.SetActive(true);
        arcMesh.BuildFromBallisticSolution(sol);
    }

    // =====================================================
    // INTERNAL — FIRING
    // =====================================================

    void FireBroadside(Transform origin, float apexHeight)
    {
        BallisticSolution baseSolution =
            BuildSolution(origin.position, origin.forward, shotDistance, apexHeight);

        Vector3 right =
            Vector3.Cross(Vector3.up, origin.forward).normalized;

        float halfWidth = 2f;

        for (int i = 0; i < shotsPerBroadside; i++)
        {
            float laneT = cannonballsPerSide == 1
                ? 0.5f
                : (float)i / (cannonballsPerSide - 1);

            Vector3 spawnPos =
                origin.position + right * Mathf.Lerp(-halfWidth, halfWidth, laneT);

            BallisticSolution sol = baseSolution;
            sol.origin = spawnPos;

            Fire(sol);
        }
    }

    void Fire(BallisticSolution sol)
    {
        Vector3 right =
            Vector3.Cross(Vector3.up, sol.velocity).normalized;

        sol.origin += right * Random.Range(-lateralSpread, lateralSpread);
        sol.velocity += Vector3.up * Random.Range(-verticalSpread, verticalSpread);
        sol.velocity *= 1f + Random.Range(-speedVariance, speedVariance);

        Rigidbody rb = Instantiate(
            cannonballPrefab,
            sol.origin,
            Quaternion.identity
        ).GetComponent<Rigidbody>();

        rb.velocity = sol.velocity;
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

        return new BallisticSolution
        {
            origin = origin,
            velocity =
                forward.normalized * horizontalSpeed +
                Vector3.up * Mathf.Sqrt(2f * g * apexHeight),
            gravity = g
        };
    }

    float GetDefaultApex() => Mathf.Lerp(minHeight, maxHeight, 0.7f);
    float GetAIApex()      => Mathf.Lerp(minHeight, maxHeight, 0.6f);
}