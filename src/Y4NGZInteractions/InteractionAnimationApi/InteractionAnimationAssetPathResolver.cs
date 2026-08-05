using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal static class InteractionAnimationAssetPathResolver
    {
        internal const string RootMissingReason = "pack_asset_root_missing";
        internal const string PathEscapesRootReason = "asset_bundle_path_escapes_root";

        // Thunderstore/Gale installs use the team-name prefixed folder; the bare
        // folder name is kept for local dev installs that predate the rename.
        private const string PluginFolderName = "y4ngz313-Y4NGZInteractions";
        private const string LegacyPluginFolderName = "y4ngz-Y4NGZInteractions";

        private static string[] cachedDefaultRoots;

        /// <summary>
        /// Ordered fallback roots used when a consumer supplies no AssetRootPath.
        /// The assembly's own directory wins because it is correct for every
        /// install layout; the named plugin folders and the BepInEx paths follow.
        /// </summary>
        internal static string[] GetDefaultAssetRoots()
        {
            if (cachedDefaultRoots != null)
                return cachedDefaultRoots;

            var roots = new List<string>(5);

            string assemblyDirectory = TryGetExecutingAssemblyDirectory();
            if (!string.IsNullOrWhiteSpace(assemblyDirectory))
                roots.Add(assemblyDirectory);

            try
            {
                roots.Add(Path.Combine(Paths.PluginPath, PluginFolderName));
                roots.Add(Path.Combine(Paths.PluginPath, LegacyPluginFolderName));
                roots.Add(Paths.PluginPath);
            }
            catch
            {
                // Paths is unavailable outside a BepInEx host; the remaining
                // candidates still cover the assembly-relative layout.
            }

            roots.Add(AppDomain.CurrentDomain.BaseDirectory);

            cachedDefaultRoots = roots.ToArray();
            return cachedDefaultRoots;
        }

        private static string TryGetExecutingAssemblyDirectory()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(location))
                    return string.Empty;

                return Path.GetDirectoryName(location) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static bool TryNormalizeAssetRoot(
            string assetRootPath,
            out string normalizedRoot,
            out string reason)
        {
            normalizedRoot = string.Empty;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(assetRootPath))
                return true;

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
            string[] legacyRoots,
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

            if (!string.IsNullOrWhiteSpace(normalizedAssetRoot))
            {
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

            // AssetRootPath is optional. With no consumer root, preserve the exact legacy
            // behavior: rooted names pass through, then the first existing fallback wins,
            // and a missing file resolves to the first fallback for useful diagnostics.
            if (Path.IsPathRooted(bundleFileName))
            {
                resolvedPath = bundleFileName;
                return true;
            }

            string firstCandidate = string.Empty;
            string[] roots = legacyRoots ?? Array.Empty<string>();
            for (int i = 0; i < roots.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(roots[i]))
                    continue;

                string candidate = Path.Combine(roots[i], bundleFileName);
                if (string.IsNullOrEmpty(firstCandidate))
                    firstCandidate = candidate;
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }

            resolvedPath = firstCandidate;
            return true;
        }

        private static bool IsWithinRoot(string normalizedRoot, string candidate)
        {
            string root = TrimEndingDirectorySeparators(Path.GetFullPath(normalizedRoot));
            string fullCandidate = Path.GetFullPath(candidate);
            if (string.Equals(root, fullCandidate, StringComparison.OrdinalIgnoreCase))
                return true;

            string rootPrefix = root + Path.DirectorySeparatorChar;
            return fullCandidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
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
