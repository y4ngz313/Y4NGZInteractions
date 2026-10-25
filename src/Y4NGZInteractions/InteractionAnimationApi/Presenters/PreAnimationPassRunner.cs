using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// #660: drain host for <see cref="LegBindPoseNormalizer"/>. Execution order 32000 runs both
    /// callbacks after every default-order script, so rebuilds from Better Armory's
    /// PlayerControllerB.Update postfix (where it starts and stops sessions) are already
    /// scheduled when Update drains. A write must land right before the pass that captures the
    /// rebuilt rig's shin defaults, never after it. A Normal/UnscaledTime body Animator
    /// animates after all Updates, so Update drains it this frame; FixedUpdate may drain it
    /// too, since it precedes Update, and never runs between an Update-phase stop and that
    /// frame's Normal pass, so it cannot drain early. An AnimatePhysics Animator animates in
    /// the physics step right after FixedUpdate, which precedes Update: an Update write would
    /// land after this frame's pass and render the rest shin before IK re-evaluates, so those
    /// entries wait for the next FixedUpdate. Rebuilds from LateUpdate (the host's own
    /// auto-stop) drain on the next frame's first eligible callback, still ahead of the
    /// capture. Lives on the hidden DDOL host object (#31);
    /// <see cref="InteractionRuntimeHost"/> keeps its default order.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class PreAnimationPassRunner : MonoBehaviour
    {
        private void FixedUpdate() => LegBindPoseNormalizer.RunPending(physicsStep: true);

        private void Update() => LegBindPoseNormalizer.RunPending(physicsStep: false);
    }
}
