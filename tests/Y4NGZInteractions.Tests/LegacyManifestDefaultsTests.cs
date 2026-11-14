using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;

namespace Y4NGZInteractions.Tests;

public sealed class LegacyManifestDefaultsTests
{
    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // localViewmodel, body and body.prop are present but carry none of the keys the rows read.
    private const string SchemaOneEmptyObjectsJson = """
    {
      "schemaVersion": 1,
      "interactionId": "legacy-defaults",
      "localViewmodel": {},
      "body": { "prop": {} }
    }
    """;

    [Fact]
    public void SchemaOneWithoutFullBodyLayerWeightMigratesToFollowRamp()
    {
        Assert.Equal(-1f, ParseSchemaOne().body.fullBodyLayerWeight);
    }

    [Fact]
    public void SchemaOneWithoutSuppressRigBuildersDoesNotRebuildRigBuilders()
    {
        Assert.False(ParseSchemaOne().body.rebuildRigBuilders);
    }

    [Fact]
    public void SchemaOneWithoutStartLayerWeightStartsAtFullWeight()
    {
        Assert.Equal(1f, ParseSchemaOne().body.startLayerWeight);
    }

    [Fact]
    public void SchemaOneWithoutPropLocalScaleKeepsUnitScale()
    {
        Assert.Equal(1f, ParseSchemaOne().body.prop.localScale);
    }

    [Fact]
    public void SchemaOneWithoutCameraAnchorKeepsDefaultAnchor()
    {
        Assert.Equal(
            "Y4NGZ_ViewmodelCameraAnchor",
            ParseSchemaOne().localViewmodel.cameraAnchorPath);
    }

    [Fact]
    public void SchemaOneWithoutCameraLocalPositionKeepsDefaultPosition()
    {
        InteractionAnimationVector3 position = ParseSchemaOne().localViewmodel.cameraLocalPosition;

        Assert.Equal(0f, position.x);
        Assert.Equal(-0.42f, position.y);
        Assert.Equal(0.95f, position.z);
    }

    [Fact]
    public void SchemaOneWithoutViewmodelLocalScaleKeepsDefaultScale()
    {
        InteractionAnimationVector3 scale = ParseSchemaOne().localViewmodel.localScale;

        Assert.Equal(0.55f, scale.x);
        Assert.Equal(0.55f, scale.y);
        Assert.Equal(0.55f, scale.z);
    }

    [Fact]
    public void LegacyManifestWithoutSchemaVersionReadsSchemaOne()
    {
        Assert.Equal(1, new JObject().ToObject<LegacyInteractionAnimationManifest>().schemaVersion);
    }

    [Theory]
    [MemberData(nameof(DefaultedLegacyFields))]
    public void DefaultedLegacyFieldSurvivesAMissingKey(string typeName, string fieldName)
    {
        Type type = LegacyType(typeName);
        FieldInfo field = type.GetField(fieldName, InstanceFields);
        object declared = field.GetValue(Activator.CreateInstance(type));
        object read = field.GetValue(new JObject().ToObject(type));

        if (field.FieldType.IsArray)
        {
            Assert.NotNull(read);
            Assert.Equal(((Array)declared).Length, ((Array)read).Length);
        }
        else if (field.FieldType.IsClass && field.FieldType != typeof(string))
        {
            Assert.IsType(field.FieldType, read);
        }
        else
        {
            Assert.Equal(declared, read);
        }
    }

    public static IEnumerable<object[]> DefaultedLegacyFields()
    {
        foreach (Type type in LegacyTypes())
        {
            object declared = Activator.CreateInstance(type);
            foreach (FieldInfo field in type.GetFields(InstanceFields))
            {
                object value = field.GetValue(declared);
                bool clrZero = value == null ||
                    (field.FieldType.IsValueType &&
                     value.Equals(Activator.CreateInstance(field.FieldType)));
                if (!clrZero)
                    yield return new object[] { type.Name, field.Name };
            }
        }
    }

    private static InteractionAnimationManifest ParseSchemaOne()
    {
        InteractionAnimationValidationReport report =
            InteractionAnimationManifestValidator.Parse(
                SchemaOneEmptyObjectsJson, out InteractionAnimationManifest manifest);

        Assert.True(report.IsValid);
        return manifest;
    }

    private static IEnumerable<Type> LegacyTypes()
    {
        yield return typeof(LegacyInteractionAnimationManifest);
        foreach (Type nested in typeof(LegacyInteractionAnimationManifest).GetNestedTypes(
                     BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return nested;
        }
    }

    private static Type LegacyType(string name)
    {
        foreach (Type type in LegacyTypes())
        {
            if (type.Name == name)
                return type;
        }
        throw new ArgumentException("Unknown legacy manifest type: " + name, nameof(name));
    }
}
