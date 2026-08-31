namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal enum CrouchTriggerAction
    {
        None,
        Fire,
        Reset
    }

    internal static class CrouchTriggerPolicy
    {
        internal static CrouchTriggerAction ResolveEdgeAction(
            bool isLocalPlayer,
            bool hasPreviousState,
            bool previousCrouching,
            bool currentCrouching)
        {
            if (!hasPreviousState || previousCrouching == currentCrouching)
                return CrouchTriggerAction.None;

            // PlayerControllerB.Crouch_performed owns this trigger for the local player.
            // Mirroring that same edge on the next presenter tick visibly enters crouch twice.
            // Remote players have no owner-side input callback, so their swapped controller
            // still needs the presenter to synthesize the edge.
            if (isLocalPlayer)
                return CrouchTriggerAction.None;

            return currentCrouching
                ? CrouchTriggerAction.Fire
                : CrouchTriggerAction.Reset;
        }

        internal static bool ShouldAssertRestoreEntry(
            bool preserveLiveBaseLayer,
            bool liveBaseLayerStateReplayed,
            bool currentCrouchingKnown,
            bool currentCrouching,
            bool entryAlreadyMirrored)
        {
            return preserveLiveBaseLayer &&
                !liveBaseLayerStateReplayed &&
                currentCrouchingKnown &&
                currentCrouching &&
                !entryAlreadyMirrored;
        }
    }
}
