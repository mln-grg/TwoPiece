using UnityEngine;

[RequireComponent(typeof(ShipController))]
[RequireComponent(typeof(CannonsController))]
public class PlayerShipInput : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public Transform aimReticle; // UI element or world space crosshair

    [Header("Aiming")]
    public LayerMask aimLayers; // What can be targeted (water, ships, etc.)
    public float maxAimDistance = 100f;
    
    [Tooltip("How far up/down you can aim")]
    public float minAimAngle = -15f;
    public float maxAimAngle = 45f;

    [Header("Camera Control")]
    public float cameraSensitivity = 2f;
    public bool invertY = false;

    ShipController ship;
    CannonsController cannons;

    // Aim state
    Vector3 currentAimPoint;
    bool isAimingLeft;
    bool isAimingRight;
    float aimPitch; // Vertical aim angle

    void Awake()
    {
        ship = GetComponent<ShipController>();
        cannons = GetComponent<CannonsController>();

        if (!playerCamera)
            playerCamera = Camera.main;
    }

    void Update()
    {
        HandleMovementInput();
        HandleAimingInput();
        HandleFiring();
    }

    void HandleMovementInput()
    {
        // Steering
        ship.steeringInput = Input.GetAxis("Horizontal");

        // Sail control
        if (Input.GetKeyDown(KeyCode.W)) ship.sailDelta = +1;
        if (Input.GetKeyDown(KeyCode.S)) ship.sailDelta = -1;

        // Dash
        if (Input.GetKeyDown(KeyCode.Space))
            ship.TryDash();
    }

    void HandleAimingInput()
    {
        bool leftAim = Input.GetMouseButton(0);
        bool rightAim = Input.GetMouseButton(1);

        isAimingLeft = leftAim;
        isAimingRight = rightAim;

        if (leftAim || rightAim)
        {
            // Adjust aim pitch with mouse Y
            float yInput = Input.GetAxis("Mouse Y");
            if (invertY) yInput = -yInput;

            aimPitch += yInput * cameraSensitivity;
            aimPitch = Mathf.Clamp(aimPitch, minAimAngle, maxAimAngle);

            // Calculate aim direction
            Vector3 sideDirection = leftAim ? -transform.right : transform.right;
            Vector3 aimDirection = Quaternion.AngleAxis(aimPitch, transform.forward) * sideDirection;

            // Raycast to find aim point
            if (Physics.Raycast(transform.position, aimDirection, out RaycastHit hit, maxAimDistance, aimLayers))
            {
                currentAimPoint = hit.point;
            }
            else
            {
                currentAimPoint = transform.position + aimDirection * maxAimDistance;
            }

            // Update reticle position
            if (aimReticle)
            {
                aimReticle.position = currentAimPoint;
                aimReticle.gameObject.SetActive(true);
            }

            // Show trajectory preview
            if (leftAim)
                cannons.PreviewLeftToPoint(currentAimPoint);
            else
                cannons.PreviewRightToPoint(currentAimPoint);
        }
        else
        {
            // Hide aiming UI
            cannons.HidePreview();
            
            if (aimReticle)
                aimReticle.gameObject.SetActive(false);

            // Reset pitch when not aiming
            aimPitch = Mathf.Lerp(aimPitch, 0f, 5f * Time.deltaTime);
        }
    }

    void HandleFiring()
    {
        // Fire on release (or you could change to fire on press)
        if (Input.GetMouseButtonUp(0))
        {
            cannons.FireLeftBroadsideAtPoint(currentAimPoint);
        }

        if (Input.GetMouseButtonUp(1))
        {
            cannons.FireRightBroadsideAtPoint(currentAimPoint);
        }

        // Alternative: Quick fire keys for non-aimed shots
        if (Input.GetKeyDown(KeyCode.Q))
        {
            cannons.FireLeftBroadside();
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            cannons.FireRightBroadside();
        }
    }
}
