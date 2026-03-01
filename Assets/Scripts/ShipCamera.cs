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

    public enum AimSide { None, Left, Right, Front, Back }

    Camera cam;
    Vector3 currentVelocity;
    Vector2 cameraRotation;
    float currentRoll;
    AimSide currentAimSide = AimSide.None;
    AimSide targetAimSide = AimSide.None;

    bool isDocked;
    Transform dockedCameraTarget;

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

    void UpdateCameraRotation()
    {
        // Only allow free camera control when NOT aiming
        if (!isAiming)
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
                // Wrap around 360 degrees
                if (cameraRotation.y > 180f)
                    cameraRotation.y -= 360f;
                else if (cameraRotation.y < -180f)
                    cameraRotation.y += 360f;
            }
            else
            {
                // Clamp to limited angle
                cameraRotation.y = Mathf.Clamp(cameraRotation.y, -maxHorizontalAngle, maxHorizontalAngle);
            }
        }
        else
        {
            // When in aim mode, lock camera to side rotation
            if (targetAimSide != AimSide.None)
            {
                Vector3 aimRot = GetAimRotation(targetAimSide);
                cameraRotation.y = Mathf.Lerp(cameraRotation.y, aimRot.y, aimSnapSpeed * Time.deltaTime);
                cameraRotation.x = Mathf.Lerp(cameraRotation.x, aimRot.x, aimSnapSpeed * Time.deltaTime);
            }
        }

        // Target rotation follows ship with camera offset
        float targetRoll = 0f;
        if (isAiming && targetAimSide != AimSide.None)
        {
            Vector3 aimRot = GetAimRotation(targetAimSide);
            targetRoll = aimRot.z;
        }
        currentRoll = Mathf.Lerp(currentRoll, targetRoll, aimSnapSpeed * Time.deltaTime);
        Quaternion targetRotation = ship.rotation * Quaternion.Euler(-cameraRotation.x, cameraRotation.y, currentRoll);

        // Smooth rotation
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSmoothing * Time.deltaTime
        );
    }

    void DetermineSideFromCamera()
    {
        // Determine which side the camera is looking at based on horizontal rotation
        if (isAiming)
        {
            // Normalize angle to -180 to 180 range
            float normalizedAngle = cameraRotation.y;
            while (normalizedAngle > 180f) normalizedAngle -= 360f;
            while (normalizedAngle < -180f) normalizedAngle += 360f;
            
            // Front: -sideThreshold to +sideThreshold
            // Right: sideThreshold to (90 - sideThreshold)
            // Back: (90 + sideThreshold) to (180) and (-180) to (-90 - sideThreshold)
            // Left: (-90 + sideThreshold) to -sideThreshold
            
            if (normalizedAngle >= -sideThreshold && normalizedAngle <= sideThreshold)
            {
                currentAimSide = AimSide.Front;
            }
            else if (normalizedAngle > sideThreshold && normalizedAngle < 90f - sideThreshold)
            {
                currentAimSide = AimSide.Right;
            }
            else if (normalizedAngle < -sideThreshold && normalizedAngle > -90f + sideThreshold)
            {
                currentAimSide = AimSide.Left;
            }
            else if (Mathf.Abs(normalizedAngle) > 90f + sideThreshold)
            {
                currentAimSide = AimSide.Back;
            }
            else
            {
                // In deadzone, keep current side
                currentAimSide = targetAimSide;
            }
        }
        else
        {
            currentAimSide = AimSide.None;
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

        // Smooth follow (faster when aiming)
        float smoothing = isAiming ? positionSmoothing * 1.5f : positionSmoothing;
        
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPos,
            ref currentVelocity,
            1f / smoothing
        );
    }

    void UpdateFieldOfView()
    {
        if (!cam) return;

        float targetFOV = isAiming ? aimFOV : normalFOV;
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