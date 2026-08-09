using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using GameNetcodeStuff;
using UnityEngine;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class SessionLifecycleEdgeTests : IDisposable
{
    private readonly string assetRoot = Path.Combine(
        Path.GetTempPath(), "Y4NGZInteractions.SessionTests", Guid.NewGuid().ToString("N"));
    private readonly PlayerControllerB player =
        (PlayerControllerB)RuntimeHelpers.GetUninitializedObject(typeof(PlayerControllerB));
    private InteractionAnimationCoordinator coordinator;

    public SessionLifecycleEdgeTests()
    {
        Directory.CreateDirectory(assetRoot);
    }

    public void Dispose()
    {
        LCInteractionAnimationAPI.Shutdown();
        coordinator?.Shutdown();
        if (Directory.Exists(assetRoot))
            Directory.Delete(assetRoot, true);
    }

    [Fact]
    public void FailedSuspendEndsIncumbentAndReleasesItsLease()
    {
        var incumbent = new FakePresenter { StopFailuresRemaining = 1 };
        var replacement = new FakePresenter();
        var afterFailure = new FakePresenter();
        var presenters = new Queue<FakePresenter>(new[]
        {
            incumbent,
            replacement,
            afterFailure
        });
        coordinator = CreateCoordinator(presenters);
        RegisterPack();
        LCInteractionAnimationAPI.Initialize(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle first, out _));

        int eventCount = 0;
        InteractionAnimationEndedEventArgs ended = null;
        EventHandler<InteractionAnimationEndedEventArgs> handler = (_, args) =>
        {
            eventCount++;
            ended = args;
        };
        LCInteractionAnimationAPI.InteractionEnded += handler;
        try
        {
            Assert.False(coordinator.TryStartInteraction(
                Request("second", InteractionAnimationConflictPolicy.InterruptExisting),
                out InteractionAnimationHandle failed, out string reason));

            Assert.Equal(InteractionAnimationHandle.Empty, failed);
            Assert.Equal("presenter_suspend_exception:InvalidOperationException", reason);
            Assert.False(coordinator.IsInteractionActive(first));
            Assert.Equal(1, eventCount);
            Assert.Equal(first, ended.Handle);
            Assert.Equal(InteractionAnimationStopReason.PresenterFailure, ended.StopReason);
            Assert.False(incumbent.HasResourceOwnership);
            Assert.Equal(2, incumbent.StopCallCount);

            Assert.True(coordinator.TryStartInteraction(
                Request("second"), out InteractionAnimationHandle recovered, out reason), reason);
            Assert.True(coordinator.IsInteractionActive(recovered));
            Assert.Equal(1, afterFailure.StartCount);
        }
        finally
        {
            LCInteractionAnimationAPI.InteractionEnded -= handler;
        }
    }

    [Fact]
    public void ResumedSessionKeepsItsOriginalNaturalEndDeadline()
    {
        DateTime now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
        var presenter = new FakePresenter();
        InteractionAnimationSession session = CreateSession(
            presenter,
            10f,
            () => now,
            () => null);

        Assert.True(session.TryPreflight(out _));
        Assert.True(session.TryStart(out _));
        now = now.AddSeconds(4);
        Assert.True(session.TrySuspend(out _));
        now = now.AddSeconds(1);
        Assert.True(session.TryResume(out _));

        now = now.AddSeconds(5.1);
        Assert.Equal(
            InteractionAnimationStopReason.NaturalEnd,
            session.Tick(0.1f));
        Assert.Equal(2, presenter.StartCount);
    }

    [Theory]
    [InlineData(InteractionAnimationStopReason.PlayerInvalidated)]
    [InlineData(InteractionAnimationStopReason.PlayerDied)]
    [InlineData(InteractionAnimationStopReason.RoundUnloaded)]
    public void LifecycleInvalidationStopsBeforePresenterTick(
        InteractionAnimationStopReason reason)
    {
        var presenter = new FakePresenter();
        InteractionAnimationSession session = CreateSession(
            presenter,
            0f,
            () => DateTime.UtcNow,
            () => reason);

        Assert.True(session.TryPreflight(out _));
        Assert.True(session.TryStart(out _));

        Assert.Equal(reason, session.Tick(0.1f));
        Assert.Equal(0, presenter.TickCount);
    }

    private InteractionAnimationCoordinator CreateCoordinator(
        Queue<FakePresenter> presenters)
    {
        return new InteractionAnimationCoordinator(
            null,
            _ => presenters.Dequeue(),
            () => player,
            (PlayerControllerB _,
             InteractionAnimationPresentationKind _,
             InteractionAnimationHandle[] _,
             out string reason) =>
            {
                reason = string.Empty;
                return true;
            },
            (context, presenter) => new InteractionAnimationSession(
                context, presenter, null, () => null));
    }

    private InteractionAnimationSession CreateSession(
        FakePresenter presenter,
        float durationSeconds,
        Func<DateTime> utcNow,
        Func<InteractionAnimationStopReason?> invalidationResolver)
    {
        var request = new InteractionAnimationRequest
        {
            Player = player,
            PackId = "sample.pack",
            InteractionId = "first"
        };
        var definition = new InteractionAnimationDefinition
        {
            InteractionId = "first",
            PresentationKind =
                InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
            ManifestJson = "{}"
        };
        var manifest = new InteractionAnimationManifest
        {
            interactionId = "first",
            durationSeconds = durationSeconds
        };
        var context = new InteractionAnimationContext(
            InteractionAnimationHandle.NewHandle(),
            request,
            definition,
            manifest,
            assetRoot,
            null);
        return new InteractionAnimationSession(
            context, presenter, utcNow, invalidationResolver);
    }

    private InteractionAnimationRequest Request(
        string interactionId,
        InteractionAnimationConflictPolicy policy =
            InteractionAnimationConflictPolicy.RejectIfBusy)
    {
        return new InteractionAnimationRequest
        {
            Player = player,
            PackId = "sample.pack",
            InteractionId = interactionId,
            ConflictPolicy = policy
        };
    }

    private void RegisterPack()
    {
        var pack = new InteractionAnimationPackDefinition
        {
            PackId = "sample.pack",
            Version = "1.0.0",
            AssetRootPath = assetRoot,
            Interactions = new[]
            {
                Definition("first"),
                Definition("second")
            }
        };
        Assert.True(coordinator.TryRegisterInteractionPack(pack, out string reason), reason);
    }

    private static InteractionAnimationDefinition Definition(string id)
    {
        return new InteractionAnimationDefinition
        {
            InteractionId = id,
            PresentationKind =
                InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
            ManifestJson = $$"""
            {
              "schemaVersion": 2,
              "interactionId": "{{id}}",
              "durationSeconds": 0,
              "localViewmodel": {
                "bundleFileName": "sample.bundle",
                "prefabAssetName": "SampleRig",
                "controllerAssetName": "SampleController",
                "cameraAnchorPath": "Rig/CameraAnchor"
              }
            }
            """
        };
    }

    private sealed class FakePresenter : IInteractionPresenter
    {
        internal int StopFailuresRemaining { get; set; }
        internal int StartCount { get; private set; }
        internal int StopCallCount { get; private set; }
        internal int TickCount { get; private set; }

        public InteractionAnimationStopReason? RequestedStopReason => null;
        public bool HasResourceOwnership { get; private set; }

        public bool TryPreflight(InteractionAnimationContext context, out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryStart(InteractionAnimationContext context, out string reason)
        {
            reason = string.Empty;
            StartCount++;
            HasResourceOwnership = true;
            return true;
        }

        public void Tick(float deltaTime)
        {
            TickCount++;
        }

        public void Stop(InteractionAnimationStopReason stopReason)
        {
            StopCallCount++;
            if (StopFailuresRemaining > 0)
            {
                StopFailuresRemaining--;
                throw new InvalidOperationException("Synthetic suspend failure.");
            }
            HasResourceOwnership = false;
        }

        public float BeginExit() => 0f;

        public bool TrySetAnimatorParameter(
            string parameterName,
            AnimatorControllerParameterType parameterType,
            float value)
        {
            return true;
        }
    }
}
