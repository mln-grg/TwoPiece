using UnityEngine;
using System.Collections;
using System.Collections.Generic;

#region Ballistics

public struct BallisticSolution
{
    public Vector3 Origin;
    public Vector3 Velocity;
    public float Gravity;

    public Vector3 Evaluate(float t)
    {
        return Origin
             + Velocity * t
             + 0.5f * Vector3.down * Gravity * t * t;
    }
}

#endregion

public class CannonsController : MonoBehaviour
{
    // =====================================================
    // CONFIG
    // =====================================================

    [Header("Cannon Layout")]
    public int rowsOfGuns = 3;

    [Tooltip("How many rows fire at once")]
    public int rowsPerShot = 1;

    [Tooltip("Delay between row volleys")]
    public float intervalBetweenRows = 0.15f;

    [Header("Ballistics")]
    public float minApex = 3f;
    public float maxApex = 14f;

    [Header("Cannons")]
    public List<Cannon> leftCannons = new();
    public List<Cannon> rightCannons = new();

    [Header("Fallback Origins (preview)")]
    public Transform leftCannonOrigin;
    public Transform rightCannonOrigin;

    [Header("References")]
    public GameObject cannonballPrefab;
    public FixedDistanceArcMesh arcMesh;

    // =====================================================
    // INTERNAL
    // =====================================================

    float playerLeftApex;
    float playerRightApex;
    bool hasPlayerApex;

    // =====================================================
    // PREVIEW (UNCHANGED CONTRACT)
    // =====================================================

    public void PreviewLeft(float apex)
    {
        playerLeftApex = apex;
        hasPlayerApex = true;
        PreviewArea(leftCannonOrigin, apex);
    }

    public void PreviewRight(float apex)
    {
        playerRightApex = apex;
        hasPlayerApex = true;
        PreviewArea(rightCannonOrigin, apex);
    }

    public void HidePreview()
    {
        if (arcMesh)
            arcMesh.gameObject.SetActive(false);
    }

    void PreviewArea(Transform origin, float apex)
    {
        if (!origin || !arcMesh)
            return;

        BallisticSolution sol = BuildSolution(
            origin.position,
            origin.forward,
            40f,
            apex
        );

        arcMesh.gameObject.SetActive(true);
        arcMesh.BuildFromBallisticSolution(sol);
    }

    // =====================================================
    // FIRING API
    // =====================================================

    public void FireLeftBroadside()
    {
        float apex = hasPlayerApex ? playerLeftApex : GetDefaultApex();
        StartCoroutine(FireByRows(leftCannons, leftCannonOrigin, apex));
    }

    public void FireRightBroadside()
    {
        float apex = hasPlayerApex ? playerRightApex : GetDefaultApex();
        StartCoroutine(FireByRows(rightCannons, rightCannonOrigin, apex));
    }

    // =====================================================
    // CORE LOGIC
    // =====================================================

    IEnumerator FireByRows(
        List<Cannon> cannons,
        Transform fallbackOrigin,
        float apex
    )
    {
        if (cannons == null || cannons.Count == 0 || fallbackOrigin == null)
            yield break;

        List<List<Cannon>> rows = BuildRows(cannons);

        BallisticSolution reference =
            BuildSolution(
                fallbackOrigin.position,
                fallbackOrigin.forward,
                40f,
                apex
            );

        for (int r = 0; r < rows.Count; r += rowsPerShot)
        {
            for (int i = 0; i < rowsPerShot; i++)
            {
                int rowIndex = r + i;
                if (rowIndex >= rows.Count)
                    break;

                FireRow(rows[rowIndex], reference);
            }

            yield return new WaitForSeconds(intervalBetweenRows);
        }
    }

    void FireRow(
        List<Cannon> row,
        BallisticSolution reference
    )
    {
        float spacing = 0.6f;

        int count = row.Count;
        int mid = count / 2;

        for (int i = 0; i < count; i++)
        {
            Cannon cannon = row[i];

            float offset =
                (i - mid) * spacing;

            Vector3 origin =
                cannon.muzzle
                    ? cannon.muzzle.position
                    : cannon.transform.position;

            Vector3 velocity =
                reference.Velocity +
                cannon.transform.right * offset;

            BallisticSolution sol = new BallisticSolution
            {
                Origin = origin,
                Velocity = velocity,
                Gravity = reference.Gravity
            };

            Cannonball ball = Instantiate(
                cannonballPrefab,
                sol.Origin,
                Quaternion.identity
            ).GetComponent<Cannonball>();

            ball.Owner = gameObject;
            ball.LaunchAnalytic(sol);
        }
    }

    // =====================================================
    // ROW BUILDING
    // =====================================================

    List<List<Cannon>> BuildRows(List<Cannon> cannons)
    {
        cannons.Sort((a, b) =>
            b.transform.position.y.CompareTo(a.transform.position.y));

        List<List<Cannon>> rows = new();

        int perRow = Mathf.CeilToInt((float)cannons.Count / rowsOfGuns);

        for (int i = 0; i < cannons.Count; i += perRow)
        {
            List<Cannon> row = cannons.GetRange(
                i,
                Mathf.Min(perRow, cannons.Count - i));

            row.Sort((a, b) =>
                a.transform.localPosition.x.CompareTo(b.transform.localPosition.x));

            rows.Add(row);
        }

        return rows;
    }

    // =====================================================
    // BALLISTICS
    // =====================================================

    BallisticSolution BuildSolution(
        Vector3 origin,
        Vector3 forward,
        float distance,
        float apex
    )
    {
        float g = Physics.gravity.magnitude;

        float tUp = Mathf.Sqrt(2f * apex / g);
        float totalTime = tUp * 2f;

        float horizontalSpeed = distance / totalTime;

        return new BallisticSolution
        {
            Origin = origin,
            Velocity =
                forward.normalized * horizontalSpeed +
                Vector3.up * Mathf.Sqrt(2f * g * apex),
            Gravity = g
        };
    }

    float GetDefaultApex()
    {
        return Mathf.Lerp(minApex, maxApex, 0.7f);
    }
}
