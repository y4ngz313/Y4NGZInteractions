using System;
using System.IO;
using BepInEx.Configuration;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions.Tests;

public sealed class BlendingSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "Y4NGZInteractions.BlendingSettingsTests", Guid.NewGuid().ToString("N"));

    public BlendingSettingsTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        BlendingSettings.Shutdown();
        BlendingSettings.SmoothBlending = true;
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [Fact]
    public void RetiredRestoreStateModeIsRemovedAndSmoothBlendingBindsOnByDefault()
    {
        string path = Path.Combine(directory, "fixture.cfg");
        File.WriteAllText(
            path,
            "[Interaction Animation API Restore Diagnostics]\n" +
            "\n" +
            "## Selects how vanilla animator layer states resume at live-body Stop.\n" +
            "# Setting type: String\n" +
            "# Default value: fresh\n" +
            "# Acceptable values: fresh, crossfade, replay\n" +
            "Restore State Mode = fresh\n" +
            "\n");

        BlendingSettings.Initialize(new ConfigFile(path, true), null);

        Assert.True(BlendingSettings.SmoothBlending);
        string afterFirstLaunch = File.ReadAllText(path);
        Assert.DoesNotContain("Restore State Mode", afterFirstLaunch);
        Assert.DoesNotContain("[Interaction Animation API Restore Diagnostics]", afterFirstLaunch);
        Assert.Contains("[Blending]", afterFirstLaunch);
        Assert.Contains("Smooth Blending = true", afterFirstLaunch);
        Assert.Contains(BlendingSettings.SmoothBlendingDescription, afterFirstLaunch);

        byte[] firstBytes = File.ReadAllBytes(path);
        BlendingSettings.Shutdown();
        BlendingSettings.Initialize(new ConfigFile(path, true), null);

        Assert.True(BlendingSettings.SmoothBlending);
        Assert.Equal(firstBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void PruneLeavesAFileWithoutRetiredEntriesUntouched()
    {
        string path = Path.Combine(directory, "clean.cfg");
        File.WriteAllText(path, "[Other]\n\nKey = value\n\n");
        DateTime written = File.GetLastWriteTimeUtc(path);
        var config = new ConfigFile(path, true);

        Assert.False(BlendingSettings.PruneRetiredEntries(config, null));
        Assert.Equal("[Other]\n\nKey = value\n\n", File.ReadAllText(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }
}
