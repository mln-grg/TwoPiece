using System;
using UnityEngine;

public enum FreeAimSubMode { SingleShot, FullyAutomatic }

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

    [Header("Free Aim Mode")]
    [Tooltip("Optional HUD component for the free-aim crosshair — auto-found on this GameObject if blank")]
    public FreeAimHUD freeAimHUD;

    [Header("Full Auto Settings")]
    [Tooltip("Fire interval at the START of holding LMB (slowest rate)")]
    public float autoFireIntervalMax = 0.65f;
    [Tooltip("Fire interval after fully spun-up (fastest rate)")]
    public float autoFireIntervalMin = 0.08f;
    [Tooltip("Seconds of continuous fire before reaching maximum fire rate")]
    public float autoAccelTime       = 3f;

    ShipController ship;
    CannonsController cannons;

    // Normal aim state
    Vector3 currentAimPoint;
    float aimPitch;
    bool wasAiming;

    // Free aim state
    bool           isFreeAiming;
    FreeAimSubMode freeAimSubMode = FreeAimSubMode.SingleShot;

    // Full auto state
    bool  autoFiring;
    float autoHoldTime;   // accumulates while LMB held — drives acceleration
    float autoFireTimer;  // counts up to the current fire interval


    GearState prevGear;

    void Awake()
    {
        ship    = GetComponent<ShipController>();
        cannons = GetComponent<CannonsController>();

        if (!shipCamera)
            shipCamera = FindObjectOfType<ShipCamera>();

        if (!freeAimHUD)
            freeAimHUD = GetComponent<FreeAimHUD>();
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
        // All movement input is suppressed during a dash
        if (ship.IsDashing) return;

        // Steering
        ship.steeringInput = Input.GetAxis("Horizontal");

        // Gear control
        if (Input.GetKeyDown(KeyCode.W)) ship.sailDelta = +1;
        if (Input.GetKeyDown(KeyCode.S)) ship.sailDelta = -1;

        // Dashes — forward / left / right
        if (Input.GetKeyDown(KeyCode.Space)) ship.TryDash(DashDirection.Forward);
        if (Input.GetKeyDown(KeyCode.Q))     ship.TryDash(DashDirection.Left);
        if (Input.GetKeyDown(KeyCode.E))     ship.TryDash(DashDirection.Right);

        // Trigger travel camera when reaching or leaving Gear 3
        GearState currentGear = ship.currentGear;
        if (currentGear != prevGear)
        {
            if (currentGear == GearState.Gear3)
                shipCamera.EnterTravelMode();
            else if (prevGear == GearState.Gear3)
                shipCamera.ExitTravelMode();

            prevGear = currentGear;
        }
    }

    void HandleAimingAndFiring()
    {
        // ── Locked during a dash ───────────────────────────────────────────────
        if (ship.IsDashing)
        {
            if (wasAiming)
            {
                if (shipCamera) shipCamera.ExitAimMode();
                cannons.HidePreview();
                wasAiming = false;
            }
            if (isFreeAiming) ExitFreeAimMode();
            return;
        }

        // ── Combat disabled at Gear 3 ──────────────────────────────────────────
        if (ship.currentGear == GearState.Gear3)
        {
            if (wasAiming)
            {
                if (shipCamera) shipCamera.ExitAimMode();
                cannons.HidePreview();
                wasAiming = false;
            }
            if (isFreeAiming) ExitFreeAimMode();
            return;
        }

        // ── FreeAimMode: Shift held ────────────────────────────────────────────
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (shiftHeld)
        {
            // Force-exit normal RMB aim mode so the two modes never overlap
            if (wasAiming)
            {
                if (shipCamera) shipCamera.ExitAimMode();
                cannons.HidePreview();
                wasAiming = false;
            }
            HandleFreeAimMode();
            return;
        }

        // Shift released — leave FreeAimMode
        if (isFreeAiming)
            ExitFreeAimMode();

        // ── Normal RMB aiming ─────────────────────────────────────────────────
        bool aimButton  = Input.GetMouseButton(1);       // RMB
        bool fireButton = Input.GetMouseButtonDown(0);   // LMB

        if (aimButton && !wasAiming)
        {
            if (shipCamera) shipCamera.EnterAimMode();
            wasAiming = true;
        }
        else if (!aimButton && wasAiming)
        {
            if (shipCamera) shipCamera.ExitAimMode();
            cannons.HidePreview();
            wasAiming = false;
        }

        if (aimButton)
        {
            UpdateAimPoint();
            ShowTrajectoryPreview();
        }

        if (fireButton && aimButton)
            FireCurrentSide();
    }

    // ── FreeAimMode ───────────────────────────────────────────────────────────

    void HandleFreeAimMode()
    {
        // First frame entering FreeAimMode
        if (!isFreeAiming)
        {
            isFreeAiming  = true;
            autoFiring    = false;
            autoHoldTime  = 0f;
            autoFireTimer = 0f;

            cannons.EnterFreeAim();

            if (shipCamera) shipCamera.EnterFreeAimMode();
            if (freeAimHUD) freeAimHUD.Show(freeAimSubMode);
        }

        // Toggle sub-mode with F (only while in FreeAimMode)
        if (Input.GetKeyDown(KeyCode.F))
        {
            freeAimSubMode = freeAimSubMode == FreeAimSubMode.SingleShot
                ? FreeAimSubMode.FullyAutomatic
                : FreeAimSubMode.SingleShot;

            if (freeAimHUD) freeAimHUD.UpdateSubMode(freeAimSubMode);

            // Cancel any in-progress auto fire when switching modes
            autoFiring    = false;
            autoHoldTime  = 0f;
            autoFireTimer = 0f;
        }

        // No arc preview — straight-line shots, the crosshair is the only aiming aid.
        // Make sure any leftover broadside arc from normal mode is hidden.
        cannons.HidePreview();

        // ── Input ─────────────────────────────────────────────────────────────
        bool lmbHeld = Input.GetMouseButton(0);
        bool lmbDown = Input.GetMouseButtonDown(0);

        if (freeAimSubMode == FreeAimSubMode.SingleShot)
        {
            autoFiring   = false;
            autoHoldTime = 0f;
            if (freeAimHUD) freeAimHUD.SetAutoProgress(0f, false);

            if (lmbDown)
                cannons.FireFreeAimCannons();
        }
        else // FullyAutomatic
        {
            if (lmbHeld)
            {
                if (!autoFiring)
                {
                    // Fire the very first shot the instant LMB goes down
                    autoFiring    = true;
                    autoHoldTime  = 0f;
                    autoFireTimer = 0f;
                    cannons.FireFreeAimCannons();
                }
                else
                {
                    autoHoldTime += Time.deltaTime;

                    // Acceleration: interpolate from slow to fast over autoAccelTime seconds
                    float accel           = Mathf.Clamp01(autoHoldTime / autoAccelTime);
                    float currentInterval = Mathf.Lerp(autoFireIntervalMax, autoFireIntervalMin, accel);

                    if (freeAimHUD) freeAimHUD.SetAutoProgress(accel, true);

                    autoFireTimer += Time.deltaTime;
                    if (autoFireTimer >= currentInterval)
                    {
                        autoFireTimer = 0f;
                        cannons.FireFreeAimCannons();
                    }
                }
            }
            else
            {
                // LMB released — stop and reset acceleration
                if (autoFiring && freeAimHUD) freeAimHUD.SetAutoProgress(0f, false);

                autoFiring    = false;
                autoHoldTime  = 0f;
                autoFireTimer = 0f;
            }
        }
    }

    void ExitFreeAimMode()
    {
        isFreeAiming  = false;
        autoFiring    = false;
        autoHoldTime  = 0f;
        autoFireTimer = 0f;

        cannons.ExitFreeAim();

        if (shipCamera) shipCamera.ExitFreeAimMode();
        cannons.HidePreview();
        if (freeAimHUD) freeAimHUD.Hide();
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

        // Get cannon origin based on current side
        Transform cannonOrigin = GetCannonOrigin(currentSide);

        if (!cannonOrigin)
        {
            Debug.LogWarning($"Cannon origin not set for {currentSide}!");
            return;
        }

        // Get direction based on side
        Vector3 sideDir = GetSideDirection(currentSide);
        
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
            case ShipCamera.AimSide.Front:
                cannons.PreviewFrontToPoint(currentAimPoint);
                break;
            case ShipCamera.AimSide.Back:
                cannons.PreviewBackToPoint(currentAimPoint);
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
            case ShipCamera.AimSide.Front:
                cannons.FireFrontAtPoint(currentAimPoint);
                break;
            case ShipCamera.AimSide.Back:
                cannons.FireBackAtPoint(currentAimPoint);
                break;
        }
    }

    Transform GetCannonOrigin(ShipCamera.AimSide side)
    {
        switch (side)
        {
            case ShipCamera.AimSide.Left:
                return cannons.leftCannonOrigin;
            case ShipCamera.AimSide.Right:
                return cannons.rightCannonOrigin;
            case ShipCamera.AimSide.Front:
                return cannons.frontCannonOrigin;
            case ShipCamera.AimSide.Back:
                return cannons.backCannonOrigin;
            default:
                return null;
        }
    }

    Vector3 GetSideDirection(ShipCamera.AimSide side)
    {
        switch (side)
        {
            case ShipCamera.AimSide.Left:
                return -transform.right;
            case ShipCamera.AimSide.Right:
                return transform.right;
            case ShipCamera.AimSide.Front:
                return transform.forward;
            case ShipCamera.AimSide.Back:
                return -transform.forward;
            default:
                return transform.forward;
        }
    }

    // Debug visualization
    void OnDrawGizmos()
    {
        if (!shipCamera || !shipCamera.IsAiming) return;

        ShipCamera.AimSide currentSide = shipCamera.CurrentAimSide;
        if (currentSide == ShipCamera.AimSide.None) return;

        Transform cannonOrigin = GetCannonOrigin(currentSide);

        if (!cannonOrigin) return;

        // Draw aim ray
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(cannonOrigin.position, currentAimPoint);
        Gizmos.DrawSphere(currentAimPoint, 0.5f);
    }
}