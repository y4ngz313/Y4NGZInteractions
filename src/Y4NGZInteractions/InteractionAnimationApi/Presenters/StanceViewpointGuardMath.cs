using System;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal static class StanceViewpointGuardMath
    {
        internal static bool HasSustainedMismatch(
            bool stanceChanged,
            bool exempt,
            float heightDeviation,
            float tolerance,
            float deltaSeconds,
            float requiredSeconds,
            ref float mismatchSeconds)
        {
            if (stanceChanged || exempt || heightDeviation <= tolerance)
            {
                mismatchSeconds = 0f;
                return false;
            }

            mismatchSeconds += Math.Max(0f, deltaSeconds);
            return mismatchSeconds >= Math.Max(0f, requiredSeconds);
        }
    }
}
