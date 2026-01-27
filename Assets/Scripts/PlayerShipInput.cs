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
    float previewHeight = 8f;

    void Update()
    {
        ship.steeringInput = Input.GetAxis("Horizontal");

        if (Input.GetKeyDown(KeyCode.W)) ship.sailDelta = +1;
        if (Input.GetKeyDown(KeyCode.S)) ship.sailDelta = -1;

        // --- AIMING ---
        bool leftAim  = Input.GetMouseButton(0);
        bool rightAim = Input.GetMouseButton(1);

        if (leftAim)
        {
            previewHeight += Input.GetAxis("Mouse Y") * 10f * Time.deltaTime;
            previewHeight = Mathf.Clamp(previewHeight, 2f, 15f);
            cannons.PreviewLeft(previewHeight);
        }
        else if (rightAim)
        {
            previewHeight += Input.GetAxis("Mouse Y") * 10f * Time.deltaTime;
            previewHeight = Mathf.Clamp(previewHeight, 2f, 15f);
            cannons.PreviewRight(previewHeight);
        }
        else
        {
            cannons.HidePreview();
        }

        // --- FIRE ON RELEASE ---
        if (Input.GetMouseButtonUp(0))
            cannons.FireLeftBroadside();

        if (Input.GetMouseButtonUp(1))
            cannons.FireRightBroadside();
    }
}