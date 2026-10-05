using NUnit.Framework;

namespace Sequence.Tests
{
    public class SequenceRulesTests
    {
        [Test]
        public void TreeFinished_AtFullGrowth_OrTimeout()
        {
            Assert.IsFalse(SequenceRules.TreeFinished(0.5f, 1f, 0.005f, 10f, 300f));
            Assert.IsTrue(SequenceRules.TreeFinished(0.996f, 1f, 0.005f, 10f, 300f));
            Assert.IsTrue(SequenceRules.TreeFinished(0.2f, 1f, 0.005f, 300f, 300f));
            Assert.IsFalse(SequenceRules.TreeFinished(0.2f, 1f, 0.005f, 9999f, 0f), "timeout 0 disables it");
        }

        [Test]
        public void TreePhaseOver_FullGrowthCountsOnlyAfterInteractive()
        {
            Assert.IsFalse(SequenceRules.TreePhaseOver(false, 1f, 1f, 0.005f, false), "scripted growth to max does not end it");
            Assert.IsTrue(SequenceRules.TreePhaseOver(true, 0.996f, 1f, 0.005f, false));
            Assert.IsFalse(SequenceRules.TreePhaseOver(true, 0.5f, 1f, 0.005f, false));
            Assert.IsTrue(SequenceRules.TreePhaseOver(false, 0.1f, 1f, 0.005f, true), "timeout ends it regardless");
        }

        [Test]
        public void GlitchEnvelope_NeutralAtEnds_FullAtPeak()
        {
            Assert.AreEqual(0f, SequenceRules.GlitchEnvelope(0f, 0.55f));
            Assert.AreEqual(0f, SequenceRules.GlitchEnvelope(1f, 0.55f));
            Assert.AreEqual(1f, SequenceRules.GlitchEnvelope(0.55f, 0.55f), 1e-5f);
            Assert.Less(SequenceRules.GlitchEnvelope(0.3f, 0.55f), SequenceRules.GlitchEnvelope(0.5f, 0.55f));
            Assert.Greater(SequenceRules.GlitchEnvelope(0.6f, 0.55f), SequenceRules.GlitchEnvelope(0.9f, 0.55f));
        }

        [Test]
        public void CrossedPeak_FiresExactlyOnce()
        {
            int fired = 0;
            float prev = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float t = i / 100f;
                if (SequenceRules.CrossedPeak(prev, t, 0.55f)) fired++;
                prev = t;
            }
            Assert.AreEqual(1, fired);
        }
    }
}
