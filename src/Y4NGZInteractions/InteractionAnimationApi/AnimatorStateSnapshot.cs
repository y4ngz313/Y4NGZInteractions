using System;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal enum AnimatorStateRestoreMode
    {
        Fresh,
        Crossfade,
        Replay
    }

    internal enum AnimatorLayerReplayTarget
    {
        None,
        Current,
        Next
    }

    internal sealed class AnimatorStateSnapshot
    {
        private const float CrossfadeDurationSeconds = 0.12f;

        private readonly LayerSnapshot[] layers;
        private readonly ParameterSnapshot[] parameters;

        public RuntimeAnimatorController RuntimeAnimatorController { get; }
        public float Speed { get; }

        /// <summary>
        /// Player stance (PlayerControllerB.isCrouching) at capture time, set by the presenter
        /// immediately after <see cref="Capture"/>. Null when the capturer had no player.
        /// Restore uses it to refuse replaying a stale base-layer state when the player's
        /// stance changed during the session (equip-while-crouched must not replay a crouch
        /// pose after standing).
        /// </summary>
        public bool? CapturedCrouching { get; set; }

        private AnimatorStateSnapshot(
            RuntimeAnimatorController runtimeAnimatorController,
            float speed,
            LayerSnapshot[] layers,
            ParameterSnapshot[] parameters)
        {
            RuntimeAnimatorController = runtimeAnimatorController;
            Speed = speed;
            this.layers = layers ?? Array.Empty<LayerSnapshot>();
            this.parameters = parameters ?? Array.Empty<ParameterSnapshot>();
        }

        public static AnimatorStateSnapshot Capture(Animator animator)
        {
            if (animator == null)
            {
                return null;
            }

            try
            {
                RuntimeAnimatorController runtimeAnimatorController = animator.runtimeAnimatorController;
                float speed = animator.speed;

                if (!animator.isInitialized)
                {
                    return new AnimatorStateSnapshot(
                        runtimeAnimatorController,
                        speed,
                        Array.Empty<LayerSnapshot>(),
                        Array.Empty<ParameterSnapshot>());
                }

                int layerCount = Math.Max(0, animator.layerCount);
                LayerSnapshot[] layers = new LayerSnapshot[layerCount];
                for (int i = 0; i < layerCount; i++)
                {
                    AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(i);
                    bool inTransition = animator.IsInTransition(i);
                    AnimatorStateInfo nextStateInfo = inTransition
                        ? animator.GetNextAnimatorStateInfo(i)
                        : default;
                    layers[i] = new LayerSnapshot(
                        stateInfo.fullPathHash,
                        stateInfo.shortNameHash,
                        stateInfo.normalizedTime,
                        nextStateInfo.fullPathHash,
                        nextStateInfo.shortNameHash,
                        nextStateInfo.normalizedTime,
                        animator.GetLayerWeight(i),
                        inTransition);
                }

                AnimatorControllerParameter[] animatorParameters = animator.parameters ?? Array.Empty<AnimatorControllerParameter>();
                ParameterSnapshot[] parameters = new ParameterSnapshot[animatorParameters.Length];
                for (int i = 0; i < animatorParameters.Length; i++)
                {
                    AnimatorControllerParameter parameter = animatorParameters[i];
                    parameters[i] = ParameterSnapshot.Capture(animator, parameter);
                }

                return new AnimatorStateSnapshot(runtimeAnimatorController, speed, layers, parameters);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Writes the captured parameter values onto the animator's CURRENT controller without
        /// touching the controller, layers, or states. Use immediately after swapping in a shell
        /// controller: Unity resets every parameter to its default on a controller swap, and
        /// vanilla only re-asserts transition-gated parameters ("Walking", "crouching", ...) on
        /// state changes — so without this, equipping mid-walk leaves the locomotion blend dead
        /// for the whole session.
        /// </summary>
        public int ReapplyParameters(Animator animator)
        {
            if (animator == null)
                return 0;

            int reapplied = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                try
                {
                    parameters[i].Restore(animator);
                    reapplied++;
                }
                catch { }
            }

            return reapplied;
        }

        /// <summary>
        /// Replays one captured layer onto the animator's CURRENT controller when that
        /// controller carries the same state. Live-body shell controllers retain vanilla's
        /// base layer, so this preserves a settled stance across the swap instead of
        /// evaluating one frame in the controller's default standing state. A layer captured
        /// mid-transition replays the transition's destination (see
        /// <see cref="SelectReplayTarget"/>). True means a state was instantly played; the
        /// caller's next Animator.Update lands it.
        /// </summary>
        public bool TryReapplyLayerState(
            Animator animator,
            int layerIndex,
            out AnimatorLayerReplayTarget replayTarget,
            out int replayedShortNameHash)
        {
            replayTarget = AnimatorLayerReplayTarget.None;
            replayedShortNameHash = 0;
            if (animator == null || layerIndex < 0 ||
                layerIndex >= layers.Length || layerIndex >= animator.layerCount)
            {
                return false;
            }

            try
            {
                LayerSnapshot layer = layers[layerIndex];
                bool hasNextState = layer.InTransition &&
                    layer.NextFullPathHash != 0 &&
                    animator.HasState(layerIndex, layer.NextFullPathHash);
                // The destination wins whenever it exists, so the source lookup is only needed
                // when it does not.
                bool hasCurrentState = !hasNextState &&
                    layer.FullPathHash != 0 &&
                    animator.HasState(layerIndex, layer.FullPathHash);
                AnimatorLayerReplayTarget target = SelectReplayTarget(
                    layer.FullPathHash,
                    layer.NextFullPathHash,
                    layer.InTransition,
                    hasCurrentState,
                    hasNextState);
                if (target == AnimatorLayerReplayTarget.None)
                    return false;

                bool playNext = target == AnimatorLayerReplayTarget.Next;
                animator.SetLayerWeight(layerIndex, layer.Weight);
                animator.Play(
                    playNext ? layer.NextFullPathHash : layer.FullPathHash,
                    layerIndex,
                    playNext ? layer.NextNormalizedTime : layer.NormalizedTime);
                replayTarget = target;
                replayedShortNameHash = playNext ? layer.NextShortNameHash : layer.ShortNameHash;
                return true;
            }
            catch
            {
                replayTarget = AnimatorLayerReplayTarget.None;
                replayedShortNameHash = 0;
                return false;
            }
        }

        /// <summary>
        /// Which captured state of one layer to replay. A layer captured mid-transition is
        /// already heading into its NEXT state, and the source state may sit in the other
        /// stance (CrouchIdle -> CrouchWalk keeps the crouch either way, but Idle1 -> CrouchDown
        /// does not), so the destination at its own normalized time wins whenever the target
        /// controller carries it. Otherwise the current state replays; a zero hash or a state
        /// the controller lacks is refused.
        /// </summary>
        internal static AnimatorLayerReplayTarget SelectReplayTarget(
            int currentStateHash,
            int nextStateHash,
            bool inTransition,
            bool hasCurrentState,
            bool hasNextState)
        {
            if (inTransition && nextStateHash != 0 && hasNextState)
                return AnimatorLayerReplayTarget.Next;
            if (currentStateHash != 0 && hasCurrentState)
                return AnimatorLayerReplayTarget.Current;
            return AnimatorLayerReplayTarget.None;
        }

        public bool Restore(Animator animator)
        {
            return Restore(animator, RuntimeAnimatorController);
        }

        public bool Restore(Animator animator, RuntimeAnimatorController expectedCurrentController)
        {
            return Restore(animator, expectedCurrentController, rebindAnimator: true);
        }

        public bool Restore(
            Animator animator,
            RuntimeAnimatorController expectedCurrentController,
            bool rebindAnimator)
        {
            return Restore(
                animator,
                expectedCurrentController,
                rebindAnimator,
                AnimatorStateRestoreMode.Replay);
        }

        public bool Restore(
            Animator animator,
            RuntimeAnimatorController expectedCurrentController,
            bool rebindAnimator,
            AnimatorStateRestoreMode restoreMode)
        {
            return Restore(
                animator,
                expectedCurrentController,
                rebindAnimator,
                restoreMode,
                syncParametersBeforeStateReplay: null,
                restoreBaseLayerState: true);
        }

        /// <param name="animator">Animator whose captured state is restored.</param>
        /// <param name="expectedCurrentController">
        /// Controller that must still be owned before restoration proceeds.
        /// </param>
        /// <param name="rebindAnimator">Whether to rebind before replaying captured state.</param>
        /// <param name="restoreMode">Controls which captured layers and transitions replay.</param>
        /// <param name="syncParametersBeforeStateReplay">
        /// Invoked after the captured parameters are rewritten but BEFORE any forced state
        /// replay and the zero-delta Animator.Update. Lets the caller overwrite stale
        /// transition-gated locomotion parameters ("Walking", "crouching", "Jumping") with the
        /// player's CURRENT state so the replayed frame is not driven by equip-time values.
        /// </param>
        /// <param name="restoreBaseLayerState">
        /// False skips the forced Play/CrossFade of the captured base-layer (layer 0) state —
        /// used when the player's stance changed during the session, so the freshly synced
        /// parameters drive layer 0 instead of a stale crouch/stand pose. Other layers and the
        /// base-layer weight are restored as usual.
        /// </param>
        /// <param name="afterControllerAssigned">
        /// Invoked immediately after the captured controller is re-assigned (and after the
        /// optional Rebind), BEFORE any parameter rewrite, layer-state replay, or the final
        /// zero-delta Update. The re-assignment defers Unity's write-defaults capture to the
        /// next evaluation, so this is the caller's one chance to normalize the pose and burn
        /// an evaluation in the controller's default (standing) state — otherwise the capture
        /// bakes whatever stance the outgoing session left on the rig (#37's teardown mirror:
        /// a crouched drop bakes crouched defaults that vanilla's unkeyed states write back).
        /// </param>
        public bool Restore(
            Animator animator,
            RuntimeAnimatorController expectedCurrentController,
            bool rebindAnimator,
            AnimatorStateRestoreMode restoreMode,
            Action syncParametersBeforeStateReplay,
            bool restoreBaseLayerState,
            Action afterControllerAssigned = null)
        {
            if (animator == null)
            {
                return false;
            }

            try
            {
                if (expectedCurrentController != null &&
                    animator.runtimeAnimatorController != expectedCurrentController)
                {
                    return false;
                }

                animator.runtimeAnimatorController = RuntimeAnimatorController;

                // Swapping controllers does not reset every transform written by the outgoing
                // controller. Rebind first so bones that the restored vanilla state does not
                // curve cannot retain a custom interaction pose (tilted wrists/floating arms).
                if (rebindAnimator)
                    animator.Rebind();

                if (afterControllerAssigned != null)
                {
                    try { afterControllerAssigned(); } catch { }
                }

                animator.speed = Speed;

                for (int i = 0; i < parameters.Length; i++)
                {
                    parameters[i].Restore(animator);
                }

                // Overwrite stale transition-gated locomotion parameters from live player state
                // before the forced state replay below evaluates — otherwise the equip-time
                // values captured above drive the replayed frame.
                if (syncParametersBeforeStateReplay != null)
                {
                    try { syncParametersBeforeStateReplay(); } catch { }
                }

                int layerCount = Math.Min(animator.layerCount, layers.Length);
                for (int i = 0; i < layerCount; i++)
                {
                    LayerSnapshot layer = layers[i];
                    animator.SetLayerWeight(i, layer.Weight);

                    if (i == 0 && !restoreBaseLayerState)
                        continue;

                    if (layer.FullPathHash != 0)
                    {
                        if (restoreMode == AnimatorStateRestoreMode.Crossfade)
                        {
                            animator.CrossFadeInFixedTime(
                                layer.FullPathHash,
                                CrossfadeDurationSeconds,
                                i,
                                Mathf.Repeat(layer.NormalizedTime, 1f));
                        }
                        else if (restoreMode == AnimatorStateRestoreMode.Replay)
                        {
                            animator.Play(layer.FullPathHash, i, layer.NormalizedTime);
                        }
                    }
                }

                animator.Update(0f);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private readonly struct LayerSnapshot
        {
            public readonly int FullPathHash;
            public readonly int ShortNameHash;
            public readonly float NormalizedTime;
            // Destination of the transition in flight at capture; zero when none was.
            public readonly int NextFullPathHash;
            public readonly int NextShortNameHash;
            public readonly float NextNormalizedTime;
            public readonly float Weight;
            public readonly bool InTransition;

            public LayerSnapshot(
                int fullPathHash,
                int shortNameHash,
                float normalizedTime,
                int nextFullPathHash,
                int nextShortNameHash,
                float nextNormalizedTime,
                float weight,
                bool inTransition)
            {
                FullPathHash = fullPathHash;
                ShortNameHash = shortNameHash;
                NormalizedTime = normalizedTime;
                NextFullPathHash = nextFullPathHash;
                NextShortNameHash = nextShortNameHash;
                NextNormalizedTime = nextNormalizedTime;
                Weight = weight;
                InTransition = inTransition;
            }
        }

        private readonly struct ParameterSnapshot
        {
            private readonly int nameHash;
            private readonly AnimatorControllerParameterType type;
            private readonly bool boolValue;
            private readonly int intValue;
            private readonly float floatValue;

            private ParameterSnapshot(
                int nameHash,
                AnimatorControllerParameterType type,
                bool boolValue,
                int intValue,
                float floatValue)
            {
                this.nameHash = nameHash;
                this.type = type;
                this.boolValue = boolValue;
                this.intValue = intValue;
                this.floatValue = floatValue;
            }

            public static ParameterSnapshot Capture(Animator animator, AnimatorControllerParameter parameter)
            {
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        return new ParameterSnapshot(parameter.nameHash, parameter.type, animator.GetBool(parameter.nameHash), 0, 0f);
                    case AnimatorControllerParameterType.Int:
                        return new ParameterSnapshot(parameter.nameHash, parameter.type, false, animator.GetInteger(parameter.nameHash), 0f);
                    case AnimatorControllerParameterType.Float:
                        return new ParameterSnapshot(parameter.nameHash, parameter.type, false, 0, animator.GetFloat(parameter.nameHash));
                    default:
                        return new ParameterSnapshot(parameter.nameHash, parameter.type, false, 0, 0f);
                }
            }

            public void Restore(Animator animator)
            {
                switch (type)
                {
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(nameHash, boolValue);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(nameHash, intValue);
                        break;
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(nameHash, floatValue);
                        break;
                }
            }
        }
    }
}
