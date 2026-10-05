using UnityEngine;

namespace Nib.ProcTree
{
    /// <summary>Ping-pongs a <see cref="ProceduralTree"/>'s growth 0 → 1 → 0 in Play mode (demo helper).</summary>
    [AddComponentMenu("Nib/Proc Tree/Growth Animator")]
    public sealed class GrowthAnimator : MonoBehaviour
    {
        /// <summary>Tree to drive (defaults to one on this GameObject).</summary>
        public ProceduralTree tree;
        /// <summary>Seconds for 0 → 1.</summary>
        public float secondsToGrow = 20f;
        /// <summary>Seconds to hold at each end.</summary>
        public float holdSeconds = 2f;

        float _t;

        void Reset() => tree = GetComponent<ProceduralTree>();

        void Update()
        {
            if (tree == null) return;
            float cycle = 2f * (secondsToGrow + holdSeconds);
            _t = (_t + Time.deltaTime) % cycle;
            float t = _t;
            float g;
            if (t < secondsToGrow) g = t / secondsToGrow;
            else if ((t -= secondsToGrow) < holdSeconds) g = 1f;
            else if ((t -= holdSeconds) < secondsToGrow) g = 1f - t / secondsToGrow;
            else g = 0f;
            tree.growth = g;
        }
    }
}
