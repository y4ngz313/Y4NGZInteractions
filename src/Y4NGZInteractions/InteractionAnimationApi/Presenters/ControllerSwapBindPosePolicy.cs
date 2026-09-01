namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// Pure decision for normalizing the arms chain around a live-body controller swap.
    ///
    /// Assigning a controller defers Unity's write-defaults capture to the FIRST evaluation
    /// after the assignment — measured in game (round 6): normalizing the pose before the swap
    /// alone changed nothing, because the first evaluation was the replayed crouch state. The
    /// working sequence is normalize, assign, then burn one zero-delta evaluation in the
    /// controller's default standing state (the capture priming) BEFORE any stance replay. A
    /// capture taken crouched bakes the arms chain at crouch height (metarig 1.017 instead of
    /// the authored 2.104), and the 11 of 15 Base Layer states that do not key the metarig
    /// (Walk, Sprint, WalkSideways, Jump, JumpLand, FallNoJump, ...) write it back — the
    /// first-person arms render ~1.2 m below the camera whenever such a state runs, while
    /// Idle1 (which keys the binding) looks healthy. The live stance is re-posed in the same
    /// frame, so neither the normalization nor the priming evaluation is ever visible.
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
