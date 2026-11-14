using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GameNetcodeStuff;
using UnityEngine;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

// The Unity reference assemblies carry no method bodies, so vectors and rotations are built and
// compared through their fields only.
public sealed class ExitPoseBlendCoreTests : IDisposable
{
    private static readonly Quaternion Identity = Q(0f, 0f, 0f, 1f);

    public void Dispose()
    {
        BlendingSettings.SmoothBlending = true;
        ExitPoseBlend.Shutdown();
    }

    [Fact]
    public void BlendStartsOnStopPoseAndEndsExactlyOnTargetThenFinishes()
    {
        var rig = new SimulatedRig(V(0f, 0f, 0f));
        rig.Arm(1, V(1f, 0f, 0f));
        AssertPosition(V(0f, 0f, 0f), rig.Positions[0]);

        Assert.False(rig.Pass(1, 0.016f));
        Assert.Equal(0f, rig.Positions[0].x);
        Assert.False(rig.Pass(2, 0.1f));
        Assert.Equal(0.5f, rig.Positions[0].x, 6);
        Assert.True(rig.Pass(3, 0.1f));
        Assert.Equal(1f, rig.Positions[0].x);
        Assert.False(rig.Core.IsLive);
    }

    [Fact]
    public void TargetChangedMidBlendIsFollowedFromStartPose()
    {
        Quaternion start = Identity;
        Quaternion firstTarget = YRotation(30f);
        Quaternion secondTarget = YRotation(80f);
        var rig = new SimulatedRig(V(0f, 0f, 0f), start);
        rig.Arm(1, V(0f, 0f, 0f), start);

        rig.DriveRotation(0, firstTarget);
        rig.Pass(2, 0.05f);
        rig.DriveRotation(0, secondTarget);
        rig.Pass(3, 0.05f);

        AssertRotation(ReferenceNlerp(start, secondTarget, 0.5f), rig.Rotations[0]);
        rig.DriveRotation(0, secondTarget);
        Assert.True(rig.Pass(4, 0.1f));
        Assert.True(ExitPoseBlendCore.SameExact(secondTarget, rig.Rotations[0]));
    }

    [Fact]
    public void UndrivenBoneEndsOnRestorePoseAndDrivenBoneFollowsAnimator()
    {
        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));
        rig.Arm(1, V(1f, 0f, 0f), V(0f, 1f, 0f));
        Vector3 animatorValue = V(0f, 2f, 0f);

        bool ended = false;
        for (int frame = 2; frame < 10 && !ended; frame++)
        {
            rig.PrePass();
            rig.DrivePosition(1, animatorValue);
            ended = rig.Pass(frame, 0.05f);
        }

        Assert.True(ended);
        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(animatorValue, rig.Positions[1]);
    }

    [Fact]
    public void CancelAfterPassGivesDrivenLastAnimatorValueAndUndrivenRestorePoseThenWritesNothing()
    {
        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));
        rig.Arm(1, V(1f, 0f, 0f), V(0f, 1f, 0f));
        rig.DrivePosition(1, V(0f, 3f, 0f));
        rig.Pass(2, 0.05f);

        rig.Cancel(ExitPoseBlendCancelKind.BeforeNewSession);

        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 3f, 0f), rig.Positions[1]);
        Assert.True(rig.Pass(3, 0.05f));
        AssertNothingWritten(rig);
        rig.PrePass();
        AssertNothingWritten(rig);
    }

    [Fact]
    public void CancelBeforeFirstPassWritesRestorePoseOnEveryBone()
    {
        var rig = new SimulatedRig(V(0f, 0f, 0f), V(5f, 0f, 0f));
        rig.Arm(1, V(1f, 0f, 0f), V(0f, 1f, 0f));
        AssertPosition(V(0f, 0f, 0f), rig.Positions[0]);

        rig.DrivePosition(1, V(9f, 9f, 9f));
        rig.Cancel(ExitPoseBlendCancelKind.BeforeNewSession);

        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 1f, 0f), rig.Positions[1]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void DestroyedPlayerOrBoneDropsTheBlendWithoutWriting(bool playerValid, bool boneDestroyed)
    {
        Assert.Equal(
            ExitPoseBlendPassDecision.Drop,
            ExitPoseBlendRules.DecidePass(
                playerValid,
                playerDead: true,
                playerLostControl: true,
                roundChanged: true,
                animatorActiveAndEnabled: false,
                culled: true,
                boneDestroyed,
                controllerChanged: true));

        var rig = new SimulatedRig(V(0f, 0f, 0f));
        rig.Arm(1, V(1f, 0f, 0f));
        rig.Pass(2, 0.05f);
        Vector3 before = rig.Positions[0];

        rig.Cancel(ExitPoseBlendCancelKind.Drop);

        AssertNothingWritten(rig);
        AssertPosition(before, rig.Positions[0]);
        Assert.False(rig.Core.IsLive);
    }

    [Theory]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(false, true, false, true, false, false)]
    [InlineData(false, false, true, true, false, false)]
    [InlineData(false, false, false, false, false, false)]
    [InlineData(false, false, false, true, true, false)]
    [InlineData(false, false, false, true, false, true)]
    public void BodyThatStillExistsReleasesUntouchedChannelsToRestorePose(
        bool playerDead,
        bool playerLostControl,
        bool roundChanged,
        bool animatorActive,
        bool culled,
        bool controllerChanged)
    {
        Assert.Equal(
            ExitPoseBlendPassDecision.Release,
            ExitPoseBlendRules.DecidePass(
                true,
                playerDead,
                playerLostControl,
                roundChanged,
                animatorActive,
                culled,
                boneDestroyed: false,
                controllerChanged));

        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));
        rig.Arm(1, V(1f, 0f, 0f), V(0f, 1f, 0f));
        rig.PrePass();
        rig.DrivePosition(1, V(0f, 2f, 0f));
        rig.Pass(1, 0.016f);
        rig.PrePass();
        rig.DrivePosition(1, V(0f, 3f, 0f));

        rig.Cancel(ExitPoseBlendCancelKind.Release);

        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        Assert.True(ExitPoseBlendCore.SameExact(Identity, rig.Rotations[0]));
        AssertPosition(V(0f, 3f, 0f), rig.Positions[1]);
        Assert.False(rig.Core.WritePositions[1]);
        Assert.False(rig.Core.IsLive);
        rig.PrePass();
        AssertNothingWritten(rig);
    }

    [Fact]
    public void ReleaseBeforeAnyPassPutsRestorePoseOnEveryChannelStillHoldingTheStopPose()
    {
        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));
        Quaternion restored = YRotation(20f);
        rig.Arm(1, new[] { V(1f, 0f, 0f), V(0f, 1f, 0f) }, new[] { restored, restored });
        rig.DriveRotation(1, YRotation(70f));

        rig.Cancel(ExitPoseBlendCancelKind.Release);

        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 1f, 0f), rig.Positions[1]);
        Assert.True(ExitPoseBlendCore.SameExact(restored, rig.Rotations[0]));
        Assert.True(ExitPoseBlendCore.SameExact(YRotation(70f), rig.Rotations[1]));
        Assert.True(rig.Core.WritePositions[1]);
        Assert.False(rig.Core.WriteRotations[1]);
    }

    [Fact]
    public void ControllerChangeLeavesDrivenBonesAndRestoresUndrivenBones()
    {
        Assert.Equal(
            ExitPoseBlendPassDecision.Release,
            ExitPoseBlendRules.DecidePass(true, false, false, false, true, false, false, true));
        Assert.Equal(
            ExitPoseBlendPassDecision.Step,
            ExitPoseBlendRules.DecidePass(true, false, false, false, true, false, false, false));

        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));
        rig.Arm(1, V(1f, 0f, 0f), V(0f, 1f, 0f));
        rig.Pass(2, 0.05f);
        Vector3 foreignPosition = V(7f, 7f, 7f);
        Quaternion foreignRotation = YRotation(10f);
        rig.DrivePosition(1, foreignPosition);
        rig.DriveRotation(1, foreignRotation);

        rig.Cancel(ExitPoseBlendCancelKind.Release);

        Assert.True(rig.Core.WritePositions[0]);
        Assert.True(rig.Core.WriteRotations[0]);
        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        Assert.True(ExitPoseBlendCore.SameExact(Identity, rig.Rotations[0]));
        Assert.False(rig.Core.WritePositions[1]);
        Assert.False(rig.Core.WriteRotations[1]);
        AssertPosition(foreignPosition, rig.Positions[1]);
        Assert.True(ExitPoseBlendCore.SameExact(foreignRotation, rig.Rotations[1]));
    }

    [Fact]
    public void ArmingNeedsRestoredAnimatedVisibleLivingBody()
    {
        Assert.True(ExitPoseBlendRules.CanArm(true, true, false, false, false, 12));
        Assert.False(ExitPoseBlendRules.CanArm(false, true, false, false, false, 12));
        Assert.False(ExitPoseBlendRules.CanArm(true, false, false, false, false, 12));
        Assert.False(ExitPoseBlendRules.CanArm(true, true, true, false, false, 12));
        Assert.False(ExitPoseBlendRules.CanArm(true, true, false, true, false, 12));
        Assert.False(ExitPoseBlendRules.CanArm(true, true, false, false, true, 12));
        Assert.False(ExitPoseBlendRules.CanArm(true, true, false, false, false, 0));

        Assert.True(ExitPoseBlendRules.IsCulled(alwaysAnimate: false, anyRendererVisible: false));
        Assert.False(ExitPoseBlendRules.IsCulled(alwaysAnimate: true, anyRendererVisible: false));
        Assert.False(ExitPoseBlendRules.IsCulled(alwaysAnimate: false, anyRendererVisible: true));
    }

    [Theory]
    [InlineData(InteractionAnimationStopReason.Requested, true, true)]
    [InlineData(InteractionAnimationStopReason.Requested, false, true)]
    [InlineData(InteractionAnimationStopReason.NaturalEnd, true, true)]
    [InlineData(InteractionAnimationStopReason.NaturalEnd, false, true)]
    [InlineData(InteractionAnimationStopReason.PresenterFailure, true, true)]
    [InlineData(InteractionAnimationStopReason.PresenterFailure, false, true)]
    [InlineData(InteractionAnimationStopReason.Interrupted, true, true)]
    [InlineData(InteractionAnimationStopReason.Interrupted, false, false)]
    [InlineData(InteractionAnimationStopReason.PlayerDied, true, false)]
    [InlineData(InteractionAnimationStopReason.PlayerInvalidated, true, false)]
    [InlineData(InteractionAnimationStopReason.RoundUnloaded, true, false)]
    [InlineData(InteractionAnimationStopReason.Shutdown, true, false)]
    public void StopQualifiesByReasonAndInterruptedOnlyWhileThePlayerIsControlled(
        InteractionAnimationStopReason reason,
        bool playerControlled,
        bool qualifies)
    {
        Assert.Equal(qualifies, ExitPoseBlendRules.QualifiesForArming(reason, suspend: false, playerControlled));
        Assert.False(ExitPoseBlendRules.QualifiesForArming(reason, suspend: true, playerControlled));
    }

    [Fact]
    public void SuspendStopCapturesNothingAndTheNextStopIsNotASuspend()
    {
        PlayerControllerB player = Player(controlled: true);

        Assert.Equal(0, CountRigResolves(() => Assert.Null(ExitPoseBlend.CaptureStartPose(
            player, null, InteractionAnimationStopReason.Interrupted, suspend: true))));
        Assert.Equal(1, CountRigResolves(() => Assert.Null(ExitPoseBlend.CaptureStartPose(
            player, null, InteractionAnimationStopReason.Interrupted, suspend: false))));
        Assert.Equal(0, ExitPoseBlend.LiveCount);
        Assert.False(ExitPoseBlend.RunnerEnabled);
    }

    [Fact]
    public void SwitchOffOrNonQualifyingStopNeverResolvesBonesCapturesOrEnablesRunner()
    {
        PlayerControllerB controlled = Player(controlled: true);
        PlayerControllerB uncontrolled = Player(controlled: false);

        BlendingSettings.SmoothBlending = false;
        Assert.Equal(0, CountRigResolves(() =>
        {
            Assert.Null(ExitPoseBlend.CaptureStartPose(
                controlled, null, InteractionAnimationStopReason.Requested, suspend: false));
            Assert.Null(ExitPoseBlend.CaptureStartPose(
                controlled, null, InteractionAnimationStopReason.Interrupted, suspend: false));
            ExitPoseBlend.Arm(null, animatorRestored: true);
        }));
        Assert.Equal(0, ExitPoseBlend.LiveCount);
        Assert.False(ExitPoseBlend.RunnerEnabled);

        BlendingSettings.SmoothBlending = true;
        Assert.Equal(0, CountRigResolves(() =>
        {
            Assert.Null(ExitPoseBlend.CaptureStartPose(
                controlled, null, InteractionAnimationStopReason.PlayerDied, suspend: false));
            Assert.Null(ExitPoseBlend.CaptureStartPose(
                uncontrolled, null, InteractionAnimationStopReason.Interrupted, suspend: false));
        }));
        Assert.Equal(2, CountRigResolves(() =>
        {
            Assert.Null(ExitPoseBlend.CaptureStartPose(
                uncontrolled, null, InteractionAnimationStopReason.Requested, suspend: false));
            Assert.Null(ExitPoseBlend.CaptureStartPose(
                controlled, null, InteractionAnimationStopReason.Interrupted, suspend: false));
        }));
        Assert.False(ExitPoseBlend.RunnerEnabled);
    }

    [Theory]
    [InlineData(InteractionAnimationStopReason.Requested)]
    [InlineData(InteractionAnimationStopReason.NaturalEnd)]
    [InlineData(InteractionAnimationStopReason.Interrupted)]
    [InlineData(InteractionAnimationStopReason.PresenterFailure)]
    [InlineData(InteractionAnimationStopReason.PlayerInvalidated)]
    [InlineData(InteractionAnimationStopReason.PlayerDied)]
    [InlineData(InteractionAnimationStopReason.RoundUnloaded)]
    [InlineData(InteractionAnimationStopReason.Shutdown)]
    public void DeadPlayerNeverArmsWhateverTheReason(InteractionAnimationStopReason reason)
    {
        Assert.False(Arms(reason, playerDead: true));
    }

    [Fact]
    public void BlendArmedBeforeTheBodyDiesReleasesAndTheDeadBodyNeverArms()
    {
        Assert.False(Arms(InteractionAnimationStopReason.Interrupted, playerDead: true, culled: true));

        Quaternion restored = YRotation(40f);
        SimulatedRig rig = ArmTwoBones(restored);
        rig.PrePass();
        Vector3 deathPose = V(0f, 2f, 0f);
        rig.DrivePosition(1, deathPose);

        Assert.Equal(ExitPoseBlendPassDecision.Release, DecideLivePass(playerDead: true));
        rig.Cancel(ExitPoseBlendCancelKind.Release);

        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(deathPose, rig.Positions[1]);
        Assert.True(ExitPoseBlendCore.SameExact(restored, rig.Rotations[1]));
        Assert.False(rig.Core.IsLive);
    }

    [Fact]
    public void InterruptedStopAfterDisconnectArmsNothingAndBeforeItReleasesOnLostControl()
    {
        Assert.False(Arms(InteractionAnimationStopReason.Interrupted, playerControlled: false));
        Assert.True(Arms(InteractionAnimationStopReason.Interrupted, playerControlled: true));

        Quaternion restored = YRotation(40f);
        SimulatedRig rig = ArmTwoBones(restored);
        rig.PrePass();
        Assert.All(rig.Core.WritePositions, write => Assert.True(write));
        Assert.All(rig.Core.WriteRotations, write => Assert.True(write));
        AssertRestorePose(rig, restored);

        Assert.Equal(ExitPoseBlendPassDecision.Release, DecideLivePass(playerLostControl: true));
        rig.Cancel(ExitPoseBlendCancelKind.Release);

        AssertRestorePose(rig, restored);
        Assert.False(rig.Core.IsLive);
        rig.PrePass();
        AssertNothingWritten(rig);
    }

    [Fact]
    public void RoundChangeAfterArmReleasesAndASameRoundInterruptedStopBlendsToItsEnd()
    {
        Assert.True(Arms(InteractionAnimationStopReason.Interrupted));
        Quaternion restored = YRotation(40f);

        SimulatedRig changed = ArmTwoBones(restored);
        changed.PrePass();
        Assert.Equal(ExitPoseBlendPassDecision.Release, DecideLivePass(roundChanged: true));
        changed.Cancel(ExitPoseBlendCancelKind.Release);
        AssertRestorePose(changed, restored);
        Assert.False(changed.Core.IsLive);

        Assert.Equal(ExitPoseBlendPassDecision.Step, DecideLivePass());
        SimulatedRig same = ArmTwoBones(restored);
        same.PrePass();
        Assert.False(same.Pass(2, 0.1f));
        Assert.Equal(0.5f, same.Positions[0].x, 6);
        same.PrePass();
        Assert.True(same.Pass(3, 0.1f));
        AssertRestorePose(same, restored);
    }

    [Fact]
    public void HiddenOrInactiveBodyAtStopArmsNothingAndHidingAfterArmReleases()
    {
        Assert.False(Arms(InteractionAnimationStopReason.Interrupted, animatorActive: false));
        Assert.False(Arms(InteractionAnimationStopReason.Interrupted, culled: true));
        Assert.Equal(ExitPoseBlendPassDecision.Release, DecideLivePass(animatorActive: false));
        Assert.Equal(ExitPoseBlendPassDecision.Release, DecideLivePass(culled: true));

        Quaternion restored = YRotation(40f);
        SimulatedRig rig = ArmTwoBones(restored);
        rig.PrePass();
        rig.Pass(2, 0.05f);
        rig.Cancel(ExitPoseBlendCancelKind.Release);

        AssertRestorePose(rig, restored);
        Assert.False(rig.Core.IsLive);
    }

    [Fact]
    public void ShutdownStopArmsNothingAndShutdownGivesEveryLiveBlendItsFinalValues()
    {
        Assert.False(Arms(InteractionAnimationStopReason.Shutdown));

        Quaternion restored = YRotation(40f);
        SimulatedRig beforePass = ArmTwoBones(restored);
        SimulatedRig afterPass = ArmTwoBones(restored);
        afterPass.PrePass();
        afterPass.DrivePosition(1, V(0f, 2f, 0f));
        afterPass.Pass(2, 0.05f);

        // ExitPoseBlend.Shutdown cancels every live blend as before a new session.
        beforePass.Cancel(ExitPoseBlendCancelKind.BeforeNewSession);
        afterPass.Cancel(ExitPoseBlendCancelKind.BeforeNewSession);
        ExitPoseBlend.Shutdown();

        AssertRestorePose(beforePass, restored);
        AssertPosition(V(1f, 0f, 0f), afterPass.Positions[0]);
        AssertPosition(V(0f, 2f, 0f), afterPass.Positions[1]);
        Assert.True(ExitPoseBlendCore.SameExact(restored, afterPass.Rotations[1]));
        Assert.False(beforePass.Core.IsLive);
        Assert.False(afterPass.Core.IsLive);
        Assert.Equal(0, ExitPoseBlend.LiveCount);
        Assert.False(ExitPoseBlend.RunnerEnabled);
    }

    [Fact]
    public void SameFrameInterruptedStopThenStartPutsRestorePoseOnEveryBoneBeforeCapture()
    {
        Assert.True(Arms(InteractionAnimationStopReason.Interrupted));

        Quaternion restored = YRotation(40f);
        SimulatedRig rig = ArmTwoBones(restored);
        rig.Cancel(ExitPoseBlendCancelKind.BeforeNewSession);

        Assert.All(rig.Core.WritePositions, write => Assert.True(write));
        Assert.All(rig.Core.WriteRotations, write => Assert.True(write));
        AssertRestorePose(rig, restored);
        Assert.False(rig.Core.IsLive);
    }

    [Fact]
    public void PrePassPutsRestorePoseOnEveryChannelTheBlendWroteAtArmAndMidBlend()
    {
        Quaternion restored = YRotation(40f);
        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));
        rig.Arm(1, new[] { V(1f, 0f, 0f), V(0f, 1f, 0f) }, new[] { Identity, restored });
        AssertPosition(V(0f, 0f, 0f), rig.Positions[1]);

        rig.PrePass();
        AssertRestorePose(rig, restored);

        rig.DrivePosition(1, V(0f, 2f, 0f));
        rig.Pass(1, 0.016f);
        AssertPosition(V(0f, 0f, 0f), rig.Positions[1]);
        rig.PrePass();
        AssertRestorePose(rig, restored);

        rig.DrivePosition(1, V(0f, 2f, 0f));
        Assert.False(rig.Pass(2, 0.05f));
        Assert.Equal(0.15625f, rig.Positions[0].x, 6);
        Assert.Equal(0.3125f, rig.Positions[1].y, 6);
        rig.PrePass();
        AssertRestorePose(rig, restored);
    }

    [Fact]
    public void PrePassLeavesAChannelAnotherWriterChangedAndThePassFollowsIt()
    {
        Quaternion restored = YRotation(30f);
        var rig = new SimulatedRig(V(0f, 0f, 0f));
        rig.Arm(1, V(1f, 0f, 0f), restored);
        rig.PrePass();
        rig.Pass(1, 0.016f);
        rig.PrePass();
        rig.Pass(2, 0.05f);

        Vector3 foreign = V(5f, 5f, 5f);
        rig.DrivePosition(0, foreign);
        rig.PrePass();

        AssertPosition(foreign, rig.Positions[0]);
        Assert.False(rig.Core.WritePositions[0]);
        Assert.True(ExitPoseBlendCore.SameExact(restored, rig.Rotations[0]));

        rig.Pass(3, 0.05f);
        Assert.Equal(2.5f, rig.Positions[0].x, 6);
        Assert.Equal(2.5f, rig.Positions[0].y, 6);
        AssertRotation(ReferenceNlerp(Identity, restored, 0.5f), rig.Rotations[0]);
    }

    [Fact]
    public void ArmAfterTheFramesPassRendersStopPoseThenStepsFromThePrePassNextFrame()
    {
        RunIdlePrePass(7);
        Assert.True(ExitPoseBlend.AnimationPassRan(7));
        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));

        rig.Arm(7, TwoRestoredPositions(), new[] { Identity, Identity }, ExitPoseBlend.AnimationPassRan(7));
        AssertPosition(V(0f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 0f, 0f), rig.Positions[1]);

        rig.PrePass();
        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 1f, 0f), rig.Positions[1]);
        rig.DrivePosition(1, V(0f, 2f, 0f));
        Assert.False(rig.Pass(8, 0.05f));
        Assert.Equal(0.15625f, rig.Positions[0].x, 6);
        Assert.Equal(0.3125f, rig.Positions[1].y, 6);
    }

    [Fact]
    public void ArmBeforeTheFramesPassLeavesRestorePoseThenThatPassRendersStopPoseAndStepsTheNext()
    {
        RunIdlePrePass(6);
        Assert.False(ExitPoseBlend.AnimationPassRan(7));
        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));

        rig.Arm(7, TwoRestoredPositions(), new[] { Identity, Identity }, ExitPoseBlend.AnimationPassRan(7));
        AssertNothingWritten(rig);
        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 1f, 0f), rig.Positions[1]);

        rig.PrePass();
        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 1f, 0f), rig.Positions[1]);
        rig.DrivePosition(1, V(0f, 2f, 0f));
        rig.Pass(7, 0.05f);
        AssertPosition(V(0f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 0f, 0f), rig.Positions[1]);
        Assert.Equal(0f, rig.Core.ElapsedSeconds);

        rig.PrePass();
        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 1f, 0f), rig.Positions[1]);
        rig.DrivePosition(1, V(0f, 2f, 0f));
        rig.Pass(8, 0.05f);
        Assert.Equal(0.15625f, rig.Positions[0].x, 6);
        Assert.Equal(0.3125f, rig.Positions[1].y, 6);
    }

    [Fact]
    public void ControllerSwapInTheSameCallAsABeforePassArmReadsRestorePoseOnEveryBone()
    {
        RunIdlePrePass(6);
        Quaternion restored = YRotation(40f);
        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));
        rig.Arm(7, TwoRestoredPositions(), new[] { Identity, restored }, ExitPoseBlend.AnimationPassRan(7));

        // The swap and its rig rebuild read the bones here, before the frame's pre-pass.
        AssertRestorePose(rig, restored);

        rig.PrePass();
        AssertRestorePose(rig, restored);
        Vector3 swappedKey = V(0f, 3f, 0f);
        rig.DrivePosition(1, swappedKey);
        Assert.Equal(
            ExitPoseBlendPassDecision.Release,
            ExitPoseBlendRules.DecidePass(true, false, false, false, true, false, false, controllerChanged: true));
        rig.Cancel(ExitPoseBlendCancelKind.Release);

        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        Assert.True(ExitPoseBlendCore.SameExact(Identity, rig.Rotations[0]));
        AssertPosition(swappedKey, rig.Positions[1]);
        Assert.True(ExitPoseBlendCore.SameExact(restored, rig.Rotations[1]));
        Assert.False(rig.Core.IsLive);
    }

    [Fact]
    public void ChannelWhoseStoredRotationDiffersByOneUlpStillReadsAsUndriven()
    {
        Quaternion restored = YRotation(60f);
        var rig = new SimulatedRig(V(0f, 0f, 0f), Identity)
        {
            StoreRotation = rotation => Q(rotation.x, rotation.y, rotation.z, MathF.BitIncrement(rotation.w))
        };
        rig.Arm(1, V(0f, 0f, 0f), restored);

        rig.PrePass();
        AssertRotation(restored, rig.Rotations[0]);
        rig.Pass(1, 0.016f);
        rig.PrePass();
        AssertRotation(restored, rig.Rotations[0]);
        rig.Pass(2, 0.05f);
        AssertRotation(ReferenceNlerp(Identity, restored, 0.15625f), rig.Rotations[0]);
        rig.PrePass();
        AssertRotation(restored, rig.Rotations[0]);
        rig.Pass(3, 0.05f);
        AssertRotation(ReferenceNlerp(Identity, restored, 0.5f), rig.Rotations[0]);
        rig.PrePass();

        Assert.True(rig.Pass(4, 0.1f));
        AssertRotation(restored, rig.Rotations[0]);
    }

    [Fact]
    public void HitchLongerThanBlendWritesTargetAndEnds()
    {
        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));
        rig.Arm(1, V(1f, 0f, 0f), V(0f, 1f, 0f));
        rig.DrivePosition(1, V(0f, 4f, 0f));

        Assert.True(rig.Pass(2, 0.5f));

        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 4f, 0f), rig.Positions[1]);
        Assert.False(rig.Core.IsLive);
    }

    [Fact]
    public void RunningBlendKeepsItsCurveWhenSwitchFlipsOff()
    {
        var rig = new SimulatedRig(V(0f, 0f, 0f));
        rig.Arm(1, V(1f, 0f, 0f));
        rig.Pass(2, 0.05f);
        BlendingSettings.SmoothBlending = false;
        rig.Pass(3, 0.1f);

        Assert.Equal(0.84375f, rig.Positions[0].x, 6);
        Assert.True(rig.Pass(4, 0.1f));
        Assert.Equal(1f, rig.Positions[0].x);
    }

    [Fact]
    public void NlerpTakesShortPathAndUnusableRotationsAreSkipped()
    {
        Quaternion target = YRotation(90f);
        Quaternion oppositeSign = Q(-target.x, -target.y, -target.z, -target.w);
        Quaternion viaShortPath = ExitPoseBlendCore.Nlerp(Identity, oppositeSign, 0.5f);
        AssertRotation(ReferenceNlerp(Identity, target, 0.5f), viaShortPath);
        Assert.True(viaShortPath.w > 0.9f);

        var rig = new SimulatedRig(
            new[] { V(0f, 0f, 0f), V(0f, 0f, 0f), V(0f, 0f, 0f) },
            new[] { Identity, Q(0f, 0f, 0f, 0f), Identity });
        rig.Arm(
            1,
            new[] { V(1f, 0f, 0f), V(1f, 0f, 0f), V(1f, 0f, 0f) },
            new[] { Q(float.NaN, 0f, 0f, 1f), Identity, target });
        Assert.False(Written(rig, 0));
        Assert.False(Written(rig, 1));
        Assert.True(Written(rig, 2));

        rig.DriveRotation(2, Q(float.NaN, 0f, 0f, 1f));
        rig.Pass(2, 0.05f);

        Assert.False(Written(rig, 0));
        Assert.False(Written(rig, 1));
        Assert.False(Written(rig, 2));
        Assert.False(ExitPoseBlendCore.IsUsable(V(float.PositiveInfinity, 0f, 0f), Identity));
        Assert.False(ExitPoseBlendCore.IsUsable(V(0f, 0f, 0f), Q(0f, 0f, 0f, 0f)));
    }

    [Fact]
    public void BoneSelectionUnionsSourcesWithoutDuplicatesNullsOrExcludedTransforms()
    {
        var spine = new Node("spine");
        var arm = new Node("arm");
        var hand = new Node("hand");
        var leg = new Node("leg");
        var sameNameOtherBone = new Node("arm");
        var animatorRoot = new Node("metarig");
        var armsRoot = new Node("ArmsMetarig");
        var camera = new Node("MainCamera");
        var cameraParent = new Node("CameraContainer");
        var sources = new List<Node[]>
        {
            new[] { animatorRoot, spine, arm, null, cameraParent },
            null,
            new[] { arm, hand, armsRoot, spine },
            new[] { leg, sameNameOtherBone, camera }
        };
        var excluded = new List<Node> { animatorRoot, armsRoot, camera, cameraParent };
        var selected = new List<Node> { new Node("stale") };

        ExitPoseBoneSelection.Select(sources, excluded, selected);

        Assert.Equal(new[] { spine, arm, hand, leg, sameNameOtherBone }, selected);
    }

    private static PlayerControllerB Player(bool controlled)
    {
        var player = (PlayerControllerB)RuntimeHelpers.GetUninitializedObject(typeof(PlayerControllerB));
        player.isPlayerControlled = controlled;
        return player;
    }

    // Calls of the rig resolver while body runs; the resolver builds no rig.
    private static int CountRigResolves(Action body)
    {
        Func<PlayerControllerB, ExitPoseBlendRig> original = ExitPoseBlend.RigResolver;
        int calls = 0;
        ExitPoseBlend.RigResolver = _ =>
        {
            calls++;
            return null;
        };
        try
        {
            body();
        }
        finally
        {
            ExitPoseBlend.RigResolver = original;
        }
        return calls;
    }

    // Both gates of a stop that is not a suspend: the qualification at capture and the body
    // checks at arm.
    private static bool Arms(
        InteractionAnimationStopReason reason,
        bool playerControlled = true,
        bool playerDead = false,
        bool animatorActive = true,
        bool culled = false)
    {
        return ExitPoseBlendRules.QualifiesForArming(reason, suspend: false, playerControlled) &&
            ExitPoseBlendRules.CanArm(true, animatorActive, false, culled, playerDead, 12);
    }

    // The pass decision for a player and bones that still exist under the armed controller.
    private static ExitPoseBlendPassDecision DecideLivePass(
        bool playerDead = false,
        bool playerLostControl = false,
        bool roundChanged = false,
        bool animatorActive = true,
        bool culled = false)
    {
        return ExitPoseBlendRules.DecidePass(
            true,
            playerDead,
            playerLostControl,
            roundChanged,
            animatorActive,
            culled,
            boneDestroyed: false,
            controllerChanged: false);
    }

    // Two bones stopped at the origin and restored to (1,0,0)/identity and (0,1,0)/restored.
    private static SimulatedRig ArmTwoBones(Quaternion restored)
    {
        var rig = new SimulatedRig(V(0f, 0f, 0f), V(0f, 0f, 0f));
        rig.Arm(1, TwoRestoredPositions(), new[] { Identity, restored });
        return rig;
    }

    private static Vector3[] TwoRestoredPositions() => new[] { V(1f, 0f, 0f), V(0f, 1f, 0f) };

    // The pre-animation pass of frameCount with no live blend.
    private static void RunIdlePrePass(int frameCount)
    {
        Func<int> engineFrameCount = ExitPoseBlend.ReadFrameCount;
        ExitPoseBlend.ReadFrameCount = () => frameCount;
        try
        {
            ExitPoseBlend.BeforeAnimationPass();
        }
        finally
        {
            ExitPoseBlend.ReadFrameCount = engineFrameCount;
        }
    }

    private static Vector3 V(float x, float y, float z) => new Vector3 { x = x, y = y, z = z };

    private static Quaternion Q(float x, float y, float z, float w) =>
        new Quaternion { x = x, y = y, z = z, w = w };

    private static Quaternion YRotation(float degrees)
    {
        double half = degrees * Math.PI / 360.0;
        return Q(0f, (float)Math.Sin(half), 0f, (float)Math.Cos(half));
    }

    private static Quaternion ReferenceNlerp(Quaternion from, Quaternion to, float t)
    {
        double dot = from.x * to.x + from.y * to.y + from.z * to.z + from.w * to.w;
        double sign = dot < 0 ? -1.0 : 1.0;
        double x = from.x * (1 - t) + to.x * t * sign;
        double y = from.y * (1 - t) + to.y * t * sign;
        double z = from.z * (1 - t) + to.z * t * sign;
        double w = from.w * (1 - t) + to.w * t * sign;
        double length = Math.Sqrt(x * x + y * y + z * z + w * w);
        return Q((float)(x / length), (float)(y / length), (float)(z / length), (float)(w / length));
    }

    // Exact, component by component.
    private static void AssertPosition(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.x, actual.x);
        Assert.Equal(expected.y, actual.y);
        Assert.Equal(expected.z, actual.z);
    }

    private static void AssertRotation(Quaternion expected, Quaternion actual)
    {
        Assert.Equal(expected.x, actual.x, 5);
        Assert.Equal(expected.y, actual.y, 5);
        Assert.Equal(expected.z, actual.z, 5);
        Assert.Equal(expected.w, actual.w, 5);
    }

    // The two-bone restore pose of the pre-pass rows: (1,0,0)/identity and (0,1,0)/restored.
    private static void AssertRestorePose(SimulatedRig rig, Quaternion restored)
    {
        AssertPosition(V(1f, 0f, 0f), rig.Positions[0]);
        AssertPosition(V(0f, 1f, 0f), rig.Positions[1]);
        Assert.True(ExitPoseBlendCore.SameExact(Identity, rig.Rotations[0]));
        Assert.True(ExitPoseBlendCore.SameExact(restored, rig.Rotations[1]));
    }

    private static bool Written(SimulatedRig rig, int bone) =>
        rig.Core.WritePositions[bone] || rig.Core.WriteRotations[bone];

    private static void AssertNothingWritten(SimulatedRig rig)
    {
        Assert.All(rig.Core.WritePositions, write => Assert.False(write));
        Assert.All(rig.Core.WriteRotations, write => Assert.False(write));
    }

    private sealed class Node
    {
        internal Node(string name) => Name = name;

        internal string Name { get; }

        public override string ToString() => Name;
    }

    // Bone transforms and an animator around one core: values only change through the blend's
    // writes (stored through StoreRotation, then read back), the restore, or
    // DrivePosition/DriveRotation (the animator or another writer setting a bone).
    private sealed class SimulatedRig
    {
        internal SimulatedRig(params Vector3[] startPositions)
            : this(startPositions, Fill(startPositions.Length, Identity))
        {
        }

        internal SimulatedRig(Vector3 startPosition, Quaternion startRotation)
            : this(new[] { startPosition }, new[] { startRotation })
        {
        }

        internal SimulatedRig(Vector3[] startPositions, Quaternion[] startRotations)
        {
            Core = new ExitPoseBlendCore(startPositions.Length);
            Positions = (Vector3[])startPositions.Clone();
            Rotations = (Quaternion[])startRotations.Clone();
            Read();
            Core.CaptureStart();
        }

        internal ExitPoseBlendCore Core { get; }

        internal Vector3[] Positions { get; }

        internal Quaternion[] Rotations { get; }

        // What the transform keeps when a rotation is set.
        internal Func<Quaternion, Quaternion> StoreRotation { get; set; } = rotation => rotation;

        internal void Arm(int frame, params Vector3[] restoredPositions)
        {
            Arm(frame, restoredPositions, Fill(restoredPositions.Length, Identity));
        }

        internal void Arm(int frame, Vector3 restoredPosition, Quaternion restoredRotation)
        {
            Arm(frame, new[] { restoredPosition }, new[] { restoredRotation });
        }

        internal void Arm(
            int frame,
            Vector3[] restoredPositions,
            Quaternion[] restoredRotations,
            bool writeStartPose = true)
        {
            Array.Copy(restoredPositions, Positions, Positions.Length);
            Array.Copy(restoredRotations, Rotations, Rotations.Length);
            Read();
            Core.Arm(frame, writeStartPose);
            Apply();
        }

        internal void DrivePosition(int bone, Vector3 value) => Positions[bone] = value;

        internal void DriveRotation(int bone, Quaternion value) => Rotations[bone] = value;

        // The player-loop step right before the animation pass.
        internal void PrePass()
        {
            Read();
            Core.BeforeAnimationPass();
            Apply();
        }

        internal bool Pass(int frame, float deltaTime)
        {
            Read();
            bool ended = Core.Step(frame, deltaTime);
            Apply();
            return ended;
        }

        internal void Cancel(ExitPoseBlendCancelKind kind)
        {
            Read();
            Core.Cancel(kind);
            Apply();
        }

        private void Read()
        {
            Array.Copy(Positions, Core.Positions, Positions.Length);
            Array.Copy(Rotations, Core.Rotations, Rotations.Length);
        }

        private void Apply()
        {
            for (int i = 0; i < Positions.Length; i++)
            {
                if (Core.WritePositions[i])
                    Positions[i] = Core.Positions[i];
                if (Core.WriteRotations[i])
                    Rotations[i] = StoreRotation(Core.Rotations[i]);
            }

            Read();
            Core.AcceptStoredValues();
        }

        private static Quaternion[] Fill(int count, Quaternion value)
        {
            var values = new Quaternion[count];
            for (int i = 0; i < count; i++)
                values[i] = value;
            return values;
        }
    }
}
