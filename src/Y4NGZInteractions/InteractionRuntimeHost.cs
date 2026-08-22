using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions
{
    /// <summary>
    /// Runtime lifecycle owner for the plugin: the per-frame API tick, the host for coroutines
    /// and diagnostics components, and the sole place teardown happens.
    /// </summary>
    /// <remarks>
    /// #31: this used to live on <see cref="Plugin"/> itself, which BepInEx instantiates as a
    /// component on its shared "BepInEx_Manager" GameObject. With the BepInEx default
    /// Chainloader/HideManagerGameObject=false that object carries no hide flags, so anything
    /// doing <c>GameObject.Find</c>-based cleanup can destroy it right after chainloader startup
    /// — taking every plugin component riding on it down at once. When that happened here the
    /// plugin's OnDestroy ran <c>Shutdown()</c> plus <c>Harmony.UnpatchSelf()</c> at frame 0 and
    /// the animation API was dead for the whole session (dependants such as LethalCCTV then fell
    /// back to legacy playback). Owning the host object is the fix: DontDestroyOnLoad gives it
    /// persistence and HideFlags.HideAndDontSave keeps it out of <c>GameObject.Find</c>, so
    /// nothing external can reach it by name. Standing convention across the Y4NGZ plugins —
    /// do not move these responsibilities back onto BepInEx_Manager.
    /// </remarks>
    internal sealed class InteractionRuntimeHost : MonoBehaviour
    {
        private Harmony harmony;
        private ManualLogSource log;
        private bool applicationQuitting;
        private bool tornDown;

        internal void Initialize(Harmony harmony, ManualLogSource log)
        {
            this.harmony = harmony;
            this.log = log;
            Application.quitting += OnApplicationQuitting;
        }

        private void LateUpdate()
        {
            InteractionAnimationApiRestoreDiagnostics.BeginCoordinatorLateUpdateTick();
            try
            {
                InteractionAnimationApiPlugin.Tick(Time.deltaTime);
            }
            finally
            {
                InteractionAnimationApiRestoreDiagnostics.EndCoordinatorLateUpdateTick();
            }
        }

        private void OnApplicationQuit()
        {
            applicationQuitting = true;
        }

        private void OnApplicationQuitting()
        {
            applicationQuitting = true;
            TearDown();
        }

        private void OnDestroy()
        {
            Application.quitting -= OnApplicationQuitting;

            // A destroy outside process shutdown means something reached this object deliberately.
            // Restoration still has to run, but it is a fault worth naming in the log rather than
            // a silent session-wide loss of the API (#31).
            if (!applicationQuitting)
            {
                log?.LogError(
                    $"{Plugin.Name} runtime host destroyed outside application shutdown " +
                    $"(frame={Time.frameCount}, host='{(gameObject != null ? gameObject.name : "<null>")}'). " +
                    "The interaction animation API and all Harmony patches are going down with it.");
            }

            TearDown();
        }

        private void TearDown()
        {
            if (tornDown)
                return;
            tornDown = true;

            try
            {
                InteractionAnimationApiPlugin.Shutdown();
                InteractionAnimationApiRestoreDiagnostics.Shutdown();
            }
            catch (Exception exception)
            {
                log?.LogWarning($"{Plugin.Name} shutdown warning: {exception.Message}");
            }

            harmony?.UnpatchSelf();
        }
    }
}
