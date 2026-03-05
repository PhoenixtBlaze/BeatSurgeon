using System;
using System.Reflection;
using HarmonyLib;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using UnityEngine;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals))]
    internal static class RainbowNotePatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("RainbowNotePatch");
        private static readonly Type TargetType = typeof(ColorNoteVisuals);

        private static FieldInfo TryGetField(Type t, params string[] names)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string n in names)
            {
                FieldInfo f = t.GetField(n, flags);
                if (f != null) return f;
            }
            return null;
        }

        private static readonly FieldInfo MpbField = TryGetField(TargetType, "materialPropertyBlockControllers", "_materialPropertyBlockControllers");
        private static readonly FieldInfo DefaultAlphaField = TryGetField(TargetType, "defaultColorAlpha", "_defaultColorAlpha");
        private static readonly FieldInfo ColorIdField = TryGetField(TargetType, "colorId", "_colorId");

        [HarmonyPostfix]
        [HarmonyPatch("HandleNoteControllerDidInit")]
        private static void Postfix(ColorNoteVisuals __instance, NoteControllerBase noteController)
        {
            try
            {
                if (!RainbowManager.RainbowActive && !RainbowManager.NoteColorActive)
                    return;

                if (MpbField == null || DefaultAlphaField == null || ColorIdField == null)
                    return;

                var controllersObj = MpbField.GetValue(__instance) as Array;
                if (controllersObj == null || controllersObj.Length == 0)
                    return;

                float defaultAlpha = (float)DefaultAlphaField.GetValue(__instance);
                int colorId = ColorIdField.IsStatic ? (int)ColorIdField.GetValue(null) : (int)ColorIdField.GetValue(__instance);
                var noteData = noteController?.noteData;
                if (noteData == null)
                    return;

                MaterialPropertyBlockController[] controllers = controllersObj as MaterialPropertyBlockController[];
                if (controllers == null)
                {
                    controllers = new MaterialPropertyBlockController[controllersObj.Length];
                    for (int i = 0; i < controllersObj.Length; i++)
                        controllers[i] = controllersObj.GetValue(i) as MaterialPropertyBlockController;
                }

                if (RainbowManager.RainbowActive)
                {
                    RainbowManager.Instance.RegisterNote(__instance, noteData.colorType, controllers, colorId, defaultAlpha);
                    return;
                }

                if (!RainbowManager.NoteColorActive)
                    return;

                Color finalColor = noteData.colorType == ColorType.ColorA ? RainbowManager.LeftColor : RainbowManager.RightColor;
                foreach (MaterialPropertyBlockController controller in controllers)
                {
                    if (controller == null) continue;
                    var mpb = controller.materialPropertyBlock;
                    if (mpb == null) continue;
                    mpb.SetColor(colorId, finalColor.ColorWithAlpha(defaultAlpha));
                    controller.ApplyChanges();
                }
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnDestroy")]
        private static void OnDestroyPostfix(ColorNoteVisuals __instance)
        {
            try
            {
                if (RainbowManager.RainbowActive)
                    RainbowManager.Instance.UnregisterNote(__instance);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnDestroyPostfix");
            }
        }
    }
}
