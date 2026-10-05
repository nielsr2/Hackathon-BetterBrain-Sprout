using Nib.ProcTree.Core.Meshing;
using NUnit.Framework;
using UnityEngine;
using SVec3 = System.Numerics.Vector3;

namespace Cinema.Tests
{
    public class CinemaTests
    {
        // Trunk 0→2 m over years 0–2, then a branch 2→(1,3,0) over years 2–3, then one leaf at year 3.
        static TreeBakeData Bake() => new TreeBakeData
        {
            years = 4,
            deathFadeYears = 1f,
            segments = new[]
            {
                new SegmentGpu { start = new SVec3(0, 0, 0), end = new SVec3(0, 2, 0), birth = 0f, growDuration = 2f, death = 1e6f },
                new SegmentGpu { start = new SVec3(0, 2, 0), end = new SVec3(1, 3, 0), birth = 2f, growDuration = 1f, death = 1e6f },
            },
            leaves = new[] { new LeafGpu { position = new SVec3(1, 3, 0), size = 0.5f, birth = 3f, death = 1e6f } },
        };

        [Test]
        public void LiveBounds_FollowsLinearExtension()
        {
            Assert.IsTrue(TreeFraming.LiveBounds(Bake(), 1f, out var b));
            Assert.AreEqual(1f, b.max.y, 1e-5f, "trunk half extended at year 1");
            Assert.AreEqual(0f, b.size.x, 1e-5f, "branch not born yet");

            TreeFraming.LiveBounds(Bake(), 2.5f, out b);
            Assert.AreEqual(2.5f, b.max.y, 1e-5f);
            Assert.AreEqual(0.5f, b.max.x, 1e-5f);
        }

        [Test]
        public void LiveBounds_PadsLeaves_AndIsEmptyBeforeBirth()
        {
            TreeFraming.LiveBounds(Bake(), 3f, out var b);
            Assert.AreEqual(3.5f, b.max.y, 1e-5f);
            Assert.AreEqual(1.5f, b.max.x, 1e-5f);

            var late = Bake();
            late.segments[0].birth = 1f;
            late.segments[1].birth = 5f;
            Assert.IsFalse(TreeFraming.LiveBounds(late, 0.5f, out _));
        }

        [Test]
        public void FitDistance_FitsTheNarrowerAxis()
        {
            // 90° vertical, square: half-angle 45°, d = r / sin 45°.
            Assert.AreEqual(Mathf.Sqrt(2f), TreeFraming.FitDistance(1f, 90f, 1f), 1e-4f);
            // Portrait aspect makes the horizontal axis the limit → farther away.
            Assert.Greater(TreeFraming.FitDistance(1f, 60f, 0.5f), TreeFraming.FitDistance(1f, 60f, 1.78f));
        }

        [Test]
        public void ToWorld_TranslatesAndScales()
        {
            var w = TreeFraming.ToWorld(new Bounds(new Vector3(0, 1, 0), new Vector3(1, 2, 1)),
                Matrix4x4.TRS(new Vector3(0, 10, 0), Quaternion.Euler(0, 45, 0), Vector3.one * 2f));
            Assert.AreEqual(12f, w.center.y, 1e-4f);
            Assert.AreEqual(4f, w.size.y, 1e-4f);
            Assert.AreEqual(2f * Mathf.Sqrt(2f), w.size.x, 1e-4f);
        }

        static AutoInputs Inputs(bool settled = false, float held = 0f, float height = 0.1f, float frac = 0.05f) => new AutoInputs
        {
            orbitSettled = settled, orbitHeldSeconds = held, orbitHoldSeconds = 2f,
            liveHeight = height, closeUpMaxHeight = 1.5f,
            grownFraction = frac, fullTreeAtFraction = 0.6f,
        };

        [Test]
        public void NextAuto_WalksTheProgram()
        {
            Assert.AreEqual(Shot.Orbit, CinemaRules.NextAuto(Shot.Orbit, Inputs(settled: false, held: 99f)));
            Assert.AreEqual(Shot.Orbit, CinemaRules.NextAuto(Shot.Orbit, Inputs(settled: true, held: 1f)));
            Assert.AreEqual(Shot.CloseUp, CinemaRules.NextAuto(Shot.Orbit, Inputs(settled: true, held: 2f)));
            Assert.AreEqual(Shot.Medium, CinemaRules.NextAuto(Shot.CloseUp, Inputs(height: 1.5f)));
            Assert.AreEqual(Shot.FullTree, CinemaRules.NextAuto(Shot.Medium, Inputs(height: 5f, frac: 0.6f)));
        }

        [Test]
        public void NextAuto_NeverGoesBack_AndSkipsSatisfiedStages()
        {
            Assert.AreEqual(Shot.FullTree, CinemaRules.NextAuto(Shot.FullTree, Inputs()), "a receding tree does not pull the camera back in");
            Assert.AreEqual(Shot.FullTree, CinemaRules.NextAuto(Shot.CloseUp, Inputs(height: 9f, frac: 0.9f)));
        }

        [Test]
        public void WeightedMix_NormalisesAndWrapsAngles()
        {
            var m = new WeightedMix();
            m.Add(10f, 0.25f);
            m.Add(20f, 0.25f);
            Assert.AreEqual(15f, m.Value, 1e-4f, "partial total weight is normalised");

            var a = new WeightedMix();
            a.AddAngle(350f, 0.5f);
            a.AddAngle(10f, 0.5f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, a.Angle), 1e-3f, "blends through 0°, not 180°");

            Assert.IsFalse(new WeightedMix().HasValue);
        }

        [Test]
        public void BlendWeight_SmoothstepClamped()
        {
            Assert.AreEqual(0f, CinemaRules.BlendWeight(0f, 2f));
            Assert.AreEqual(0.5f, CinemaRules.BlendWeight(1f, 2f), 1e-5f);
            Assert.AreEqual(1f, CinemaRules.BlendWeight(5f, 2f));
            Assert.AreEqual(1f, CinemaRules.BlendWeight(0f, 0f), "zero duration is a cut");
        }
    }
}
