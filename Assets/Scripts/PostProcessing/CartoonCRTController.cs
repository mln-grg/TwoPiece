// ═══════════════════════════════════════════════════════════════════════════════
//  CartoonCRTController.cs
//  Runtime controller for the Saturday Morning Cartoon + CRT post-process stack.
//  HDRP 17.x  ·  Unity 6
// ───────────────────────────────────────────────────────────────────────────────
//
//  Attach this to any active GameObject in the scene.
//  Assign your Global Volume in the Inspector.
//
//  At startup this script creates an INSTANCE of the Volume Profile so that all
//  runtime changes are non-destructive — your saved Profile asset is never modified.
//
//  USAGE EXAMPLES
//  ──────────────
//  // Gradually increase CRT intensity during a cutscene:
//  crtController.AnimateCRTStrength(1f, 2.0f);
//
//  // Instantly snap to a preset:
//  crtController.ApplyPreset(CartoonCRTPreset.SaturdayMorning);
//
//  // Control from another script:
//  crtController.SetSaturation(55f);
//  crtController.SetBloomIntensity(0.5f);
//
// ═══════════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// ─────────────────────────────────────────────────────────────────────────────
//  Preset definitions
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Named visual presets you can switch between at runtime (cutscenes, time of day, etc.).
/// </summary>
public enum CartoonCRTPreset
{
    /// <summary>Bright, saturated, warm — the default look.</summary>
    SaturdayMorning,

    /// <summary>Cooler tone, reduced saturation, stronger scanlines — late night broadcast feel.</summary>
    LateNight,

    /// <summary>Boosted saturation and bloom, reduced CRT noise — party/celebration mode.</summary>
    Celebration,

    /// <summary>Heavy static and noise — simulates poor TV reception.</summary>
    BadSignal,

    /// <summary>CRT disabled, clean cartoon colours only.</summary>
    CleanCartoon,
}

// ─────────────────────────────────────────────────────────────────────────────
//  Controller
// ─────────────────────────────────────────────────────────────────────────────

public class CartoonCRTController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Inspector
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Volume Reference")]
    [Tooltip("Your Global Volume (the one that has the SaturdayMorningCRTComponent override). " +
             "This script will CLONE its profile at runtime so the original asset is never modified.")]
    public Volume globalVolume;

    [Header("Starting Preset")]
    [Tooltip("Which preset to apply immediately on Start().")]
    public CartoonCRTPreset startingPreset = CartoonCRTPreset.SaturdayMorning;

    [Header("Transition Speed")]
    [Tooltip("How quickly values lerp when AnimateCRTStrength / AnimateSaturation etc. are called. " +
             "Higher = snappier transitions.")]
    public float transitionSpeed = 3f;

    // ═══════════════════════════════════════════════════════════════════════
    //  Cached volume component references
    //  Populated in Start() from the INSTANCED (runtime) profile.
    // ═══════════════════════════════════════════════════════════════════════

    SaturdayMorningCRTComponent m_CRT;
    ColorAdjustments            m_ColorAdjust;
    Bloom                       m_Bloom;
    Vignette                    m_Vignette;
    FilmGrain                   m_FilmGrain;
    DepthOfField                m_DepthOfField;

    // ═══════════════════════════════════════════════════════════════════════
    //  Init
    // ═══════════════════════════════════════════════════════════════════════

    void Start()
    {
        if (globalVolume == null)
        {
            Debug.LogError("[CartoonCRT] No Global Volume assigned. " +
                           "Drag your scene's Global Volume into the Inspector.", this);
            return;
        }

        // ── Clone the profile ────────────────────────────────────────────────
        // This is the CRITICAL step: we create a runtime instance of the profile.
        // All subsequent reads and writes go to this instance, NEVER to the saved asset.
        if (globalVolume.sharedProfile != null)
        {
            globalVolume.profile = Instantiate(globalVolume.sharedProfile);
            globalVolume.profile.name = globalVolume.sharedProfile.name + " [Runtime]";
        }
        else
        {
            Debug.LogWarning("[CartoonCRT] Global Volume has no shared profile assigned. " +
                             "Effect components cannot be found.", this);
            return;
        }

        // ── Cache references to each volume component ────────────────────────
        // TryGet returns false (and leaves out null) if the component isn't in the profile.
        // That's fine — all setter methods below guard against null.
        var p = globalVolume.profile;

        if (!p.TryGet(out m_CRT))
            Debug.LogWarning("[CartoonCRT] SaturdayMorningCRTComponent override not found in profile. " +
                             "Add it via the Volume Profile Inspector.");

        if (!p.TryGet(out m_ColorAdjust))
            Debug.LogWarning("[CartoonCRT] ColorAdjustments override not found in profile.");

        if (!p.TryGet(out m_Bloom))
            Debug.LogWarning("[CartoonCRT] Bloom override not found in profile.");

        if (!p.TryGet(out m_Vignette))
            Debug.LogWarning("[CartoonCRT] Vignette override not found in profile.");

        if (!p.TryGet(out m_FilmGrain))
            Debug.LogWarning("[CartoonCRT] FilmGrain override not found in profile. (Optional)");

        if (!p.TryGet(out m_DepthOfField))
            Debug.LogWarning("[CartoonCRT] DepthOfField override not found in profile. (Optional)");

        // ── Apply the starting preset ────────────────────────────────────────
        ApplyPreset(startingPreset, instant: true);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Individual setters  — use these from other scripts for fine control
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Master CRT overlay intensity. 0 = off, 1 = full effect.</summary>
    public void SetCRTStrength(float value)
    {
        if (m_CRT == null) return;
        m_CRT.intensity.value = Mathf.Clamp01(value);
    }

    /// <summary>Scanline darkness. 0 = none, 0.2 = classic SD CRT.</summary>
    public void SetScanlineIntensity(float value)
    {
        if (m_CRT == null) return;
        m_CRT.scanlineIntensity.value = Mathf.Clamp01(value);
    }

    /// <summary>VHS static noise. 0 = clean, 0.1 = noticeable grain, 0.3 = heavy static.</summary>
    public void SetNoiseIntensity(float value)
    {
        if (m_CRT == null) return;
        m_CRT.noiseIntensity.value = Mathf.Clamp(value, 0f, 0.5f);
    }

    /// <summary>Screen curvature from barrel distortion. 0 = flat, 0.07 = realistic CRT.</summary>
    public void SetBarrelDistortion(float value)
    {
        if (m_CRT == null) return;
        m_CRT.barrelDistortion.value = Mathf.Clamp(value, 0f, 0.3f);
    }

    /// <summary>Warm amber phosphor colour toning. 0 = neutral, 0.3 = warm nostalgic.</summary>
    public void SetWarmTone(float value)
    {
        if (m_CRT == null) return;
        m_CRT.warmTone.value = Mathf.Clamp01(value);
    }

    // ── Built-in HDRP effects ───────────────────────────────────────────────

    /// <summary>
    /// Colour saturation. 0 = desaturated, 40 = vivid cartoon, 80 = oversaturated.
    /// Range matches HDRP ColorAdjustments.saturation: -100 to +100.
    /// </summary>
    public void SetSaturation(float value)
    {
        if (m_ColorAdjust == null) return;
        m_ColorAdjust.saturation.value = Mathf.Clamp(value, -100f, 100f);
    }

    /// <summary>Post exposure offset in EV. 0 = neutral, 0.2 = slightly brighter.</summary>
    public void SetExposure(float ev)
    {
        if (m_ColorAdjust == null) return;
        m_ColorAdjust.postExposure.value = ev;
    }

    /// <summary>
    /// Bloom glow intensity. 0.2–0.4 = phosphor glow. Above 0.8 = very glowy.
    /// </summary>
    public void SetBloomIntensity(float value)
    {
        if (m_Bloom == null) return;
        m_Bloom.intensity.value = Mathf.Max(0f, value);
    }

    /// <summary>Vignette strength. 0 = none, 0.3 = subtle frame, 0.6 = heavy.</summary>
    public void SetVignetteIntensity(float value)
    {
        if (m_Vignette == null) return;
        m_Vignette.intensity.value = Mathf.Clamp01(value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Animated transitions  — non-blocking coroutines
    // ═══════════════════════════════════════════════════════════════════════

    Coroutine m_CRTStrengthCoroutine;

    /// <summary>
    /// Smoothly animate the master CRT intensity to <paramref name="target"/> over
    /// <paramref name="duration"/> seconds. Cancels any in-progress animation.
    /// </summary>
    public void AnimateCRTStrength(float target, float duration)
    {
        if (m_CRTStrengthCoroutine != null)
            StopCoroutine(m_CRTStrengthCoroutine);
        m_CRTStrengthCoroutine = StartCoroutine(AnimateFloat(
            () => m_CRT?.intensity.value ?? 0f,
            v  => { if (m_CRT != null) m_CRT.intensity.value = v; },
            target, duration));
    }

    /// <summary>
    /// Smoothly animate the colour saturation to <paramref name="target"/> over
    /// <paramref name="duration"/> seconds.
    /// </summary>
    public void AnimateSaturation(float target, float duration)
    {
        StartCoroutine(AnimateFloat(
            () => m_ColorAdjust?.saturation.value ?? 0f,
            v  => { if (m_ColorAdjust != null) m_ColorAdjust.saturation.value = v; },
            target, duration));
    }

    // Generic lerp coroutine — takes a getter and setter lambda so it works for any float field
    IEnumerator AnimateFloat(
        System.Func<float>   getter,
        System.Action<float> setter,
        float target,
        float duration)
    {
        float elapsed = 0f;
        float start   = getter();

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / duration);
            // Smooth-step easing for a more cinematic feel than linear
            float smooth = t * t * (3f - 2f * t);
            setter(Mathf.Lerp(start, target, smooth));
            yield return null;
        }

        setter(target); // guarantee exact value at end
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Preset system
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply a named visual preset. If <paramref name="instant"/> is false (default),
    /// values lerp over <see cref="transitionSpeed"/> seconds rather than snapping.
    /// </summary>
    public void ApplyPreset(CartoonCRTPreset preset, bool instant = false)
    {
        switch (preset)
        {
            case CartoonCRTPreset.SaturdayMorning:
                ApplySaturdayMorning(instant);   break;
            case CartoonCRTPreset.LateNight:
                ApplyLateNight(instant);         break;
            case CartoonCRTPreset.Celebration:
                ApplyCelebration(instant);       break;
            case CartoonCRTPreset.BadSignal:
                ApplyBadSignal(instant);         break;
            case CartoonCRTPreset.CleanCartoon:
                ApplyCleanCartoon(instant);      break;
        }
    }

    // ── Preset implementations ──────────────────────────────────────────────

    // The helper below handles instant-set vs. animated transition cleanly
    void Apply(System.Action<float> setter, float target, bool instant)
    {
        if (instant)
            setter(target);
        else
            StartCoroutine(AnimateFloat(
                () => 0f /* unused for instant=false start value */,
                setter,
                target,
                1f / transitionSpeed));
    }

    void ApplySaturdayMorning(bool instant)
    {
        //  Full CRT + warm palette + cartoon saturation — the hero look
        SetInstantOrAnimate(ref m_CRT,           c => c.intensity.value,        v => { if (m_CRT != null) m_CRT.intensity.value        = v; }, 1.00f, instant);
        SetInstantOrAnimate(ref m_CRT,           c => c.scanlineIntensity.value, v => { if (m_CRT != null) m_CRT.scanlineIntensity.value = v; }, 0.20f, instant);
        SetInstantOrAnimate(ref m_CRT,           c => c.noiseIntensity.value,    v => { if (m_CRT != null) m_CRT.noiseIntensity.value    = v; }, 0.03f, instant);
        SetInstantOrAnimate(ref m_CRT,           c => c.barrelDistortion.value,  v => { if (m_CRT != null) m_CRT.barrelDistortion.value  = v; }, 0f,    instant);
        SetInstantOrAnimate(ref m_CRT,           c => c.warmTone.value,          v => { if (m_CRT != null) m_CRT.warmTone.value          = v; }, 0f,    instant);

        if (m_ColorAdjust != null)
        {
            if (instant) { m_ColorAdjust.saturation.value = 42f; m_ColorAdjust.postExposure.value = 0.20f; m_ColorAdjust.contrast.value = 22f; }
            else { StartCoroutine(AnimateFloat(() => m_ColorAdjust.saturation.value,   v => m_ColorAdjust.saturation.value   = v, 42f,  1f / transitionSpeed)); }
        }

        if (m_Bloom != null)
        {
            if (instant) m_Bloom.intensity.value = 0.38f;
            else StartCoroutine(AnimateFloat(() => m_Bloom.intensity.value, v => m_Bloom.intensity.value = v, 0.38f, 1f / transitionSpeed));
        }

        if (m_Vignette != null)
        {
            if (instant) m_Vignette.intensity.value = 0.28f;
        }
    }

    void ApplyLateNight(bool instant)
    {
        //  Cooler, muted, heavier scanlines — late-night TV broadcast feel
        if (m_CRT != null && instant)
        {
            m_CRT.intensity.value        = 1.00f;
            m_CRT.scanlineIntensity.value = 0.32f;
            m_CRT.noiseIntensity.value    = 0.06f;
            m_CRT.barrelDistortion.value  = 0.09f;
            m_CRT.warmTone.value          = 0.04f;  // cold, not warm
        }

        if (m_ColorAdjust != null && instant)
        {
            m_ColorAdjust.saturation.value  = 18f;
            m_ColorAdjust.postExposure.value = -0.15f;
            m_ColorAdjust.contrast.value     = 28f;
        }

        if (m_Bloom != null && instant) m_Bloom.intensity.value = 0.20f;
        if (m_Vignette != null && instant) m_Vignette.intensity.value = 0.42f;
    }

    void ApplyCelebration(bool instant)
    {
        //  Everything brighter and more colourful — celebration / win-state pop
        if (m_CRT != null && instant)
        {
            m_CRT.intensity.value        = 0.60f;   // softer CRT — let the colours sing
            m_CRT.scanlineIntensity.value = 0.10f;
            m_CRT.noiseIntensity.value    = 0.01f;
            m_CRT.barrelDistortion.value  = 0.05f;
            m_CRT.warmTone.value          = 0.30f;
        }

        if (m_ColorAdjust != null && instant)
        {
            m_ColorAdjust.saturation.value  = 65f;
            m_ColorAdjust.postExposure.value = 0.35f;
            m_ColorAdjust.contrast.value     = 15f;
        }

        if (m_Bloom != null && instant) m_Bloom.intensity.value = 0.70f;  // big glow!
        if (m_Vignette != null && instant) m_Vignette.intensity.value = 0.15f;
    }

    void ApplyBadSignal(bool instant)
    {
        //  Heavy static and distortion — poor TV reception / interference
        if (m_CRT != null && instant)
        {
            m_CRT.intensity.value        = 1.00f;
            m_CRT.scanlineIntensity.value = 0.45f;
            m_CRT.noiseIntensity.value    = 0.22f;  // lots of snow
            m_CRT.noiseSpeed.value        = 30f;
            m_CRT.barrelDistortion.value  = 0.14f;
            m_CRT.chromaticAberration.value = 0.028f;
            m_CRT.warmTone.value          = 0.05f;
        }

        if (m_ColorAdjust != null && instant)
        {
            m_ColorAdjust.saturation.value  = 10f;
            m_ColorAdjust.postExposure.value = -0.3f;
        }

        if (m_Bloom != null && instant) m_Bloom.intensity.value = 0.15f;
        if (m_Vignette != null && instant) m_Vignette.intensity.value = 0.50f;
    }

    void ApplyCleanCartoon(bool instant)
    {
        //  CRT effects off — pure vivid cartoon palette, no TV overlay
        if (m_CRT != null && instant) m_CRT.intensity.value = 0f;

        if (m_ColorAdjust != null && instant)
        {
            m_ColorAdjust.saturation.value  = 50f;
            m_ColorAdjust.postExposure.value = 0.25f;
            m_ColorAdjust.contrast.value     = 18f;
        }

        if (m_Bloom != null && instant) m_Bloom.intensity.value = 0.25f;
        if (m_Vignette != null && instant) m_Vignette.intensity.value = 0.20f;
    }

    // Small helper — avoids repeating the instant vs. animated conditional everywhere
    void SetInstantOrAnimate<T>(
        ref T                       component,
        System.Func<T, float>       getter,
        System.Action<float>        setter,
        float                       target,
        bool                        instant) where T : VolumeComponent
    {
        if (component == null) return;
        if (instant)
        {
            setter(target);
        }
        else
        {
            // C# does not allow capturing a ref parameter inside a lambda.
            // Copy to a plain local variable first — the value is a reference type
            // (VolumeComponent) so this is a reference copy, not a deep clone.
            T captured = component;
            StartCoroutine(AnimateFloat(
                () => getter(captured), setter, target, 1f / transitionSpeed));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Cleanup — destroy the runtime profile clone when this object is destroyed
    // ═══════════════════════════════════════════════════════════════════════

    void OnDestroy()
    {
        // If we created a runtime profile instance, destroy it to avoid memory leaks.
        // The original sharedProfile is left untouched.
        if (globalVolume != null && globalVolume.profile != null
            && globalVolume.profile.name.EndsWith("[Runtime]"))
        {
            Destroy(globalVolume.profile);
        }
    }
}
