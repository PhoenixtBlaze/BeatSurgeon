using System;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using HarmonyLib;
using UnityEngine;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals), "HandleNoteControllerDidInit")]
    [HarmonyPriority(Priority.Low)]
    internal static class RaidFountainNotePatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("RaidFountainNotePatch");

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

            if (RaidFountainNoteManager.Instance.IsNoteMarked(gameNote))
            {
                return true;
            }

            if (!ExclusiveNoteEffectArbiter.TryReserve(gameNote, ExclusiveNoteEffectKind.Raid))
            {
                return false;
            }

            NoteData noteData = gameNote.noteData;
            if (!RaidFountainNoteManager.Instance.TryMarkAndAttach(gameNote))
            {
                ExclusiveNoteEffectArbiter.Release(gameNote);
                return false;
            }

            LogUtils.Debug(() =>
                "RaidFountainNotePatch: Attached raid fountain via "
                + trigger
                + " time="
                + noteData.time.ToString("F3"));
            return true;
        }
    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteDidStartJump")]
    internal static class RaidFountainLateStartJumpPatch
    {
        private static void Prefix(NoteController noteController)
        {
            try
            {
                if (!RaidFountainNoteManager.Instance.HasPendingNotes)
                {
                    return;
                }

                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                RaidFountainNotePatch.TryMarkAndAttach(gameNote, "start-jump");
            }
            catch (Exception ex)
            {
                LogUtils.Warn("RaidFountainLateStartJumpPatch: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteWasMissed")]
    internal static class RaidFountainMissPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("RaidFountainMissPatch");

        private static void Prefix(NoteController noteController)
        {
            try
            {
                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                RaidFountainNoteManager.Instance.TryRequeueMarkedNote(gameNote, "Missed");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Prefix");
            }
        }
    }

    [HarmonyPatch(typeof(GameNoteController), "HandleCut")]
    [HarmonyPriority(Priority.Low)]
    internal static class RaidFountainCutPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("RaidFountainCutPatch");

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
                RaidFountainNoteManager.Instance.TryConsumeMarkedNote(
                    __instance,
                    out _,
                    cutPoint,
                    explode: true);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }
    }
}
