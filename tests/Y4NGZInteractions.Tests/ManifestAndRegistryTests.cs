using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

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
        Assert.True(manifest.body.preserveGameplayCamera);
        Assert.True(manifest.body.stopOnGameplayCameraDisplacement);
        Assert.False(manifest.body.stabilizeLocalCameraPosition);
        Assert.False(manifest.body.localCameraOwnedExternally);
        Assert.Contains(report.Issues, issue =>
            issue.Code == "manifest_schema_1_migrated" &&
            issue.Severity == InteractionAnimationValidationSeverity.Warning);
    }

    [Fact]
    public void SchemaOneCameraSemanticsRetainIndependentLegacyIntent()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "interactionId": "camera-contract",
          "exemptFromCameraDisplacementGuard": true,
          "body": {
            "enabled": true,
            "stabilizeLocalCameraPosition": true,
            "localCameraOwnedExternally": true
          }
        }
        """;

        InteractionAnimationValidationReport report =
            InteractionAnimationManifestValidator.Parse(
                json, out InteractionAnimationManifest manifest);

        Assert.True(report.IsValid);
        Assert.False(manifest.body.preserveGameplayCamera);
        Assert.False(manifest.body.stopOnGameplayCameraDisplacement);
        Assert.True(manifest.body.stabilizeLocalCameraPosition);
        Assert.True(manifest.body.localCameraOwnedExternally);
    }

    [Fact]
    public void SchemaOneOrdinaryWeaponDoesNotAcquireCameraPositionPinning()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "interactionId": "ordinary-weapon",
          "body": {
            "enabled": true,
            "movementParameter": "MovementState"
          }
        }
        """;

        InteractionAnimationValidationReport report =
            InteractionAnimationManifestValidator.Parse(
                json, out InteractionAnimationManifest manifest);

        Assert.True(report.IsValid);
        Assert.True(manifest.body.preserveGameplayCamera);
        Assert.True(manifest.body.stopOnGameplayCameraDisplacement);
        Assert.False(manifest.body.stabilizeLocalCameraPosition);
        Assert.False(manifest.body.localCameraOwnedExternally);
    }

    [Fact]
    public void SchemaTwoCameraSemanticsRemainIndependent()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "interactionId": "explicit-camera-contract",
          "body": {
            "enabled": true,
            "preserveGameplayCamera": false,
            "stopOnGameplayCameraDisplacement": false,
            "stabilizeLocalCameraPosition": true,
            "localCameraOwnedExternally": false
          }
        }
        """;

        InteractionAnimationValidationReport report =
            InteractionAnimationManifestValidator.Parse(
                json, out InteractionAnimationManifest manifest);

        Assert.True(report.IsValid);
        Assert.False(manifest.body.preserveGameplayCamera);
        Assert.False(manifest.body.stopOnGameplayCameraDisplacement);
        Assert.True(manifest.body.stabilizeLocalCameraPosition);
        Assert.False(manifest.body.localCameraOwnedExternally);
    }

    [Fact]
    public void SchemaOnePropAttachmentResolvesLegacyLeafBoneRecursively()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "interactionId": "legacy-prop",
          "body": {
            "enabled": true,
            "prop": {
              "enabled": true,
              "prefabName": "LegacyProp",
              "attachBone": "hand.R"
            }
          }
        }
        """;

        InteractionAnimationValidationReport report =
            InteractionAnimationManifestValidator.Parse(
                json, out InteractionAnimationManifest manifest);
        TestTransform root = CreateNestedHandRig(out TestTransform hand);

        TestTransform resolved = PropAttachBoneResolver.Resolve(
            root,
            manifest.body.prop,
            (candidate, path) => candidate.FindExactPath(path),
            (candidate, name) => candidate.FindRecursiveName(name));

        Assert.True(report.IsValid);
        Assert.True(manifest.body.prop.useLegacyRecursiveAttachBoneLookup);
        Assert.Null(root.FindExactPath("hand.R"));
        Assert.Same(hand, resolved);
    }

    [Fact]
    public void SchemaTwoPropAttachmentPreservesExactPathLookup()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "interactionId": "exact-prop",
          "body": {
            "enabled": true,
            "prop": {
              "enabled": true,
              "prefabAssetName": "ExactProp",
              "attachBonePath": "metarig/spine.003/shoulder.R/arm.R_upper/arm.R_lower/hand.R"
            }
          }
        }
        """;

        InteractionAnimationValidationReport report =
            InteractionAnimationManifestValidator.Parse(
                json, out InteractionAnimationManifest manifest);
        TestTransform root = CreateNestedHandRig(out TestTransform hand);
        bool usedRecursiveLookup = false;

        TestTransform resolved = PropAttachBoneResolver.Resolve(
            root,
            manifest.body.prop,
            (candidate, path) => candidate.FindExactPath(path),
            (candidate, name) =>
            {
                usedRecursiveLookup = true;
                return candidate.FindRecursiveName(name);
            });

        Assert.True(report.IsValid);
        Assert.False(manifest.body.prop.useLegacyRecursiveAttachBoneLookup);
        Assert.False(usedRecursiveLookup);
        Assert.Same(hand, resolved);
    }

    private static TestTransform CreateNestedHandRig(out TestTransform hand)
    {
        var root = new TestTransform("root");
        hand = root
            .Add("metarig")
            .Add("spine.003")
            .Add("shoulder.R")
            .Add("arm.R_upper")
            .Add("arm.R_lower")
            .Add("hand.R");
        return root;
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

    private sealed class TestTransform
    {
        private readonly List<TestTransform> children = new();

        internal TestTransform(string name)
        {
            Name = name;
        }

        private string Name { get; }

        internal TestTransform Add(string name)
        {
            var child = new TestTransform(name);
            children.Add(child);
            return child;
        }

        internal TestTransform FindExactPath(string path)
        {
            TestTransform current = this;
            foreach (string segment in path.Split('/'))
            {
                current = current.children.FirstOrDefault(
                    child => string.Equals(child.Name, segment, StringComparison.Ordinal));
                if (current == null)
                    return null;
            }
            return current;
        }

        internal TestTransform FindRecursiveName(string name)
        {
            if (string.Equals(Name, name, StringComparison.Ordinal))
                return this;
            foreach (TestTransform child in children)
            {
                TestTransform found = child.FindRecursiveName(name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
