#ifndef TREE_GROWTH_INCLUDED
#define TREE_GROWTH_INCLUDED

// GPU growth reveal. Mirrors Core/Meshing/GrowthReveal.cs line for line — change both together.
// Struct layouts mirror Core/Meshing/TreeBakeData.cs (SegmentGpu 48 B, LeafGpu 64 B).

struct SegmentGpu
{
    float3 start;
    float  birth;
    float3 end;
    float  growDuration;
    float  death;
    float  axisBaseSegment;
    float  axisPhase;
    float  axisOrder;
};

struct LeafGpu
{
    float3 position;
    float  size;
    float4 rotation;   // xyzw
    float  birth;
    float  death;
    float  segment;
    float  tint;
    float  phase;
    float  flush;
    float  pad0;
    float  pad1;
};

StructuredBuffer<SegmentGpu> _Segments;
StructuredBuffer<float>      _RadiusKeys;    // 16 per segment, on _KeyYears
StructuredBuffer<LeafGpu>    _Leaves;        // sorted by birth
StructuredBuffer<uint>       _LiveLeaves;    // slot -> leaf index

#define TREE_KEY_COUNT 16
#define TREE_LEAF_DROP_YEARS 0.15

// HDRP's motion-vector pass evaluates ApplyMeshModification twice: once with _TimeParameters
// (current) and once with _LastTimeParameters (previous). Use last frame's year for the latter.
float TreeYearFor(float3 timeParameters)
{
#if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_MOTION_VECTORS)
    if (timeParameters.x != _TimeParameters.x) return _PrevTreeYear;
#endif
    return _TreeYear;
}

float TreeRadius(uint s, float year)
{
    uint o = s * TREE_KEY_COUNT;
    if (year <= _KeyYears[0]) return _RadiusKeys[o];
    [loop] for (uint k = 1; k < TREE_KEY_COUNT; k++)
    {
        if (year <= _KeyYears[k])
        {
            float t = (year - _KeyYears[k - 1]) / max(1e-6, _KeyYears[k] - _KeyYears[k - 1]);
            return lerp(_RadiusKeys[o + k - 1], _RadiusKeys[o + k], t);
        }
    }
    return _RadiusKeys[o + TREE_KEY_COUNT - 1];
}

// Revealed bark vertex: on its segment's centerline up to the growth front, pushed out radially
// by the current radius. Returns the radius used (0 when collapsed) through 'radius'.
float3 TreeBarkPosition(uint si, float fraction, float radialScale, float3 radial, float year, out float radius)
{
    SegmentGpu seg = _Segments[si];
    radius = 0.0;
    if (year <= seg.birth) return seg.start;                       // not yet born: collapsed

    float progress = saturate((year - seg.birth) / seg.growDuration);
    float t = fraction < progress ? fraction : progress;
    float3 c = lerp(seg.start, seg.end, t);

    float r = TreeRadius(si, year) * radialScale;
    if (fraction > progress) r = 0.0;                              // growing tip: a point
    if (year > seg.death) r *= 1.0 - saturate((year - seg.death) / _DeathFadeYears);

    radius = r;
    return c + radial * r;
}

float TreeSmooth01(float t) { t = saturate(t); return t * t * (3.0 - 2.0 * t); }

float TreeLeafScale(LeafGpu l, float year)
{
    if (year <= l.birth) return 0.0;
    float unfold = TreeSmooth01((year - l.birth) / l.flush);
    float drop = 1.0 - TreeSmooth01((year - l.death) / TREE_LEAF_DROP_YEARS);
    return unfold * drop;
}

float3 TreeRotate(float4 q, float3 v)
{
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

#endif // TREE_GROWTH_INCLUDED
