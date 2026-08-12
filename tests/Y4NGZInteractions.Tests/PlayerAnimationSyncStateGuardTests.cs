using System.Collections.Generic;
using System.Reflection;
using GameNetcodeStuff;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions.Tests;

public sealed class PlayerAnimationSyncStateGuardTests
{
    [Fact]
    public void EnsureCountCreatesEveryRequiredSlotWhenStateListIsMissing()
    {
        List<int> states = null;

        bool expanded = PlayerAnimationSyncStateGuardPatch.EnsureCount(
            ref states,
            requiredCount: 3);

        Assert.True(expanded);
        Assert.Equal(new[] { 0, 0, 0 }, states);
    }

    [Fact]
    public void EnsureCountPreservesExistingHashesAndAppendsMissingSlots()
    {
        var states = new List<int> { 17, 23 };

        bool expanded = PlayerAnimationSyncStateGuardPatch.EnsureCount(
            ref states,
            requiredCount: 4);

        Assert.True(expanded);
        Assert.Equal(new[] { 17, 23, 0, 0 }, states);
    }

    [Fact]
    public void EnsureCountDoesNotShrinkAListAfterTheOriginalControllerReturns()
    {
        var states = new List<int> { 17, 23, 42 };
        List<int> original = states;

        bool expanded = PlayerAnimationSyncStateGuardPatch.EnsureCount(
            ref states,
            requiredCount: 2);

        Assert.False(expanded);
        Assert.Same(original, states);
        Assert.Equal(new[] { 17, 23, 42 }, states);
    }

    [Fact]
    public void PatchContractMatchesVanillaAnimationSyncMembers()
    {
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        Assert.NotNull(typeof(PlayerControllerB).GetMethod(
            "UpdatePlayerAnimationsToOtherClients",
            flags));
        Assert.Equal(typeof(List<int>), typeof(PlayerControllerB).GetField(
            "currentAnimationStateHash",
            flags)?.FieldType);
        Assert.Equal(typeof(List<int>), typeof(PlayerControllerB).GetField(
            "previousAnimationStateHash",
            flags)?.FieldType);
    }
}
