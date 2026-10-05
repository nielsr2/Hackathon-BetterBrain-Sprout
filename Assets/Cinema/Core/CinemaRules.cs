namespace Cinema
{
    /// <summary>The director's shots, in auto-program order.</summary>
    public enum Shot { Orbit, CloseUp, Medium, FullTree }

    /// <summary>What the auto program looks at when deciding to move on.</summary>
    public struct AutoInputs
    {
        public bool orbitSettled;
        public float orbitHeldSeconds;
        public float orbitHoldSeconds;
        /// <summary>Visible tree height above its root (m).</summary>
        public float liveHeight;
        public float closeUpMaxHeight;
        /// <summary>Visible height / mature height, 0..1.</summary>
        public float grownFraction;
        public float fullTreeAtFraction;
    }

    /// <summary>Pure auto-program rules for <c>CinemaDirector</c> (EditMode-testable).</summary>
    public static class CinemaRules
    {
        /// <summary>
        /// Orbit → CloseUp once the orbit has settled and held; CloseUp → Medium once the seedling
        /// outgrows the close-up; Medium → FullTree once the tree nears full size. Only ever moves
        /// forward, and skips straight through stages whose condition already holds.
        /// </summary>
        public static Shot NextAuto(Shot current, in AutoInputs i)
        {
            var s = current;
            if (s == Shot.Orbit && i.orbitSettled && i.orbitHeldSeconds >= i.orbitHoldSeconds) s = Shot.CloseUp;
            if (s == Shot.CloseUp && i.liveHeight >= i.closeUpMaxHeight) s = Shot.Medium;
            if (s == Shot.Medium && i.grownFraction >= i.fullTreeAtFraction) s = Shot.FullTree;
            return s;
        }

        /// <summary>Smoothstep blend weight for <paramref name="elapsed"/> of <paramref name="duration"/>.</summary>
        public static float BlendWeight(float elapsed, float duration)
        {
            if (duration <= 0f) return 1f;
            float t = elapsed / duration;
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }
    }
}
