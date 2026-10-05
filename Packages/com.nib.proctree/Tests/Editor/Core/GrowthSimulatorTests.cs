using System;
using System.Numerics;
using NUnit.Framework;
using Nib.ProcTree.Core;
using Nib.ProcTree.Core.Simulation;

namespace Nib.ProcTree.Tests
{
    /// <summary>Simulation behaviour on the shipped Oak preset (one shared mature run per fixture).</summary>
    public class GrowthSimulatorTests
    {
        static TreeHistory _oak;
        static TreeParams _p;

        static TreeHistory Oak()
        {
            if (_oak == null) { _p = TreeParams.OakParkland(); _oak = GrowthSimulator.Run(_p, 1); }
            return _oak;
        }

        static TreeParams Small()
        {
            var p = TreeParams.OakParkland();
            p.years = 20; p.markerCount = 6000;
            return p;
        }

        static bool Visible(Segment s, float year) => s.birth < year && year < s.death;

        static void Extent(TreeHistory h, float year, out float height, out float width)
        {
            float maxY = 0f, maxR = 0f;
            foreach (var s in h.segments)
            {
                if (!Visible(s, year)) continue;
                maxY = MathF.Max(maxY, s.end.Y);
                maxR = MathF.Max(maxR, new Vector2(s.end.X, s.end.Z).Length());
            }
            height = maxY; width = maxR * 2f;
        }

        [Test]
        public void SameSeed_SameTree_DifferentSeed_DifferentTree()
        {
            var a = GrowthSimulator.Run(Small(), 5);
            var b = GrowthSimulator.Run(Small(), 5);
            var c = GrowthSimulator.Run(Small(), 6);
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
            Assert.AreNotEqual(a.ComputeHash(), c.ComputeHash());
        }

        [Test]
        public void YearZero_NothingExists()
        {
            var h = Oak();
            foreach (var s in h.segments) Assert.Greater(s.birth, -1e-6f);
            foreach (var l in h.leaves) Assert.Greater(l.birth, 0f);
            foreach (var s in h.segments) Assert.IsFalse(Visible(s, 0f));
        }

        [Test]
        public void YearOne_IsASingleUprightSeedlingWithLeaves()
        {
            var h = Oak();
            int segs = 0; float top = 0f;
            foreach (var s in h.segments)
            {
                if (s.GrownAt > 1f) continue;
                segs++;
                Assert.AreEqual(0, s.axis, "only the trunk exists in year one");
                top = MathF.Max(top, s.end.Y);
            }
            Assert.Greater(segs, 0);
            Assert.AreEqual(_p.seedlingHeight, top, _p.seedlingHeight * 0.35f);
            int leaves = 0;
            foreach (var l in h.leaves) if (l.birth < 1f) leaves++;
            Assert.AreEqual(_p.seedlingLeafCount, leaves);
        }

        [Test]
        public void Height_GrowsMonotonically()
        {
            var h = Oak();
            float prev = 0f;
            for (int y = 2; y <= h.years; y += 6)
            {
                Extent(h, y, out float height, out _);
                Assert.GreaterOrEqual(height, prev - 0.05f, $"year {y}");
                prev = height;
            }
            Assert.Greater(prev, _p.matureHeight * 0.6f, "mature tree reaches most of its envelope height");
        }

        [Test]
        public void Young_TallerThanWide_Mature_WiderThanTall()
        {
            var h = Oak();
            Extent(h, h.years * 0.25f, out float hy, out float wy);
            Extent(h, h.years, out float hm, out float wm);
            Assert.Greater(hy, wy, $"young h={hy:F2} w={wy:F2}");
            Assert.Greater(wm, hm, $"mature h={hm:F2} w={wm:F2}");
        }

        [Test]
        public void Topology_ChildrenStartAtParentEnd_AndNeverBeforeParentFinishes()
        {
            var h = Oak();
            for (int i = 0; i < h.segments.Count; i++)
            {
                var s = h.segments[i];
                if (s.parent < 0) continue;
                Assert.Less(s.parent, i, "parents precede children");
                var p = h.segments[s.parent];
                Assert.Less(Vector3.Distance(p.end, s.start), 1e-4f);
                Assert.GreaterOrEqual(s.birth + 1e-4f, p.GrownAt, $"segment {i}");
            }
        }

        [Test]
        public void Mature_StaysNearEnvelope()
        {
            var h = Oak();
            float halfW = _p.matureWidth * 0.5f * 1.2f;
            foreach (var s in h.segments)
            {
                Assert.Less(s.end.Y, _p.matureHeight * 1.2f);
                Assert.Less(new Vector2(s.end.X, s.end.Z).Length(), halfW);
                Assert.Greater(s.end.Y, -0.01f);
            }
        }

        [Test]
        public void Deaths_AfterBirths_LeavesDieWithTheirWood()
        {
            var h = Oak();
            foreach (var s in h.segments) Assert.GreaterOrEqual(s.death, s.birth);
            foreach (var l in h.leaves)
            {
                Assert.Greater(l.death, l.birth);
                Assert.LessOrEqual(l.death, h.segments[l.segment].death + 1e-4f);
            }
        }

        [Test]
        public void Leaves_OnlyNearTwigTips_InteriorIsBare()
        {
            var h = Oak();
            foreach (var l in h.leaves)
                Assert.GreaterOrEqual(l.birth + 1e-4f, h.segments[l.segment].GrownAt);

            // At several ages, every live leaf hangs within leafZoneInternodes of its axis tip
            // (unless it is still inside its minimum life).
            for (float y = 10f; y <= h.years; y += 17f)
            {
                foreach (var l in h.leaves)
                {
                    if (!(l.birth < y && y < l.death)) continue;
                    if (y < l.birth + _p.leafMinLifeYears) continue;
                    int behind = 0;
                    for (int c = h.segments[l.segment].mainChild; c >= 0; c = h.segments[c].mainChild)
                        if (h.segments[c].GrownAt < y) behind++;
                    Assert.LessOrEqual(behind, _p.leafZoneInternodes, $"year {y}");
                }
            }
        }

        [Test]
        public void SomeLowLimbsArePruned()
        {
            var h = Oak();
            int dead = 0;
            foreach (var a in h.axes) if (!float.IsPositiveInfinity(a.death)) dead++;
            Assert.Greater(dead, 0);
        }

        [Test]
        public void MatureLeafCount_InHeroRange()
        {
            var h = Oak();
            float y = h.years;
            int alive = 0;
            foreach (var l in h.leaves) if (l.birth < y && y < l.death + 1e-3f) alive++;
            Assert.That(alive, Is.InRange(10000, 40000), $"alive leaves {alive}");
        }

        [Test]
        public void PipeModel_ParentMatchesChildren_WhenNothingIsPruned()
        {
            var p = Small(); p.pruneLightThreshold = 0f; p.rootFlare = 0f;
            var h = GrowthSimulator.Run(p, 2);
            int last = TreeHistory.KeyCount - 1;
            float e = p.pipeExponent;
            var sum = new double[h.segments.Count];
            for (int i = 0; i < h.segments.Count; i++)
            {
                int par = h.segments[i].parent;
                if (par >= 0) sum[par] += Math.Pow(h.radiusKeys[i * TreeHistory.KeyCount + last], e);
            }
            int checkedCount = 0;
            for (int i = 0; i < h.segments.Count; i++)
            {
                if (sum[i] == 0) continue;
                bool isAxisTip = h.segments[i].mainChild < 0;
                if (isAxisTip) continue;
                double parent = Math.Pow(h.radiusKeys[i * TreeHistory.KeyCount + last], e);
                Assert.AreEqual(sum[i], parent, parent * 0.05, $"segment {i}");
                checkedCount++;
            }
            Assert.Greater(checkedCount, 10);
        }

        [Test]
        public void Radius_NeverShrinks_AndTrunkIsOakSized()
        {
            var h = Oak();
            for (int s = 0; s < h.segments.Count; s++)
                for (int k = 1; k < TreeHistory.KeyCount; k++)
                    Assert.GreaterOrEqual(h.radiusKeys[s * TreeHistory.KeyCount + k] + 1e-7f, h.radiusKeys[s * TreeHistory.KeyCount + k - 1]);
            float trunkDiameter = 2f * h.RadiusAt(0, h.years);
            Assert.That(trunkDiameter, Is.InRange(0.8f, 1.6f), $"trunk {trunkDiameter:F2} m");
        }

        [Test]
        public void Bounds_ContainEverything()
        {
            var h = Oak();
            foreach (var s in h.segments)
            {
                Assert.IsTrue(s.end.X >= h.boundsMin.X && s.end.X <= h.boundsMax.X);
                Assert.IsTrue(s.end.Y >= h.boundsMin.Y && s.end.Y <= h.boundsMax.Y);
            }
            foreach (var l in h.leaves)
                Assert.IsTrue(l.position.Y >= h.boundsMin.Y && l.position.Y <= h.boundsMax.Y);
        }
    }
}
