using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Y4NGZInteractions.Examples.Editor
{
    public static class Y4NGZInteractionContractValidator
    {
        [MenuItem("Y4NGZ Interactions/Inspect Selected Controller")]
        public static void InspectSelectedController()
        {
            AnimatorController controller = Selection.activeObject as AnimatorController;
            if (controller == null)
                throw new InvalidOperationException("Select an AnimatorController.");

            string layers = string.Join(", ", controller.layers.Select(layer => layer.name));
            string parameters = string.Join(
                ", ",
                controller.parameters.Select(parameter =>
                    parameter.name + ":" + parameter.type));
            Debug.Log("Layers: " + layers + "\nParameters: " + parameters);
        }

        public static void ValidateAllOrThrow()
        {
            var issues = new List<string>();

            GameObject bodyProxy = AssetDatabase.LoadAssetAtPath<GameObject>(
                Y4NGZInteractionExampleBuilder.SourceRoot + "/BodyWorldProxy.prefab");
            AnimatorController bodyController = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                Y4NGZInteractionExampleBuilder.BodyControllerPath);
            AnimationClip bodyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                Y4NGZInteractionExampleBuilder.BodyClipPath);
            AddRange(issues, ValidateController(
                bodyController,
                new[] { "Base Layer" },
                RequiredParameters()));
            AddRange(issues, ValidateTransformPaths(
                bodyProxy != null ? bodyProxy.transform : null,
                new[] { "spine.004/shoulder.R/arm.R" },
                "body"));
            AddRange(issues, ValidateClipBindings(
                bodyClip,
                bodyProxy != null ? bodyProxy.transform : null));

            GameObject viewmodel = AssetDatabase.LoadAssetAtPath<GameObject>(
                Y4NGZInteractionExampleBuilder.ViewmodelPrefabPath);
            AnimatorController viewmodelController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    Y4NGZInteractionExampleBuilder.ViewmodelControllerPath);
            AnimationClip viewmodelClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                Y4NGZInteractionExampleBuilder.ViewmodelClipPath);
            AddRange(issues, ValidateController(
                viewmodelController,
                new[] { "Base Layer" },
                RequiredParameters()));
            AddRange(issues, ValidateCameraAnchor(
                viewmodel != null ? viewmodel.transform : null,
                "Rig/CameraAnchor"));
            AddRange(issues, ValidateRendererPaths(
                viewmodel != null ? viewmodel.transform : null,
                new[] { "Rig/Arm", "Rig/Prop" }));
            AddRange(issues, ValidateClipBindings(
                viewmodelClip,
                viewmodel != null ? viewmodel.transform : null));
            AddRange(issues, ValidatePropAttachment(
                bodyProxy != null ? bodyProxy.transform : null,
                "spine.004/shoulder.R/arm.R/forearm.R/hand.R"));

            ThrowIfAny(issues);
        }

        public static void ValidateSelectionOrThrow()
        {
            UnityEngine.Object selected = Selection.activeObject;
            var issues = new List<string>();
            if (selected is AnimatorController controller)
                AddRange(issues, ValidateController(
                    controller, Array.Empty<string>(), Array.Empty<ParameterRequirement>()));
            else if (selected is AnimationClip clip)
                AddRange(issues, ValidateClipBindings(clip, null));
            else if (selected is GameObject prefab)
                AddRange(issues, ValidateCameraAnchor(prefab.transform, "Rig/CameraAnchor"));
            else
                issues.Add("selection.unsupported: Select a prefab, clip, or controller.");
            ThrowIfAny(issues);
        }

        public static IReadOnlyList<string> ValidateController(
            AnimatorController controller,
            IEnumerable<string> requiredLayers,
            IEnumerable<ParameterRequirement> requiredParameters)
        {
            var issues = new List<string>();
            if (controller == null)
            {
                issues.Add("controller.missing");
                return issues;
            }

            var layers = new HashSet<string>(
                controller.layers.Select(layer => layer.name),
                StringComparer.Ordinal);
            foreach (string layer in requiredLayers ?? Array.Empty<string>())
            {
                if (!layers.Contains(layer))
                    issues.Add("controller.layer_missing:" + layer);
            }

            var parameters = controller.parameters.ToDictionary(
                parameter => parameter.name,
                parameter => parameter.type,
                StringComparer.Ordinal);
            foreach (ParameterRequirement requirement in
                     requiredParameters ?? Array.Empty<ParameterRequirement>())
            {
                if (!parameters.TryGetValue(requirement.Name, out AnimatorControllerParameterType type))
                    issues.Add("controller.parameter_missing:" + requirement.Name);
                else if (type != requirement.Type)
                    issues.Add("controller.parameter_type:" + requirement.Name);
            }
            return issues;
        }

        public static IReadOnlyList<string> ValidateTransformPaths(
            Transform root,
            IEnumerable<string> paths,
            string category)
        {
            var issues = new List<string>();
            if (root == null)
            {
                issues.Add(category + ".root_missing");
                return issues;
            }

            foreach (string path in paths ?? Array.Empty<string>())
            {
                if (!IsCanonicalPath(path))
                    issues.Add(category + ".path_invalid:" + path);
                else if (root.Find(path) == null)
                    issues.Add(category + ".path_missing:" + path);
            }
            return issues;
        }

        public static IReadOnlyList<string> ValidateCameraAnchor(
            Transform root,
            string path)
        {
            var issues = new List<string>(
                ValidateTransformPaths(root, new[] { path }, "camera_anchor"));
            if (root != null && IsCanonicalPath(path))
            {
                Transform anchor = root.Find(path);
                if (anchor != null && anchor.GetComponentsInChildren<Camera>(true).Length > 1)
                    issues.Add("camera_anchor.multiple_cameras");
            }
            return issues;
        }

        public static IReadOnlyList<string> ValidateRendererPaths(
            Transform root,
            IEnumerable<string> paths)
        {
            var issues = new List<string>(
                ValidateTransformPaths(root, paths, "renderer"));
            if (root == null)
                return issues;

            foreach (string path in paths ?? Array.Empty<string>())
            {
                Transform value = IsCanonicalPath(path) ? root.Find(path) : null;
                if (value != null && value.GetComponent<Renderer>() == null)
                    issues.Add("renderer.component_missing:" + path);
            }
            return issues;
        }

        public static IReadOnlyList<string> ValidatePropAttachment(
            Transform root,
            string attachBonePath)
        {
            return ValidateTransformPaths(
                root, new[] { attachBonePath }, "prop_attachment");
        }

        public static IReadOnlyList<string> ValidateClipBindings(
            AnimationClip clip,
            Transform root)
        {
            var issues = new List<string>();
            if (clip == null)
            {
                issues.Add("clip.missing");
                return issues;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length == 0)
                issues.Add("clip.bindings_empty");
            foreach (EditorCurveBinding binding in bindings)
            {
                if (!IsCanonicalPath(binding.path))
                    issues.Add("clip.binding_path_invalid:" + binding.path);
                else if (root != null && root.Find(binding.path) == null)
                    issues.Add("clip.binding_path_missing:" + binding.path);
            }
            return issues;
        }

        public static bool IsCanonicalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                path.StartsWith("/", StringComparison.Ordinal) ||
                path.EndsWith("/", StringComparison.Ordinal) ||
                path.Contains("\\") ||
                path.Contains("//"))
                return false;
            return path.Split('/').All(segment =>
                segment.Length > 0 && segment != "." && segment != "..");
        }

        public readonly struct ParameterRequirement
        {
            public ParameterRequirement(
                string name,
                AnimatorControllerParameterType type)
            {
                Name = name;
                Type = type;
            }

            public string Name { get; }
            public AnimatorControllerParameterType Type { get; }
        }

        private static ParameterRequirement[] RequiredParameters()
        {
            return new[]
            {
                new ParameterRequirement(
                    "InteractionActive", AnimatorControllerParameterType.Bool),
                new ParameterRequirement("Enter", AnimatorControllerParameterType.Trigger),
                new ParameterRequirement("Exit", AnimatorControllerParameterType.Trigger),
                new ParameterRequirement("MovementState", AnimatorControllerParameterType.Int),
                new ParameterRequirement("Blend", AnimatorControllerParameterType.Float)
            };
        }

        private static void AddRange(
            ICollection<string> destination,
            IEnumerable<string> source)
        {
            foreach (string issue in source)
                destination.Add(issue);
        }

        private static void ThrowIfAny(IReadOnlyCollection<string> issues)
        {
            if (issues.Count == 0)
                return;
            throw new InvalidOperationException(
                "Example authoring validation failed:\n" + string.Join("\n", issues));
        }
    }
}
