using System;
using System.Collections.Generic;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal enum ExitPoseBlendCancelKind
    {
        // A new session for the player starts: every bone gets its final value now.
        BeforeNewSession,

        // The blend ends early on a body that still exists (controller swapped, player dead or
        // out of control, round changed, animator disabled or culled): channels that still hold
        // the blend's last write get R, the rest are left alone.
        Release,

        // The player or a bone was destroyed: nothing is written.
        Drop
    }

    internal enum ExitPoseBlendPassDecision
    {
        Step,
        Release,
        Drop
    }

    /// <summary>
    /// When an exit pose blend may arm and what each runner pass does with it.
    /// </summary>
    internal static class ExitPoseBlendRules
    {
        internal static bool QualifiesForArming(InteractionAnimationStopReason stopReason)
        {
            return stopReason == InteractionAnimationStopReason.Requested ||
                stopReason == InteractionAnimationStopReason.NaturalEnd ||
                stopReason == InteractionAnimationStopReason.PresenterFailure;
        }

        // The animator is not evaluating: it may cull and none of the body's renderers is visible.
        internal static bool IsCulled(bool alwaysAnimate, bool anyRendererVisible)
        {
            return !alwaysAnimate && !anyRendererVisible;
        }

        // An animator that evaluates in the physics step would read the blend's last write
        // before the pre-animation pass could put the restored pose back, so it never blends.
        internal static bool CanArm(
            bool animatorRestored,
            bool animatorActiveAndEnabled,
            bool animatesInPhysicsStep,
            bool culled,
            bool playerDead,
            int boneCount)
        {
            return animatorRestored && animatorActiveAndEnabled && !animatesInPhysicsStep && !culled &&
                !playerDead && boneCount > 0;
        }

        // Only a destroyed player or bone ends the blend without a write; any other reason to stop
        // early still hands the untouched channels back to R.
        internal static ExitPoseBlendPassDecision DecidePass(
            bool playerValid,
            bool playerDead,
            bool playerLostControl,
            bool roundChanged,
            bool animatorActiveAndEnabled,
            bool culled,
            bool boneDestroyed,
            bool controllerChanged)
        {
            if (!playerValid || boneDestroyed)
                return ExitPoseBlendPassDecision.Drop;

            if (playerDead || playerLostControl || roundChanged || !animatorActiveAndEnabled || culled ||
                controllerChanged)
            {
                return ExitPoseBlendPassDecision.Release;
            }

            return ExitPoseBlendPassDecision.Step;
        }
    }

    /// <summary>
    /// Pose data and stepping of one exit pose blend over local bone positions and rotations.
    /// The blend runs from P0, the pose the session last rendered, to each channel's target: the
    /// value the animator wrote this frame when it drives the channel, or R, the pose right after
    /// the restore, when it does not. Right before every animation pass each channel that still
    /// holds the blend's last write gets R back, so no animation pass reads a value the blend
    /// wrote; after the pass a channel is driven when its value differs from that last write.
    /// The caller exchanges bone values through <see cref="Positions"/>, <see cref="Rotations"/>,
    /// <see cref="WritePositions"/> and <see cref="WriteRotations"/>; after writing it reads the
    /// stored values back into Positions and Rotations and calls
    /// <see cref="AcceptStoredValues"/>. Values are built through their fields, never Unity's
    /// constructors or operators, so the core runs the same outside the engine.
    /// </summary>
    internal sealed class ExitPoseBlendCore
    {
        internal const float DurationSeconds = 0.2f;

        private const float MinRotationLengthSquared = 1e-6f;

        private readonly Vector3[] startPositions;
        private readonly Quaternion[] startRotations;
        private readonly Vector3[] restoredPositions;
        private readonly Quaternion[] restoredRotations;
        private readonly Vector3[] writtenPositions;
        private readonly Quaternion[] writtenRotations;
        private readonly Vector3[] targetPositions;
        private readonly Quaternion[] targetRotations;
        private readonly bool[] positionDriven;
        private readonly bool[] rotationDriven;
        private int armFrame;

        internal ExitPoseBlendCore(int boneCount)
        {
            BoneCount = boneCount;
            Positions = new Vector3[boneCount];
            Rotations = new Quaternion[boneCount];
            WritePositions = new bool[boneCount];
            WriteRotations = new bool[boneCount];
            startPositions = new Vector3[boneCount];
            startRotations = new Quaternion[boneCount];
            restoredPositions = new Vector3[boneCount];
            restoredRotations = new Quaternion[boneCount];
            writtenPositions = new Vector3[boneCount];
            writtenRotations = new Quaternion[boneCount];
            targetPositions = new Vector3[boneCount];
            targetRotations = new Quaternion[boneCount];
            positionDriven = new bool[boneCount];
            rotationDriven = new bool[boneCount];
        }

        internal int BoneCount { get; }

        // Current bone values before each call; the values to write after it.
        internal Vector3[] Positions { get; }

        internal Quaternion[] Rotations { get; }

        // Channels whose Positions and Rotations entries are written after a call.
        internal bool[] WritePositions { get; }

        internal bool[] WriteRotations { get; }

        internal bool IsLive { get; private set; }

        internal int Passes { get; private set; }

        internal float ElapsedSeconds { get; private set; }

        // Positions and Rotations hold P0.
        internal void CaptureStart()
        {
            Array.Copy(Positions, startPositions, BoneCount);
            Array.Copy(Rotations, startRotations, BoneCount);
            IsLive = false;
        }

        // Positions and Rotations hold R on entry and P0, to be written once, on return.
        internal void Arm(int frameCount)
        {
            armFrame = frameCount;
            ElapsedSeconds = 0f;
            Passes = 0;
            for (int i = 0; i < BoneCount; i++)
            {
                restoredPositions[i] = Positions[i];
                restoredRotations[i] = Rotations[i];
                targetPositions[i] = Positions[i];
                targetRotations[i] = Rotations[i];
                writtenPositions[i] = startPositions[i];
                writtenRotations[i] = startRotations[i];
                positionDriven[i] = false;
                rotationDriven[i] = false;
                bool write = HasUsableEnds(i);
                WritePositions[i] = write;
                WriteRotations[i] = write;
                Positions[i] = startPositions[i];
                Rotations[i] = startRotations[i];
            }

            IsLive = true;
        }

        /// <summary>
        /// Positions and Rotations hold the values the transforms stored after the last write.
        /// Each written channel keeps its stored value as the blend's last write, so a transform
        /// that adjusts a value when it is set never reads as driven.
        /// </summary>
        internal void AcceptStoredValues()
        {
            for (int i = 0; i < BoneCount; i++)
            {
                if (WritePositions[i])
                    writtenPositions[i] = Positions[i];
                if (WriteRotations[i])
                    writtenRotations[i] = Rotations[i];
            }
        }

        /// <summary>
        /// Runs right before an animation pass. Positions and Rotations hold the current values on
        /// entry. Each channel that still holds the blend's last write gets R and becomes R's last
        /// write, so the pass reads R there; a channel another writer changed since is left alone.
        /// </summary>
        internal void BeforeAnimationPass()
        {
            Array.Clear(WritePositions, 0, BoneCount);
            Array.Clear(WriteRotations, 0, BoneCount);
            if (!IsLive)
                return;

            for (int i = 0; i < BoneCount; i++)
            {
                if (!HasUsableEnds(i))
                    continue;

                if (SameExact(Positions[i], writtenPositions[i]))
                {
                    Positions[i] = restoredPositions[i];
                    writtenPositions[i] = restoredPositions[i];
                    WritePositions[i] = true;
                }

                if (SameExact(Rotations[i], writtenRotations[i]))
                {
                    Rotations[i] = restoredRotations[i];
                    writtenRotations[i] = restoredRotations[i];
                    WriteRotations[i] = true;
                }
            }
        }

        /// <summary>
        /// One runner pass, after the animation pass. Positions and Rotations hold the current
        /// values on entry and the values to write on return. The pass of the arming frame uses
        /// t = 0 (P0); later passes add <paramref name="deltaTime"/>. Returns true when the blend
        /// ended with this pass.
        /// </summary>
        internal bool Step(int frameCount, float deltaTime)
        {
            if (!IsLive)
            {
                Array.Clear(WritePositions, 0, BoneCount);
                Array.Clear(WriteRotations, 0, BoneCount);
                return true;
            }

            if (frameCount != armFrame && deltaTime > 0f)
                ElapsedSeconds += deltaTime;
            bool finishing = ElapsedSeconds >= DurationSeconds;
            float eased = finishing
                ? 1f
                : InteractionLayerWeightPolicy.Ease(ElapsedSeconds / DurationSeconds, smooth: true);

            for (int i = 0; i < BoneCount; i++)
            {
                Vector3 currentPosition = Positions[i];
                Quaternion currentRotation = Rotations[i];
                if (!HasUsableEnds(i) || !IsUsable(currentPosition, currentRotation))
                {
                    WritePositions[i] = false;
                    WriteRotations[i] = false;
                    continue;
                }

                bool positionIsDriven = !SameExact(currentPosition, writtenPositions[i]);
                bool rotationIsDriven = !SameExact(currentRotation, writtenRotations[i]);
                Vector3 positionTarget = positionIsDriven ? currentPosition : restoredPositions[i];
                Quaternion rotationTarget = rotationIsDriven ? currentRotation : restoredRotations[i];
                Vector3 position = Lerp(startPositions[i], positionTarget, eased);
                Quaternion rotation = Nlerp(startRotations[i], rotationTarget, eased);

                Positions[i] = position;
                Rotations[i] = rotation;
                WritePositions[i] = true;
                WriteRotations[i] = true;
                writtenPositions[i] = position;
                writtenRotations[i] = rotation;
                targetPositions[i] = positionTarget;
                targetRotations[i] = rotationTarget;
                positionDriven[i] = positionIsDriven;
                rotationDriven[i] = rotationIsDriven;
            }

            Passes++;
            if (finishing)
                IsLive = false;
            return finishing;
        }

        /// <summary>
        /// Ends the blend. BeforeNewSession gives every bone its final value: R before the first
        /// pass, otherwise the last target of driven channels and R for the rest. Release reads
        /// the current values from Positions and Rotations and gives R only to channels that still
        /// hold the blend's last write. Drop writes nothing.
        /// </summary>
        internal void Cancel(ExitPoseBlendCancelKind kind)
        {
            bool wasLive = IsLive;
            IsLive = false;
            for (int i = 0; i < BoneCount; i++)
            {
                WritePositions[i] = false;
                WriteRotations[i] = false;
                if (!wasLive || kind == ExitPoseBlendCancelKind.Drop || !HasUsableEnds(i))
                    continue;

                if (kind == ExitPoseBlendCancelKind.BeforeNewSession)
                {
                    bool afterPass = Passes > 0;
                    Positions[i] = afterPass && positionDriven[i] ? targetPositions[i] : restoredPositions[i];
                    Rotations[i] = afterPass && rotationDriven[i] ? targetRotations[i] : restoredRotations[i];
                    WritePositions[i] = true;
                    WriteRotations[i] = true;
                    continue;
                }

                if (SameExact(Positions[i], writtenPositions[i]))
                {
                    Positions[i] = restoredPositions[i];
                    WritePositions[i] = true;
                }

                if (SameExact(Rotations[i], writtenRotations[i]))
                {
                    Rotations[i] = restoredRotations[i];
                    WriteRotations[i] = true;
                }
            }
        }

        internal static Vector3 Lerp(Vector3 from, Vector3 to, float t)
        {
            if (t <= 0f)
                return from;
            if (t >= 1f)
                return to;
            return new Vector3
            {
                x = from.x + (to.x - from.x) * t,
                y = from.y + (to.y - from.y) * t,
                z = from.z + (to.z - from.z) * t
            };
        }

        // Normalized lerp along the shorter arc.
        internal static Quaternion Nlerp(Quaternion from, Quaternion to, float t)
        {
            if (t <= 0f)
                return from;
            if (t >= 1f)
                return to;

            float dot = from.x * to.x + from.y * to.y + from.z * to.z + from.w * to.w;
            float fromScale = 1f - t;
            float toScale = dot < 0f ? -t : t;
            float x = from.x * fromScale + to.x * toScale;
            float y = from.y * fromScale + to.y * toScale;
            float z = from.z * fromScale + to.z * toScale;
            float w = from.w * fromScale + to.w * toScale;
            float lengthSquared = x * x + y * y + z * z + w * w;
            if (!(lengthSquared > MinRotationLengthSquared))
                return to;
            float inverseLength = 1f / (float)Math.Sqrt(lengthSquared);
            return new Quaternion
            {
                x = x * inverseLength,
                y = y * inverseLength,
                z = z * inverseLength,
                w = w * inverseLength
            };
        }

        internal static bool IsUsable(Vector3 position, Quaternion rotation)
        {
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z) ||
                !IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) ||
                !IsFinite(rotation.w))
            {
                return false;
            }

            float lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y +
                rotation.z * rotation.z + rotation.w * rotation.w;
            return lengthSquared > MinRotationLengthSquared;
        }

        // Component equality; Unity's Vector3 and Quaternion operators compare approximately.
        internal static bool SameExact(Vector3 a, Vector3 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z;
        }

        internal static bool SameExact(Quaternion a, Quaternion b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        }

        private bool HasUsableEnds(int bone)
        {
            return IsUsable(startPositions[bone], startRotations[bone]) &&
                IsUsable(restoredPositions[bone], restoredRotations[bone]);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// The bones an exit pose blend carries: the union of the body's skin bone lists.
    /// </summary>
    internal static class ExitPoseBoneSelection
    {
        /// <summary>
        /// Fills <paramref name="selected"/> with every non-null item of the source lists in
        /// order, once each, minus the excluded items. Identity is reference identity.
        /// </summary>
        internal static void Select<T>(
            IReadOnlyList<T[]> sources,
            IReadOnlyList<T> excluded,
            List<T> selected)
            where T : class
        {
            selected.Clear();
            if (sources == null)
                return;

            for (int s = 0; s < sources.Count; s++)
            {
                T[] source = sources[s];
                if (source == null)
                    continue;
                for (int i = 0; i < source.Length; i++)
                {
                    T item = source[i];
                    if (item is null || ContainsReference(excluded, item) || ContainsReference(selected, item))
                        continue;
                    selected.Add(item);
                }
            }
        }

        private static bool ContainsReference<T>(IReadOnlyList<T> items, T item)
            where T : class
        {
            if (items == null)
                return false;
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], item))
                    return true;
            }
            return false;
        }
    }
}
