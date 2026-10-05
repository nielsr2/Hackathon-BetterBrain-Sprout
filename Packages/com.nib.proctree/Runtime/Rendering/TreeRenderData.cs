using Nib.ProcTree.Core;
using Nib.ProcTree.Core.Meshing;
using UnityEngine;
using N = System.Numerics;

namespace Nib.ProcTree.Rendering
{
    /// <summary>
    /// A bake converted into Unity-typed arrays, ready for upload. Built entirely on a worker
    /// thread (plain structs and arrays only, no Unity API calls), so the main thread just copies
    /// it into a Mesh and GraphicsBuffers.
    /// </summary>
    public sealed class TreeRenderData
    {
        /// <summary>The source bake.</summary>
        public TreeBakeData bake;
        /// <summary>Hash of the params + seed that produced it.</summary>
        public ulong sourceHash;

        // Bark mesh
        internal Vector3[] barkPositions, barkNormals;
        internal Vector4[] barkTangents;
        internal Vector2[] barkUv0, barkUv1, barkUv2, barkUv3;
        internal Color[] barkColors;
        internal int[] barkIndices;

        // Leaf slot mesh (maxLiveLeaves copies of the blade)
        internal Vector3[] leafPositions, leafNormals;
        internal Vector4[] leafTangents;
        internal Vector2[] leafUv0, leafUv1;
        internal int[] leafIndices;
        internal int leafIndicesPerSlot;

        // GPU buffers, flattened in HLSL struct order
        internal float[] segmentData;   // 12 floats per segment
        internal float[] leafData;      // 16 floats per leaf

        /// <summary>Mature bounds (tree-local).</summary>
        public Bounds bounds;

        /// <summary>Converts <paramref name="b"/>. Safe on any thread.</summary>
        public static TreeRenderData From(TreeBakeData b, ulong sourceHash)
        {
            var d = new TreeRenderData { bake = b, sourceHash = sourceHash };
            var bark = b.bark;
            int n = bark.centerline.Length;
            d.barkPositions = new Vector3[n];
            d.barkNormals = new Vector3[n];
            d.barkTangents = new Vector4[n];
            d.barkUv0 = new Vector2[n]; d.barkUv1 = new Vector2[n]; d.barkUv2 = new Vector2[n]; d.barkUv3 = new Vector2[n];
            d.barkColors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                d.barkPositions[i] = V(bark.centerline[i]);
                d.barkNormals[i] = V(bark.radial[i]);
                // TANGENT = around-ring direction (d(radial)/d(angle)) so the bark normal map's U follows the ring.
                var around = N.Vector3.Cross(bark.tangent[i], bark.radial[i]);
                d.barkTangents[i] = new Vector4(around.X, around.Y, around.Z, 1f);
                d.barkUv0[i] = V(bark.uv0[i]); d.barkUv1[i] = V(bark.uv1[i]);
                d.barkUv2[i] = V(bark.uv2[i]); d.barkUv3[i] = V(bark.uv3[i]);
                var c = bark.color[i];
                d.barkColors[i] = new Color(c.X, c.Y, c.Z, c.W);
            }
            d.barkIndices = bark.indices;

            // Leaf slot mesh
            var t = b.leafMesh;
            int slots = Mathf.Max(1, b.maxLiveLeaves);
            int tv = t.positions.Length, ti = t.indices.Length;
            d.leafIndicesPerSlot = ti;
            d.leafPositions = new Vector3[slots * tv];
            d.leafNormals = new Vector3[slots * tv];
            d.leafTangents = new Vector4[slots * tv];
            d.leafUv0 = new Vector2[slots * tv];
            d.leafUv1 = new Vector2[slots * tv];
            d.leafIndices = new int[slots * ti];
            for (int s = 0; s < slots; s++)
            {
                int vo = s * tv;
                for (int v = 0; v < tv; v++)
                {
                    d.leafPositions[vo + v] = V(t.positions[v]);
                    d.leafNormals[vo + v] = V(t.normals[v]);
                    d.leafTangents[vo + v] = new Vector4(1f, 0f, 0f, 1f);
                    d.leafUv0[vo + v] = V(t.uv0[v]);
                    d.leafUv1[vo + v] = new Vector2(s, 0f);
                }
                int io = s * ti;
                for (int k = 0; k < ti; k++) d.leafIndices[io + k] = vo + t.indices[k];
            }

            // Segment buffer: start.xyz, birth, end.xyz, growDuration, death, axisBaseSegment, axisPhase, axisOrder
            d.segmentData = new float[b.segments.Length * 12];
            for (int i = 0; i < b.segments.Length; i++)
            {
                var s = b.segments[i]; int o = i * 12;
                d.segmentData[o] = s.start.X; d.segmentData[o + 1] = s.start.Y; d.segmentData[o + 2] = s.start.Z; d.segmentData[o + 3] = s.birth;
                d.segmentData[o + 4] = s.end.X; d.segmentData[o + 5] = s.end.Y; d.segmentData[o + 6] = s.end.Z; d.segmentData[o + 7] = s.growDuration;
                d.segmentData[o + 8] = s.death; d.segmentData[o + 9] = s.axisBaseSegment; d.segmentData[o + 10] = s.axisPhase; d.segmentData[o + 11] = s.axisOrder;
            }

            // Leaf buffer: position.xyz, size, rotation.xyzw, birth, death, segment, tint, phase, flush, pad, pad
            d.leafData = new float[Mathf.Max(1, b.leaves.Length) * 16];
            for (int i = 0; i < b.leaves.Length; i++)
            {
                var l = b.leaves[i]; int o = i * 16;
                d.leafData[o] = l.position.X; d.leafData[o + 1] = l.position.Y; d.leafData[o + 2] = l.position.Z; d.leafData[o + 3] = l.size;
                d.leafData[o + 4] = l.rotation.X; d.leafData[o + 5] = l.rotation.Y; d.leafData[o + 6] = l.rotation.Z; d.leafData[o + 7] = l.rotation.W;
                d.leafData[o + 8] = l.birth; d.leafData[o + 9] = l.death; d.leafData[o + 10] = l.segment; d.leafData[o + 11] = l.tint;
                d.leafData[o + 12] = l.phase; d.leafData[o + 13] = l.flush;
            }

            var mn = V(b.boundsMin); var mx = V(b.boundsMax);
            d.bounds = new Bounds((mn + mx) * 0.5f, Vector3.Max(mx - mn, Vector3.one * 0.1f));
            return d;
        }

        static Vector3 V(N.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
        static Vector2 V(N.Vector2 v) => new Vector2(v.X, v.Y);
    }
}
