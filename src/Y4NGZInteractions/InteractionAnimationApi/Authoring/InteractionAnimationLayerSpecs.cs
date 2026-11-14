using System;

namespace Y4NGZInteractions.InteractionAnimationApi.Authoring
{
    [Flags]
    internal enum InteractionAnimationViewerMask : byte
    {
        None = 0,
        LocalFirstPerson = 1,
        LocalThirdPerson = 2,
        Remote = 4,
        All = 7
    }

    [Flags]
    internal enum InteractionAnimationLayerYield : byte
    {
        None = 0,
        Sprint = 1,
        HandsOnWall = 2
    }

    internal readonly struct InteractionAnimationLayerSpec
    {
        internal InteractionAnimationLayerSpec(
            string name,
            InteractionAnimationViewerMask viewers,
            float weight,
            InteractionAnimationLayerYield yield)
        {
            Name = name;
            Viewers = viewers;
            Weight = weight;
            Yield = yield;
        }

        internal string Name { get; }

        internal InteractionAnimationViewerMask Viewers { get; }

        // FollowRampWeight follows the session ramp; 0 to 1 pins the base weight.
        internal float Weight { get; }

        internal InteractionAnimationLayerYield Yield { get; }
    }

    internal static class InteractionAnimationLayerSpecs
    {
        internal const int MaxLayers = 8;
        internal const float FollowRampWeight = -1f;

        // Vanilla player layers. Vanilla writes their weights every frame, so body.layers may not
        // name them.
        internal static readonly string[] ReservedLayerNames =
        {
            "Base Layer",
            "EmotesNoArms",
            "HoldingItemsRightHand",
            "UpperBodyEmotes",
            "HoldingItemsBothHands",
            "SpecialAnimations"
        };

        // Order: fullBodyLayer (weight fullBodyLayerWeight, any negative value as FollowRampWeight),
        // firstPersonArmsLayer (FollowRampWeight), then body.layers in order. Blank names are
        // skipped. The first two are seen by every viewer and never yield.
        internal static InteractionAnimationLayerSpec[] Resolve(
            InteractionAnimationManifest.BodyManifest body)
        {
            if (body == null)
                return Array.Empty<InteractionAnimationLayerSpec>();

            InteractionAnimationManifest.LayerManifest[] layers =
                body.layers ?? Array.Empty<InteractionAnimationManifest.LayerManifest>();
            bool hasFullBody = !string.IsNullOrWhiteSpace(body.fullBodyLayer);
            bool hasFirstPersonArms = !string.IsNullOrWhiteSpace(body.firstPersonArmsLayer);
            int count = (hasFullBody ? 1 : 0) + (hasFirstPersonArms ? 1 : 0);
            for (int i = 0; i < layers.Length; i++)
            {
                if (HasName(layers[i]))
                    count++;
            }

            var specs = new InteractionAnimationLayerSpec[count];
            int next = 0;
            if (hasFullBody)
            {
                specs[next++] = new InteractionAnimationLayerSpec(
                    body.fullBodyLayer,
                    InteractionAnimationViewerMask.All,
                    body.fullBodyLayerWeight < 0f ? FollowRampWeight : body.fullBodyLayerWeight,
                    InteractionAnimationLayerYield.None);
            }
            if (hasFirstPersonArms)
            {
                specs[next++] = new InteractionAnimationLayerSpec(
                    body.firstPersonArmsLayer,
                    InteractionAnimationViewerMask.All,
                    FollowRampWeight,
                    InteractionAnimationLayerYield.None);
            }
            for (int i = 0; i < layers.Length; i++)
            {
                InteractionAnimationManifest.LayerManifest layer = layers[i];
                if (!HasName(layer))
                    continue;
                specs[next++] = new InteractionAnimationLayerSpec(
                    layer.name,
                    ParseViewers(layer.viewers),
                    layer.weight,
                    ParseYields(layer.yieldTo));
            }
            return specs;
        }

        internal static bool TryParseViewer(string value, out InteractionAnimationViewerMask viewer)
        {
            switch (value)
            {
                case "localFirstPerson":
                    viewer = InteractionAnimationViewerMask.LocalFirstPerson;
                    return true;
                case "localThirdPerson":
                    viewer = InteractionAnimationViewerMask.LocalThirdPerson;
                    return true;
                case "remote":
                    viewer = InteractionAnimationViewerMask.Remote;
                    return true;
                default:
                    viewer = InteractionAnimationViewerMask.None;
                    return false;
            }
        }

        internal static bool TryParseYield(string value, out InteractionAnimationLayerYield yield)
        {
            switch (value)
            {
                case "sprint":
                    yield = InteractionAnimationLayerYield.Sprint;
                    return true;
                case "handsOnWall":
                    yield = InteractionAnimationLayerYield.HandsOnWall;
                    return true;
                default:
                    yield = InteractionAnimationLayerYield.None;
                    return false;
            }
        }

        internal static bool IsReservedLayerName(string name)
        {
            return Array.IndexOf(ReservedLayerNames, name) >= 0;
        }

        private static bool HasName(InteractionAnimationManifest.LayerManifest layer)
        {
            return layer != null && !string.IsNullOrWhiteSpace(layer.name);
        }

        // An absent or empty list means every viewer.
        private static InteractionAnimationViewerMask ParseViewers(string[] values)
        {
            if (values == null || values.Length == 0)
                return InteractionAnimationViewerMask.All;
            InteractionAnimationViewerMask mask = InteractionAnimationViewerMask.None;
            for (int i = 0; i < values.Length; i++)
            {
                if (TryParseViewer(values[i], out InteractionAnimationViewerMask viewer))
                    mask |= viewer;
            }
            return mask;
        }

        private static InteractionAnimationLayerYield ParseYields(string[] values)
        {
            InteractionAnimationLayerYield mask = InteractionAnimationLayerYield.None;
            if (values == null)
                return mask;
            for (int i = 0; i < values.Length; i++)
            {
                if (TryParseYield(values[i], out InteractionAnimationLayerYield yield))
                    mask |= yield;
            }
            return mask;
        }
    }
}
