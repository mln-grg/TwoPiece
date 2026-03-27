using UnityEngine;

/// <summary>
/// Configurable on-screen debug HUD. Shows player gear, speed, and health bars
/// alongside the enemy's health bars. Every visual value is exposed in the Inspector.
///
/// Setup:
///   • Attach to any GameObject (e.g. an empty called "DebugHUD").
///   • Ships are auto-found at runtime if the fields are left empty
///     (player via "Player" tag, enemy via the first AIBrain in the scene).
/// </summary>
public class DebugHUD : MonoBehaviour
{
    // =========================================================================
    //  INSPECTOR
    // =========================================================================

    [Header("References")]
    [Tooltip("Player ship root. Auto-found via 'Player' tag if left empty.")]
    public ShipController playerShip;

    [Tooltip("Enemy ship root. Auto-found via first AIBrain in scene if left empty.")]
    public ShipController enemyShip;

    // ── Visibility ───────────────────────────────────────────────────────────

    [Header("Visibility")]
    [Tooltip("Show the HUD at startup.")]
    public bool visible = true;

    [Tooltip("Keyboard key that toggles the HUD on/off at runtime.")]
    public KeyCode toggleKey = KeyCode.F1;

    // ── Position & Size ──────────────────────────────────────────────────────

    [Header("Position & Size")]
    [Tooltip("Pixel distance from the left edge of the screen.")]
    public int panelX = 20;

    [Tooltip("Pixel distance from the top edge of the screen.")]
    public int panelY = 20;

    [Tooltip("Total width of the HUD panel in pixels.")]
    public int panelWidth = 300;

    [Tooltip("Inner padding on all sides of the panel (pixels).")]
    public int padding = 12;

    [Tooltip("Width of the left-hand label column (pixels). "
           + "Increase if labels are getting clipped.")]
    public int labelColumnWidth = 90;

    // ── Row Sizing ───────────────────────────────────────────────────────────

    [Header("Row Sizing")]
    [Tooltip("Height of each text row in pixels.")]
    public int lineHeight = 26;

    [Tooltip("Height of the health bars in pixels.")]
    public int barHeight = 12;

    [Tooltip("Extra vertical gap between the Player and Enemy sections (pixels).")]
    public int sectionSpacing = 10;

    // ── Font ─────────────────────────────────────────────────────────────────

    [Header("Font")]
    [Tooltip("Font size for section headers (e.g. PLAYER SHIP).")]
    public int headerFontSize = 14;

    [Tooltip("Font size for data labels and values.")]
    public int labelFontSize = 13;

    [Tooltip("Draw a drop-shadow behind all text to improve readability.")]
    public bool textShadow = true;

    [Tooltip("Shadow offset in pixels.")]
    public int shadowOffset = 1;

    // ── Panel Colours ─────────────────────────────────────────────────────────

    [Header("Panel Colour")]
    [Tooltip("Colour and opacity of the HUD background panel.")]
    public Color panelColor = new Color(0.04f, 0.04f, 0.04f, 0.82f);

    // ── Text Colours ─────────────────────────────────────────────────────────

    [Header("Text Colours")]
    public Color headerColor  = new Color(1.00f, 0.85f, 0.25f, 1f);
    public Color labelColor   = new Color(0.75f, 0.75f, 0.75f, 1f);
    public Color valueColor   = new Color(1.00f, 1.00f, 1.00f, 1f);
    public Color shadowColor  = new Color(0.00f, 0.00f, 0.00f, 0.85f);

    // ── Bar Colours ───────────────────────────────────────────────────────────

    [Header("Hull Bar Colours")]
    public Color hullBarFill  = new Color(0.90f, 0.25f, 0.20f, 1f);
    public Color hullBarEmpty = new Color(0.35f, 0.08f, 0.06f, 1f);

    [Header("Sail Bar Colours")]
    public Color sailBarFill  = new Color(0.25f, 0.65f, 1.00f, 1f);
    public Color sailBarEmpty = new Color(0.06f, 0.18f, 0.38f, 1f);

    // =========================================================================
    //  PRIVATE STATE
    // =========================================================================

    HealthComponent _playerHull;
    HealthComponent _playerSail;
    HealthComponent _enemyHull;
    HealthComponent _enemySail;

    GUIStyle _boxStyle;
    GUIStyle _headerStyle;
    GUIStyle _labelStyle;
    GUIStyle _valueStyle;
    GUIStyle _shadowStyle;
    Texture2D _panelTex;
    bool _stylesReady;

    // =========================================================================
    //  UNITY LIFECYCLE
    // =========================================================================

    void Start()
    {
        if (playerShip == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) playerShip = p.GetComponent<ShipController>();
        }

        if (enemyShip == null)
        {
            AIBrain brain = FindObjectOfType<AIBrain>();
            if (brain != null) enemyShip = brain.Ship;
        }

        CacheHealthComponents();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            visible = !visible;
    }

    // Rebuilds styles whenever an Inspector value changes in the Editor.
    void OnValidate() => _stylesReady = false;

    void OnDestroy()
    {
        if (_panelTex != null) Destroy(_panelTex);
    }

    // =========================================================================
    //  HEALTH CACHE
    // =========================================================================

    void CacheHealthComponents()
    {
        if (playerShip != null) { _playerHull = playerShip.hullHealth; _playerSail = playerShip.sailHealth; }
        if (enemyShip  != null) { _enemyHull  = enemyShip.hullHealth;  _enemySail  = enemyShip.sailHealth;  }
    }

    // =========================================================================
    //  GUI
    // =========================================================================

    void OnGUI()
    {
        if (!visible) return;

        EnsureStyles();

        // Re-cache if a ship was destroyed and refs went stale
        if ((playerShip != null && _playerHull == null) ||
            (enemyShip  != null && _enemyHull  == null))
            CacheHealthComponents();

        // ── Measure total panel height ────────────────────────────────────
        int playerRows = playerShip != null ? 4 : 1;   // thruster, speed, hull, sail
        int enemyRows  = enemyShip  != null ? 2 : 1;   // hull, sail
        // 2 headers + player rows + section gap row + enemy rows
        int totalRows  = 2 + playerRows + 1 + enemyRows;
        int panelH     = padding + totalRows * (lineHeight + 2) + sectionSpacing + padding;

        // ── Draw panel background ─────────────────────────────────────────
        GUI.Box(new Rect(panelX, panelY, panelWidth, panelH), GUIContent.none, _boxStyle);

        float cy = panelY + padding;

        // ── PLAYER SHIP ───────────────────────────────────────────────────
        DrawHeader(ref cy, "▸  PLAYER SHIP");

        if (playerShip != null)
        {
            DrawRow(ref cy, "Thruster", $"{playerShip.ThrusterPercent * 100f:F0}%");
            DrawRow (ref cy, "Speed", $"{playerShip.CurrentSpeed:F1}  /  {playerShip.MaxSpeed:F1} u/s");
            DrawBar (ref cy, "Hull",  _playerHull, hullBarFill, hullBarEmpty);
            DrawBar (ref cy, "Sails", _playerSail, sailBarFill, sailBarEmpty);
        }
        else
        {
            DrawRow(ref cy, "", "— not found —");
        }

        cy += sectionSpacing;

        // ── ENEMY SHIP ────────────────────────────────────────────────────
        DrawHeader(ref cy, "▸  ENEMY SHIP");

        if (enemyShip != null)
        {
            DrawBar(ref cy, "Hull",  _enemyHull, hullBarFill, hullBarEmpty);
            DrawBar(ref cy, "Sails", _enemySail, sailBarFill, sailBarEmpty);
        }
        else
        {
            DrawRow(ref cy, "", "— not found —");
        }
    }

    // =========================================================================
    //  DRAWING HELPERS
    // =========================================================================

    void DrawHeader(ref float cy, string title)
    {
        float x = panelX + padding;
        float w = panelWidth - padding * 2;

        if (textShadow)
            GUI.Label(new Rect(x + shadowOffset, cy + shadowOffset, w, lineHeight), title, _shadowStyle);

        GUI.Label(new Rect(x, cy, w, lineHeight), title, _headerStyle);
        cy += lineHeight + 2;
    }

    void DrawRow(ref float cy, string label, string value)
    {
        float x     = panelX + padding;
        float valX  = x + labelColumnWidth;
        float valW  = panelWidth - padding * 2 - labelColumnWidth;

        if (textShadow)
        {
            GUI.Label(new Rect(x    + shadowOffset, cy + shadowOffset, labelColumnWidth, lineHeight), label, _shadowStyle);
            GUI.Label(new Rect(valX + shadowOffset, cy + shadowOffset, valW,             lineHeight), value, _shadowStyle);
        }

        GUI.Label(new Rect(x,    cy, labelColumnWidth, lineHeight), label, _labelStyle);
        GUI.Label(new Rect(valX, cy, valW,             lineHeight), value, _valueStyle);
        cy += lineHeight + 2;
    }

    void DrawBar(ref float cy, string label, HealthComponent hc, Color fillColor, Color emptyColor)
    {
        float x    = panelX + padding;
        float barX = x + labelColumnWidth;
        float barW = panelWidth - padding * 2 - labelColumnWidth;

        // Label
        if (textShadow)
            GUI.Label(new Rect(x + shadowOffset, cy + shadowOffset, labelColumnWidth, lineHeight), label, _shadowStyle);
        GUI.Label(new Rect(x, cy, labelColumnWidth, lineHeight), label, _labelStyle);

        if (hc != null)
        {
            float fraction = hc.maxHealth > 0f ? Mathf.Clamp01(hc.currentHealth / hc.maxHealth) : 0f;

            // Centre the bar vertically in the line
            float barY = cy + (lineHeight - barHeight) * 0.5f;

            // Background track
            DrawRect(new Rect(barX, barY, barW, barHeight), emptyColor);
            // Filled portion
            DrawRect(new Rect(barX, barY, barW * fraction, barHeight), fillColor);

            // HP numbers — centred on the bar with shadow
            string txt = $"{hc.currentHealth:F0} / {hc.maxHealth:F0}";
            if (textShadow)
                GUI.Label(new Rect(barX + shadowOffset, cy + shadowOffset, barW, lineHeight), txt, _shadowStyle);
            GUI.Label(new Rect(barX, cy, barW, lineHeight), txt, _valueStyle);
        }
        else
        {
            GUI.Label(new Rect(barX, cy, barW, lineHeight), "—", _labelStyle);
        }

        cy += lineHeight + 2;
    }

    static void DrawRect(Rect rect, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // =========================================================================
    //  STYLE SETUP
    // =========================================================================

    void EnsureStyles()
    {
        if (_stylesReady) return;

        // Panel background texture
        if (_panelTex != null) Destroy(_panelTex);
        _panelTex = new Texture2D(1, 1);
        _panelTex.SetPixel(0, 0, panelColor);
        _panelTex.Apply();

        _boxStyle = new GUIStyle(GUI.skin.box);
        _boxStyle.normal.background = _panelTex;

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = headerFontSize,
            alignment = TextAnchor.MiddleLeft,
        };
        _headerStyle.normal.textColor = headerColor;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Normal,
            fontSize  = labelFontSize,
            alignment = TextAnchor.MiddleLeft,
        };
        _labelStyle.normal.textColor = labelColor;

        _valueStyle = new GUIStyle(_labelStyle);
        _valueStyle.normal.textColor = valueColor;

        // Shadow — same size/alignment, always black
        _shadowStyle = new GUIStyle(_labelStyle)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = headerFontSize, // generous so it covers both header and label shadows
        };
        _shadowStyle.normal.textColor = shadowColor;

        _stylesReady = true;
    }

    // =========================================================================
    //  UTILITIES
    // =========================================================================

    static string GearName(GearState gear) => gear switch
    {
        GearState.Idle  => "Idle",
        GearState.Gear1 => "Gear 1",
        GearState.Gear2 => "Gear 2",
        GearState.Gear3 => "Gear 3",
        _               => "?"
    };
}
