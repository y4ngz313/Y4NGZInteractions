using System;
using System.Linq;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;

namespace Y4NGZInteractions.Tests;

public sealed class ManifestLayerTests
{
    [Fact]
    public void SchemaTwoLayersParseWithDefaultsForOmittedKeys()
    {
        InteractionAnimationValidationReport report = ValidateBody(
            "[{'name': 'Hands'}, " +
            "{'name': 'Legs', 'viewers': ['remote'], 'weight': 0.5, 'yieldTo': ['sprint']}]",
            out InteractionAnimationManifest manifest);

        AssertValid(report);
        Assert.Equal(2, manifest.body.layers.Length);
        InteractionAnimationManifest.LayerManifest hands = manifest.body.layers[0];
        Assert.Equal("Hands", hands.name);
        Assert.Empty(hands.viewers);
        Assert.Equal(-1f, hands.weight);
        Assert.Empty(hands.yieldTo);
        InteractionAnimationManifest.LayerManifest legs = manifest.body.layers[1];
        Assert.Equal("Legs", legs.name);
        Assert.Equal(new[] { "remote" }, legs.viewers);
        Assert.Equal(0.5f, legs.weight);
        Assert.Equal(new[] { "sprint" }, legs.yieldTo);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{'name': 'Hands', 'weight': -1}]")]
    [InlineData("[{'name': 'Hands', 'weight': 0}]")]
    [InlineData("[{'name': 'Hands', 'weight': 1}]")]
    [InlineData("[{'name': 'Hands', 'viewers': ['localFirstPerson', 'localThirdPerson', 'remote'], " +
                "'yieldTo': ['sprint', 'handsOnWall']}]")]
    [InlineData("[{'name': 'fullbody'}]")]
    public void SchemaTwoLayerBoundariesAreAccepted(string layers)
    {
        AssertValid(ValidateBody(layers, out _));
    }

    [Theory]
    [InlineData("[{'name': 'Hands', 'mystery': true}]",
        "manifest_unknown_field", "$.body.layers[0].mystery")]
    [InlineData("[{'name': 'Hands'}, {'name': 'Legs', 'Weight': 1}]",
        "manifest_unknown_field", "$.body.layers[1].Weight")]
    [InlineData("[{'name': ''}]",
        "manifest_body_layer_name_missing", "$.body.layers[0].name")]
    [InlineData("[{'name': '   '}]",
        "manifest_body_layer_name_missing", "$.body.layers[0].name")]
    [InlineData("[{'viewers': ['remote']}]",
        "manifest_body_layer_name_missing", "$.body.layers[0].name")]
    [InlineData("[null]",
        "manifest_body_layer_name_missing", "$.body.layers[0]")]
    [InlineData("[{'name': 'FullBody'}]",
        "manifest_body_layer_duplicate", "$.body.layers[0].name")]
    [InlineData("[{'name': 'Arms'}]",
        "manifest_body_layer_duplicate", "$.body.layers[0].name")]
    [InlineData("[{'name': 'Hands'}, {'name': 'Hands'}]",
        "manifest_body_layer_duplicate", "$.body.layers[1].name")]
    [InlineData("[{'name': 'Hands', 'viewers': []}]",
        "manifest_body_layer_viewers_empty", "$.body.layers[0].viewers")]
    [InlineData("[{'name': 'Hands', 'viewers': null}]",
        "manifest_body_layer_viewers_empty", "$.body.layers[0].viewers")]
    [InlineData("[{'name': 'Hands', 'viewers': ['remote', 'firstPerson']}]",
        "manifest_body_layer_viewer_invalid", "$.body.layers[0].viewers[1]")]
    [InlineData("[{'name': 'Hands', 'viewers': ['Remote']}]",
        "manifest_body_layer_viewer_invalid", "$.body.layers[0].viewers[0]")]
    [InlineData("[{'name': 'Hands', 'yieldTo': ['sprint', 'crouch']}]",
        "manifest_body_layer_yield_invalid", "$.body.layers[0].yieldTo[1]")]
    [InlineData("[{'name': 'Hands', 'yieldTo': ['HandsOnWall']}]",
        "manifest_body_layer_yield_invalid", "$.body.layers[0].yieldTo[0]")]
    [InlineData("[{'name': 'Hands', 'weight': -0.5}]",
        "manifest_body_layer_weight_invalid", "$.body.layers[0].weight")]
    [InlineData("[{'name': 'Hands', 'weight': 1.2}]",
        "manifest_body_layer_weight_invalid", "$.body.layers[0].weight")]
    [InlineData("[{'name': 'Hands', 'weight': NaN}]",
        "manifest_body_layer_weight_invalid", "$.body.layers[0].weight")]
    [InlineData("[{'name': 'Hands'}, {'name': 'Legs', 'weight': 2}]",
        "manifest_body_layer_weight_invalid", "$.body.layers[1].weight")]
    public void SchemaTwoLayerErrorsReportTheirCodeAtTheirPath(
        string layers,
        string code,
        string path)
    {
        AssertError(ValidateBody(layers, out _), code, path);
    }

    [Theory]
    [InlineData("Base Layer")]
    [InlineData("EmotesNoArms")]
    [InlineData("HoldingItemsRightHand")]
    [InlineData("UpperBodyEmotes")]
    [InlineData("HoldingItemsBothHands")]
    [InlineData("SpecialAnimations")]
    public void SchemaTwoLayersRejectVanillaLayerNames(string name)
    {
        AssertError(
            ValidateBody("[{'name': '" + name + "'}]", out _),
            "manifest_body_layer_reserved",
            "$.body.layers[0].name");
    }

    [Fact]
    public void FullBodyLayerMayStillNameAVanillaLayer()
    {
        string json = BodyJson("[{'name': 'Hands'}]")
            .Replace("\"FullBody\"", "\"EmotesNoArms\"");

        AssertValid(InteractionAnimationManifestValidator.Validate(
            json, "layers", InteractionAnimationPresentationKind.BodyWorld, out _));
    }

    [Fact]
    public void SchemaTwoLayersAcceptEightEntries()
    {
        AssertValid(ValidateBody(NamedLayers(8), out _));
    }

    [Fact]
    public void SchemaTwoLayersRejectNineEntries()
    {
        AssertError(
            ValidateBody(NamedLayers(9), out _),
            "manifest_body_layer_count_invalid",
            "$.body.layers");
    }

    [Theory]
    [InlineData("\"schemaVersion\": 1,")]
    [InlineData("")]
    public void SchemaOneWithBodyLayersIsRejected(string schemaVersionField)
    {
        string json = """
        {
          {{SCHEMA}}
          "interactionId": "legacy-layers",
          "body": {
            "enabled": true,
            "fullBodyLayer": "FullBody",
            "layers": [ { "name": "Hands" } ]
          }
        }
        """.Replace("{{SCHEMA}}", schemaVersionField);

        InteractionAnimationValidationReport report =
            InteractionAnimationManifestValidator.Parse(
                json, out InteractionAnimationManifest manifest);

        Assert.Null(manifest);
        Assert.Equal(
            "manifest_field_requires_schema_2",
            InteractionAnimationManifestValidator.GetFirstErrorCode(report));
        AssertError(report, "manifest_field_requires_schema_2", "$.body.layers");
    }

    [Fact]
    public void SchemaOneResolvesToFullBodyThenFirstPersonArms()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "interactionId": "legacy-two-layers",
          "body": {
            "enabled": true,
            "fullBodyLayer": "FullBody",
            "firstPersonArmsLayer": "Arms",
            "fullBodyLayerWeight": 0.4
          }
        }
        """;

        InteractionAnimationValidationReport report =
            InteractionAnimationManifestValidator.Parse(
                json, out InteractionAnimationManifest manifest);

        AssertValid(report);
        Assert.Collection(
            InteractionAnimationLayerSpecs.Resolve(manifest.body),
            Spec("FullBody", InteractionAnimationViewerMask.All, 0.4f,
                InteractionAnimationLayerYield.None),
            Spec("Arms", InteractionAnimationViewerMask.All, -1f,
                InteractionAnimationLayerYield.None));
    }

    [Fact]
    public void SchemaTwoWithoutLayersResolvesToFullBodyThenFirstPersonArms()
    {
        InteractionAnimationValidationReport report =
            ValidateBody(null, out InteractionAnimationManifest manifest);

        AssertValid(report);
        Assert.Empty(manifest.body.layers);
        Assert.Collection(
            InteractionAnimationLayerSpecs.Resolve(manifest.body),
            Spec("FullBody", InteractionAnimationViewerMask.All, -1f,
                InteractionAnimationLayerYield.None),
            Spec("Arms", InteractionAnimationViewerMask.All, -1f,
                InteractionAnimationLayerYield.None));
    }

    [Fact]
    public void LayersResolveAfterTheTwoNamedLayersInOrderWithParsedMasks()
    {
        InteractionAnimationValidationReport report = ValidateBody(
            "[{'name': 'Hands', 'viewers': ['localFirstPerson', 'remote'], " +
            "'yieldTo': ['sprint', 'handsOnWall']}, " +
            "{'name': 'Legs', 'weight': 0.25}, " +
            "{'name': 'Head', 'viewers': ['localThirdPerson'], 'yieldTo': ['handsOnWall']}]",
            out InteractionAnimationManifest manifest);

        AssertValid(report);
        Assert.Collection(
            InteractionAnimationLayerSpecs.Resolve(manifest.body),
            Spec("FullBody", InteractionAnimationViewerMask.All, -1f,
                InteractionAnimationLayerYield.None),
            Spec("Arms", InteractionAnimationViewerMask.All, -1f,
                InteractionAnimationLayerYield.None),
            Spec("Hands",
                InteractionAnimationViewerMask.LocalFirstPerson |
                InteractionAnimationViewerMask.Remote,
                -1f,
                InteractionAnimationLayerYield.Sprint |
                InteractionAnimationLayerYield.HandsOnWall),
            Spec("Legs", InteractionAnimationViewerMask.All, 0.25f,
                InteractionAnimationLayerYield.None),
            Spec("Head", InteractionAnimationViewerMask.LocalThirdPerson, -1f,
                InteractionAnimationLayerYield.HandsOnWall));
    }

    [Theory]
    [InlineData("", "Arms", "Arms")]
    [InlineData("FullBody", "", "FullBody")]
    [InlineData(" ", " ", "")]
    public void BlankFullBodyOrFirstPersonArmsLayerIsSkipped(
        string fullBodyLayer,
        string firstPersonArmsLayer,
        string expectedNames)
    {
        var body = new InteractionAnimationManifest.BodyManifest
        {
            fullBodyLayer = fullBodyLayer,
            firstPersonArmsLayer = firstPersonArmsLayer
        };

        Assert.Equal(
            expectedNames,
            string.Join(",", InteractionAnimationLayerSpecs.Resolve(body).Select(spec => spec.Name)));
    }

    [Theory]
    [InlineData(-1f, -1f)]
    [InlineData(-0.25f, -1f)]
    [InlineData(0f, 0f)]
    [InlineData(0.4f, 0.4f)]
    public void FullBodyLayerWeightResolvesToFollowRampOrPinnedWeight(
        float fullBodyLayerWeight,
        float expectedWeight)
    {
        var body = new InteractionAnimationManifest.BodyManifest
        {
            fullBodyLayer = "FullBody",
            fullBodyLayerWeight = fullBodyLayerWeight
        };

        InteractionAnimationLayerSpec spec =
            Assert.Single(InteractionAnimationLayerSpecs.Resolve(body));
        Assert.Equal(expectedWeight, spec.Weight);
    }

    private static Action<InteractionAnimationLayerSpec> Spec(
        string name,
        InteractionAnimationViewerMask viewers,
        float weight,
        InteractionAnimationLayerYield yields)
    {
        return spec =>
        {
            Assert.Equal(name, spec.Name);
            Assert.Equal(viewers, spec.Viewers);
            Assert.Equal(weight, spec.Weight);
            Assert.Equal(yields, spec.Yield);
        };
    }

    private static string NamedLayers(int count)
    {
        return "[" + string.Join(
            ", ",
            Enumerable.Range(0, count).Select(index => "{'name': 'Extra" + index + "'}")) + "]";
    }

    private static InteractionAnimationValidationReport ValidateBody(
        string layers,
        out InteractionAnimationManifest manifest)
    {
        return InteractionAnimationManifestValidator.Validate(
            BodyJson(layers),
            "layers",
            InteractionAnimationPresentationKind.BodyWorld,
            out manifest);
    }

    // Single quotes in a layers fragment stand for JSON double quotes; null omits body.layers.
    private static string BodyJson(string layers)
    {
        string layersField = layers == null
            ? string.Empty
            : ",\n    \"layers\": " + layers.Replace('\'', '"');
        return $$"""
        {
          "schemaVersion": 2,
          "interactionId": "layers",
          "durationSeconds": 0,
          "body": {
            "enabled": true,
            "bundleFileName": "sample.bundle",
            "controllerAssetName": "SampleController",
            "fullBodyLayer": "FullBody",
            "firstPersonArmsLayer": "Arms"{{layersField}}
          }
        }
        """;
    }

    private static void AssertValid(InteractionAnimationValidationReport report)
    {
        Assert.True(report.IsValid, string.Join(Environment.NewLine,
            report.Issues.Select(issue => issue.Code + " " + issue.JsonPath + " " + issue.Message)));
    }

    private static void AssertError(
        InteractionAnimationValidationReport report,
        string code,
        string path)
    {
        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, issue =>
            issue.Code == code &&
            issue.JsonPath == path &&
            issue.Severity == InteractionAnimationValidationSeverity.Error);
    }
}
