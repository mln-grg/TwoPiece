// ═══════════════════════════════════════════════════════════════════════════════
//  CRTFullscreenPass_HDRP.shader
//  HDRP 17.x  ·  Unity 6
// ───────────────────────────────────────────────────────────────────────────────
//
//  Fullscreen post-process shader for the Saturday Morning CRT effect.
//  Driven by SaturdayMorningCRTComponent.cs — do not assign as a regular material.
//
//  Effects applied in the fragment shader (in order):
//    1.  Barrel distortion         → CRT convex glass curve
//    2.  Screen edge mask          → rounded black border
//    3.  Chromatic aberration      → edge-weighted RGB channel separation
//    4.  Scene colour sample       → read the HDRP source frame
//    5.  Scanlines                 → horizontal bright/dark rows with scroll
//    6.  Phosphor triad            → subtle R/G/B column pattern
//    7.  VHS noise                 → additive random static grain
//    8.  Warm analog tone          → amber/yellow phosphor colour shift
//    9.  Master intensity lerp     → blends between original and CRT output
//
// ═══════════════════════════════════════════════════════════════════════════════

Shader "Hidden/Custom/CRTFullscreenPass_HDRP"
{
    // No Properties block needed — all values are set via Material.SetFloat/SetTexture
    // from SaturdayMorningCRTComponent.Render().

    HLSLINCLUDE

    // ── Target and renderer pragmas ─────────────────────────────────────────
    #pragma editor_sync_compilation
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    // ── HDRP includes ────────────────────────────────────────────────────────
    // Common.hlsl   → PI, TWO_PI, frac(), dot(), floor() etc. (Core RP)
    // ShaderVariables.hlsl → _ScreenSize, _Time, s_linear_clamp_sampler etc. (HDRP)
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    // ── Source frame texture (bound by SaturdayMorningCRTComponent) ──────────
    // TEXTURE2D_X is XR-safe (handles single-pass stereo, VR eye indices).
    // For a non-XR project it behaves identically to TEXTURE2D.
    // Confirmed name: "_MainTex" (matches HDRP package sample Colorblindness.cs)
    TEXTURE2D_X(_MainTex);

    // ── Shader parameters (set per-frame by SaturdayMorningCRTComponent) ────
    float _Intensity;
    float _ScanlineIntensity;
    float _ScanlineCount;
    float _ScanlineScrollSpeed;
    float _ChromaticAberration;
    float _BarrelDistortion;
    float _BorderSoftness;
    float _NoiseIntensity;
    float _NoiseSpeed;
    float _NoisePixelSize;
    float _PhosphorIntensity;
    float _WarmTone;

    // ════════════════════════════════════════════════════════════════════════
    //  Vertex shader
    //  Uses the HDRP fullscreen triangle pattern (3 vertices, no mesh needed).
    //  GetFullScreenTriangleVertexPosition / TexCoord are from Common.hlsl.
    // ════════════════════════════════════════════════════════════════════════

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord   : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        // Standard HDRP fullscreen triangle — no mesh, positions generated from vertexID
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord   = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Helper functions
    // ════════════════════════════════════════════════════════════════════════

    // ── 1. Barrel Distortion ─────────────────────────────────────────────────
    //
    // Pushes UV coordinates outward from the centre, bowing the image like the
    // curved glass of a CRT tube. The further from centre, the more the warp.
    //
    //  strength = 0     → no distortion (flat screen)
    //  strength = 0.07  → realistic CRT curvature
    //  strength = 0.20  → exaggerated fisheye
    //
    float2 BarrelDistort(float2 uv, float strength)
    {
        float2 cc = uv - 0.5;               // remap to [-0.5, +0.5] origin at centre
        float  r2 = dot(cc, cc);            // squared radius from centre
        // Scale outward by r² — corners pushed furthest, centre unchanged
        return uv + cc * r2 * strength;
    }

    // ── 2. Screen Edge Mask ──────────────────────────────────────────────────
    //
    // Returns 0 outside the barrel-distorted image area, 1 in the interior.
    // Creates the characteristic rounded black border of an old TV set.
    // softness controls how gradually the edge fades (0 = hard pixel edge).
    //
    float ScreenEdgeMask(float2 uv, float softness)
    {
        // Clamp softness to a small minimum so smoothstep never receives equal
        // edge values (edge0 == edge1 → division by zero → NaN/0 on right/bottom edge).
        softness = max(softness, 0.002);
        float2 inner = smoothstep(0.0, softness, uv) * smoothstep(1.0, 1.0 - softness, uv);
        return inner.x * inner.y;
    }

    // ── 3. Chromatic Aberration ───────────────────────────────────────────────
    //
    // Samples the R, G, B channels at slightly different UV positions, pulling
    // them apart along the vector from screen centre. Mimics the way analogue CRT
    // electron guns converge imperfectly — fringes appear most at the corners.
    //
    // The aberration is ZERO at screen centre and MAXIMUM at the corners.
    // This matches real-world CRT behaviour and HDRP's own CA implementation.
    //
    float3 SampleWithCA(float2 uv, float amount)
    {
        // Direction from screen centre — R pushed outward, B pushed inward
        float2 dir   = (uv - 0.5) * amount;

        // s_linear_clamp_sampler: UV values outside [0,1] clamp to border colour
        // rather than wrapping — critical for barrel-distorted edges
        float r = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv + dir    ).r;
        float g = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv          ).g;
        float b = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv - dir    ).b;

        return float3(r, g, b);
    }

    // ── 4. Scanlines ──────────────────────────────────────────────────────────
    //
    // Returns a [0,1] luminance multiplier creating alternating bright/dark rows.
    // A sine wave produces smooth gradients between scanlines rather than hard
    // edges, which looks more realistic than a step function.
    //
    // The optional scroll replicates the slow "roll" artefact of an unsynced TV.
    //
    float ScanlineMask(float2 uv, float count, float intensity, float scrollSpeed)
    {
        // Map UV.y into scanline rows, offset by time for the rolling effect
        // TWO_PI from Common.hlsl = 2 * PI
        float phase = uv.y * count * 0.5 + _Time.y * scrollSpeed;
        float wave  = sin(phase * TWO_PI) * 0.5 + 0.5;   // [0, 1]

        // 1 = no darkening (wave peak = bright scanline)
        // darkens on wave troughs proportional to intensity
        return 1.0 - wave * intensity;
    }

    // ── 5. Phosphor Triad Pattern ─────────────────────────────────────────────
    //
    // Simulates the vertical R/G/B phosphor stripe pattern of a shadow-mask CRT.
    // Each column of screen pixels is tinted toward R, G, or B in repeating order.
    //
    // Keep intensity very low (0.03–0.08) — it is a subliminal texture.
    // More visible on large close-up objects; barely perceptible on detailed areas.
    //
    float3 PhosphorTriad(float2 uv, float intensity)
    {
        // Which column (mod 3) is this pixel in?
        float col = fmod(floor(uv.x * _ScreenSize.x), 3.0);

        // Boost the matching channel, dim the others
        // The asymmetry (1.15 vs 0.88) keeps perceived brightness even
        float3 tint;
        tint.r = (col < 0.5)             ? 1.15 : 0.88;   // columns 0, 3, 6 …  → RED
        tint.g = (col >= 0.5 && col < 1.5) ? 1.15 : 0.88; // columns 1, 4, 7 …  → GREEN
        tint.b = (col >= 1.5)             ? 1.15 : 0.88;   // columns 2, 5, 8 …  → BLUE

        // Lerp between neutral (1,1,1) and the tinted pattern
        return lerp(float3(1.0, 1.0, 1.0), tint, intensity);
    }

    // ── 6. VHS Noise ─────────────────────────────────────────────────────────
    //
    // Cheap, fast pseudo-random hash. Produces a value in [0, 1).
    // Chunking into pixel blocks (noisePixelSize) gives the coarse "analog static"
    // appearance rather than fine per-pixel photographic grain.
    //
    float Hash21(float2 p)
    {
        // Two-component hash via frac(sin(dot)) — fast and good enough for noise
        p  = frac(p * float2(127.1, 311.7));
        p += dot(p, p + 34.23);
        return frac(p.x * p.y);
    }

    float VHSNoise(float2 uv, float speed, float pixelSize, float noiseIntensity)
    {
        // Convert UV to pixel coordinates, then group into blocks of 'pixelSize'
        float2 p = floor(uv * _ScreenSize.xy / pixelSize);
        // Advance the hash seed each frame at 'speed' rows per second
        p.y     += floor(_Time.y * speed);
        // Remap [0,1] → [-1,+1] for additive (not just brightening) noise
        return (Hash21(p) - 0.5) * 2.0 * noiseIntensity;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Fragment shader — the main CRT pipeline
    // ════════════════════════════════════════════════════════════════════════

    float4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.texcoord;

        // ── Step 1: Barrel distortion ───────────────────────────────────────
        // All subsequent sampling uses distUV so the warp applies to everything.
        float2 distUV = BarrelDistort(uv, _BarrelDistortion);

        // ── Step 2: Screen edge mask ────────────────────────────────────────
        // Only run when barrel distortion or border softness is actually in use.
        // When both are 0 (the default), skip entirely — the function would
        // receive degenerate smoothstep inputs (edge0 == edge1) on certain
        // hardware, producing NaN that collapses the right/bottom edge to black.
        float edgeMask = (_BarrelDistortion > 0.001 || _BorderSoftness > 0.001)
            ? ScreenEdgeMask(distUV, _BorderSoftness)
            : 1.0;

        // ── Step 3: Chromatic aberration strength (edge-weighted) ───────────
        // Distance from screen centre: 0 at centre, ~1 at corners
        float edgeDist  = length(distUV - 0.5) * 2.0;            // [0, ~1.41]
        float caAmount  = _ChromaticAberration * edgeDist * edgeDist; // quadratic falloff

        // ── Step 4: Sample scene colour + chromatic aberration ──────────────
        float3 color = SampleWithCA(distUV, caAmount);

        // ── Step 5: Scanlines ────────────────────────────────────────────────
        float scanMask = ScanlineMask(distUV, _ScanlineCount, _ScanlineIntensity, _ScanlineScrollSpeed);
        color *= scanMask;

        // ── Step 6: Phosphor triad ────────────────────────────────────────────
        color *= PhosphorTriad(distUV, _PhosphorIntensity);

        // ── Step 7: VHS noise (additive) ─────────────────────────────────────
        // Additive noise brightens the image slightly overall; tiny amounts look
        // like analogue grain. Use undistorted UV so noise doesn't warp with barrel.
        float noise = VHSNoise(uv, _NoiseSpeed, _NoisePixelSize, _NoiseIntensity);
        color += noise;

        // ── Step 8: Warm phosphor tone ────────────────────────────────────────
        // Old CRT phosphors ran warm — gently push reds/ambers up, blues down.
        // These are small deltas (never > ±0.08) to stay within the cartoon palette.
        color.r += _WarmTone * 0.07;
        color.g += _WarmTone * 0.02;
        color.b -= _WarmTone * 0.06;

        // ── Step 9: Apply edge mask ───────────────────────────────────────────
        // Multiply to black outside the CRT screen area created by barrel distortion.
        color *= edgeMask;

        // ── Step 10: Clamp and master intensity blend ─────────────────────────
        // Clamp before blend so noise/warm-tone can't push values wildly out of range.
        color = saturate(color);

        // Lerp between the raw source pixel and our full CRT output.
        // _Intensity = 1 → full CRT effect.  _Intensity = 0 → pass-through.
        float3 originalColor = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv).rgb;
        color = lerp(originalColor, color, _Intensity);

        return float4(color, 1.0);
    }

    ENDHLSL

    // ════════════════════════════════════════════════════════════════════════
    //  SubShader
    // ════════════════════════════════════════════════════════════════════════

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "Saturday Morning CRT Pass"

            // Standard fullscreen pass state: no depth write, always pass depth test,
            // no blending (we output the fully composited result), no culling.
            ZWrite Off
            ZTest  Always
            Blend  Off
            Cull   Off

            HLSLPROGRAM
            // HLSLINCLUDE block above is automatically included in all HLSLPROGRAM blocks.
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
