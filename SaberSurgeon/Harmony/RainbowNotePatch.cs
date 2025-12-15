using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SaberSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals))]
    internal static class RainbowNotePatch
    {
        private static readonly Type TargetType = typeof(ColorNoteVisuals);

        private static readonly FieldInfo MpbField =
            AccessTools.Field(TargetType, "materialPropertyBlockControllers")
            ?? AccessTools.Field(TargetType, "_materialPropertyBlockControllers");

        private static readonly FieldInfo DefaultAlphaField =
            AccessTools.Field(TargetType, "defaultColorAlpha")
            ?? AccessTools.Field(TargetType, "_defaultColorAlpha");

        private static readonly FieldInfo ColorIdField =
            AccessTools.Field(TargetType, "colorId")
            ?? AccessTools.Field(TargetType, "_colorId");

        [HarmonyPostfix]
        [HarmonyPatch("HandleNoteControllerDidInit")]
        private static void Postfix(ColorNoteVisuals __instance, NoteControllerBase noteController)
        {
            // Apply when either mode is active
            if (!Gameplay.RainbowManager.RainbowActive && !Gameplay.RainbowManager.NoteColorActive)
                return;

            if (MpbField == null || DefaultAlphaField == null || ColorIdField == null)
            {
                Plugin.Log.Warn("RainbowNotePatch: Failed to find ColorNoteVisuals fields (mpb/alpha/colorId).");
                return;
            }

            var controllersObj = MpbField.GetValue(__instance) as Array;
            if (controllersObj == null || controllersObj.Length == 0)
                return;

            float defaultAlpha = (float)DefaultAlphaField.GetValue(__instance);
            int colorId = (int)ColorIdField.GetValue(null); // static

            Color finalColor;

            if (Gameplay.RainbowManager.RainbowActive)
            {
                finalColor = UnityEngine.Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.8f, 1f);
            }
            else
            {
                var data = noteController?.noteData;
                if (data == null) return;

                // ColorA = left (saberA), ColorB = right (saberB)
                finalColor = (data.colorType == ColorType.ColorA)
                    ? Gameplay.RainbowManager.LeftColor
                    : Gameplay.RainbowManager.RightColor;
            }

            foreach (var ctrlObj in controllersObj)
            {
                if (ctrlObj == null) continue;

                var ctrlType = ctrlObj.GetType();
                var mpbProp = ctrlType.GetProperty("materialPropertyBlock",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var applyMethod = ctrlType.GetMethod("ApplyChanges",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (mpbProp == null || applyMethod == null) continue;

                var mpb = mpbProp.GetValue(ctrlObj) as MaterialPropertyBlock;
                if (mpb == null) continue;

                mpb.SetColor(colorId, finalColor.ColorWithAlpha(defaultAlpha));
                applyMethod.Invoke(ctrlObj, null);
            }
        }
    }
}
