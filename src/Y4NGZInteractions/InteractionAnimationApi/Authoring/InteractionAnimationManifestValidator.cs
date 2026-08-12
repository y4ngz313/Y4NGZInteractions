using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Y4NGZInteractions.InteractionAnimationApi.Authoring
{
    internal static class InteractionAnimationManifestValidator
    {
        private static readonly JsonSerializerSettings StrictSchema2Settings =
            new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };

        internal static InteractionAnimationValidationReport Parse(
            string json,
            out InteractionAnimationManifest manifest)
        {
            manifest = null;
            var issues = new List<InteractionAnimationValidationIssue>();
            if (string.IsNullOrWhiteSpace(json))
            {
                AddError(issues, "manifest_json_empty", "$", "Manifest JSON is required.");
                return new InteractionAnimationValidationReport(issues);
            }

            JObject root;
            try
            {
                root = JObject.Parse(
                    json,
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling =
                            DuplicatePropertyNameHandling.Error
                    });
            }
            catch (JsonReaderException exception)
            {
                AddError(issues, "manifest_json_invalid", ToJsonPath(exception.Path),
                    "Manifest JSON is invalid: " + exception.Message);
                return new InteractionAnimationValidationReport(issues);
            }

            int schemaVersion = 1;
            JToken schemaToken = root["schemaVersion"];
            if (schemaToken == null)
            {
                foreach (JProperty property in root.Properties())
                {
                    if (string.Equals(
                            property.Name,
                            "schemaVersion",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        AddError(
                            issues,
                            "manifest_unknown_field",
                            "$." + property.Name,
                            "Schema-2 field names are case-sensitive.");
                        return new InteractionAnimationValidationReport(issues);
                    }
                }
            }
            if (schemaToken != null)
            {
                if (schemaToken.Type != JTokenType.Integer)
                {
                    AddError(issues, "manifest_schema_version_invalid", "$.schemaVersion",
                        "schemaVersion must be an integer.");
                    return new InteractionAnimationValidationReport(issues);
                }
                schemaVersion = schemaToken.Value<int>();
            }
            try
            {
                if (schemaVersion == 1)
                {
                    manifest = LegacyInteractionAnimationMigration.Normalize(
                        root.ToObject<LegacyInteractionAnimationManifest>());
                    issues.Add(new InteractionAnimationValidationIssue(
                        "manifest_schema_1_migrated", "$.schemaVersion",
                        "Schema 1 was accepted through the 1.x compatibility path; author schema 2 for new content.",
                        InteractionAnimationValidationSeverity.Warning));
                }
                else if (schemaVersion == 2)
                {
                    ValidateSchema2PropertyNames(root, issues);
                    if (issues.Count == 0)
                    {
                        manifest = JsonConvert.DeserializeObject<InteractionAnimationManifest>(
                            json, StrictSchema2Settings);
                    }
                }
                else
                {
                    AddError(issues, "manifest_schema_version_unsupported", "$.schemaVersion",
                        "Only schema versions 1 and 2 are supported.");
                }
            }
            catch (JsonSerializationException exception)
            {
                string code = exception.Message.IndexOf("Could not find member",
                    StringComparison.OrdinalIgnoreCase) >= 0
                    ? "manifest_unknown_field"
                    : "manifest_json_invalid";
                AddError(issues, code, ToJsonPath(exception.Path),
                    "Manifest does not match its schema: " + exception.Message);
            }
            catch (Exception exception)
            {
                AddError(issues, "manifest_json_invalid", "$",
                    "Manifest could not be read: " + exception.Message);
            }

            if (manifest == null && issues.Count == 0)
                AddError(issues, "manifest_json_returned_null", "$", "Manifest JSON produced no object.");
            return new InteractionAnimationValidationReport(issues);
        }

        internal static InteractionAnimationValidationReport Validate(
            string json,
            string expectedInteractionId,
            InteractionAnimationPresentationKind presentationKind,
            out InteractionAnimationManifest manifest)
        {
            InteractionAnimationValidationReport parsed = Parse(json, out manifest);
            var issues = new List<InteractionAnimationValidationIssue>(parsed.Issues);
            if (manifest != null && parsed.IsValid)
                ValidateNormalized(manifest, expectedInteractionId, presentationKind, issues);
            return new InteractionAnimationValidationReport(issues);
        }

        internal static string GetFirstErrorCode(InteractionAnimationValidationReport report)
        {
            if (report == null)
                return "validation_report_missing";
            for (int i = 0; i < report.Issues.Count; i++)
            {
                if (report.Issues[i].Severity == InteractionAnimationValidationSeverity.Error)
                    return report.Issues[i].Code;
            }
            return string.Empty;
        }

        private static void ValidateSchema2PropertyNames(
            JObject root,
            IList<InteractionAnimationValidationIssue> issues)
        {
            ValidateProperties(
                root,
                "$",
                issues,
                "schemaVersion",
                "interactionId",
                "durationSeconds",
                "bundleInternalName",
                "localViewmodel",
                "body");

            if (root["localViewmodel"] is JObject viewmodel)
            {
                const string path = "$.localViewmodel";
                ValidateProperties(
                    viewmodel,
                    path,
                    issues,
                    "bundleFileName",
                    "prefabAssetName",
                    "controllerAssetName",
                    "activeBool",
                    "enterTrigger",
                    "exitTrigger",
                    "exitSeconds",
                    "cameraAnchorPath",
                    "cameraLocalPosition",
                    "cameraLocalEuler",
                    "localScale",
                    "hideVanillaFirstPersonArms",
                    "prefabRenderersToHide",
                    "prefabRenderersToShow");
                ValidateVectorProperties(
                    viewmodel["cameraLocalPosition"],
                    path + ".cameraLocalPosition",
                    issues);
                ValidateVectorProperties(
                    viewmodel["cameraLocalEuler"],
                    path + ".cameraLocalEuler",
                    issues);
                ValidateVectorProperties(
                    viewmodel["localScale"],
                    path + ".localScale",
                    issues);
            }

            if (!(root["body"] is JObject body))
                return;
            const string bodyPath = "$.body";
            ValidateProperties(
                body,
                bodyPath,
                issues,
                "enabled",
                "bundleFileName",
                "controllerAssetName",
                "activeBool",
                "enterTrigger",
                "exitTrigger",
                "fullBodyLayer",
                "firstPersonArmsLayer",
                "startLayerWeight",
                "layerWeightRampSeconds",
                "fullBodyLayerWeight",
                "enterLayerFadeSeconds",
                "naturalEndLayerFadeSeconds",
                "rebuildRigBuilders",
                "exitSeconds",
                "movementParameter",
                "preserveGameplayCamera",
                "stopOnGameplayCameraDisplacement",
                "stabilizeLocalCameraPosition",
                "localCameraOwnedExternally",
                "stopOnVanillaSpecialAnimation",
                "clipPack",
                "prop");

            if (body["clipPack"] is JObject clipPack)
            {
                const string clipPackPath = "$.body.clipPack";
                ValidateProperties(
                    clipPack,
                    clipPackPath,
                    issues,
                    "enabled",
                    "bundleFileName",
                    "bundleInternalName",
                    "overrides");
                if (clipPack["overrides"] is JArray overrides)
                {
                    for (int i = 0; i < overrides.Count; i++)
                    {
                        if (overrides[i] is JObject item)
                        {
                            ValidateProperties(
                                item,
                                clipPackPath + ".overrides[" + i + "]",
                                issues,
                                "slot",
                                "clip");
                        }
                    }
                }
            }

            if (body["prop"] is JObject prop)
            {
                const string propPath = "$.body.prop";
                ValidateProperties(
                    prop,
                    propPath,
                    issues,
                    "enabled",
                    "prefabAssetName",
                    "attachBonePath",
                    "localPosition",
                    "localEulerAngles",
                    "localScale",
                    "releaseSeconds");
                ValidateVectorProperties(
                    prop["localPosition"],
                    propPath + ".localPosition",
                    issues);
                ValidateVectorProperties(
                    prop["localEulerAngles"],
                    propPath + ".localEulerAngles",
                    issues);
            }
        }

        private static void ValidateVectorProperties(
            JToken token,
            string path,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (token is JObject value)
                ValidateProperties(value, path, issues, "x", "y", "z");
        }

        private static void ValidateProperties(
            JObject value,
            string path,
            IList<InteractionAnimationValidationIssue> issues,
            params string[] allowedNames)
        {
            foreach (JProperty property in value.Properties())
            {
                bool allowed = false;
                for (int i = 0; i < allowedNames.Length; i++)
                {
                    if (string.Equals(
                            property.Name,
                            allowedNames[i],
                            StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }
                if (!allowed)
                {
                    AddError(
                        issues,
                        "manifest_unknown_field",
                        path + "." + property.Name,
                        "Schema-2 field names are exact and case-sensitive.");
                }
            }
        }
        private static void ValidateNormalized(
            InteractionAnimationManifest manifest,
            string expectedInteractionId,
            InteractionAnimationPresentationKind presentationKind,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (manifest.schemaVersion != 2)
                AddError(issues, "manifest_schema_version_invalid", "$.schemaVersion",
                    "The normalized schema version must be 2.");
            RequireIdentifier(manifest.interactionId, "$.interactionId",
                "manifest_interaction_id_empty", issues);
            if (!string.IsNullOrWhiteSpace(expectedInteractionId) &&
                !string.Equals(manifest.interactionId, expectedInteractionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddError(issues, "manifest_interaction_id_mismatch", "$.interactionId",
                    "The manifest interactionId must match its registered interaction id.");
            }
            ValidateNonNegativeFinite(manifest.durationSeconds, "$.durationSeconds",
                "manifest_duration_invalid", issues);

            if (presentationKind == InteractionAnimationPresentationKind.DedicatedLocalViewmodel)
                ValidateLocalViewmodel(manifest.localViewmodel, issues);
            else if (presentationKind == InteractionAnimationPresentationKind.BodyWorld)
                ValidateBody(manifest.body, issues);
            else
                AddError(issues, "presentation_kind_invalid", "$.presentationKind",
                    "The presentation kind is not supported by the 1.0 API.");
        }

        private static void ValidateLocalViewmodel(
            InteractionAnimationManifest.LocalViewmodelManifest viewmodel,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (viewmodel == null)
            {
                AddError(issues, "manifest_viewmodel_missing", "$.localViewmodel",
                    "DedicatedLocalViewmodel requires a localViewmodel object.");
                return;
            }
            ValidateBundlePath(viewmodel.bundleFileName, "$.localViewmodel.bundleFileName",
                "manifest_viewmodel_bundle_file_invalid", issues);
            RequireAssetName(viewmodel.prefabAssetName, "$.localViewmodel.prefabAssetName",
                "manifest_viewmodel_prefab_asset_invalid", issues);
            RequireAssetName(viewmodel.controllerAssetName, "$.localViewmodel.controllerAssetName",
                "manifest_viewmodel_controller_asset_invalid", issues);
            RequireTransformPath(viewmodel.cameraAnchorPath, "$.localViewmodel.cameraAnchorPath",
                "manifest_viewmodel_camera_anchor_invalid", issues);
            ValidateNonNegativeFinite(viewmodel.exitSeconds, "$.localViewmodel.exitSeconds",
                "manifest_viewmodel_exit_seconds_invalid", issues);
            ValidateVector(viewmodel.cameraLocalPosition, "$.localViewmodel.cameraLocalPosition", false, issues);
            ValidateVector(viewmodel.cameraLocalEuler, "$.localViewmodel.cameraLocalEuler", false, issues);
            ValidateVector(viewmodel.localScale, "$.localViewmodel.localScale", true, issues);
            ValidateTransformPaths(viewmodel.prefabRenderersToHide,
                "$.localViewmodel.prefabRenderersToHide", issues);
            ValidateTransformPaths(viewmodel.prefabRenderersToShow,
                "$.localViewmodel.prefabRenderersToShow", issues);
        }

        private static void ValidateBody(
            InteractionAnimationManifest.BodyManifest body,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (body == null || !body.enabled)
            {
                AddError(issues, "manifest_body_disabled", "$.body.enabled",
                    "BodyWorld requires body.enabled to be true.");
                return;
            }
            ValidateBundlePath(body.bundleFileName, "$.body.bundleFileName",
                "manifest_body_bundle_file_invalid", issues);
            RequireAssetName(body.controllerAssetName, "$.body.controllerAssetName",
                "manifest_body_controller_asset_invalid", issues);
            ValidateUnitWeight(body.startLayerWeight, "$.body.startLayerWeight",
                "manifest_body_start_layer_weight_invalid", issues);
            ValidateNonNegativeFinite(body.layerWeightRampSeconds, "$.body.layerWeightRampSeconds",
                "manifest_body_layer_ramp_invalid", issues);
            if (!IsFinite(body.fullBodyLayerWeight) || body.fullBodyLayerWeight > 1f)
                AddError(issues, "manifest_body_full_body_weight_invalid",
                    "$.body.fullBodyLayerWeight",
                    "fullBodyLayerWeight must be negative for shared ramping or between 0 and 1.");
            ValidateNonNegativeFinite(body.enterLayerFadeSeconds, "$.body.enterLayerFadeSeconds",
                "manifest_body_enter_fade_invalid", issues);
            ValidateNonNegativeFinite(body.naturalEndLayerFadeSeconds,
                "$.body.naturalEndLayerFadeSeconds", "manifest_body_natural_end_fade_invalid", issues);
            ValidateNonNegativeFinite(body.exitSeconds, "$.body.exitSeconds",
                "manifest_body_exit_seconds_invalid", issues);
            ValidateClipPack(body.clipPack, issues);
            ValidateProp(body.prop, issues);
        }

        private static void ValidateClipPack(
            InteractionAnimationManifest.ClipPackManifest clipPack,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (clipPack == null || !clipPack.enabled)
                return;
            ValidateBundlePath(clipPack.bundleFileName, "$.body.clipPack.bundleFileName",
                "manifest_clip_pack_bundle_file_invalid", issues);
            if (clipPack.overrides == null || clipPack.overrides.Length == 0)
            {
                AddError(issues, "manifest_clip_pack_overrides_empty", "$.body.clipPack.overrides",
                    "An enabled clip pack requires at least one override.");
                return;
            }
            var slots = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < clipPack.overrides.Length; i++)
            {
                string path = "$.body.clipPack.overrides[" + i + "]";
                InteractionAnimationManifest.ClipOverrideManifest item = clipPack.overrides[i];
                if (item == null)
                {
                    AddError(issues, "manifest_clip_override_null", path, "Clip override is required.");
                    continue;
                }
                RequireAssetName(item.slot, path + ".slot",
                    "manifest_clip_override_slot_invalid", issues);
                RequireAssetName(item.clip, path + ".clip",
                    "manifest_clip_override_asset_invalid", issues);
                if (!string.IsNullOrWhiteSpace(item.slot) && !slots.Add(item.slot))
                    AddError(issues, "manifest_clip_override_slot_duplicate", path + ".slot",
                        "Each controller slot may be overridden once.");
            }
        }

        private static void ValidateProp(
            InteractionAnimationManifest.PropManifest prop,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (prop == null || !prop.enabled)
                return;
            RequireAssetName(prop.prefabAssetName, "$.body.prop.prefabAssetName",
                "manifest_prop_prefab_asset_invalid", issues);
            RequireTransformPath(prop.attachBonePath, "$.body.prop.attachBonePath",
                "manifest_prop_attach_bone_invalid", issues);
            ValidateVector(prop.localPosition, "$.body.prop.localPosition", false, issues);
            ValidateVector(prop.localEulerAngles, "$.body.prop.localEulerAngles", false, issues);
            if (!IsFinite(prop.localScale) || prop.localScale <= 0f)
                AddError(issues, "manifest_prop_scale_invalid", "$.body.prop.localScale",
                    "Prop scale must be finite and greater than zero.");
            ValidateNonNegativeFinite(prop.releaseSeconds, "$.body.prop.releaseSeconds",
                "manifest_prop_release_seconds_invalid", issues);
        }

        private static void ValidateTransformPaths(
            string[] paths,
            string basePath,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (paths == null)
            {
                AddError(issues, "manifest_transform_path_array_null", basePath,
                    "Transform path arrays must be empty arrays instead of null.");
                return;
            }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < paths.Length; i++)
            {
                string path = basePath + "[" + i + "]";
                RequireTransformPath(paths[i], path, "manifest_transform_path_invalid", issues);
                if (!string.IsNullOrWhiteSpace(paths[i]) && !unique.Add(paths[i]))
                    issues.Add(new InteractionAnimationValidationIssue(
                        "manifest_transform_path_duplicate", path,
                        "Duplicate transform paths are ignored.",
                        InteractionAnimationValidationSeverity.Warning));
            }
        }

        private static void ValidateBundlePath(
            string value, string jsonPath, string code,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim() ||
                Path.IsPathRooted(value) || value.IndexOf('\\') >= 0 || HasTraversal(value))
                AddError(issues, code, jsonPath,
                    "Bundle paths must be trimmed, relative, slash-separated, and confined to the pack root.");
        }

        private static void RequireIdentifier(
            string value, string jsonPath, string code,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                AddError(issues, code, jsonPath, "A trimmed non-empty identifier is required.");
        }

        private static void RequireAssetName(
            string value, string jsonPath, string code,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || HasTraversal(value))
                AddError(issues, code, jsonPath, "A trimmed non-empty asset name is required.");
        }

        private static void RequireTransformPath(
            string value, string jsonPath, string code,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim() ||
                value.StartsWith("/", StringComparison.Ordinal) ||
                value.EndsWith("/", StringComparison.Ordinal) ||
                value.IndexOf('\\') >= 0 || value.IndexOf("//", StringComparison.Ordinal) >= 0 ||
                HasTraversal(value))
                AddError(issues, code, jsonPath,
                    "A canonical prefab-relative transform path is required.");
        }

        private static bool HasTraversal(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            string[] segments = value.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "." || segments[i] == "..")
                    return true;
            }
            return false;
        }

        private static void ValidateVector(
            InteractionAnimationVector3 value, string jsonPath, bool requirePositive,
            IList<InteractionAnimationValidationIssue> issues)
        {
            bool valid = IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
            if (requirePositive)
                valid &= value.x > 0f && value.y > 0f && value.z > 0f;
            if (!valid)
                AddError(issues, "manifest_vector_invalid", jsonPath,
                    requirePositive
                        ? "Vector components must be finite and greater than zero."
                        : "Vector components must be finite.");
        }

        private static void ValidateUnitWeight(
            float value, string jsonPath, string code,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (!IsFinite(value) || value < 0f || value > 1f)
                AddError(issues, code, jsonPath, "Layer weight must be between 0 and 1.");
        }

        private static void ValidateNonNegativeFinite(
            float value, string jsonPath, string code,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (!IsFinite(value) || value < 0f)
                AddError(issues, code, jsonPath, "Value must be finite and non-negative.");
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string ToJsonPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "$";
            return path.StartsWith("[", StringComparison.Ordinal) ? "$" + path : "$." + path;
        }

        private static void AddError(
            IList<InteractionAnimationValidationIssue> issues,
            string code, string jsonPath, string message)
        {
            issues.Add(new InteractionAnimationValidationIssue(
                code, jsonPath, message, InteractionAnimationValidationSeverity.Error));
        }
    }
}
