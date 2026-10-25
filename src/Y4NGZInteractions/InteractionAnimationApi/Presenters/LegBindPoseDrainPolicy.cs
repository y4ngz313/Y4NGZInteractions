namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal enum LegBindPoseDrainAction
    {
        Normalize,
        KeepPending,
        DropPlayerMissing,
        DropAnimatorInactive
    }

    /// <summary>
    /// #660: pure decision for draining a pending leg bind-pose normalization.
    ///
    /// An AnimatePhysics body animates in the physics step after FixedUpdate, so an Update
    /// write would render the rest shin before IK re-evaluates. A missing or inactive Animator
    /// has no natural pass, so a write would only change the rendered pose.
    /// </summary>
    internal static class LegBindPoseDrainPolicy
    {
        /// <summary>
        /// Drops missing players and inactive Animators before selecting the next natural pass.
        /// </summary>
        internal static LegBindPoseDrainAction Decide(
            bool playerPresent,
            bool animatorActive,
            bool animatePhysics,
            bool physicsStep)
        {
            if (!playerPresent)
                return LegBindPoseDrainAction.DropPlayerMissing;

            if (!animatorActive)
                return LegBindPoseDrainAction.DropAnimatorInactive;

            if (animatePhysics && !physicsStep)
                return LegBindPoseDrainAction.KeepPending;

            return LegBindPoseDrainAction.Normalize;
        }
    }
}
