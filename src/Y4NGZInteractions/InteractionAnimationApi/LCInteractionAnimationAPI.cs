namespace Y4NGZInteractions.InteractionAnimationApi
{
    public static class LCInteractionAnimationAPI
    {
        private static InteractionAnimationCoordinator coordinator;

        public static bool IsInitialized => coordinator != null;

        public static bool TryRegisterInteractionPack(
            InteractionAnimationPackDefinition pack,
            out string reason)
        {
            if (coordinator == null)
            {
                reason = "interaction_animation_api_not_initialized";
                return false;
            }

            return coordinator.TryRegisterInteractionPack(pack, out reason);
        }

        public static bool TryStartInteraction(
            InteractionAnimationRequest request,
            out InteractionAnimationHandle handle,
            out string reason)
        {
            handle = InteractionAnimationHandle.Empty;

            if (coordinator == null)
            {
                reason = "interaction_animation_api_not_initialized";
                return false;
            }

            return coordinator.TryStartInteraction(request, out handle, out reason);
        }

        /// <summary>
        /// Loads and deserializes a registered live-body interaction's controller, clip pack,
        /// clips, and prop ahead of gameplay. The assets remain cached until API shutdown.
        /// </summary>
        public static bool TryPreloadInteractionAssets(
            string packId,
            string interactionId,
            out string reason)
        {
            if (coordinator == null)
            {
                reason = "interaction_animation_api_not_initialized";
                return false;
            }

            return coordinator.TryPreloadInteractionAssets(packId, interactionId, out reason);
        }

        public static bool TryStopInteraction(
            InteractionAnimationHandle handle,
            InteractionAnimationStopReason stopReason)
        {
            return coordinator != null && coordinator.TryStopInteraction(handle, stopReason);
        }

        public static bool IsInteractionActive(InteractionAnimationHandle handle)
        {
            return coordinator != null && coordinator.IsInteractionActive(handle);
        }

        /// <summary>
        /// Returns true while the API owns this player's live body animator. Consumers that
        /// directly swap playerBodyAnimator controllers must not begin while this is true.
        /// </summary>
        public static bool IsPlayerInteractionActive(GameNetcodeStuff.PlayerControllerB player)
        {
            return coordinator != null && coordinator.IsPlayerInteractionActive(player);
        }

        /// <summary>
        /// Immediately restores and stops this player's API-owned live-body session. Direct
        /// controller owners should call this before taking ownership, then verify the query is false.
        /// </summary>
        public static bool TryStopPlayerInteractions(
            GameNetcodeStuff.PlayerControllerB player,
            InteractionAnimationStopReason stopReason)
        {
            return coordinator != null && coordinator.TryStopPlayerInteractions(player, stopReason);
        }

        // Generic animator parameter passthrough for consuming mods (e.g. Y4NGZUpgrades action
        // gestures: set the action index, then fire the action trigger).
        public static bool TrySetInteractionBool(InteractionAnimationHandle handle, string parameterName, bool value)
        {
            return coordinator != null && coordinator.TrySetInteractionAnimatorParameter(
                handle, parameterName, UnityEngine.AnimatorControllerParameterType.Bool, value ? 1f : 0f);
        }

        public static bool TrySetInteractionInt(InteractionAnimationHandle handle, string parameterName, int value)
        {
            return coordinator != null && coordinator.TrySetInteractionAnimatorParameter(
                handle, parameterName, UnityEngine.AnimatorControllerParameterType.Int, value);
        }

        public static bool TryFireInteractionTrigger(InteractionAnimationHandle handle, string parameterName)
        {
            return coordinator != null && coordinator.TrySetInteractionAnimatorParameter(
                handle, parameterName, UnityEngine.AnimatorControllerParameterType.Trigger, 0f);
        }

        /// <summary>
        /// Toggle-style graceful exit: plays the interaction's put-away animation, then the
        /// interaction stops automatically. Use instead of TryStopInteraction for toggled
        /// interactions (e.g. holstering a held prop).
        /// </summary>
        public static bool TryBeginInteractionExit(InteractionAnimationHandle handle, out string reason)
        {
            reason = string.Empty;
            if (coordinator == null)
            {
                reason = "interaction_animation_api_not_initialized";
                return false;
            }

            return coordinator.TryBeginInteractionExit(handle, out reason);
        }

        internal static void Initialize(InteractionAnimationCoordinator coordinator)
        {
            LCInteractionAnimationAPI.coordinator = coordinator;
        }

        internal static void Shutdown()
        {
            coordinator = null;
        }
    }
}
