using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    /// <summary>
    /// The <c>[Blending] Smooth Blending</c> switch. Sessions read it once at start (fade curve,
    /// viewer and yield transitions); an exit pose blend reads it once when Stop begins arming.
    /// </summary>
    internal static class BlendingSettings
    {
        internal const string Section = "Blending";
        internal const string SmoothBlendingKey = "Smooth Blending";
        internal const string SmoothBlendingDescription =
            "Eases interaction layer fades and blends every interaction back into the player's live vanilla pose when it ends. Off gives straight-line fades and a one-frame return to vanilla.";

        // Entries this plugin no longer binds. BepInEx keeps unbound entries and writes them back
        // on every save, so they are removed from the file explicitly.
        private static readonly ConfigDefinition[] RetiredEntries =
        {
            new ConfigDefinition("Interaction Animation API Restore Diagnostics", "Restore State Mode")
        };

        private static ConfigEntry<bool> smoothBlending;
        private static bool unboundSmoothBlending = true;
        private static bool pruneUnavailableLogged;

        /// <summary>
        /// The switch value: the bound config entry, or a plain value while nothing is bound.
        /// </summary>
        internal static bool SmoothBlending
        {
            get
            {
                ConfigEntry<bool> entry = smoothBlending;
                return entry != null ? entry.Value : unboundSmoothBlending;
            }
            set
            {
                ConfigEntry<bool> entry = smoothBlending;
                if (entry != null)
                    entry.Value = value;
                else
                    unboundSmoothBlending = value;
            }
        }

        internal static void Initialize(ConfigFile config, ManualLogSource log)
        {
            if (config == null)
                return;
            smoothBlending = config.Bind(Section, SmoothBlendingKey, true, SmoothBlendingDescription);
            PruneRetiredEntries(config, log);
        }

        /// <summary>
        /// Removes the retired entries from the file's unbound entries and saves only when one was
        /// removed, so a file that no longer holds them is left byte-identical.
        /// </summary>
        internal static bool PruneRetiredEntries(ConfigFile config, ManualLogSource log)
        {
            if (config == null)
                return false;

            PropertyInfo property = typeof(ConfigFile).GetProperty(
                "OrphanedEntries",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (!(property?.GetValue(config) is IDictionary<ConfigDefinition, string> orphanedEntries))
            {
                if (!pruneUnavailableLogged)
                {
                    pruneUnavailableLogged = true;
                    log?.LogWarning(
                        "[LCInteractionAnimationAPI] config.prune_unavailable: ConfigFile.OrphanedEntries " +
                        "is missing; retired entries stay in the config file.");
                }
                return false;
            }

            bool removed = false;
            for (int i = 0; i < RetiredEntries.Length; i++)
                removed |= orphanedEntries.Remove(RetiredEntries[i]);
            if (removed)
                config.Save();
            return removed;
        }

        /// <summary>Unbinds the entry; the switch reads the plain value again.</summary>
        internal static void Shutdown()
        {
            smoothBlending = null;
        }
    }
}
