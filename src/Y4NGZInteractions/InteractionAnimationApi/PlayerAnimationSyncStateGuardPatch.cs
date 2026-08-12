using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    [HarmonyPatch(typeof(PlayerControllerB), "UpdatePlayerAnimationsToOtherClients")]
    internal static class PlayerAnimationSyncStateGuardPatch
    {
        [HarmonyPrefix]
        private static void EnsureAnimationStateHashCounts(
            PlayerControllerB __instance,
            ref List<int> ___currentAnimationStateHash,
            ref List<int> ___previousAnimationStateHash)
        {
            int requiredCount = __instance?.playerBodyAnimator?.layerCount ?? 0;
            bool currentExpanded = EnsureCount(
                ref ___currentAnimationStateHash,
                requiredCount);
            bool previousExpanded = EnsureCount(
                ref ___previousAnimationStateHash,
                requiredCount);

            if (currentExpanded || previousExpanded)
            {
                Plugin.Log?.LogInfo(
                    "[LCInteractionAnimationAPI] " +
                    "live_body.sync_state_capacity_expanded: " +
                    $"player={__instance.playerClientId}, layers={requiredCount}.");
            }
        }

        internal static bool EnsureCount(ref List<int> states, int requiredCount)
        {
            if (requiredCount <= 0 || states?.Count >= requiredCount)
                return false;

            if (states == null)
                states = new List<int>(requiredCount);

            while (states.Count < requiredCount)
                states.Add(0);

            return true;
        }
    }
}
