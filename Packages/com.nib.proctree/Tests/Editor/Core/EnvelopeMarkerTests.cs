using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Nib.ProcTree.Core;
using Nib.ProcTree.Core.Simulation;

namespace Nib.ProcTree.Tests
{
    public class EnvelopeMarkerTests
    {
        static TreeParams P() { var p = TreeParams.OakParkland(); p.markerCount = 5000; p.Clamp(); return p; }

        [Test]
        public void Envelope_Mature_IsWiderThanTall_Young_IsTallerThanWide()
        {
            var e = new CrownEnvelope(P());
            e.Size(1f, out float h1, out float w1);
            e.Size(0.25f, out float hy, out float wy);
            Assert.Greater(w1, h1);
            Assert.Greater(hy, wy);
        }

        [Test]
        public void Envelope_Contains_CenterButNotFarPoint()
        {
            var e = new CrownEnvelope(P());
            e.Size(1f, out float h, out _);
            Assert.IsTrue(e.Contains(new Vector3(0f, h * 0.6f, 0f), 1f));
            Assert.IsFalse(e.Contains(new Vector3(50f, h * 0.6f, 0f), 1f));
            Assert.IsFalse(e.Contains(new Vector3(0f, h * 1.5f, 0f), 1f));
        }

        [Test]
        public void Markers_Deterministic()
        {
            var a = new MarkerCloud(P(), new SeededRng(3));
            var b = new MarkerCloud(P(), new SeededRng(3));
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a.Position[i], b.Position[i]);
                Assert.AreEqual(a.AppearAge[i], b.AppearAge[i]);
            }
        }

        [Test]
        public void Markers_EachInsideItsAppearEnvelope()
        {
            var p = P();
            var e = new CrownEnvelope(p);
            var m = new MarkerCloud(p, new SeededRng(3));
            for (int i = 0; i < m.Count; i++)
                Assert.IsTrue(e.Contains(m.Position[i], m.AppearAge[i]), $"marker {i}");
        }

        [Test]
        public void Markers_YoungOnesExistNearTheGround()
        {
            var m = new MarkerCloud(P(), new SeededRng(3));
            int young = 0;
            for (int i = 0; i < m.Count; i++)
                if (m.AppearAge[i] < 0.08f && m.Position[i].Y < 2f) young++;
            Assert.Greater(young, 50);
        }

        [Test]
        public void Markers_VisibilityRespectsAgeAndConsumption()
        {
            var m = new MarkerCloud(P(), new SeededRng(3));
            int i = 0;
            Assert.IsFalse(m.Visible(i, m.AppearAge[i] - 0.01f));
            Assert.IsTrue(m.Visible(i, m.AppearAge[i] + 0.01f));
            m.Consume(i);
            Assert.IsFalse(m.Visible(i, 1f));
        }

        [Test]
        public void SpatialHash_QueryMatchesBruteForce()
        {
            var rng = new SeededRng(11);
            var pts = new List<Vector3>();
            for (int i = 0; i < 2000; i++) pts.Add(rng.InsideUnitSphere() * 5f);
            var hash = new SpatialHash(0.7f);
            hash.Build(pts.ToArray(), pts.Count);
            var found = new List<int>();
            for (int q = 0; q < 50; q++)
            {
                var c = rng.InsideUnitSphere() * 5f;
                float r = rng.Range(0.2f, 1.5f);
                found.Clear();
                hash.Query(c, r, found);
                found.Sort();
                var brute = new List<int>();
                for (int i = 0; i < pts.Count; i++) if (Vector3.DistanceSquared(pts[i], c) <= r * r) brute.Add(i);
                CollectionAssert.AreEqual(brute, found);
            }
        }
    }
}
