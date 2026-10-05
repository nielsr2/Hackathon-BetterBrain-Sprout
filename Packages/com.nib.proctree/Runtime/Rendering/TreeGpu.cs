using System;
using Nib.ProcTree.Core.Meshing;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nib.ProcTree.Rendering
{
    /// <summary>
    /// Main-thread GPU side of one tree: bark mesh, leaf slot mesh, segment/radius/leaf/live-index
    /// buffers, and the property block both renderers share. Changing the year costs one leaf
    /// compaction (only when the year changed), one small buffer upload, a submesh range change and
    /// a property-block write — no mesh rebuilds.
    /// </summary>
    public sealed class TreeGpu : IDisposable
    {
        static readonly int SegmentsId = Shader.PropertyToID("_Segments");
        static readonly int RadiusKeysId = Shader.PropertyToID("_RadiusKeys");
        static readonly int LeavesId = Shader.PropertyToID("_Leaves");
        static readonly int LiveLeavesId = Shader.PropertyToID("_LiveLeaves");
        static readonly int KeyYearsId = Shader.PropertyToID("_KeyYears");
        static readonly int TreeYearId = Shader.PropertyToID("_TreeYear");
        static readonly int PrevTreeYearId = Shader.PropertyToID("_PrevTreeYear");
        static readonly int DeathFadeId = Shader.PropertyToID("_DeathFadeYears");
        static readonly int LiveCountId = Shader.PropertyToID("_LiveLeafCount");

        /// <summary>Bark mesh (not saved with the scene).</summary>
        public readonly Mesh barkMesh;
        /// <summary>Leaf slot mesh (not saved with the scene).</summary>
        public readonly Mesh leafMesh;
        /// <summary>The data this was built from.</summary>
        public readonly TreeRenderData data;

        readonly GraphicsBuffer _segments, _radius, _leaves, _live;
        readonly LiveLeafCompactor _compactor;
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        readonly int _slots;
        float _compactedYear = float.NaN;

        /// <summary>Leaves drawn at the current year.</summary>
        public int LiveLeafCount { get; private set; }

        /// <summary>Uploads <paramref name="d"/>. Main thread.</summary>
        public TreeGpu(TreeRenderData d)
        {
            data = d;
            var b = d.bake;

            barkMesh = new Mesh { name = "ProcTree Bark", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            barkMesh.SetVertices(d.barkPositions);
            barkMesh.SetNormals(d.barkNormals);
            barkMesh.SetTangents(d.barkTangents);
            barkMesh.SetUVs(0, d.barkUv0);
            barkMesh.SetUVs(1, d.barkUv1);
            barkMesh.SetUVs(2, d.barkUv2);
            barkMesh.SetUVs(3, d.barkUv3);
            barkMesh.SetColors(d.barkColors);
            barkMesh.SetIndices(d.barkIndices, MeshTopology.Triangles, 0, false);
            barkMesh.bounds = d.bounds;     // mature bounds: stable culling while growing
            barkMesh.UploadMeshData(true);

            _slots = Mathf.Max(1, b.maxLiveLeaves);
            leafMesh = new Mesh { name = "ProcTree Leaves", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            leafMesh.SetVertices(d.leafPositions);
            leafMesh.SetNormals(d.leafNormals);
            leafMesh.SetTangents(d.leafTangents);
            leafMesh.SetUVs(0, d.leafUv0);
            leafMesh.SetUVs(1, d.leafUv1);
            leafMesh.SetIndices(d.leafIndices, MeshTopology.Triangles, 0, false);
            leafMesh.bounds = d.bounds;
            // Not marked no-longer-readable: the submesh range is changed every time growth moves.

            int segCount = Mathf.Max(1, b.segments.Length);
            _segments = new GraphicsBuffer(GraphicsBuffer.Target.Structured, segCount, 48);
            _radius = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, b.radiusKeys.Length), 4);
            _leaves = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, b.leaves.Length), 64);
            _live = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _slots, 4);
            if (b.segments.Length > 0) _segments.SetData(d.segmentData);
            if (b.radiusKeys.Length > 0) _radius.SetData(b.radiusKeys);
            _leaves.SetData(d.leafData);
            _compactor = new LiveLeafCompactor(b.leaves);

            _mpb.SetBuffer(SegmentsId, _segments);
            _mpb.SetBuffer(RadiusKeysId, _radius);
            _mpb.SetBuffer(LeavesId, _leaves);
            _mpb.SetBuffer(LiveLeavesId, _live);
            _mpb.SetFloatArray(KeyYearsId, b.keyYears);
            _mpb.SetFloat(DeathFadeId, b.deathFadeYears);
        }

        /// <summary>Shows the tree at <paramref name="year"/> (and <paramref name="prevYear"/> for motion vectors).</summary>
        public void SetYear(float year, float prevYear, Renderer bark, Renderer leaves)
        {
            if (year != _compactedYear)
            {
                _compactedYear = year;
                int live = Mathf.Min(_compactor.Compact(year), _slots);
                LiveLeafCount = live;
                if (live > 0) _live.SetData(_compactor.LiveIndices, 0, 0, live);
                leafMesh.SetSubMesh(0, new SubMeshDescriptor(0, Mathf.Max(3, live * data.leafIndicesPerSlot)),
                                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
            _mpb.SetFloat(TreeYearId, year);
            _mpb.SetFloat(PrevTreeYearId, prevYear);
            _mpb.SetFloat(LiveCountId, LiveLeafCount);
            if (bark != null) bark.SetPropertyBlock(_mpb);
            if (leaves != null)
            {
                leaves.SetPropertyBlock(_mpb);
                leaves.forceRenderingOff = LiveLeafCount == 0;
            }
        }

        /// <summary>Releases buffers and meshes.</summary>
        public void Dispose()
        {
            _segments?.Release();
            _radius?.Release();
            _leaves?.Release();
            _live?.Release();
            Destroy(barkMesh);
            Destroy(leafMesh);
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
