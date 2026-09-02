namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// Pure decision for normalizing the arms chain around a live-body controller swap.
    ///
    /// Round 9 (#37) overturned the round-6/8 theory this policy was built on: the capture
    /// that decides what the 11 unkeyed Base Layer states write back happens at the RigBuilder
    /// graph build (see <see cref="RigBuildCapturePolicy"/>), not at the controller
    /// assignment, so this normalization alone does not steer it. It is kept as defense in
    /// depth: it clears teardown residue from the arms chain before the swap and holds a
    /// pristine pose for the window between the assignment and the rig build. The live stance
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
