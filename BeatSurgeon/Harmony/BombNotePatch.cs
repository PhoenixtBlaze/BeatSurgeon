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
using BeatSurgeon.Utils;
using BeatSurgeon.Twitch;

namespace BeatSurgeon.HarmonyPatches
{
    // Runs after ColorNoteVisuals creates the normal note visuals
    [HarmonyPatch(typeof(ColorNoteVisuals), "HandleNoteControllerDidInit")]
    [HarmonyPriority(Priority.Last)]
    internal static class BombNotePatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("BombNotePatch");
        private static BombNoteController _bombPrefab;

        // Reflection caches (avoid AccessTools.Field per note init)
        private static FieldInfo _noteColorField;
        private static FieldInfo _colorManagerField;
        private static MethodInfo _colorForTypeMethod;

        private static void Postfix(ColorNoteVisuals __instance, NoteControllerBase noteController)
        {
            try
            {
                // Only create bombs while the bomb window is active
                if (!BombManager.IsBombWindowActive)
                    return;

            var noteData = noteController?.noteData;
            if (!BombManager.IsEligibleBombNote(noteData))
                return;

            var gameNote = noteController as GameNoteController ?? __instance.GetComponentInParent<GameNoteController>();
            if (gameNote == null)
            {
                LogUtils.Warn("BombNotePatch: No GameNoteController found for noteController type '" + (noteController != null ? noteController.GetType().Name : "<null>") + "'");
                return;
            }

            LogUtils.Debug(
                () => $"BombNotePatch: INIT -> time={noteData.time:F3}, colorType={noteData.colorType}, " +
                $"cutDir={noteData.cutDirection}, obj='{gameNote.name}', layer={gameNote.gameObject.layer}"
            );

            if (!TryMarkAndApplyBombVisual(gameNote, __instance, noteData, "init"))
                return;
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }

        internal static bool TryMarkAndApplyBombVisual(GameNoteController gameNote, ColorNoteVisuals visuals, NoteData noteData, string trigger)
        {
            if (gameNote == null || !BombManager.IsEligibleBombNote(noteData))
            {
                return false;
            }

            if (GlitterManager.Instance.TryRequeueMarkedEffect(gameNote, "BombOverride", out int requeuedDenomination))
            {
                OutlineEmitterManager.Instance.DetachFromNote(gameNote);
                GlitterLoopEmitterManager.Instance.DetachFromNote(gameNote);
                LogUtils.Debug(() =>
                    "BombNotePatch: Requeued glitter effect because note became a bomb at "
                    + noteData.time.ToString("F3")
                    + " denomination="
                    + requeuedDenomination);
            }

            CacheBombPrefabIfNeeded();

            bool isBomb = BombManager.Instance.MarkNoteAsBomb(noteData);
            if (!isBomb)
            {
                return false;
            }

            LogUtils.Debug(() => $"BombNotePatch: Marked note via {trigger} at {noteData.time:F3}");

            Color noteColor = visuals != null
                ? TryGetNoteColor(visuals, noteData.colorType)
                : Color.magenta;

            CacheAndDisableNoteCube(
                gameNote,
                out var cubeMr,
                out var circleMr,
                out var arrowMr,
                out var arrowGlowMr,
                out var cubeWasEnabled,
                out var circleWasEnabled,
                out var arrowWasEnabled,
                out var arrowGlowWasEnabled);

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
                arrowMr,
                arrowGlowMr,
                cubeWasEnabled,
                circleWasEnabled,
                arrowWasEnabled,
                arrowGlowWasEnabled
            );

            return true;
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
            out MeshRenderer arrowMr,
            out MeshRenderer arrowGlowMr,
            out bool cubeWasEnabled,
            out bool circleWasEnabled,
            out bool arrowWasEnabled,
            out bool arrowGlowWasEnabled)
        {
            cubeMr = null;
            circleMr = null;
            arrowMr = null;
            arrowGlowMr = null;
            cubeWasEnabled = false;
            circleWasEnabled = false;
            arrowWasEnabled = false;
            arrowGlowWasEnabled = false;

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

            var arrowT = gameNote.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "NoteArrow");

            arrowMr = arrowT != null
                ? (arrowT.GetComponent<MeshRenderer>() ?? arrowT.GetComponentInChildren<MeshRenderer>(true))
                : null;

            if (arrowMr != null)
            {
                arrowWasEnabled = arrowMr.enabled;
                arrowMr.enabled = false;
            }

            var arrowGlowT = gameNote.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "NoteArrowGlow");

            arrowGlowMr = arrowGlowT != null
                ? (arrowGlowT.GetComponent<MeshRenderer>() ?? arrowGlowT.GetComponentInChildren<MeshRenderer>(true))
                : null;

            if (arrowGlowMr != null)
            {
                arrowGlowWasEnabled = arrowGlowMr.enabled;
                arrowGlowMr.enabled = false;
            }
        }


    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteDidStartJump")]
    internal static class BombLateStartJumpPatch
    {
        private static void Prefix(NoteController noteController)
        {
            try
            {
                if (!BombManager.IsBombWindowActive)
                {
                    return;
                }

                var noteData = noteController?.noteData;
                if (!BombManager.IsEligibleBombNote(noteData))
                {
                    return;
                }

                if (BombManager.Instance.IsNoteMarkedAsBomb(noteData))
                {
                    return;
                }

                var gameNote = noteController as GameNoteController;
                if (gameNote == null)
                {
                    return;
                }

                var visuals = gameNote.GetComponentInChildren<ColorNoteVisuals>(true);
                if (BombNotePatch.TryMarkAndApplyBombVisual(gameNote, visuals, noteData, "start-jump"))
                {
                    LogUtils.Debug(() => $"BombNotePatch: Late-marked bomb note at start-jump for time={noteData.time:F3}");
                }
            }
            catch (Exception ex)
            {
                LogUtils.Warn("BombLateStartJumpPatch: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(GameNoteController), "HandleCut")]
    [HarmonyPriority(Priority.High)]
    internal static class BombCutPatch
    {
        private sealed class PooledFlyingText : MonoBehaviour
        {
            internal TMP_Text TextComponent;
            internal TMP_FontAsset AppliedFont;
            internal bool UsesCurvedText;
        }

        private static readonly LogUtil _log = LogUtil.GetLogger("BombCutPatch");
        private static CurvedTextMeshPro _flyingTextPrefab;
        private static readonly Queue<PooledFlyingText> _curvedFlyingTextPool = new Queue<PooledFlyingText>();
        private static readonly Queue<PooledFlyingText> _simpleFlyingTextPool = new Queue<PooledFlyingText>();
        private static readonly Vector3 FlyingTextWarmActivationOffset = new Vector3(0f, -2048f, 0f);
        private const int RecommendedFlyingTextPoolSize = 4;
        private const int MaxFlyingTextPoolSize = 12;

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
            try
            {
                var noteData = __instance.noteData;
                if (noteData == null) return;

                BombManager.BombRequest bombRequest;
                if (!BombManager.Instance.TryConsumeBomb(noteData, out bombRequest)) return;

                string requesterName = bombRequest?.RequesterName ?? "Unknown";
                string displayText = string.IsNullOrWhiteSpace(bombRequest?.DisplayText)
                    ? requesterName
                    : bombRequest.DisplayText;

                LogUtils.Debug(() => $"BombCutPatch: Bomb cut requestedBy={requesterName} displayText={displayText}");

                EnsureRefs();

                Color fireworkColor = EntitlementsState.HasVisualsAccess
                    ? (Plugin.Settings?.BombGradientStart ?? Color.red)
                    : Color.red;

                FireworksExplosionPool.Instance.Spawn(
                    cutPoint,
                    fireworkColor,
                    burstCount: 250,
                    life: 2.0f
                );

                SpawnFlyingText(displayText, cutPoint);

                BombManager.Instance.ClearBombVisuals();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }

        private static void EnsureRefs()
        {
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

        internal static void PrewarmFlyingTextResources()
        {
            try
            {
                EnsureRefs();

                if (_flyingTextPrefab != null)
                {
                    EnsureFlyingTextPoolSize(_curvedFlyingTextPool, RecommendedFlyingTextPoolSize, useCurvedText: true);
                    RefreshFlyingTextPool(_curvedFlyingTextPool);
                }
                else
                {
                    EnsureFlyingTextPoolSize(_simpleFlyingTextPool, RecommendedFlyingTextPoolSize, useCurvedText: false);
                    RefreshFlyingTextPool(_simpleFlyingTextPool);
                }
            }
            catch (Exception ex)
            {
                LogUtils.Warn("BombCutPatch: Failed to prewarm flying text resources: " + ex.Message);
            }
        }

        internal static void SpawnFlyingText(string displayText, Vector3 cutPoint)
        {
            if (string.IsNullOrEmpty(displayText)) return;

            EnsureRefs();

            try
            {
                if (_flyingTextPrefab != null)
                    SpawnCurvedFlyingText(displayText, cutPoint);
                else
                    SpawnSimpleFlyingText(displayText, cutPoint);
            }
            catch (Exception ex)
            {
                LogUtils.Error($"BombCutPatch: Error spawning bomb text: {ex}");
            }
        }

        private static void ApplyBombTextFont(TMP_Text textComponent, TMP_FontAsset customFont, TMP_FontAsset fallbackFont)
        {
            if (textComponent == null)
            {
                return;
            }

            FontBundleLoader.TryApplySelectedBombFont(textComponent, fallbackFont);
        }

        private static void SpawnCurvedFlyingText(string displayText, Vector3 cutPoint)
        {
            PooledFlyingText pooledText = GetOrCreateFlyingText(useCurvedText: true);
            if (pooledText == null || pooledText.TextComponent == null)
            {
                return;
            }

            GameObject textGo = pooledText.gameObject;
            textGo.transform.position = cutPoint + Vector3.up * 0.5f;
            textGo.SetActive(true);
            ConfigureFlyingText(pooledText, displayText, _flyingTextPrefab != null ? _flyingTextPrefab.font : null);

            float height = EntitlementsState.HasVisualsAccess
                ? (Plugin.Settings?.BombTextHeight ?? 1.0f)
                : 1.0f;
            float width = EntitlementsState.HasVisualsAccess
                ? (Plugin.Settings?.BombTextWidth ?? 1.0f)
                : 1.0f;
            height = Mathf.Clamp(height, 0.5f, 5f);
            width = Mathf.Clamp(width, 0.5f, 5f);
            textGo.transform.localScale = new Vector3(width, height, height);

            CoroutineHost.Instance.StartCoroutine(AnimateFlyingText(pooledText, cutPoint));
        }

        private static void SpawnSimpleFlyingText(string displayText, Vector3 cutPoint)
        {
            PooledFlyingText pooledText = GetOrCreateFlyingText(useCurvedText: false);
            if (pooledText == null || pooledText.TextComponent == null)
            {
                return;
            }

            GameObject textGo = pooledText.gameObject;
            textGo.transform.position = cutPoint + Vector3.up * 0.5f;
            textGo.transform.localScale = Vector3.one;
            textGo.SetActive(true);
            ConfigureFlyingText(pooledText, displayText, _tekoFontCached);

            CoroutineHost.Instance.StartCoroutine(AnimateFlyingText(pooledText, cutPoint));
        }

        private static void EnsureFlyingTextPoolSize(Queue<PooledFlyingText> pool, int desiredPoolSize, bool useCurvedText)
        {
            if (pool == null)
            {
                return;
            }

            while (GetLivePooledEntryCount(pool) < desiredPoolSize)
            {
                PooledFlyingText pooledText = CreatePooledFlyingText(useCurvedText);
                if (pooledText == null)
                {
                    return;
                }

                pool.Enqueue(pooledText);
            }
        }

        private static int GetLivePooledEntryCount(Queue<PooledFlyingText> pool)
        {
            int count = 0;
            foreach (PooledFlyingText pooledText in pool)
            {
                if (pooledText != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static void RefreshFlyingTextPool(Queue<PooledFlyingText> pool)
        {
            if (pool == null)
            {
                return;
            }

            foreach (PooledFlyingText pooledText in pool)
            {
                if (pooledText != null && pooledText.TextComponent != null)
                {
                    WarmPrepareFlyingText(pooledText);
                }
            }
        }

        private static PooledFlyingText GetOrCreateFlyingText(bool useCurvedText)
        {
            Queue<PooledFlyingText> pool = useCurvedText ? _curvedFlyingTextPool : _simpleFlyingTextPool;
            while (pool.Count > 0)
            {
                PooledFlyingText pooledText = pool.Dequeue();
                if (pooledText != null && pooledText.TextComponent != null)
                {
                    return pooledText;
                }
            }

            return CreatePooledFlyingText(useCurvedText);
        }

        private static PooledFlyingText CreatePooledFlyingText(bool useCurvedText)
        {
            GameObject textGo = new GameObject(useCurvedText ? "BombUsername_CurvedText" : "BombUsername_Text");
            PooledFlyingText pooledText = textGo.AddComponent<PooledFlyingText>();
            pooledText.UsesCurvedText = useCurvedText;

            if (useCurvedText)
            {
                pooledText.TextComponent = textGo.AddComponent<CurvedTextMeshPro>();
            }
            else
            {
                pooledText.TextComponent = textGo.AddComponent<TextMeshPro>();
                textGo.AddComponent<LookAtCamera>();
            }

            WarmPrepareFlyingText(pooledText);
            return pooledText;
        }

        private static void WarmPrepareFlyingText(PooledFlyingText pooledText)
        {
            if (pooledText == null || pooledText.TextComponent == null)
            {
                return;
            }

            GameObject textGo = pooledText.gameObject;
            textGo.transform.position = FlyingTextWarmActivationOffset;
            textGo.transform.localScale = Vector3.one;
            textGo.SetActive(true);
            ConfigureFlyingText(
                pooledText,
                "Warmup",
                pooledText.UsesCurvedText && _flyingTextPrefab != null ? _flyingTextPrefab.font : _tekoFontCached);

            if (pooledText.TextComponent != null)
            {
                pooledText.TextComponent.text = string.Empty;
            }

            textGo.transform.SetParent(null, false);
            textGo.transform.localScale = Vector3.one;
            textGo.SetActive(false);
        }

        private static void ConfigureFlyingText(PooledFlyingText pooledText, string displayText, TMP_FontAsset fallbackFont)
        {
            TMP_Text textComponent = pooledText?.TextComponent;
            if (textComponent == null)
            {
                return;
            }

            TMP_FontAsset customFont = FontBundleLoader.BombUsernameFont;
            TMP_FontAsset resolvedFont = customFont ?? fallbackFont;
            bool fontChanged = resolvedFont != null && pooledText.AppliedFont != resolvedFont;
            if (fontChanged || textComponent.font == null)
            {
                ApplyBombTextFont(textComponent, customFont, fallbackFont);
                pooledText.AppliedFont = textComponent.font;

                if (pooledText.UsesCurvedText)
                {
                    ApplyBloomToTextMaterial(textComponent);
                }
            }

            if (pooledText.UsesCurvedText)
            {
                LogUtils.Debug(() => $"BombText font = {(customFont != null ? customFont.name : "NULL (fallback)")}");
            }

            textComponent.text = displayText;
            textComponent.fontSize = 4f;
            textComponent.alignment = TextAlignmentOptions.Center;
            textComponent.color = Color.yellow;
            var _mat = textComponent.fontSharedMaterial ?? textComponent.fontMaterial ?? textComponent.material;
            if (_mat != null)
            {
                textComponent.outlineWidth = 0.2f;
                textComponent.outlineColor = Color.black;
            }
            textComponent.SetAllDirty();
            textComponent.ForceMeshUpdate();
        }

        // Keep a dedicated material instance per curved pooled text object so per-text glow/color changes
        // never mutate shared TMP font materials.
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

        private static IEnumerator AnimateFlyingText(PooledFlyingText pooledText, Vector3 startPos)
        {
            if (pooledText == null || pooledText.TextComponent == null)
            {
                yield break;
            }

            GameObject textGo = pooledText.gameObject;
            float duration = EntitlementsState.HasVisualsAccess
                ? Mathf.Clamp(Plugin.Settings?.FlyingTextTravelSeconds ?? 4.0f, 0.5f, 20f)
                : 4.0f;
            float elapsed = 0f;

            Vector3 initialPosition = startPos + Vector3.up * 0.5f;
            Vector3 targetPos;

            if (!SurgeonEffectsBundleService.TryResolveFollowerCanvasStartWorldPosition(out targetPos))
            {
                float spawnDistance = EntitlementsState.HasVisualsAccess
                    ? (Plugin.Settings?.BombSpawnDistance ?? 10.0f)
                    : 10.0f;
                spawnDistance = Mathf.Clamp(spawnDistance, 2f, 20f);

                Vector3 forward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
                targetPos = startPos + forward * spawnDistance + Vector3.up * 2f;
            }

            TMP_Text tmp = textGo.GetComponent<TMP_Text>();
            Color startColor = EntitlementsState.HasVisualsAccess
                ? (Plugin.Settings?.BombGradientStart ?? Color.yellow)
                : Color.yellow;
            Color endColor = EntitlementsState.HasVisualsAccess
                ? (Plugin.Settings?.BombGradientEnd ?? Color.red)
                : Color.red;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                textGo.transform.position = Vector3.Lerp(initialPosition, targetPos, t);

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

            ReturnFlyingTextToPool(pooledText, destroyIfPoolFull: true);
        }

        private static void ReturnFlyingTextToPool(PooledFlyingText pooledText, bool destroyIfPoolFull)
        {
            if (pooledText == null)
            {
                return;
            }

            GameObject textGo = pooledText.gameObject;
            if (pooledText.TextComponent != null)
            {
                pooledText.TextComponent.text = string.Empty;
            }

            textGo.transform.SetParent(null, false);
            textGo.transform.localScale = Vector3.one;
            textGo.SetActive(false);

            Queue<PooledFlyingText> pool = pooledText.UsesCurvedText ? _curvedFlyingTextPool : _simpleFlyingTextPool;
            if (destroyIfPoolFull && GetLivePooledEntryCount(pool) >= MaxFlyingTextPoolSize)
            {
                UnityEngine.Object.Destroy(textGo);
                return;
            }

            pool.Enqueue(pooledText);
        }
    }

    [HarmonyPatch(typeof(NoteCutCoreEffectsSpawner), "HandleNoteWasCut")]
    internal static class BombMarkedNoteCutCoreEffectsPatch
    {
        private static bool Prefix(NoteController noteController, in NoteCutInfo noteCutInfo)
        {
            var noteData = noteController?.noteData;
            if (noteData == null)
            {
                return true;
            }

            if (!BombManager.Instance.IsNoteMarkedAsBomb(noteData))
            {
                return true;
            }

            LogUtils.Debug(() => "BombMarkedNoteCutCoreEffectsPatch: Suppressing base NoteCutCoreEffectsSpawner for BeatSurgeon bomb note.");
            return false;
        }
    }

    [HarmonyPatch(typeof(BadNoteCutEffectSpawner), "HandleNoteWasCut")]
    internal static class BombMarkedBadCutEffectsPatch
    {
        private static bool Prefix(NoteController noteController, in NoteCutInfo noteCutInfo)
        {
            var noteData = noteController?.noteData;
            if (noteData == null)
            {
                return true;
            }

            if (!BombManager.Instance.IsNoteMarkedAsBomb(noteData))
            {
                return true;
            }

            LogUtils.Debug(() => "BombMarkedBadCutEffectsPatch: Suppressing base BadNoteCutEffectSpawner for BeatSurgeon bomb note.");
            return false;
        }
    }
}
