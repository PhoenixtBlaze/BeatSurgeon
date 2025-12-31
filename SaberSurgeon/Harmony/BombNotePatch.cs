using HarmonyLib;
using HMUI;
using SaberSurgeon;
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
                $"BombNotePatch: INIT -> time={noteData.time:F3}, colorType={noteData.colorType}, " +
                $"cutDir={noteData.cutDirection}, obj='{gameNote.name}', layer={gameNote.gameObject.layer}"
            );

            
            CacheBombPrefabIfNeeded();
            Color noteColor = TryGetNoteColor(__instance, noteData.colorType);

            bool isBomb = BombManager.Instance.MarkNoteAsBomb(noteData);
            if (!isBomb)
                return;

            BombManager.Instance.RegisterBombVisual(gameNote);

            // Only disable the NoteCube mesh (main note body), keep arrows
            DisableNoteCubeOnly(gameNote);

            // Create bomb visual with color
            AttachBombVisualWithColor(gameNote, noteColor);

            
        }

        private static void CacheBombPrefabIfNeeded()
        {
            if (_bombPrefab != null)
                return;

            _bombPrefab = Resources.FindObjectsOfTypeAll<BombNoteController>().FirstOrDefault();
            if (_bombPrefab != null)
                LogUtils.Debug($"BombNotePatch: Cached BombNoteController prefab '{_bombPrefab.name}'");
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
                    LogUtils.Debug($"BombNotePatch: Got note color via reflection: {noteColor}");
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
                        LogUtils.Debug($"BombNotePatch: Got color from ColorManager: {noteColor}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtils.Error($"BombNotePatch: Error getting color: {ex}");
            }

            return noteColor;
        }

        private static void DisableNoteCubeOnly(GameNoteController gameNote)
        {
            var noteCube = gameNote.transform.Find("NoteCube");
            if (noteCube == null)
                return;

            var cubeRenderer = noteCube.GetComponent<MeshRenderer>();
            if (cubeRenderer != null)
            {
                cubeRenderer.enabled = false;
                LogUtils.Debug("BombNotePatch: Disabled NoteCube renderer (keeping arrows visible)");
            }

            // Also disable the circle mesh if it exists (for dot notes)
            var circle = noteCube.Find("NoteCircleGlow");
            if (circle != null)
            {
                var circleRenderer = circle.GetComponent<MeshRenderer>();
                if (circleRenderer != null)
                    circleRenderer.enabled = false;
            }
        }

        private static void AttachBombVisualWithColor(GameNoteController gameNote, Color noteColor)
        {
            var existing = gameNote.transform.Find("SaberSurgeon_BombVisual");
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
                LogUtils.Debug("BombNotePatch: Removed pooled bomb visual before (re)creating");
                
            }

            var root = new GameObject("SaberSurgeon_BombVisual");
            root.transform.SetParent(gameNote.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            int noteLayer = gameNote.gameObject.layer;
            root.layer = noteLayer;
            root.SetActive(true);

            LogUtils.Debug($"BombNotePatch: Created bomb root under {gameNote.name}, layer={noteLayer}");

            if (_bombPrefab != null)
            {
                var prefabGO = _bombPrefab.gameObject;
                var instance = UnityEngine.Object.Instantiate(prefabGO, root.transform);
                instance.name = "BombPrefabInstance";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                instance.SetActive(true);

                // Remove BombNoteController + Colliders so it isn't a real bomb
                foreach (var bomb in instance.GetComponentsInChildren<BombNoteController>(true))
                {
                    bomb.enabled = false;
                    UnityEngine.Object.Destroy(bomb);
                }
                foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.Destroy(col);

                SetLayerRecursively(root.transform, noteLayer);

                var bombRenderers = root.GetComponentsInChildren<Renderer>(true);
                LogUtils.Debug($"BombNotePatch: Found {bombRenderers.Length} renderers in bomb visual");

                foreach (var mr in bombRenderers)
                {
                    if (mr == null) continue;

                    mr.enabled = true;
                    mr.gameObject.SetActive(true);

                    ApplyRendererColor(mr, noteColor);

                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    mr.receiveShadows = true;
                }
            }
            else
            {
                // Sphere fallback (no material allocations per-bomb)
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

                    if (_sphereSharedMaterial == null)
                    {
                        var safeShader = Shader.Find("Custom/SimpleLit") ?? Shader.Find("Standard");
                        if (safeShader != null)
                            _sphereSharedMaterial = new Material(safeShader);
                    }

                    if (_sphereSharedMaterial != null)
                        mr.sharedMaterial = _sphereSharedMaterial;

                    ApplyRendererColor(mr, noteColor);

                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    mr.receiveShadows = true;
                }

                SetLayerRecursively(root.transform, noteLayer);
                LogUtils.Debug("BombNotePatch: Sphere bomb visual created and colored");
            }

            // Watchdog: ignore bomb visual renderers, only watch base note tree changes
            try
            {
                var watchdog = gameNote.gameObject.AddComponent<RendererWatchdog>();
                watchdog.Init(gameNote.transform, 1.0f, root.transform);
                LogUtils.Debug("BombNotePatch: Watchdog started for 1s (ignoring bomb visual subtree)");
            }
            catch (Exception ex)
            {
                LogUtils.Warn("BombNotePatch: Failed to start watchdog: " + ex.Message);
            }
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
                else LogUtils.Debug($"BombNotePatch: No known color property on shader '{mat.shader?.name}' (matIndex={i})");
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
                    LogUtils.Debug("BombCutPatch: Cached NoteCutCoreEffectsSpawner");
            }

            if (_flyingTextPrefab == null)
            {
                var flyingScores = Resources.FindObjectsOfTypeAll<FlyingScoreEffect>();
                if (flyingScores != null && flyingScores.Length > 0)
                {
                    _flyingTextPrefab = flyingScores[0].GetComponentInChildren<CurvedTextMeshPro>(true);
                    if (_flyingTextPrefab != null)
                        LogUtils.Debug("BombCutPatch: Cached CurvedTextMeshPro from FlyingScoreEffect");
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

            LogUtils.Debug($"BombText font = {(customFont != null ? customFont.name : "NULL (fallback)")}");

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
