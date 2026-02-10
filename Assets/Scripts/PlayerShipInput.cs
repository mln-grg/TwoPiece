using System;
using UnityEngine;

[RequireComponent(typeof(ShipController))]
[RequireComponent(typeof(CannonsController))]
public class PlayerShipInput : MonoBehaviour
{
    [Header("References")]
    public ShipCamera shipCamera;

    [Header("Aiming")]
    public LayerMask aimLayers;
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
    float aimPitch;
    bool wasAiming;

    void Awake()
    {
        ship = GetComponent<ShipController>();
        cannons = GetComponent<CannonsController>();

        if (!shipCamera)
            shipCamera = FindObjectOfType<ShipCamera>();
    }

    private void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        HandleMovementInput();
        HandleAimingAndFiring();
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

    void HandleAimingAndFiring()
    {
        bool aimButton = Input.GetMouseButton(1); // RMB to aim
        bool fireButton = Input.GetMouseButtonDown(0); // LMB to fire

        // Enter/Exit aim mode
        if (aimButton && !wasAiming)
        {
            if (shipCamera)
                shipCamera.EnterAimMode();
            wasAiming = true;
        }
        else if (!aimButton && wasAiming)
        {
            if (shipCamera)
                shipCamera.ExitAimMode();
            cannons.HidePreview();
            wasAiming = false;
        }

        // While aiming, update trajectory preview
        if (aimButton)
        {
            UpdateAimPoint();
            ShowTrajectoryPreview();
        }

        // Fire when LMB pressed while holding RMB
        if (fireButton && aimButton)
        {
            FireCurrentSide();
        }
    }

    void UpdateAimPoint()
    {
        if (!shipCamera) return;

        ShipCamera.AimSide currentSide = shipCamera.CurrentAimSide;

        if (currentSide == ShipCamera.AimSide.None)
            return;

        // Adjust aim pitch with mouse Y
        float yInput = Input.GetAxis("Mouse Y");
        if (!invertY)
            yInput = -yInput;

        aimPitch += yInput * cameraSensitivity * 0.5f;
        aimPitch = Mathf.Clamp(aimPitch, minAimAngle, maxAimAngle);

        Transform cannonOrigin = currentSide == ShipCamera.AimSide.Left
            ? cannons.leftCannonOrigin
            : cannons.rightCannonOrigin;

        if (!cannonOrigin)
        {
            Debug.LogWarning("Cannon origin not set!");
            return;
        }

        // Use aimPitch directly as the cannon launch angle.
        // Compute where the cannonball would land so the ballistic
        // solver (SolveToPoint) arrives back at the same angle.
        float angleRad = aimPitch * Mathf.Deg2Rad;
        float v = cannons.muzzleVelocity;
        float g = Physics.gravity.magnitude;

        float vx = v * Mathf.Cos(angleRad);
        float vy = v * Mathf.Sin(angleRad);

        // Time to hit water (y ~ 0):  originY + vy*t - 0.5*g*t² = 0
        float originY = cannonOrigin.position.y;
        float disc = vy * vy + 2f * g * originY;
        float t = (vy + Mathf.Sqrt(Mathf.Max(disc, 0f))) / g;

        float horizontalDist = vx * t;

        // Flatten side direction to horizontal
        Vector3 sideDir = currentSide == ShipCamera.AimSide.Left
            ? -transform.right
            : transform.right;
        Vector3 flatSide = new Vector3(sideDir.x, 0f, sideDir.z).normalized;

        currentAimPoint = cannonOrigin.position + flatSide * horizontalDist;
        currentAimPoint.y = 0f;
    }

    void ShowTrajectoryPreview()
    {
        if (!shipCamera) return;

        ShipCamera.AimSide currentSide = shipCamera.CurrentAimSide;

        switch (currentSide)
        {
            case ShipCamera.AimSide.Left:
                cannons.PreviewLeftToPoint(currentAimPoint);
                break;
            case ShipCamera.AimSide.Right:
                cannons.PreviewRightToPoint(currentAimPoint);
                break;
        }
    }

    void FireCurrentSide()
    {
        if (!shipCamera) return;

        ShipCamera.AimSide currentSide = shipCamera.CurrentAimSide;

        switch (currentSide)
        {
            case ShipCamera.AimSide.Left:
                cannons.FireLeftBroadsideAtPoint(currentAimPoint);
                break;
            case ShipCamera.AimSide.Right:
                cannons.FireRightBroadsideAtPoint(currentAimPoint);
                break;
        }
    }

    // Debug visualization
    void OnDrawGizmos()
    {
        if (!shipCamera || !shipCamera.IsAiming) return;

        ShipCamera.AimSide currentSide = shipCamera.CurrentAimSide;
        if (currentSide == ShipCamera.AimSide.None) return;

        Transform cannonOrigin = currentSide == ShipCamera.AimSide.Left 
            ? cannons.leftCannonOrigin 
            : cannons.rightCannonOrigin;

        if (!cannonOrigin) return;

        // Draw aim ray
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(cannonOrigin.position, currentAimPoint);
        Gizmos.DrawSphere(currentAimPoint, 0.5f);
    }
}