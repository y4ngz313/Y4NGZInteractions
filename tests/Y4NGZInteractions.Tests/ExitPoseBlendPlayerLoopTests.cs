using System;
using UnityEngine.LowLevel;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;
using PreLateUpdate = UnityEngine.PlayerLoop.PreLateUpdate;
using PostLateUpdate = UnityEngine.PlayerLoop.PostLateUpdate;
using Update = UnityEngine.PlayerLoop.Update;

namespace Y4NGZInteractions.Tests;

public sealed class ExitPoseBlendPlayerLoopTests
{
    private static readonly Type PrePass = typeof(ExitPoseBlendPlayerLoop.ExitPoseBlendBeforeAnimationPass);

    [Fact]
    public void InstallPutsOnePrePassFirstInPreLateUpdateAndTeardownRemovesIt()
    {
        PlayerLoopSystem original = Loop(
            Phase(typeof(Update), typeof(Update.ScriptRunBehaviourUpdate)),
            Phase(
                typeof(PreLateUpdate),
                typeof(PreLateUpdate.AIUpdatePostScript),
                typeof(PreLateUpdate.DirectorUpdateAnimationBegin),
                typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate)),
            Phase(typeof(PostLateUpdate), typeof(PostLateUpdate.UpdateAllRenderers)));
        PlayerLoopSystem current = original;
        int writes = 0;
        Func<PlayerLoopSystem> engineRead = ExitPoseBlendPlayerLoop.ReadLoop;
        Action<PlayerLoopSystem> engineWrite = ExitPoseBlendPlayerLoop.WriteLoop;
        Func<int> engineFrameCount = ExitPoseBlend.ReadFrameCount;
        ExitPoseBlendPlayerLoop.ReadLoop = () => current;
        ExitPoseBlendPlayerLoop.WriteLoop = loop =>
        {
            current = loop;
            writes++;
        };
        try
        {
            Assert.True(ExitPoseBlendPlayerLoop.Install());
            Assert.True(ExitPoseBlendPlayerLoop.IsInstalled);
            Assert.Equal(1, writes);
            Assert.Equal(
                new[]
                {
                    PrePass,
                    typeof(PreLateUpdate.AIUpdatePostScript),
                    typeof(PreLateUpdate.DirectorUpdateAnimationBegin),
                    typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate)
                },
                Types(current.subSystemList[1]));
            Assert.Equal(Types(original), Types(current));
            Assert.Equal(Types(original.subSystemList[0]), Types(current.subSystemList[0]));
            Assert.Equal(Types(original.subSystemList[2]), Types(current.subSystemList[2]));
            Assert.Equal(3, original.subSystemList[1].subSystemList.Length);

            // With no live blend the installed step only records the frame, so an arm later in
            // that frame counts as after its animation pass and an arm in the next frame does not.
            ExitPoseBlend.ReadFrameCount = () => 42;
            current.subSystemList[1].subSystemList[0].updateDelegate();
            Assert.True(ExitPoseBlend.AnimationPassRan(42));
            Assert.False(ExitPoseBlend.AnimationPassRan(43));
            Assert.Equal(0, ExitPoseBlend.LiveCount);
            Assert.False(ExitPoseBlend.RunnerEnabled);

            Assert.True(ExitPoseBlendPlayerLoop.Install());
            Assert.Equal(1, writes);

            ExitPoseBlendPlayerLoop.Uninstall();
            Assert.False(ExitPoseBlendPlayerLoop.IsInstalled);
            Assert.Equal(2, writes);
            Assert.Equal(Types(original.subSystemList[1]), Types(current.subSystemList[1]));
            Assert.Equal(Types(original.subSystemList[0]), Types(current.subSystemList[0]));
            Assert.Equal(Types(original.subSystemList[2]), Types(current.subSystemList[2]));

            ExitPoseBlendPlayerLoop.Uninstall();
            Assert.Equal(2, writes);

            current = Loop(Phase(typeof(Update), typeof(Update.ScriptRunBehaviourUpdate)));
            Assert.False(ExitPoseBlendPlayerLoop.Install());
            Assert.False(ExitPoseBlendPlayerLoop.IsInstalled);
            Assert.Equal(2, writes);
        }
        finally
        {
            ExitPoseBlendPlayerLoop.Uninstall();
            ExitPoseBlendPlayerLoop.ReadLoop = engineRead;
            ExitPoseBlendPlayerLoop.WriteLoop = engineWrite;
            ExitPoseBlend.ReadFrameCount = engineFrameCount;
            ExitPoseBlend.Shutdown();
        }
    }

    private static PlayerLoopSystem Loop(params PlayerLoopSystem[] phases) =>
        new PlayerLoopSystem { subSystemList = phases };

    private static PlayerLoopSystem Phase(Type phase, params Type[] systems)
    {
        var list = new PlayerLoopSystem[systems.Length];
        for (int i = 0; i < systems.Length; i++)
            list[i] = new PlayerLoopSystem { type = systems[i] };
        return new PlayerLoopSystem { type = phase, subSystemList = list };
    }

    private static Type[] Types(PlayerLoopSystem system) =>
        Array.ConvertAll(system.subSystemList, entry => entry.type);
}
