// ═══════════════════════════════════════════════════════════════════════════════
//  SaturdayMorningCRTComponent.cs
//  HDRP 17.x Custom Post Process Volume Component  ·  Unity 6
// ───────────────────────────────────────────────────────────────────────────────
//
//  Applies a stylised CRT TV overlay on top of all other HDRP post-processing.
//  The effect stacks on top of Color Grading, Bloom, etc. — it IS the TV screen.
//
//  Effects (in application order, all individually toggleable via intensity):
//    1. Barrel distortion    — curved convex CRT glass
//    2. Chromatic aberration — RGB channel bleed, strongest at screen edges
//    3. Scanlines            — alternating bright/dark horizontal rows + scroll
//    4. Phosphor triad       — subtle R/G/B column pattern (shadow-mask CRT)
//    5. VHS noise/static     — chunky additive grain
//    6. Warm phosphor tone   — amber colour temperature shift
//    7. Screen edge mask     — black rounded border from barrel distortion
//
//  SETUP
//  ─────
//  1. This script lives on a Volume Profile. Do NOT add it to a GameObject directly.
//     Instead: open your Global Volume's Profile, click Add Override, search for
//     "Saturday Morning CRT" and add it.
//
//  2. Register this class in HDRP Global Settings:
//       Project Settings → Graphics → HDRP Global Settings
//       → Custom Post Process Orders → "After Post Process" list
//       Click + and select "SaturdayMorningCRTComponent"
//     Without this step the effect will be compiled but silently not applied.
//
//  3. The shader "Hidden/Custom/CRTFullscreenPass_HDRP" must be in your project.
//     It lives at:  Assets/Shaders/CRTFullscreenPass_HDRP.shader
//
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[Serializable]
[VolumeComponentMenu("Post-processing/Custom/Saturday Morning CRT")]
public sealed class SaturdayMorningCRTComponent : CustomPostProcessVolumeComponent, IPostProcessComponent
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Volume Parameters — all exposed in the Inspector on the Volume Profile
    // ═══════════════════════════════════════════════════════════════════════

    [Header("━━  Master  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("Master blend weight for the entire CRT overlay. " +
             "0 = invisible, 1 = full strength. " +
             "Lerp this at runtime to fade the TV effect in/out.")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 1f);

    // ── Scanlines ────────────────────────────────────────────────────────

    [Header("━━  Scanlines  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("How dark the scanline troughs are. " +
             "0 = no scanlines. 0.15–0.25 is a classic SD CRT look. " +
             "Above 0.4 starts looking like a screen-door filter.")]
    public ClampedFloatParameter scanlineIntensity = new ClampedFloatParameter(0.20f, 0f, 1f);

    [Tooltip("Number of horizontal scanline rows. " +
             "480 = classic NTSC SD. 576 = PAL. 240 = extra chunky retro. " +
             "Higher values = finer, less visible lines at normal view distance.")]
    public ClampedFloatParameter scanlineCount = new ClampedFloatParameter(480f, 60f, 2160f);

    [Tooltip("How fast the scanline pattern slowly drifts downward — replicates the " +
             "subtle roll artefact on old analogue TV sets. " +
             "0 = perfectly static. 0.3–0.6 = gentle drift.")]
    public ClampedFloatParameter scanlineScrollSpeed = new ClampedFloatParameter(0.35f, 0f, 5f);

    // ── Chromatic Aberration / RGB Bleed ─────────────────────────────────

    [Header("━━  Chromatic Aberration (Edge RGB Bleed)  ━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("How far the R and B channels are pulled apart at screen edges. " +
             "0 = none. 0.008 = subtle fringe. 0.02 = noticeable CRT bleed. " +
             "Effect is zero at screen centre and maximum at the corners.")]
    public ClampedFloatParameter chromaticAberration = new ClampedFloatParameter(0.010f, 0f, 0.05f);

    // ── Barrel Distortion ─────────────────────────────────────────────────

    [Header("━━  Barrel Distortion (Screen Curvature)  ━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("Bows the image outward from the centre, simulating the convex glass " +
             "of a CRT tube. 0 = flat. 0.06–0.10 = realistic CRT curve. " +
             "Above 0.18 becomes exaggerated / fisheye.")]
    public ClampedFloatParameter barrelDistortion = new ClampedFloatParameter(0f, 0f, 0.3f);

    [Tooltip("Softness of the black border that appears at the screen edges after " +
             "barrel distortion. 0 = hard pixel edge. 0.03–0.06 = soft vignette border.")]
    public ClampedFloatParameter borderSoftness = new ClampedFloatParameter(0f, 0f, 0.15f);

    // ── VHS Noise ────────────────────────────────────────────────────────

    [Header("━━  VHS Noise / Static  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("Intensity of the additive random noise (VHS static grain). " +
             "0 = none. 0.03 = subtle. 0.08 = noticeable. 0.2 = heavy static.")]
    public ClampedFloatParameter noiseIntensity = new ClampedFloatParameter(0.035f, 0f, 0.5f);

    [Tooltip("How fast the noise pattern changes each frame. " +
             "2 = slow swimming grain. 15 = fast flickering static. 30 = blizzard.")]
    public ClampedFloatParameter noiseSpeed = new ClampedFloatParameter(18f, 0f, 60f);

    [Tooltip("Size of each noise 'pixel' cluster in screen pixels. " +
             "1 = per-pixel grain (fine film grain). 2–3 = chunky VHS static. " +
             "4+ = coarse blocky noise.")]
    public ClampedFloatParameter noisePixelSize = new ClampedFloatParameter(2f, 1f, 8f);

    // ── Phosphor Grid ─────────────────────────────────────────────────────

    [Header("━━  Phosphor Triad Pattern  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("Intensity of the R/G/B phosphor column pattern (shadow-mask CRT). " +
             "Keep very low: 0.03–0.08. Mostly subliminal texture at normal distance. " +
             "Set to 0 for cleaner cartoon look.")]
    public ClampedFloatParameter phosphorIntensity = new ClampedFloatParameter(0.04f, 0f, 0.4f);

    // ── Warm Tone ────────────────────────────────────────────────────────

    [Header("━━  Warm Analog Tone  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("Shifts the image toward warm amber/yellow — like old phosphor or incandescent " +
             "TV sets from the 80s. 0 = neutral. 0.3 = warm nostalgia. 1 = very orange/amber.")]
    public ClampedFloatParameter warmTone = new ClampedFloatParameter(0f, 0f, 1f);

    // ═══════════════════════════════════════════════════════════════════════
    //  Internals
    // ═══════════════════════════════════════════════════════════════════════

    // Name must exactly match the "Name" line inside CRTFullscreenPass_HDRP.shader
    const string k_ShaderName = "Hidden/Custom/CRTFullscreenPass_HDRP";

    Material m_Material;

    // Cache property IDs once to avoid per-frame string hashing
    static class ShaderIDs
    {
        // Source texture (HDRP example uses _MainTex — confirmed from HDRP package samples)
        internal static readonly int MainTex               = Shader.PropertyToID("_MainTex");

        internal static readonly int Intensity             = Shader.PropertyToID("_Intensity");
        internal static readonly int ScanlineIntensity     = Shader.PropertyToID("_ScanlineIntensity");
        internal static readonly int ScanlineCount         = Shader.PropertyToID("_ScanlineCount");
        internal static readonly int ScanlineScrollSpeed   = Shader.PropertyToID("_ScanlineScrollSpeed");
        internal static readonly int ChromaticAberration   = Shader.PropertyToID("_ChromaticAberration");
        internal static readonly int BarrelDistortion      = Shader.PropertyToID("_BarrelDistortion");
        internal static readonly int BorderSoftness        = Shader.PropertyToID("_BorderSoftness");
        internal static readonly int NoiseIntensity        = Shader.PropertyToID("_NoiseIntensity");
        internal static readonly int NoiseSpeed            = Shader.PropertyToID("_NoiseSpeed");
        internal static readonly int NoisePixelSize        = Shader.PropertyToID("_NoisePixelSize");
        internal static readonly int PhosphorIntensity     = Shader.PropertyToID("_PhosphorIntensity");
        internal static readonly int WarmTone              = Shader.PropertyToID("_WarmTone");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  IPostProcessComponent
    // ═══════════════════════════════════════════════════════════════════════

    public bool IsActive()
    {
        // Bail out early if there's nothing to render — saves a draw call
        return m_Material != null
            && intensity.value > 0.001f
            && (scanlineIntensity.value > 0.001f
                || chromaticAberration.value > 0.001f
                || barrelDistortion.value > 0.001f
                || noiseIntensity.value > 0.001f
                || phosphorIntensity.value > 0.001f
                || warmTone.value > 0.001f);
    }

    // Run after ALL built-in HDRP post-processing so we sit on top like a TV overlay.
    // AfterPostProcess = after Bloom, Colour Grading, DoF, CA, etc. have all run.
    public override CustomPostProcessInjectionPoint injectionPoint
        => CustomPostProcessInjectionPoint.AfterPostProcess;

    // ═══════════════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═══════════════════════════════════════════════════════════════════════

    public override void Setup()
    {
        var shader = Shader.Find(k_ShaderName);

        if (shader == null)
        {
            Debug.LogError(
                $"[SaturdayMorningCRT] Cannot find shader '{k_ShaderName}'.\n" +
                "Make sure CRTFullscreenPass_HDRP.shader is in your project (not excluded from build).\n" +
                "Check its first line reads: Shader \"Hidden/Custom/CRTFullscreenPass_HDRP\"",
                this);
            return;
        }

        // CoreUtils.CreateEngineMaterial correctly sets up the material with
        // the HDRP-compatible render queue and keywords
        m_Material = CoreUtils.CreateEngineMaterial(shader);
    }

    public override void Render(
        CommandBuffer cmd,
        HDCamera      camera,
        RTHandle      source,
        RTHandle      destination)
    {
        if (m_Material == null) return;

        // Bind the source frame (this frame's rendered image before our pass)
        // HDRP confirmed convention: _MainTex (from HDRP package sample Colorblindness.cs)
        m_Material.SetTexture(ShaderIDs.MainTex,             source);

        // Push all volume parameters to the shader
        m_Material.SetFloat(ShaderIDs.Intensity,             intensity.value);
        m_Material.SetFloat(ShaderIDs.ScanlineIntensity,     scanlineIntensity.value);
        m_Material.SetFloat(ShaderIDs.ScanlineCount,         scanlineCount.value);
        m_Material.SetFloat(ShaderIDs.ScanlineScrollSpeed,   scanlineScrollSpeed.value);
        m_Material.SetFloat(ShaderIDs.ChromaticAberration,   chromaticAberration.value);
        m_Material.SetFloat(ShaderIDs.BarrelDistortion,      barrelDistortion.value);
        m_Material.SetFloat(ShaderIDs.BorderSoftness,        borderSoftness.value);
        m_Material.SetFloat(ShaderIDs.NoiseIntensity,        noiseIntensity.value);
        m_Material.SetFloat(ShaderIDs.NoiseSpeed,            noiseSpeed.value);
        m_Material.SetFloat(ShaderIDs.NoisePixelSize,        noisePixelSize.value);
        m_Material.SetFloat(ShaderIDs.PhosphorIntensity,     phosphorIntensity.value);
        m_Material.SetFloat(ShaderIDs.WarmTone,              warmTone.value);

        // Blit the processed image to the destination buffer using our shader
        HDUtils.DrawFullScreen(cmd, m_Material, destination, shaderPassId: 0);
    }

    public override void Cleanup()
    {
        // CoreUtils.Destroy safely handles null and calls Destroy/DestroyImmediate correctly
        CoreUtils.Destroy(m_Material);
        m_Material = null;
    }
}
