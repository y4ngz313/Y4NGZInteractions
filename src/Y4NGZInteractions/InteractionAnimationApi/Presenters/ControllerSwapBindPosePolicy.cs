namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// Pure decision for normalizing the arms chain around a live-body controller swap.
    ///
    /// Round 9 (#37) overturned the round-6/8 theory this policy was built on: this
    /// normalization does not steer what the unkeyed Base Layer states write back (the
    /// seam-frame fix lives in the LateUpdate write-back owners). It is kept as defense in
    /// depth: it clears teardown residue from the arms chain before the swap. The live stance
    /// is re-posed in the same frame, so the normalization is never visible.
    /// </summary>
    internal static class ControllerSwapBindPosePolicy
    {
        /// <summary>
        /// True when a target's local position should be put back on its pristine value before
        /// the controller swap: a pristine baseline exists and the current position deviates from
        /// it beyond tolerance on any axis. Without a baseline nothing is restored — guessing a
        /// rest pose would plant exactly the residue this policy exists to prevent.
        /// </summary>
        internal static bool ShouldRestoreBeforeSwap(
            bool pristineAvailable,
            float deltaX,
            float deltaY,
            float deltaZ,
            float toleranceMeters)
        {
            return pristineAvailable &&
                CameraStanceRestorePolicy.ExceedsResidueTolerance(
                    deltaX, deltaY, deltaZ, toleranceMeters);
        }
    }
}
