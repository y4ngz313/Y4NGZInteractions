using System;
using System.IO;
using System.Linq;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;

namespace Y4NGZInteractions.Tests;

public sealed class ManifestAndRegistryTests : IDisposable
{
    private readonly string assetRoot = Path.Combine(
        Path.GetTempPath(), "Y4NGZInteractions.Tests", Guid.NewGuid().ToString("N"));

    public ManifestAndRegistryTests()
    {
        Directory.CreateDirectory(assetRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(assetRoot))
            Directory.Delete(assetRoot, true);
    }

    [Fact]
    public void SchemaOneMigratesToSchemaTwoWithWarning()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "interactionId": "wave",
          "durationSeconds": 1.5,
          "localViewmodel": {
            "bundleFileName": "sample.bundle",
            "prefab": "SampleRig",
            "controller": "SampleController",
            "cameraAnchor": "Rig/CameraAnchor",
            "cameraLocalPosition": { "x": 0, "y": 0, "z": 0 },
            "cameraLocalEuler": { "x": 0, "y": 0, "z": 0 },
            "localScale": { "x": 1, "y": 1, "z": 1 }
          }
        }
        """;

        InteractionAnimationValidationReport report =
            InteractionAnimationManifestValidator.Validate(
                json, "wave", InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
                out InteractionAnimationManifest manifest);

        Assert.True(report.IsValid);
        Assert.Equal(2, manifest.schemaVersion);
        Assert.Equal("SampleRig", manifest.localViewmodel.prefabAssetName);
        Assert.Contains(report.Issues, issue =>
            issue.Code == "manifest_schema_1_migrated" &&
            issue.Severity == InteractionAnimationValidationSeverity.Warning);
    }

    [Fact]
    public void SchemaVersionWrongTypeHasPathSpecificError()
    {
        const string json = """
        {
          "schemaVersion": "2",
          "interactionId": "wave"
        }
        """;
        InteractionAnimationValidationReport report =
            LCInteractionAnimationAPI.ValidateInteractionManifest(
                json, "wave", InteractionAnimationPresentationKind.DedicatedLocalViewmodel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, issue =>
            issue.Code == "manifest_schema_version_invalid" &&
            issue.JsonPath == "$.schemaVersion");
    }

    [Theory]
    [InlineData("\"interactionId\"", "\"InteractionId\"", "$.InteractionId")]
    [InlineData("\"prefabAssetName\"", "\"PrefabAssetName\"", "$.localViewmodel.PrefabAssetName")]
    [InlineData("\"x\": 0", "\"X\": 0", "$.localViewmodel.cameraLocalPosition.X")]
    public void SchemaTwoFieldNamesAreCaseSensitive(
        string oldName,
        string newName,
        string expectedPath)
    {
        string json = ValidViewmodelJsonWithPosition("wave")
            .Replace(oldName, newName);

        InteractionAnimationValidationReport report =
            LCInteractionAnimationAPI.ValidateInteractionManifest(
                json, "wave", InteractionAnimationPresentationKind.DedicatedLocalViewmodel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, issue =>
            issue.Code == "manifest_unknown_field" &&
            issue.JsonPath == expectedPath);
    }

    [Fact]
    public void SchemaTwoRejectsDuplicatePropertiesAtTheirJsonPath()
    {
        string json = ValidViewmodelJson("wave")
            .Replace(
                "\"durationSeconds\": 1",
                "\"durationSeconds\": 1, \"durationSeconds\": 2");

        InteractionAnimationValidationReport report =
            LCInteractionAnimationAPI.ValidateInteractionManifest(
                json, "wave", InteractionAnimationPresentationKind.DedicatedLocalViewmodel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, issue =>
            issue.Code == "manifest_json_invalid" &&
            issue.JsonPath.Contains("durationSeconds", StringComparison.Ordinal));
    }
    [Fact]
    public void SchemaTwoRejectsUnknownFieldsAtTheirJsonPath()
    {
        string json = ValidViewmodelJson("wave")
            .Replace("\"durationSeconds\": 1", "\"durationSeconds\": 1, \"mystery\": true");

        InteractionAnimationValidationReport report =
            LCInteractionAnimationAPI.ValidateInteractionManifest(
                json, "wave", InteractionAnimationPresentationKind.DedicatedLocalViewmodel);

        Assert.False(report.IsValid);
        InteractionAnimationValidationIssue issue = Assert.Single(
            report.Issues, value => value.Code == "manifest_unknown_field");
        Assert.Contains("mystery", issue.JsonPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SchemaTwoUsesNeutralTransformDefaults()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "interactionId": "wave",
          "durationSeconds": 1,
          "localViewmodel": {
            "bundleFileName": "sample.bundle",
            "prefabAssetName": "SampleRig",
            "controllerAssetName": "SampleController",
            "cameraAnchorPath": "Rig/CameraAnchor"
          }
        }
        """;

        InteractionAnimationValidationReport report =
            InteractionAnimationManifestValidator.Validate(
                json, "wave", InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
                out InteractionAnimationManifest manifest);

        Assert.True(report.IsValid, string.Join(Environment.NewLine,
            report.Issues.Select(issue => issue.Code + " " + issue.JsonPath + " " + issue.Message)));
        Assert.Equal(0f, manifest.localViewmodel.cameraLocalPosition.x);
        Assert.Equal(0f, manifest.localViewmodel.cameraLocalPosition.y);
        Assert.Equal(0f, manifest.localViewmodel.cameraLocalPosition.z);
        Assert.Equal(0f, manifest.localViewmodel.cameraLocalEuler.x);
        Assert.Equal(0f, manifest.localViewmodel.cameraLocalEuler.y);
        Assert.Equal(0f, manifest.localViewmodel.cameraLocalEuler.z);
        Assert.Equal(1f, manifest.localViewmodel.localScale.x);
        Assert.Equal(1f, manifest.localViewmodel.localScale.y);
        Assert.Equal(1f, manifest.localViewmodel.localScale.z);
    }

    [Theory]
    [InlineData("../escape.bundle")]
    [InlineData("nested\\escape.bundle")]
    [InlineData("/rooted.bundle")]
    public void ManifestRejectsUnconfinedBundlePaths(string bundlePath)
    {
        string json = ValidViewmodelJson("wave")
            .Replace("sample.bundle", bundlePath.Replace("\\", "\\\\"));

        InteractionAnimationValidationReport report =
            LCInteractionAnimationAPI.ValidateInteractionManifest(
                json, "wave", InteractionAnimationPresentationKind.DedicatedLocalViewmodel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues,
            issue => issue.Code == "manifest_viewmodel_bundle_file_invalid" &&
                     issue.JsonPath == "$.localViewmodel.bundleFileName");
    }

    [Fact]
    public void ResolverConfinesBundleToRegisteredRoot()
    {
        Assert.False(InteractionAnimationAssetPathResolver.TryResolveBundlePath(
            "../escape.bundle", assetRoot, out _, out string reason));
        Assert.StartsWith(InteractionAnimationAssetPathResolver.PathEscapesRootReason, reason);
    }

    [Fact]
    public void PackValidationSnapshotsCallerOwnedDefinitions()
    {
        var definition = new InteractionAnimationDefinition
        {
            InteractionId = "wave",
            PresentationKind = InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
            ManifestJson = ValidViewmodelJson("wave")
        };
        var pack = new InteractionAnimationPackDefinition
        {
            PackId = "sample.pack",
            Version = "1.0.0",
            AssetRootPath = assetRoot,
            Interactions = new[] { definition }
        };

        InteractionAnimationValidationReport report =
            InteractionAnimationPackValidator.Validate(pack, out RegisteredPackSnapshot snapshot);
        definition.InteractionId = "mutated";
        definition.ManifestJson = "{}";
        pack.PackId = "mutated.pack";
        pack.Interactions[0] = null;

        Assert.True(report.IsValid);
        Assert.Equal("sample.pack", snapshot.PackId);
        Assert.True(snapshot.TryGetInteraction("wave", out RegisteredInteractionSnapshot saved));
        Assert.Equal("wave", saved.InteractionId);
        Assert.Equal("wave", saved.Manifest.interactionId);
    }

    [Fact]
    public void RegistrationReportsFirstPathSpecificErrorCode()
    {
        var pack = new InteractionAnimationPackDefinition
        {
            Version = "1.0.0",
            AssetRootPath = assetRoot,
            Interactions = Array.Empty<InteractionAnimationDefinition>()
        };

        InteractionAnimationValidationReport report =
            InteractionAnimationPackValidator.Validate(pack, out _);

        Assert.False(report.IsValid);
        Assert.Equal("pack_id_empty",
            InteractionAnimationManifestValidator.GetFirstErrorCode(report));
        Assert.Equal("$.PackId", report.Issues[0].JsonPath);
    }

    private static string ValidViewmodelJsonWithPosition(string interactionId)
    {
        return ValidViewmodelJson(interactionId).Replace(
            "\"cameraAnchorPath\": \"Rig/CameraAnchor\"",
            "\"cameraAnchorPath\": \"Rig/CameraAnchor\", " +
            "\"cameraLocalPosition\": { \"x\": 0, \"y\": 0, \"z\": 0 }");
    }
    private static string ValidViewmodelJson(string interactionId)
    {
        return $$"""
        {
          "schemaVersion": 2,
          "interactionId": "{{interactionId}}",
          "durationSeconds": 1,
          "localViewmodel": {
            "bundleFileName": "sample.bundle",
            "prefabAssetName": "SampleRig",
            "controllerAssetName": "SampleController",
            "cameraAnchorPath": "Rig/CameraAnchor"
          }
        }
        """;
    }
}
