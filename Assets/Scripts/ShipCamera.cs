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

    public enum AimSide { None, Left, Right, Front, Back }

    Camera cam;
    Vector3 currentVelocity;
    Vector2 cameraRotation;
    float currentRoll;
    AimSide currentAimSide = AimSide.None;
    AimSide targetAimSide = AimSide.None;

    bool isDocked;
    Transform dockedCameraTarget;

    bool isTraveling;
    bool isTransitioningToTravel;
    bool isTransitioningFromTravel;
    bool wasInTravelMode;
    float transitionTimer;

    // SmoothDamp velocity state for rotation
    float travelPitchVelocity;
    float travelYawVelocity;
    float aimPitchVelocity;
    float aimYawVelocity;
    float rollVelocity;

    bool IsInTravelMode => isTraveling || previewTravelMode;

    // Public getters
    public AimSide CurrentAimSide => currentAimSide;
    public bool IsAiming => isAiming;
    public bool IsCameraLocked => isAiming;

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
            travelPitchVelocity       = 0f;
            travelYawVelocity         = 0f;
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
    }

    public void ExitTravelMode()
    {
        isTraveling = false;
        // Reset pitch so player doesn't snap back from a steep angle
        cameraRotation.x = 0f;
    }

    void UpdateCameraRotation()
    {
        // Only allow free camera control when NOT aiming
        if (!isAiming)
        {
            if (IsInTravelMode)
            {
                if (isTransitioningToTravel)
                {
                    cameraRotation.x = Mathf.SmoothDampAngle(cameraRotation.x, travelPitch, ref travelPitchVelocity, travelTransitionTime);
                    cameraRotation.y = Mathf.SmoothDampAngle(cameraRotation.y, travelYaw,   ref travelYawVelocity,   travelTransitionTime);
                    // Completion is timer-driven from LateUpdate — no manual check needed
                }
                else
                {
                    // Transition complete — full free-look while keeping the travel position
                    float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
                    float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;
                    if (invertY) mouseY = -mouseY;

                    cameraRotation.x += mouseY;
                    cameraRotation.y += mouseX;
                    cameraRotation.x = Mathf.Clamp(cameraRotation.x, minVerticalAngle, maxVerticalAngle);

                    if (allow360Rotation)
                    {
                        if (cameraRotation.y > 180f)  cameraRotation.y -= 360f;
                        else if (cameraRotation.y < -180f) cameraRotation.y += 360f;
                    }
                    else
                    {
                        cameraRotation.y = Mathf.Clamp(cameraRotation.y, -maxHorizontalAngle, maxHorizontalAngle);
                    }
                }
            }
            else
            {
                float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
                float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;

                if (invertY)
                    mouseY = -mouseY;

                cameraRotation.x += mouseY;
                cameraRotation.y += mouseX;

                // Clamp vertical rotation
                cameraRotation.x = Mathf.Clamp(cameraRotation.x, minVerticalAngle, maxVerticalAngle);

                // Horizontal rotation handling
                if (allow360Rotation)
                {
                    if (cameraRotation.y > 180f)
                        cameraRotation.y -= 360f;
                    else if (cameraRotation.y < -180f)
                        cameraRotation.y += 360f;
                }
                else
                {
                    cameraRotation.y = Mathf.Clamp(cameraRotation.y, -maxHorizontalAngle, maxHorizontalAngle);
                }
            }
        }
        else
        {
            // When in aim mode, lock camera to side rotation
            if (targetAimSide != AimSide.None)
            {
                Vector3 aimRot = GetAimRotation(targetAimSide);
                float aimSmoothTime = 1f / aimSnapSpeed;
                cameraRotation.y = Mathf.SmoothDampAngle(cameraRotation.y, aimRot.y, ref aimYawVelocity,   aimSmoothTime);
                cameraRotation.x = Mathf.SmoothDampAngle(cameraRotation.x, aimRot.x, ref aimPitchVelocity, aimSmoothTime);
            }
        }

        // Target rotation follows ship with camera offset
        float targetRoll = 0f;
        if (isAiming && targetAimSide != AimSide.None)
        {
            Vector3 aimRot = GetAimRotation(targetAimSide);
            targetRoll = aimRot.z;
        }
        currentRoll = Mathf.SmoothDampAngle(currentRoll, targetRoll, ref rollVelocity, 1f / aimSnapSpeed);
        Quaternion targetRotation = ship.rotation * Quaternion.Euler(-cameraRotation.x, cameraRotation.y, currentRoll);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSmoothing * Time.deltaTime
        );
    }

    void DetermineSideFromCamera()
    {
        if (!isAiming)
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
        Vector3 targetOffset;

        if (isAiming)
        {
            // Use side-specific offset when aiming
            targetOffset = GetAimOffset(currentAimSide);
        }
        else if (IsInTravelMode)
        {
            targetOffset = travelOffset;
        }
        else
        {
            targetOffset = normalOffset;

            // Add dynamic offset based on ship speed when not aiming
            if (dynamicCamera)
            {
                ShipController shipController = ship.GetComponent<ShipController>();
                if (shipController)
                {
                    float speedRatio = shipController.CurrentSpeed / shipController.MaxSpeed;
                    targetOffset.z -= speedRatio * speedOffsetMultiplier;
                }
            }
        }

        // Calculate target position in world space
        Vector3 targetPos = ship.TransformPoint(targetOffset);

        // Travel enter and exit both use travelTransitionTime so the move in and out feel the same
        float smoothTime = (isTransitioningToTravel || isTransitioningFromTravel) ? travelTransitionTime
                         : isAiming                                               ? 1f / (positionSmoothing * 1.5f)
                                                                                  : 1f / positionSmoothing;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPos,
            ref currentVelocity,
            smoothTime
        );
    }

    void UpdateFieldOfView()
    {
        if (!cam) return;

        float targetFOV = isAiming ? aimFOV : IsInTravelMode ? travelFOV : normalFOV;
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
        isAiming = false;
        currentAimSide = AimSide.None;
        targetAimSide = AimSide.None;
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