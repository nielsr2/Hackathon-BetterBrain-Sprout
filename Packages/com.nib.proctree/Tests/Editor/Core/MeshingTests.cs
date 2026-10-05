using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Nib.ProcTree.Core;
using Nib.ProcTree.Core.Meshing;
using Nib.ProcTree.Core.Simulation;

namespace Nib.ProcTree.Tests
{
    public class MeshingTests
    {
        static TreeParams _p;
        static TreeHistory _h;
        static TreeBakeData _bake;

        static TreeBakeData Bake()
        {
            if (_bake == null)
            {
                _p = TreeParams.OakParkland();
                _h = GrowthSimulator.Run(_p, 1);
                _bake = TreeBaker.Bake(_h, _p);
            }
            return _bake;
        }

        static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        [Test]
        public void Bark_ArraysAreSane()
        {
            var b = Bake().bark;
            int n = b.centerline.Length;
            Assert.AreEqual(n, b.radial.Length);
            Assert.AreEqual(n, b.tangent.Length);
            Assert.AreEqual(n, b.uv0.Length);
            Assert.AreEqual(n, b.uv1.Length);
            Assert.AreEqual(n, b.uv2.Length);
            Assert.AreEqual(n, b.uv3.Length);
            Assert.AreEqual(0, b.indices.Length % 3);
            int segCount = Bake().segments.Length;
            for (int i = 0; i < n; i++)
            {
                Assert.IsTrue(Finite(b.centerline[i]));
                Assert.AreEqual(1f, b.radial[i].Length(), 1e-3f);
                Assert.AreEqual(1f, b.tangent[i].Length(), 1e-3f);
                int seg = (int)b.uv1[i].X;
                Assert.That(seg, Is.InRange(0, segCount - 1));
                Assert.That(b.uv1[i].Y, Is.InRange(0f, 1f));
            }
            foreach (int idx in b.indices) Assert.That(idx, Is.InRange(0, n - 1));
        }

        [Test]
        public void Bark_UnderVertexBudget()
        {
            int n = Bake().bark.centerline.Length;
            Console.WriteLine($"bark vertices: {n}, triangles: {Bake().bark.indices.Length / 3}");
            Assert.Less(n, 400_000);
        }

        [Test]
        public void SegmentGpu_MatchesHistory_WithFiniteDeaths()
        {
            var bake = Bake();
            Assert.AreEqual(_h.segments.Count, bake.segments.Length);
            Assert.AreEqual(_h.segments.Count * TreeHistory.KeyCount, bake.radiusKeys.Length);
            for (int i = 0; i < bake.segments.Length; i++)
            {
                Assert.IsTrue(float.IsFinite(bake.segments[i].death));
                Assert.AreEqual(_h.segments[i].birth, bake.segments[i].birth);
            }
        }

        [Test]
        public void Reveal_YearZero_EverythingCollapsed()
        {
            var bake = Bake();
            Assert.AreEqual(0.0, BarkArea(bake, 0f), 1e-9);
            int live = new LiveLeafCompactor(bake.leaves).Compact(0f);
            Assert.AreEqual(0, live);
        }

        [Test]
        public void Reveal_BarkAreaGrows_UntilFirstPruning()
        {
            var bake = Bake();
            float firstDeath = float.MaxValue;
            foreach (var s in bake.segments) firstDeath = MathF.Min(firstDeath, s.death);
            double prev = 0;
            for (float y = 0.1f; y < firstDeath - 0.01f; y += 0.37f)
            {
                double a = BarkArea(bake, y);
                Assert.GreaterOrEqual(a + 1e-9, prev, $"year {y}");
                prev = a;
            }
            Assert.Greater(prev, 0.0);
        }

        [Test]
        public void Reveal_VerticesStayInsideMatureBounds()
        {
            var bake = Bake();
            var b = bake.bark;
            var pad = new Vector3(0.05f);
            foreach (float y in new[] { 3f, 20f, 47.5f, 80f })
                for (int i = 0; i < b.centerline.Length; i += 7)
                {
                    var v = GrowthReveal.BarkVertex(bake, i, y);
                    Assert.IsTrue(Finite(v));
                    Assert.IsTrue(Vector3.Max(v, bake.boundsMin - pad) == v && Vector3.Min(v, bake.boundsMax + pad) == v, $"vertex {i} year {y}: {v}");
                }
        }

        [Test]
        public void Reveal_VertexOnlyMovesAlongItsSegmentAndRadially()
        {
            var bake = Bake();
            var b = bake.bark;
            for (int i = 0; i < b.centerline.Length; i += 97)
            {
                var seg = bake.segments[(int)b.uv1[i].X];
                var axisDir = Vector3.Normalize(seg.end - seg.start);
                foreach (float y in new[] { seg.birth + 0.001f, seg.birth + seg.growDuration * 0.5f, 79f })
                {
                    var v = GrowthReveal.BarkVertex(bake, i, y);
                    // Decompose relative to the start: what's left after removing the segment
                    // direction must lie along this vertex's radial direction.
                    var d = v - seg.start;
                    var off = d - axisDir * Vector3.Dot(d, axisDir);
                    var radialPart = off - b.radial[i] * Vector3.Dot(off, b.radial[i]);
                    Assert.Less(radialPart.Length(), 0.02f + 0.05f * Vector3.Distance(seg.start, seg.end), $"vertex {i}");
                }
            }
        }

        [Test]
        public void Compactor_MatchesBruteForce_AndDoesNotAllocate()
        {
            var bake = Bake();
            var c = new LiveLeafCompactor(bake.leaves);
            c.Compact(40f);                                  // warm-up
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (float y = 0f; y <= 80f; y += 3.3f) c.Compact(y);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "compaction must not allocate");

            foreach (float y in new[] { 0.5f, 7f, 33.3f, 80f })
            {
                int n = c.Compact(y);
                var got = new HashSet<uint>();
                for (int i = 0; i < n; i++) got.Add(c.LiveIndices[i]);
                var expect = new HashSet<uint>();
                for (int i = 0; i < bake.leaves.Length; i++)
                    if (GrowthReveal.LeafScale(bake.leaves[i], y) > 0f) expect.Add((uint)i);
                CollectionAssert.AreEquivalent(expect, got, $"year {y}");
            }
        }

        [Test]
        public void Leaves_SortedByBirth_AndScaleIsContinuous()
        {
            var leaves = Bake().leaves;
            for (int i = 1; i < leaves.Length; i++) Assert.GreaterOrEqual(leaves[i].birth, leaves[i - 1].birth);
            var l = leaves[leaves.Length / 2];
            float prev = 0f;
            for (float y = l.birth - 0.05f; y < l.birth + 0.3f; y += 0.01f)
            {
                float s = GrowthReveal.LeafScale(l, y);
                Assert.LessOrEqual(MathF.Abs(s - prev), 0.2f);
                prev = s;
            }
        }

        [Test]
        public void MaxLiveLeaves_CoversEveryYear()
        {
            var bake = Bake();
            var c = new LiveLeafCompactor(bake.leaves);
            for (float y = 0f; y <= bake.years; y += 0.173f)
                Assert.LessOrEqual(c.Compact(y), bake.maxLiveLeaves, $"year {y}");
            Console.WriteLine($"max live leaves: {bake.maxLiveLeaves}");
        }

        [Test]
        public void LeafMesh_IsSane()
        {
            var m = Bake().leafMesh;
            Assert.Greater(m.positions.Length, 8);
            Assert.AreEqual(0, m.indices.Length % 3);
            foreach (var n in m.normals) Assert.AreEqual(1f, n.Length(), 1e-3f);
            foreach (var uv in m.uv0) { Assert.That(uv.X, Is.InRange(0f, 1f)); Assert.That(uv.Y, Is.InRange(0f, 1f)); }
        }

        static double BarkArea(TreeBakeData bake, float year)
        {
            var b = bake.bark;
            var pos = new Vector3[b.centerline.Length];
            for (int i = 0; i < pos.Length; i++) pos[i] = GrowthReveal.BarkVertex(bake, i, year);
            double area = 0;
            for (int t = 0; t < b.indices.Length; t += 3)
            {
                var a = pos[b.indices[t]]; var c1 = pos[b.indices[t + 1]]; var c2 = pos[b.indices[t + 2]];
                area += 0.5 * Vector3.Cross(c1 - a, c2 - a).Length();
            }
            return area;
        }
    }
}
