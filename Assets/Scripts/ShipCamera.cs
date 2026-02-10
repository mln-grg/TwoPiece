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

    [Header("Side Aiming Rotations (Pitch, Yaw, Roll)")]
    public Vector3 leftAimRotation = new Vector3(0f, -45f, 0f);
    public Vector3 rightAimRotation = new Vector3(0f, 45f, 0f);
    
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
    
    [Tooltip("Max angle camera can rotate left/right")]
    public float maxHorizontalAngle = 60f;

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
    [Tooltip("Angle threshold to determine left vs right")]
    public float sideThreshold = 15f;

    public enum AimSide { None, Left, Right }
    
    Camera cam;
    Vector3 currentVelocity;
    Vector2 cameraRotation;
    float currentRoll;
    AimSide currentAimSide = AimSide.None;
    AimSide targetAimSide = AimSide.None;

    // Public getters
    public AimSide CurrentAimSide => currentAimSide;
    public bool IsAiming => isAiming;
    public bool IsCameraLocked => isAiming; // Camera is locked when aiming

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

        UpdateCameraRotation();
        DetermineSideFromCamera();
        UpdateCameraPosition();
        UpdateFieldOfView();
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

            // Clamp vertical and horizontal rotation
            cameraRotation.x = Mathf.Clamp(cameraRotation.x, minVerticalAngle, maxVerticalAngle);
            cameraRotation.y = Mathf.Clamp(cameraRotation.y, -maxHorizontalAngle, maxHorizontalAngle);
        }
        else
        {
            // When in aim mode, lock camera to side rotation
            if (targetAimSide != AimSide.None)
            {
                Vector3 aimRot = targetAimSide == AimSide.Left ? leftAimRotation : rightAimRotation;
                cameraRotation.y = Mathf.Lerp(cameraRotation.y, aimRot.y, aimSnapSpeed * Time.deltaTime);
                cameraRotation.x = Mathf.Lerp(cameraRotation.x, aimRot.x, aimSnapSpeed * Time.deltaTime);
            }
        }

        // Target rotation follows ship with camera offset
        float targetRoll = 0f;
        if (isAiming && targetAimSide != AimSide.None)
        {
            Vector3 aimRot = targetAimSide == AimSide.Left ? leftAimRotation : rightAimRotation;
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
            if (cameraRotation.y < -sideThreshold)
                currentAimSide = AimSide.Left;
            else if (cameraRotation.y > sideThreshold)
                currentAimSide = AimSide.Right;
            else
                currentAimSide = targetAimSide; // Keep current side in deadzone
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
            switch (currentAimSide)
            {
                case AimSide.Left:
                    targetOffset = leftAimOffset;
                    break;
                case AimSide.Right:
                    targetOffset = rightAimOffset;
                    break;
                default:
                    targetOffset = normalOffset;
                    break;
            }
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
        if (cameraRotation.y < 0f)
            targetAimSide = AimSide.Left;
        else
            targetAimSide = AimSide.Right;
        
        currentAimSide = targetAimSide;
    }

    public void ExitAimMode()
    {
        isAiming = false;
        currentAimSide = AimSide.None;
        targetAimSide = AimSide.None;
    }
}