using UnityEngine;

public class ShipCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform ship;

    [Header("Camera Position")]
    public Vector3 normalOffset = new Vector3(0f, 8f, -15f);
    public Vector3 aimOffset = new Vector3(0f, 6f, -10f);
    
    [Tooltip("How smoothly camera follows ship")]
    public float positionSmoothing = 5f;
    
    [Tooltip("How smoothly camera rotates")]
    public float rotationSmoothing = 3f;

    [Header("Camera Control")]
    public float lookSensitivity = 2f;
    public bool invertY = false;

    [Header("Aim Mode")]
    public bool isAiming;
    
    [Tooltip("Field of view when aiming")]
    public float aimFOV = 50f;
    
    [Tooltip("Field of view when normal")]
    public float normalFOV = 60f;
    
    [Tooltip("How fast FOV transitions")]
    public float fovSpeed = 8f;

    [Header("Dynamic Camera")]
    public bool dynamicCamera = true;
    
    [Tooltip("Extra offset based on ship speed")]
    public float speedOffsetMultiplier = 0.5f;

    Camera cam;
    Vector3 currentVelocity;
    Vector2 cameraRotation;

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

        UpdateCameraPosition();
        UpdateCameraRotation();
        UpdateFieldOfView();
    }

    void UpdateCameraPosition()
    {
        // Choose offset based on aim state
        Vector3 targetOffset = isAiming ? aimOffset : normalOffset;

        // Add dynamic offset based on ship speed
        if (dynamicCamera)
        {
            ShipController shipController = ship.GetComponent<ShipController>();
            if (shipController)
            {
                float speedRatio = shipController.CurrentSpeed / shipController.MaxSpeed;
                targetOffset.z -= speedRatio * speedOffsetMultiplier;
            }
        }

        // Calculate target position in world space
        Vector3 targetPos = ship.TransformPoint(targetOffset);

        // Smooth follow
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPos,
            ref currentVelocity,
            1f / positionSmoothing
        );
    }

    void UpdateCameraRotation()
    {
        // Get free look input (optional - can be disabled for simpler camera)
        if (Input.GetMouseButton(2)) // Middle mouse for free look
        {
            float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;

            if (invertY)
                mouseY = -mouseY;

            cameraRotation.x += mouseY;
            cameraRotation.y += mouseX;

            cameraRotation.x = Mathf.Clamp(cameraRotation.x, -30f, 45f);
        }
        else
        {
            // Return to neutral when not free-looking
            cameraRotation = Vector2.Lerp(cameraRotation, Vector2.zero, Time.deltaTime * 3f);
        }

        // Target rotation follows ship with free look offset
        Quaternion targetRotation = ship.rotation * Quaternion.Euler(-cameraRotation.x, cameraRotation.y, 0f);

        // Smooth rotation
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSmoothing * Time.deltaTime
        );
    }

    void UpdateFieldOfView()
    {
        if (!cam) return;

        float targetFOV = isAiming ? aimFOV : normalFOV;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, fovSpeed * Time.deltaTime);
    }

    public void SetAiming(bool aiming)
    {
        isAiming = aiming;
    }
}
