using HarmonyLib;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals))]
    internal static class GhostNotesPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("GhostNotesPatch");

        [HarmonyPostfix]
        [HarmonyPatch("HandleNoteControllerDidInit")]
        private static void Postfix(ColorNoteVisuals __instance, NoteControllerBase noteController)
        {
            try
            {
                if (!GhostNotesManager.GhostActive)
                    return;

                var noteData = noteController?.noteData;
                if (noteData == null || noteData.colorType == ColorType.None)
                    return;

                if (!GhostNotesManager.FirstNoteShown)
                {
                    GhostNotesManager.FirstNoteShown = true;
                    return;
                }

                var gameNote = NoteUtils.FindNoteControllerParent(__instance);
                if (gameNote == null)
                    return;

                var controller = gameNote.gameObject.GetComponent<GhostVisualController>();
                if (controller == null)
                    controller = gameNote.gameObject.AddComponent<GhostVisualController>();

                controller.Initialize(gameNote, noteData.time);
            }
            catch (System.Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }
    }
}
