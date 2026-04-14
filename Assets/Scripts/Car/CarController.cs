using System;
using UnityEngine;

/// <summary>
/// Grounded car controller: drives on the surface via per-wheel spring-damper suspension.
/// Supports handbrake-induced oversteer, front-wheel steering, and wheel mesh animation.
/// Visual tilt is applied to a separate child mesh so physics stays clean.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Preset definition
    // ═══════════════════════════════════════════════════════════════════════

    [Serializable]
    public struct CarPreset
    {
        [Header("Physics")]
        public float mass;
        public float linearDrag;
        public float angularDrag;

        [Header("Suspension")]
        [Tooltip("Natural (unloaded) length of the suspension spring in metres")]
        public float suspensionRestLength;
        [Tooltip("Maximum compression travel beyond rest length")]
        public float suspensionTravel;
        [Tooltip("Spring force coefficient (tune until car sits at ~40% compression at rest)")]
        public float springStrength;
        [Tooltip("Vertical velocity damping, prevents oscillation")]
        public float springDamping;

        [Header("Speed")]
        public float maxForwardSpeed;
        public float maxReverseSpeed;
        [Tooltip("Acceleration in m/s²")]
        public float acceleration;
        [Tooltip("Extra deceleration when braking against current motion")]
        public float brakeDeceleration;
        [Tooltip("Per-frame fraction of forward velocity cancelled when throttle is released. " +
                 "Higher = stops faster.")]
        public float forwardReleaseDamp;

        [Header("Steering")]
        [Tooltip("Yaw torque applied when steering")]
        public float turnTorque;
        [Tooltip("Maximum visual angle of front wheels when steering fully")]
        public float maxSteerAngle;

        [Header("Grip")]
        [Tooltip("Per-frame fraction of lateral velocity cancelled when driving normally. " +
                 "Higher = grippier. ~0.18 is responsive.")]
        public float normalLateralDamp;
        [Tooltip("Per-frame fraction cancelled at rear axle while handbrake is held. " +
                 "~0.01–0.02 gives a good rear-slide.")]
        public float handbrakeLateralDamp;

        [Header("Visual Tilt")]
        [Tooltip("Max bank angle (roll) when steering")]
        public float maxBankAngle;
        [Tooltip("Max pitch angle when accelerating/braking")]
        public float maxPitchAngle;
        [Tooltip("How quickly the visual tilt interpolates")]
        public float tiltSpeed;
    }

    public enum PresetType { Light, Medium, Heavy, Custom }
    public enum DriveMode  { FrontWheelDrive, RearWheelDrive, AllWheelDrive }

    // ═══════════════════════════════════════════════════════════════════════
    //  Inspector
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Active Preset")]
    public PresetType activePreset = PresetType.Medium;

    [Header("Presets  (edit values here)")]
    public CarPreset lightPreset;
    public CarPreset mediumPreset;
    public CarPreset heavyPreset;
    [Tooltip("Fully custom — selected when activePreset = Custom")]
    public CarPreset customPreset;

    [Header("Drive Mode")]
    [Tooltip("Affects which wheel meshes spin visually. Physics drive force is the same regardless.")]
    public DriveMode driveMode = DriveMode.AllWheelDrive;

    [Header("Ground Detection")]
    public LayerMask groundMask = ~0;

    [Header("Wheel Transforms  (physics origins)")]
    [Tooltip("4 empty child GameObjects at wheel centre positions. Order: FL, FR, RL, RR")]
    public Transform[] wheelPoints;

    [Header("Wheel Meshes  (visual only)")]
    [Tooltip("Visual wheel meshes — spun and steered. Order: FL, FR, RL, RR. " +
             "If a wheel spins backwards, enable invertWheelSpin.")]
    public Transform[] wheelMeshes;

    [Tooltip("Enable if your wheel meshes spin in the wrong direction")]
    public bool invertWheelSpin = false;

    [Header("Visual Mesh")]
    [Tooltip("Assign the visual child mesh here to receive banking/pitch tilt. " +
             "The physics parent stays level. Leave empty to skip.")]
    public Transform visualMesh;

    // ═══════════════════════════════════════════════════════════════════════
    //  Input  (set each frame by CarInput)
    // ═══════════════════════════════════════════════════════════════════════

    [HideInInspector] public float ThrottleInput;   // -1 … +1
    [HideInInspector] public float SteerInput;      // -1 … +1
    [HideInInspector] public bool  HandbrakeHeld;

    // ═══════════════════════════════════════════════════════════════════════
    //  Runtime state
    // ═══════════════════════════════════════════════════════════════════════

    Rigidbody rb;
    CarPreset p;           // active preset copy

    bool   isGrounded;
    bool[] wheelGrounded;  // per-wheel contact flag (length 4)

    float currentBank;     // smoothed visual roll
    float currentPitch;    // smoothed visual pitch

    float wheelSpinAngle;  // accumulated spin rotation in degrees

    // ═══════════════════════════════════════════════════════════════════════
    //  Unity callbacks
    // ═══════════════════════════════════════════════════════════════════════

    // Populates preset fields with sane defaults when the component is first added
    // or when Reset is chosen in the Inspector.
    void Reset()
    {
        lightPreset  = MakeLight();
        mediumPreset = MakeMedium();
        heavyPreset  = MakeHeavy();
        customPreset = MakeMedium();
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Interpolation is the #1 fix for visual jitter when the camera
        // runs in LateUpdate and physics runs in FixedUpdate.
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        wheelGrounded = new bool[4];
        ApplyPreset(activePreset);
    }

    void FixedUpdate()
    {
        Suspension();
        Drive();
        Steer();
        ApplyLateralGrip();
        Stabilize();
        AnimateWheels();
        AnimateVisualTilt();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Preset
    // ═══════════════════════════════════════════════════════════════════════

    public void ApplyPreset(PresetType type)
    {
        p = type switch
        {
            PresetType.Light  => lightPreset,
            PresetType.Heavy  => heavyPreset,
            PresetType.Custom => customPreset,
            _                 => mediumPreset,
        };

        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.mass           = p.mass;
        rb.linearDamping  = p.linearDrag;
        rb.angularDamping = p.angularDrag;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Suspension  — spring-damper per wheel
    // ═══════════════════════════════════════════════════════════════════════

    void Suspension()
    {
        isGrounded = false;

        if (wheelPoints == null || wheelPoints.Length == 0)
            return;

        float rayLength = p.suspensionRestLength + p.suspensionTravel;

        for (int i = 0; i < wheelPoints.Length; i++)
        {
            Transform pt = wheelPoints[i];
            if (pt == null) continue;

            if (!Physics.Raycast(pt.position, -transform.up, out RaycastHit hit,
                                 rayLength, groundMask))
            {
                wheelGrounded[i] = false;
                continue;
            }

            wheelGrounded[i] = true;
            isGrounded = true;

            // How compressed is the spring? 0 = fully extended, 1 = fully compressed.
            float compression = 1f - (hit.distance / rayLength);

            // Spring: proportional to compression
            float spring = compression * p.springStrength;

            // Damping: opposes the vertical velocity at this contact point
            float vertVel = Vector3.Dot(rb.GetPointVelocity(pt.position), transform.up);
            float damping = vertVel * p.springDamping;

            float force = Mathf.Max(0f, spring - damping);
            rb.AddForceAtPosition(transform.up * force, pt.position, ForceMode.Acceleration);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Drive
    // ═══════════════════════════════════════════════════════════════════════

    void Drive()
    {
        if (Mathf.Abs(ThrottleInput) < 0.01f)
        {
            // No throttle — bleed forward speed
            Vector3 loc = transform.InverseTransformDirection(rb.linearVelocity);
            loc.z = Mathf.Lerp(loc.z, 0f, p.forwardReleaseDamp);
            rb.linearVelocity = transform.TransformDirection(loc);
            return;
        }

        if (!isGrounded) return;

        float fwdSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float limit    = ThrottleInput > 0f ? p.maxForwardSpeed : p.maxReverseSpeed;

        if (Mathf.Abs(fwdSpeed) < limit)
            rb.AddForce(transform.forward * (ThrottleInput * p.acceleration),
                        ForceMode.Acceleration);

        // Extra braking force when pushing against current motion direction
        if (ThrottleInput < 0f && fwdSpeed > 0.5f)
            rb.AddForce(-transform.forward * p.brakeDeceleration, ForceMode.Acceleration);
        else if (ThrottleInput > 0f && fwdSpeed < -0.5f)
            rb.AddForce(transform.forward * p.brakeDeceleration, ForceMode.Acceleration);
    }

    void Steer()
    {
        if (Mathf.Abs(SteerInput) < 0.01f) return;
        if (!isGrounded) return;

        // Scale torque by speed so the car can't spin on the spot
        float speedNorm = Mathf.Clamp01(rb.linearVelocity.magnitude / (p.maxForwardSpeed * 0.5f));

        rb.AddTorque(transform.up * (SteerInput * p.turnTorque * speedNorm),
                     ForceMode.Acceleration);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Lateral grip  — the core of the "car feel"
    //  Handbrake = full front grip, reduced rear grip → oversteer / tail-slide
    // ═══════════════════════════════════════════════════════════════════════

    void ApplyLateralGrip()
    {
        if (!isGrounded) return;

        if (HandbrakeHeld && wheelPoints != null && wheelPoints.Length >= 4)
        {
            // Front axle: full grip
            Vector3 frontCenter = (wheelPoints[0].position + wheelPoints[1].position) * 0.5f;
            ApplyAxleLateralDamp(frontCenter, p.normalLateralDamp);

            // Rear axle: barely any grip — tail slides out
            Vector3 rearCenter = (wheelPoints[2].position + wheelPoints[3].position) * 0.5f;
            ApplyAxleLateralDamp(rearCenter, p.handbrakeLateralDamp);
        }
        else
        {
            // Normal grip — kill lateral slip globally (same as HoverCarController)
            Vector3 loc = transform.InverseTransformDirection(rb.linearVelocity);
            loc.x = Mathf.Lerp(loc.x, 0f, p.normalLateralDamp);
            rb.linearVelocity = transform.TransformDirection(loc);
        }
    }

    // Applies lateral damping as a velocity-change impulse at a specific world point.
    // Using AddForceAtPosition means front/rear corrections produce real yaw moments.
    void ApplyAxleLateralDamp(Vector3 worldPos, float fraction)
    {
        Vector3 pointVel   = rb.GetPointVelocity(worldPos);
        float   lateralVel = Vector3.Dot(pointVel, transform.right);
        float   correction = -lateralVel * fraction;
        rb.AddForceAtPosition(transform.right * correction, worldPos,
                              ForceMode.VelocityChange);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Stabilize  — gentle upright correction so the car doesn't tip over
    // ═══════════════════════════════════════════════════════════════════════

    void Stabilize()
    {
        Vector3 correction = Vector3.Cross(transform.up, Vector3.up);
        rb.AddTorque(correction * 8f - rb.angularVelocity * 2f, ForceMode.Acceleration);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Wheel animation  — spin + front-wheel steering
    // ═══════════════════════════════════════════════════════════════════════

    void AnimateWheels()
    {
        if (wheelMeshes == null || wheelMeshes.Length < 4) return;

        // Approximate wheel radius from suspension geometry.
        // If your wheels look wrong, adjust this heuristic or expose it as an inspector field.
        float wheelRadius      = p.suspensionRestLength * 0.5f;
        float fwdSpeed         = Vector3.Dot(rb.linearVelocity, transform.forward);
        float degreesPerSecond = (fwdSpeed / (2f * Mathf.PI * wheelRadius)) * 360f;

        if (invertWheelSpin) degreesPerSecond = -degreesPerSecond;

        wheelSpinAngle  = (wheelSpinAngle + degreesPerSecond * Time.fixedDeltaTime) % 360f;

        float targetSteer = SteerInput * p.maxSteerAngle;

        for (int i = 0; i < 4; i++)
        {
            if (wheelMeshes[i] == null) continue;

            bool isFront = i < 2;   // 0 = FL, 1 = FR, 2 = RL, 3 = RR

            bool isDriveWheel = driveMode switch
            {
                DriveMode.FrontWheelDrive => isFront,
                DriveMode.RearWheelDrive  => !isFront,
                _                         => true,   // AllWheelDrive
            };

            float spinAngle = isDriveWheel ? wheelSpinAngle : 0f;

            // Steer rotation (Y) combined with spin rotation (X).
            // Multiplying steerRot * spinRot means spin happens in the steered local frame.
            Quaternion spinRot  = Quaternion.Euler(spinAngle, 0f, 0f);
            Quaternion steerRot = isFront
                ? Quaternion.Euler(0f, targetSteer, 0f)
                : Quaternion.identity;

            wheelMeshes[i].localRotation = steerRot * spinRot;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Visual tilt  — banking and pitch on the mesh child only
    // ═══════════════════════════════════════════════════════════════════════

    void AnimateVisualTilt()
    {
        if (visualMesh == null) return;

        float t      = Time.fixedDeltaTime * p.tiltSpeed;
        currentBank  = Mathf.Lerp(currentBank,  -SteerInput    * p.maxBankAngle,  t);
        currentPitch = Mathf.Lerp(currentPitch, -ThrottleInput * p.maxPitchAngle, t);

        visualMesh.localRotation = Quaternion.Euler(currentPitch, 0f, currentBank);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Gizmos
    // ═══════════════════════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        if (wheelPoints == null) return;

        CarPreset preview = activePreset switch
        {
            PresetType.Light  => lightPreset,
            PresetType.Heavy  => heavyPreset,
            PresetType.Custom => customPreset,
            _                 => mediumPreset,
        };

        float rayLen = preview.suspensionRestLength + preview.suspensionTravel;

        Gizmos.color = Color.green;
        foreach (Transform pt in wheelPoints)
        {
            if (pt == null) continue;
            Gizmos.DrawRay(pt.position, -pt.up * rayLen);
            // Wire sphere marks the rest position (where the wheel sits unloaded)
            Gizmos.DrawWireSphere(pt.position - pt.up * preview.suspensionRestLength, 0.02f);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Default preset values  (tuned for miniature car, ~0.2–0.3 m scale)
    // ═══════════════════════════════════════════════════════════════════════

    static CarPreset MakeLight() => new CarPreset
    {
        // Nimble, quick, loose rear
        mass                  = 0.5f,
        linearDrag            = 1.8f,
        angularDrag           = 5f,
        suspensionRestLength  = 0.06f,
        suspensionTravel      = 0.03f,
        springStrength        = 35f,
        springDamping         = 6f,
        maxForwardSpeed       = 7f,
        maxReverseSpeed       = 3f,
        acceleration          = 18f,
        brakeDeceleration     = 22f,
        forwardReleaseDamp    = 0.15f,
        turnTorque            = 8f,
        maxSteerAngle         = 35f,
        normalLateralDamp     = 0.20f,
        handbrakeLateralDamp  = 0.02f,
        maxBankAngle          = 12f,
        maxPitchAngle         = 7f,
        tiltSpeed             = 7f,
    };

    static CarPreset MakeMedium() => new CarPreset
    {
        // Well-rounded, responsive but not twitchy
        mass                  = 1.0f,
        linearDrag            = 1.5f,
        angularDrag           = 5f,
        suspensionRestLength  = 0.07f,
        suspensionTravel      = 0.035f,
        springStrength        = 28f,
        springDamping         = 7f,
        maxForwardSpeed       = 5f,
        maxReverseSpeed       = 2f,
        acceleration          = 12f,
        brakeDeceleration     = 16f,
        forwardReleaseDamp    = 0.12f,
        turnTorque            = 6f,
        maxSteerAngle         = 30f,
        normalLateralDamp     = 0.18f,
        handbrakeLateralDamp  = 0.015f,
        maxBankAngle          = 8f,
        maxPitchAngle         = 5f,
        tiltSpeed             = 6f,
    };

    static CarPreset MakeHeavy() => new CarPreset
    {
        // Sluggish, planted, tight
        mass                  = 2.0f,
        linearDrag            = 1.2f,
        angularDrag           = 6f,
        suspensionRestLength  = 0.08f,
        suspensionTravel      = 0.04f,
        springStrength        = 22f,
        springDamping         = 9f,
        maxForwardSpeed       = 3.5f,
        maxReverseSpeed       = 1.5f,
        acceleration          = 8f,
        brakeDeceleration     = 12f,
        forwardReleaseDamp    = 0.10f,
        turnTorque            = 4f,
        maxSteerAngle         = 25f,
        normalLateralDamp     = 0.14f,
        handbrakeLateralDamp  = 0.01f,
        maxBankAngle          = 5f,
        maxPitchAngle         = 4f,
        tiltSpeed             = 4f,
    };

#if UNITY_EDITOR
    void OnValidate()
    {
        // Allow live preset-switching in the Inspector during Play mode
        if (Application.isPlaying && rb != null)
            ApplyPreset(activePreset);
    }
#endif
}
