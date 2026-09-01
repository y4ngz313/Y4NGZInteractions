using System;
using System.Collections.Generic;
using System.IO;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    /// <summary>
    /// Pure decisions for detecting shadow copies of this plugin's assembly in the plugins tree.
    ///
    /// BepInEx deduplicates plugin GUIDs at discovery time ("Skipping [... 1.0.1] because a newer
    /// version exists"), but a skipped DLL still sits on disk, and the AssemblyResolve fallback
    /// serves consumers whichever matching FILE a folder scan finds first. A consumer bound to the
    /// skipped copy talks to a second, never-initialized set of API statics and permanently sees
    /// <c>interaction_animation_api_not_initialized</c> while the loaded copy logs a healthy
    /// startup. That failure produced no line of its own in the log, which is why this detector
    /// exists: any same-named DLL that is not the loaded file is an error worth naming at startup.
    /// </summary>
    internal static class DuplicateInstallPolicy
    {
        /// <summary>
        /// Returns every candidate path that names a different file than the loaded assembly,
        /// normalized, de-duplicated, and sorted for stable logging. An unknown loaded location
        /// (for example a byte-loaded assembly) returns no shadows: without a trusted identity for
        /// the live copy every candidate would be a false positive.
        /// </summary>
        internal static string[] FindShadowCopies(
            string loadedAssemblyPath,
            IEnumerable<string> candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(loadedAssemblyPath) || candidatePaths == null)
                return Array.Empty<string>();

            string loadedFull = TryNormalize(loadedAssemblyPath);
            if (loadedFull == null)
                return Array.Empty<string>();

            var shadows = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in candidatePaths)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                string candidateFull = TryNormalize(candidate);
                if (candidateFull == null)
                    continue;
                if (!string.Equals(candidateFull, loadedFull, StringComparison.OrdinalIgnoreCase))
                    shadows.Add(candidateFull);
            }

            var result = new string[shadows.Count];
            shadows.CopyTo(result);
            return result;
        }

        private static string TryNormalize(string path)
        {
            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
