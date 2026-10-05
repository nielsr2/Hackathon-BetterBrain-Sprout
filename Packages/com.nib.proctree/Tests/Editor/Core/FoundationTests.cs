using System;
using NUnit.Framework;
using Nib.ProcTree.Core;

namespace Nib.ProcTree.Tests
{
    public class FoundationTests
    {
        [Test]
        public void Rng_SameSeed_SameSequence()
        {
            var a = new SeededRng(42); var b = new SeededRng(42);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextFloat(), b.NextFloat());
        }

        [Test]
        public void Rng_AdjacentSeeds_Diverge()
        {
            var a = new SeededRng(1); var b = new SeededRng(2);
            int same = 0;
            for (int i = 0; i < 100; i++) if (a.NextFloat() == b.NextFloat()) same++;
            Assert.Less(same, 3);
        }

        [Test]
        public void Rng_UnitVector_IsUnit_AndInsideSphere_IsInside()
        {
            var r = new SeededRng(7);
            for (int i = 0; i < 500; i++)
            {
                Assert.AreEqual(1f, r.UnitVector().Length(), 1e-4f);
                Assert.LessOrEqual(r.InsideUnitSphere().Length(), 1f + 1e-5f);
            }
        }

        [Test]
        public void Curve_Endpoints_Interpolation_Clamp()
        {
            var c = SampledCurve.FromFunc(t => t * t, 65);
            Assert.AreEqual(0f, c.Evaluate(0f), 1e-6f);
            Assert.AreEqual(1f, c.Evaluate(1f), 1e-6f);
            Assert.AreEqual(0.25f, c.Evaluate(0.5f), 1e-3f);
            Assert.AreEqual(1f, c.Evaluate(5f), 1e-6f);
            Assert.AreEqual(0f, c.Evaluate(-5f), 1e-6f);
            Assert.AreEqual(0.3f, SampledCurve.Constant(0.3f).Evaluate(0.7f), 1e-6f);
        }

        [Test]
        public void Params_Clamp_FixesGarbage()
        {
            var p = TreeParams.OakParkland();
            p.years = -3; p.internodeLength = float.NaN; p.apicalDominance = 7f; p.markerCount = -1;
            p.leafMinLifeYears = 0f; p.leafZoneInternodes = -4;
            p.Clamp();
            Assert.GreaterOrEqual(p.years, 1);
            Assert.IsFalse(float.IsNaN(p.internodeLength));
            Assert.Greater(p.internodeLength, 0f);
            Assert.LessOrEqual(p.apicalDominance, 1f);
            Assert.Greater(p.markerCount, 0);
            Assert.Greater(p.leafMinLifeYears, 0f);
            Assert.GreaterOrEqual(p.leafZoneInternodes, 1);
        }

        [Test]
        public void Params_Hash_StableAndSensitive()
        {
            var a = TreeParams.OakParkland(); var b = TreeParams.OakParkland();
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
            b.gnarliness += 0.01f;
            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash());
            var c = TreeParams.OakParkland();
            c.heightByAge = SampledCurve.Constant(0.5f);
            Assert.AreNotEqual(a.ComputeHash(), c.ComputeHash());
        }

        [Test]
        public void Params_Clone_IsDeep()
        {
            var a = TreeParams.OakParkland();
            var b = a.Clone();
            b.heightByAge.samples[3] = 99f;
            Assert.AreNotEqual(99f, a.heightByAge.samples[3]);
        }
    }
}
