using System;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using HarmonyLib;
using UnityEngine;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals), "HandleNoteControllerDidInit")]
    [HarmonyPriority(Priority.Low)]
    internal static class SubscriberTrailCubeNotePatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SubscriberTrailCubeNotePatch");

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

            NoteData noteData = gameNote.noteData;
            if (GlitterManager.Instance.CanMarkNextEffect(gameNote, noteData))
            {
                return false;
            }

            if (!SubscriberTrailCubeManager.Instance.TryMarkAndAttach(gameNote))
            {
                return false;
            }

            LogUtils.Debug(() =>
                "SubscriberTrailCubeNotePatch: Attached subscriber TrailCube via "
                + trigger
                + " time="
                + noteData.time.ToString("F3"));
            return true;
        }
    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteDidStartJump")]
    internal static class SubscriberTrailCubeLateStartJumpPatch
    {
        private static void Prefix(NoteController noteController)
        {
            try
            {
                if (!SubscriberTrailCubeManager.Instance.HasPendingNotes)
                {
                    return;
                }

                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                SubscriberTrailCubeNotePatch.TryMarkAndAttach(gameNote, "start-jump");
            }
            catch (Exception ex)
            {
                LogUtils.Warn("SubscriberTrailCubeLateStartJumpPatch: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteWasMissed")]
    internal static class SubscriberTrailCubeMissPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SubscriberTrailCubeMissPatch");

        private static void Prefix(NoteController noteController)
        {
            try
            {
                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                SubscriberTrailCubeManager.Instance.TryRequeueMarkedNote(gameNote, "Missed");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Prefix");
            }
        }
    }

    [HarmonyPatch(typeof(GameNoteController), "HandleCut")]
    [HarmonyPriority(Priority.Low)]
    internal static class SubscriberTrailCubeCutPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SubscriberTrailCubeCutPatch");

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
                if (!SubscriberTrailCubeManager.Instance.TryConsumeMarkedNote(__instance, out string requesterName))
                {
                    return;
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