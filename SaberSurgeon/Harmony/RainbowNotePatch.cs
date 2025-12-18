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

        /// <summary>
        /// Try to find a field by any of the provided names using reflection directly
        /// (bypasses Harmony warnings for fields that don't exist).
        /// </summary>
        private static FieldInfo TryGetField(Type t, params string[] names)
        {
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var n in names)
            {
                var f = t.GetField(n, flags);
                if (f != null)
                    return f;
            }
            return null;
        }

        // Initialize fields via custom TryGetField to avoid Harmony warnings
        private static readonly FieldInfo MpbField =
            TryGetField(TargetType, "materialPropertyBlockControllers", "_materialPropertyBlockControllers");

        private static readonly FieldInfo DefaultAlphaField =
            TryGetField(TargetType, "defaultColorAlpha", "_defaultColorAlpha");

        private static readonly FieldInfo ColorIdField =
            TryGetField(TargetType, "colorId", "_colorId");

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

            // Handle both static and instance colorId field (version compatibility)
            int colorId;
            if (ColorIdField.IsStatic)
                colorId = (int)ColorIdField.GetValue(null);
            else
                colorId = (int)ColorIdField.GetValue(__instance);

            Color finalColor;

            if (Gameplay.RainbowManager.RainbowActive)
            {
                finalColor = UnityEngine.Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.8f, 1f);
            }
            else
            {
                var data = noteController?.noteData;
                if (data == null)
                    return;

                // ColorA = left (saberA), ColorB = right (saberB)
                finalColor = (data.colorType == ColorType.ColorA)
                    ? Gameplay.RainbowManager.LeftColor
                    : Gameplay.RainbowManager.RightColor;
            }

            foreach (var ctrlObj in controllersObj)
            {
                if (ctrlObj == null)
                    continue;

                var ctrlType = ctrlObj.GetType();

                var mpbProp = ctrlType.GetProperty("materialPropertyBlock",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                var applyMethod = ctrlType.GetMethod("ApplyChanges",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (mpbProp == null || applyMethod == null)
                    continue;

                var mpb = mpbProp.GetValue(ctrlObj) as MaterialPropertyBlock;
                if (mpb == null)
                    continue;

                mpb.SetColor(colorId, finalColor.ColorWithAlpha(defaultAlpha));
                applyMethod.Invoke(ctrlObj, null);
            }
        }
    }
}
