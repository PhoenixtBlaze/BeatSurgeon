using HarmonyLib;
using UnityEngine;
using SaberSurgeon.Gameplay;

namespace SaberSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals))]
    internal static class GhostNotesPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("HandleNoteControllerDidInit")]
        private static void Postfix(ColorNoteVisuals __instance, NoteControllerBase noteController)
        {
            if (!Gameplay.GhostNotesManager.GhostActive)
                return;

            var noteData = noteController.noteData;
            if (noteData == null || noteData.colorType == ColorType.None)
                return;

            // Let the very first note stay totally normal
            if (!Gameplay.GhostNotesManager.FirstNoteShown)
            {
                Gameplay.GhostNotesManager.FirstNoteShown = true;
                return;
            }

            var gameNote = __instance.GetComponentInParent<GameNoteController>();
            if (gameNote == null)
            {
                Plugin.Log.Warn("GhostNotesPatch: No GameNoteController parent found");
                return;
            }

            var controller = gameNote.gameObject.GetComponent<GhostVisualController>();
            if (controller == null)
            {
                controller = gameNote.gameObject.AddComponent<GhostVisualController>();
                Plugin.Log.Info($"GhostNotesPatch: Added GhostVisualController to note at t={noteData.time:F3}");
            }

            controller.Initialize(gameNote, noteData.time);
        }
    }
}