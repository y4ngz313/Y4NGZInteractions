using System;
using GameNetcodeStuff;
using UnityEngine;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal sealed class InteractionAnimationSession
    {
        private readonly InteractionAnimationContext context;
        private readonly IInteractionPresenter presenter;
        private readonly StartOfRound owningRound;
        private readonly bool wasPlayerControlled;
        private readonly Func<DateTime> utcNow;
        private readonly Func<InteractionAnimationStopReason?> invalidationResolver;
        private DateTime startedUtc;
        private DateTime exitStopAtUtc = DateTime.MaxValue;
        private bool active;
        private bool suspended;
        private bool ended;

        internal InteractionAnimationSession(
            InteractionAnimationContext context,
            IInteractionPresenter presenter,
            Func<DateTime> utcNow = null,
            Func<InteractionAnimationStopReason?> invalidationResolver = null)
        {
            this.context = context;
            this.presenter = presenter;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            this.invalidationResolver = invalidationResolver ?? GetInvalidationReason;
            try { owningRound = StartOfRound.Instance; }
            catch { owningRound = null; }
            try { wasPlayerControlled = context?.Request?.Player?.isPlayerControlled == true; }
            catch { wasPlayerControlled = false; }
        }

        internal InteractionAnimationHandle Handle => context.Handle;
        internal PlayerControllerB Player => context.Request.Player;
        internal string PackId => context.Request.PackId;
        internal string InteractionId => context.Request.InteractionId;
        internal InteractionAnimationPresentationKind PresentationKind =>
            context.Definition.PresentationKind;
        internal bool IsActive => active && !ended;
        internal bool IsSuspended => suspended && !ended;
        internal bool IsEnded => ended;
        internal bool HasResourceOwnership => active && presenter.HasResourceOwnership;

        internal bool TryPreflight(out string reason)
        {
            reason = string.Empty;
            try
            {
                if (presenter == null)
                {
                    reason = "presenter_missing";
                    return false;
                }

                return presenter.TryPreflight(context, out reason);
            }
            catch (Exception exception)
            {
                reason = "presenter_preflight_exception:" + exception.GetType().Name;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] presenter.preflight_failed: " + exception);
                return false;
            }
        }

        internal bool TryStart(out string reason)
        {
            reason = string.Empty;
            try
            {
                if (presenter == null)
                {
                    reason = "presenter_missing";
                    return false;
                }
                if (!presenter.TryStart(context, out reason))
                {
                    TryRestore(InteractionAnimationStopReason.PresenterFailure);
                    return false;
                }
                startedUtc = utcNow();
                exitStopAtUtc = DateTime.MaxValue;
                active = true;
                suspended = false;
                return true;
            }
            catch (Exception exception)
            {
                reason = "presenter_start_exception:" + exception.GetType().Name;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] presenter.start_failed: " + exception);
                TryRestore(InteractionAnimationStopReason.PresenterFailure);
                return false;
            }
        }

        internal bool TrySuspend(out string reason)
        {
            reason = string.Empty;
            if (!active || ended)
                return false;
            try
            {
                presenter.Stop(InteractionAnimationStopReason.Interrupted);
                active = false;
                suspended = true;
                return true;
            }
            catch (Exception exception)
            {
                active = false;
                suspended = false;
                TryRestore(InteractionAnimationStopReason.PresenterFailure);
                ended = true;
                reason = "presenter_suspend_exception:" + exception.GetType().Name;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] presenter.suspend_failed: " + exception);
                return false;
            }
        }

        internal bool TryResume(out string reason)
        {
            reason = string.Empty;
            if (!suspended || ended)
                return false;
            DateTime previousStartedUtc = startedUtc;
            DateTime previousExitStopAtUtc = exitStopAtUtc;
            if (!TryPreflight(out reason) || !TryStart(out reason))
                return false;
            startedUtc = previousStartedUtc;
            exitStopAtUtc = previousExitStopAtUtc;
            return true;
        }

        internal bool TryFinalizeSuspended()
        {
            if (!suspended || ended)
                return false;
            suspended = false;
            ended = true;
            return true;
        }

        internal bool TryStopAndRestore(InteractionAnimationStopReason reason)
        {
            if (ended)
                return false;
            if (active)
                TryRestore(reason);
            active = false;
            suspended = false;
            ended = true;
            return true;
        }

        internal InteractionAnimationStopReason? Tick(float deltaTime)
        {
            if (!active || ended)
                return null;
            InteractionAnimationStopReason? invalidation = invalidationResolver();
            if (invalidation.HasValue)
                return invalidation;

            try
            {
                presenter.Tick(deltaTime);
            }
            catch (Exception exception)
            {
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] presenter.tick_failed: " + exception);
                return InteractionAnimationStopReason.PresenterFailure;
            }

            if (presenter.RequestedStopReason.HasValue)
                return presenter.RequestedStopReason;
            DateTime now = utcNow();
            if (now >= exitStopAtUtc)
                return InteractionAnimationStopReason.NaturalEnd;
            float duration = context.Manifest.durationSeconds;
            if (duration > 0f && (now - startedUtc).TotalSeconds >= duration)
                return InteractionAnimationStopReason.NaturalEnd;
            return null;
        }

        internal float BeginExit()
        {
            if (!active || presenter == null)
                return 0f;
            float seconds = Math.Max(0f, presenter.BeginExit());
            exitStopAtUtc = utcNow().AddSeconds(seconds);
            return seconds;
        }

        internal bool TrySetAnimatorParameter(
            string parameterName,
            AnimatorControllerParameterType parameterType,
            float value)
        {
            return active && presenter != null &&
                presenter.TrySetAnimatorParameter(parameterName, parameterType, value);
        }

        internal InteractionAnimationEndedEventArgs CreateEndedEvent(
            InteractionAnimationStopReason reason)
        {
            return new InteractionAnimationEndedEventArgs(
                Handle, Player, PackId, InteractionId, PresentationKind, reason);
        }

        private InteractionAnimationStopReason? GetInvalidationReason()
        {
            PlayerControllerB player = Player;
            try
            {
                if (player == null || player.gameObject == null ||
                    !player.gameObject.scene.IsValid())
                    return InteractionAnimationStopReason.PlayerInvalidated;
                if (player.isPlayerDead)
                    return InteractionAnimationStopReason.PlayerDied;
                if (wasPlayerControlled && !player.isPlayerControlled)
                    return InteractionAnimationStopReason.PlayerInvalidated;
                if (owningRound != null && StartOfRound.Instance != owningRound)
                    return InteractionAnimationStopReason.RoundUnloaded;
            }
            catch
            {
                return InteractionAnimationStopReason.PlayerInvalidated;
            }
            return null;
        }

        private void TryRestore(InteractionAnimationStopReason reason)
        {
            try
            {
                presenter?.Stop(reason);
            }
            catch (Exception exception)
            {
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] presenter.restore_failed: " + exception);
            }
        }
    }
}
