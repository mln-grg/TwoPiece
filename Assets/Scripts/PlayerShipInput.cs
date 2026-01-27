using UnityEngine;

[RequireComponent(typeof(ShipController))]
[RequireComponent(typeof(CannonsController))]
public class PlayerShipInput : MonoBehaviour
{
    ShipController ship;
    CannonsController cannons;

    void Awake()
    {
        ship = GetComponent<ShipController>();
        cannons = GetComponent<CannonsController>();
    }

    void Update()
    {
        // ---------------- STEERING ----------------
        ship.steeringInput = Input.GetAxis("Horizontal");

        // ---------------- SAIL CONTROL ----------------
        if (Input.GetKeyDown(KeyCode.W))
            ship.sailDelta = +1;

        if (Input.GetKeyDown(KeyCode.S))
            ship.sailDelta = -1;

        // ---------------- CANNONS ----------------
        if (Input.GetMouseButtonDown(0))
            cannons.FireLeftBroadside();

        if (Input.GetMouseButtonDown(1))
            cannons.FireRightBroadside();
    }
}