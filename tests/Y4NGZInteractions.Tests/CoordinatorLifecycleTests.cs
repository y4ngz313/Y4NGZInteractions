using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using GameNetcodeStuff;
using UnityEngine;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class CoordinatorLifecycleTests : IDisposable
{
    private readonly string assetRoot = Path.Combine(
        Path.GetTempPath(), "Y4NGZInteractions.LifecycleTests", Guid.NewGuid().ToString("N"));
    private readonly PlayerControllerB player =
        (PlayerControllerB)RuntimeHelpers.GetUninitializedObject(typeof(PlayerControllerB));
    private InteractionAnimationCoordinator coordinator;

    public CoordinatorLifecycleTests()
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
    public void DedicatedViewmodelRejectsRemotePlayersBeforePresenterCreation()
    {
        var localPlayer =
            (PlayerControllerB)RuntimeHelpers.GetUninitializedObject(
                typeof(PlayerControllerB));
        var presenters = new Queue<FakePresenter>(new[] { new FakePresenter() });
        coordinator = new InteractionAnimationCoordinator(
            null,
            _ => presenters.Dequeue(),
            () => localPlayer,
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
        RegisterTwoViewmodels(coordinator);

        Assert.False(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle handle, out string reason));

        Assert.Equal("dedicated_viewmodel_requires_local_player", reason);
        Assert.Equal(InteractionAnimationHandle.Empty, handle);
        Assert.Single(presenters);
    }
    [Fact]
    public void RejectIfBusyLeavesCurrentSessionUntouched()
    {
        var incumbent = new FakePresenter();
        coordinator = CreateCoordinator(new Queue<FakePresenter>(new[] { incumbent }));
        RegisterTwoViewmodels(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle first, out _));

        Assert.False(coordinator.TryStartInteraction(
            Request("second"), out InteractionAnimationHandle rejected, out string reason));

        Assert.Equal("interaction_resource_busy", reason);
        Assert.Equal(InteractionAnimationHandle.Empty, rejected);
        Assert.True(coordinator.IsInteractionActive(first));
        Assert.Equal(1, incumbent.StartCount);
        Assert.Empty(incumbent.StopReasons);
    }

    [Fact]
    public void FailedReplacementRestoresTheCurrentSession()
    {
        var incumbent = new FakePresenter();
        var replacement = new FakePresenter { StartResult = false };
        coordinator = CreateCoordinator(
            new Queue<FakePresenter>(new[] { incumbent, replacement }));
        RegisterTwoViewmodels(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle first, out _));

        Assert.False(coordinator.TryStartInteraction(
            Request("second", InteractionAnimationConflictPolicy.InterruptExisting),
            out InteractionAnimationHandle failed, out string reason));

        Assert.Equal("fake_start_failed", reason);
        Assert.Equal(InteractionAnimationHandle.Empty, failed);
        Assert.True(coordinator.IsInteractionActive(first));
        Assert.True(coordinator.TryGetActiveInteraction(
            player, InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
            out InteractionAnimationHandle stillActive));
        Assert.Equal(first, stillActive);
        Assert.Equal(2, incumbent.StartCount);
        Assert.Equal(new[] { InteractionAnimationStopReason.Interrupted },
            incumbent.StopReasons);
    }

    [Fact]
    public void FailedReplacementPreflightNeverSuspendsTheCurrentSession()
    {
        var incumbent = new FakePresenter();
        var replacement = new FakePresenter { PreflightResult = false };
        coordinator = CreateCoordinator(
            new Queue<FakePresenter>(new[] { incumbent, replacement }));
        RegisterTwoViewmodels(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle first, out _));

        Assert.False(coordinator.TryStartInteraction(
            Request("second", InteractionAnimationConflictPolicy.InterruptExisting),
            out InteractionAnimationHandle failed, out string reason));

        Assert.Equal("fake_preflight_failed", reason);
        Assert.Equal(InteractionAnimationHandle.Empty, failed);
        Assert.True(coordinator.IsInteractionActive(first));
        Assert.Equal(1, incumbent.StartCount);
        Assert.Empty(incumbent.StopReasons);
        Assert.Equal(0, replacement.StartCount);
    }
    [Fact]
    public void FailedExternalOwnershipPreflightNeverCreatesOrSuspendsReplacement()
    {
        var incumbent = new FakePresenter();
        var replacement = new FakePresenter();
        var presenters = new Queue<FakePresenter>(new[] { incumbent, replacement });
        int ownershipChecks = 0;
        coordinator = new InteractionAnimationCoordinator(
            null,
            _ => presenters.Dequeue(),
            () => player,
            (PlayerControllerB _,
             InteractionAnimationPresentationKind _,
             InteractionAnimationHandle[] _,
             out string reason) =>
            {
                ownershipChecks++;
                bool allowed = ownershipChecks == 1;
                reason = allowed ? string.Empty : "player_animator_owned_externally";
                return allowed;
            },
            (context, presenter) => new InteractionAnimationSession(
                context, presenter, null, () => null));
        RegisterTwoBodies(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle first, out _));

        Assert.False(coordinator.TryStartInteraction(
            Request("second", InteractionAnimationConflictPolicy.InterruptExisting),
            out InteractionAnimationHandle failed, out string reason));

        Assert.Equal("player_animator_owned_externally", reason);
        Assert.Equal(InteractionAnimationHandle.Empty, failed);
        Assert.True(coordinator.IsInteractionActive(first));
        Assert.Empty(incumbent.StopReasons);
        Assert.Single(presenters);
        Assert.Equal(0, replacement.StartCount);
    }
    [Fact]
    public void SuccessfulReplacementRestoresBeforeRaisingEnded()
    {
        var incumbent = new FakePresenter();
        var replacement = new FakePresenter();
        coordinator = CreateCoordinator(
            new Queue<FakePresenter>(new[] { incumbent, replacement }));
        RegisterTwoViewmodels(coordinator);
        LCInteractionAnimationAPI.Initialize(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle first, out _));

        int eventCount = 0;
        InteractionAnimationEndedEventArgs ended = null;
        EventHandler<InteractionAnimationEndedEventArgs> handler = (_, args) =>
        {
            eventCount++;
            Assert.False(incumbent.HasResourceOwnership);
            ended = args;
        };
        LCInteractionAnimationAPI.InteractionEnded += handler;
        try
        {
            Assert.True(coordinator.TryStartInteraction(
                Request("second", InteractionAnimationConflictPolicy.InterruptExisting),
                out InteractionAnimationHandle second, out _));

            Assert.Equal(1, eventCount);
            Assert.Equal(first, ended.Handle);
            Assert.Equal("first", ended.InteractionId);
            Assert.Equal(InteractionAnimationStopReason.Interrupted, ended.StopReason);
            Assert.False(coordinator.IsInteractionActive(first));
            Assert.True(coordinator.IsInteractionActive(second));
        }
        finally
        {
            LCInteractionAnimationAPI.InteractionEnded -= handler;
        }
    }

    [Fact]
    public void StopRaisesExactlyOnceAfterRestoration()
    {
        var presenter = new FakePresenter();
        coordinator = CreateCoordinator(new Queue<FakePresenter>(new[] { presenter }));
        RegisterTwoViewmodels(coordinator);
        LCInteractionAnimationAPI.Initialize(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle handle, out _));

        int eventCount = 0;
        EventHandler<InteractionAnimationEndedEventArgs> handler = (_, args) =>
        {
            Assert.False(presenter.HasResourceOwnership);
            Assert.Equal(InteractionAnimationStopReason.Requested, args.StopReason);
            eventCount++;
        };
        LCInteractionAnimationAPI.InteractionEnded += handler;
        try
        {
            Assert.True(coordinator.TryStopInteraction(
                handle, InteractionAnimationStopReason.Requested));
            Assert.False(coordinator.TryStopInteraction(
                handle, InteractionAnimationStopReason.Requested));
            Assert.Equal(1, eventCount);
            Assert.Equal(new[] { InteractionAnimationStopReason.Requested },
                presenter.StopReasons);
        }
        finally
        {
            LCInteractionAnimationAPI.InteractionEnded -= handler;
        }
    }

    [Theory]
    [InlineData(InteractionAnimationStopReason.Requested)]
    [InlineData(InteractionAnimationStopReason.NaturalEnd)]
    [InlineData(InteractionAnimationStopReason.Interrupted)]
    [InlineData(InteractionAnimationStopReason.PlayerInvalidated)]
    [InlineData(InteractionAnimationStopReason.PlayerDied)]
    [InlineData(InteractionAnimationStopReason.RoundUnloaded)]
    [InlineData(InteractionAnimationStopReason.PresenterFailure)]
    [InlineData(InteractionAnimationStopReason.Shutdown)]
    public void EveryStopReasonRaisesExactlyOnePostRestoreEvent(
        InteractionAnimationStopReason stopReason)
    {
        var presenter = new FakePresenter();
        coordinator = CreateCoordinator(new Queue<FakePresenter>(new[] { presenter }));
        RegisterTwoViewmodels(coordinator);
        LCInteractionAnimationAPI.Initialize(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle handle, out _));

        int eventCount = 0;
        InteractionAnimationEndedEventArgs ended = null;
        EventHandler<InteractionAnimationEndedEventArgs> handler = (_, args) =>
        {
            Assert.False(presenter.HasResourceOwnership);
            eventCount++;
            ended = args;
        };
        LCInteractionAnimationAPI.InteractionEnded += handler;
        try
        {
            Assert.True(coordinator.TryStopInteraction(handle, stopReason));
            Assert.False(coordinator.TryStopInteraction(handle, stopReason));
            Assert.Equal(1, eventCount);
            Assert.Equal(stopReason, ended.StopReason);
            Assert.Equal(handle, ended.Handle);
        }
        finally
        {
            LCInteractionAnimationAPI.InteractionEnded -= handler;
        }
    }
    [Fact]
    public void AllPublicParameterTypesReachThePresenter()
    {
        var presenter = new FakePresenter();
        coordinator = CreateCoordinator(new Queue<FakePresenter>(new[] { presenter }));
        RegisterTwoViewmodels(coordinator);
        LCInteractionAnimationAPI.Initialize(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle handle, out _));

        Assert.True(LCInteractionAnimationAPI.TrySetInteractionBool(handle, "Enabled", true));
        Assert.True(LCInteractionAnimationAPI.TrySetInteractionInt(handle, "Mode", 3));
        Assert.True(LCInteractionAnimationAPI.TrySetInteractionFloat(handle, "Blend", 0.25f));
        Assert.True(LCInteractionAnimationAPI.TryFireInteractionTrigger(handle, "Use"));

        Assert.Equal(new[]
        {
            ("Enabled", AnimatorControllerParameterType.Bool, 1f),
            ("Mode", AnimatorControllerParameterType.Int, 3f),
            ("Blend", AnimatorControllerParameterType.Float, 0.25f),
            ("Use", AnimatorControllerParameterType.Trigger, 0f)
        }, presenter.Parameters);
    }

    [Fact]
    public void PresenterTickFailureStopsWithPresenterFailure()
    {
        var presenter = new FakePresenter { ThrowOnTick = true };
        coordinator = CreateCoordinator(new Queue<FakePresenter>(new[] { presenter }));
        RegisterTwoViewmodels(coordinator);
        LCInteractionAnimationAPI.Initialize(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle handle, out _));

        InteractionAnimationStopReason? endedReason = null;
        EventHandler<InteractionAnimationEndedEventArgs> handler =
            (_, args) => endedReason = args.StopReason;
        LCInteractionAnimationAPI.InteractionEnded += handler;
        try
        {
            coordinator.Tick(0.1f);
            Assert.False(coordinator.IsInteractionActive(handle));
            Assert.Equal(InteractionAnimationStopReason.PresenterFailure, endedReason);
        }
        finally
        {
            LCInteractionAnimationAPI.InteractionEnded -= handler;
        }
    }

    [Fact]
    public void ShutdownRestoresSessionsAndReleasesTheirLeases()
    {
        var firstPresenter = new FakePresenter();
        var afterShutdownPresenter = new FakePresenter();
        var presenters = new Queue<FakePresenter>(new[]
        {
            firstPresenter,
            afterShutdownPresenter
        });
        coordinator = CreateCoordinator(presenters);
        RegisterTwoViewmodels(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("first"), out InteractionAnimationHandle first, out _));

        coordinator.Shutdown();

        Assert.False(coordinator.IsInteractionActive(first));
        Assert.Equal(new[] { InteractionAnimationStopReason.Shutdown },
            firstPresenter.StopReasons);
        RegisterTwoViewmodels(coordinator);
        Assert.True(coordinator.TryStartInteraction(
            Request("second"), out _, out _));
    }

    private InteractionAnimationCoordinator CreateCoordinator(Queue<FakePresenter> presenters)
    {
        return new InteractionAnimationCoordinator(
            null,
            _ => presenters.Dequeue(),
            () => player,
            (PlayerControllerB playerValue,
             InteractionAnimationPresentationKind presentationKind,
             InteractionAnimationHandle[] conflicts,
             out string reason) =>
            {
                reason = string.Empty;
                return true;
            },
            (context, presenter) => new InteractionAnimationSession(
                context, presenter, null, () => null));
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

    private void RegisterTwoViewmodels(InteractionAnimationCoordinator value)
    {
        var pack = new InteractionAnimationPackDefinition
        {
            PackId = "sample.pack",
            Version = "1.0.0",
            AssetRootPath = assetRoot,
            Interactions = new[]
            {
                ViewmodelDefinition("first"),
                ViewmodelDefinition("second")
            }
        };
        Assert.True(value.TryRegisterInteractionPack(pack, out string reason), reason);
    }

    private void RegisterTwoBodies(InteractionAnimationCoordinator value)
    {
        var pack = new InteractionAnimationPackDefinition
        {
            PackId = "sample.pack",
            Version = "1.0.0",
            AssetRootPath = assetRoot,
            Interactions = new[]
            {
                BodyDefinition("first"),
                BodyDefinition("second")
            }
        };
        Assert.True(value.TryRegisterInteractionPack(pack, out string reason), reason);
    }

    private static InteractionAnimationDefinition BodyDefinition(string id)
    {
        return new InteractionAnimationDefinition
        {
            InteractionId = id,
            PresentationKind = InteractionAnimationPresentationKind.BodyWorld,
            ManifestJson = $$"""
            {
              "schemaVersion": 2,
              "interactionId": "{{id}}",
              "durationSeconds": 0,
              "body": {
                "enabled": true,
                "bundleFileName": "sample.bundle",
                "controllerAssetName": "SampleController"
              }
            }
            """
        };
    }
    private static InteractionAnimationDefinition ViewmodelDefinition(string id)
    {
        return new InteractionAnimationDefinition
        {
            InteractionId = id,
            PresentationKind = InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
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
        internal bool PreflightResult { get; set; } = true;
        internal bool StartResult { get; set; } = true;
        internal bool ThrowOnTick { get; set; }
        internal int StartCount { get; private set; }
        internal List<InteractionAnimationStopReason> StopReasons { get; } = new();
        internal List<(string, AnimatorControllerParameterType, float)> Parameters { get; } =
            new();

        public InteractionAnimationStopReason? RequestedStopReason { get; set; }
        public bool HasResourceOwnership { get; private set; }

        public bool TryPreflight(InteractionAnimationContext context, out string reason)
        {
            reason = PreflightResult ? string.Empty : "fake_preflight_failed";
            return PreflightResult;
        }

        public bool TryStart(InteractionAnimationContext context, out string reason)
        {
            StartCount++;
            reason = StartResult ? string.Empty : "fake_start_failed";
            HasResourceOwnership = StartResult;
            return StartResult;
        }

        public void Tick(float deltaTime)
        {
            if (ThrowOnTick)
                throw new InvalidOperationException("Fake presenter failure.");
        }

        public void Stop(InteractionAnimationStopReason stopReason)
        {
            StopReasons.Add(stopReason);
            HasResourceOwnership = false;
        }

        public float BeginExit() => 0f;

        public bool TrySetAnimatorParameter(
            string parameterName,
            AnimatorControllerParameterType parameterType,
            float value)
        {
            Parameters.Add((parameterName, parameterType, value));
            return true;
        }
    }
}
