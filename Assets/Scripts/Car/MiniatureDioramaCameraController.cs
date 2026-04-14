// ═══════════════════════════════════════════════════════════════════════════════
//  MiniatureDioramaCameraController.cs
//  Cinemachine 3.1.x   ·   HDRP 17.x   ·   Unity 6
// ───────────────────────────────────────────────────────────────────────────────
//
//  Replicates the stylized high-angle diorama camera used in Little Devil Inside's
//  world-map sections — a gentle top-down perspective that makes the world read
//  as a small physical tabletop model.
//
//  KEY DESIGN DECISIONS
//  ─────────────────────
//  • A hidden "lag target" GameObject is what Cinemachine actually tracks. We
//    SmoothDamp it toward the real player each frame, giving us complete control
//    over follow smoothing without touching Cinemachine's internal damping API
//    (which changed between CM versions). Set CinemachineFollow damping to 0 in
//    the Inspector — this script does all the easing.
//
//  • CinemachineFollow (Body) uses BindingMode.WorldSpace so the camera offset
//    is always in WORLD space, never rotated by the car's facing direction.
//    This is what keeps the angle fixed regardless of where the car points.
//
//  • CinemachineHardLookAt (Aim) points the camera precisely at the lag target.
//    Combined with the WorldSpace offset, the tilt angle naturally emerges from
//    the offset vector — no manual rotation needed.
//
//  • HDRP Manual Depth of Field blurs both foreground AND background simultaneously,
//    creating the classic tilt-shift "miniature model" illusion.
//
// ───────────────────────────────────────────────────────────────────────────────
//
//  SETUP — 10 steps
//  ─────────────────
//  1.  Main Camera: Add a CinemachineBrain component.
//      Set "Update Method" to Smart Update (default is fine).
//
//  2.  Create a new empty GameObject → name it "DioramaVirtualCamera".
//      Add these three components (order does not matter):
//        • CinemachineCamera
//        • CinemachineFollow       ← Body (position)
//        • CinemachineHardLookAt   ← Aim (rotation)
//        • MiniatureDioramaCameraController  (this script)
//
//  3.  On CinemachineCamera:
//        Lens → Field of View: 35  (the script overrides this at runtime)
//        Lens → Near Clip Plane: 0.3
//        Lens → Far Clip Plane: 1000
//        Priority: 10 (or whatever is highest in your scene)
//
//  4.  On CinemachineFollow:
//        Binding Mode: World Space
//        Position Damping X/Y/Z: 0   ← IMPORTANT — our lag target does the smoothing
//        Follow Offset: leave at 0; this script sets it every frame
//
//  5.  On CinemachineHardLookAt: no configuration needed.
//
//  6.  On MiniatureDioramaCameraController:
//        Follow Target: drag in your player / car transform
//
//  7.  (Optional — Depth of Field)
//        Create a new empty GameObject → Add a Volume component.
//        Set it to Global (Is Global = true) and assign a new Volume Profile.
//        In the profile, Add Override → Depth of Field.
//        Tick "All" to override everything, then set Focus Mode = Manual.
//        Drag this Volume into the "Post Process Volume" field on this script.
//
//  8.  (Optional — Tilt-Shift post process)
//        In the same Volume profile add:
//          • Bloom (Intensity ~0.3, Threshold ~0.9)          ← emissive glow
//          • Color Adjustments (Saturation +20, Contrast +15) ← toy colours
//          • Vignette (Intensity ~0.25)                       ← frame the miniature
//        These three combine with DoF for the full "physical model" look.
//
//  9.  (Optional — Camera sway / handheld feel)
//        Add a CinemachineBasicMultiChannelPerlin component to the same
//        DioramaVirtualCamera GameObject.
//        Assign a Noise Profile: "Handheld_tele_mild" or "Handheld_normal_mild".
//        Amplitude Gain: 0.05–0.15    Frequency Gain: 0.3–0.6
//        The built-in sway in this script also adds positional drift.
//
//  10. Press Play — the camera should float above and follow the car.
//      Tune tiltAngle (35–50°), orbitDistance, and fieldOfView in the Inspector.
//
// ═══════════════════════════════════════════════════════════════════════════════

using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;   // BindingMode enum lives here in CM 3.x
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[RequireComponent(typeof(CinemachineCamera))]
public class MiniatureDioramaCameraController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Target
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Target")]
    [Tooltip("The Transform the camera will follow — your player / car root.")]
    public Transform followTarget;

    // ═══════════════════════════════════════════════════════════════════════
    //  Camera Angle & Distance
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Camera Angle & Distance")]

    [Tooltip("Elevation angle above horizontal, in degrees. " +
             "0° = side-on view. 90° = directly overhead. " +
             "35–45° is the sweet spot for the diorama illusion.")]
    [Range(15f, 85f)]
    public float tiltAngle = 42f;

    [Tooltip("Radial distance from the follow target to the camera. " +
             "Lower = closer, bigger model feel. Higher = more of the world visible.")]
    public float orbitDistance = 14f;

    [Tooltip("World-space right/left shift of the camera relative to the target. " +
             "0 = centred. Positive = camera shifted right.")]
    public float lateralOffset = 0f;

    [Tooltip("World-space forward/back bias. Positive = camera looks ahead of the target " +
             "(shows more of what's coming). Negative = camera trails behind.")]
    public float forwardBias = 0f;

    // ═══════════════════════════════════════════════════════════════════════
    //  Lens
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Lens")]

    [Tooltip("Vertical Field of View in degrees. " +
             "Lower FOV = telephoto compression = stronger miniature illusion. " +
             "30–45° is recommended. Below 25° needs a larger orbitDistance.")]
    [Range(10f, 80f)]
    public float fieldOfView = 35f;

    [Tooltip("Near clip plane. Keep at 0.3 for miniature-scale scenes; " +
             "increase if you see z-fighting near the camera.")]
    public float nearClipPlane = 0.3f;

    [Tooltip("Far clip plane. 1000 is fine for most miniature landscapes.")]
    public float farClipPlane = 1000f;

    // ═══════════════════════════════════════════════════════════════════════
    //  Follow Smoothing
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Follow Smoothing")]

    [Tooltip("Smooth time for horizontal (X/Z) follow. " +
             "0.05 = snappy, 0.2 = responsive, 0.5 = dreamy cinematic lag. " +
             "This is the SmoothDamp time constant, not a speed.")]
    public float horizontalSmoothTime = 0.22f;

    [Tooltip("Smooth time for vertical (Y) follow. " +
             "Tuned separately so the camera absorbs bumps gently " +
             "without bobbing. Usually 1.5–2× the horizontal value.")]
    public float verticalSmoothTime   = 0.40f;

    // ═══════════════════════════════════════════════════════════════════════
    //  Zoom
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Zoom  (Mouse Scroll Wheel)")]

    [Tooltip("Allow scroll-wheel zoom by varying the orbit distance.")]
    public bool enableZoom = true;

    [Tooltip("World-units of orbit distance change per scroll unit.")]
    public float zoomSpeed = 6f;

    [Tooltip("How smoothly the zoom settles after scrolling (SmoothDamp time).")]
    public float zoomSmoothTime = 0.18f;

    [Tooltip("Closest the camera is allowed to get (orbitDistance lower bound).")]
    public float minOrbitDistance = 5f;

    [Tooltip("Furthest the camera is allowed to go (orbitDistance upper bound).")]
    public float maxOrbitDistance = 30f;

    // ═══════════════════════════════════════════════════════════════════════
    //  Handheld Sway
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Handheld Sway  (Optional)")]

    [Tooltip("Enable subtle Perlin-noise drift to simulate someone gently holding " +
             "a camera over a diorama — gives life to an otherwise locked shot.")]
    public bool enableSway = false;

    [Tooltip("World-space amplitude of the positional drift in metres. " +
             "0.05–0.1 m is barely perceptible; above 0.2 is noticeable.")]
    public float swayPositionAmplitude = 0.06f;

    [Tooltip("How fast the sway cycle moves. " +
             "Lower = slow, dreamy drift. Higher = jittery.")]
    public float swayFrequency = 0.12f;

    [Tooltip("Random seed offset so multiple cameras with sway don't move in sync.")]
    public float swaySeedOffset = 0f;

    // ═══════════════════════════════════════════════════════════════════════
    //  Depth of Field  (HDRP Volume — Manual mode)
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Depth of Field  (HDRP Manual Mode)")]

    [Tooltip("The HDRP Volume that contains the DepthOfField override. " +
             "Can be a Global or trigger-volume. Leave empty to skip.")]
    public Volume postProcessVolume;

    [Tooltip("If true, this script drives the DepthOfField distance values every frame " +
             "to keep the sharp focus band centred on the follow target as the camera moves.")]
    public bool controlDepthOfField = true;

    [Tooltip("Half-width of the sharp focus band around the target, in world units. " +
             "1.0–2.0 keeps a tight band, 4.0–6.0 is more forgiving. " +
             "Smaller = shallower DOF = stronger miniature illusion.")]
    public float focusBandHalfWidth = 1.8f;

    [Tooltip("Distance over which blur fades from sharp to maximum, " +
             "applied to both the near and far sides of the focus band. " +
             "2–4 units gives a gradual transition; 0.5 is very abrupt.")]
    public float blurFalloffDistance = 3.5f;

    [Tooltip("Maximum blur radius for both near (foreground) and far (background). " +
             "6–10 is strong; 3–5 is subtle. Both active simultaneously = tilt-shift look.")]
    [Range(1f, 16f)]
    public float maxBlurRadius = 9f;

    [Tooltip("How quickly the DOF focus distance tracks camera movement " +
             "(SmoothDamp time). Prevents the focus plane from snapping on bumps.")]
    public float focusSmoothTime = 0.25f;

    // ═══════════════════════════════════════════════════════════════════════
    //  References  (auto-assigned; only set manually if you have multiple)
    // ═══════════════════════════════════════════════════════════════════════

    [Header("References  (auto-assigned on Awake)")]

    [Tooltip("CinemachineFollow on this same GameObject. Auto-found if left empty.")]
    public CinemachineFollow followComponent;

    // ═══════════════════════════════════════════════════════════════════════
    //  Private state
    // ═══════════════════════════════════════════════════════════════════════

    CinemachineCamera vcam;
    Camera            mainCamera;

    // ── Lag target ──────────────────────────────────────────────────────────
    // The hidden GameObject that Cinemachine tracks. We SmoothDamp this toward
    // the real player each Update, keeping all smoothing in one place.
    Transform lagTarget;
    Vector3   lagVelocityXZ;   // SmoothDamp velocity for X and Z (stored in x and z)
    float     lagVelocityY;    // SmoothDamp velocity for Y

    // ── Zoom ────────────────────────────────────────────────────────────────
    float targetOrbitDistance;
    float zoomVelocity;

    // ── DOF ─────────────────────────────────────────────────────────────────
    DepthOfField dof;
    float        currentFocusDistance;
    float        focusDistanceVelocity;

    // ═══════════════════════════════════════════════════════════════════════
    //  Awake  — wiring
    // ═══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        // ── Grab components ─────────────────────────────────────────────────
        vcam       = GetComponent<CinemachineCamera>();
        mainCamera = Camera.main;

        if (followComponent == null)
            followComponent = GetComponent<CinemachineFollow>();

        if (followComponent == null)
        {
            Debug.LogError(
                "[DioramaCamera] No CinemachineFollow component found on this GameObject. " +
                "Add one and set its Binding Mode to World Space in the Inspector.", this);
            return;
        }

        // ── Binding mode ─────────────────────────────────────────────────────
        // WorldSpace = the FollowOffset is expressed in WORLD space, meaning the
        // camera offset never rotates when the car turns — exactly what we want.
        var trackerSettings = followComponent.TrackerSettings;
        trackerSettings.BindingMode = BindingMode.WorldSpace;
        followComponent.TrackerSettings = trackerSettings;

        // ── Create the lag target ─────────────────────────────────────────────
        // An invisible transform that Cinemachine follows with ZERO internal damping.
        // All smoothing lives in UpdateLagTarget() below — this keeps it centralised
        // and away from CM's version-sensitive internal API.
        var lagGO = new GameObject("[DioramaCamera_LagTarget]")
        {
            hideFlags = HideFlags.HideAndDontSave   // invisible in Hierarchy + won't save
        };
        lagTarget = lagGO.transform;

        if (followTarget != null)
            lagTarget.position = followTarget.position;

        // Point both Follow (position) and LookAt (rotation) at the lag target.
        vcam.Follow = lagTarget;
        vcam.LookAt = lagTarget;

        // ── Zoom init ────────────────────────────────────────────────────────
        targetOrbitDistance = orbitDistance;

        // ── DOF init ─────────────────────────────────────────────────────────
        InitialiseDepthOfField();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Start  — first-frame forced state so nothing "animates in"
    // ═══════════════════════════════════════════════════════════════════════

    void Start()
    {
        // Force offset and lens immediately — no pop on the first rendered frame
        ApplyFollowOffset();
        ApplyLens();

        // Seed the focus distance at the actual distance so DOF doesn't
        // animate from zero on the first frame
        if (mainCamera != null && followTarget != null)
        {
            currentFocusDistance = Vector3.Distance(
                mainCamera.transform.position, followTarget.position);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Update
    // ═══════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (followTarget == null || lagTarget == null) return;

        HandleZoomInput();       // scroll wheel → orbitDistance
        UpdateLagTarget();       // smooth follow of player
        ApplyFollowOffset();     // compute + push offset from tiltAngle + orbitDistance
        ApplyLens();             // push FOV + clip planes

        if (enableSway)
            ApplySway();         // Perlin noise positional drift

        if (controlDepthOfField && dof != null)
            UpdateDepthOfField();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Lag Target
    //  The heart of the smooth follow. We move this hidden transform toward
    //  the real player position using SmoothDamp on each axis independently.
    // ═══════════════════════════════════════════════════════════════════════

    void UpdateLagTarget()
    {
        Vector3 target  = followTarget.position;
        Vector3 current = lagTarget.position;

        // Horizontal axes use one smooth time — responsive to turning
        float newX = Mathf.SmoothDamp(
            current.x, target.x, ref lagVelocityXZ.x, horizontalSmoothTime);
        float newZ = Mathf.SmoothDamp(
            current.z, target.z, ref lagVelocityXZ.z, horizontalSmoothTime);

        // Vertical axis uses its own smooth time — absorbs bumps without bobbing
        float newY = Mathf.SmoothDamp(
            current.y, target.y, ref lagVelocityY, verticalSmoothTime);

        lagTarget.position = new Vector3(newX, newY, newZ);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Follow Offset
    //  Converts the human-friendly tiltAngle + orbitDistance into the
    //  world-space (X, Y, Z) offset that CinemachineFollow positions the camera at.
    //
    //  At tiltAngle = 0°  → camera is level with target (side view)
    //  At tiltAngle = 45° → Y and -Z are equal (classic isometric look)
    //  At tiltAngle = 90° → camera is directly overhead (flat top-down)
    // ═══════════════════════════════════════════════════════════════════════

    void ApplyFollowOffset()
    {
        if (followComponent == null) return;

        float rad = tiltAngle * Mathf.Deg2Rad;

        //  Y = height above target = distance × sin(tilt)
        //  Z = behind target       = distance × cos(tilt), negative = camera is behind
        float offsetY = orbitDistance * Mathf.Sin(rad);
        float offsetZ = -orbitDistance * Mathf.Cos(rad);

        followComponent.FollowOffset = new Vector3(
            lateralOffset,
            offsetY,
            offsetZ + forwardBias
        );
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Lens
    //  LensSettings is a struct — always read → modify → write back.
    // ═══════════════════════════════════════════════════════════════════════

    void ApplyLens()
    {
        var lens           = vcam.Lens;
        lens.FieldOfView   = fieldOfView;
        lens.NearClipPlane = nearClipPlane;
        lens.FarClipPlane  = farClipPlane;
        vcam.Lens          = lens;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Zoom
    // ═══════════════════════════════════════════════════════════════════════

    void HandleZoomInput()
    {
        if (!enableZoom) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            // Scroll up = zoom in (smaller distance); scroll down = zoom out
            targetOrbitDistance -= scroll * zoomSpeed;
            targetOrbitDistance  = Mathf.Clamp(
                targetOrbitDistance, minOrbitDistance, maxOrbitDistance);
        }

        // Ease toward the target distance each frame
        orbitDistance = Mathf.SmoothDamp(
            orbitDistance, targetOrbitDistance, ref zoomVelocity, zoomSmoothTime);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Handheld Sway
    //  Applies slow Perlin-noise drift to the follow offset (NOT to the lag
    //  target, so the camera wanders but still looks at the player precisely).
    //  For rotational sway, add a CinemachineBasicMultiChannelPerlin component
    //  to this same GameObject and assign a "Handheld_tele_mild" noise profile.
    // ═══════════════════════════════════════════════════════════════════════

    void ApplySway()
    {
        if (followComponent == null) return;

        float t = Time.time * swayFrequency + swaySeedOffset;

        // Perlin noise returns [0, 1]; remap to [-1, 1] for centred drift
        float noiseX = (Mathf.PerlinNoise(t,        t * 0.7f + 10f) - 0.5f) * 2f;
        float noiseZ = (Mathf.PerlinNoise(t * 0.8f + 7f, t + 3f)   - 0.5f) * 2f;

        float driftX = noiseX * swayPositionAmplitude;
        float driftZ = noiseZ * swayPositionAmplitude;

        // Add drift on top of the offset already set by ApplyFollowOffset.
        // We read the current offset (already computed this frame) and nudge it.
        var offset = followComponent.FollowOffset;
        offset.x  += driftX;
        offset.z  += driftZ;
        followComponent.FollowOffset = offset;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Depth of Field  — HDRP Manual Mode
    //
    //  Tilt-shift miniature effect requires blur on BOTH sides of the focus plane:
    //
    //    ←── near blur ───┤ nearStart    nearEnd ├──── SHARP BAND ────┤ farStart    farEnd ├─── far blur ───→
    //    (foreground fog)                         (player lives here)                        (background fog)
    //
    //  The HDRP Manual mode gives direct control over all four distances,
    //  which we recalculate every frame as the camera moves.
    // ═══════════════════════════════════════════════════════════════════════

    void InitialiseDepthOfField()
    {
        if (postProcessVolume == null || !controlDepthOfField) return;

        // Find or create the DepthOfField override in the Volume profile
        if (!postProcessVolume.profile.TryGet<DepthOfField>(out dof))
        {
            dof = postProcessVolume.profile.Add<DepthOfField>(true);
            Debug.Log("[DioramaCamera] DepthOfField override added to Volume profile.", this);
        }

        dof.active = true;

        // Switch to Manual mode — UsePhysicalCamera is more realistic but harder to
        // art-direct. Manual lets us define exactly where blur starts and ends.
        dof.focusMode.value         = DepthOfFieldMode.Manual;
        dof.focusMode.overrideState = true;

        // Set max blur radius for both sides.
        // These stay constant; only the distance band changes at runtime.
        // nearMaxBlur and farMaxBlur are plain float properties in HDRP 17.x,
        // NOT VolumeParameter<float> — assign directly, no .Override() needed.
        dof.nearMaxBlur = maxBlurRadius;
        dof.farMaxBlur  = maxBlurRadius;
    }

    void UpdateDepthOfField()
    {
        if (dof == null || mainCamera == null || followTarget == null) return;

        // How far is the camera from the follow target right now?
        float rawDistance = Vector3.Distance(
            mainCamera.transform.position, followTarget.position);

        // Smooth it — without this, the DOF plane snaps visibly on every bump
        currentFocusDistance = Mathf.SmoothDamp(
            currentFocusDistance, rawDistance,
            ref focusDistanceVelocity, focusSmoothTime);

        float focus = currentFocusDistance;

        // ── Near (foreground) blur band ──────────────────────────────────
        // nearFocusEnd   = where near blur fades to zero / sharp region starts
        // nearFocusStart = where near blur reaches maximum (closer to camera)
        float nearFocusEnd   = Mathf.Max(0.1f,  focus - focusBandHalfWidth);
        float nearFocusStart = Mathf.Max(0.01f, nearFocusEnd - blurFalloffDistance);

        // ── Far (background) blur band ───────────────────────────────────
        // farFocusStart = where sharp region ends / far blur begins to ramp up
        // farFocusEnd   = where far blur reaches maximum (further from camera)
        float farFocusStart = focus + focusBandHalfWidth;
        float farFocusEnd   = farFocusStart + blurFalloffDistance;

        dof.nearFocusStart.Override(nearFocusStart);
        dof.nearFocusEnd.Override(nearFocusEnd);
        dof.farFocusStart.Override(farFocusStart);
        dof.farFocusEnd.Override(farFocusEnd);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Swap the follow target at runtime (e.g. when the player switches vehicles).
    /// The lag target instantly snaps to the new target's position to avoid a
    /// long travel shot.
    /// </summary>
    public void SetFollowTarget(Transform newTarget, bool snapInstantly = true)
    {
        followTarget = newTarget;
        if (snapInstantly) SnapToTarget();
    }

    /// <summary>
    /// Instantly teleport the lag target to the current followTarget position.
    /// Call this whenever the player teleports so the camera doesn't slide across the map.
    /// </summary>
    public void SnapToTarget()
    {
        if (followTarget == null || lagTarget == null) return;
        lagTarget.position = followTarget.position;
        lagVelocityXZ      = Vector3.zero;
        lagVelocityY       = 0f;
    }

    /// <summary>
    /// Smoothly animate the tilt angle to a new value (useful for cinematics).
    /// Call this each frame with a Lerped value, or just set the field directly.
    /// </summary>
    public void SetTiltAngle(float degrees)
    {
        tiltAngle = Mathf.Clamp(degrees, 15f, 85f);
    }

    /// <summary>
    /// Smoothly animate the field of view (e.g. zoom in on a point of interest).
    /// </summary>
    public void SetFieldOfView(float fov)
    {
        fieldOfView = Mathf.Clamp(fov, 10f, 80f);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Cleanup
    // ═══════════════════════════════════════════════════════════════════════

    void OnDestroy()
    {
        // The lag target is HideAndDontSave, so Unity won't clean it up automatically
        if (lagTarget != null)
            Destroy(lagTarget.gameObject);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Gizmos  — visualise the camera cone and DOF bands in the Scene view
    // ═══════════════════════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        if (followTarget == null) return;

        float rad       = tiltAngle * Mathf.Deg2Rad;
        float offsetY   = orbitDistance * Mathf.Sin(rad);
        float offsetZ   = -orbitDistance * Mathf.Cos(rad);
        Vector3 camPos  = followTarget.position + new Vector3(lateralOffset, offsetY, offsetZ);

        // Camera → target line
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(camPos, followTarget.position);

        // Camera position marker
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(camPos, 0.25f);

        // Target marker
        Gizmos.color = new Color(1f, 0.6f, 0f);
        Gizmos.DrawWireSphere(followTarget.position, 0.15f);

        // DOF focus band — shown as a horizontal slab centred on the target
        if (controlDepthOfField)
        {
            // Sharp band (green)
            Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
            Vector3 bandSize   = new Vector3(orbitDistance, focusBandHalfWidth * 2f, orbitDistance);
            Gizmos.DrawWireCube(followTarget.position, bandSize);

            // Blur falloff zones (red, semi-transparent)
            Gizmos.color = new Color(1f, 0f, 0f, 0.12f);
            Vector3 falloffSize = new Vector3(
                orbitDistance,
                focusBandHalfWidth * 2f + blurFalloffDistance * 2f,
                orbitDistance);
            Gizmos.DrawWireCube(followTarget.position, falloffSize);
        }

#if UNITY_EDITOR
        // Angle arc label
        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.Label(
            camPos + Vector3.up * 0.4f,
            $"Tilt: {tiltAngle:F0}°  FOV: {fieldOfView:F0}°  Dist: {orbitDistance:F1}m");
#endif
    }
}
