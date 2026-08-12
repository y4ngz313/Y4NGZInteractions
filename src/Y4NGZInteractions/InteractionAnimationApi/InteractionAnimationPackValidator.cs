using System;
using System.Collections.Generic;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal sealed class RegisteredPackSnapshot
    {
        private readonly Dictionary<string, RegisteredInteractionSnapshot> interactions;

        internal RegisteredPackSnapshot(
            string packId,
            string version,
            string assetRootPath,
            Dictionary<string, RegisteredInteractionSnapshot> interactions)
        {
            PackId = packId;
            Version = version;
            AssetRootPath = assetRootPath;
            this.interactions = interactions;
        }

        internal string PackId { get; }
        internal string Version { get; }
        internal string AssetRootPath { get; }
        internal int InteractionCount => interactions.Count;

        internal bool TryGetInteraction(
            string interactionId,
            out RegisteredInteractionSnapshot interaction)
        {
            return interactions.TryGetValue(interactionId, out interaction);
        }
    }

    internal sealed class RegisteredInteractionSnapshot
    {
        internal RegisteredInteractionSnapshot(
            string interactionId,
            InteractionAnimationPresentationKind presentationKind,
            string manifestJson,
            InteractionAnimationManifest manifest)
        {
            InteractionId = interactionId;
            PresentationKind = presentationKind;
            ManifestJson = manifestJson;
            Manifest = manifest;
        }

        internal string InteractionId { get; }
        internal InteractionAnimationPresentationKind PresentationKind { get; }
        internal string ManifestJson { get; }
        internal InteractionAnimationManifest Manifest { get; }

        internal InteractionAnimationDefinition CreateDefinition()
        {
            return new InteractionAnimationDefinition
            {
                InteractionId = InteractionId,
                PresentationKind = PresentationKind,
                ManifestJson = ManifestJson
            };
        }
    }

    internal static class InteractionAnimationPackValidator
    {
        internal static InteractionAnimationValidationReport Validate(
            InteractionAnimationPackDefinition pack,
            out RegisteredPackSnapshot snapshot)
        {
            snapshot = null;
            var issues = new List<InteractionAnimationValidationIssue>();
            if (pack == null)
            {
                AddError(issues, "pack_null", "$", "Pack definition is required.");
                return new InteractionAnimationValidationReport(issues);
            }

            RequireValue(pack.PackId, "$.PackId", "pack_id_empty", issues);
            RequireValue(pack.Version, "$.Version", "pack_version_empty", issues);
            string normalizedRoot = string.Empty;
            if (!InteractionAnimationAssetPathResolver.TryNormalizeAssetRoot(
                    pack.AssetRootPath, out normalizedRoot, out string rootReason))
            {
                AddError(issues, ReasonCode(rootReason), "$.AssetRootPath",
                    "AssetRootPath must name an existing directory and is required for path confinement.");
            }

            var interactions = new Dictionary<string, RegisteredInteractionSnapshot>(
                StringComparer.OrdinalIgnoreCase);
            if (pack.Interactions == null || pack.Interactions.Length == 0)
            {
                AddError(issues, "pack_interactions_empty", "$.Interactions",
                    "At least one interaction is required.");
            }
            else
            {
                for (int i = 0; i < pack.Interactions.Length; i++)
                    ValidateInteraction(pack.Interactions[i], i, interactions, issues);
            }

            var report = new InteractionAnimationValidationReport(issues);
            if (report.IsValid)
                snapshot = new RegisteredPackSnapshot(
                    pack.PackId, pack.Version, normalizedRoot, interactions);
            return report;
        }

        private static void ValidateInteraction(
            InteractionAnimationDefinition definition,
            int index,
            IDictionary<string, RegisteredInteractionSnapshot> interactions,
            IList<InteractionAnimationValidationIssue> issues)
        {
            string path = "$.Interactions[" + index + "]";
            if (definition == null)
            {
                AddError(issues, "interaction_null", path, "Interaction is required.");
                return;
            }

            RequireValue(definition.InteractionId, path + ".InteractionId",
                "interaction_id_empty", issues);
            if (!Enum.IsDefined(typeof(InteractionAnimationPresentationKind),
                    definition.PresentationKind))
            {
                AddError(issues, "presentation_kind_invalid", path + ".PresentationKind",
                    "Only BodyWorld and DedicatedLocalViewmodel are supported.");
            }

            InteractionAnimationValidationReport manifestReport =
                InteractionAnimationManifestValidator.Validate(
                    definition.ManifestJson, definition.InteractionId,
                    definition.PresentationKind, out InteractionAnimationManifest manifest);
            for (int i = 0; i < manifestReport.Issues.Count; i++)
            {
                InteractionAnimationValidationIssue issue = manifestReport.Issues[i];
                issues.Add(new InteractionAnimationValidationIssue(
                    issue.Code,
                    path + ".ManifestJson" + issue.JsonPath.Substring(1),
                    issue.Message,
                    issue.Severity));
            }

            if (string.IsNullOrWhiteSpace(definition.InteractionId))
                return;
            if (interactions.ContainsKey(definition.InteractionId))
            {
                AddError(issues, "interaction_id_duplicate", path + ".InteractionId",
                    "Interaction ids must be unique within a pack.");
            }
            else if (manifestReport.IsValid && manifest != null)
            {
                interactions.Add(definition.InteractionId,
                    new RegisteredInteractionSnapshot(
                        definition.InteractionId,
                        definition.PresentationKind,
                        definition.ManifestJson ?? string.Empty,
                        manifest));
            }
        }

        private static void RequireValue(
            string value,
            string path,
            string code,
            IList<InteractionAnimationValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                AddError(issues, code, path, "A trimmed non-empty value is required.");
        }

        private static string ReasonCode(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return "pack_asset_root_invalid";
            int separator = reason.IndexOf(':');
            return separator >= 0 ? reason.Substring(0, separator) : reason;
        }

        private static void AddError(
            IList<InteractionAnimationValidationIssue> issues,
            string code,
            string path,
            string message)
        {
            issues.Add(new InteractionAnimationValidationIssue(
                code, path, message, InteractionAnimationValidationSeverity.Error));
        }
    }
}
