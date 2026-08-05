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

    internal sealed class AnimatorStateSnapshot
    {
        private const float CrossfadeDurationSeconds = 0.12f;

        private readonly LayerSnapshot[] layers;
        private readonly ParameterSnapshot[] parameters;

        public RuntimeAnimatorController RuntimeAnimatorController { get; }
        public float Speed { get; }

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
                    layers[i] = new LayerSnapshot(
                        stateInfo.fullPathHash,
                        stateInfo.normalizedTime,
                        animator.GetLayerWeight(i));
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
                animator.speed = Speed;

                for (int i = 0; i < parameters.Length; i++)
                {
                    parameters[i].Restore(animator);
                }

                int layerCount = Math.Min(animator.layerCount, layers.Length);
                for (int i = 0; i < layerCount; i++)
                {
                    LayerSnapshot layer = layers[i];
                    animator.SetLayerWeight(i, layer.Weight);

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
            public readonly float NormalizedTime;
            public readonly float Weight;

            public LayerSnapshot(int fullPathHash, float normalizedTime, float weight)
            {
                FullPathHash = fullPathHash;
                NormalizedTime = normalizedTime;
                Weight = weight;
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
