using System;
using System.IO;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal static class InteractionAnimationAssetPathResolver
    {
        internal const string RootMissingReason = "pack_asset_root_missing";
        internal const string PathEscapesRootReason = "asset_bundle_path_escapes_root";

        internal static bool TryNormalizeAssetRoot(
            string assetRootPath,
            out string normalizedRoot,
            out string reason)
        {
            normalizedRoot = string.Empty;
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(assetRootPath))
            {
                reason = RootMissingReason;
                return false;
            }

            try
            {
                normalizedRoot = TrimEndingDirectorySeparators(
                    Path.GetFullPath(assetRootPath.Trim()));
            }
            catch (Exception exception)
            {
                reason = "pack_asset_root_invalid:" + exception.GetType().Name;
                normalizedRoot = string.Empty;
                return false;
            }

            if (!Directory.Exists(normalizedRoot))
            {
                reason = RootMissingReason + ":" + normalizedRoot;
                normalizedRoot = string.Empty;
                return false;
            }
            return true;
        }

        internal static bool TryResolveBundlePath(
            string bundleFileName,
            string normalizedAssetRoot,
            out string resolvedPath,
            out string reason)
        {
            resolvedPath = string.Empty;
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(bundleFileName))
            {
                reason = "asset_bundle_file_empty";
                return false;
            }
            if (string.IsNullOrWhiteSpace(normalizedAssetRoot))
            {
                reason = RootMissingReason;
                return false;
            }

            try
            {
                string candidate = Path.IsPathRooted(bundleFileName)
                    ? Path.GetFullPath(bundleFileName)
                    : Path.GetFullPath(Path.Combine(normalizedAssetRoot, bundleFileName));
                if (!IsWithinRoot(normalizedAssetRoot, candidate))
                {
                    reason = PathEscapesRootReason + ":" + bundleFileName;
                    return false;
                }

                resolvedPath = candidate;
                return true;
            }
            catch (Exception exception)
            {
                reason = "asset_bundle_path_invalid:" + exception.GetType().Name;
                return false;
            }
        }

        private static bool IsWithinRoot(string normalizedRoot, string candidate)
        {
            string root = TrimEndingDirectorySeparators(Path.GetFullPath(normalizedRoot));
            string fullCandidate = Path.GetFullPath(candidate);
            if (string.Equals(root, fullCandidate, StringComparison.OrdinalIgnoreCase))
                return true;
            return fullCandidate.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimEndingDirectorySeparators(string path)
        {
            string root = Path.GetPathRoot(path) ?? string.Empty;
            while (path.Length > root.Length &&
                   (path[path.Length - 1] == Path.DirectorySeparatorChar ||
                    path[path.Length - 1] == Path.AltDirectorySeparatorChar))
            {
                path = path.Substring(0, path.Length - 1);
            }
            return path;
        }
    }
}
