using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GameNetcodeStuff;
using UnityEngine;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal delegate bool InteractionAnimationBodyOwnershipValidator(
        PlayerControllerB player,
        InteractionAnimationPresentationKind presentationKind,
        InteractionAnimationHandle[] conflicts,
        out string reason);

    internal sealed class InteractionAnimationCoordinator
    {
        private readonly ManualLogSource logger;
        private readonly Func<InteractionAnimationPresentationKind, IInteractionPresenter>
            presenterFactory;
        private readonly Func<PlayerControllerB> localPlayerResolver;
        private readonly InteractionAnimationBodyOwnershipValidator
            bodyOwnershipValidator;
        private readonly Func<InteractionAnimationContext, IInteractionPresenter,
            InteractionAnimationSession> sessionFactory;
        private readonly Dictionary<string, RegisteredPackSnapshot> packs =
            new Dictionary<string, RegisteredPackSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<InteractionAnimationHandle, InteractionAnimationSession>
            activeSessions =
                new Dictionary<InteractionAnimationHandle, InteractionAnimationSession>();
        private readonly InteractionAnimationResourceLeaseRegistry leases =
            new InteractionAnimationResourceLeaseRegistry();

        internal InteractionAnimationCoordinator(
            ManualLogSource logger,
            Func<InteractionAnimationPresentationKind, IInteractionPresenter> presenterFactory = null,
            Func<PlayerControllerB> localPlayerResolver = null,
            InteractionAnimationBodyOwnershipValidator bodyOwnershipValidator = null,
            Func<InteractionAnimationContext, IInteractionPresenter,
                InteractionAnimationSession> sessionFactory = null)
        {
            this.logger = logger;
            this.presenterFactory = presenterFactory ?? CreateDefaultPresenter;
            this.localPlayerResolver = localPlayerResolver ?? ResolveDefaultLocalPlayer;
            this.bodyOwnershipValidator =
                bodyOwnershipValidator ?? TryValidateBodyAnimatorOwnership;
            this.sessionFactory = sessionFactory ??
                ((context, presenter) => new InteractionAnimationSession(context, presenter));
        }

        internal bool TryRegisterInteractionPack(
            InteractionAnimationPackDefinition pack,
            out string reason)
        {
            InteractionAnimationValidationReport report =
                InteractionAnimationPackValidator.Validate(pack, out RegisteredPackSnapshot snapshot);
            reason = InteractionAnimationManifestValidator.GetFirstErrorCode(report);
            if (!report.IsValid)
                return false;
            if (packs.ContainsKey(snapshot.PackId))
            {
                reason = "pack_already_registered";
                return false;
            }

            packs.Add(snapshot.PackId, snapshot);
            logger?.LogDebug(
                "[LCInteractionAnimationAPI] pack.registered: " +
                $"pack='{snapshot.PackId}' version='{snapshot.Version}' " +
                $"interactions={snapshot.InteractionCount}.");
            return true;
        }

        internal bool TryStartInteraction(
            InteractionAnimationRequest request,
            out InteractionAnimationHandle handle,
            out string reason)
        {
            handle = InteractionAnimationHandle.Empty;
            if (!TryResolveRequest(request, out RegisteredPackSnapshot pack,
                    out RegisteredInteractionSnapshot definition, out reason))
                return false;

            PlayerControllerB localPlayer = ResolveLocalPlayer();
            bool isLocal = ReferenceEquals(request.Player, localPlayer);
            if (definition.PresentationKind ==
                    InteractionAnimationPresentationKind.DedicatedLocalViewmodel && !isLocal)
            {
                reason = "dedicated_viewmodel_requires_local_player";
                return false;
            }

            InteractionAnimationResourceClaim[] claims = BuildClaims(
                request.Player, definition.PresentationKind, isLocal);
            if (!leases.TryPlanAcquisition(
                    claims, request.ConflictPolicy,
                    out InteractionAnimationHandle[] conflicts, out reason))
                return false;
            if (!bodyOwnershipValidator(
                    request.Player, definition.PresentationKind, conflicts, out reason))
                return false;

            handle = InteractionAnimationHandle.NewHandle();
            var requestSnapshot = new InteractionAnimationRequest
            {
                Player = request.Player,
                PackId = pack.PackId,
                InteractionId = definition.InteractionId,
                ConflictPolicy = request.ConflictPolicy
            };
            var context = new InteractionAnimationContext(
                handle,
                requestSnapshot,
                definition.CreateDefinition(),
                definition.Manifest,
                pack.AssetRootPath,
                logger);
            IInteractionPresenter presenter = CreatePresenter(definition.PresentationKind);
            InteractionAnimationSession session = sessionFactory(context, presenter);
            if (!session.TryPreflight(out reason))
            {
                handle = InteractionAnimationHandle.Empty;
                return false;
            }

            InteractionAnimationSession[] suspended = Array.Empty<InteractionAnimationSession>();
            if (conflicts.Length > 0 &&
                !TrySuspendConflicts(conflicts, out suspended, out reason))
            {
                handle = InteractionAnimationHandle.Empty;
                return false;
            }

            if (!leases.TryAcquire(handle, claims, out reason))
            {
                ResumeConflicts(suspended);
                handle = InteractionAnimationHandle.Empty;
                return false;
            }
            if (!session.TryStart(out reason))
            {
                leases.Release(handle);
                ResumeConflicts(suspended);
                handle = InteractionAnimationHandle.Empty;
                return false;
            }

            FinalizeConflicts(suspended);
            activeSessions.Add(handle, session);
            logger?.LogDebug(
                "[LCInteractionAnimationAPI] interaction.started: " +
                $"handle={handle} pack='{pack.PackId}' interaction='{definition.InteractionId}' " +
                $"presentation='{definition.PresentationKind}'.");
            return true;
        }

        internal bool TryPreloadInteractionAssets(
            string packId,
            string interactionId,
            out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(interactionId))
            {
                reason = "preload_invalid_identity";
                return false;
            }
            if (!packs.TryGetValue(packId, out RegisteredPackSnapshot pack))
            {
                reason = "pack_not_registered";
                return false;
            }
            if (!pack.TryGetInteraction(interactionId, out RegisteredInteractionSnapshot definition))
            {
                reason = "interaction_not_registered";
                return false;
            }

            if (definition.PresentationKind == InteractionAnimationPresentationKind.BodyWorld)
                return LiveBodyAnimatorPresenter.TryPreloadBundles(
                    definition.Manifest, pack.AssetRootPath, logger, out reason);
            return LocalViewmodelPresenter.TryBeginPreload(
                definition.Manifest, pack.AssetRootPath, logger, out reason);
        }

        internal bool TryStopInteraction(
            InteractionAnimationHandle handle,
            InteractionAnimationStopReason reason)
        {
            if (!activeSessions.TryGetValue(handle, out InteractionAnimationSession session))
                return false;
            if (!session.TryStopAndRestore(reason))
                return false;

            leases.Release(handle);
            activeSessions.Remove(handle);
            NotifyEnded(session, reason);
            logger?.LogDebug(
                "[LCInteractionAnimationAPI] interaction.stopped: " +
                $"handle={handle} reason='{reason}'.");
            return true;
        }

        internal bool IsInteractionActive(InteractionAnimationHandle handle)
        {
            return activeSessions.TryGetValue(handle, out InteractionAnimationSession session) &&
                session.IsActive;
        }

        internal bool TryGetActiveInteraction(
            PlayerControllerB player,
            InteractionAnimationPresentationKind presentationKind,
            out InteractionAnimationHandle handle)
        {
            handle = InteractionAnimationHandle.Empty;
            if (ReferenceEquals(player, null))
                return false;
            foreach (KeyValuePair<InteractionAnimationHandle, InteractionAnimationSession> pair
                     in activeSessions)
            {
                if (pair.Value.IsActive && ReferenceEquals(pair.Value.Player, player) &&
                    pair.Value.PresentationKind == presentationKind)
                {
                    handle = pair.Key;
                    return true;
                }
            }
            return false;
        }

        internal bool TrySetInteractionAnimatorParameter(
            InteractionAnimationHandle handle,
            string parameterName,
            AnimatorControllerParameterType parameterType,
            float value)
        {
            return activeSessions.TryGetValue(handle, out InteractionAnimationSession session) &&
                session.TrySetAnimatorParameter(parameterName, parameterType, value);
        }

        internal bool TryBeginInteractionExit(
            InteractionAnimationHandle handle,
            out string reason)
        {
            reason = string.Empty;
            if (!activeSessions.TryGetValue(handle, out InteractionAnimationSession session) ||
                !session.IsActive)
            {
                reason = "interaction_not_active";
                return false;
            }
            float seconds = session.BeginExit();
            logger?.LogDebug(
                "[LCInteractionAnimationAPI] interaction.exit_begun: " +
                $"handle={handle} exitSeconds={seconds:0.###}.");
            return true;
        }

        internal void Tick(float deltaTime)
        {
            if (activeSessions.Count == 0)
                return;
            InteractionAnimationHandle[] handles = activeSessions.Keys.ToArray();
            for (int i = 0; i < handles.Length; i++)
            {
                if (!activeSessions.TryGetValue(handles[i], out InteractionAnimationSession session))
                    continue;
                InteractionAnimationStopReason? reason = session.Tick(deltaTime);
                if (reason.HasValue)
                    TryStopInteraction(handles[i], reason.Value);
            }
        }

        internal void Shutdown()
        {
            InteractionAnimationHandle[] handles = activeSessions.Keys.ToArray();
            for (int i = 0; i < handles.Length; i++)
                TryStopInteraction(handles[i], InteractionAnimationStopReason.Shutdown);
            leases.Clear();
            packs.Clear();
        }

        private bool TryResolveRequest(
            InteractionAnimationRequest request,
            out RegisteredPackSnapshot pack,
            out RegisteredInteractionSnapshot interaction,
            out string reason)
        {
            pack = null;
            interaction = null;
            reason = string.Empty;
            if (request == null)
            {
                reason = "request_null";
                return false;
            }
            if (ReferenceEquals(request.Player, null))
            {
                reason = "request_player_missing";
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.PackId))
            {
                reason = "request_pack_id_empty";
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.InteractionId))
            {
                reason = "request_interaction_id_empty";
                return false;
            }
            if (!Enum.IsDefined(typeof(InteractionAnimationConflictPolicy), request.ConflictPolicy))
            {
                reason = "request_conflict_policy_invalid";
                return false;
            }
            if (!packs.TryGetValue(request.PackId, out pack))
            {
                reason = "pack_not_registered";
                return false;
            }
            if (!pack.TryGetInteraction(request.InteractionId, out interaction))
            {
                reason = "interaction_not_registered";
                return false;
            }
            return true;
        }

        private bool TryValidateBodyAnimatorOwnership(
            PlayerControllerB player,
            InteractionAnimationPresentationKind presentationKind,
            InteractionAnimationHandle[] conflicts,
            out string reason)
        {
            reason = string.Empty;
            if (presentationKind != InteractionAnimationPresentationKind.BodyWorld)
                return true;
            Animator animator = player != null ? player.playerBodyAnimator : null;
            if (animator == null)
            {
                reason = "missing_body_animator";
                return false;
            }

            for (int i = 0; i < conflicts.Length; i++)
            {
                if (activeSessions.TryGetValue(conflicts[i], out InteractionAnimationSession session) &&
                    ReferenceEquals(session.Player, player) &&
                    session.PresentationKind == InteractionAnimationPresentationKind.BodyWorld &&
                    session.HasResourceOwnership)
                    return true;
            }

            RuntimeAnimatorController expected = ResolveExpectedVanillaController(player);
            if (expected == null)
            {
                reason = "expected_player_animator_controller_missing";
                return false;
            }
            if (animator.runtimeAnimatorController != expected)
            {
                reason = "player_animator_owned_externally";
                return false;
            }
            return true;
        }

        private bool TrySuspendConflicts(
            InteractionAnimationHandle[] handles,
            out InteractionAnimationSession[] suspended,
            out string reason)
        {
            reason = string.Empty;
            var sessions = new List<InteractionAnimationSession>();
            for (int i = 0; i < handles.Length; i++)
            {
                if (!activeSessions.TryGetValue(handles[i], out InteractionAnimationSession session))
                    continue;
                if (!session.TrySuspend(out reason))
                {
                    for (int resumeIndex = 0; resumeIndex < sessions.Count; resumeIndex++)
                    {
                        InteractionAnimationSession suspendedSession = sessions[resumeIndex];
                        if (suspendedSession.TryResume(out _))
                            continue;
                        leases.Release(suspendedSession.Handle);
                        activeSessions.Remove(suspendedSession.Handle);
                        suspendedSession.TryStopAndRestore(
                            InteractionAnimationStopReason.PresenterFailure);
                        NotifyEnded(suspendedSession,
                            InteractionAnimationStopReason.PresenterFailure);
                    }
                    if (session.IsEnded)
                    {
                        leases.Release(session.Handle);
                        activeSessions.Remove(session.Handle);
                        NotifyEnded(session, InteractionAnimationStopReason.PresenterFailure);
                    }
                    suspended = Array.Empty<InteractionAnimationSession>();
                    return false;
                }
                sessions.Add(session);
            }
            suspended = sessions.ToArray();
            for (int i = 0; i < suspended.Length; i++)
                leases.Release(suspended[i].Handle);
            return true;
        }

        private void ResumeConflicts(InteractionAnimationSession[] sessions)
        {
            for (int i = 0; i < sessions.Length; i++)
            {
                InteractionAnimationSession session = sessions[i];
                bool isLocal = ReferenceEquals(session.Player, ResolveLocalPlayer());
                InteractionAnimationResourceClaim[] claims = BuildClaims(
                    session.Player, session.PresentationKind, isLocal);
                if (leases.TryAcquire(session.Handle, claims, out _) &&
                    session.TryResume(out _))
                    continue;

                leases.Release(session.Handle);
                activeSessions.Remove(session.Handle);
                session.TryStopAndRestore(InteractionAnimationStopReason.PresenterFailure);
                NotifyEnded(session, InteractionAnimationStopReason.PresenterFailure);
            }
        }

        private void FinalizeConflicts(InteractionAnimationSession[] sessions)
        {
            for (int i = 0; i < sessions.Length; i++)
            {
                InteractionAnimationSession session = sessions[i];
                activeSessions.Remove(session.Handle);
                if (session.TryFinalizeSuspended())
                    NotifyEnded(session, InteractionAnimationStopReason.Interrupted);
            }
        }

        private void NotifyEnded(
            InteractionAnimationSession session,
            InteractionAnimationStopReason reason)
        {
            LCInteractionAnimationAPI.NotifyInteractionEnded(session.CreateEndedEvent(reason));
        }

        private static InteractionAnimationResourceClaim[] BuildClaims(
            PlayerControllerB player,
            InteractionAnimationPresentationKind presentationKind,
            bool isLocal)
        {
            if (presentationKind == InteractionAnimationPresentationKind.DedicatedLocalViewmodel)
            {
                return new[]
                {
                    new InteractionAnimationResourceClaim(
                        InteractionAnimationResourceKind.LocalCameraAndArms, player)
                };
            }
            if (isLocal)
            {
                return new[]
                {
                    new InteractionAnimationResourceClaim(
                        InteractionAnimationResourceKind.BodyAnimator, player),
                    new InteractionAnimationResourceClaim(
                        InteractionAnimationResourceKind.LocalCameraAndArms, player)
                };
            }
            return new[]
            {
                new InteractionAnimationResourceClaim(
                    InteractionAnimationResourceKind.BodyAnimator, player)
            };
        }

        private PlayerControllerB ResolveLocalPlayer()
        {
            return localPlayerResolver();
        }

        private static PlayerControllerB ResolveDefaultLocalPlayer()
        {
            try
            {
                return GameNetworkManager.Instance?.localPlayerController ??
                    StartOfRound.Instance?.localPlayerController;
            }
            catch
            {
                return null;
            }
        }

        private RuntimeAnimatorController ResolveExpectedVanillaController(
            PlayerControllerB player)
        {
            StartOfRound round = StartOfRound.Instance;
            if (round == null || ReferenceEquals(player, null))
                return null;
            return ReferenceEquals(player, ResolveLocalPlayer())
                ? round.localClientAnimatorController
                : round.otherClientsAnimatorController;
        }

        private IInteractionPresenter CreatePresenter(
            InteractionAnimationPresentationKind presentationKind)
        {
            return presenterFactory(presentationKind);
        }

        private static IInteractionPresenter CreateDefaultPresenter(
            InteractionAnimationPresentationKind presentationKind)
        {
            if (presentationKind == InteractionAnimationPresentationKind.DedicatedLocalViewmodel)
                return new LocalViewmodelPresenter();
            if (presentationKind == InteractionAnimationPresentationKind.BodyWorld)
                return new LiveBodyAnimatorPresenter();
            return null;
        }
    }
}
