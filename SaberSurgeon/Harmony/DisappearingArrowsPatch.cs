using System.Reflection;
using HarmonyLib;
using UnityEngine;
using BeatSurgeon.Gameplay;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals))]
    internal static class DisappearingArrowsPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("HandleNoteControllerDidInit")]
        private static void Postfix(ColorNoteVisuals __instance)
        {
            // Only affect notes while our DA effect is active
            if (!DisappearingArrowsManager.DisappearingActive)
                return;

            var type = typeof(ColorNoteVisuals);

            // We need the underlying NoteController to get noteData.time
            var noteControllerField = AccessTools.Field(type, "_noteController");
            if (noteControllerField == null)
            {
                Plugin.Log.Warn("DisappearingArrowsPatch: Failed to reflect _noteController field.");
                return;
            }

            var noteController = noteControllerField.GetValue(__instance) as NoteControllerBase;
            if (noteController == null || noteController.noteData == null)
                return;

            var noteData = noteController.noteData;

            // We now affect both directional and dot notes, so no cutDirection/Any check

            var gameNote = NoteUtils.FindNoteControllerParent(__instance);
            if (gameNote == null)
            {
                Plugin.Log.Warn("DisappearingArrowsPatch: No note controller parent found");
                return;
            }

            var controller = gameNote.gameObject.GetComponent<DisappearingArrowsVisualController>();
            if (controller == null)
                controller = gameNote.gameObject.AddComponent<DisappearingArrowsVisualController>();

            controller.Initialize(gameNote, noteData.time);
        }
    }
}
