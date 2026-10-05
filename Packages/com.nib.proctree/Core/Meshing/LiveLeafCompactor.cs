using System;

namespace Nib.ProcTree.Core.Meshing
{
    /// <summary>
    /// Builds the list of leaves alive at a year so the GPU only processes those (spec §5.3).
    /// Leaves are sorted by birth, so only the born prefix is scanned. Preallocated: no GC per call.
    /// </summary>
    public sealed class LiveLeafCompactor
    {
        readonly LeafGpu[] _leaves;

        /// <summary>Indices of live leaves; valid up to the count returned by <see cref="Compact"/>.</summary>
        public readonly uint[] LiveIndices;

        /// <summary>Wraps leaves that are sorted by birth.</summary>
        public LiveLeafCompactor(LeafGpu[] leavesSortedByBirth)
        {
            _leaves = leavesSortedByBirth;
            LiveIndices = new uint[Math.Max(1, _leaves.Length)];
        }

        /// <summary>Fills <see cref="LiveIndices"/> for <paramref name="year"/>; returns the count.</summary>
        public int Compact(float year)
        {
            int born = BornCount(year);
            int n = 0;
            for (int i = 0; i < born; i++)
                if (GrowthReveal.LeafLive(_leaves[i], year)) LiveIndices[n++] = (uint)i;
            return n;
        }

        int BornCount(float year)
        {
            int lo = 0, hi = _leaves.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_leaves[mid].birth < year) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }
}
