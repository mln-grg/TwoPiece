// Floating Boat for HDRP Water System created by Seta
// https://www.youtube.com/@SetaLevelDesign
// Comments added by ChatGPT
// Licence: Creative Commons

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
    [Header("Water Settings")]
    public WaterSurface targetSurface; // Target water surface to float on
    public bool includeDeformers = true; // Include water deformers in height calculation
    public float verticalOffset = 0.0f; // Vertical offset from water surface

    [Header("Follow Settings")]
    public bool followWaterCurrent = false; // Should boat follow water current
    public float currentSpeedMultiplier = 1f; // Multiplier for current speed

    [Header("Boat Dimensions")]
    public float length = 2f; // Boat length
    public float width = 1f; // Boat width
    public bool showGizmos = true; // Toggle to show or hide gizmo visualization in the Scene view

    [Header("Motion Settings")]
    public float positionLerpSpeed = 5f; // Smooth speed for vertical motion
    public float rotationLerpSpeed = 2f; // Smooth speed for rotation

    [Header("Collision Settings")]
    public bool useRigidbodyForCollision = false; // Use Rigidbody to detect collisions
    public LayerMask obstacleLayers; // Layers considered as obstacles

    private bool disableCurrentFlow = false; // Disable current when colliding
    private Rigidbody rb; // Rigidbody reference
    private WaterSearchParameters searchParams = new WaterSearchParameters(); // Parameters for water queries
    private WaterSearchResult searchResult = new WaterSearchResult(); // Result of water queries
    private Vector3 smoothedPosition; // Smoothed position for floating motion
    private Quaternion smoothedRotation; // Smoothed rotation for floating motion

    void Awake()
    {
        rb = GetComponent<Rigidbody>(); // Get Rigidbody component
        if (rb == null && useRigidbodyForCollision) // Add Rigidbody if missing
            rb = gameObject.AddComponent<Rigidbody>();

        if (rb != null)
        {
            rb.useGravity = false; // Disable gravity
            rb.isKinematic = !useRigidbodyForCollision; // Set kinematic based on collision usage
        }

        smoothedPosition = transform.position; // Initialize smoothed position
        smoothedRotation = transform.rotation; // Initialize smoothed rotation
    }

    void LateUpdate()
    {
        if (targetSurface == null) return; // Exit if no water surface assigned

        // Sampling points around the boat
        Vector3 localBow = transform.forward * (length / 2f); // Front point
        Vector3 localStern = -transform.forward * (length / 2f); // Back point
        Vector3 localLeft = -transform.right * (width / 2f); // Left point
        Vector3 localRight = transform.right * (width / 2f); // Right point

        Vector3 worldBow = transform.position + localBow; // World position front
        Vector3 worldStern = transform.position + localStern; // World position back
        Vector3 worldLeft = transform.position + localLeft; // World position left
        Vector3 worldRight = transform.position + localRight; // World position right

        float hBow = GetWaterHeight(worldBow); // Water height at front
        float hStern = GetWaterHeight(worldStern); // Water height at back
        float hLeft = GetWaterHeight(worldLeft); // Water height at left
        float hRight = GetWaterHeight(worldRight); // Water height at right

        // Adjusted points with water height
        Vector3 adjustedBow = new Vector3(worldBow.x, hBow, worldBow.z); // Adjusted front
        Vector3 adjustedStern = new Vector3(worldStern.x, hStern, worldStern.z); // Adjusted back
        Vector3 adjustedLeft = new Vector3(worldLeft.x, hLeft, worldLeft.z); // Adjusted left
        Vector3 adjustedRight = new Vector3(worldRight.x, hRight, worldRight.z); // Adjusted right

        // Directions and water normal
        Vector3 forwardDir = (adjustedBow - adjustedStern).normalized; // Forward direction
        Vector3 rightDir = (adjustedRight - adjustedLeft).normalized; // Right direction
        Vector3 waterNormal = Vector3.Cross(forwardDir, rightDir).normalized; // Normal of water surface

        // Average height of the boat
        float avgHeight = (hBow + hStern + hLeft + hRight) / 4f; // Compute average water height
        Vector3 targetWavePos = new Vector3(transform.position.x, avgHeight + verticalOffset, transform.position.z); // Target wave position

        // Smooth position and rotation
        smoothedPosition.y = Mathf.Lerp(smoothedPosition.y, targetWavePos.y, positionLerpSpeed * Time.deltaTime); // Smooth vertical motion
        smoothedRotation = Quaternion.Slerp(smoothedRotation, Quaternion.FromToRotation(transform.up, waterNormal) * transform.rotation, rotationLerpSpeed * Time.deltaTime); // Smooth rotation

        // Final position for floating
        Vector3 finalPosition = new Vector3(transform.position.x, smoothedPosition.y, transform.position.z); // Resulting position

        // Apply water current
        if (followWaterCurrent && !disableCurrentFlow) // Check if current should affect
        {
            Vector3 currentDir = GetWaterCurrentDirection(transform.position); // Get water current direction
            finalPosition += currentDir * currentSpeedMultiplier * Time.deltaTime; // Move with current
        }

        // Update transform
        transform.position = finalPosition; // Apply position
        transform.rotation = smoothedRotation; // Apply rotation
    }

    private float GetWaterHeight(Vector3 worldPos)
    {
        searchParams.startPositionWS = (float3)worldPos; // Start position for search
        searchParams.targetPositionWS = (float3)worldPos; // Target position
        searchParams.includeDeformation = includeDeformers; // Include deformers
        searchParams.maxIterations = 8; // Max search iterations
        searchParams.error = 0.01f; // Allowed error
        searchParams.excludeSimulation = false; // Include simulation

        if (targetSurface.ProjectPointOnWaterSurface(searchParams, out searchResult)) // Project point onto water
            return searchResult.projectedPositionWS.y; // Return water height

        return worldPos.y; // Default to current y
    }

    private Vector3 GetWaterCurrentDirection(Vector3 worldPos)
    {
        searchParams.startPositionWS = (float3)worldPos; // Start position
        searchParams.targetPositionWS = (float3)worldPos; // Target position
        searchParams.includeDeformation = includeDeformers; // Include deformers
        searchParams.maxIterations = 4; // Max iterations
        searchParams.error = 0.01f; // Allowed error
        searchParams.excludeSimulation = false; // Include simulation

        if (targetSurface.ProjectPointOnWaterSurface(searchParams, out searchResult)) // Project point
            return ((Vector3)searchResult.currentDirectionWS).normalized; // Return normalized current

        return Vector3.zero; // Default to no current
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!useRigidbodyForCollision) return; // Ignore if not using Rigidbody

        if (((1 << collision.gameObject.layer) & obstacleLayers) != 0) // Check obstacle layer
        {
            disableCurrentFlow = true; // Stop current flow
            followWaterCurrent = false; // Stop following current
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (!useRigidbodyForCollision) return; // Ignore if not using Rigidbody

        if (((1 << collision.gameObject.layer) & obstacleLayers) != 0) // Check obstacle layer
            disableCurrentFlow = false; // Re-enable current flow
    }

    private void OnDrawGizmos() // Called by Unity to draw debug visuals (Gizmos) in the Scene view
    {
        if (!showGizmos) return;        // Exit early if gizmo visualization is disabled
        Gizmos.color = Color.cyan; // Set Gizmo color to cyan (light blue)

        // Define local positions for the boat’s key points (relative to its center)
        Vector3 localBow = transform.forward * (length / 2f);  // Local position of the bow (front)
        Vector3 localStern = -transform.forward * (length / 2f); // Local position of the stern (back)
        Vector3 localLeft = -transform.right * (width / 2f); // Local position of the left side
        Vector3 localRight = transform.right * (width / 2f); // Local position of the right side

        // Convert local positions to world space (actual positions in the scene)
        Vector3 bowPoint = transform.position + localBow;   // World position of the bow
        Vector3 sternPoint = transform.position + localStern; // World position of the stern
        Vector3 leftPoint = transform.position + localLeft;   // World position of the left side
        Vector3 rightPoint = transform.position + localRight; // World position of the right side

        // Draw small spheres at each of the measurement points for visual reference
        Gizmos.DrawSphere(bowPoint, 0.05f);   // Draw sphere at bow
        Gizmos.DrawSphere(sternPoint, 0.05f); // Draw sphere at stern
        Gizmos.DrawSphere(leftPoint, 0.05f);  // Draw sphere at left side
        Gizmos.DrawSphere(rightPoint, 0.05f); // Draw sphere at right side

        // Draw lines connecting the measurement points to visualize boat shape
        Gizmos.DrawLine(bowPoint, sternPoint); // Draw line showing boat length
        Gizmos.DrawLine(leftPoint, rightPoint); // Draw line showing boat width
    }
}

#if UNITY_EDITOR

[CustomEditor(typeof(FloatingBoat))]
public class FloatingBoatAdvancedEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); // Draw default inspector

        FloatingBoat obj = (FloatingBoat)target; // Get target object

        if (obj.useRigidbodyForCollision) // If Rigidbody is used
        {
            Collider[] colliders = obj.GetComponents<Collider>(); // Get all colliders

            bool hasValidCollider = colliders.Any(c =>
                c is BoxCollider || // Valid box collider
                c is SphereCollider || // Valid sphere collider
                c is CapsuleCollider || // Valid capsule collider
                (c is MeshCollider mc && mc.convex) // Valid convex mesh collider
            );

            if (!hasValidCollider) // Show warning if no collider
            {
                EditorGUILayout.HelpBox(
                    "This object requires a Basic Collider (Box,Sphere etc.) to use Rigidbody for collisions. Please add a Collider manually.",
                    MessageType.Warning
                );
            }
        }
    }
}
#endif
