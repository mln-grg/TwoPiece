using System;
using UnityEngine;

public enum FreeAimSubMode { SingleShot, FullyAutomatic }

/// <summary>
/// Player input handler for AC7-style sky-ship flight controls.
///
/// CONTROLLER (Xbox / XInput / PS layout):
///   Left  Stick X      → Roll / Bank  (turns the ship via bank-driven yaw)
///   Left  Stick Y      → Pitch        (stick DOWN = nose UP — standard flight)
///   Right Trigger (RT) → Thruster     (analog 0-1)
///   Right Stick        → Camera look
///   LT  (aim button)   → Enter aim mode (broadside arc or front crosshair)
///   Square / X btn (2) → Fire / Quick-fire
///     • LT + Square         = aimed fire (arc for sides; full-auto forward for front)
///     • Square alone        = quick fire at nearest side (no aim required)
///     • Hold Square (front) = full-auto forward
///   X   (btn 0, hold)   → Brake to full stop / hold to dock
///   Circle (btn 1)     → Dash — direction from left stick (left/right/forward)
///   LB  (btn 4, hold)  → Free Aim mode (strafing forward cannons)
///
/// KEYBOARD / MOUSE:
///   A / D          → Roll / Bank
///   W / S          → Pitch  (W = nose up, S = nose down)
///   Left Shift     → Thruster
///   E  (hold)      → Brake / dock
///   Mouse RMB      → Enter aim mode (same as LT)
///   Mouse LMB      → Fire / Quick-fire  (same as Square)
///   Tab  (hold)    → Free Aim mode
///   F              → Toggle Single / Full-Auto in Free Aim
///   Space          → Forward dash
///   Q              → Left dash
///   R              → Right dash
/// </summary>
[RequireComponent(typeof(ShipController))]
[RequireComponent(typeof(CannonsController))]
public class PlayerShipInput : MonoBehaviour
{
    [Header("References")]
    public ShipCamera shipCamera;

    [Header("Aiming")]
    public LayerMask aimLayers;
    public float maxAimDistance = 100f;

    [Tooltip("How far up/down the broadside cannons can aim")]
    public float minAimAngle = -15f;
    public float maxAimAngle =  45f;

    [Header("Quick Fire")]
    [Tooltip("Range used when firing a broadside without aiming (Square / LMB with no aim held)")]
    public float quickFireRange = 25f;

    [Header("Camera Control")]
    public float cameraSensitivity = 2f;
    public bool  invertY = false;

    [Header("Controller Aim Sensitivity")]
    [Tooltip("Degrees per second the right stick Y adjusts broadside arc elevation")]
    public float rightStickElevationSensitivity = 60f;
    // Forward-aim right-stick sensitivity is shared with normal free-look —
    // adjust ShipCamera.controllerSensitivity to tune it.

    [Header("Free Aim Mode")]
    [Tooltip("Optional HUD component for the free-aim crosshair — auto-found on this GameObject if blank")]
    public FreeAimHUD freeAimHUD;

    [Header("Full Auto Settings")]
    [Tooltip("Fire interval at the START of holding fire (slowest rate)")]
    public float autoFireIntervalMax = 0.65f;
    [Tooltip("Fire interval after fully spun-up (fastest rate)")]
    public float autoFireIntervalMin = 0.08f;
    [Tooltip("Seconds of continuous fire before reaching maximum fire rate")]
    public float autoAccelTime = 3f;

    // ── Private references ────────────────────────────────────────────────────
    ShipController    ship;
    CannonsController cannons;

    // ── Aim state ─────────────────────────────────────────────────────────────
    Vector3 currentAimPoint;
    float   aimPitch;
    bool    wasAiming;

    // ── Front-aim state (aim mode + front side = crosshair + full-auto) ───────
    bool isFrontAiming;

    // ── Free aim state ────────────────────────────────────────────────────────
    bool           isFreeAiming;
    FreeAimSubMode freeAimSubMode = FreeAimSubMode.SingleShot;

    // ── Shared full-auto state (reused across front-aim, free-aim, quick-fire) ─
    bool  autoFiring;
    float autoHoldTime;
    float autoFireTimer;

    // ── Awake / Start ─────────────────────────────────────────────────────────

    void Awake()
    {
        ship    = GetComponent<ShipController>();
        cannons = GetComponent<CannonsController>();

        if (!shipCamera)
            shipCamera = FindObjectOfType<ShipCamera>();

        if (!freeAimHUD)
            freeAimHUD = GetComponent<FreeAimHUD>();
    }

    void Start()
    {
        Cursor.visible   = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        HandleFlightInput();
        HandleAimingAndFiring();
    }

    // =========================================================================
    // FLIGHT INPUT
    // =========================================================================

    void HandleFlightInput()
    {
        if (ship.IsDashing) return;

        // ── Thruster — Right Trigger (RT) / Left Shift ────────────────────────
        float rt = Mathf.Clamp01(Input.GetAxis("RightTrigger"));
        ship.thrusterInput = rt;

        // ── Pitch — Left Stick Y (inverted) / W-S ────────────────────────────
        ship.pitchInput = Input.GetAxis("Vertical");

        // ── Roll / Bank → Yaw — Left Stick X / A-D ───────────────────────────
        ship.steeringInput = Input.GetAxis("Horizontal");

        // ── Brake — X button (JoystickButton0) / E key ───────────────────────
        // Brings the ship to a full stop while held; releases back to normal speed.
        bool brakeHeld = Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.E);
        ship.brakeInput = brakeHeld ? 1f : 0f;

        // ── Dash — Circle (JoystickButton1) / keyboard ───────────────────────
        // Controller: Circle + left stick direction = contextual dash.
        if (Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            // Read stick that was already set this frame
            float side = ship.steeringInput;
            if      (side < -0.5f) ship.TryDash(DashDirection.Left);
            else if (side >  0.5f) ship.TryDash(DashDirection.Right);
            else                   ship.TryDash(DashDirection.Forward);
        }

        // Keyboard dashes (E is now brake, so right-dash moved to R)
        if (Input.GetKeyDown(KeyCode.Space)) ship.TryDash(DashDirection.Forward);
        if (Input.GetKeyDown(KeyCode.Q))     ship.TryDash(DashDirection.Left);
        if (Input.GetKeyDown(KeyCode.R))     ship.TryDash(DashDirection.Right);
    }

    // =========================================================================
    // AIMING AND FIRING
    // =========================================================================

    void HandleAimingAndFiring()
    {
        if (ship.IsDashing)
        {
            ExitAllCombatModes();
            return;
        }

        // Consolidated fire input from mouse + controller Square/X button
        bool fireDown = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.JoystickButton2);
        bool fireHeld = Input.GetMouseButton(0)     || Input.GetKey(KeyCode.JoystickButton2);

        // ── Free Aim Mode — Tab / LB (hold) ──────────────────────────────────
        bool freeAimHeld = Input.GetKey(KeyCode.Tab) || Input.GetKey(KeyCode.JoystickButton4);

        if (freeAimHeld)
        {
            if (wasAiming) ExitAimMode();
            HandleFreeAimMode(fireDown, fireHeld);
            return;
        }
        if (isFreeAiming) ExitFreeAimMode();

        // ── Aimed Mode — RMB / LT ─────────────────────────────────────────────
        // LT axis > 0.5 acts as the aim button on controller.
        bool aimHeld = Input.GetMouseButton(1)
                    || Input.GetAxis("LeftTrigger") > 0.5f;

        if (aimHeld && !wasAiming)
        {
            if (shipCamera) shipCamera.EnterAimMode();
            wasAiming = true;
        }
        else if (!aimHeld && wasAiming)
        {
            ExitAimMode();
        }

        if (wasAiming)
        {
            // isFrontAiming check FIRST: once the camera has been swapped to
            // free-aim mode, CurrentAimSide returns None, so we must not use
            // it to drive front-aim decisions after the first frame.
            if (isFrontAiming)
            {
                HandleFrontAimMode(fireDown, fireHeld);
            }
            else
            {
                ShipCamera.AimSide side = shipCamera ? shipCamera.CurrentAimSide : ShipCamera.AimSide.None;

                if (side == ShipCamera.AimSide.Front)
                {
                    // First time we see Front — let HandleFrontAimMode initialise
                    HandleFrontAimMode(fireDown, fireHeld);
                }
                else
                {
                    if (side != ShipCamera.AimSide.None)
                    {
                        UpdateAimPoint();
                        ShowTrajectoryPreview();
                        if (fireDown) FireCurrentSide();
                    }
                }
            }
            return;
        }

        // ── Quick Fire — fire button pressed with NO aim held ─────────────────
        if (isFrontAiming) ExitFrontAimMode();
        HandleQuickFire(fireDown, fireHeld);
    }

    // ── Front Aim Mode ────────────────────────────────────────────────────────
    // LT held, camera facing forward.
    // Camera switches to free-aim so the RIGHT STICK moves the crosshair.
    // Free-aim cannons track the camera direction, giving full 3-D forward aiming.

    void HandleFrontAimMode(bool fireDown, bool fireHeld)
    {
        if (!isFrontAiming)
        {
            isFrontAiming = true;
            ResetAutoFire();

            // Leave broadside-snap aim; enter free-aim so the right stick
            // drives the camera (and therefore the forward cannon direction).
            if (shipCamera)
            {
                shipCamera.ExitAimMode();
                shipCamera.EnterFreeAimMode();
            }

            cannons.EnterFreeAim();
            cannons.HidePreview();
            if (freeAimHUD) freeAimHUD.Show(FreeAimSubMode.FullyAutomatic);
        }

        cannons.HidePreview(); // no arc while in front-aim

        if (fireHeld)
        {
            if (!autoFiring)
            {
                autoFiring = true;
                ResetAutoFire();
                cannons.FireFreeAimCannons();
            }
            else
            {
                autoHoldTime += Time.deltaTime;
                float accel    = Mathf.Clamp01(autoHoldTime / autoAccelTime);
                float interval = Mathf.Lerp(autoFireIntervalMax, autoFireIntervalMin, accel);
                if (freeAimHUD) freeAimHUD.SetAutoProgress(accel, true);
                autoFireTimer += Time.deltaTime;
                if (autoFireTimer >= interval) { autoFireTimer = 0f; cannons.FireFreeAimCannons(); }
            }
        }
        else
        {
            if (autoFiring && freeAimHUD) freeAimHUD.SetAutoProgress(0f, false);
            autoFiring = false;
            ResetAutoFire();
        }
    }

    void ExitFrontAimMode()
    {
        if (!isFrontAiming) return;
        isFrontAiming = false;
        autoFiring    = false;
        ResetAutoFire();
        // Camera was switched to free-aim on enter — restore it now.
        if (shipCamera) shipCamera.ExitFreeAimMode();
        cannons.ExitFreeAim();
        if (freeAimHUD) freeAimHUD.Hide();
    }

    // ── Quick Fire ────────────────────────────────────────────────────────────
    // Square / LMB with no aim button held. Front = full-auto forward.
    // Side / Back = single-volley broadside at a default range.

    void HandleQuickFire(bool fireDown, bool fireHeld)
    {
        // Determine nearest side from camera yaw (works without entering aim mode)
        float camYaw = shipCamera ? shipCamera.CameraYaw : 0f;
        while (camYaw >  180f) camYaw -= 360f;
        while (camYaw < -180f) camYaw += 360f;

        ShipCamera.AimSide nearestSide;
        if      (camYaw >= -45f && camYaw <=  45f) nearestSide = ShipCamera.AimSide.Front;
        else if (camYaw >   45f && camYaw <= 135f) nearestSide = ShipCamera.AimSide.Right;
        else if (camYaw <  -45f && camYaw >= -135f) nearestSide = ShipCamera.AimSide.Left;
        else                                        nearestSide = ShipCamera.AimSide.Back;

        if (nearestSide == ShipCamera.AimSide.Front)
        {
            // Full-auto forward — same accelerating pattern used in front-aim mode
            if (fireHeld)
            {
                if (!autoFiring)
                {
                    autoFiring = true;
                    ResetAutoFire();
                    cannons.FireFreeAimCannons();
                }
                else
                {
                    autoHoldTime += Time.deltaTime;
                    float accel    = Mathf.Clamp01(autoHoldTime / autoAccelTime);
                    float interval = Mathf.Lerp(autoFireIntervalMax, autoFireIntervalMin, accel);
                    autoFireTimer += Time.deltaTime;
                    if (autoFireTimer >= interval) { autoFireTimer = 0f; cannons.FireFreeAimCannons(); }
                }
            }
            else
            {
                autoFiring = false;
                ResetAutoFire();
            }
        }
        else
        {
            // Broadside quick-fire: single volley at a preset distance, no aiming
            autoFiring = false;
            ResetAutoFire();

            if (fireDown)
            {
                Vector3 origin   = GetCannonOriginPos(nearestSide);
                Vector3 sideDir  = GetSideDirection(nearestSide);
                Vector3 flatSide = new Vector3(sideDir.x, 0f, sideDir.z).normalized;
                Vector3 point    = origin + flatSide * quickFireRange;
                point.y = 0f;
                FireSideAtPoint(nearestSide, point);
            }
        }
    }

    // ── Tab / LB Free Aim Mode ────────────────────────────────────────────────

    void HandleFreeAimMode(bool fireDown, bool fireHeld)
    {
        if (!isFreeAiming)
        {
            isFreeAiming = true;
            ResetAutoFire();
            cannons.EnterFreeAim();
            if (shipCamera) shipCamera.EnterFreeAimMode();
            if (freeAimHUD) freeAimHUD.Show(freeAimSubMode);
        }

        // Toggle sub-mode with F
        if (Input.GetKeyDown(KeyCode.F))
        {
            freeAimSubMode = freeAimSubMode == FreeAimSubMode.SingleShot
                ? FreeAimSubMode.FullyAutomatic
                : FreeAimSubMode.SingleShot;
            if (freeAimHUD) freeAimHUD.UpdateSubMode(freeAimSubMode);
            ResetAutoFire();
        }

        cannons.HidePreview();

        if (freeAimSubMode == FreeAimSubMode.SingleShot)
        {
            autoFiring = false;
            if (freeAimHUD) freeAimHUD.SetAutoProgress(0f, false);
            if (fireDown) cannons.FireFreeAimCannons();
        }
        else
        {
            if (fireHeld)
            {
                if (!autoFiring)
                {
                    autoFiring = true;
                    ResetAutoFire();
                    cannons.FireFreeAimCannons();
                }
                else
                {
                    autoHoldTime += Time.deltaTime;
                    float accel    = Mathf.Clamp01(autoHoldTime / autoAccelTime);
                    float interval = Mathf.Lerp(autoFireIntervalMax, autoFireIntervalMin, accel);
                    if (freeAimHUD) freeAimHUD.SetAutoProgress(accel, true);
                    autoFireTimer += Time.deltaTime;
                    if (autoFireTimer >= interval) { autoFireTimer = 0f; cannons.FireFreeAimCannons(); }
                }
            }
            else
            {
                if (autoFiring && freeAimHUD) freeAimHUD.SetAutoProgress(0f, false);
                autoFiring = false;
                ResetAutoFire();
            }
        }
    }

    void ExitFreeAimMode()
    {
        isFreeAiming = false;
        autoFiring   = false;
        ResetAutoFire();
        cannons.ExitFreeAim();
        if (shipCamera) shipCamera.ExitFreeAimMode();
        cannons.HidePreview();
        if (freeAimHUD) freeAimHUD.Hide();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void ExitAimMode()
    {
        if (shipCamera) shipCamera.ExitAimMode();
        cannons.HidePreview();
        ExitFrontAimMode();
        wasAiming = false;
    }

    void ExitAllCombatModes()
    {
        if (wasAiming)     ExitAimMode();
        if (isFreeAiming)  ExitFreeAimMode();
    }

    void ResetAutoFire()
    {
        autoHoldTime  = 0f;
        autoFireTimer = 0f;
    }

    // ── Aim Point Calculation (broadside arc) ─────────────────────────────────

    void UpdateAimPoint()
    {
        if (!shipCamera) return;

        ShipCamera.AimSide currentSide = shipCamera.CurrentAimSide;
        if (currentSide == ShipCamera.AimSide.None) return;

        // Mouse Y (delta-based) + right stick Y (axis, scaled by dt) both adjust elevation.
        // Right stick Y: positive = stick up = raise arc.  Use invertY to flip if needed.
        float mouseContrib = Input.GetAxis("Mouse Y") * cameraSensitivity * 0.5f;
        float stickContrib = Input.GetAxis("RightStickY") * rightStickElevationSensitivity * Time.deltaTime;

        float yInput = mouseContrib + stickContrib;
        if (!invertY) yInput = -yInput;

        aimPitch += yInput;
        aimPitch  = Mathf.Clamp(aimPitch, minAimAngle, maxAimAngle);

        Transform cannonOrigin = GetCannonOriginTransform(currentSide);
        if (!cannonOrigin)
        {
            Debug.LogWarning($"Cannon origin not set for {currentSide}!");
            return;
        }

        Vector3 sideDir = GetSideDirection(currentSide);

        float angleRad = aimPitch * Mathf.Deg2Rad;
        float v = cannons.muzzleVelocity;
        float g = Physics.gravity.magnitude;

        float vx = v * Mathf.Cos(angleRad);
        float vy = v * Mathf.Sin(angleRad);

        float originY = cannonOrigin.position.y;
        float disc    = vy * vy + 2f * g * originY;
        float t       = (vy + Mathf.Sqrt(Mathf.Max(disc, 0f))) / g;

        float horizontalDist = vx * t;
        Vector3 flatSide     = new Vector3(sideDir.x, 0f, sideDir.z).normalized;

        currentAimPoint   = cannonOrigin.position + flatSide * horizontalDist;
        currentAimPoint.y = 0f;
    }

    void ShowTrajectoryPreview()
    {
        if (!shipCamera) return;

        ShipCamera.AimSide currentSide = shipCamera.CurrentAimSide;

        switch (currentSide)
        {
            case ShipCamera.AimSide.Left:  cannons.PreviewLeftToPoint(currentAimPoint);  break;
            case ShipCamera.AimSide.Right: cannons.PreviewRightToPoint(currentAimPoint); break;
            case ShipCamera.AimSide.Front: cannons.PreviewFrontToPoint(currentAimPoint); break;
            case ShipCamera.AimSide.Back:  cannons.PreviewBackToPoint(currentAimPoint);  break;
        }
    }

    void FireCurrentSide()
    {
        if (!shipCamera) return;
        FireSideAtPoint(shipCamera.CurrentAimSide, currentAimPoint);
    }

    void FireSideAtPoint(ShipCamera.AimSide side, Vector3 point)
    {
        switch (side)
        {
            case ShipCamera.AimSide.Left:  cannons.FireLeftBroadsideAtPoint(point);  break;
            case ShipCamera.AimSide.Right: cannons.FireRightBroadsideAtPoint(point); break;
            case ShipCamera.AimSide.Front: cannons.FireFrontAtPoint(point);          break;
            case ShipCamera.AimSide.Back:  cannons.FireBackAtPoint(point);           break;
        }
    }

    Transform GetCannonOriginTransform(ShipCamera.AimSide side)
    {
        switch (side)
        {
            case ShipCamera.AimSide.Left:  return cannons.leftCannonOrigin;
            case ShipCamera.AimSide.Right: return cannons.rightCannonOrigin;
            case ShipCamera.AimSide.Front: return cannons.frontCannonOrigin;
            case ShipCamera.AimSide.Back:  return cannons.backCannonOrigin;
            default:                       return null;
        }
    }

    Vector3 GetCannonOriginPos(ShipCamera.AimSide side)
    {
        Transform t = GetCannonOriginTransform(side);
        return t ? t.position : transform.position;
    }

    Vector3 GetSideDirection(ShipCamera.AimSide side)
    {
        switch (side)
        {
            case ShipCamera.AimSide.Left:  return -transform.right;
            case ShipCamera.AimSide.Right: return  transform.right;
            case ShipCamera.AimSide.Front: return  transform.forward;
            case ShipCamera.AimSide.Back:  return -transform.forward;
            default:                       return  transform.forward;
        }
    }

    // ── Debug Gizmos ──────────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (!shipCamera || !shipCamera.IsAiming) return;

        ShipCamera.AimSide currentSide = shipCamera.CurrentAimSide;
        if (currentSide == ShipCamera.AimSide.None || currentSide == ShipCamera.AimSide.Front) return;

        Transform cannonOrigin = GetCannonOriginTransform(currentSide);
        if (!cannonOrigin) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(cannonOrigin.position, currentAimPoint);
        Gizmos.DrawSphere(currentAimPoint, 0.5f);
    }
}
