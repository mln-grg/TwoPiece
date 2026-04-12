using System;
using UnityEngine;

/// <summary>
/// Hover-car controller: moves like a grounded car (with drift) but floats
/// above the surface via spring-based hover. Supports jump + Mario Kart-style glide.
/// Visual tilt is applied to a separate child mesh so physics stays clean.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class HoverCarController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Preset definition
    // ═══════════════════════════════════════════════════════════════════════

    [Serializable]
    public struct ShipPreset
    {
        [Header("Physics")]
        public float mass;
        public float linearDrag;
        public float angularDrag;

        [Header("Hover")]
        [Tooltip("Target height above the ground surface")]
        public float hoverHeight;
        [Tooltip("Spring force pushing the ship up (tune until it floats stably)")]
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
                 "Higher = stops faster. 0.1 stops in ~1s, 0.25 stops almost immediately.")]
        public float forwardReleaseDamp;

        [Header("Steering")]
        [Tooltip("Yaw torque applied when steering")]
        public float turnTorque;

        [Header("Grip")]
        [Tooltip("Per-frame fraction of lateral velocity cancelled when driving normally. " +
                 "Higher = grippier. ~0.15 is responsive, ~0.25 is very tight.")]
        public float normalLateralDamp;
        [Tooltip("Per-frame fraction cancelled while drifting. ~0.01–0.03 gives a good slide.")]
        public float driftLateralDamp;

        [Header("Visual Tilt")]
        [Tooltip("Max bank angle (roll) when steering")]
        public float maxBankAngle;
        [Tooltip("Max pitch angle when accelerating/braking")]
        public float maxPitchAngle;
        [Tooltip("How quickly the visual tilt interpolates")]
        public float tiltSpeed;

        [Header("Jump")]
        [Tooltip("Upward velocity added on jump")]
        public float jumpForce;
        [Tooltip("0–1. Throttle/steer effectiveness in the air")]
        public float airControlFactor;
        [Tooltip("Extra gravity multiplier while falling (makes landings snappier). 1 = normal.")]
        public float fallGravityMultiplier;
    }

    public enum PresetType { Light, Medium, Heavy, Custom }

    // ═══════════════════════════════════════════════════════════════════════
    //  Inspector
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Active Preset")]
    public PresetType activePreset = PresetType.Medium;

    [Header("Presets  (edit values here)")]
    public ShipPreset lightPreset;
    public ShipPreset mediumPreset;
    public ShipPreset heavyPreset;
    [Tooltip("Fully custom — selected when activePreset = Custom")]
    public ShipPreset customPreset;

    [Header("Ground Detection")]
    public LayerMask groundMask = ~0;

    [Header("Hover Points")]
    [Tooltip("Assign 4 empty child GameObjects at the ship's corners. " +
             "If left empty, falls back to the ship's pivot.")]
    public Transform[] hoverPoints;

    [Header("Visual Mesh")]
    [Tooltip("Assign the visual child mesh here to receive banking/pitch tilt. " +
             "The physics parent stays level. Leave empty to skip.")]
    public Transform visualMesh;

    // ═══════════════════════════════════════════════════════════════════════
    //  Input  (set each frame by HoverCarInput)
    // ═══════════════════════════════════════════════════════════════════════

    [HideInInspector] public float ThrottleInput;   // -1 … +1
    [HideInInspector] public float SteerInput;      // -1 … +1
    [HideInInspector] public bool  DriftHeld;
    [HideInInspector] public bool  JumpPressed;     // single-frame flag

    // ═══════════════════════════════════════════════════════════════════════
    //  Runtime state
    // ═══════════════════════════════════════════════════════════════════════

    Rigidbody rb;
    ShipPreset p;           // active preset copy

    bool  isGrounded;
    float airTime;

    float currentBank;      // smoothed visual roll
    float currentPitch;     // smoothed visual pitch

    // ═══════════════════════════════════════════════════════════════════════
    //  Unity callbacks
    // ═══════════════════════════════════════════════════════════════════════

    // Called when the component is first added, and when Reset is chosen in
    // the Inspector — populates the preset fields with sane defaults.
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
        // runs in LateUpdate and physics in FixedUpdate.
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        ApplyPreset(activePreset);
    }

    void FixedUpdate()
    {
        Hover();
        Move();
        Steer();
        ApplyLateralGrip();
        HandleJumpGlide();
        Stabilize();
        AnimateVisualTilt();

        // JumpPressed is a single-frame flag — clear it here so it can't
        // fire twice if FixedUpdate runs more than once per visual frame.
        JumpPressed = false;
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
        rb.mass            = p.mass;
        rb.linearDamping   = p.linearDrag;
        rb.angularDamping  = p.angularDrag;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Hover  — spring force per hover-point
    // ═══════════════════════════════════════════════════════════════════════

    void Hover()
    {
        isGrounded = false;

        Transform[] pts = (hoverPoints != null && hoverPoints.Length > 0)
            ? hoverPoints
            : new[] { transform };

        foreach (Transform pt in pts)
        {
            if (!Physics.Raycast(pt.position, -Vector3.up, out RaycastHit hit,
                                 p.hoverHeight * 2f, groundMask))
                continue;

            float compression = Mathf.Clamp01(1f - hit.distance / p.hoverHeight);

            // Spring: proportional to compression
            float spring = compression * p.springStrength;

            // Damping: opposes vertical velocity at this point
            float vertVel = Vector3.Dot(rb.GetPointVelocity(pt.position), Vector3.up);
            float damping = vertVel * p.springDamping;

            float force = Mathf.Max(0f, spring - damping);
            rb.AddForceAtPosition(Vector3.up * force, pt.position, ForceMode.Acceleration);

            if (hit.distance <= p.hoverHeight * 1.15f)
                isGrounded = true;
        }

        if (isGrounded)
            airTime = 0f;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Drive
    // ═══════════════════════════════════════════════════════════════════════

    void Move()
    {
        if (Mathf.Abs(ThrottleInput) < 0.01f)
        {
            // No throttle held — bleed forward speed
            Vector3 loc = transform.InverseTransformDirection(rb.linearVelocity);
            loc.z = Mathf.Lerp(loc.z, 0f, p.forwardReleaseDamp);
            rb.linearVelocity = transform.TransformDirection(loc);
            return;
        }

        float ctrl     = isGrounded ? 1f : p.airControlFactor;
        float fwdSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float limit    = ThrottleInput > 0f ? p.maxForwardSpeed : p.maxReverseSpeed;

        if (Mathf.Abs(fwdSpeed) < limit)
            rb.AddForce(transform.forward * (ThrottleInput * p.acceleration * ctrl),
                        ForceMode.Acceleration);

        // Extra braking force when pushing against current motion
        if (ThrottleInput < 0f && fwdSpeed > 0.5f)
            rb.AddForce(-transform.forward * (p.brakeDeceleration * ctrl),
                        ForceMode.Acceleration);
    }

    void Steer()
    {
        if (Mathf.Abs(SteerInput) < 0.01f) return;

        // Scale torque by speed so the ship can't spin on the spot
        float speedNorm = Mathf.Clamp01(rb.linearVelocity.magnitude / (p.maxForwardSpeed * 0.5f));
        float ctrl      = isGrounded ? 1f : p.airControlFactor * 0.5f;

        rb.AddTorque(transform.up * (SteerInput * p.turnTorque * speedNorm * ctrl),
                     ForceMode.Acceleration);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Lateral grip  — the core of the "car feel"
    //  Each FixedUpdate we nudge lateral velocity toward zero by the damp factor.
    //  normalLateralDamp ≈ 0.15  →  tight grip
    //  driftLateralDamp  ≈ 0.02  →  loose, slidey drift
    // ═══════════════════════════════════════════════════════════════════════

    void ApplyLateralGrip()
    {
        if (!isGrounded) return;

        float damp  = DriftHeld ? p.driftLateralDamp : p.normalLateralDamp;
        Vector3 loc = transform.InverseTransformDirection(rb.linearVelocity);
        loc.x       = Mathf.Lerp(loc.x, 0f, damp);
        rb.linearVelocity = transform.TransformDirection(loc);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Jump
    // ═══════════════════════════════════════════════════════════════════════

    void HandleJumpGlide()
    {
        if (JumpPressed && isGrounded)
        {
            rb.AddForce(Vector3.up * p.jumpForce, ForceMode.VelocityChange);
            isGrounded = false;
        }

        if (!isGrounded)
        {
            airTime += Time.fixedDeltaTime;

            // Extra fall gravity for snappy, satisfying landings
            float extra = Mathf.Abs(Physics.gravity.y) * (p.fallGravityMultiplier - 1f);
            rb.AddForce(Vector3.down * extra, ForceMode.Acceleration);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Stabilize  — gentle upright correction so the ship doesn't tip over
    // ═══════════════════════════════════════════════════════════════════════

    void Stabilize()
    {
        // Cross product gives the axis/amount to rotate toward Vector3.up
        Vector3 correction = Vector3.Cross(transform.up, Vector3.up);
        rb.AddTorque(correction * 8f - rb.angularVelocity * 2f, ForceMode.Acceleration);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Visual tilt  — banking and pitch on the mesh child only
    // ═══════════════════════════════════════════════════════════════════════

    void AnimateVisualTilt()
    {
        if (visualMesh == null) return;

        float t       = Time.fixedDeltaTime * p.tiltSpeed;
        currentBank   = Mathf.Lerp(currentBank,  -SteerInput   * p.maxBankAngle,  t);
        currentPitch  = Mathf.Lerp(currentPitch, -ThrottleInput * p.maxPitchAngle, t);

        visualMesh.localRotation = Quaternion.Euler(currentPitch, 0f, currentBank);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Gizmos
    // ═══════════════════════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        Transform[] pts = (hoverPoints != null && hoverPoints.Length > 0)
            ? hoverPoints
            : new[] { transform };

        Gizmos.color = Color.cyan;
        foreach (var pt in pts)
            Gizmos.DrawRay(pt.position, Vector3.down * (p.hoverHeight * 2f));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Default preset values
    // ═══════════════════════════════════════════════════════════════════════

    static ShipPreset MakeLight() => new ShipPreset
    {
        // Nimble, floaty, lots of drift potential
        mass                  = 500f,
        linearDrag            = 1.5f,
        angularDrag           = 4f,
        hoverHeight           = 1.6f,
        springStrength        = 65f,
        springDamping         = 7f,
        maxForwardSpeed       = 48f,
        maxReverseSpeed       = 16f,
        acceleration          = 28f,
        brakeDeceleration     = 35f,
        forwardReleaseDamp    = 0.15f,
        turnTorque            = 130f,
        normalLateralDamp     = 0.18f,
        driftLateralDamp      = 0.02f,
        maxBankAngle          = 24f,
        maxPitchAngle         = 10f,
        tiltSpeed             = 7f,
        jumpForce             = 11f,
        airControlFactor      = 0.5f,
        fallGravityMultiplier = 1.8f,
    };

    static ShipPreset MakeMedium() => new ShipPreset
    {
        // Well-rounded, responsive but not twitchy
        mass                  = 1100f,
        linearDrag            = 1.2f,
        angularDrag           = 5f,
        hoverHeight           = 2.0f,
        springStrength        = 55f,
        springDamping         = 9f,
        maxForwardSpeed       = 34f,
        maxReverseSpeed       = 12f,
        acceleration          = 18f,
        brakeDeceleration     = 24f,
        forwardReleaseDamp    = 0.12f,
        turnTorque            = 95f,
        normalLateralDamp     = 0.15f,
        driftLateralDamp      = 0.015f,
        maxBankAngle          = 16f,
        maxPitchAngle         = 8f,
        tiltSpeed             = 6f,
        jumpForce             = 8f,
        airControlFactor      = 0.4f,
        fallGravityMultiplier = 2.0f,
    };

    static ShipPreset MakeHeavy() => new ShipPreset
    {
        // Sluggish, hard to turn, planted, short glide
        mass                  = 2400f,
        linearDrag            = 1.0f,
        angularDrag           = 6f,
        hoverHeight           = 2.4f,
        springStrength        = 50f,
        springDamping         = 12f,
        maxForwardSpeed       = 24f,
        maxReverseSpeed       = 8f,
        acceleration          = 12f,
        brakeDeceleration     = 16f,
        forwardReleaseDamp    = 0.10f,
        turnTorque            = 60f,
        normalLateralDamp     = 0.12f,
        driftLateralDamp      = 0.01f,
        maxBankAngle          = 10f,
        maxPitchAngle         = 6f,
        tiltSpeed             = 4f,
        jumpForce             = 6f,
        airControlFactor      = 0.3f,
        fallGravityMultiplier = 2.5f,
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
