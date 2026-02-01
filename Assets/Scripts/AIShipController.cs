using UnityEngine;

[RequireComponent(typeof(ShipController))]
[RequireComponent(typeof(CannonsController))]
public class AIShipController : MonoBehaviour
{
    [Header("Target")]
    public Transform player;

    [Header("Behavior")]
    public float desiredCombatDistance = 40f;
    public float distanceTolerance = 8f;
    public float broadsideAngleTolerance = 12f;
    public float fireCooldown = 4f;

    ShipController ship;
    CannonsController cannons;

    float fireTimer;

    void Awake()
    {
        ship = GetComponent<ShipController>();
        cannons = GetComponent<CannonsController>();
    }

    void Update()
    {
        if (!player)
            return;

        fireTimer -= Time.deltaTime;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        float distance = toPlayer.magnitude;
        float signedAngle =
            Vector3.SignedAngle(transform.forward, toPlayer, Vector3.up);

        // ---------------- SAIL LOGIC ----------------
        if (ship.currentSail == SailState.NoSail)
            ship.sailDelta = +1;

        if (distance > desiredCombatDistance + distanceTolerance)
            ship.sailDelta = +1; // speed up
        else if (distance < desiredCombatDistance - distanceTolerance)
            ship.sailDelta = -1; // slow down

        // ---------------- STEERING ----------------
        bool inBroadsideRange =
            Mathf.Abs(distance - desiredCombatDistance) < distanceTolerance;

        if (inBroadsideRange)
        {
            // Turn to expose a side
            ship.steeringInput = Mathf.Sign(signedAngle);
        }
        else
        {
            // Chase player
            ship.steeringInput =
                Mathf.Clamp(signedAngle / 45f, -1f, 1f);
        }

        // ---------------- FIRING ----------------
        TryFire(toPlayer);
    }

    void TryFire(Vector3 toPlayer)
    {
        if (fireTimer > 0f)
            return;

        float leftAngle =
            Vector3.Angle(-transform.right, toPlayer);

        float rightAngle =
            Vector3.Angle(transform.right, toPlayer);

        if (leftAngle < broadsideAngleTolerance)
        {
            //cannons.FireLeftBroadsideAI();
            fireTimer = fireCooldown;
        }
        else if (rightAngle < broadsideAngleTolerance)
        {
            //cannons.FireRightBroadsideAI();
            fireTimer = fireCooldown;
        }
    }
}