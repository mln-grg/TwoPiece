using UnityEngine;

/// <summary>
/// Screen-space HUD rendered during FreeAimMode.
/// Shows a centred crosshair, the active sub-mode label, and a fire-rate bar for Full Auto.
/// Add this component to the player ship alongside PlayerShipInput and wire the reference.
/// </summary>
public class FreeAimHUD : MonoBehaviour
{
    [Header("Crosshair Colours")]
    public Color singleShotColor = new Color(0.85f, 0.95f, 1f,   1f);
    public Color fullAutoColor   = new Color(1f,    0.55f, 0.15f, 1f);

    [Header("Crosshair Shape")]
    [Tooltip("Half-length of each arm in pixels")]
    public float armLength    = 18f;
    [Tooltip("Gap between screen centre and arm start at rest")]
    public float centreGap    =  5f;
    [Tooltip("Arm thickness in pixels")]
    public float armThickness =  2f;
    [Tooltip("Gap multiplier when Full-Auto just starts firing (crosshair is open/spread). " +
             "Compresses down to centreGap as fire rate reaches maximum.")]
    public float spinUpGapMult = 3f;

    [Header("Sub-mode Label")]
    public int   labelFontSize = 14;
    public float labelOffsetY  = 36f;   // px below crosshair centre

    [Header("Rate Bar (Full Auto)")]
    [Tooltip("Bar is always visible while Full-Auto is active — shows current fire rate 0→max")]
    public float barWidth   = 100f;
    public float barHeight  =   4f;
    public float barOffsetY =  52f;   // px below crosshair centre

    // ── runtime state ─────────────────────────────────────────────────────────
    bool           _visible;
    FreeAimSubMode _subMode;

    /// <summary>0 = slowest rate (just started), 1 = maximum fire rate.</summary>
    float _accelProgress;
    bool  _firing;   // true while Full-Auto LMB is held

    GUIStyle  _labelStyle;
    Texture2D _whiteTex;

    // ── public API ────────────────────────────────────────────────────────────

    public void Show(FreeAimSubMode subMode)
    {
        _visible       = true;
        _subMode       = subMode;
        _accelProgress = 0f;
        _firing        = false;
    }

    public void Hide()
    {
        _visible = false;
        _firing  = false;
    }

    public void UpdateSubMode(FreeAimSubMode subMode)
    {
        _subMode       = subMode;
        _accelProgress = 0f;
        _firing        = false;
    }

    /// <summary>
    /// Called every frame while Full-Auto LMB is held.
    /// <paramref name="progress"/> 0–1 maps slow→max fire rate.
    /// </summary>
    public void SetAutoProgress(float progress, bool firing)
    {
        _accelProgress = Mathf.Clamp01(progress);
        _firing        = firing;
    }

    // ── rendering ─────────────────────────────────────────────────────────────

    void OnGUI()
    {
        if (!_visible) return;

        EnsureResources();

        float cx = Screen.width  * 0.5f;
        float cy = Screen.height * 0.5f;

        Color armColor = _subMode == FreeAimSubMode.SingleShot ? singleShotColor : fullAutoColor;

        // Full-Auto: crosshair starts open (spread) and compresses to tight as rate climbs.
        // Single-Shot: crosshair stays at the default centreGap.
        float dynamicGap = centreGap;
        if (_subMode == FreeAimSubMode.FullyAutomatic && _firing)
            dynamicGap = Mathf.Lerp(centreGap * spinUpGapMult, centreGap, _accelProgress);

        GUI.color = armColor;

        // Left arm
        DrawRect(cx - dynamicGap - armLength, cy - armThickness * 0.5f, armLength,    armThickness);
        // Right arm
        DrawRect(cx + dynamicGap,             cy - armThickness * 0.5f, armLength,    armThickness);
        // Top arm
        DrawRect(cx - armThickness * 0.5f, cy - dynamicGap - armLength, armThickness, armLength);
        // Bottom arm
        DrawRect(cx - armThickness * 0.5f, cy + dynamicGap,             armThickness, armLength);

        // Centre dot
        float dotR = armThickness;
        DrawRect(cx - dotR, cy - dotR, dotR * 2f, dotR * 2f);

        // Sub-mode label
        _labelStyle.normal.textColor = armColor;
        string modeText = _subMode == FreeAimSubMode.SingleShot
            ? "[ SINGLE SHOT ]"
            : "[ FULL AUTO ]";
        GUI.Label(new Rect(cx - 100f, cy + labelOffsetY, 200f, 24f), modeText, _labelStyle);

        // Rate bar — visible whenever Full-Auto is actively firing
        if (_subMode == FreeAimSubMode.FullyAutomatic && _firing)
        {
            float bx = cx - barWidth * 0.5f;
            float by = cy + barOffsetY;

            // Dark background track
            GUI.color = new Color(0f, 0f, 0f, 0.40f);
            DrawRect(bx, by, barWidth, barHeight);

            // Fill — brighter/more saturated as rate climbs
            Color fill = Color.Lerp(new Color(1f, 0.3f, 0f, 0.7f), fullAutoColor, _accelProgress);
            GUI.color = fill;
            DrawRect(bx, by, barWidth * _accelProgress, barHeight);
        }

        GUI.color = Color.white;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    void DrawRect(float x, float y, float w, float h)
        => GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);

    void EnsureResources()
    {
        if (_whiteTex == null)
        {
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = labelFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }
    }
}
