using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions.Tests;

public sealed class InteractionTelemetryLoggerTests : IDisposable
{
    private const string Key = "Enable Interaction Telemetry Logger";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "Y4NGZInteractions.TelemetryLoggerTests", Guid.NewGuid().ToString("N"));
    private readonly string assetRoot;
    private readonly ManualLogSource source = new ManualLogSource("TelemetryLoggerTests");
    private readonly List<LogEventArgs> events = new List<LogEventArgs>();
    private InteractionAnimationCoordinator coordinator;

    public InteractionTelemetryLoggerTests()
    {
        assetRoot = Path.Combine(directory, "assets");
        Directory.CreateDirectory(assetRoot);
        source.LogEvent += (_, args) => events.Add(args);
    }

    public void Dispose()
    {
        coordinator?.Shutdown();
        InteractionAnimationApiRestoreDiagnostics.Shutdown();
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [Fact]
    public void DefaultConfigWritesTheKeyOffAndPackRegistrationLogsNothing()
    {
        string path = Path.Combine(directory, "default.cfg");
        InteractionAnimationApiRestoreDiagnostics.Initialize(new ConfigFile(path, true), null);

        Assert.False(InteractionAnimationApiRestoreDiagnostics.InteractionTelemetryLoggerEnabled);
        Assert.Contains(Key + " = false", File.ReadAllText(path));

        RegisterPack();

        Assert.Empty(events);
    }

    [Fact]
    public void EnabledConfigLogsOnePackRegisteredInfoLine()
    {
        string path = Path.Combine(directory, "enabled.cfg");
        File.WriteAllText(
            path,
            "[" + InteractionAnimationApiRestoreDiagnostics.ConfigSection + "]\n" +
            "\n" +
            Key + " = true\n" +
            "\n");
        InteractionAnimationApiRestoreDiagnostics.Initialize(new ConfigFile(path, true), null);

        Assert.True(InteractionAnimationApiRestoreDiagnostics.InteractionTelemetryLoggerEnabled);

        RegisterPack();

        LogEventArgs logged = Assert.Single(events);
        Assert.Equal(LogLevel.Info, logged.Level);
        Assert.StartsWith(
            "[LCInteractionAnimationAPI] pack.registered:", logged.Data.ToString());
    }

    [Fact]
    public void ShutdownTurnsTheSwitchOffAgain()
    {
        string path = Path.Combine(directory, "shutdown.cfg");
        File.WriteAllText(
            path,
            "[" + InteractionAnimationApiRestoreDiagnostics.ConfigSection + "]\n" +
            "\n" +
            Key + " = true\n" +
            "\n");
        InteractionAnimationApiRestoreDiagnostics.Initialize(new ConfigFile(path, true), null);
        Assert.True(InteractionAnimationApiRestoreDiagnostics.InteractionTelemetryLoggerEnabled);

        InteractionAnimationApiRestoreDiagnostics.Shutdown();

        Assert.False(InteractionAnimationApiRestoreDiagnostics.InteractionTelemetryLoggerEnabled);
    }

    private void RegisterPack()
    {
        coordinator = new InteractionAnimationCoordinator(source);
        var pack = new InteractionAnimationPackDefinition
        {
            PackId = "sample.pack",
            Version = "1.0.0",
            AssetRootPath = assetRoot,
            Interactions = new[]
            {
                new InteractionAnimationDefinition
                {
                    InteractionId = "first",
                    PresentationKind =
                        InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
                    ManifestJson = """
                    {
                      "schemaVersion": 2,
                      "interactionId": "first",
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
}
