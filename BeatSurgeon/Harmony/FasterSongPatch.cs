using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using BeatSurgeon.Utils;
using UnityEngine;
using Unity.Profiling;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(AudioTimeSyncController))]
    internal static class FasterSongPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("FasterSongPatch");
        private static readonly ProfilerMarker UpdateProfiler = new ProfilerMarker("BeatSurgeon.FasterSongPatch.Update");

        internal static float Multiplier { get; set; } = 1.0f;

        private class ScaleData
        {
            internal bool Initialized;
            internal float BaseScale;
            internal bool WasScaled;
        }

        private static readonly ConditionalWeakTable<AudioTimeSyncController, ScaleData> ScaleDataByController =
            new ConditionalWeakTable<AudioTimeSyncController, ScaleData>();

        private static readonly AccessTools.FieldRef<AudioTimeSyncController, float> TimeScaleRef =
            AccessTools.FieldRefAccess<AudioTimeSyncController, float>("_timeScale");

        private static readonly AccessTools.FieldRef<AudioTimeSyncController, AudioSource> AudioSourceRef =
            AccessTools.FieldRefAccess<AudioTimeSyncController, AudioSource>("_audioSource");

        [HarmonyPrefix]
        [HarmonyPatch("Update")]
        private static void Prefix_Update(AudioTimeSyncController __instance)
        {
            try
            {
                if (__instance == null || !__instance.isActiveAndEnabled)
                {
                    return;
                }

                using (UpdateProfiler.Auto())
                {
                    float multiplier = Multiplier;

                    if (Mathf.Approximately(multiplier, 1.0f) || multiplier <= 0.0f)
                    {
                        if (!ScaleDataByController.TryGetValue(__instance, out ScaleData existingData) || !existingData.WasScaled)
                        {
                            return;
                        }

                        float restoreScale = existingData.Initialized ? existingData.BaseScale : 1.0f;
                        if (!Mathf.Approximately(TimeScaleRef(__instance), restoreScale))
                        {
                            TimeScaleRef(__instance) = restoreScale;
                        }

                        AudioSource restoreSource = AudioSourceRef(__instance);
                        if (restoreSource != null && !Mathf.Approximately(restoreSource.pitch, restoreScale))
                        {
                            restoreSource.pitch = restoreScale;
                        }

                        existingData.WasScaled = false;
                        return;
                    }

                    ScaleData data = ScaleDataByController.GetOrCreateValue(__instance);
                    if (!data.Initialized)
                    {
                        data.BaseScale = __instance.timeScale;
                        data.Initialized = true;
                    }

                    float effectiveScale = data.BaseScale * multiplier;
                    if (!Mathf.Approximately(TimeScaleRef(__instance), effectiveScale))
                    {
                        TimeScaleRef(__instance) = effectiveScale;
                    }

                    AudioSource source = AudioSourceRef(__instance);
                    if (source != null && !Mathf.Approximately(source.pitch, effectiveScale))
                    {
                        source.pitch = effectiveScale;
                    }

                    data.WasScaled = true;
                }
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Prefix_Update");
            }
        }

        internal static void ClearCache()
        {
            Multiplier = 1.0f;
            _log.Debug("Cache cleared and multiplier reset");
        }
    }
}
