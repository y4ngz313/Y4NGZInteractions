using System;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal static class PropAttachBoneResolver
    {
        internal static T Resolve<T>(
            T root,
            InteractionAnimationManifest.PropManifest prop,
            Func<T, string, T> findExactPath,
            Func<T, string, T> findRecursiveName)
            where T : class
        {
            if (root == null || prop == null ||
                string.IsNullOrWhiteSpace(prop.attachBonePath))
            {
                return null;
            }

            return prop.useLegacyRecursiveAttachBoneLookup
                ? findRecursiveName(root, prop.attachBonePath)
                : findExactPath(root, prop.attachBonePath);
        }
    }
}
