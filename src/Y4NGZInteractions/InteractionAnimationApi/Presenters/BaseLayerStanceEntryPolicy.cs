namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal enum BaseLayerStanceEntryAction
    {
        None,
        PlayCrouchIdle,
        FireStartCrouchingTrigger
    }

    /// <summary>
    /// Base-layer stance entry at both live-body controller swaps: the shell assignment at
    /// session start and the vanilla controller restore at session stop. Assigning a controller
    /// resets layer 0 to its default standing state; the presenter first replays the pre-swap
    /// base state, and this policy decides what must still happen before the swap's first
    /// Animator.Update(0f) so a crouched player never evaluates a standing frame.
    ///
    /// The crouch cluster {CrouchDown, CrouchIdle, CrouchWalk} is entered only through the
    /// "startCrouching" trigger, which CROSSFADES out of the standing default: the body stands
    /// for the blend and sinks back. The player was crouched the whole time, so the entry is an
    /// instant Play of CrouchIdle; the trigger is the last resort for a controller that lacks
    /// that state. A replayed state counts only when it is itself a crouch-cluster state: a
    /// standing state replayed under a crouched player is the same one-frame stand.
    ///
    /// A standing player needs nothing here: the "crouching" Bool gates the cluster's exits and
    /// the mid-session resync owns the rest. Stance comes from PlayerControllerB.isCrouching,
    /// which replicates, so local, remote and observer sessions follow the same rule.
    /// </summary>
    internal static class BaseLayerStanceEntryPolicy
    {
        internal static BaseLayerStanceEntryAction Resolve(
            bool crouchingKnown,
            bool crouching,
            bool baseStateReplayed,
            bool replayedStateIsCrouchState,
            bool crouchIdleAvailable)
        {
            if (!crouchingKnown || !crouching)
                return BaseLayerStanceEntryAction.None;

            if (baseStateReplayed && replayedStateIsCrouchState)
                return BaseLayerStanceEntryAction.None;

            return crouchIdleAvailable
                ? BaseLayerStanceEntryAction.PlayCrouchIdle
                : BaseLayerStanceEntryAction.FireStartCrouchingTrigger;
        }
    }
}
