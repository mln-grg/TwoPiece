using UnityEngine;

/// <summary>
/// Attach to any dock GameObject. Place a child transform and assign it to
/// "Dock Point" — this is where the player ship will be pulled to.
///
/// Setup:
///   1. Add this script to your dock/island/port object.
///   2. Create a child GameObject (e.g. "DockPoint"), position + rotate it
///      where you want the ship to rest, and assign it to Dock Point.
///   3. Make sure the player ship GameObject has the tag "Player".
/// </summary>
public class DockingStation : MonoBehaviour
{
    [Header("Dock Point")]
    [Tooltip("Child transform that defines where the ship parks when docked. " +
             "Its forward direction should point the same way the docked ship faces.")]
    public Transform dockPoint;

    [Tooltip("Child transform the camera moves to when docked. Position and rotate it " +
             "to frame the scene the way you want. Leave empty to skip camera override.")]
    public Transform dockedCameraPoint;

    [Header("Detection")]
    [Tooltip("How close the player must be before the dock prompt appears.")]
    public float detectionRange = 20f;

    [Header("Docking Motion")]
    [Tooltip("How fast (units/sec) the ship moves toward the dock point.")]
    public float moveSpeed = 5f;

    [Tooltip("How fast (degrees/sec) the ship rotates toward the dock orientation.")]
    public float rotateSpeed = 60f;

    [Tooltip("Distance at which the ship snaps into the exact docked position.")]
    public float snapDistance = 0.3f;

    [Header("Interaction")]
    [Tooltip("Seconds the player must hold E before docking begins. Set to 0 for instant.")]
    public float holdDuration = 1.5f;

    [Header("Player")]
    [Tooltip("Tag on the player ship GameObject.")]
    public string playerTag = "Player";

    // ---- cached player references ----
    Transform       playerTransform;
    ShipController  shipController;
    PlayerShipInput playerInput;
    ShipCamera      shipCamera;
    MonoBehaviour   floatingBoat;   // FloatingBoat — fetched by name to avoid hard dependency

    // ---- state machine ----
    enum DockState { Sailing, NearDock, Docking, Docked }
    DockState state = DockState.Sailing;

    float holdTimer;
    float undockCooldown;   // prevents E-press on undock from immediately re-triggering dock

    // ---- cached GUI resources (built once) ----
    GUIStyle  promptStyle;
    Texture2D barBgTex;
    Texture2D barFillTex;

    // =============================================================

    void Start()
    {
        if (!dockPoint)
            Debug.LogWarning($"[DockingStation] No DockPoint assigned on '{gameObject.name}'. " +
                             "Create a child GameObject, position it, and assign it to Dock Point.", this);
    }

    void Update()
    {
        if (!playerTransform)
            FindPlayer();

        if (!playerTransform || !dockPoint)
            return;

        switch (state)
        {
            case DockState.Sailing:
            case DockState.NearDock:
                UpdateProximity();
                break;

            case DockState.Docking:
                PerformDocking();
                break;

            case DockState.Docked:
                if (Input.GetKeyDown(KeyCode.E))
                    Undock();
                break;
        }
    }

    // =============================================================
    // PROXIMITY & HOLD INPUT
    // =============================================================

    void UpdateProximity()
    {
        float dist = Vector3.Distance(playerTransform.position, dockPoint.position);

        if (dist > detectionRange)
        {
            state        = DockState.Sailing;
            holdTimer    = 0f;
            undockCooldown = 0f;
            return;
        }

        state = DockState.NearDock;

        undockCooldown = Mathf.Max(0f, undockCooldown - Time.deltaTime);

        if (Input.GetKey(KeyCode.E) && undockCooldown <= 0f)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdDuration)
                BeginDocking();
        }
        else
        {
            holdTimer = 0f;
        }
    }

    // =============================================================
    // DOCKING
    // =============================================================

    void BeginDocking()
    {
        state     = DockState.Docking;
        holdTimer = 0f;

        // Neutralise ship state before disabling its Update
        if (shipController)
        {
            shipController.steeringInput = 0f;
            shipController.currentSail   = SailState.NoSail;
        }

        // Disable player-controlled components so they don't fight us
        if (playerInput)  playerInput.enabled  = false;
        if (shipController) shipController.enabled = false;
        if (floatingBoat)   floatingBoat.enabled   = false;
    }

    void PerformDocking()
    {
        // Smoothly slide the ship toward the dock point
        playerTransform.position = Vector3.MoveTowards(
            playerTransform.position,
            dockPoint.position,
            moveSpeed * Time.deltaTime
        );

        // Smoothly rotate to match the dock orientation
        playerTransform.rotation = Quaternion.RotateTowards(
            playerTransform.rotation,
            dockPoint.rotation,
            rotateSpeed * Time.deltaTime
        );

        // Snap when close enough
        if (Vector3.Distance(playerTransform.position, dockPoint.position) <= snapDistance)
        {
            playerTransform.SetPositionAndRotation(dockPoint.position, dockPoint.rotation);
            state = DockState.Docked;

            if (shipCamera && dockedCameraPoint)
                shipCamera.EnterDockedView(dockedCameraPoint);
        }
    }

    // =============================================================
    // UNDOCKING
    // =============================================================

    void Undock()
    {
        state          = DockState.Sailing;
        undockCooldown = 0.4f;  // block dock input briefly so the same keypress doesn't re-trigger

        if (shipCamera) shipCamera.ExitDockedView();

        if (playerInput)    playerInput.enabled    = true;
        if (shipController) shipController.enabled  = true;
        if (floatingBoat)   floatingBoat.enabled    = true;
    }

    // =============================================================
    // PLAYER DISCOVERY
    // =============================================================

    void FindPlayer()
    {
        var go = GameObject.FindGameObjectWithTag(playerTag);
        if (!go) return;

        playerTransform = go.transform;
        shipController  = go.GetComponent<ShipController>();
        playerInput     = go.GetComponent<PlayerShipInput>();
        floatingBoat    = go.GetComponent("FloatingBoat") as MonoBehaviour;

        // Try to get camera from PlayerShipInput first, then fall back to scene search
        shipCamera = playerInput ? playerInput.shipCamera : null;
        if (!shipCamera)
            shipCamera = Object.FindObjectOfType<ShipCamera>();
    }

    // =============================================================
    // ON-SCREEN PROMPT (works without a Canvas — uses legacy OnGUI)
    // =============================================================

    void OnGUI()
    {
        if (state != DockState.NearDock && state != DockState.Docked)
            return;

        EnsureGUIResources();

        const float barW    = 300f;
        const float barH    = 14f;
        const float labelH  = 34f;
        const float padding = 6f;

        float centerX = (Screen.width  - barW) * 0.5f;
        float baseY   =  Screen.height * 0.75f;

        // ---- label ----
        string prompt = state == DockState.Docked
            ? "Press [E] to undock"
            : holdTimer > 0.01f ? "Docking..." : "Hold [E] to dock";

        DrawShadowedLabel(new Rect(centerX, baseY, barW, labelH), prompt);

        // ---- progress bar (only while actively holding E) ----
        if (state == DockState.NearDock && holdTimer > 0.01f)
        {
            float pct    = holdDuration > 0f ? Mathf.Clamp01(holdTimer / holdDuration) : 1f;
            float barY   = baseY + labelH + padding;
            var   bgRect = new Rect(centerX, barY, barW, barH);
            var   fillRect = new Rect(centerX + 1f, barY + 1f,
                                      (barW - 2f) * pct, barH - 2f);

            // Background
            GUI.DrawTexture(bgRect, barBgTex);

            // Filled portion — colour shifts from teal to gold as it completes
            Color fillColor = Color.Lerp(
                new Color(0.15f, 0.80f, 0.75f),   // teal
                new Color(1.00f, 0.80f, 0.20f),   // gold
                pct
            );
            var oldColor = GUI.color;
            GUI.color = fillColor;
            GUI.DrawTexture(fillRect, barFillTex);
            GUI.color = oldColor;

            // Percentage label centred on the bar
            DrawShadowedLabel(new Rect(centerX, barY - 1f, barW, barH + 2f),
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
            barFillTex.SetPixel(0, 0, Color.white);   // tinted at draw-time via GUI.color
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

    // =============================================================
    // EDITOR GIZMOS
    // =============================================================

    void OnDrawGizmosSelected()
    {
        // Detection sphere
        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.18f);
        Gizmos.DrawSphere(transform.position, detectionRange);
        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        if (!dockPoint) return;

        // Dock point marker
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(dockPoint.position, 1.5f);
        // Forward arrow
        Gizmos.DrawLine(dockPoint.position, dockPoint.position + dockPoint.forward * 4f);
        // Wing lines to hint at ship width
        Gizmos.DrawLine(dockPoint.position + dockPoint.right * 2f,
                        dockPoint.position - dockPoint.right * 2f);
    }
}
