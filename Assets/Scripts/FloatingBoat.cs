using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public class FloatingBoat : MonoBehaviour
{
    [Header("Water")]
    public WaterSurface targetSurface;
    public bool includeDeformers = true;
    public float verticalOffset = 0f;

    [Header("Sampling")]
    public float length = 4f;
    public float width = 2f;

    [Header("Vertical Motion")]
    public float verticalSpring = 12f;   // stiffness
    public float verticalDamping = 6f;   // resistance

    [Header("Rotation Response")]
    public float normalFollowSpeed = 6f;

    float verticalVelocity;
    float currentY;
    Quaternion currentTilt;

    WaterSearchParameters searchParams = new();
    WaterSearchResult searchResult = new();

    void OnEnable()
    {
        currentY = transform.position.y;
        currentTilt = transform.rotation;
    }

    void LateUpdate()
    {
        if (!targetSurface)
            return;

        Vector3 pos = transform.position;
        Vector3 fwd = transform.forward;
        Vector3 right = transform.right;

        Vector3 bow   = pos + fwd * (length * 0.5f);
        Vector3 stern = pos - fwd * (length * 0.5f);
        Vector3 left  = pos - right * (width * 0.5f);
        Vector3 rightP= pos + right * (width * 0.5f);

        float hBow = SampleHeight(bow);
        float hStern = SampleHeight(stern);
        float hLeft = SampleHeight(left);
        float hRight = SampleHeight(rightP);
        float hCenter = SampleHeight(pos);

        // ---------------- HEIGHT (SPRING) ----------------
        float targetY =
            Mathf.Max(hCenter, (hBow + hStern + hLeft + hRight) * 0.25f)
            + verticalOffset;

        float error = targetY - currentY;

        verticalVelocity += error * verticalSpring * Time.deltaTime;
        verticalVelocity -= verticalVelocity * verticalDamping * Time.deltaTime;

        currentY += verticalVelocity * Time.deltaTime;

        // ---------------- NORMAL ----------------
        Vector3 pBow = new(bow.x, hBow, bow.z);
        Vector3 pStern = new(stern.x, hStern, stern.z);
        Vector3 pLeft = new(left.x, hLeft, left.z);
        Vector3 pRight = new(rightP.x, hRight, rightP.z);

        Vector3 forwardDir = (pBow - pStern).normalized;
        Vector3 rightDir = (pRight - pLeft).normalized;
        Vector3 waterNormal = Vector3.Cross(forwardDir, rightDir).normalized;

        Quaternion targetTilt =
            Quaternion.FromToRotation(Vector3.up, waterNormal);

        currentTilt = Quaternion.Slerp(
            currentTilt,
            targetTilt,
            normalFollowSpeed * Time.deltaTime
        );

        // ---------------- APPLY ----------------
        Vector3 euler = transform.rotation.eulerAngles;

        transform.SetPositionAndRotation(
            new Vector3(pos.x, currentY, pos.z),
            Quaternion.Euler(
                currentTilt.eulerAngles.x,
                euler.y,
                currentTilt.eulerAngles.z
            )
        );
    }

    float SampleHeight(Vector3 worldPos)
    {
        searchParams.startPositionWS = (float3)worldPos;
        searchParams.targetPositionWS = (float3)worldPos;
        searchParams.includeDeformation = includeDeformers;

        if (targetSurface.ProjectPointOnWaterSurface(searchParams, out searchResult))
            return searchResult.projectedPositionWS.y;

        return worldPos.y;
    }
}
