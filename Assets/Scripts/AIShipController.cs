using UnityEngine;

[RequireComponent(typeof(ShipController))]
[RequireComponent(typeof(CannonsController))]
public class AIShipController : MonoBehaviour
{
    [Header("Target")]
    public Transform player;

    [Header("Combat Behavior")]
    public float desiredCombatDistance = 35f;
    public float distanceTolerance = 10f;
    
    [Tooltip("Preferred angle to maintain (90 = perfect broadside)")]
    public float preferredBroadsideAngle = 85f;
    public float broadsideAngleTolerance = 20f;

    [Header("Firing")]
    public float fireCooldown = 3.5f;
    public float aimLeadAmount = 1.2f; // How much to lead the target
    
    [Tooltip("Min distance to start firing")]
    public float minFireDistance = 15f;
    
    [Tooltip("Max distance to fire")]
    public float maxFireDistance = 60f;

    [Header("Maneuvering")]
    public float repositionInterval = 8f; // How often to change tactics
    public float aggressiveness = 0.5f; // 0 = defensive, 1 = aggressive

    ShipController ship;
    CannonsController cannons;

    float fireTimer;
    float repositionTimer;
    bool preferLeftSide = true; // Which side to expose

    enum CombatState
    {
        Approach,      // Getting into range
        Broadside,     // Exposing side and firing
        Reposition,    // Turning to new angle
        Flee           // Retreating (if damaged)
    }

    CombatState currentState = CombatState.Approach;

    void Awake()
    {
        ship = GetComponent<ShipController>();
        cannons = GetComponent<CannonsController>();
        
        // Randomize starting side preference
        preferLeftSide = Random.value > 0.5f;
    }

    void Update()
    {
        if (!player)
            return;

        fireTimer -= Time.deltaTime;
        repositionTimer -= Time.deltaTime;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        float distance = toPlayer.magnitude;
        float signedAngle = Vector3.SignedAngle(transform.forward, toPlayer, Vector3.up);

        // Occasionally switch tactics
        if (repositionTimer <= 0f)
        {
            repositionTimer = repositionInterval + Random.Range(-2f, 2f);
            preferLeftSide = !preferLeftSide; // Switch sides
        }

        UpdateCombatState(distance, signedAngle);
        UpdateMovement(distance, signedAngle);
        TryFire(toPlayer, distance);
    }

    void UpdateCombatState(float distance, float signedAngle)
    {
        // State machine
        if (distance > maxFireDistance)
        {
            currentState = CombatState.Approach;
        }
        else if (distance < minFireDistance && ship.CurrentSpeed > ship.MaxSpeed * 0.5f)
        {
            currentState = CombatState.Reposition; // Too close, need space
        }
        else if (IsInBroadsidePosition(signedAngle))
        {
            currentState = CombatState.Broadside;
        }
        else
        {
            currentState = CombatState.Reposition;
        }
    }

    void UpdateMovement(float distance, float signedAngle)
    {
        // ============== SAIL CONTROL ==============
        switch (currentState)
        {
            case CombatState.Approach:
                // Speed up to close distance
                if (ship.currentSail != SailState.FullSail)
                    ship.sailDelta = +1;
                break;

            case CombatState.Broadside:
                // Match player speed for parallel engagement
                if (distance > desiredCombatDistance + distanceTolerance)
                    ship.sailDelta = +1;
                else if (distance < desiredCombatDistance - distanceTolerance)
                    ship.sailDelta = -1;
                else
                    ship.sailDelta = 0; // Hold position
                break;

            case CombatState.Reposition:
                // Medium speed for maneuvering
                if (ship.currentSail == SailState.NoSail)
                    ship.sailDelta = +1;
                else if (ship.currentSail == SailState.FullSail && distance < desiredCombatDistance)
                    ship.sailDelta = -1;
                break;

            case CombatState.Flee:
                // Full speed away
                if (ship.currentSail != SailState.FullSail)
                    ship.sailDelta = +1;
                break;
        }

        // ============== STEERING ==============
        float targetAngle = preferLeftSide ? preferredBroadsideAngle : -preferredBroadsideAngle;

        switch (currentState)
        {
            case CombatState.Approach:
                // Head toward player
                ship.steeringInput = Mathf.Clamp(signedAngle / 45f, -1f, 1f);
                break;

            case CombatState.Broadside:
                // Maintain broadside angle
                float angleDiff = signedAngle - targetAngle;
                ship.steeringInput = Mathf.Clamp(angleDiff / 30f, -1f, 1f);
                break;

            case CombatState.Reposition:
                // Turn to establish broadside
                if (Mathf.Abs(signedAngle - targetAngle) > broadsideAngleTolerance)
                {
                    float turnDir = (targetAngle - signedAngle) > 0f ? 1f : -1f;
                    ship.steeringInput = turnDir;
                }
                else
                {
                    ship.steeringInput = Mathf.Clamp((signedAngle - targetAngle) / 30f, -1f, 1f);
                }
                break;

            case CombatState.Flee:
                // Turn away from player
                ship.steeringInput = Mathf.Clamp(-signedAngle / 45f, -1f, 1f);
                break;
        }
    }

    void TryFire(Vector3 toPlayer, float distance)
    {
        if (fireTimer > 0f)
            return;

        if (distance < minFireDistance || distance > maxFireDistance)
            return;

        // Calculate lead for moving target
        Rigidbody playerRb = player.GetComponent<Rigidbody>();
        Vector3 targetPoint = player.position;

        if (playerRb)
        {
            float timeToTarget = distance / 30f; // Estimate based on shot speed
            targetPoint += playerRb.linearVelocity * timeToTarget * aimLeadAmount;
        }

        // Determine which side can fire
        float leftAngle = Vector3.Angle(-transform.right, toPlayer);
        float rightAngle = Vector3.Angle(transform.right, toPlayer);

        bool canFireLeft = leftAngle < broadsideAngleTolerance;
        bool canFireRight = rightAngle < broadsideAngleTolerance;

        if (canFireLeft && (preferLeftSide || !canFireRight))
        {
            cannons.FireLeftBroadsideAtPoint(targetPoint);
            fireTimer = fireCooldown + Random.Range(-0.5f, 0.5f);
        }
        else if (canFireRight)
        {
            cannons.FireRightBroadsideAtPoint(targetPoint);
            fireTimer = fireCooldown + Random.Range(-0.5f, 0.5f);
        }
    }

    bool IsInBroadsidePosition(float signedAngle)
    {
        float targetAngle = preferLeftSide ? preferredBroadsideAngle : -preferredBroadsideAngle;
        return Mathf.Abs(signedAngle - targetAngle) < broadsideAngleTolerance;
    }

    // Debug visualization
    void OnDrawGizmos()
    {
        if (!player) return;

        Gizmos.color = currentState == CombatState.Broadside ? Color.green : Color.yellow;
        Gizmos.DrawLine(transform.position, player.position);

        // Show preferred broadside direction
        Vector3 broadsideDir = preferLeftSide ? -transform.right : transform.right;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, broadsideDir * 10f);
    }
}
