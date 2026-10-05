using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Nib.ProcTree.Core.Meshing
{
    /// <summary>
    /// GPU record of one segment (48 B). Deaths are finite (<see cref="TreeBaker.Never"/>) so the
    /// shader never sees infinities. Layout must match <c>SegmentGpu</c> in TreeGrowth.hlsl.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SegmentGpu
    {
        /// <summary>Base point (m).</summary>
        public Vector3 start;
        /// <summary>Year extension starts.</summary>
        public float birth;
        /// <summary>Fully extended tip (m).</summary>
        public Vector3 end;
        /// <summary>Years to extend.</summary>
        public float growDuration;
        /// <summary>Year the wood died, or <see cref="TreeBaker.Never"/>.</summary>
        public float death;
        /// <summary>Axis base point index into segments (wind pivot) as float.</summary>
        public float axisBaseSegment;
        /// <summary>Axis wind phase in [0,1).</summary>
        public float axisPhase;
        /// <summary>Axis branching order.</summary>
        public float axisOrder;
    }

    /// <summary>GPU record of one leaf (64 B). Layout must match <c>LeafGpu</c> in TreeGrowth.hlsl.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LeafGpu
    {
        /// <summary>Attachment point (m).</summary>
        public Vector3 position;
        /// <summary>Blade length (m).</summary>
        public float size;
        /// <summary>Orientation (x,y,z,w).</summary>
        public Vector4 rotation;
        /// <summary>Year it starts unfolding.</summary>
        public float birth;
        /// <summary>Year it drops.</summary>
        public float death;
        /// <summary>Parent segment index as float (wind follows the twig).</summary>
        public float segment;
        /// <summary>Colour jitter [-1,1].</summary>
        public float tint;
        /// <summary>Wind phase [0,1).</summary>
        public float phase;
        /// <summary>Years to unfold.</summary>
        public float flush;
        float _pad0, _pad1;
    }

    /// <summary>
    /// Bark tube mesh as plain arrays. Channels (spec §5.1, revised for HDRP's float2 UVs):
    /// POSITION = mature centerline point, NORMAL = radial, TANGENT = axis direction,
    /// UV0 = (angle01, arc length m), UV1 = (segmentId, fractionAlong), UV2 = (radialScale, axisPhase),
    /// UV3 = (axisOrder, axis base mature radius m - constant per axis so bark ridges never pop), COLOR = bark tint.
    /// </summary>
    public sealed class BarkMeshArrays
    {
        /// <summary>Mature centerline point.</summary>
        public Vector3[] centerline = Array.Empty<Vector3>();
        /// <summary>Unit radial direction.</summary>
        public Vector3[] radial = Array.Empty<Vector3>();
        /// <summary>Unit axis direction.</summary>
        public Vector3[] tangent = Array.Empty<Vector3>();
        /// <summary>(angle01, arc length).</summary>
        public Vector2[] uv0 = Array.Empty<Vector2>();
        /// <summary>(segmentId, fractionAlong).</summary>
        public Vector2[] uv1 = Array.Empty<Vector2>();
        /// <summary>(radialScale, axisPhase).</summary>
        public Vector2[] uv2 = Array.Empty<Vector2>();
        /// <summary>(axisOrder, axis base mature radius).</summary>
        public Vector2[] uv3 = Array.Empty<Vector2>();
        /// <summary>Bark tint (rgb) + 1.</summary>
        public Vector4[] color = Array.Empty<Vector4>();
        /// <summary>Triangle list.</summary>
        public int[] indices = Array.Empty<int>();
    }

    /// <summary>The single leaf blade mesh: +Z length, +Y face normal, +X width; base at the origin.</summary>
    public sealed class LeafMeshArrays
    {
        /// <summary>Positions for a 1 m blade.</summary>
        public Vector3[] positions = Array.Empty<Vector3>();
        /// <summary>Normals.</summary>
        public Vector3[] normals = Array.Empty<Vector3>();
        /// <summary>Atlas UVs.</summary>
        public Vector2[] uv0 = Array.Empty<Vector2>();
        /// <summary>Triangle list.</summary>
        public int[] indices = Array.Empty<int>();
    }

    /// <summary>Everything the runtime needs to render a tree at any age. Pure data.</summary>
    public sealed class TreeBakeData
    {
        /// <summary>Simulated years.</summary>
        public int years;
        /// <summary>Shared key years (16).</summary>
        public float[] keyYears = Array.Empty<float>();
        /// <summary>Segments (same order as the history).</summary>
        public SegmentGpu[] segments = Array.Empty<SegmentGpu>();
        /// <summary>Radius keys, 16 per segment.</summary>
        public float[] radiusKeys = Array.Empty<float>();
        /// <summary>Leaves sorted by birth.</summary>
        public LeafGpu[] leaves = Array.Empty<LeafGpu>();
        /// <summary>Bark mesh.</summary>
        public BarkMeshArrays bark = new BarkMeshArrays();
        /// <summary>Leaf blade mesh.</summary>
        public LeafMeshArrays leafMesh = new LeafMeshArrays();
        /// <summary>Most leaves alive at any sampled year (+ margin): the leaf slot-mesh size.</summary>
        public int maxLiveLeaves;
        /// <summary>Years over which dead wood shrinks away.</summary>
        public float deathFadeYears = 1f;
        /// <summary>Mature bounds min.</summary>
        public Vector3 boundsMin;
        /// <summary>Mature bounds max.</summary>
        public Vector3 boundsMax;
    }
}
