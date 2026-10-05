#ifndef TREE_LIGHTING_INCLUDED
#define TREE_LIGHTING_INCLUDED

// Tree lighting, copied from ProcFoliage FrondLighting.hlsl (verified there in HDRP): wrap diffuse + GGX sheen + SH ambient +
// thickness translucency, main directional light + its shadow map only.
// Tradeoff (documented): no deferred GBuffer, no SSR, no diffusion-profile SSS.

// One light's contribution in the tree BSDF: wrap diffuse + GGX sheen +
// (under _TRANSMISSION_ON) backlit translucency, scaled by atten*shadow. Shared by the
// main directional light and every punctual (point/spot) light so they shade identically.
float3 TreeShadeOneLight(float3 N, float3 V, float3 L, float3 lightColor,
                          float atten, float shadow,
                          float3 albedo, float smoothness, float thickness)
{
    float lit = atten * shadow;
    float3 result = float3(0, 0, 0);

    // Wrap diffuse: soft terminator for a leaf mass.
    const float wrap = 0.35;
    float ndl = dot(N, L);
    float diffuse = saturate((ndl + wrap) / ((1.0 + wrap) * (1.0 + wrap)));
    result += albedo * lightColor * (diffuse * lit);

    // GGX-style sheen from mask smoothness (palm waxy vs fern matte is data).
    float rough = max(1.0 - smoothness, 0.04);
    float a2 = rough * rough * rough * rough;
    float3 H = normalize(L + V);
    float ndh = saturate(dot(N, H));
    float d = ndh * ndh * (a2 - 1.0) + 1.0;
    float ggx = a2 / max(3.14159265 * d * d, 1e-4);
    result += lightColor * (ggx * smoothness * 0.06 * saturate(ndl) * lit);

#ifdef _TRANSMISSION_ON
    // Hero effect: backlit leaflets glow. Thin (thickness->1) transmits most.
    float backAmount = pow(saturate(dot(-L, V) * 0.5 + 0.5), _TransmissionScatter);
    float transmit = backAmount * thickness * _TransmissionStrength;
    result += _TransmissionColor.rgb * lightColor * transmit * atten * lerp(0.4, 1.0, shadow);
#endif
    return result;
}

float3 TreeEvaluateLighting(PositionInputs posInput, float3 N, float3 V,
                             float3 albedo, float smoothness, float ao, float thickness)
{
    // Ambient: SH probe so shadowed sides aren't black; AO from mask.
    float3 color = albedo * EvaluateAmbientProbe(N) * ao;

#ifdef TREE_HAS_LIGHTLOOP
    if (_DirectionalLightCount > 0)
    {
        DirectionalLightData light = _DirectionalLightDatas[0];
        float3 L = -light.forward;

        float shadow = 1.0;
        if (light.shadowIndex >= 0)
        {
            HDShadowContext shadowContext = InitShadowContext();
            shadow = GetDirectionalShadowAttenuation(shadowContext,
                posInput.positionSS, posInput.positionWS, N, light.shadowIndex, L);
        }

        color += TreeShadeOneLight(N, V, L, light.color, 1.0, shadow,
                                    albedo, smoothness, thickness);
    }

    // Punctual (point + spot) lights via HDRP's culled per-tile list (spec 2026-07-12).
    // The Unlit forward pass doesn't populate tileCoord (only under _ENABLE_SHADOW_MATTE),
    // so compute it here from screen position — same as ShaderPassForwardUnlit does.
    // shadow = 1.0 here; punctual shadow receiving is added in the next task.
#ifdef USE_FPTL_LIGHTLIST
    {
        posInput.tileCoord = uint2(posInput.positionSS.xy) / GetTileSize();
        uint lightStart, lightCount;
        GetCountAndStart(posInput, LIGHTCATEGORY_PUNCTUAL, lightStart, lightCount);

        HDShadowContext shadowContext = InitShadowContext();   // shared by all punctual lights

        for (uint li = 0; li < lightCount; li++)
        {
            LightData pl = FetchLight(lightStart, li);

            float3 unL = pl.positionRWS - posInput.positionWS;   // sample -> light
            float distSq = max(dot(unL, unL), 1e-6);
            float dist = sqrt(distSq);
            float3 L = unL * rsqrt(distSq);

            // HDRP distance + angle attenuation (core CommonLighting.hlsl):
            //   DistanceWindowing  = saturate(rangeAttenuationBias - Sq(distSq * rangeAttenuationScale))
            //   distance attenuation = (1/distSq) * Sq(DistanceWindowing)
            //   angle (spot)       = Sq(saturate(cosFwd * angleScale + angleOffset))
            float window = saturate(pl.rangeAttenuationBias - Sq(distSq * pl.rangeAttenuationScale));
            float distAtten = (window * window) / distSq;
            float angleAtten = saturate(dot(-pl.forward, L) * pl.angleScale + pl.angleOffset);
            float atten = distAtten * angleAtten * angleAtten;   // point lights: angleScale=0, angleOffset=1 -> 1
            if (atten <= 0.0) continue;

            // Receive punctual shadows (mirrors HDRP LightEvaluation.hlsl call).
            float shadow = 1.0;
            if (pl.shadowIndex >= 0 && pl.shadowDimmer > 0.0)
            {
                shadow = GetPunctualShadowAttenuation(shadowContext, posInput.positionSS,
                    posInput.positionWS, N, pl.shadowIndex, L, dist,
                    pl.lightType == GPULIGHTTYPE_POINT, pl.lightType != GPULIGHTTYPE_PROJECTOR_BOX);
                shadow = lerp(1.0, shadow, pl.shadowDimmer);
            }

            color += TreeShadeOneLight(N, V, L, pl.color, atten, shadow,
                                        albedo, smoothness, thickness);
        }
    }
#endif
#endif // TREE_HAS_LIGHTLOOP

    // Brief 03 step 8: pre-expose the physical-unit radiance, matching HDRP's lit
    // forward convention (ShaderPassForward.hlsl:217 multiplies final lighting by
    // GetCurrentExposureMultiplier()). ShaderPassForwardUnlit only applies
    // GetDeExposureMultiplier() (=1.0) to surface color, so without this the tree
    // renders ~2^14x brighter than HDRP/Lit neighbors at 100000 lux.
    return color * GetCurrentExposureMultiplier();
}

#endif // TREE_LIGHTING_INCLUDED
