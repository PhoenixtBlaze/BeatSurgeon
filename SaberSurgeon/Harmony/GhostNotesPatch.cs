using HarmonyLib;
using SaberSurgeon.Gameplay;
using System.Reflection;
using UnityEngine;

namespace SaberSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals))]
    internal static class GhostNotesPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("HandleNoteControllerDidInit")]
        private static void Postfix(ColorNoteVisuals __instance)
        {
            bool rainbow = RainbowManager.RainbowActive;
            bool noteColorOverride = RainbowManager.NoteColorActive;

            if (!rainbow && !noteColorOverride)
                return;

            var type = typeof(ColorNoteVisuals);

            // Private instance fields on ColorNoteVisuals
            var mpbField = AccessTools.Field(type, "_materialPropertyBlockControllers");
            var defaultAlphaField = AccessTools.Field(type, "_defaultColorAlpha");
            // Static field
            var colorIdField = AccessTools.Field(type, "_colorId");
            // For left/right detection
            var noteControllerField = AccessTools.Field(type, "_noteController");

            if (mpbField == null || defaultAlphaField == null || colorIdField == null)
            {
                Plugin.Log.Warn("RainbowNotePatch: Failed to reflect ColorNoteVisuals fields.");
                return;
            }

            var controllersObj = mpbField.GetValue(__instance) as System.Array;
            if (controllersObj == null || controllersObj.Length == 0)
                return;

            float defaultAlpha = (float)defaultAlphaField.GetValue(__instance);
            int colorId = (int)colorIdField.GetValue(null); // static field

            // Choose base color for this note
            Color baseColor;

            if (noteColorOverride)
            {
                // Decide left/right from NoteController.noteData.colorType
                Color left = RainbowManager.LeftColor;
                Color right = RainbowManager.RightColor;

                Color chosen = left;

                if (noteControllerField != null)
                {
                    var noteController = noteControllerField.GetValue(__instance) as NoteController;
                    if (noteController != null && noteController.noteData != null)
                    {
                        var ct = noteController.noteData.colorType;
                        if (ct == ColorType.ColorA)
                            chosen = left;
                        else if (ct == ColorType.ColorB)
                            chosen = right;
                    }
                }

                baseColor = chosen;
            }
            else
            {
                // Random bright color for pure rainbow mode
                baseColor = UnityEngine.Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.8f, 1f);
            }

            foreach (var ctrlObj in controllersObj)
            {
                if (ctrlObj == null)
                    continue;

                var ctrlType = ctrlObj.GetType();

                var mpbProp = ctrlType.GetProperty(
                    "materialPropertyBlock",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                var applyMethod = ctrlType.GetMethod(
                    "ApplyChanges",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (mpbProp == null || applyMethod == null)
                    continue;

                var mpb = mpbProp.GetValue(ctrlObj) as MaterialPropertyBlock;
                if (mpb == null)
                    continue;

                mpb.SetColor(colorId, baseColor.ColorWithAlpha(defaultAlpha));
                applyMethod.Invoke(ctrlObj, null);
            }
        }

    }
}
