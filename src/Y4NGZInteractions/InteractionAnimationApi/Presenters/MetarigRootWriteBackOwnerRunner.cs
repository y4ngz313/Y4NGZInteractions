using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// #37 round 18b: EARLY LateUpdate host for <see cref="MetarigRootWriteBackOwner"/>. The
    /// metarig root is an ancestor of the whole camera chain, and vanilla
    /// PlayerControllerB.LateUpdate snaps the visor to localVisorTargetPoint (a camera-chain
    /// child) every frame. Correcting the root only from the plugin host's ordinary
    /// LateUpdate, which lands after vanilla's, moved the camera 0.184 m out from under a
    /// visor that had already been placed against the poisoned pose: the visor edges vanished
    /// in Idle1/Sprint and popped back only in Walk, where the clip keys the root itself.
    /// Unity evaluates Normal-mode animators before any LateUpdate, so a very early LateUpdate
    /// sees the animator's write-back and can fix it before vanilla reads the chain. The host
    /// LateUpdate still ticks the owner afterwards as the frame's last word (a no-op once this
    /// runner has corrected).
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    internal sealed class MetarigRootWriteBackOwnerRunner : MonoBehaviour
    {
        private void LateUpdate()
        {
            MetarigRootWriteBackOwner.LateUpdateTick("early");
        }
    }
}
