using HarmonyLib;
using HMUI;
using BeatSurgeon;
using BeatSurgeon.Gameplay;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace BeatSurgeon.HarmonyPatches
{
    // Runs after ColorNoteVisuals creates the normal note visuals
    [HarmonyPatch(typeof(ColorNoteVisuals), "HandleNoteControllerDidInit")]
    [HarmonyPriority(Priority.Last)]
    internal static class BombNotePatch
    {
        private static BombNoteController _bombPrefab;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // MPB coloring (NO per-renderer material allocations)
        private static readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SimpleColorId = Shader.PropertyToID("_SimpleColor");

        // Reflection caches (avoid AccessTools.Field per note init)
        private static FieldInfo _noteColorField;
        private static FieldInfo _colorManagerField;
        private static MethodInfo _colorForTypeMethod;

        // Sphere fallback: keep a single shared material, use MPB for color
        private static Material _sphereSharedMaterial;

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
                LogUtils.Warn("BombNotePatch: No GameNoteController parent found");
                return;
            }

            LogUtils.Debug(
                () => $"BombNotePatch: INIT -> time={noteData.time:F3}, colorType={noteData.colorType}, " +
                $"cutDir={noteData.cutDirection}, obj='{gameNote.name}', layer={gameNote.gameObject.layer}"
            );

            
            CacheBombPrefabIfNeeded();
            

            bool isBomb = BombManager.Instance.MarkNoteAsBomb(noteData);
            if (!isBomb)
                return;

            Color noteColor = TryGetNoteColor(__instance, noteData.colorType);

            CacheAndDisableNoteCube(gameNote, out var cubeMr, out var circleMr, out var cubeWasEnabled, out var circleWasEnabled);


            // Rent pooled bomb visual and cache the exact instance
            int noteLayer = gameNote.gameObject.layer;
            GameObject prefabGo = _bombPrefab != null ? _bombPrefab.gameObject : null;
            var visualInst = BombVisualPool.Instance.Rent(gameNote.transform, noteLayer, noteColor, prefabGo);

            try
            {
                var watchdog = gameNote.gameObject.AddComponent<RendererWatchdog>();
                watchdog.Init(gameNote.transform, 1.0f, visualInst.transform);
            }
            catch (Exception ex)
            {
                LogUtils.Warn("BombNotePatch: Failed to start watchdog: " + ex.Message);
            }


            // Give BombManager everything it needs to clear without Find()
            BombManager.Instance.RegisterBombVisual(
                gameNote,
                visualInst,
                cubeMr,
                circleMr,
                cubeWasEnabled,
                circleWasEnabled
            );


        }

        private static void CacheBombPrefabIfNeeded()
        {
            if (_bombPrefab != null)
                return;

            _bombPrefab = Resources.FindObjectsOfTypeAll<BombNoteController>().FirstOrDefault();
            if (_bombPrefab != null)
                LogUtils.Debug(() => $"BombNotePatch: Cached BombNoteController prefab '{_bombPrefab.name}'");
            else
                LogUtils.Warn("BombNotePatch: No BombNoteController found – will use sphere fallback");
        }

        private static Color TryGetNoteColor(ColorNoteVisuals visuals, ColorType colorType)
        {
            Color noteColor = Color.magenta; // fallback

            try
            {
                if (_noteColorField == null)
                {
                    _noteColorField =
                        AccessTools.Field(typeof(ColorNoteVisuals), "_noteColor") ??
                        AccessTools.Field(typeof(ColorNoteVisuals), "noteColor");
                }

                if (_noteColorField != null)
                {
                    noteColor = (Color)_noteColorField.GetValue(visuals);
                    LogUtils.Debug(() => $"BombNotePatch: Got note color via reflection: {noteColor}");
                    return noteColor;
                }

                // Fallback: ColorManager.ColorForType(colorType)
                if (_colorManagerField == null)
                {
                    _colorManagerField =
                        AccessTools.Field(typeof(ColorNoteVisuals), "_colorManager") ??
                        AccessTools.Field(typeof(ColorNoteVisuals), "colorManager");
                }

                var cm = _colorManagerField?.GetValue(visuals);
                if (cm != null)
                {
                    if (_colorForTypeMethod == null)
                        _colorForTypeMethod = AccessTools.Method(cm.GetType(), "ColorForType");

                    if (_colorForTypeMethod != null)
                    {
                        noteColor = (Color)_colorForTypeMethod.Invoke(cm, new object[] { colorType });
                        LogUtils.Debug(() => $"BombNotePatch: Got color from ColorManager: {noteColor}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtils.Error($"BombNotePatch: Error getting color: {ex}");
            }

            return noteColor;
        }

        private static void CacheAndDisableNoteCube(
            GameNoteController gameNote,
            out MeshRenderer cubeMr,
            out MeshRenderer circleMr,
            out bool cubeWasEnabled,
            out bool circleWasEnabled)
        {
            cubeMr = null;
            circleMr = null;
            cubeWasEnabled = false;
            circleWasEnabled = false;

            var noteCube = gameNote.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "NoteCube");

            if (noteCube == null)
            {
                LogUtils.Warn($"BombNotePatch: NoteCube not found under '{gameNote.name}'");
                return;
            }

            cubeMr = noteCube.GetComponent<MeshRenderer>()
                    ?? noteCube.GetComponentInChildren<MeshRenderer>(true);

            if (cubeMr != null)
            {
                cubeWasEnabled = cubeMr.enabled;
                cubeMr.enabled = false;
            }

            var circleT = noteCube.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "NoteCircleGlow");

            circleMr = circleT != null
                ? (circleT.GetComponent<MeshRenderer>() ?? circleT.GetComponentInChildren<MeshRenderer>(true))
                : null;

            if (circleMr != null)
            {
                circleWasEnabled = circleMr.enabled;
                circleMr.enabled = false;
            }
        }


        private static void DisableNoteCubeOnly(GameNoteController gameNote)
            {
                // Always log entry so the logs prove this method is actually running
                LogUtils.Debug(() => $"BombNotePatch: DisableNoteCubeOnly called for '{gameNote.name}'");

                // Find NoteCube at ANY depth (Transform.Find("NoteCube") only checks direct children)
                var noteCube = gameNote.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "NoteCube");

                if (noteCube == null)
                {
                    LogUtils.Warn($"BombNotePatch: NoteCube not found under '{gameNote.name}'");
                    return;
                }

                // Disable the main cube body renderer (either on NoteCube or inside it)
                var cubeRenderer =
                    noteCube.GetComponent<MeshRenderer>() ??
                    noteCube.GetComponentInChildren<MeshRenderer>(true);

                if (cubeRenderer != null)
                {
                    cubeRenderer.enabled = false;
                    LogUtils.Debug(() => "BombNotePatch: Disabled NoteCube renderer (keeping arrows visible)");
                }
                else
                {
                    LogUtils.Warn("BombNotePatch: NoteCube found, but no MeshRenderer found on it or its children");
                }

                // Disable dot-circle if present (same “any depth” approach)
                var circle = noteCube.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "NoteCircleGlow");

                var circleRenderer =
                    circle != null
                        ? (circle.GetComponent<MeshRenderer>() ?? circle.GetComponentInChildren<MeshRenderer>(true))
                        : null;

                if (circleRenderer != null)
                    circleRenderer.enabled = false;
        }


        private static string GetPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }


        private static void AttachBombVisualWithColor(GameNoteController gameNote, Color noteColor)
        {
            int noteLayer = gameNote.gameObject.layer;
            GameObject prefabGo = _bombPrefab != null ? _bombPrefab.gameObject : null;

            BombVisualPool.Instance.Rent(gameNote.transform, noteLayer, noteColor, prefabGo);
        }


        private static void ApplyRendererColor(Renderer r, Color noteColor)
        {
            if (r == null) return;

            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) return;

            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) continue;

                _mpb.Clear();
                bool any = false;

                if (mat.HasProperty(ColorId)) { _mpb.SetColor(ColorId, noteColor); any = true; }
                if (mat.HasProperty(SimpleColorId)) { _mpb.SetColor(SimpleColorId, noteColor); any = true; }
                if (mat.HasProperty(BaseColorId)) { _mpb.SetColor(BaseColorId, noteColor); any = true; }
                if (mat.HasProperty(TintColorId)) { _mpb.SetColor(TintColorId, noteColor); any = true; }
                if (mat.HasProperty(EmissionColorId)) { _mpb.SetColor(EmissionColorId, noteColor); any = true; }

                if (any) r.SetPropertyBlock(_mpb, i);
                else LogUtils.Debug(() => $"BombNotePatch: No known color property on shader '{mat.shader?.name}' (matIndex={i})");
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
    [HarmonyPriority(Priority.High)]
    internal static class BombCutPatch
    {
        private static NoteCutCoreEffectsSpawner _effectsSpawner;
        private static CurvedTextMeshPro _flyingTextPrefab;

        // Caches to avoid repeated Resources.FindObjectsOfTypeAll
        private static Shader _tmpDistanceFieldShader;
        private static TMP_FontAsset _tekoFontCached;

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

            LogUtils.Info($"BombCutPatch: Bomb cut by {bomber}");

            EnsureRefs();

            Color fireworkColor = Plugin.Settings?.BombGradientStart ?? Color.red;

            FireworksExplosionPool.Instance.Spawn(
                cutPoint,
                fireworkColor,
                burstCount: 250,
                life: 2.0f
            );

            SpawnFlyingUsername(bomber, cutPoint);

            BombManager.Instance.ClearBombVisuals();
        }

        private static void EnsureRefs()
        {
            if (_effectsSpawner == null)
            {
                _effectsSpawner = Resources.FindObjectsOfTypeAll<NoteCutCoreEffectsSpawner>().FirstOrDefault();
                if (_effectsSpawner != null)
                    LogUtils.Debug(() => "BombCutPatch: Cached NoteCutCoreEffectsSpawner");
            }

            if (_flyingTextPrefab == null)
            {
                var flyingScores = Resources.FindObjectsOfTypeAll<FlyingScoreEffect>();
                if (flyingScores != null && flyingScores.Length > 0)
                {
                    _flyingTextPrefab = flyingScores[0].GetComponentInChildren<CurvedTextMeshPro>(true);
                    if (_flyingTextPrefab != null)
                        LogUtils.Debug(() => "BombCutPatch: Cached CurvedTextMeshPro from FlyingScoreEffect");
                }
            }

            if (_tmpDistanceFieldShader == null)
            {
                _tmpDistanceFieldShader = Resources.FindObjectsOfTypeAll<Shader>()
                    .FirstOrDefault(s => s != null && s.name != null && s.name.Contains("TextMeshPro/Distance Field"));
            }

            if (_tekoFontCached == null)
            {
                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                _tekoFontCached = fonts?.FirstOrDefault(f => f != null && f.name != null && f.name.Contains("Teko"));
            }
        }

        private static void SpawnFlyingUsername(string username, Vector3 cutPoint)
        {
            if (string.IsNullOrEmpty(username)) return;

            try
            {
                if (_flyingTextPrefab != null)
                    SpawnCurvedFlyingText(username, cutPoint);
                else
                    SpawnSimpleFlyingText(username, cutPoint);
            }
            catch (Exception ex)
            {
                LogUtils.Error($"BombCutPatch: Error spawning username text: {ex}");
            }
        }

        private static void SpawnCurvedFlyingText(string username, Vector3 cutPoint)
        {
            var textGo = new GameObject("BombUsername_CurvedText");
            textGo.transform.position = cutPoint + Vector3.up * 0.5f;

            var curvedText = textGo.AddComponent<CurvedTextMeshPro>();

            var customFont = FontBundleLoader.BombUsernameFont;
            if (customFont != null)
                curvedText.font = customFont;
            else if (_flyingTextPrefab.font != null)
                curvedText.font = _flyingTextPrefab.font;

            LogUtils.Debug(() => $"BombText font = {(customFont != null ? customFont.name : "NULL (fallback)")}");

            curvedText.text = username;
            curvedText.fontSize = 4f;
            curvedText.alignment = TextAlignmentOptions.Center;
            curvedText.color = Color.yellow;
            curvedText.outlineWidth = 0.2f;
            curvedText.outlineColor = Color.black;

            ApplyBloomToTextMaterial(curvedText);

            float height = Plugin.Settings?.BombTextHeight ?? 1.0f;
            float width = Plugin.Settings?.BombTextWidth ?? 1.0f;
            height = Mathf.Clamp(height, 0.5f, 5f);
            width = Mathf.Clamp(width, 0.5f, 5f);
            textGo.transform.localScale = new Vector3(width, height, height);

            CoroutineHost.Instance.StartCoroutine(AnimateFlyingText(textGo, cutPoint));
        }

        private static void SpawnSimpleFlyingText(string username, Vector3 cutPoint)
        {
            var textGo = new GameObject("BombUsername_Text");
            textGo.transform.position = cutPoint + Vector3.up * 0.5f;

            var tmp = textGo.AddComponent<TextMeshPro>();

            var customFont = FontBundleLoader.BombUsernameFont;
            if (customFont != null)
                tmp.font = customFont;
            else if (_tekoFontCached != null)
                tmp.font = _tekoFontCached;

            tmp.text = username;
            tmp.fontSize = 4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.yellow;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = Color.black;

            textGo.AddComponent<LookAtCamera>();

            CoroutineHost.Instance.StartCoroutine(AnimateFlyingText(textGo, cutPoint));
        }

        // NOTE: This still clones a material per spawned text object.
        // That’s usually acceptable (bomb cut is infrequent), and avoids changing shared TMP materials globally.
        private static void ApplyBloomToTextMaterial(TMP_Text textComponent)
        {
            if (textComponent == null || textComponent.material == null) return;

            Material mat = new Material(textComponent.material);

            if (_tmpDistanceFieldShader != null)
                mat.shader = _tmpDistanceFieldShader;

            mat.EnableKeyword("_EMISSION");
            mat.SetFloat("_GlowPower", 0.5f);
            mat.SetFloat("_Glow", 1.0f);
            mat.SetFloat("_ScaleRatioA", 1.0f);
            mat.SetFloat("_ScaleRatioB", 1.0f);

            textComponent.material = mat;
        }

        private static IEnumerator AnimateFlyingText(GameObject textGo, Vector3 startPos)
        {
            float duration = 2.0f;
            float elapsed = 0f;

            float spawnDistance = Plugin.Settings?.BombSpawnDistance ?? 10.0f;
            spawnDistance = Mathf.Clamp(spawnDistance, 2f, 20f);

            Vector3 forward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
            Vector3 targetPos = startPos + forward * spawnDistance + Vector3.up * 2f;

            TMP_Text tmp = textGo.GetComponent<TMP_Text>();
            Color startColor = Plugin.Settings?.BombGradientStart ?? Color.yellow;
            Color endColor = Plugin.Settings?.BombGradientEnd ?? Color.red;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                textGo.transform.position = Vector3.Lerp(startPos + Vector3.up * 0.5f, targetPos, t);

                if (tmp != null)
                {
                    Color c = Color.Lerp(startColor, endColor, t);
                    c.a = Mathf.Lerp(1f, 0f, t);
                    tmp.color = c;
                }

                float scale = Mathf.Sin(t * Mathf.PI) * 0.3f + 1f;
                textGo.transform.localScale = Vector3.one * scale;

                yield return null;
            }

            UnityEngine.Object.Destroy(textGo);
        }
    }
}
