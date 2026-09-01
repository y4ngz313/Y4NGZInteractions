namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal enum CrouchTriggerAction
    {
        None,
        Fire,
        Reset
    }

    internal enum CrouchRestoreEntryOutcome
    {
        NotNeeded,
        InstantEntry,
        TriggerFallback
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

        /// <summary>
        /// How the stop-phase crouch re-entry actually landed. The "startCrouching" trigger
        /// CROSSFADES from the restored standing Idle1 — the restore-scoped camera pin releases
        /// mid-blend, so the viewpoint pops up toward standing and sinks back as the blend
        /// completes. The player was crouched the whole time, so the correct entry is an instant
        /// Play into the crouch cluster; the trigger remains only as the fallback for a
        /// controller whose cluster states this build does not recognise.
        /// </summary>
        internal static CrouchRestoreEntryOutcome ResolveRestoreEntryOutcome(
            bool shouldAssertRestoreEntry,
            bool landedInCrouchCluster)
        {
            if (!shouldAssertRestoreEntry)
                return CrouchRestoreEntryOutcome.NotNeeded;
            return landedInCrouchCluster
                ? CrouchRestoreEntryOutcome.InstantEntry
                : CrouchRestoreEntryOutcome.TriggerFallback;
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
