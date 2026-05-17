using System;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using HarmonyLib;
using UnityEngine;

namespace BeatSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(ColorNoteVisuals), "HandleNoteControllerDidInit")]
    [HarmonyPriority(Priority.Low)]
    internal static class TestNotePatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TestNotePatch");

        private static void Postfix(ColorNoteVisuals __instance, NoteControllerBase noteController)
        {
            try
            {
                var gameNote = noteController as GameNoteController ?? __instance?.GetComponentInParent<GameNoteController>();
                TryMarkAndAttach(gameNote, "init");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }

        internal static bool TryMarkAndAttach(GameNoteController gameNote, string trigger)
        {
            if (gameNote == null)
            {
                return false;
            }

            var noteData = gameNote.noteData;
            bool attachedAny = false;

            if (TestEffectManager.Instance.TryMarkNextEffect(gameNote, noteData, out int denomination))
            {
                bool outlineAttached = OutlineEmitterManager.Instance.TryAttachToNote(gameNote);
                LogUtils.Debug(() =>
                    "TestNotePatch: Marked note via "
                    + trigger
                    + " time="
                    + noteData.time.ToString("F3")
                    + " denomination="
                    + denomination
                    + " outlineAttached="
                    + outlineAttached
                    + " cutBurstDeferred=true");
                return true;
            }

            if (SubscriberTrailCubeManager.Instance.TryMarkAndAttach(gameNote))
            {
                LogUtils.Debug(() =>
                    "TestNotePatch: Attached subscriber TrailCube via "
                    + trigger
                    + " time="
                    + noteData.time.ToString("F3"));
                attachedAny = true;
            }

            return attachedAny;
        }
    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteDidStartJump")]
    internal static class TestLateStartJumpPatch
    {
        private static void Prefix(NoteController noteController)
        {
            try
            {
                if (!TestEffectManager.Instance.HasPendingEffects && !SubscriberTrailCubeManager.Instance.HasPendingNotes)
                {
                    return;
                }

                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                TestNotePatch.TryMarkAndAttach(gameNote, "start-jump");
            }
            catch (Exception ex)
            {
                LogUtils.Warn("TestLateStartJumpPatch: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteWasMissed")]
    internal static class TestMissPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TestMissPatch");

        private static void Prefix(NoteController noteController)
        {
            try
            {
                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                bool requeuedAny = false;

                if (TestEffectManager.Instance.TryRequeueMarkedEffect(gameNote, "Missed", out int denomination))
                {
                    OutlineEmitterManager.Instance.DetachFromNote(gameNote);
                    GlitterLoopEmitterManager.Instance.DetachFromNote(gameNote);
                    LogUtils.Debug(() =>
                        "TestMissPatch: Requeued missed test note time="
                        + (gameNote.noteData != null ? gameNote.noteData.time.ToString("F3") : "<unknown>")
                        + " denomination="
                        + denomination);
                    requeuedAny = true;
                }

                if (SubscriberTrailCubeManager.Instance.TryRequeueMarkedNote(gameNote, "Missed"))
                {
                    requeuedAny = true;
                }

                if (!requeuedAny)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Prefix");
            }
        }
    }

    [HarmonyPatch(typeof(GameNoteController), "HandleCut")]
    [HarmonyPriority(Priority.Low)]
    internal static class TestCutPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TestCutPatch");

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
                var noteData = __instance?.noteData;
                if (noteData == null)
                {
                    return;
                }

                bool consumedAny = false;

                if (TestEffectManager.Instance.TryConsumeMarkedEffect(__instance, out int denomination, out string requesterName))
                {
                    OutlineEmitterManager.Instance.DetachFromNote(__instance);
                    GlitterLoopEmitterManager.Instance.DetachFromNote(__instance);

                    if (!GlitterExplosionPool.Instance.Spawn(denomination, cutPoint))
                    {
                        Plugin.Log.Warn("TestCutPatch: Failed to spawn glitter emitter for denomination=" + denomination);
                    }

                    Vector3 returnTarget = BitParticleEmitterPool.ResolveReturnTarget(__instance, cutPoint);
                    if (!BitParticleEmitterPool.Instance.Spawn(denomination, cutPoint, returnTarget))
                    {
                        Plugin.Log.Warn("TestCutPatch: Failed to spawn bit particle emitter for denomination=" + denomination);
                    }

                    BombCutPatch.SpawnFlyingText(requesterName, cutPoint);
                    SubscriberTrailCubeManager.Instance.TryConsumeMarkedNote(__instance, out _);
                    return;
                }

                if (SubscriberTrailCubeManager.Instance.TryConsumeMarkedNote(__instance, out string subscriberRequesterName))
                {
                    BombCutPatch.SpawnFlyingText(subscriberRequesterName, cutPoint);
                    consumedAny = true;
                }

                if (!consumedAny)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }
    }
}