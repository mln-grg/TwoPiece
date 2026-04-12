using UnityEngine;

/// <summary>
/// Attach to any dock GameObject. Place a child transform and assign it to
/// "Dock Point" — this is where the player ship will be pulled to.
///
/// Docking input:
///   Controller — hold X (JoystickButton0) while inside detection range.
///   Keyboard   — hold E while inside detection range.
///
/// An in-game ring drawn with a LineRenderer shows the detection radius
/// at all times. It pulses gold when the player is close enough to dock.
///
/// Setup:
///   1. Add this script to your dock/island/port object.
///   2. Create a child GameObject (e.g. "DockPoint"), position + rotate it
///      where you want the ship to rest, and assign it to Dock Point.
///   3. Make sure the player ship has the tag "Player".
/// </summary>
public class DockingStation : MonoBehaviour
{
    [Header("Dock Point")]
    [Tooltip("Child transform that defines where the ship parks when docked. " +
             "Its forward direction should match the docked ship's facing.")]
    public Transform dockPoint;

    [Tooltip("Child transform the camera moves to when docked. Leave empty to skip.")]
    public Transform dockedCameraPoint;

    [Header("Detection")]
    [Tooltip("How close the player must be before the dock prompt appears.")]
    public float detectionRange = 20f;

    [Header("Docking Motion")]
    [Tooltip("How fast (units/sec) the ship moves toward the dock point.")]
    public float moveSpeed = 5f;

    [Tooltip("How fast (deg/sec) the ship rotates toward the dock orientation.")]
    public float rotateSpeed = 60f;

    [Tooltip("Distance at which the ship snaps into the exact docked position.")]
    public float snapDistance = 0.3f;

    [Header("Interaction")]
    [Tooltip("Seconds the player must hold the dock button before docking begins.")]
    public float holdDuration = 1.5f;

    [Header("Player")]
    public string playerTag = "Player";

    // ── Radius ring visualisation ──────────────────────────────────────────────
    [Header("Radius Ring")]
    [Tooltip("Number of line segments used to draw the circle — higher = smoother.")]
    public int ringSegments = 64;

    [Tooltip("Width of the ring line in world units.")]
    public float ringWidth = 0.25f;

    [Tooltip("Height offset of the ring above this transform's position (local Y).")]
    public float ringHeightOffset = 0.5f;

    [Tooltip("Ring colour when no player is nearby.")]
    public Color ringColorIdle = new Color(0.20f, 1.00f, 0.50f, 0.30f);

    [Tooltip("Ring colour (base) when the player is inside the detection range.")]
    public Color ringColorNear = new Color(1.00f, 0.80f, 0.20f, 0.85f);

    [Tooltip("Ring pulse speed (oscillations per second) when player is near.")]
    public float ringPulseSpeed = 2f;

    // ── Cached player references ───────────────────────────────────────────────
    Transform       playerTransform;
    ShipController  shipController;
    PlayerShipInput playerInput;
    ShipCamera      shipCamera;
    MonoBehaviour   floatingBoat;

    // ── State machine ──────────────────────────────────────────────────────────
    enum DockState { Sailing, NearDock, Docked }
    DockState state = DockState.Sailing;

    float      holdTimer;
    float      undockCooldown;
    bool       holdActive;
    Vector3    dockStartPosition;
    Quaternion dockStartRotation;
    Quaternion dockedRotation;

    // ── LineRenderer ring ──────────────────────────────────────────────────────
    LineRenderer _ring;

    // ── GUI resources ──────────────────────────────────────────────────────────
    GUIStyle  promptStyle;
    Texture2D barBgTex;
    Texture2D barFillTex;

    // ==========================================================================

    void Start()
    {
        if (!dockPoint)
            Debug.LogWarning($"[DockingStation] No DockPoint assigned on '{gameObject.name}'.", this);

        BuildRing();
    }

    // ── Ring ───────────────────────────────────────────────────────────────────

    void BuildRing()
    {
        // Create a dedicated child GameObject so the ring can be positioned cleanly.
        var ringGO = new GameObject("_DockingRing");
        ringGO.transform.SetParent(transform, worldPositionStays: false);
        ringGO.transform.localPosition = new Vector3(0f, ringHeightOffset, 0f);
        ringGO.transform.localRotation = Quaternion.identity;

        _ring = ringGO.AddComponent<LineRenderer>();
        _ring.useWorldSpace    = false;
        _ring.loop             = true;
        _ring.startWidth       = ringWidth;
        _ring.endWidth         = ringWidth;
        _ring.positionCount    = ringSegments;
        _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _ring.receiveShadows   = false;

        // Use a simple unlit material — no special assets required.
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.renderQueue = 3000; // transparent queue
        _ring.material  = mat;

        // Place circle points in local space.
        float step = 2f * Mathf.PI / ringSegments;
        for (int i = 0; i < ringSegments; i++)
        {
            float angle = i * step;
            _ring.SetPosition(i, new Vector3(
                Mathf.Cos(angle) * detectionRange,
                0f,
                Mathf.Sin(angle) * detectionRange));
        }

        SetRingColor(ringColorIdle);
    }

    void SetRingColor(Color c)
    {
        if (!_ring) return;
        _ring.startColor = c;
        _ring.endColor   = c;
    }

    // ==========================================================================
    // MAIN UPDATE
    // ==========================================================================

    void Update()
    {
        if (!playerTransform)
            FindPlayer();

        if (!playerTransform || !dockPoint)
        {
            SetRingColor(ringColorIdle);
            return;
        }

        switch (state)
        {
            case DockState.Sailing:
            case DockState.NearDock:
                UpdateProximity();
                break;

            case DockState.Docked:
                playerTransform.position = dockPoint.position;
                // Undock: same button used to dock (X / E)
                if (Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.E))
                    Undock();
                SetRingColor(ringColorIdle);
                break;
        }
    }

    // ==========================================================================
    // PROXIMITY & HOLD INPUT
    // ==========================================================================

    void UpdateProximity()
    {
        float dist = Vector3.Distance(playerTransform.position, dockPoint.position);

        if (dist > detectionRange)
        {
            state          = DockState.Sailing;
            holdTimer      = 0f;
            undockCooldown = 0f;
            if (holdActive) CancelHold();
            SetRingColor(ringColorIdle);
            return;
        }

        // ── Player is within range ─────────────────────────────────────────────
        state = DockState.NearDock;
        undockCooldown = Mathf.Max(0f, undockCooldown - Time.deltaTime);

        // Pulse the ring colour to draw attention
        float pulse    = 0.5f + 0.5f * Mathf.Sin(Time.time * ringPulseSpeed * Mathf.PI * 2f);
        Color nearPulse = Color.Lerp(
            ringColorNear,
            new Color(ringColorNear.r, ringColorNear.g, ringColorNear.b, 1f),
            pulse);
        SetRingColor(nearPulse);

        // Dock input: X (JoystickButton0) or E key
        bool dockHeld = undockCooldown <= 0f &&
                        (Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.E));

        if (dockHeld)
        {
            if (!holdActive)
                StartHold();

            holdTimer += Time.deltaTime;

            float t = holdDuration > 0f ? Mathf.Clamp01(holdTimer / holdDuration) : 1f;
            playerTransform.position = Vector3.Lerp(dockStartPosition, dockPoint.position, t);
            playerTransform.rotation = Quaternion.Lerp(dockStartRotation, dockedRotation, t);

            if (holdTimer >= holdDuration)
                CompleteDocking();
        }
        else
        {
            if (holdActive) CancelHold();
            holdTimer = 0f;
        }
    }

    // ==========================================================================
    // DOCKING
    // ==========================================================================

    void StartHold()
    {
        holdActive = true;

        dockStartPosition = playerTransform.position;
        dockStartRotation = playerTransform.rotation;
        dockedRotation    = Quaternion.Euler(0f, playerTransform.eulerAngles.y, 0f);

        if (shipController)
        {
            shipController.steeringInput = 0f;
            shipController.thrusterInput = 0f;
            shipController.brakeInput    = 0f;
            shipController.currentGear   = GearState.Idle; // kept for legacy compat
        }

        if (playerInput)    playerInput.enabled    = false;
        if (shipController) shipController.enabled  = false;
        if (floatingBoat)   floatingBoat.enabled    = false;
    }

    void CancelHold()
    {
        holdActive = false;

        if (playerInput)    playerInput.enabled    = true;
        if (shipController) shipController.enabled  = true;
        if (floatingBoat)   floatingBoat.enabled    = true;
    }

    void CompleteDocking()
    {
        holdActive = false;
        holdTimer  = 0f;
        state      = DockState.Docked;

        playerTransform.SetPositionAndRotation(dockPoint.position, dockedRotation);

        /*if (shipCamera && dockedCameraPoint)
            shipCamera.EnterDockedView(dockedCameraPoint);*/
    }

    // ==========================================================================
    // UNDOCKING
    // ==========================================================================

    void Undock()
    {
        state          = DockState.Sailing;
        undockCooldown = 0.4f;
        holdActive     = false;
        holdTimer      = 0f;

        /*if (shipCamera) shipCamera.ExitDockedView();*/

        if (playerInput)    playerInput.enabled    = true;
        if (shipController) shipController.enabled  = true;
        if (floatingBoat)   floatingBoat.enabled    = true;
    }

    // ==========================================================================
    // PLAYER DISCOVERY
    // ==========================================================================

    void FindPlayer()
    {
        var go = GameObject.FindGameObjectWithTag(playerTag);
        if (!go) return;

        playerTransform = go.transform;
        shipController  = go.GetComponent<ShipController>();
        playerInput     = go.GetComponent<PlayerShipInput>();
        floatingBoat    = go.GetComponent("FloatingBoat") as MonoBehaviour;

        shipCamera = playerInput ? playerInput.shipCamera : null;
        if (!shipCamera)
            shipCamera = Object.FindObjectOfType<ShipCamera>();
    }

    // ==========================================================================
    // ON-SCREEN PROMPT
    // ==========================================================================

    void OnGUI()
    {
        if (state != DockState.NearDock && state != DockState.Docked)
            return;

        EnsureGUIResources();

        const float barW   = 300f;
        const float barH   = 14f;
        const float labelH = 34f;
        const float pad    = 6f;

        float cx   = (Screen.width  - barW) * 0.5f;
        float baseY = Screen.height * 0.75f;

        string prompt = state == DockState.Docked
            ? "Press [X / E] to undock"
            : holdTimer > 0.01f ? "Docking..." : "Hold [X / E] to dock";

        DrawShadowedLabel(new Rect(cx, baseY, barW, labelH), prompt);

        if (state == DockState.NearDock && holdTimer > 0.01f)
        {
            float pct      = holdDuration > 0f ? Mathf.Clamp01(holdTimer / holdDuration) : 1f;
            float barY     = baseY + labelH + pad;
            var   bgRect   = new Rect(cx, barY, barW, barH);
            var   fillRect = new Rect(cx + 1f, barY + 1f, (barW - 2f) * pct, barH - 2f);

            GUI.DrawTexture(bgRect, barBgTex);

            Color fillColor = Color.Lerp(
                new Color(0.15f, 0.80f, 0.75f),
                new Color(1.00f, 0.80f, 0.20f),
                pct);

            var oldColor = GUI.color;
            GUI.color = fillColor;
            GUI.DrawTexture(fillRect, barFillTex);
            GUI.color = oldColor;

            DrawShadowedLabel(new Rect(cx, barY - 1f, barW, barH + 2f),
                              $"{Mathf.RoundToInt(pct * 100f)}%",
                              fontSize: 11, alpha: 0.85f);
        }
    }

    void EnsureGUIResources()
    {
        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 20,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white }
            };
        }

        if (barBgTex == null)
        {
            barBgTex = new Texture2D(1, 1);
            barBgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
            barBgTex.Apply();
        }

        if (barFillTex == null)
        {
            barFillTex = new Texture2D(1, 1);
            barFillTex.SetPixel(0, 0, Color.white);
            barFillTex.Apply();
        }
    }

    void DrawShadowedLabel(Rect rect, string text, int fontSize = 0, float alpha = 1f)
    {
        var style = new GUIStyle(promptStyle);
        if (fontSize > 0) style.fontSize = fontSize;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.65f * alpha);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
        style.normal.textColor = new Color(1f, 1f, 1f, alpha);
        GUI.Label(rect, text, style);
    }

    // ==========================================================================
    // EDITOR GIZMOS (Supplement to the in-game ring — selected-only detail view)
    // ==========================================================================

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.18f);
        Gizmos.DrawSphere(transform.position, detectionRange);
        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        if (!dockPoint) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(dockPoint.position, 1.5f);
        Gizmos.DrawLine(dockPoint.position, dockPoint.position + dockPoint.forward * 4f);
        Gizmos.DrawLine(dockPoint.position + dockPoint.right * 2f,
                        dockPoint.position - dockPoint.right * 2f);
    }
}
