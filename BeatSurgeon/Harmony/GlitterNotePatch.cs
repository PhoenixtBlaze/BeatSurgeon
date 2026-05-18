using System;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using HarmonyLib;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals), "HandleNoteControllerDidInit")]
    [HarmonyPriority(Priority.Low)]
    internal static class GlitterNotePatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("GlitterNotePatch");

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
            if (!GlitterManager.Instance.TryMarkNextEffect(gameNote, noteData, out int denomination))
            {
                return false;
            }

            bool outlineAttached = OutlineEmitterManager.Instance.TryAttachToNote(gameNote);
            LogUtils.Debug(() =>
                "GlitterNotePatch: Marked note via "
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
    internal static class GlitterLateStartJumpPatch
    {
        private static void Prefix(NoteController noteController)
        {
            try
            {
                if (!GlitterManager.Instance.HasPendingEffects)
                {
                    return;
                }

                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                GlitterNotePatch.TryMarkAndAttach(gameNote, "start-jump");
            }
            catch (Exception ex)
            {
                LogUtils.Warn("GlitterLateStartJumpPatch: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteWasMissed")]
    internal static class GlitterMissPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("GlitterMissPatch");

        private static void Prefix(NoteController noteController)
        {
            try
            {
                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                if (!GlitterManager.Instance.TryRequeueMarkedEffect(gameNote, "Missed", out int denomination))
                {
                    return;
                }

                OutlineEmitterManager.Instance.DetachFromNote(gameNote);
                GlitterLoopEmitterManager.Instance.DetachFromNote(gameNote);
                LogUtils.Debug(() =>
                    "GlitterMissPatch: Requeued missed glitter note time="
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
}