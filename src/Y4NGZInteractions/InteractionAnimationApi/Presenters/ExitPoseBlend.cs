using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GameNetcodeStuff;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// The exit pose blend of live-body sessions. Stop captures the pose the session last
    /// rendered, restores the vanilla controller, then writes that pose back once;
    /// <see cref="ExitPoseBlendRunner"/> then carries every skin bone into the live vanilla pose
    /// over <see cref="ExitPoseBlendCore.DurationSeconds"/>. Right before each animation pass
    /// (<see cref="ExitPoseBlendPlayerLoop"/>) every channel that still holds a blend's write gets
    /// the restored pose back, so no animation pass, rig rebuild or controller swap reads a
    /// blended value. Owns the per-player bone cache, the live blends and the runner's enabled
    /// state. Local and visual only; nothing is networked.
    /// </summary>
    internal static class ExitPoseBlend
    {
        private static readonly List<ExitPoseBlendRig> Rigs = new List<ExitPoseBlendRig>();
        private static readonly List<ExitPoseBlendRig> LiveBlends = new List<ExitPoseBlendRig>();
        private static Behaviour runner;
        private static bool failureLogged;

        // Resolves the cached blend rig of a player, building it on first use or after the body's
        // renderers or meshes changed. Replaced only by tests.
        internal static Func<PlayerControllerB, ExitPoseBlendRig> RigResolver = ResolveRig;

        internal static int LiveCount => LiveBlends.Count;

        internal static bool RunnerEnabled { get; private set; }

        internal static void AttachRunner(Behaviour value)
        {
            runner = value;
            SetRunnerEnabled(LiveBlends.Count > 0);
        }

        /// <summary>
        /// First step of a live-body Stop: captures P0 when the Smooth Blending switch is on, the
        /// stop reason qualifies and the body is visibly animating. Returns the rig to arm after the
        /// restore, or null.
        /// </summary>
        internal static ExitPoseBlendRig CaptureStartPose(
            PlayerControllerB player,
            Animator animator,
            InteractionAnimationStopReason stopReason)
        {
            if (!BlendingSettings.SmoothBlending || !ExitPoseBlendRules.QualifiesForArming(stopReason))
                return null;

            try
            {
                ExitPoseBlendRig rig = RigResolver(player);
                if (rig == null)
                    return null;
                Remove(rig);
                return rig.TryCaptureStart(animator) ? rig : null;
            }
            catch (Exception exception)
            {
                LogFailureOnce("capture", exception);
                return null;
            }
        }

        /// <summary>
        /// Last step of a live-body Stop, after the camera and visor are final: captures R, writes
        /// P0 once and hands the blend to the runner. Arms only when the restore reported success
        /// and both the runner and the pre-animation pass are in place.
        /// </summary>
        internal static void Arm(ExitPoseBlendRig rig, bool animatorRestored)
        {
            if (rig == null || runner == null || !ExitPoseBlendPlayerLoop.IsInstalled)
                return;

            try
            {
                if (!rig.TryArm(animatorRestored))
                    return;
                LiveBlends.Add(rig);
                SetRunnerEnabled(true);
            }
            catch (Exception exception)
            {
                LogFailureOnce("arm", exception);
            }
        }

        // Ends the player's live blend before a new session captures any pose.
        internal static void CancelForNewSession(PlayerControllerB player)
        {
            bool cancelled = false;
            for (int i = LiveBlends.Count - 1; i >= 0; i--)
            {
                ExitPoseBlendRig rig = LiveBlends[i];
                if (!ReferenceEquals(rig.Player, player))
                    continue;
                CancelSafely(rig, ExitPoseBlendCancelKind.BeforeNewSession);
                LiveBlends.RemoveAt(i);
                cancelled = true;
            }

            if (cancelled && LiveBlends.Count == 0)
                SetRunnerEnabled(false);
        }

        // Head of PreLateUpdate, right before the animators update: every live blend puts R back
        // on the channels that still hold its last write. While idle this is one count check.
        internal static void BeforeAnimationPass()
        {
            if (LiveBlends.Count == 0)
                return;

            for (int i = LiveBlends.Count - 1; i >= 0; i--)
            {
                try
                {
                    LiveBlends[i].BeforeAnimationPass();
                }
                catch (Exception exception)
                {
                    LogFailureOnce("pre_pass", exception);
                    LiveBlends.RemoveAt(i);
                }
            }

            if (LiveBlends.Count == 0)
                SetRunnerEnabled(false);
        }

        // One pass over every live blend; ExitPoseBlendRunner calls it once per frame.
        internal static void StepAll(int frameCount, float deltaTime)
        {
            for (int i = LiveBlends.Count - 1; i >= 0; i--)
            {
                bool ended;
                try
                {
                    ended = LiveBlends[i].Step(frameCount, deltaTime);
                }
                catch (Exception exception)
                {
                    LogFailureOnce("step", exception);
                    ended = true;
                }

                if (ended)
                    LiveBlends.RemoveAt(i);
            }

            if (LiveBlends.Count == 0)
                SetRunnerEnabled(false);
        }

        // Plugin teardown: every live blend gets its final values and every cache entry goes.
        internal static void Shutdown()
        {
            for (int i = 0; i < LiveBlends.Count; i++)
                CancelSafely(LiveBlends[i], ExitPoseBlendCancelKind.BeforeNewSession);
            LiveBlends.Clear();
            Rigs.Clear();
            SetRunnerEnabled(false);
            runner = null;
            failureLogged = false;
        }

        private static ExitPoseBlendRig ResolveRig(PlayerControllerB player)
        {
            if (player == null)
                return null;

            ExitPoseBlendRig cached = null;
            for (int i = Rigs.Count - 1; i >= 0; i--)
            {
                ExitPoseBlendRig rig = Rigs[i];
                if (rig.Player == null)
                {
                    Remove(rig);
                    Rigs.RemoveAt(i);
                }
                else if (ReferenceEquals(rig.Player, player))
                {
                    cached = rig;
                }
            }

            if (cached != null && cached.MatchesBody())
                return cached;

            if (cached != null)
            {
                Remove(cached);
                Rigs.Remove(cached);
            }

            ExitPoseBlendRig built = ExitPoseBlendRig.Build(player);
            Rigs.Add(built);
            return built;
        }

        // Takes a blend out of the live list without writing anything.
        private static void Remove(ExitPoseBlendRig rig)
        {
            if (LiveBlends.Remove(rig))
            {
                rig.Core.Cancel(ExitPoseBlendCancelKind.Drop);
                if (LiveBlends.Count == 0)
                    SetRunnerEnabled(false);
            }
        }

        private static void CancelSafely(ExitPoseBlendRig rig, ExitPoseBlendCancelKind kind)
        {
            try
            {
                rig.Cancel(kind);
            }
            catch (Exception exception)
            {
                LogFailureOnce("cancel", exception);
            }
        }

        // With no runner attached nothing reaches the engine; a destroyed runner counts as none.
        private static void SetRunnerEnabled(bool enabled)
        {
            if (runner is null || runner == null)
            {
                RunnerEnabled = false;
                return;
            }

            RunnerEnabled = enabled;
            EnableRunner(enabled);
        }

        // Engine calls compile with the method that holds them, so this one stays separate and
        // the registry runs outside the engine while no runner is attached.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnableRunner(bool enabled)
        {
            runner.enabled = enabled;
        }

        private static void LogFailureOnce(string phase, Exception exception)
        {
            if (failureLogged)
                return;
            failureLogged = true;
            Plugin.Log?.LogWarning(
                $"[LCInteractionAnimationAPI] exit_pose_blend.{phase}_failed: {exception}; " +
                "the blend ends and the player keeps the vanilla pose.");
        }
    }

    /// <summary>
    /// One player's exit pose blend: the bones of its skinned body renderers, the pose buffers
    /// and the state an armed blend checks every pass.
    /// </summary>
    internal sealed class ExitPoseBlendRig
    {
        private const int SourceCount = 4;

        private readonly SkinnedMeshRenderer[] renderers;
        private readonly Mesh[] meshes;
        private readonly Transform[] bones;
        private Animator animator;
        private RuntimeAnimatorController armedController;
        private StartOfRound armedRound;
        private bool wasPlayerControlled;

        private ExitPoseBlendRig(
            PlayerControllerB player,
            SkinnedMeshRenderer[] renderers,
            Mesh[] meshes,
            Transform[] bones)
        {
            Player = player;
            this.renderers = renderers;
            this.meshes = meshes;
            this.bones = bones;
            Core = new ExitPoseBlendCore(bones.Length);
        }

        internal PlayerControllerB Player { get; }

        internal ExitPoseBlendCore Core { get; }

        internal static ExitPoseBlendRig Build(PlayerControllerB player)
        {
            var renderers = new[]
            {
                player.thisPlayerModel,
                player.thisPlayerModelLOD1,
                player.thisPlayerModelLOD2,
                player.thisPlayerModelArms
            };
            var meshes = new Mesh[SourceCount];
            var boneLists = new Transform[SourceCount][];
            for (int i = 0; i < SourceCount; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                meshes[i] = renderer.sharedMesh;
                Transform[] list = renderer.bones;
                // Destroyed bones compare equal to null only through Unity's operator; selection
                // works on plain references.
                for (int b = 0; b < list.Length; b++)
                {
                    if (list[b] == null)
                        list[b] = null;
                }
                boneLists[i] = list;
            }

            var selected = new List<Transform>();
            ExitPoseBoneSelection.Select(boneLists, ReadExcludedTransforms(player), selected);
            return new ExitPoseBlendRig(player, renderers, meshes, selected.ToArray());
        }

        // The cached bone list still belongs to the player's current renderers and meshes.
        internal bool MatchesBody()
        {
            PlayerControllerB player = Player;
            return MatchesSource(0, player.thisPlayerModel) &&
                MatchesSource(1, player.thisPlayerModelLOD1) &&
                MatchesSource(2, player.thisPlayerModelLOD2) &&
                MatchesSource(3, player.thisPlayerModelArms) &&
                !HasDestroyedBone();
        }

        internal bool TryCaptureStart(Animator bodyAnimator)
        {
            animator = bodyAnimator;
            if (!IsAnimatorActive() || animator.updateMode == AnimatorUpdateMode.AnimatePhysics ||
                IsCulled() || Player.isPlayerDead || bones.Length == 0)
            {
                return false;
            }

            ReadBones();
            Core.CaptureStart();
            return true;
        }

        internal bool TryArm(bool animatorRestored)
        {
            bool animatorActive = IsAnimatorActive();
            if (!ExitPoseBlendRules.CanArm(
                    animatorRestored,
                    animatorActive,
                    animatorActive && animator.updateMode == AnimatorUpdateMode.AnimatePhysics,
                    animatorActive && IsCulled(),
                    Player == null || Player.isPlayerDead,
                    bones.Length) ||
                HasDestroyedBone())
            {
                return false;
            }

            armedController = animator.runtimeAnimatorController;
            armedRound = StartOfRound.Instance;
            wasPlayerControlled = Player.isPlayerControlled;
            ReadBones();
            Core.Arm(Time.frameCount);
            WriteBones();
            return true;
        }

        // Right before the animation pass. A destroyed bone is left to the runner, which drops the
        // blend this frame.
        internal void BeforeAnimationPass()
        {
            if (HasDestroyedBone())
                return;

            ReadBones();
            Core.BeforeAnimationPass();
            WriteBones();
        }

        // True when the blend ended with this pass.
        internal bool Step(int frameCount, float deltaTime)
        {
            ExitPoseBlendPassDecision decision = DecidePass();
            if (decision == ExitPoseBlendPassDecision.Drop)
            {
                Core.Cancel(ExitPoseBlendCancelKind.Drop);
                return true;
            }

            ReadBones();
            if (decision == ExitPoseBlendPassDecision.Release)
            {
                Core.Cancel(ExitPoseBlendCancelKind.Release);
                WriteBones();
                return true;
            }

            bool ended = Core.Step(frameCount, deltaTime);
            WriteBones();
            return ended;
        }

        internal void Cancel(ExitPoseBlendCancelKind kind)
        {
            if (kind != ExitPoseBlendCancelKind.Drop && HasDestroyedBone())
                kind = ExitPoseBlendCancelKind.Drop;
            if (kind == ExitPoseBlendCancelKind.Release)
                ReadBones();
            Core.Cancel(kind);
            if (kind != ExitPoseBlendCancelKind.Drop)
                WriteBones();
        }

        private bool MatchesSource(int source, SkinnedMeshRenderer current)
        {
            SkinnedMeshRenderer cached = renderers[source];
            if (!ReferenceEquals(current, cached))
                return false;
            if (cached is null)
                return true;
            return cached != null && ReferenceEquals(cached.sharedMesh, meshes[source]);
        }

        private ExitPoseBlendPassDecision DecidePass()
        {
            PlayerControllerB player = Player;
            GameObject playerObject = player != null ? player.gameObject : null;
            bool playerValid = playerObject != null && playerObject.scene.IsValid();
            bool animatorActive = IsAnimatorActive();
            return ExitPoseBlendRules.DecidePass(
                playerValid,
                playerValid && player.isPlayerDead,
                playerValid && wasPlayerControlled && !player.isPlayerControlled,
                armedRound != null && StartOfRound.Instance != armedRound,
                animatorActive,
                animatorActive && IsCulled(),
                HasDestroyedBone(),
                animatorActive && animator.runtimeAnimatorController != armedController);
        }

        private bool IsAnimatorActive()
        {
            return animator != null && animator.isActiveAndEnabled;
        }

        private bool IsCulled()
        {
            bool anyVisible = false;
            for (int i = 0; i < SourceCount && !anyVisible; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                anyVisible = renderer != null && renderer.isVisible;
            }

            return ExitPoseBlendRules.IsCulled(
                animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
                anyVisible);
        }

        private bool HasDestroyedBone()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null)
                    return true;
            }
            return false;
        }

        private void ReadBones()
        {
            Vector3[] positions = Core.Positions;
            Quaternion[] rotations = Core.Rotations;
            for (int i = 0; i < bones.Length; i++)
                bones[i].GetLocalPositionAndRotation(out positions[i], out rotations[i]);
        }

        // Writes only the flagged channels, then reads each written bone back so the blend's last
        // write is what the transform stored.
        private void WriteBones()
        {
            Vector3[] positions = Core.Positions;
            Quaternion[] rotations = Core.Rotations;
            bool[] writePositions = Core.WritePositions;
            bool[] writeRotations = Core.WriteRotations;
            for (int i = 0; i < bones.Length; i++)
            {
                bool writePosition = writePositions[i];
                bool writeRotation = writeRotations[i];
                if (!writePosition && !writeRotation)
                    continue;

                Transform bone = bones[i];
                if (writePosition && writeRotation)
                    bone.SetLocalPositionAndRotation(positions[i], rotations[i]);
                else if (writePosition)
                    bone.localPosition = positions[i];
                else
                    bone.localRotation = rotations[i];
                bone.GetLocalPositionAndRotation(out positions[i], out rotations[i]);
            }

            Core.AcceptStoredValues();
        }

        // Transforms other owners write: the animator object, the first-person arms rig roots and
        // the camera with every ancestor.
        private static List<Transform> ReadExcludedTransforms(PlayerControllerB player)
        {
            var excluded = new List<Transform>();
            AddIfPresent(excluded, player.playerBodyAnimator != null ? player.playerBodyAnimator.transform : null);
            AddIfPresent(excluded, player.playerModelArmsMetarig);
            AddIfPresent(excluded, player.localArmsTransform);
            Transform cameraChain = player.gameplayCamera != null ? player.gameplayCamera.transform : null;
            while (cameraChain != null)
            {
                excluded.Add(cameraChain);
                cameraChain = cameraChain.parent;
            }
            return excluded;
        }

        private static void AddIfPresent(List<Transform> transforms, Transform transform)
        {
            if (transform != null)
                transforms.Add(transform);
        }
    }
}
