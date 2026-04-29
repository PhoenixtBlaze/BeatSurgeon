using System;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using HarmonyLib;
using UnityEngine;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals), "HandleNoteControllerDidInit")]
    [HarmonyPriority(Priority.Low)]
    internal static class TestNotePatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TestNotePatch");

        private static void Postfix(ColorNoteVisuals __instance, NoteControllerBase noteController)
        {
            try
            {
                var gameNote = noteController as GameNoteController ?? __instance?.GetComponentInParent<GameNoteController>();
                TryMarkAndAttach(gameNote, "init");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }

        internal static bool TryMarkAndAttach(GameNoteController gameNote, string trigger)
        {
            if (gameNote == null)
            {
                return false;
            }

            var noteData = gameNote.noteData;
            if (!TestEffectManager.Instance.TryMarkNextEffect(gameNote, noteData, out int denomination))
            {
                return false;
            }

            bool outlineAttached = OutlineEmitterManager.Instance.TryAttachToNote(gameNote);
            LogUtils.Debug(() =>
                "TestNotePatch: Marked note via "
                + trigger
                + " time="
                + noteData.time.ToString("F3")
                + " denomination="
                + denomination
                + " outlineAttached="
                + outlineAttached
                + " cutBurstDeferred=true");
            return true;
        }
    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteDidStartJump")]
    internal static class TestLateStartJumpPatch
    {
        private static void Prefix(NoteController noteController)
        {
            try
            {
                if (!TestEffectManager.Instance.HasPendingEffects)
                {
                    return;
                }

                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                TestNotePatch.TryMarkAndAttach(gameNote, "start-jump");
            }
            catch (Exception ex)
            {
                LogUtils.Warn("TestLateStartJumpPatch: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteWasMissed")]
    internal static class TestMissPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TestMissPatch");

        private static void Prefix(NoteController noteController)
        {
            try
            {
                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                if (!TestEffectManager.Instance.TryRequeueMarkedEffect(gameNote, "Missed", out int denomination))
                {
                    return;
                }

                OutlineEmitterManager.Instance.DetachFromNote(gameNote);
                GlitterLoopEmitterManager.Instance.DetachFromNote(gameNote);
                LogUtils.Debug(() =>
                    "TestMissPatch: Requeued missed test note time="
                    + (gameNote.noteData != null ? gameNote.noteData.time.ToString("F3") : "<unknown>")
                    + " denomination="
                    + denomination);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Prefix");
            }
        }
    }

    [HarmonyPatch(typeof(GameNoteController), "HandleCut")]
    [HarmonyPriority(Priority.Low)]
    internal static class TestCutPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TestCutPatch");

        private static void Postfix(
            GameNoteController __instance,
            Saber saber,
            Vector3 cutPoint,
            Quaternion orientation,
            Vector3 cutDirVec,
            bool allowBadCut)
        {
            try
            {
                var noteData = __instance?.noteData;
                if (noteData == null)
                {
                    return;
                }

                if (!TestEffectManager.Instance.TryConsumeMarkedEffect(__instance, out int denomination, out string requesterName))
                {
                    return;
                }

                OutlineEmitterManager.Instance.DetachFromNote(__instance);
                GlitterLoopEmitterManager.Instance.DetachFromNote(__instance);

                Vector3 returnTarget = BitParticleEmitterPool.ResolveReturnTarget(__instance, cutPoint);
                if (!BitParticleEmitterPool.Instance.Spawn(denomination, cutPoint, returnTarget))
                {
                    Plugin.Log.Warn("TestCutPatch: Failed to spawn bit emitter for denomination=" + denomination);
                }

                BombCutPatch.SpawnFlyingText(requesterName, cutPoint);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }
    }
}