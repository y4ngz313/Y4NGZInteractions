using System;

namespace Y4NGZInteractions.InteractionAnimationApi.Authoring
{
    internal static class LegacyInteractionAnimationMigration
    {
        internal static InteractionAnimationManifest Normalize(
            LegacyInteractionAnimationManifest legacy)
        {
            legacy = legacy ?? new LegacyInteractionAnimationManifest();
            LegacyInteractionAnimationManifest.LegacyLocalViewmodelManifest oldView =
                legacy.localViewmodel ??
                new LegacyInteractionAnimationManifest.LegacyLocalViewmodelManifest();
            LegacyInteractionAnimationManifest.LegacyBodyManifest oldBody =
                legacy.body ?? new LegacyInteractionAnimationManifest.LegacyBodyManifest();
            LegacyInteractionAnimationManifest.LegacyClipPackManifest oldClipPack =
                oldBody.clipPack ??
                new LegacyInteractionAnimationManifest.LegacyClipPackManifest();
            LegacyInteractionAnimationManifest.LegacyPropManifest oldProp =
                oldBody.prop ?? new LegacyInteractionAnimationManifest.LegacyPropManifest();

            return new InteractionAnimationManifest
            {
                schemaVersion = 2,
                interactionId = legacy.interactionId ?? string.Empty,
                durationSeconds = legacy.durationSeconds,
                bundleInternalName = legacy.bundleInternalName ?? string.Empty,
                localViewmodel = new InteractionAnimationManifest.LocalViewmodelManifest
                {
                    bundleFileName = oldView.bundleFileName ?? string.Empty,
                    prefabAssetName = oldView.prefab ?? string.Empty,
                    controllerAssetName = oldView.controller ?? string.Empty,
                    activeBool = oldView.activeBool ?? string.Empty,
                    enterTrigger = oldView.enterTrigger ?? string.Empty,
                    exitTrigger = oldView.exitTrigger ?? string.Empty,
                    exitSeconds = oldView.exitSeconds,
                    cameraAnchorPath = oldView.cameraAnchor ?? string.Empty,
                    cameraLocalPosition = oldView.cameraLocalPosition,
                    cameraLocalEuler = oldView.cameraLocalEuler,
                    localScale = oldView.localScale,
                    hideVanillaFirstPersonArms = ContainsLegacyArmHint(
                        legacy.liveRenderersToHide),
                    prefabRenderersToHide =
                        oldView.hideSourceRenderers ?? Array.Empty<string>(),
                    prefabRenderersToShow =
                        oldView.visibleRenderers ?? Array.Empty<string>()
                },
                body = new InteractionAnimationManifest.BodyManifest
                {
                    enabled = oldBody.enabled,
                    bundleFileName = oldBody.bundleFileName ?? string.Empty,
                    controllerAssetName = !string.IsNullOrWhiteSpace(
                        oldBody.controllerAssetName)
                        ? oldBody.controllerAssetName
                        : oldBody.controller ?? string.Empty,
                    activeBool = oldBody.activeBool ?? string.Empty,
                    enterTrigger = oldBody.enterTrigger ?? string.Empty,
                    exitTrigger = oldBody.exitTrigger ?? string.Empty,
                    fullBodyLayer = oldBody.fullBodyLayer ?? string.Empty,
                    firstPersonArmsLayer = oldBody.firstPersonArmsLayer ?? string.Empty,
                    startLayerWeight = oldBody.startLayerWeight,
                    layerWeightRampSeconds = oldBody.layerWeightRampSeconds,
                    fullBodyLayerWeight = oldBody.fullBodyLayerWeight,
                    enterLayerFadeSeconds = oldBody.enterLayerFadeSeconds,
                    naturalEndLayerFadeSeconds = oldBody.naturalEndLayerFadeSeconds,
                    rebuildRigBuilders = !oldBody.suppressRigBuilders,
                    exitSeconds = oldBody.exitSeconds,
                    movementParameter = oldBody.movementParameter ?? string.Empty,
                    preserveGameplayCamera =
                        !legacy.exemptFromCameraDisplacementGuard &&
                        !oldBody.localCameraOwnedExternally,
                    stopOnVanillaSpecialAnimation =
                        !legacy.exemptFromSpecialAnimationAutoStop,
                    clipPack = new InteractionAnimationManifest.ClipPackManifest
                    {
                        enabled = oldClipPack.enabled,
                        bundleFileName = oldClipPack.bundleFileName ?? string.Empty,
                        bundleInternalName = oldClipPack.bundleInternalName ?? string.Empty,
                        overrides = NormalizeOverrides(oldClipPack.overrides)
                    },
                    prop = new InteractionAnimationManifest.PropManifest
                    {
                        useLegacyRecursiveAttachBoneLookup = true,
                        enabled = oldProp.enabled,
                        prefabAssetName = oldProp.prefabName ?? string.Empty,
                        attachBonePath = oldProp.attachBone ?? string.Empty,
                        localPosition = oldProp.localPosition,
                        localEulerAngles = oldProp.localEulerAngles,
                        localScale = oldProp.localScale,
                        releaseSeconds = oldProp.releaseSeconds
                    }
                }
            };
        }

        private static InteractionAnimationManifest.ClipOverrideManifest[] NormalizeOverrides(
            LegacyInteractionAnimationManifest.LegacyClipOverrideManifest[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<InteractionAnimationManifest.ClipOverrideManifest>();

            var result = new InteractionAnimationManifest.ClipOverrideManifest[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                LegacyInteractionAnimationManifest.LegacyClipOverrideManifest item = source[i];
                result[i] = new InteractionAnimationManifest.ClipOverrideManifest
                {
                    slot = item?.slot ?? string.Empty,
                    clip = item?.clip ?? string.Empty
                };
            }
            return result;
        }

        private static bool ContainsLegacyArmHint(string[] hints)
        {
            if (hints == null)
                return false;
            for (int i = 0; i < hints.Length; i++)
            {
                if (string.Equals(hints[i], "thisPlayerModelArms",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(hints[i], "lc_first_person_hands",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
