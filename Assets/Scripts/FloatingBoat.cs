using UnityEngine; // Unity core engine
using UnityEngine.Rendering.HighDefinition; // HDRP-specific rendering
using Unity.Mathematics; // Unity mathematics library
using System.Linq; // LINQ support

#if UNITY_EDITOR
using UnityEditor; // Editor tools
#endif

[ExecuteInEditMode] // Execute in editor mode
public class FloatingBoat : MonoBehaviour
{
    [Header("Water")] public WaterSurface targetSurface;
    public bool includeDeformers = true;
    public float verticalOffset = 0f;

    [Header("Sampling")] public float length = 4f;
    public float width = 2f;

    [Header("Buoyancy Response")] [Tooltip("How fast the boat follows wave height")]
    public float heightFollowSpeed = 12f;

    [Tooltip("How fast the boat aligns to wave normal")]
    public float normalFollowSpeed = 8f;

    [Header("Debug")] public bool showGizmos = true;

    WaterSearchParameters searchParams = new WaterSearchParameters();
    WaterSearchResult searchResult = new WaterSearchResult();

    float smoothedY;
    Quaternion smoothedTilt;

    void OnEnable()
    {
        smoothedY = transform.position.y;
        smoothedTilt = Quaternion.identity;
    }

    void LateUpdate()
    {
        if (!targetSurface)
            return;

        // --- WORLD SPACE SAMPLE POINTS ---
        Vector3 pos = transform.position;

        Vector3 fwd = transform.rotation * Vector3.forward;
        Vector3 right = transform.rotation * Vector3.right;

        Vector3 bow = pos + fwd * (length * 0.5f);
        Vector3 stern = pos - fwd * (length * 0.5f);
        Vector3 left = pos - right * (width * 0.5f);
        Vector3 rightP = pos + right * (width * 0.5f);

        float hBow = SampleHeight(bow);
        float hStern = SampleHeight(stern);
        float hLeft = SampleHeight(left);
        float hRight = SampleHeight(rightP);
        float hCenter = SampleHeight(pos);

        // --- HEIGHT ---
        float targetY = Mathf.Max(hCenter, (hBow + hStern + hLeft + hRight) * 0.25f);
        targetY += verticalOffset;

        smoothedY = Mathf.Lerp(
            smoothedY,
            targetY,
            heightFollowSpeed * Time.deltaTime
        );

        // --- NORMAL ---
        Vector3 pBow = new Vector3(bow.x, hBow, bow.z);
        Vector3 pStern = new Vector3(stern.x, hStern, stern.z);
        Vector3 pLeft = new Vector3(left.x, hLeft, left.z);
        Vector3 pRight = new Vector3(rightP.x, hRight, rightP.z);

        Vector3 forwardDir = (pBow - pStern).normalized;
        Vector3 rightDir = (pRight - pLeft).normalized;

        Vector3 waterNormal = Vector3.Cross(forwardDir, rightDir).normalized;

        Quaternion targetTilt = Quaternion.FromToRotation(Vector3.up, waterNormal);

        smoothedTilt = Quaternion.Slerp(
            smoothedTilt,
            targetTilt,
            normalFollowSpeed * Time.deltaTime
        );

        // --- APPLY (PRESERVE YAW) ---
        Vector3 euler = transform.rotation.eulerAngles;

        Quaternion finalRotation = Quaternion.Euler(
            smoothedTilt.eulerAngles.x,
            euler.y,
            smoothedTilt.eulerAngles.z
        );

        transform.SetPositionAndRotation(
            new Vector3(pos.x, smoothedY, pos.z),
            finalRotation
        );
    }

    float SampleHeight(Vector3 worldPos)
    {
        searchParams.startPositionWS = (float3)worldPos;
        searchParams.targetPositionWS = (float3)worldPos;
        searchParams.includeDeformation = includeDeformers;
        searchParams.maxIterations = 8;
        searchParams.error = 0.01f;
        searchParams.excludeSimulation = false;

        if (targetSurface.ProjectPointOnWaterSurface(searchParams, out searchResult))
            return searchResult.projectedPositionWS.y;

        return worldPos.y;
    }

#if UNITY_EDITOR
    void OnDrawGizmos() 
    {
        if (!showGizmos || !targetSurface)
            return;

        Gizmos.color = Color.cyan;

        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;

        Vector3 fwd = rot * Vector3.forward;
        Vector3 right = rot * Vector3.right;

        Vector3 bow = pos + fwd * (length * 0.5f);
        Vector3 stern = pos - fwd * (length * 0.5f);
        Vector3 left = pos - right * (width * 0.5f);
        Vector3 rightP = pos + right * (width * 0.5f);

        Gizmos.DrawSphere(bow, 0.08f);
        Gizmos.DrawSphere(stern, 0.08f);
        Gizmos.DrawSphere(left, 0.08f);
        Gizmos.DrawSphere(rightP, 0.08f);

        Gizmos.DrawLine(bow, stern);
        Gizmos.DrawLine(left, rightP);
    }
#endif
}