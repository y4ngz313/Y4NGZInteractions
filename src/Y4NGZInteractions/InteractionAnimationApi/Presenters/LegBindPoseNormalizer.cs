using System;
using System.Collections.Generic;
using BepInEx.Logging;
using GameNetcodeStuff;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// #660: one-shot leg bind-pose normalization after a live-body RigBuilder rebuild.
    /// RigBuilder.Build() re-binds the Animation Rigging stream, and Unity captures the
    /// write-defaults of stream-only bones (no vanilla clip keys shin.L/shin.R) at the next
    /// natural animation pass, from whatever the live transform holds then. A stop mid-stride
    /// left the leg IK's straight output on the shin, the capture baked a zero-bend default,
    /// and TwoBoneIK folded that knee backward in every state until the next rebuild.
    /// Each rebuild therefore schedules one write of the authored rest shin rotation, applied
    /// by <see cref="RunPending"/> only from the callback that precedes the body Animator's
    /// next natural pass: Update for a Normal/UnscaledTime Animator, FixedUpdate for an
    /// AnimatePhysics one (FixedUpdate also serves Normal entries, since it precedes Update).
    /// A missing or inactive Animator gets no write: no pass follows, so it would only change
    /// the rendered pose. A write with no capture pending is overwritten by that pass, so it
    /// is harmless. Local presentation only: each client normalizes the rigs it rebuilt;
    /// nothing is networked.
    /// </summary>
    internal static class LegBindPoseNormalizer
    {
        private static readonly List<PendingLegBindPose> Pending =
            new List<PendingLegBindPose>(4);

        internal static void Schedule(PlayerControllerB player, string phase, ManualLogSource logger)
        {
            if (player == null ||
                !InteractionAnimationApiRestoreDiagnostics.NormalizeLegBindPoseAfterRigRebuildEnabled)
            {
                return;
            }

            string scheduledPhase = string.IsNullOrEmpty(phase) ? "<none>" : phase;
            for (int i = 0; i < Pending.Count; i++)
            {
                PendingLegBindPose existing = Pending[i];
                if (!ReferenceEquals(existing.Player, player))
                    continue;

                // A stop and a start in one frame (a weapon swap) rebuild twice before the pass:
                // one normalization covers both, and the log names every phase.
                if (!string.Equals(existing.Phase, scheduledPhase, StringComparison.Ordinal))
                {
                    Pending[i] = new PendingLegBindPose(
                        player,
                        existing.Phase + "+" + scheduledPhase,
                        existing.ScheduledFrame,
                        logger ?? existing.Logger);
                }
                return;
            }

            Pending.Add(new PendingLegBindPose(player, scheduledPhase, Time.frameCount, logger));
        }

        /// <summary>
        /// Drains the pending normalizations whose body Animator's next natural pass follows
        /// this callback. <paramref name="physicsStep"/> is true from FixedUpdate. Per entry: a
        /// missing or inactive Animator is dropped without a write (reason 'animator_inactive');
        /// an AnimatePhysics Animator stays pending outside the physics step, because this
        /// frame's physics pass already ran and an Update write would render the rest shin
        /// before IK re-evaluates (a leg pop); every other entry is normalized, logged and
        /// dropped. A failing entry is dropped alone; kept entries survive in place.
        /// </summary>
        internal static void RunPending(bool physicsStep)
        {
            if (Pending.Count == 0)
                return;

            int frame = Time.frameCount;
            int kept = 0;
            for (int i = 0; i < Pending.Count; i++)
            {
                PendingLegBindPose entry = Pending[i];
                if (!TryDrain(entry, physicsStep, frame))
                    Pending[kept++] = entry;
            }

            Pending.RemoveRange(kept, Pending.Count - kept);
        }

        internal static void Clear()
        {
            Pending.Clear();
        }

        // Returns false only to keep the entry pending; every other outcome drops it.
        private static bool TryDrain(PendingLegBindPose entry, bool physicsStep, int frame)
        {
            ManualLogSource logger =
                entry.Logger ?? InteractionAnimationApiRestoreDiagnostics.StaticLogger;
            string drain = physicsStep ? "fixed_update" : "update";
            try
            {
                bool playerPresent = !(entry.Player == null);
                bool animatorActive = false;
                bool animatePhysics = false;
                if (playerPresent)
                {
                    Animator animator = entry.Player.playerBodyAnimator;
                    animatorActive = !(animator == null || !animator.isActiveAndEnabled);
                    animatePhysics = animatorActive &&
                        animator.updateMode == AnimatorUpdateMode.AnimatePhysics;
                }

                switch (LegBindPoseDrainPolicy.Decide(
                    playerPresent, animatorActive, animatePhysics, physicsStep))
                {
                    case LegBindPoseDrainAction.DropPlayerMissing:
                        LogSkipped(logger, entry, frame, drain, 0, "<none>", "player_missing");
                        return true;
                    case LegBindPoseDrainAction.DropAnimatorInactive:
                        LogSkipped(logger, entry, frame, drain, 0, "<none>", "animator_inactive");
                        return true;
                    case LegBindPoseDrainAction.KeepPending:
                        return false;
                    case LegBindPoseDrainAction.Normalize:
                        break;
                }

                bool normalized =
                    InteractionAnimationApiRestoreDiagnostics.TryRestorePristineShinRotations(
                        entry.Player,
                        out int restored,
                        out string source,
                        out string reason);
                if (!normalized)
                {
                    LogSkipped(logger, entry, frame, drain, restored, source, reason);
                    return true;
                }

                logger?.LogInfo(
                    "[RestoreSeam.legbind] shin_bind_pose_normalized: " +
                    $"frame={frame} " +
                    $"player='{InteractionAnimationApiRestoreDiagnostics.DescribePlayer(entry.Player)}' " +
                    $"phase='{entry.Phase}' scheduledFrame={entry.ScheduledFrame} " +
                    $"drain='{drain}' restored={restored} source='{source}'.");
                return true;
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[RestoreSeam.legbind] shin_bind_pose_failed: " +
                    $"frame={frame} phase='{entry.Phase}' scheduledFrame={entry.ScheduledFrame} " +
                    $"drain='{drain}' reason='{exception.Message}'.");
                return true;
            }
        }

        private static void LogSkipped(
            ManualLogSource logger,
            PendingLegBindPose entry,
            int frame,
            string drain,
            int restored,
            string source,
            string reason)
        {
            logger?.LogInfo(
                "[RestoreSeam.legbind] shin_bind_pose_skipped: " +
                $"frame={frame} " +
                $"player='{InteractionAnimationApiRestoreDiagnostics.DescribePlayer(entry.Player)}' " +
                $"phase='{entry.Phase}' scheduledFrame={entry.ScheduledFrame} " +
                $"drain='{drain}' restored={restored} source='{source}' reason='{reason}'.");
        }

        private readonly struct PendingLegBindPose
        {
            internal PlayerControllerB Player { get; }
            internal string Phase { get; }
            internal int ScheduledFrame { get; }
            internal ManualLogSource Logger { get; }

            internal PendingLegBindPose(
                PlayerControllerB player,
                string phase,
                int scheduledFrame,
                ManualLogSource logger)
            {
                Player = player;
                Phase = phase;
                ScheduledFrame = scheduledFrame;
                Logger = logger;
            }
        }
    }
}
