using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using GameNetcodeStuff;
using UnityEngine;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class PostAnimationCallbackTests : IDisposable
{
    private readonly string assetRoot = Path.Combine(
        Path.GetTempPath(), "Y4NGZInteractions.PostAnimationCallbackTests", Guid.NewGuid().ToString("N"));
    private readonly PlayerControllerB localPlayer =
        (PlayerControllerB)RuntimeHelpers.GetUninitializedObject(typeof(PlayerControllerB));
    private readonly PlayerControllerB remotePlayer =
        (PlayerControllerB)RuntimeHelpers.GetUninitializedObject(typeof(PlayerControllerB));
    private readonly ManualLogSource logger = new ManualLogSource("PostAnimationCallbackTests");
    private readonly List<string> warnings = new List<string>();
    private readonly List<bool> runnerSwitches = new List<bool>();
    private readonly Queue<FramePresenter> presenters = new Queue<FramePresenter>();
    private readonly InteractionAnimationCoordinator coordinator;

    public PostAnimationCallbackTests()
    {
        Directory.CreateDirectory(assetRoot);
        logger.LogEvent += (_, args) =>
        {
            if (args.Level == LogLevel.Warning)
                warnings.Add(args.Data?.ToString() ?? string.Empty);
        };
        coordinator = new InteractionAnimationCoordinator(
            logger,
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
                context, presenter, null, () => null),
            runnerSwitches.Add);
        RegisterPack();
    }

    public void Dispose()
    {
        LCInteractionAnimationAPI.Shutdown();
        coordinator.Shutdown();
        if (Directory.Exists(assetRoot))
            Directory.Delete(assetRoot, true);
    }

    [Fact]
    public void CallbacksRunAfterTheTickInSessionStartOrderWithTheFrameData()
    {
        FramePresenter firstPresenter = Enqueue(InteractionAnimationViewer.LocalThirdPerson);
        FramePresenter secondPresenter = Enqueue(InteractionAnimationViewer.Remote);
        InteractionAnimationHandle first = Start(localPlayer, "body");
        InteractionAnimationHandle second = Start(remotePlayer, "body");
        var frames = new List<InteractionAnimationFrame>();

        Assert.True(coordinator.TrySetPostAnimationCallback(second, frames.Add));
        Assert.True(coordinator.TrySetPostAnimationCallback(first, frames.Add));
        Assert.Equal(new[] { true }, runnerSwitches);

        coordinator.Tick(0.02f);
        coordinator.RunPostAnimationCallbacks(0.02f);

        Assert.Equal(2, frames.Count);
        Assert.Equal(first, frames[0].Handle);
        Assert.Same(localPlayer, frames[0].Player);
        Assert.Equal(InteractionAnimationViewer.LocalThirdPerson, frames[0].Viewer);
        Assert.Equal(firstPresenter.SessionWeight, frames[0].Weight);
        Assert.Equal(0.25f, frames[0].Weight);
        Assert.Equal(0.02f, frames[0].DeltaTime);
        Assert.Equal(second, frames[1].Handle);
        Assert.Same(remotePlayer, frames[1].Player);
        Assert.Equal(InteractionAnimationViewer.Remote, frames[1].Viewer);
        Assert.Equal(1, secondPresenter.TickCount);
    }

    [Fact]
    public void SessionStoppedInTheTickGetsNoCallback()
    {
        FramePresenter stopping = Enqueue(InteractionAnimationViewer.LocalFirstPerson);
        Enqueue(InteractionAnimationViewer.Remote);
        InteractionAnimationHandle first = Start(localPlayer, "body");
        InteractionAnimationHandle second = Start(remotePlayer, "body");
        var called = new List<InteractionAnimationHandle>();
        coordinator.TrySetPostAnimationCallback(first, frame => called.Add(frame.Handle));
        coordinator.TrySetPostAnimationCallback(second, frame => called.Add(frame.Handle));

        stopping.StopOnTick = true;
        coordinator.Tick(0.02f);
        coordinator.RunPostAnimationCallbacks(0.02f);

        Assert.Equal(new[] { InteractionAnimationStopReason.NaturalEnd }, stopping.StopReasons);
        Assert.Equal(new[] { second }, called);
        Assert.Equal(new[] { true }, runnerSwitches);

        Assert.True(coordinator.TrySetPostAnimationCallback(second, null));
        Assert.Equal(new[] { true, false }, runnerSwitches);
    }

    [Fact]
    public void StopFromInsideACallbackUsesAnArmingReasonAndLaterCallbacksStillRun()
    {
        FramePresenter stopped = Enqueue(InteractionAnimationViewer.LocalFirstPerson);
        Enqueue(InteractionAnimationViewer.Remote);
        InteractionAnimationHandle first = Start(localPlayer, "body");
        InteractionAnimationHandle second = Start(remotePlayer, "body");
        int firstCalls = 0;
        int secondCalls = 0;
        coordinator.TrySetPostAnimationCallback(first, frame =>
        {
            firstCalls++;
            Assert.True(coordinator.TryStopInteraction(frame.Handle, InteractionAnimationStopReason.Requested));
        });
        coordinator.TrySetPostAnimationCallback(second, _ => secondCalls++);

        coordinator.RunPostAnimationCallbacks(0.02f);
        coordinator.RunPostAnimationCallbacks(0.02f);

        Assert.Equal(new[] { InteractionAnimationStopReason.Requested }, stopped.StopReasons);
        Assert.True(ExitPoseBlendRules.QualifiesForArming(stopped.StopReasons[0]));
        Assert.False(coordinator.IsInteractionActive(first));
        Assert.Equal(1, firstCalls);
        Assert.Equal(2, secondCalls);
        Assert.Equal(new[] { true }, runnerSwitches);
    }

    [Fact]
    public void CallbackIsAcceptedOnlyForActiveBodyWorldSessions()
    {
        Assert.False(LCInteractionAnimationAPI.TrySetInteractionPostAnimationCallback(
            InteractionAnimationHandle.NewHandle(), _ => { }));
        Assert.False(coordinator.TrySetPostAnimationCallback(InteractionAnimationHandle.NewHandle(), _ => { }));

        Enqueue(InteractionAnimationViewer.LocalFirstPerson);
        InteractionAnimationHandle viewmodel = Start(localPlayer, "viewmodel");
        Assert.False(coordinator.TrySetPostAnimationCallback(viewmodel, _ => { }));

        Enqueue(InteractionAnimationViewer.Remote);
        InteractionAnimationHandle body = Start(remotePlayer, "body");
        Assert.True(coordinator.TrySetPostAnimationCallback(body, _ => { }));
        Assert.True(coordinator.TryStopInteraction(body, InteractionAnimationStopReason.Requested));
        Assert.False(coordinator.TrySetPostAnimationCallback(body, _ => { }));
        Assert.Equal(new[] { true, false }, runnerSwitches);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ThrowingCallbackIsLoggedOnceClearedAndTheOtherCallbackKeepsRunning()
    {
        Enqueue(InteractionAnimationViewer.LocalFirstPerson);
        Enqueue(InteractionAnimationViewer.Remote);
        InteractionAnimationHandle throwing = Start(localPlayer, "body");
        InteractionAnimationHandle steady = Start(remotePlayer, "body");
        int throwingCalls = 0;
        int steadyCalls = 0;
        coordinator.TrySetPostAnimationCallback(throwing, _ =>
        {
            throwingCalls++;
            throw new InvalidOperationException("callback failure");
        });
        coordinator.TrySetPostAnimationCallback(steady, _ => steadyCalls++);

        coordinator.RunPostAnimationCallbacks(0.02f);
        coordinator.RunPostAnimationCallbacks(0.02f);

        Assert.Equal(1, throwingCalls);
        Assert.Equal(2, steadyCalls);
        Assert.True(coordinator.IsInteractionActive(throwing));
        string warning = Assert.Single(warnings);
        Assert.Contains("post_animation_callback_failed", warning);
        Assert.Contains(throwing.ToString(), warning);
        Assert.Contains("callback failure", warning);
        Assert.Equal(new[] { true }, runnerSwitches);
    }

    private FramePresenter Enqueue(InteractionAnimationViewer viewer)
    {
        var presenter = new FramePresenter { Viewer = viewer };
        presenters.Enqueue(presenter);
        return presenter;
    }

    private InteractionAnimationHandle Start(PlayerControllerB player, string interactionId)
    {
        Assert.True(coordinator.TryStartInteraction(
            new InteractionAnimationRequest
            {
                Player = player,
                PackId = "callback.pack",
                InteractionId = interactionId,
                ConflictPolicy = InteractionAnimationConflictPolicy.RejectIfBusy
            },
            out InteractionAnimationHandle handle,
            out string reason), reason);
        return handle;
    }

    private void RegisterPack()
    {
        var pack = new InteractionAnimationPackDefinition
        {
            PackId = "callback.pack",
            Version = "1.0.0",
            AssetRootPath = assetRoot,
            Interactions = new[]
            {
                new InteractionAnimationDefinition
                {
                    InteractionId = "body",
                    PresentationKind = InteractionAnimationPresentationKind.BodyWorld,
                    ManifestJson = """
                    {
                      "schemaVersion": 2,
                      "interactionId": "body",
                      "durationSeconds": 0,
                      "body": {
                        "enabled": true,
                        "bundleFileName": "sample.bundle",
                        "controllerAssetName": "SampleController"
                      }
                    }
                    """
                },
                new InteractionAnimationDefinition
                {
                    InteractionId = "viewmodel",
                    PresentationKind = InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
                    ManifestJson = """
                    {
                      "schemaVersion": 2,
                      "interactionId": "viewmodel",
                      "durationSeconds": 0,
                      "localViewmodel": {
                        "bundleFileName": "sample.bundle",
                        "prefabAssetName": "SampleRig",
                        "controllerAssetName": "SampleController",
                        "cameraAnchorPath": "Rig/CameraAnchor"
                      }
                    }
                    """
                }
            }
        };
        Assert.True(coordinator.TryRegisterInteractionPack(pack, out string reason), reason);
    }

    private sealed class FramePresenter : IInteractionPresenter, IInteractionPostAnimationFrameSource
    {
        internal bool StopOnTick { get; set; }
        internal int TickCount { get; private set; }
        internal List<InteractionAnimationStopReason> StopReasons { get; } = new();

        public InteractionAnimationViewer Viewer { get; set; }
        public float SessionWeight { get; private set; }
        public InteractionAnimationStopReason? RequestedStopReason { get; private set; }
        public bool HasResourceOwnership { get; private set; }

        public bool TryPreflight(InteractionAnimationContext context, out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryStart(InteractionAnimationContext context, out string reason)
        {
            reason = string.Empty;
            HasResourceOwnership = true;
            return true;
        }

        // Each tick moves the weight the callback reports, so a callback sees whether the tick ran.
        public void Tick(float deltaTime)
        {
            TickCount++;
            SessionWeight = TickCount * 0.25f;
            if (StopOnTick)
                RequestedStopReason = InteractionAnimationStopReason.NaturalEnd;
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
            float value) => true;
    }
}
