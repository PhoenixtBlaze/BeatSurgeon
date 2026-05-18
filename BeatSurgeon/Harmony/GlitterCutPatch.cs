using System;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using HarmonyLib;
using UnityEngine;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(GameNoteController), "HandleCut")]
    [HarmonyPriority(Priority.Low)]
    internal static class GlitterCutPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("GlitterCutPatch");

        private static void Postfix(
            GameNoteController __instance,
            Saber saber,
            Vector3 cutPoint,
            Quaternion orientation,
            Vector3 cutDirVec,
            bool allowBadCut)
        {
            try
            {
                if (!GlitterManager.Instance.TryConsumeMarkedEffect(__instance, out int denomination, out string requesterName))
                {
                    return;
                }

                OutlineEmitterManager.Instance.DetachFromNote(__instance);
                GlitterLoopEmitterManager.Instance.DetachFromNote(__instance);

                if (!GlitterExplosionPool.Instance.Spawn(denomination, cutPoint))
                {
                    Plugin.Log.Warn("GlitterCutPatch: Failed to spawn glitter emitter for denomination=" + denomination);
                }

                Vector3 returnTarget = BitParticleEmitterPool.ResolveReturnTarget(__instance, cutPoint);
                if (!BitParticleEmitterPool.Instance.Spawn(denomination, cutPoint, returnTarget))
                {
                    Plugin.Log.Warn("GlitterCutPatch: Failed to spawn bit particle emitter for denomination=" + denomination);
                }

                BombCutPatch.SpawnFlyingText(requesterName, cutPoint);
                SubscriberTrailCubeManager.Instance.TryConsumeMarkedNote(__instance, out _);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }
    }
}