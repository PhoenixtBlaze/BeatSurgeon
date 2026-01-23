using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals))]
    internal static class RainbowNotePatch
    {
        private static readonly Type TargetType = typeof(ColorNoteVisuals);

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
            // Only apply when rainbow or notecolor mode is active
            if (!Gameplay.RainbowManager.RainbowActive && !Gameplay.RainbowManager.NoteColorActive)
                return;

            if (MpbField == null || DefaultAlphaField == null || ColorIdField == null)
            {
                Plugin.Log.Warn("RainbowNotePatch: Failed to find ColorNoteVisuals fields.");
                return;
            }

            var controllersObj = MpbField.GetValue(__instance) as Array;
            if (controllersObj == null || controllersObj.Length == 0)
                return;

            float defaultAlpha = (float)DefaultAlphaField.GetValue(__instance);

            // Get colorId (handle both static and instance)
            int colorId;
            if (ColorIdField.IsStatic)
                colorId = (int)ColorIdField.GetValue(null);
            else
                colorId = (int)ColorIdField.GetValue(__instance);

            var noteData = noteController?.noteData;
            if (noteData == null)
                return;

            // Convert controllers array to MaterialPropertyBlockController[]
            var controllers = new MaterialPropertyBlockController[controllersObj.Length];
            for (int i = 0; i < controllersObj.Length; i++)
            {
                controllers[i] = controllersObj.GetValue(i) as MaterialPropertyBlockController;
            }

            if (Gameplay.RainbowManager.RainbowActive)
            {
                // Register note for continuous rainbow color updates
                Gameplay.RainbowManager.Instance.RegisterNote(
                    __instance,
                    noteData.colorType,
                    controllers,
                    colorId,
                    defaultAlpha
                );
            }
            else if (Gameplay.RainbowManager.NoteColorActive)
            {
                // Static color mode - set once
                Color finalColor = (noteData.colorType == ColorType.ColorA)
                    ? Gameplay.RainbowManager.LeftColor
                    : Gameplay.RainbowManager.RightColor;

                foreach (var controller in controllers)
                {
                    if (controller == null)
                        continue;

                    try
                    {
                        var mpb = controller.materialPropertyBlock;
                        if (mpb != null)
                        {
                            mpb.SetColor(colorId, finalColor.ColorWithAlpha(defaultAlpha));
                            controller.ApplyChanges();
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"RainbowNotePatch: Error setting static color: {ex.Message}");
                    }
                }
            }
        }

        // NEW: Unregister notes when they're destroyed
        [HarmonyPostfix]
        [HarmonyPatch("OnDestroy")]
        private static void OnDestroyPostfix(ColorNoteVisuals __instance)
        {
            if (Gameplay.RainbowManager.RainbowActive)
            {
                Gameplay.RainbowManager.Instance.UnregisterNote(__instance);
            }
        }
    }
}
