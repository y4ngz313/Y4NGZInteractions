using System;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class InteractionLayerWeightPolicyTests : IDisposable
{
    public void Dispose()
    {
        BlendingSettings.SmoothBlending = true;
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0.25f, 0.15625f, 0.25f)]
    [InlineData(0.5f, 0.5f, 0.5f)]
    [InlineData(0.75f, 0.84375f, 0.75f)]
    [InlineData(1f, 1f, 1f)]
    public void CurveIsSmoothstepWhenOnAndStraightWhenOff(float t, float eased, float linear)
    {
        Assert.Equal(eased, InteractionLayerWeightPolicy.Ease(t, smooth: true), 6);
        Assert.Equal(linear, InteractionLayerWeightPolicy.Ease(t, smooth: false), 6);
    }

    [Fact]
    public void SwitchOffKeepsTheStraightLineLayerWeightsExactly()
    {
        BlendingSettings.SmoothBlending = false;
        foreach (float pinnedFullBodyWeight in new[] { -1f, 0.7f })
        {
            InteractionAnimationManifest manifest = LegacyManifest(
                startLayerWeight: 0.4f,
                rampSeconds: 1f,
                fullBodyLayerWeight: pinnedFullBodyWeight,
                enterSeconds: 0.5f,
                naturalEndSeconds: 0.8f,
                durationSeconds: 3f);
            InteractionLayerWeights weights = Build(manifest, 1, 2);

            foreach (float elapsed in new[] { 0f, 0.1f, 0.25f, 0.5f, 0.9f, 1.3f, 2.5f, 2.9f, 3.1f })
            {
                weights.Update(elapsed, false, 0f, 0f, InteractionAnimationViewer.LocalFirstPerson, false, 0f, 0.016f);
                (float fullBody, float firstPerson) = BaseFormula(manifest, elapsed);
                Assert.Equal(fullBody, weights.GetWeight(0));
                Assert.Equal(firstPerson, weights.GetWeight(1));
            }

            weights.Update(1.3f, false, 0f, 0f, InteractionAnimationViewer.LocalFirstPerson, false, 0f, 0.016f);
            (float exitStartFullBody, float exitStartFirstPerson) = BaseFormula(manifest, 1.3f);
            weights.BeginExit();
            foreach (float exitElapsed in new[] { 0f, 0.1f, 0.3f, 0.5f })
            {
                weights.Update(1.3f + exitElapsed, true, exitElapsed, 0.4f,
                    InteractionAnimationViewer.LocalFirstPerson, false, 0f, 0.016f);
                float exitT = UnityClamp01(exitElapsed / 0.4f);
                Assert.Equal(UnityLerp(exitStartFullBody, 0f, exitT), weights.GetWeight(0));
                Assert.Equal(UnityLerp(exitStartFirstPerson, 0f, exitT), weights.GetWeight(1));
            }
        }
    }

    [Fact]
    public void SwitchOnEasesRampEnterNaturalEndAndExit()
    {
        InteractionLayerWeights enter = Build(LegacyManifest(enterSeconds: 0.5f), 1, 2);
        enter.Update(0.125f, false, 0f, 0f, InteractionAnimationViewer.Remote, false, 0f, 0f);
        Assert.Equal(0.15625f, enter.GetWeight(0), 6);
        Assert.Equal(0.15625f, enter.GetWeight(1), 6);

        InteractionLayerWeights ramp = Build(LegacyManifest(startLayerWeight: 0.5f, rampSeconds: 1f), 1, 2);
        ramp.Update(0.25f, false, 0f, 0f, InteractionAnimationViewer.Remote, false, 0f, 0f);
        Assert.Equal(0.578125f, ramp.GetWeight(0), 6);
        Assert.Equal(0.578125f, ramp.SessionWeight, 6);

        InteractionLayerWeights naturalEnd = Build(
            LegacyManifest(naturalEndSeconds: 1f, durationSeconds: 2f), 1, 2);
        naturalEnd.Update(1.75f, false, 0f, 0f, InteractionAnimationViewer.Remote, false, 0f, 0f);
        Assert.Equal(0.15625f, naturalEnd.GetWeight(1), 6);

        InteractionLayerWeights exit = Build(LegacyManifest(), 1, 2);
        exit.Update(1f, false, 0f, 0f, InteractionAnimationViewer.Remote, false, 0f, 0f);
        exit.BeginExit();
        exit.Update(1.1f, true, 0.1f, 0.4f, InteractionAnimationViewer.Remote, false, 0f, 0f);
        Assert.Equal(0.84375f, exit.GetWeight(0), 6);
        Assert.Equal(0.84375f, exit.SessionWeight, 6);
        exit.Update(1.5f, true, 0.5f, 0.4f, InteractionAnimationViewer.Remote, false, 0f, 0f);
        Assert.Equal(0f, exit.GetWeight(1));
    }

    [Fact]
    public void RunningFadeKeepsTheCurveLatchedAtSessionStart()
    {
        InteractionAnimationManifest manifest = LegacyManifest(enterSeconds: 0.5f);
        InteractionLayerWeights latched = Build(manifest, 1, 2);

        BlendingSettings.SmoothBlending = false;
        latched.Update(0.125f, false, 0f, 0f, InteractionAnimationViewer.Remote, false, 0f, 0f);
        Assert.Equal(0.15625f, latched.GetWeight(0), 6);

        InteractionLayerWeights next = Build(manifest, 1, 2);
        next.Update(0.125f, false, 0f, 0f, InteractionAnimationViewer.Remote, false, 0f, 0f);
        Assert.Equal(0.25f, next.GetWeight(0), 6);
    }

    [Theory]
    [InlineData(false, true, false, InteractionAnimationViewer.Remote)]
    [InlineData(false, false, true, InteractionAnimationViewer.Remote)]
    [InlineData(true, true, true, InteractionAnimationViewer.LocalFirstPerson)]
    [InlineData(true, false, false, InteractionAnimationViewer.LocalFirstPerson)]
    [InlineData(true, true, false, InteractionAnimationViewer.LocalThirdPerson)]
    public void ViewerRuleFollowsLocalityAndBodyRendering(
        bool isLocal,
        bool modelEnabled,
        bool shadowsOnly,
        InteractionAnimationViewer expected)
    {
        Assert.Equal(expected, InteractionLayerWeightPolicy.ResolveViewer(isLocal, modelEnabled, shadowsOnly));
    }

    [Fact]
    public void ViewerFactorMovesOverOneTenthOfASecond()
    {
        Assert.True(InteractionLayerWeightPolicy.IsSeenBy(
            InteractionAnimationViewerMask.LocalThirdPerson, InteractionAnimationViewer.LocalThirdPerson));
        Assert.False(InteractionLayerWeightPolicy.IsSeenBy(
            InteractionAnimationViewerMask.LocalThirdPerson | InteractionAnimationViewerMask.Remote,
            InteractionAnimationViewer.LocalFirstPerson));

        InteractionAnimationManifest manifest = LayersManifest(
            Layer("ThirdPersonOnly", viewers: new[] { "localThirdPerson" }),
            Layer("Everyone"));
        InteractionLayerWeights weights = Build(manifest, 3, 4);

        weights.Update(1f, false, 0f, 0f, InteractionAnimationViewer.LocalFirstPerson, false, 0f, 0.016f);
        Assert.Equal(0f, weights.GetWeight(0));
        Assert.Equal(1f, weights.GetWeight(1));

        weights.Update(1f, false, 0f, 0f, InteractionAnimationViewer.LocalThirdPerson, false, 0f, 0.05f);
        Assert.Equal(0.5f, weights.GetWeight(0), 6);
        weights.Update(1f, false, 0f, 0f, InteractionAnimationViewer.LocalThirdPerson, false, 0f, 0.025f);
        Assert.Equal(0.84375f, weights.GetWeight(0), 5);
        weights.Update(1f, false, 0f, 0f, InteractionAnimationViewer.LocalThirdPerson, false, 0f, 0.05f);
        Assert.Equal(1f, weights.GetWeight(0));
        Assert.Equal(1f, weights.GetWeight(1));

        BlendingSettings.SmoothBlending = false;
        InteractionLayerWeights linear = Build(manifest, 3, 4);
        linear.Update(1f, false, 0f, 0f, InteractionAnimationViewer.Remote, false, 0f, 0.016f);
        linear.Update(1f, false, 0f, 0f, InteractionAnimationViewer.LocalThirdPerson, false, 0f, 0.025f);
        Assert.Equal(0.25f, linear.GetWeight(0), 6);
    }

    [Fact]
    public void LayersYieldToSprintAndHandsOnWallByTheStrongerYield()
    {
        InteractionAnimationManifest manifest = LayersManifest(
            Layer("Sprint", yieldTo: new[] { "sprint" }),
            Layer("Wall", yieldTo: new[] { "handsOnWall" }),
            Layer("Both", yieldTo: new[] { "sprint", "handsOnWall" }),
            Layer("Steady"));
        InteractionLayerWeights weights = Build(manifest, 3, 4, 5, 6);
        Assert.True(weights.YieldsToSprint);
        Assert.True(weights.YieldsToHandsOnWall);

        weights.Update(1f, false, 0f, 0f, InteractionAnimationViewer.Remote, false, 0f, 0.016f);
        AssertWeights(weights, 1f, 1f, 1f, 1f);

        weights.Update(1f, false, 0f, 0f, InteractionAnimationViewer.Remote, true, 0f, 0.05f);
        AssertWeights(weights, 0.5f, 1f, 0.5f, 1f);
        weights.Update(1f, false, 0f, 0f, InteractionAnimationViewer.Remote, true, 0.6f, 0.05f);
        AssertWeights(weights, 0f, 0.4f, 0f, 1f);
        weights.Update(1f, false, 0f, 0f, InteractionAnimationViewer.Remote, false, 0.6f, 0.05f);
        AssertWeights(weights, 0.5f, 0.4f, 0.4f, 1f);
        weights.Update(1f, false, 0f, 0f, InteractionAnimationViewer.Remote, false, 0f, 0.05f);
        AssertWeights(weights, 1f, 1f, 1f, 1f);

        InteractionLayerWeights legacy = Build(LegacyManifest(), 1, 2);
        Assert.False(legacy.YieldsToSprint);
        Assert.False(legacy.YieldsToHandsOnWall);
    }

    [Fact]
    public void RuntimeSkipsBaseLayerAndTakenIndicesButKeepsBothNamedLayers()
    {
        InteractionAnimationManifest manifest = LegacyManifest();
        manifest.body.layers = new[]
        {
            Layer("ResolvesToBase"),
            Layer("TakenIndex"),
            Layer("Missing"),
            Layer("Extra")
        };
        InteractionAnimationLayerSpec[] specs = InteractionAnimationLayerSpecs.Resolve(manifest.body);

        InteractionLayerWeights weights = InteractionLayerWeights.Build(
            manifest,
            specs,
            new[] { 1, 1, 0, 1, -1, 2 },
            out string skippedBaseLayer,
            out string skippedDuplicateLayer);

        Assert.Equal("ResolvesToBase", skippedBaseLayer);
        Assert.Equal("TakenIndex", skippedDuplicateLayer);
        Assert.Equal(3, weights.Count);
        Assert.Equal(1, weights.GetLayerIndex(0));
        Assert.Equal(1, weights.GetLayerIndex(1));
        Assert.Equal(2, weights.GetLayerIndex(2));
    }

    private static void AssertWeights(InteractionLayerWeights weights, params float[] expected)
    {
        Assert.Equal(expected.Length, weights.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], weights.GetWeight(i), 5);
    }

    private static InteractionLayerWeights Build(InteractionAnimationManifest manifest, params int[] indices)
    {
        InteractionAnimationLayerSpec[] specs = InteractionAnimationLayerSpecs.Resolve(manifest.body);
        return InteractionLayerWeights.Build(manifest, specs, indices, out _, out _);
    }

    // The fade of fullBodyLayer and firstPersonArmsLayer before the eased curve, outside an exit.
    private static (float fullBody, float firstPerson) BaseFormula(
        InteractionAnimationManifest manifest,
        float elapsedSeconds)
    {
        InteractionAnimationManifest.BodyManifest body = manifest.body;
        float startWeight = UnityClamp01(body.startLayerWeight <= 0f ? 1f : body.startLayerWeight);
        float weight = body.layerWeightRampSeconds > 0f
            ? UnityLerp(startWeight, 1f, UnityClamp01(elapsedSeconds / body.layerWeightRampSeconds))
            : 1f;
        float fullBodyWeight = body.fullBodyLayerWeight >= 0f
            ? UnityClamp01(body.fullBodyLayerWeight)
            : weight;
        float firstPersonWeight = weight;
        if (body.enterLayerFadeSeconds > 0f)
        {
            float enterT = UnityClamp01(elapsedSeconds / body.enterLayerFadeSeconds);
            fullBodyWeight *= enterT;
            firstPersonWeight *= enterT;
        }
        if (body.naturalEndLayerFadeSeconds > 0f && manifest.durationSeconds > 0f)
        {
            float remaining = manifest.durationSeconds - elapsedSeconds;
            float endT = UnityClamp01(remaining / body.naturalEndLayerFadeSeconds);
            fullBodyWeight *= endT;
            firstPersonWeight *= endT;
        }
        return (fullBodyWeight, firstPersonWeight);
    }

    // Unity's Mathf.Clamp01 and Mathf.Lerp as the engine defines them; the reference assemblies
    // the tests compile against carry no method bodies.
    private static float UnityClamp01(float value)
    {
        if (value < 0f)
            return 0f;
        if (value > 1f)
            return 1f;
        return value;
    }

    private static float UnityLerp(float a, float b, float t)
    {
        return a + (b - a) * UnityClamp01(t);
    }

    private static InteractionAnimationManifest LegacyManifest(
        float startLayerWeight = 1f,
        float rampSeconds = 0f,
        float fullBodyLayerWeight = -1f,
        float enterSeconds = 0f,
        float naturalEndSeconds = 0f,
        float durationSeconds = 0f)
    {
        return new InteractionAnimationManifest
        {
            durationSeconds = durationSeconds,
            body = new InteractionAnimationManifest.BodyManifest
            {
                enabled = true,
                fullBodyLayer = "FullBody",
                firstPersonArmsLayer = "FirstPersonArms",
                startLayerWeight = startLayerWeight,
                layerWeightRampSeconds = rampSeconds,
                fullBodyLayerWeight = fullBodyLayerWeight,
                enterLayerFadeSeconds = enterSeconds,
                naturalEndLayerFadeSeconds = naturalEndSeconds
            }
        };
    }

    private static InteractionAnimationManifest LayersManifest(
        params InteractionAnimationManifest.LayerManifest[] layers)
    {
        return new InteractionAnimationManifest
        {
            body = new InteractionAnimationManifest.BodyManifest
            {
                enabled = true,
                layers = layers
            }
        };
    }

    private static InteractionAnimationManifest.LayerManifest Layer(
        string name,
        string[] viewers = null,
        string[] yieldTo = null)
    {
        return new InteractionAnimationManifest.LayerManifest
        {
            name = name,
            viewers = viewers ?? Array.Empty<string>(),
            yieldTo = yieldTo ?? Array.Empty<string>()
        };
    }
}
