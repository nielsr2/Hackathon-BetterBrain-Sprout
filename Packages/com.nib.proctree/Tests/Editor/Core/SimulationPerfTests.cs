using System.Diagnostics;
using NUnit.Framework;
using Nib.ProcTree.Core;
using Nib.ProcTree.Core.Meshing;
using Nib.ProcTree.Core.Simulation;

namespace Nib.ProcTree.Tests
{
    /// <summary>Budget: mature Oak simulation &lt; 500 ms (spec §9). The assert is looser so slow CI
    /// machines and the Unity editor's Mono do not flake; the measured time is printed.</summary>
    public class SimulationPerfTests
    {
        [Test]
        public void MatureOak_SimulatesWithinBudget()
        {
            GrowthSimulator.Run(TreeParams.OakParkland(), 99);          // warm-up / JIT
            var sw = Stopwatch.StartNew();
            var h = GrowthSimulator.Run(TreeParams.OakParkland(), 1);
            sw.Stop();
            System.Console.WriteLine($"mature oak: {sw.ElapsedMilliseconds} ms, {h.segments.Count} segments, {h.leaves.Count} leaves");
            Assert.Less(sw.ElapsedMilliseconds, 1500);
        }

        [Test]
        public void MatureOak_SimulatePlusBake_WithinBudget()
        {
            TreeBaker.SimulateAndBake(TreeParams.OakParkland(), 98);    // warm-up / JIT
            var sw = Stopwatch.StartNew();
            var b = TreeBaker.SimulateAndBake(TreeParams.OakParkland(), 1);
            sw.Stop();
            System.Console.WriteLine($"simulate + bake: {sw.ElapsedMilliseconds} ms, {b.bark.centerline.Length} bark verts, {b.leaves.Length} leaves");
            Assert.Less(sw.ElapsedMilliseconds, 2000);
        }
    }
}
