#ifndef TREE_WIND_INCLUDED
#define TREE_WIND_INCLUDED

// Hierarchical wind (spec §6.3), computed from stored centerline data so every pass agrees:
//   1. trunk sway   — whole tree, grows with height²
//   2. branch sway  — each segment about its axis base, amplitude ∝ 1 / current radius
//   3. leaf flutter — added by the leaf shader on top of its twig's sway
// Requires TreeGrowth.hlsl (segments, radius).

float2 TreeWindDir()
{
    return normalize(_TreeWindDirection.xz + float2(1e-4, 0));
}

// Offset (object space) for a point attached to segment 'si' at 'positionOS'.
float3 TreeWindOffset(float3 positionOS, uint si, float year, float time)
{
#ifdef _WIND_ON
    float strength = _TreeWindStrength;
    if (strength <= 0.0) return 0;
    float2 dir = TreeWindDir();
    float t = time * _TreeWindSpeed;

    // 1. Trunk sway: slow, whole tree.
    float h = max(positionOS.y, 0.0);
    float trunkBeat = sin(t * 0.55) + 0.3 * sin(t * 1.31 + 1.7);
    float3 offset = float3(dir.x, 0, dir.y) * (trunkBeat * strength * 0.0025 * h * h);

    // 2. Branch sway about the axis base; thin wood moves more.
    SegmentGpu seg = _Segments[si];
    SegmentGpu axisBase = _Segments[(uint)seg.axisBaseSegment];
    float dist = length(positionOS - axisBase.start);
    float r = max(TreeRadius(si, year), 0.002);
    float stiffness = saturate(0.004 / r);
    float phase = seg.axisPhase * 6.2831853;
    float branchBeat = sin(t * 1.7 + phase) + 0.4 * sin(t * 3.1 + phase * 1.7);
    offset += float3(dir.x, -0.15, dir.y) * (branchBeat * strength * 0.03 * dist * stiffness);
    return offset;
#else
    return 0;
#endif
}

float3 TreeLeafFlutter(float3 normalOS, float phase, float time)
{
#ifdef _WIND_ON
    return normalOS * (sin(time * _TreeFlutterFreq + phase * 43.9822971) * _TreeLeafFlutter);
#else
    return 0;
#endif
}

#endif // TREE_WIND_INCLUDED
