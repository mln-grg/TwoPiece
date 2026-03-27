using System;
using UnityEngine;

public enum FreeAimSubMode { SingleShot, FullyAutomatic }

/// <summary>
/// Player input handler for AC7-style sky-ship flight controls.
///
/// CONTROLLER (Xbox / XInput layout):
///   Left  Stick X  → Roll / Bank  (turns the ship via bank-driven yaw)
///   Left  Stick Y  → Pitch        (stick DOWN = nose UP — standard flight)
///   Right Trigger  → Thruster     (analog 0-1: idle coast → full thrust)
///   Left  Trigger  → Air Brake    (counteracts thrust for quick deceleration)
///   Right Stick    → Camera look
///   LB   (btn 4)   → Free Aim     (hold to strafe-fire with front cannons)
///   RB   (btn 5)   → Forward dash
///   L-Stick btn    → Left dash
///   R-Stick btn    → Right dash
///   A    (btn 0)   → Fire (while aiming with RMB)
///
/// KEYBOARD / MOUSE:
///   A / D          → Roll / Bank
///   W / S          → Pitch  (W = nose up, S = nose down)
///   Left Shift     → Thruster  (on = full thrust, off = idle coast)
///   Left Ctrl      → Air Brake
///   Mouse RMB      → Enter aim mode
///   Mouse LMB      → Fire (while aiming)
///   Tab  (hold)    → Free Aim mode
///   F              → Toggle Single / Full-Auto in Free Aim
///   Space          → Forward dash
///   Q              → Left dash
///   E              → Right dash
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

    [Header("Camera Control")]
    public float cameraSensitivity = 2f;
    public bool  invertY = false;

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

    // ── Free aim state ────────────────────────────────────────────────────────
    bool           isFreeAiming;
    FreeAimSubMode freeAimSubMode = FreeAimSubMode.SingleShot;

    // ── Full auto state ───────────────────────────────────────────────────────
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
    // Reads thruster (RT / Shift), pitch (Left Stick Y / W-S),
    // roll/bank (Left Stick X / A-D), air brake (LT / Ctrl), and dashes.
    // =========================================================================

    void HandleFlightInput()
    {
        // All movement suppressed during a dash burst
        if (ship.IsDashing) return;

        // ── Thruster — Right Trigger (analog 0-1) / Left Shift (keyboard) ────
        // "RightTrigger" axis is defined twice in InputManager:
        //   • type 0 (button): positiveButton = left shift    → 0 or 1
        //   • type 2 (joystick): axis 10 (Xbox RT on Windows) → 0..1
        // Unity returns the larger absolute value of the two entries.
        float rt = Mathf.Clamp01(Input.GetAxis("RightTrigger"));

        // ── Air Brake — Left Trigger / Left Ctrl ─────────────────────────────
        float lt = Mathf.Clamp01(Input.GetAxis("LeftTrigger"));

        // LT actively counteracts the throttle — gives precise speed control
        ship.thrusterInput = Mathf.Clamp01(rt - lt);

        // ── Pitch — Left Stick Y (inverted) / W-S ────────────────────────────
        // "Vertical" axis: W key → +1 (nose up), S → -1 (nose down)
        //                  Left stick DOWN → +1 (inverted in InputManager)
        // +1 = pitch up is the sign convention in ShipController.
        ship.pitchInput = Input.GetAxis("Vertical");

        // ── Roll / Bank → Yaw — Left Stick X / A-D ───────────────────────────
        ship.steeringInput = Input.GetAxis("Horizontal");

        // ── Dashes ────────────────────────────────────────────────────────────
        // Keyboard
        if (Input.GetKeyDown(KeyCode.Space)) ship.TryDash(DashDirection.Forward);
        if (Input.GetKeyDown(KeyCode.Q))     ship.TryDash(DashDirection.Left);
        if (Input.GetKeyDown(KeyCode.E))     ship.TryDash(DashDirection.Right);

        // Controller — RB = forward, L-Stick click = left, R-Stick click = right
        if (Input.GetKeyDown(KeyCode.JoystickButton5)) ship.TryDash(DashDirection.Forward);
        if (Input.GetKeyDown(KeyCode.JoystickButton8)) ship.TryDash(DashDirection.Left);
        if (Input.GetKeyDown(KeyCode.JoystickButton9)) ship.TryDash(DashDirection.Right);
    }

    // =========================================================================
    // AIMING AND FIRING
    // =========================================================================

    void HandleAimingAndFiring()
    {
        // Locked during a dash burst
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

        // ── Free Aim Mode — Tab (keyboard) / LB joystick button 4 ─────────────
        bool freeAimHeld = Input.GetKey(KeyCode.Tab)
                        || Input.GetKey(KeyCode.JoystickButton4);

        if (freeAimHeld)
        {
            // Suppress normal aim mode while free aiming
            if (wasAiming)
            {
                if (shipCamera) shipCamera.ExitAimMode();
                cannons.HidePreview();
                wasAiming = false;
            }
            HandleFreeAimMode();
            return;
        }

        // Tab / LB released — exit free aim
        if (isFreeAiming)
            ExitFreeAimMode();

        // ── Normal Broadside Aim — RMB ─────────────────────────────────────────
        bool aimButton  = Input.GetMouseButton(1);
        bool fireButton = Input.GetMouseButtonDown(0);

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

    // ── Free Aim Mode ─────────────────────────────────────────────────────────

    void HandleFreeAimMode()
    {
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

        // Toggle sub-mode with F
        if (Input.GetKeyDown(KeyCode.F))
        {
            freeAimSubMode = freeAimSubMode == FreeAimSubMode.SingleShot
                ? FreeAimSubMode.FullyAutomatic
                : FreeAimSubMode.SingleShot;

            if (freeAimHUD) freeAimHUD.UpdateSubMode(freeAimSubMode);

            autoFiring    = false;
            autoHoldTime  = 0f;
            autoFireTimer = 0f;
        }

        cannons.HidePreview();

        // Fire input: LMB (mouse) or joystick button 0 (A / Cross)
        bool lmbHeld = Input.GetMouseButton(0) || Input.GetKey(KeyCode.JoystickButton0);
        bool lmbDown = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.JoystickButton0);

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
                    autoFiring    = true;
                    autoHoldTime  = 0f;
                    autoFireTimer = 0f;
                    cannons.FireFreeAimCannons();
                }
                else
                {
                    autoHoldTime += Time.deltaTime;
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

    // ── Aim Point Calculation ─────────────────────────────────────────────────

    void UpdateAimPoint()
    {
        if (!shipCamera) return;

        ShipCamera.AimSide currentSide = shipCamera.CurrentAimSide;
        if (currentSide == ShipCamera.AimSide.None) return;

        float yInput = Input.GetAxis("Mouse Y");
        if (!invertY) yInput = -yInput;

        aimPitch += yInput * cameraSensitivity * 0.5f;
        aimPitch  = Mathf.Clamp(aimPitch, minAimAngle, maxAimAngle);

        Transform cannonOrigin = GetCannonOrigin(currentSide);
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

        ShipCamera.AimSide currentSide = shipCamera.CurrentAimSide;

        switch (currentSide)
        {
            case ShipCamera.AimSide.Left:  cannons.FireLeftBroadsideAtPoint(currentAimPoint);  break;
            case ShipCamera.AimSide.Right: cannons.FireRightBroadsideAtPoint(currentAimPoint); break;
            case ShipCamera.AimSide.Front: cannons.FireFrontAtPoint(currentAimPoint);          break;
            case ShipCamera.AimSide.Back:  cannons.FireBackAtPoint(currentAimPoint);           break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    Transform GetCannonOrigin(ShipCamera.AimSide side)
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
        if (currentSide == ShipCamera.AimSide.None) return;

        Transform cannonOrigin = GetCannonOrigin(currentSide);
        if (!cannonOrigin) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(cannonOrigin.position, currentAimPoint);
        Gizmos.DrawSphere(currentAimPoint, 0.5f);
    }
}
