using UnityEngine;

public class ShipCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform ship;

    [Header("Camera Position")]
    public Vector3 normalOffset = new Vector3(0f, 8f, -15f);
    
    [Header("Side Aiming Offsets")]
    public Vector3 leftAimOffset = new Vector3(-12f, 5f, -8f);
    public Vector3 rightAimOffset = new Vector3(12f, 5f, -8f);
    public Vector3 frontAimOffset = new Vector3(0f, 6f, 5f);
    public Vector3 backAimOffset = new Vector3(0f, 6f, -20f);

    [Header("Side Aiming Rotations (Pitch, Yaw, Roll)")]
    public Vector3 leftAimRotation = new Vector3(0f, -45f, 0f);
    public Vector3 rightAimRotation = new Vector3(0f, 45f, 0f);
    public Vector3 frontAimRotation = new Vector3(0f, 0f, 0f);
    public Vector3 backAimRotation = new Vector3(0f, 180f, 0f);
    
    [Tooltip("How smoothly camera follows ship")]
    public float positionSmoothing = 5f;
    
    [Tooltip("How smoothly camera rotates")]
    public float rotationSmoothing = 3f;

    [Header("Camera Control")]
    public float lookSensitivity = 2f;
    public bool invertY = false;
    
    [Tooltip("Max angle camera can look up")]
    public float maxVerticalAngle = 45f;
    
    [Tooltip("Max angle camera can look down")]
    public float minVerticalAngle = -30f;
    
    [Tooltip("Max angle camera can rotate left/right (ignored in aim mode)")]
    public float maxHorizontalAngle = 60f;
    
    [Tooltip("Allow camera to rotate 360° when not aiming")]
    public bool allow360Rotation = true;

    [Header("Aim Mode")]
    public bool isAiming;
    
    [Tooltip("Field of view when aiming")]
    public float aimFOV = 50f;
    
    [Tooltip("Field of view when normal")]
    public float normalFOV = 60f;
    
    [Tooltip("How fast FOV transitions")]
    public float fovSpeed = 8f;
    
    [Tooltip("How fast camera snaps to side when entering aim mode")]
    public float aimSnapSpeed = 10f;

    [Header("Dynamic Camera")]
    public bool dynamicCamera = true;
    
    [Tooltip("Extra offset based on ship speed")]
    public float speedOffsetMultiplier = 0.5f;

    [Header("Side Detection")]
    [Tooltip("Angle threshold to determine which side (used for left/right, front/back)")]
    public float sideThreshold = 15f;

    [Header("Docked View")]
    [Tooltip("How smoothly the camera moves to and from the dock camera point")]
    public float dockCameraSmoothing = 2f;

    [Header("Free Aim Mode")]
    [Tooltip("FOV when in free-aim mode")]
    public float freeAimFOV = 55f;

    [Tooltip("Max left/right yaw relative to ship forward (degrees) — prevents aiming backwards)")]
    public float freeAimMaxYaw   = 90f;

    [Tooltip("Minimum pitch (degrees, negative = down)")]
    public float freeAimMinPitch = -10f;

    [Tooltip("Maximum pitch (degrees, positive = up)")]
    public float freeAimMaxPitch =  45f;

    [Header("Travel Mode (Gear 3)")]
    [Tooltip("Enable to preview the travel camera in Play mode without being at Gear 3")]
    public bool previewTravelMode = false;

    [Tooltip("Camera position offset when at full gear — high and behind for overhead view")]
    public Vector3 travelOffset = new Vector3(0f, 28f, -18f);

    [Tooltip("Field of view during travel mode")]
    public float travelFOV = 52f;

    [Tooltip("Downward pitch angle the camera locks to during travel")]
    public float travelPitch = 55f;

    [Tooltip("Horizontal yaw offset relative to ship forward (0 = straight behind)")]
    public float travelYaw = 0f;

    [Tooltip("Seconds to complete the transition into travel mode (SmoothDamp smoothTime — higher = slower)")]
    public float travelTransitionTime = 1.0f;

    [Tooltip("How lazily the travel camera's yaw drifts to follow the ship heading (SmoothDamp time — higher = more lag, more Pirates! feel)")]
    public float travelYawLagTime = 1.2f;

    public enum AimSide { None, Left, Right, Front, Back }

    Camera cam;
    Vector3 currentVelocity;
    Vector2 cameraRotation;
    float currentRoll;
    AimSide currentAimSide = AimSide.None;
    AimSide targetAimSide = AimSide.None;

    bool isDocked;
    Transform dockedCameraTarget;

    bool isFreeAiming;

    bool isTraveling;
    bool isTransitioningToTravel;
    bool isTransitioningFromTravel;
    bool wasInTravelMode;
    float transitionTimer;

    // SmoothDamp velocity state for rotation
    float travelWorldYaw;           // world-space yaw the travel camera currently faces
    float travelWorldYawVelocity;   // SmoothDamp velocity for the lagged yaw
    float aimPitchVelocity;
    float aimYawVelocity;
    float rollVelocity;

    // Collision shake state
    [Header("Collision Shake")]
    [Tooltip("How far the camera displaces at full shake intensity.")]
    public float shakeMagnitude = 0.4f;

    [Tooltip("Seconds for the shake to fully fade out.")]
    public float shakeDuration  = 0.3f;

    float _shakeIntensity;  // 0–1, decays to zero each frame

    /// <summary>Trigger a camera shake. force 0–1 scales the magnitude.</summary>
    public void Shake(float force)
    {
        // Take the larger value so a harder hit mid-shake doesn't reduce it.
        _shakeIntensity = Mathf.Max(_shakeIntensity, Mathf.Clamp01(force));
    }

    bool IsInTravelMode => isTraveling || previewTravelMode;

    // Public getters
    public AimSide CurrentAimSide => currentAimSide;
    public bool IsAiming      => isAiming;
    public bool IsFreeAiming  => isFreeAiming;
    public bool IsCameraLocked => isAiming || isFreeAiming;

    /// <summary>Ship-relative horizontal camera yaw in degrees.
    /// Positive = looking right, negative = looking left.</summary>
    public float CameraYaw => cameraRotation.y;

    void Awake()
    {
        cam = GetComponent<Camera>();
        
        if (!cam)
            cam = Camera.main;
    }

    void LateUpdate()
    {
        if (!ship)
            return;

        // Detect travel mode edge (handles both EnterTravelMode() and previewTravelMode toggle)
        bool nowTraveling = IsInTravelMode;
        if (nowTraveling && !wasInTravelMode)
        {
            isTransitioningToTravel   = true;
            isTransitioningFromTravel = false;
            transitionTimer           = travelTransitionTime;
        }
        else if (!nowTraveling && wasInTravelMode)
        {
            isTransitioningToTravel   = false;
            isTransitioningFromTravel = true;
            transitionTimer           = travelTransitionTime;
        }
        wasInTravelMode = nowTraveling;

        // Count down and unlock when timer expires
        if (isTransitioningToTravel || isTransitioningFromTravel)
        {
            transitionTimer -= Time.deltaTime;
            if (transitionTimer <= 0f)
            {
                isTransitioningToTravel   = false;
                isTransitioningFromTravel = false;
            }
        }

        if (isDocked && dockedCameraTarget)
        {
            UpdateDockedCamera();
            UpdateFieldOfView();
            return;
        }

        UpdateCameraRotation();
        DetermineSideFromCamera();
        UpdateCameraPosition();
        UpdateFieldOfView();
    }

    void UpdateDockedCamera()
    {
        transform.position = Vector3.SmoothDamp(
            transform.position,
            dockedCameraTarget.position,
            ref currentVelocity,
            1f / dockCameraSmoothing
        );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            dockedCameraTarget.rotation,
            dockCameraSmoothing * Time.deltaTime
        );
    }

    public void EnterDockedView(Transform cameraTarget)
    {
        isDocked = true;
        dockedCameraTarget = cameraTarget;
        currentVelocity = Vector3.zero;
    }

    public void ExitDockedView()
    {
        isDocked = false;
        dockedCameraTarget = null;
        currentVelocity = Vector3.zero;
    }

    public void EnterTravelMode()
    {
        isTraveling = true;
        // Seed world yaw to ship heading so we start aligned; the lag will
        // let the camera fall behind naturally as soon as the ship turns.
        if (ship) travelWorldYaw = ship.eulerAngles.y;
        travelWorldYawVelocity = 0f;
    }

    public void ExitTravelMode()
    {
        isTraveling = false;
        // Sync back to ship-relative offset so normal camera has no sudden jump.
        // DeltaAngle gives the signed difference: how far camera yaw sits from
        // ship forward — which is exactly what cameraRotation.y means.
        if (ship) cameraRotation.y = Mathf.DeltaAngle(ship.eulerAngles.y, travelWorldYaw);
        cameraRotation.x = 0f;
    }

    void UpdateCameraRotation()
    {
        // ── Travel mode: world-space yaw that lazily drifts behind the ship ────
        // The camera does NOT lock to ship rotation — instead it tracks ship heading
        // with a configurable lag, producing the Sid Meier's Pirates! feel where
        // the ship swings ahead while the camera smoothly catches up.
        if (IsInTravelMode)
        {
            travelWorldYaw = Mathf.SmoothDampAngle(
                travelWorldYaw, ship.eulerAngles.y,
                ref travelWorldYawVelocity, travelYawLagTime);

            Quaternion targetRot = Quaternion.Euler(travelPitch, travelWorldYaw, 0f);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, rotationSmoothing * Time.deltaTime);
            return; // world-space rotation: skip ship-relative logic below
        }

        // ── Free Aim mode (ship-relative, clamped hemisphere) ───────────────────
        if (isFreeAiming)
        {
            float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;
            if (invertY) mouseY = -mouseY;

            cameraRotation.y += mouseX;
            cameraRotation.x += mouseY;

            // Clamp to the forward hemisphere — can't look backwards
            cameraRotation.y = Mathf.Clamp(cameraRotation.y, -freeAimMaxYaw,  freeAimMaxYaw);
            cameraRotation.x = Mathf.Clamp(cameraRotation.x,  freeAimMinPitch, freeAimMaxPitch);

            // No roll in free aim
            currentRoll = Mathf.SmoothDampAngle(currentRoll, 0f, ref rollVelocity, 1f / aimSnapSpeed);

            Quaternion targetRot = ship.rotation
                * Quaternion.Euler(-cameraRotation.x, cameraRotation.y, currentRoll);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, rotationSmoothing * Time.deltaTime);
            return;
        }

        // ── Normal / Aim mode (ship-relative) ───────────────────────────────────
        if (!isAiming)
        {
            float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;
            if (invertY) mouseY = -mouseY;

            cameraRotation.x += mouseY;
            cameraRotation.y += mouseX;

            cameraRotation.x = Mathf.Clamp(cameraRotation.x, minVerticalAngle, maxVerticalAngle);

            if (allow360Rotation)
            {
                if (cameraRotation.y >  180f) cameraRotation.y -= 360f;
                else if (cameraRotation.y < -180f) cameraRotation.y += 360f;
            }
            else
            {
                cameraRotation.y = Mathf.Clamp(cameraRotation.y, -maxHorizontalAngle, maxHorizontalAngle);
            }
        }
        else
        {
            // Aim mode: snap camera to the selected broadside
            if (targetAimSide != AimSide.None)
            {
                Vector3 aimRot     = GetAimRotation(targetAimSide);
                float aimSmoothTime = 1f / aimSnapSpeed;
                cameraRotation.y = Mathf.SmoothDampAngle(cameraRotation.y, aimRot.y, ref aimYawVelocity,   aimSmoothTime);
                cameraRotation.x = Mathf.SmoothDampAngle(cameraRotation.x, aimRot.x, ref aimPitchVelocity, aimSmoothTime);
            }
        }

        float targetRoll = 0f;
        if (isAiming && targetAimSide != AimSide.None)
            targetRoll = GetAimRotation(targetAimSide).z;

        currentRoll = Mathf.SmoothDampAngle(currentRoll, targetRoll, ref rollVelocity, 1f / aimSnapSpeed);

        Quaternion targetRotation = ship.rotation * Quaternion.Euler(-cameraRotation.x, cameraRotation.y, currentRoll);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothing * Time.deltaTime);
    }

    void DetermineSideFromCamera()
    {
        if (!isAiming || isFreeAiming)
        {
            currentAimSide = AimSide.None;
            return;
        }

        float a = cameraRotation.y;
        while (a >  180f) a -= 360f;
        while (a < -180f) a += 360f;

        // True hysteresis: boundaries sit at the 45°/135° midpoints between sides.
        // To enter a new side you must cross (midpoint + H); to leave you must come
        // back past (midpoint - H).  This makes rapid snapping impossible.
        float H    = sideThreshold;
        AimSide next = currentAimSide;

        switch (currentAimSide)
        {
            case AimSide.None:  // treated as Front for initial resolution
            case AimSide.Front:
                if      (a >  45f + H) next = AimSide.Right;
                else if (a < -45f - H) next = AimSide.Left;
                break;

            case AimSide.Right:
                if      (a <  45f - H)  next = AimSide.Front;
                else if (a > 135f + H)  next = AimSide.Back;
                break;

            case AimSide.Back:
                // Back spans both sides of ±180° so check sign to pick correct neighbour
                if      (a > 0f && a < 135f - H)  next = AimSide.Right;
                else if (a < 0f && a > -135f + H)  next = AimSide.Left;
                break;

            case AimSide.Left:
                if      (a > -45f + H)  next = AimSide.Front;
                else if (a < -135f - H) next = AimSide.Back;
                break;
        }

        if (next != currentAimSide)
        {
            currentAimSide = next;
            targetAimSide  = next;
        }
    }

    void UpdateCameraPosition()
    {
        Vector3 targetPos;
        float smoothTime;

        if (isAiming)
        {
            targetPos  = ship.TransformPoint(GetAimOffset(currentAimSide));
            smoothTime = 1f / (positionSmoothing * 1.5f);
        }
        else if (IsInTravelMode)
        {
            // Offset is applied in world space, rotated by the lagged travel yaw.
            // This means the camera sits behind-and-above from ITS own perspective,
            // not the ship's — so it keeps drifting even while the ship turns away.
            Vector3 worldOffset = Quaternion.Euler(0f, travelWorldYaw, 0f) * travelOffset;
            targetPos  = ship.position + worldOffset;
            smoothTime = (isTransitioningToTravel || isTransitioningFromTravel)
                         ? travelTransitionTime
                         : 1f / positionSmoothing;
        }
        else
        {
            Vector3 offset = normalOffset;
            if (dynamicCamera)
            {
                ShipController shipController = ship.GetComponent<ShipController>();
                if (shipController)
                {
                    float speedRatio = shipController.CurrentSpeed / shipController.MaxSpeed;
                    offset.z -= speedRatio * speedOffsetMultiplier;
                }
            }
            targetPos  = ship.TransformPoint(offset);
            smoothTime = (isTransitioningToTravel || isTransitioningFromTravel)
                         ? travelTransitionTime
                         : 1f / positionSmoothing;
        }

        transform.position = Vector3.SmoothDamp(
            transform.position, targetPos, ref currentVelocity, smoothTime);

        // Apply and decay collision shake offset
        if (_shakeIntensity > 0f)
        {
            transform.position += Random.insideUnitSphere * (shakeMagnitude * _shakeIntensity);
            _shakeIntensity     = Mathf.MoveTowards(_shakeIntensity, 0f, Time.deltaTime / shakeDuration);
        }
    }

    void UpdateFieldOfView()
    {
        if (!cam) return;

        float targetFOV = isAiming      ? aimFOV
                        : isFreeAiming  ? freeAimFOV
                        : IsInTravelMode ? travelFOV
                        : normalFOV;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, fovSpeed * Time.deltaTime);
    }

    public void EnterAimMode()
    {
        isAiming = true;
        
        // Determine which side to snap to based on current camera angle
        // Normalize angle to -180 to 180 range
        float normalizedAngle = cameraRotation.y;
        while (normalizedAngle > 180f) normalizedAngle -= 360f;
        while (normalizedAngle < -180f) normalizedAngle += 360f;
        
        // Snap to nearest cardinal direction
        if (normalizedAngle >= -45f && normalizedAngle <= 45f)
            targetAimSide = AimSide.Front;
        else if (normalizedAngle > 45f && normalizedAngle <= 135f)
            targetAimSide = AimSide.Right;
        else if (normalizedAngle < -45f && normalizedAngle >= -135f)
            targetAimSide = AimSide.Left;
        else
            targetAimSide = AimSide.Back;
        
        currentAimSide = targetAimSide;
    }

    public void ExitAimMode()
    {
        isAiming       = false;
        currentAimSide = AimSide.None;
        targetAimSide  = AimSide.None;
    }

    /// <summary>Enters FreeAimMode: camera follows mouse within a clamped hemisphere.</summary>
    public void EnterFreeAimMode()
    {
        isFreeAiming = true;

        // Clamp any existing rotation into the free-aim range immediately
        cameraRotation.y = Mathf.Clamp(cameraRotation.y, -freeAimMaxYaw,  freeAimMaxYaw);
        cameraRotation.x = Mathf.Clamp(cameraRotation.x,  freeAimMinPitch, freeAimMaxPitch);
    }

    /// <summary>Exits FreeAimMode and returns camera to normal free-look.</summary>
    public void ExitFreeAimMode()
    {
        isFreeAiming = false;
    }
    
    Vector3 GetAimOffset(AimSide side)
    {
        switch (side)
        {
            case AimSide.Left: return leftAimOffset;
            case AimSide.Right: return rightAimOffset;
            case AimSide.Front: return frontAimOffset;
            case AimSide.Back: return backAimOffset;
            default: return normalOffset;
        }
    }
    
    Vector3 GetAimRotation(AimSide side)
    {
        switch (side)
        {
            case AimSide.Left: return leftAimRotation;
            case AimSide.Right: return rightAimRotation;
            case AimSide.Front: return frontAimRotation;
            case AimSide.Back: return backAimRotation;
            default: return Vector3.zero;
        }
    }
}