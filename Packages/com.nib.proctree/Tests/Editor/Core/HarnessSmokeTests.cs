using NUnit.Framework;

namespace Nib.ProcTree.Tests
{
    /// <summary>Proves the test loop runs (offline harness and Unity Test Runner alike).</summary>
    public class HarnessSmokeTests
    {
        [Test]
        public void Arithmetic_StillWorks() => Assert.AreEqual(4, 2 + 2);
    }
}
