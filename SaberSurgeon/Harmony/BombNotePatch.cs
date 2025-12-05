using HarmonyLib;
using HMUI;
using SaberSurgeon.Gameplay;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;


namespace SaberSurgeon.HarmonyPatches
{

    // Runs after ColorNoteVisuals creates the normal note visuals
    [HarmonyPatch(typeof(ColorNoteVisuals), "HandleNoteControllerDidInit")]
    [HarmonyPriority(Priority.Last)]
    internal static class BombNotePatch
    {
        private static BombNoteController _bombPrefab;

        private static void Postfix(ColorNoteVisuals __instance, NoteControllerBase noteController)
        {
            // Only create bombs while the bomb window is active
            if (!BombManager.IsBombWindowActive)
                return;

            var noteData = noteController?.noteData;
            if (noteData == null || noteData.colorType == ColorType.None)
                return;

            var gameNote = __instance.GetComponentInParent<GameNoteController>();
            if (gameNote == null)
            {
                Plugin.Log.Warn("BombNotePatch: No GameNoteController parent found");
                return;
            }

            Plugin.Log.Info(
                $"BombNotePatch: INIT -> time={noteData.time:F3}, colorType={noteData.colorType}, " +
                $"cutDir={noteData.cutDirection}, obj='{gameNote.name}', layer={gameNote.gameObject.layer}");

            BombManager.Instance.MarkNoteAsBomb(noteData);

            // Cache BombNote prefab
            if (_bombPrefab == null)
            {
                _bombPrefab = Resources.FindObjectsOfTypeAll<BombNoteController>().FirstOrDefault();
                if (_bombPrefab != null)
                    Plugin.Log.Info($"BombNotePatch: Cached BombNoteController prefab '{_bombPrefab.name}'");
                else
                    Plugin.Log.Warn("BombNotePatch: No BombNoteController found – will use sphere fallback");
            }

            // FIX: Get color from ColorNoteVisuals using correct field name with underscore
            Color noteColor = Color.magenta; // fallback
            try
            {
                // Try multiple possible field names
                var colorField = AccessTools.Field(typeof(ColorNoteVisuals), "_noteColor")
                                ?? AccessTools.Field(typeof(ColorNoteVisuals), "noteColor");

                if (colorField != null)
                {
                    noteColor = (Color)colorField.GetValue(__instance);
                    Plugin.Log.Info($"BombNotePatch: Got note color via reflection: {noteColor}");
                }
                else
                {
                    // Fallback: get ColorManager and call ColorForType
                    var cmField = AccessTools.Field(typeof(ColorNoteVisuals), "_colorManager")
                                 ?? AccessTools.Field(typeof(ColorNoteVisuals), "colorManager");
                    var cm = cmField?.GetValue(__instance);
                    if (cm != null)
                    {
                        var colorForType = cm.GetType().GetMethod("ColorForType");
                        if (colorForType != null)
                        {
                            noteColor = (Color)colorForType.Invoke(cm, new object[] { noteData.colorType });
                            Plugin.Log.Info($"BombNotePatch: Got color from ColorManager: {noteColor}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"BombNotePatch: Error getting color: {ex}");
            }

            // Only disable the NoteCube mesh (main note body), keep arrows
            DisableNoteCubeOnly(gameNote);

            // Create bomb visual with color
            AttachBombVisualWithColor(gameNote, noteColor);
        }

        private static void DisableNoteCubeOnly(GameNoteController gameNote)
        {
            // Find and disable only the NoteCube renderer (main cube body)
            var noteCube = gameNote.transform.Find("NoteCube");
            if (noteCube != null)
            {
                var cubeRenderer = noteCube.GetComponent<MeshRenderer>();
                if (cubeRenderer != null)
                {
                    cubeRenderer.enabled = false;
                    Plugin.Log.Info("BombNotePatch: Disabled NoteCube renderer (keeping arrows visible)");
                }

                // Also disable the circle mesh if it exists (for dot notes)
                var circle = noteCube.Find("NoteCircleGlow");
                if (circle != null)
                {
                    var circleRenderer = circle.GetComponent<MeshRenderer>();
                    if (circleRenderer != null) circleRenderer.enabled = false;
                }
            }
        }

        private static void AttachBombVisualWithColor(GameNoteController gameNote, Color noteColor)
        {
            var existing = gameNote.transform.Find("SaberSurgeon_BombVisual");
            if (existing != null)
            {
                Plugin.Log.Debug("BombNotePatch: Bomb visual already exists, skipping create");
                return;
            }

            var root = new GameObject("SaberSurgeon_BombVisual");
            root.transform.SetParent(gameNote.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            int noteLayer = gameNote.gameObject.layer;
            root.layer = noteLayer;

            // FIX: Make sure GameObject is ACTIVE
            root.SetActive(true);

            Plugin.Log.Info($"BombNotePatch: Created bomb root under {gameNote.name}, layer={noteLayer}");

            if (_bombPrefab != null)
            {
                Plugin.Log.Info($"BombNotePatch: Using BombNote prefab '{_bombPrefab.name}' for visual");
                var prefabGO = _bombPrefab.gameObject;
                var instance = UnityEngine.Object.Instantiate(prefabGO, root.transform);
                instance.name = "BombPrefabInstance";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                // FIX: Activate the instance
                instance.SetActive(true);

                // Remove BombNoteController to prevent it from being a real bomb
                foreach (var bomb in instance.GetComponentsInChildren<BombNoteController>(true))
                    UnityEngine.Object.Destroy(bomb);
                foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.Destroy(col);

                SetLayerRecursively(root.transform, noteLayer);

                // FIX: Enable and color all mesh renderers
                var bombRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
                Plugin.Log.Info($"BombNotePatch: Found {bombRenderers.Length} renderers in bomb visual");

                foreach (var mr in bombRenderers)
                {
                    if (mr == null) continue;

                    mr.enabled = true;
                    mr.gameObject.SetActive(true); // Make sure gameobject is active too

                    // FIX: Create new material instance with color
                    if (mr.sharedMaterial != null)
                    {
                        var newMat = new Material(mr.sharedMaterial);
                        newMat.color = noteColor;
                        // Also try setting other possible color properties
                        if (newMat.HasProperty("_Color")) newMat.SetColor("_Color", noteColor);
                        if (newMat.HasProperty("_SimpleColor")) newMat.SetColor("_SimpleColor", noteColor);
                        mr.material = newMat;
                        Plugin.Log.Info($"BombNotePatch: Applied color {noteColor} to renderer '{mr.name}'");
                    }

                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    mr.receiveShadows = true;
                }

                Plugin.Log.Info($"BombNotePatch: Enabled and colored {bombRenderers.Length} bomb MeshRenderers");
            }
            else
            {
                Plugin.Log.Info("BombNotePatch: Using sphere fallback for bomb visual");
                var bombGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bombGo.name = "BombSphere";
                bombGo.transform.SetParent(root.transform, false);
                bombGo.transform.localPosition = Vector3.zero;
                bombGo.transform.localRotation = Quaternion.identity;
                bombGo.transform.localScale = Vector3.one * 0.45f;
                bombGo.SetActive(true);

                var col = bombGo.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);

                var mr = bombGo.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.enabled = true;
                    var mat = new Material(Shader.Find("Standard"));
                    mat.color = noteColor;
                    mr.material = mat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    mr.receiveShadows = true;
                }

                SetLayerRecursively(root.transform, noteLayer);
                Plugin.Log.Info($"BombNotePatch: Sphere bomb visual created and colored");
            }

            //START WATCHDOG
            var bombNode = gameNote.transform.Find("SaberSurgeon_BombVisual");
            if (bombNode != null)
            {
                var watchdog = gameNote.gameObject.AddComponent<RendererWatchdog>();

                // Use the bomb command cooldown as the watchdog duration
                float seconds = SaberSurgeon.Chat.CommandHandler.BombCooldownSeconds;
                if (seconds <= 0f) seconds = 10.0f; // sane fallback

                watchdog.Init(gameNote.transform, seconds, bombNode);
                Plugin.Log.Info($"BombNotePatch: Watchdog started for {seconds:F1}s (ignoring SaberSurgeon_BombVisual)");
            }

            else
            {
                Plugin.Log.Warn("BombNotePatch: bombNode not found after creation, watchdog not started");
            }
        }

        private static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i), layer);
        }
    }
    
   


    [HarmonyPatch(typeof(GameNoteController), "HandleCut")]
    [HarmonyPriority(Priority.High)] // Run before normal processing
    internal static class BombCutPatch
    {
        private static NoteCutCoreEffectsSpawner _effectsSpawner;
        private static CurvedTextMeshPro _flyingTextPrefab;

        private static void Postfix(
            GameNoteController __instance,
            Saber saber,
            Vector3 cutPoint,
            Quaternion orientation,
            Vector3 cutDirVec,
            bool allowBadCut)
        {
            var noteData = __instance.noteData;
            if (noteData == null) return;

            if (!BombManager.Instance.TryConsumeBomb(noteData, out var bomber)) return;

            Plugin.Log.Info($"BombCutPatch: Bomb cut by {bomber}");

            EnsureRefs();

            // Spawn explosion particles
            SpawnBombExplosion(__instance, cutPoint, orientation, saber, cutDirVec);

            // Spawn flying username text
            SpawnFlyingUsername(bomber, cutPoint);

            //stop all bomb watchdogs so pooled notes stop looking like bombs
            BombManager.Instance.StopAllBombWatchdogs();
        }

        private static void EnsureRefs()
        {
            if (_effectsSpawner == null)
            {
                _effectsSpawner = Resources.FindObjectsOfTypeAll<NoteCutCoreEffectsSpawner>().FirstOrDefault();
                if (_effectsSpawner != null)
                    Plugin.Log.Info("BombCutPatch: Cached NoteCutCoreEffectsSpawner");
            }

            if (_flyingTextPrefab == null)
            {
                // Find game's flying score text prefab
                var flyingScores = Resources.FindObjectsOfTypeAll<FlyingScoreEffect>();
                if (flyingScores != null && flyingScores.Length > 0)
                {
                    _flyingTextPrefab = flyingScores[0].GetComponentInChildren<CurvedTextMeshPro>(true);
                    if (_flyingTextPrefab != null)
                        Plugin.Log.Info("BombCutPatch: Cached CurvedTextMeshPro from FlyingScoreEffect");
                }
            }
        }

        private static void SpawnBombExplosion(
            GameNoteController noteController,
            Vector3 cutPoint,
            Quaternion orientation,
            Saber saber,
            Vector3 cutDirVec)
        {
            if (_effectsSpawner == null) return;

            try
            {
                // Access particles via reflection
                var particlesField = AccessTools.Field(typeof(NoteCutCoreEffectsSpawner), "_noteCutParticlesEffect");
                var particles = particlesField?.GetValue(_effectsSpawner) as NoteCutParticlesEffect;

                if (particles == null)
                {
                    Plugin.Log.Warn("BombCutPatch: _noteCutParticlesEffect was null");
                    return;
                }

                // Bright yellow explosion
                Color color = Color.Lerp(Color.yellow, Color.white, 0.5f); // very bright
                color.a = 1.0f;

                Vector3 cutNormal = orientation * Vector3.up;
                Vector3 saberDir = cutDirVec.normalized;
                float saberSpeed = saber.bladeSpeedForLogic;
                Vector3 moveVec = noteController.moveVec;

                float lifetimeMultiplier = Mathf.Clamp(noteController.noteData.timeToNextColorNote + 0.2f, 0.7f, 3f) * 3.0f;

                particles.SpawnParticles(
                    cutPoint,
                    cutNormal,
                    saberDir,
                    saberSpeed,
                    moveVec,
                    (Color32)color,
                    200, // sparkleCount
                    900, // explosionCount
                    lifetimeMultiplier);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"BombCutPatch: Error spawning explosion: {ex}");
            }
        }

        private static void SpawnFlyingUsername(string username, Vector3 cutPoint)
        {
            if (string.IsNullOrEmpty(username)) return;

            try
            {
                // Use game's flying text if available, otherwise create simple text
                if (_flyingTextPrefab != null)
                {
                    SpawnCurvedFlyingText(username, cutPoint);
                }
                else
                {
                    SpawnSimpleFlyingText(username, cutPoint);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"BombCutPatch: Error spawning username text: {ex}");
            }
        }

        private static void SpawnCurvedFlyingText(string username, Vector3 cutPoint)
        {
            var textGo = new GameObject("BombUsername_CurvedText");
            textGo.transform.position = cutPoint + Vector3.up * 0.5f;

            // Copy the curved text component
            var curvedText = textGo.AddComponent<CurvedTextMeshPro>();

            // Get font from prefab
            if (_flyingTextPrefab.font != null)
                curvedText.font = _flyingTextPrefab.font;

            curvedText.text = username;
            curvedText.fontSize = 4f;
            curvedText.alignment = TextAlignmentOptions.Center;
            curvedText.color = Color.yellow;
            curvedText.outlineWidth = 0.2f;
            curvedText.outlineColor = Color.black;

            // Start animation coroutine
            CoroutineHost.Instance.StartCoroutine(AnimateFlyingText(textGo, cutPoint));
        }

        private static void SpawnSimpleFlyingText(string username, Vector3 cutPoint)
        {
            var textGo = new GameObject("BombUsername_Text");
            textGo.transform.position = cutPoint + Vector3.up * 0.5f;

            var tmp = textGo.AddComponent<TextMeshPro>();

            // Try to find game's font
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            var tekoFont = fonts.FirstOrDefault(f => f.name.Contains("Teko"));
            if (tekoFont != null)
                tmp.font = tekoFont;

            tmp.text = username;
            tmp.fontSize = 4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.yellow;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = Color.black;

            // Make text face camera
            var lookAt = textGo.AddComponent<LookAtCamera>();

            // Start animation coroutine
            CoroutineHost.Instance.StartCoroutine(AnimateFlyingText(textGo, cutPoint));
        }

        private static IEnumerator AnimateFlyingText(GameObject textGo, Vector3 startPos)
        {
            float duration = 2.0f;
            float elapsed = 0f;

            // Find note spawn position (forward from player)
            Vector3 targetPos = startPos + Camera.main.transform.forward * 10f + Vector3.up * 2f;

            var tmp = textGo.GetComponent<TextMeshPro>();
            Color startColor = tmp != null ? tmp.color : Color.yellow;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Move forward and up
                textGo.transform.position = Vector3.Lerp(startPos + Vector3.up * 0.5f, targetPos, t);

                // Fade out
                if (tmp != null)
                {
                    Color c = startColor;
                    c.a = Mathf.Lerp(1f, 0f, t);
                    tmp.color = c;
                }

                // Scale up slightly then down
                float scale = Mathf.Sin(t * Mathf.PI) * 0.3f + 1f;
                textGo.transform.localScale = Vector3.one * scale;

                yield return null;
            }

            UnityEngine.Object.Destroy(textGo);
        }
    }
}
