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
    public float intervalBetweenRows = 0.12f;

    [Header("Ballistics")]
    public float defaultRange = 40f;
    public float minApex = 2f;
    public float maxApex = 18f;

    [Tooltip("Muzzle velocity for cannons")]
    public float muzzleVelocity = 35f;

    [Header("Cannons")]
    public List<Cannon> leftCannons = new();
    public List<Cannon> rightCannons = new();
    public List<Cannon> frontCannons = new();
    public List<Cannon> backCannons = new();

    [Header("Fallback Origins")]
    public Transform leftCannonOrigin;
    public Transform rightCannonOrigin;
    public Transform frontCannonOrigin;
    public Transform backCannonOrigin;

    [Header("References")]
    public GameObject cannonballPrefab;
    public FixedDistanceArcMesh arcMesh;

    [Header("Firing Audio/VFX")]
    public AudioClip fireSound;
    public float fireVolume = 0.7f;

    // =====================================================
    // INTERNAL
    // =====================================================

    Vector3 leftAimPoint;
    Vector3 rightAimPoint;
    Vector3 frontAimPoint;
    Vector3 backAimPoint;
    
    bool hasLeftAimPoint;
    bool hasRightAimPoint;
    bool hasFrontAimPoint;
    bool hasBackAimPoint;

    float lastFireTime;
    public float minFireInterval = 0.5f; // Prevent spam

    // =====================================================
    // PREVIEW API
    // =====================================================

    public void PreviewLeft(float apex)
    {
        if (!leftCannonOrigin) return;
        
        BallisticSolution sol = BuildSolution(
            leftCannonOrigin.position,
            leftCannonOrigin.forward,
            defaultRange,
            apex
        );

        ShowPreview(sol);
    }

    public void PreviewRight(float apex)
    {
        if (!rightCannonOrigin) return;
        
        BallisticSolution sol = BuildSolution(
            rightCannonOrigin.position,
            rightCannonOrigin.forward,
            defaultRange,
            apex
        );

        ShowPreview(sol);
    }
    
    public void PreviewFront(float apex)
    {
        if (!frontCannonOrigin) return;
        
        BallisticSolution sol = BuildSolution(
            frontCannonOrigin.position,
            frontCannonOrigin.forward,
            defaultRange,
            apex
        );

        ShowPreview(sol);
    }

    public void PreviewBack(float apex)
    {
        if (!backCannonOrigin) return;
        
        BallisticSolution sol = BuildSolution(
            backCannonOrigin.position,
            backCannonOrigin.forward,
            defaultRange,
            apex
        );

        ShowPreview(sol);
    }

    // Preview to specific point - Left/Right (Broadsides)
    public void PreviewLeftToPoint(Vector3 targetPoint)
    {
        if (!leftCannonOrigin) return;

        leftAimPoint = targetPoint;
        hasLeftAimPoint = true;

        BallisticSolution sol = SolveToPoint(leftCannonOrigin.position, leftCannonOrigin.forward, targetPoint);
        ShowPreview(sol);
    }

    public void PreviewRightToPoint(Vector3 targetPoint)
    {
        if (!rightCannonOrigin) return;

        rightAimPoint = targetPoint;
        hasRightAimPoint = true;

        BallisticSolution sol = SolveToPoint(rightCannonOrigin.position, rightCannonOrigin.forward, targetPoint);
        ShowPreview(sol);
    }
    
    // Preview to specific point - Front/Back (Chain shot / Oil barrels)
    public void PreviewFrontToPoint(Vector3 targetPoint)
    {
        if (!frontCannonOrigin) return;

        frontAimPoint = targetPoint;
        hasFrontAimPoint = true;

        BallisticSolution sol = SolveToPoint(frontCannonOrigin.position, frontCannonOrigin.forward, targetPoint);
        ShowPreview(sol);
    }

    public void PreviewBackToPoint(Vector3 targetPoint)
    {
        if (!backCannonOrigin) return;

        backAimPoint = targetPoint;
        hasBackAimPoint = true;

        BallisticSolution sol = SolveToPoint(backCannonOrigin.position, backCannonOrigin.forward, targetPoint);
        ShowPreview(sol);
    }

    void ShowPreview(BallisticSolution sol)
    {
        if (!arcMesh) return;

        arcMesh.gameObject.SetActive(true);
        arcMesh.BuildFromBallisticSolution(sol);
    }

    public void HidePreview()
    {
        if (arcMesh)
            arcMesh.gameObject.SetActive(false);

        hasLeftAimPoint = false;
        hasRightAimPoint = false;
        hasFrontAimPoint = false;
        hasBackAimPoint = false;
    }

    // =====================================================
    // FIRING API
    // =====================================================

    public void FireLeftBroadside()
    {
        if (Time.time - lastFireTime < minFireInterval) return;
        lastFireTime = Time.time;

        float apex = GetDefaultApex();
        StartCoroutine(FireByRows(leftCannons, leftCannonOrigin, null, apex));
    }

    public void FireRightBroadside()
    {
        if (Time.time - lastFireTime < minFireInterval) return;
        lastFireTime = Time.time;

        float apex = GetDefaultApex();
        StartCoroutine(FireByRows(rightCannons, rightCannonOrigin, null, apex));
    }
    
    public void FireFront()
    {
        if (Time.time - lastFireTime < minFireInterval) return;
        lastFireTime = Time.time;

        float apex = GetDefaultApex();
        StartCoroutine(FireByRows(frontCannons, frontCannonOrigin, null, apex));
    }

    public void FireBack()
    {
        if (Time.time - lastFireTime < minFireInterval) return;
        lastFireTime = Time.time;

        float apex = GetDefaultApex();
        StartCoroutine(FireByRows(backCannons, backCannonOrigin, null, apex));
    }

    // Fire at specific point - Broadsides
    public void FireLeftBroadsideAtPoint(Vector3 targetPoint)
    {
        if (Time.time - lastFireTime < minFireInterval) return;
        lastFireTime = Time.time;

        StartCoroutine(FireByRows(leftCannons, leftCannonOrigin, targetPoint, 0f));
    }

    public void FireRightBroadsideAtPoint(Vector3 targetPoint)
    {
        if (Time.time - lastFireTime < minFireInterval) return;
        lastFireTime = Time.time;

        StartCoroutine(FireByRows(rightCannons, rightCannonOrigin, targetPoint, 0f));
    }
    
    // Fire at specific point - Front/Back
    public void FireFrontAtPoint(Vector3 targetPoint)
    {
        if (Time.time - lastFireTime < minFireInterval) return;
        lastFireTime = Time.time;

        StartCoroutine(FireByRows(frontCannons, frontCannonOrigin, targetPoint, 0f));
    }

    public void FireBackAtPoint(Vector3 targetPoint)
    {
        if (Time.time - lastFireTime < minFireInterval) return;
        lastFireTime = Time.time;

        StartCoroutine(FireByRows(backCannons, backCannonOrigin, targetPoint, 0f));
    }

    // =====================================================
    // CORE FIRING LOGIC
    // =====================================================

    IEnumerator FireByRows(
        List<Cannon> cannons,
        Transform fallbackOrigin,
        Vector3? targetPoint,
        float apex
    )
    {
        if (cannons == null || cannons.Count == 0 || fallbackOrigin == null)
            yield break;

        List<List<Cannon>> rows = BuildRows(cannons);

        // Build reference solution
        BallisticSolution reference;
        if (targetPoint.HasValue)
        {
            reference = SolveToPoint(fallbackOrigin.position, fallbackOrigin.forward, targetPoint.Value);
        }
        else
        {
            reference = BuildSolution(fallbackOrigin.position, fallbackOrigin.forward, defaultRange, apex);
        }

        // Fire rows with stagger
        for (int r = 0; r < rows.Count; r += rowsPerShot)
        {
            for (int i = 0; i < rowsPerShot; i++)
            {
                int rowIndex = r + i;
                if (rowIndex >= rows.Count)
                    break;

                FireRow(rows[rowIndex], reference);
            }

            // Play fire sound
            if (fireSound)
                AudioSource.PlayClipAtPoint(fireSound, fallbackOrigin.position, fireVolume);

            yield return new WaitForSeconds(intervalBetweenRows);
        }
    }

    void FireRow(List<Cannon> row, BallisticSolution reference)
    {
        float spacing = 0.4f; // Spread for visual variety

        int count = row.Count;
        int mid = count / 2;

        for (int i = 0; i < count; i++)
        {
            Cannon cannon = row[i];

            // Calculate lateral offset for spread
            float offset = (i - mid) * spacing;

            Vector3 origin = cannon.muzzle ? cannon.muzzle.position : cannon.transform.position;

            // Add slight variation to velocity based on cannon position
            Vector3 velocity = reference.Velocity + cannon.transform.right * offset;

            BallisticSolution sol = new BallisticSolution
            {
                Origin = origin,
                Velocity = velocity,
                Gravity = reference.Gravity
            };

            // Spawn cannonball
            GameObject ballObj = Instantiate(cannonballPrefab, sol.Origin, Quaternion.identity);
            Cannonball ball = ballObj.GetComponent<Cannonball>();

            if (ball)
            {
                ball.Owner = gameObject;
                ball.LaunchAnalytic(sol);
            }

            // Trigger cannon effects
            TriggerCannonEffects(cannon);
        }
    }

    void TriggerCannonEffects(Cannon cannon)
    {
        if (cannon)
            cannon.Fire();
    }

    // =====================================================
    // ROW BUILDING
    // =====================================================

    List<List<Cannon>> BuildRows(List<Cannon> cannons)
    {
        // Sort by height (top to bottom)
        cannons.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));

        List<List<Cannon>> rows = new();
        int perRow = Mathf.CeilToInt((float)cannons.Count / rowsOfGuns);

        for (int i = 0; i < cannons.Count; i += perRow)
        {
            List<Cannon> row = cannons.GetRange(i, Mathf.Min(perRow, cannons.Count - i));

            // Sort each row by x position (fore to aft)
            row.Sort((a, b) => a.transform.localPosition.x.CompareTo(b.transform.localPosition.x));

            rows.Add(row);
        }

        return rows;
    }

    // =====================================================
    // BALLISTICS - Point-Based Solving
    // =====================================================

    BallisticSolution SolveToPoint(Vector3 origin, Vector3 forward, Vector3 target)
    {
        float g = Physics.gravity.magnitude;
        Vector3 toTarget = target - origin;
        toTarget.y = 0f; // Horizontal distance

        float horizontalDist = toTarget.magnitude;
        float heightDiff = target.y - origin.y;

        // Use fixed muzzle velocity and solve for angle
        // Using the formula: tan(2θ) = (v² ± sqrt(v⁴ - g(gx² + 2yv²))) / (gx)
        // Simplified: we'll use a medium arc

        float v = muzzleVelocity;
        float v2 = v * v;
        float v4 = v2 * v2;
        
        float discriminant = v4 - g * (g * horizontalDist * horizontalDist + 2 * heightDiff * v2);

        if (discriminant < 0f)
        {
            // Can't reach - fire at max range
            return BuildSolution(origin, forward, defaultRange, GetDefaultApex());
        }

        // Use the lower angle (flatter trajectory) for more AC4-like feel
        float angle = Mathf.Atan2(v2 - Mathf.Sqrt(discriminant), g * horizontalDist);

        Vector3 direction = toTarget.normalized;
        
        float horizontalSpeed = v * Mathf.Cos(angle);
        float verticalSpeed = v * Mathf.Sin(angle);

        return new BallisticSolution
        {
            Origin = origin,
            Velocity = direction * horizontalSpeed + Vector3.up * verticalSpeed,
            Gravity = g
        };
    }

    BallisticSolution BuildSolution(Vector3 origin, Vector3 forward, float distance, float apex)
    {
        float g = Physics.gravity.magnitude;

        float tUp = Mathf.Sqrt(2f * apex / g);
        float totalTime = tUp * 2f;

        float horizontalSpeed = distance / totalTime;

        return new BallisticSolution
        {
            Origin = origin,
            Velocity = forward.normalized * horizontalSpeed + Vector3.up * Mathf.Sqrt(2f * g * apex),
            Gravity = g
        };
    }

    float GetDefaultApex()
    {
        return Mathf.Lerp(minApex, maxApex, 0.5f);
    }
}