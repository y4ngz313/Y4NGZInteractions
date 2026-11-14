using System;
using UnityEngine.LowLevel;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// Owns the player-loop system that runs <see cref="ExitPoseBlend.BeforeAnimationPass"/> at
    /// the head of PreLateUpdate: after Update and coroutines, right before the animators update.
    /// The runtime host installs it once and removes it at teardown; every other system is left
    /// as it is.
    /// </summary>
    internal static class ExitPoseBlendPlayerLoop
    {
        // Reads and replaces the engine's current player loop. Replaced only by tests.
        internal static Func<PlayerLoopSystem> ReadLoop = PlayerLoop.GetCurrentPlayerLoop;
        internal static Action<PlayerLoopSystem> WriteLoop = PlayerLoop.SetPlayerLoop;

        internal static bool IsInstalled { get; private set; }

        // Installs the system unless it is already present. Returns false when the loop has no
        // PreLateUpdate phase.
        internal static bool Install()
        {
            PlayerLoopSystem loop = ReadLoop();
            if (!Contains(loop, typeof(ExitPoseBlendBeforeAnimationPass)))
            {
                var system = new PlayerLoopSystem
                {
                    type = typeof(ExitPoseBlendBeforeAnimationPass),
                    updateDelegate = ExitPoseBlend.BeforeAnimationPass
                };
                if (!TryInsertAtPhaseHead(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate), system))
                {
                    IsInstalled = false;
                    return false;
                }
                WriteLoop(loop);
            }

            IsInstalled = true;
            return true;
        }

        // Removes the system wherever a phase lists it; the loop is written only when it changed.
        internal static void Uninstall()
        {
            IsInstalled = false;
            PlayerLoopSystem loop = ReadLoop();
            if (TryRemove(ref loop, typeof(ExitPoseBlendBeforeAnimationPass)))
                WriteLoop(loop);
        }

        internal static bool Contains(PlayerLoopSystem root, Type systemType)
        {
            PlayerLoopSystem[] phases = root.subSystemList;
            if (phases == null)
                return false;
            for (int p = 0; p < phases.Length; p++)
            {
                PlayerLoopSystem[] systems = phases[p].subSystemList;
                if (systems != null && IndexOf(systems, systemType) >= 0)
                    return true;
            }
            return false;
        }

        // Puts the system first in the named phase. The phase lists are copied, not edited.
        internal static bool TryInsertAtPhaseHead(ref PlayerLoopSystem root, Type phaseType, PlayerLoopSystem system)
        {
            PlayerLoopSystem[] phases = root.subSystemList;
            if (phases == null)
                return false;
            for (int p = 0; p < phases.Length; p++)
            {
                if (phases[p].type != phaseType)
                    continue;

                PlayerLoopSystem[] systems = phases[p].subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var updated = new PlayerLoopSystem[systems.Length + 1];
                updated[0] = system;
                Array.Copy(systems, 0, updated, 1, systems.Length);
                var updatedPhases = (PlayerLoopSystem[])phases.Clone();
                updatedPhases[p].subSystemList = updated;
                root.subSystemList = updatedPhases;
                return true;
            }
            return false;
        }

        internal static bool TryRemove(ref PlayerLoopSystem root, Type systemType)
        {
            PlayerLoopSystem[] phases = root.subSystemList;
            if (phases == null)
                return false;

            PlayerLoopSystem[] updatedPhases = null;
            for (int p = 0; p < phases.Length; p++)
            {
                PlayerLoopSystem[] systems = phases[p].subSystemList;
                if (systems == null || IndexOf(systems, systemType) < 0)
                    continue;

                int kept = 0;
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i].type != systemType)
                        kept++;
                }
                var remaining = new PlayerLoopSystem[kept];
                kept = 0;
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i].type != systemType)
                        remaining[kept++] = systems[i];
                }

                updatedPhases ??= (PlayerLoopSystem[])phases.Clone();
                updatedPhases[p].subSystemList = remaining;
            }

            if (updatedPhases == null)
                return false;
            root.subSystemList = updatedPhases;
            return true;
        }

        private static int IndexOf(PlayerLoopSystem[] systems, Type systemType)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].type == systemType)
                    return i;
            }
            return -1;
        }

        // Names the system in the player loop and the profiler.
        internal struct ExitPoseBlendBeforeAnimationPass
        {
        }
    }
}
