using HarmonyLib;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using System.Reflection;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals))]
    internal static class DisappearingArrowsPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("DisappearingArrowsPatch");
        private static readonly FieldInfo NoteControllerField = AccessTools.Field(typeof(ColorNoteVisuals), "_noteController");

        [HarmonyPostfix]
        [HarmonyPatch("HandleNoteControllerDidInit")]
        private static void Postfix(ColorNoteVisuals __instance)
        {
            try
            {
                if (!DisappearingArrowsManager.DisappearingActive)
                    return;

                if (NoteControllerField == null)
                    return;

                var noteController = NoteControllerField.GetValue(__instance) as NoteControllerBase;
                if (noteController == null || noteController.noteData == null)
                    return;

                var gameNote = NoteUtils.FindNoteControllerParent(__instance);
                if (gameNote == null)
                    return;

                var controller = gameNote.gameObject.GetComponent<DisappearingArrowsVisualController>();
                if (controller == null)
                    controller = gameNote.gameObject.AddComponent<DisappearingArrowsVisualController>();

                controller.Initialize(gameNote, noteController.noteData.time);
            }
            catch (System.Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }
    }
}
