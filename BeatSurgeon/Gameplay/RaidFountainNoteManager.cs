using System;
using System.Collections.Generic;
using BeatSurgeon.Utils;
using TMPro;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Queues the next N map notes for a pure-C# raid effect (no prefab):
    /// tiny RGB name text outlining each cube, then on cut a Sparks firework plus
    /// a large RGB name-text burst. Yields to glitter and trail-cube claims.
    /// </summary>
    internal sealed class RaidFountainNoteManager : MonoBehaviour
    {
        private class QueuedEntry
        {
            internal QueuedEntry(string raiderName)
            {
                RaiderName = NormalizeRaiderName(raiderName);
            }

            internal string RaiderName { get; private set; }
        }

        private sealed class ActiveEntry : QueuedEntry
        {
            internal ActiveEntry(GameNoteController controller, string raiderName)
                : base(raiderName)
            {
                Controller = controller;
                Shards = new List<ShardState>(OutlineShardsPerNote);
            }

            internal GameNoteController Controller { get; private set; }
            internal List<ShardState> Shards { get; private set; }
        }

        private sealed class ShardState
        {
            internal GameObject Root;
            internal TextMeshPro Text;
            internal LookAtCamera Billboard;
            internal Vector3 Velocity;
            internal float Elapsed;
            internal float Lifetime;
            internal Color BaseColor;
            internal bool Active;
            /// <summary>True while the shard rides the note in local space.</summary>
            internal bool FollowsNote;
            /// <summary>Tiny shell text around the cube (vs large cut-burst shards).</summary>
            internal bool IsOutline;
        }

        private static readonly LogUtil _log = LogUtil.GetLogger("RaidFountainNoteManager");
        private static TMP_FontAsset _cachedTekoFont;

        internal const int MaxNotesPerRaid = 100;
        internal const int RecommendedWarmPoolSize = 72;
        private const int MaxPendingEntries = 256;
        private const int MaxActiveNotes = 64;
        private const int OutlineShardsPerNote = 36;
        private const int CutBurstShards = 16;
        private const int MaxPoolSize = 384;
        private const float CutBurstLifetimeSeconds = 1.15f;
        private const float CutBurstSpeedMin = 1.35f;
        private const float CutBurstSpeedMax = 2.9f;
        private const float OutlineShellRadius = 0.38f;
        private const float SpectrumCycleSeconds = 0.5f;
        private const float OutlineFontSize = 0.18f;
        private const float CutBurstFontSize = 1.75f;
        private const float OutlineTextWidth = 1.2f;
        private const float CutBurstTextWidth = 8f;

        private static RaidFountainNoteManager _instance;
        private static GameObject _go;

        private readonly LinkedList<QueuedEntry> _pendingEntries = new LinkedList<QueuedEntry>();
        private readonly Dictionary<GameNoteController, ActiveEntry> _activeEntries =
            new Dictionary<GameNoteController, ActiveEntry>();
        private readonly Queue<ShardState> _pool = new Queue<ShardState>();
        private readonly List<ShardState> _liveShards = new List<ShardState>(MaxPoolSize);
        private readonly List<ShardState> _shardScratch = new List<ShardState>(MaxPoolSize);

        private GameplayManager _gameplayManager;
        private bool _fontWarned;

        internal static RaidFountainNoteManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeon_RaidFountainNoteManager_GO");
                    UnityEngine.Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<RaidFountainNoteManager>();
                }

                return _instance;
            }
        }

        internal bool HasPendingNotes => _pendingEntries.Count > 0;

        internal static int ClampNoteCount(int requested)
        {
            return Mathf.Clamp(requested, 1, MaxNotesPerRaid);
        }

        internal bool IsNoteMarked(GameNoteController controller)
        {
            return controller != null && _activeEntries.ContainsKey(controller);
        }

        internal bool EnsureWarmPoolSize(int desiredShardCount)
        {
            desiredShardCount = Mathf.Clamp(desiredShardCount, 0, MaxPoolSize);
            if (desiredShardCount <= 0)
            {
                return true;
            }

            if (!FontBundleLoader.IsBombFontReady)
            {
                if (!_fontWarned)
                {
                    _fontWarned = true;
                    _log.Warn("RaidFountainNoteManager: bomb font not ready for warm pool.");
                }

                return false;
            }

            while (_pool.Count < desiredShardCount)
            {
                ShardState shard = CreateShard(warm: true);
                if (shard == null)
                {
                    return false;
                }

                _pool.Enqueue(shard);
            }

            return true;
        }

        internal bool QueueNotes(int requestedCount, string raiderName)
        {
            if (!IsInMap())
            {
                _log.Warn("Raid fountain notes ignored because gameplay is not active.");
                return false;
            }

            int normalizedCount = Mathf.Max(0, requestedCount);
            if (normalizedCount <= 0)
            {
                return false;
            }

            int availableSlots = Mathf.Max(0, MaxPendingEntries - (_pendingEntries.Count + _activeEntries.Count));
            if (availableSlots <= 0)
            {
                _log.Warn("Raid fountain queue is full.");
                return false;
            }

            int enqueuedCount = Mathf.Min(normalizedCount, availableSlots);
            string normalizedRaiderName = NormalizeRaiderName(raiderName);
            for (int index = 0; index < enqueuedCount; index++)
            {
                _pendingEntries.AddLast(new QueuedEntry(normalizedRaiderName));
            }

            _log.Info(
                "Queued raid fountain notes raider="
                + normalizedRaiderName
                + " requested="
                + normalizedCount
                + " enqueued="
                + enqueuedCount
                + " pending="
                + _pendingEntries.Count
                + " active="
                + _activeEntries.Count);
            return enqueuedCount > 0;
        }

        internal bool TryMarkAndAttach(GameNoteController controller)
        {
            if (controller == null)
            {
                return false;
            }

            NoteData noteData = controller.noteData;
            if (!BombManager.IsEligibleBombNote(noteData))
            {
                return false;
            }

            if (_activeEntries.ContainsKey(controller))
            {
                return true;
            }

            if (_pendingEntries.First == null)
            {
                return false;
            }

            if (_activeEntries.Count >= MaxActiveNotes)
            {
                return false;
            }

            if (BombManager.IsBombWindowActive && BombManager.Instance.IsNoteMarkedAsBomb(noteData))
            {
                return false;
            }

            if (!FontBundleLoader.IsBombFontReady)
            {
                if (!_fontWarned)
                {
                    _fontWarned = true;
                    _log.Warn("RaidFountainNoteManager: bomb font not ready; cannot attach fountain.");
                }

                return false;
            }

            QueuedEntry queued = _pendingEntries.First.Value;
            _pendingEntries.RemoveFirst();

            try
            {
                Transform parent = controller.noteTransform != null
                    ? controller.noteTransform
                    : controller.transform;
                int layer = controller.gameObject.layer;
                var activeEntry = new ActiveEntry(controller, queued.RaiderName);
                SpawnOutlineShell(parent, queued.RaiderName, layer, activeEntry.Shards);
                _activeEntries[controller] = activeEntry;
                return true;
            }
            catch (Exception ex)
            {
                _pendingEntries.AddFirst(new QueuedEntry(queued.RaiderName));
                _log.Warn("RaidFountainNoteManager: attach failed: " + ex.Message);
                return false;
            }
        }

        internal bool TryConsumeMarkedNote(
            GameNoteController controller,
            out string raiderName,
            Vector3 cutPoint,
            bool explode)
        {
            raiderName = "Unknown";
            if (controller == null)
            {
                return false;
            }

            if (!_activeEntries.TryGetValue(controller, out ActiveEntry activeEntry))
            {
                return false;
            }

            _activeEntries.Remove(controller);
            RecycleOwnedShards(activeEntry);
            raiderName = activeEntry.RaiderName;

            if (explode)
            {
                int layer = controller.gameObject != null ? controller.gameObject.layer : 0;
                SpawnCutExplosion(cutPoint, raiderName, layer);
            }

            return true;
        }

        internal bool TryConsumeMarkedNote(GameNoteController controller, out string raiderName)
        {
            Vector3 cutPoint = controller != null
                ? (controller.noteTransform != null ? controller.noteTransform.position : controller.transform.position)
                : Vector3.zero;
            return TryConsumeMarkedNote(controller, out raiderName, cutPoint, explode: false);
        }

        internal bool TryRequeueMarkedNote(GameNoteController controller, string reason)
        {
            if (controller == null)
            {
                return false;
            }

            if (!_activeEntries.TryGetValue(controller, out ActiveEntry activeEntry))
            {
                return false;
            }

            _activeEntries.Remove(controller);
            RecycleOwnedShards(activeEntry);
            _pendingEntries.AddFirst(new QueuedEntry(activeEntry.RaiderName));
            _log.Debug(
                "Requeued raid fountain raider="
                + activeEntry.RaiderName
                + " reason="
                + (string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason)
                + " pending="
                + _pendingEntries.Count
                + " active="
                + _activeEntries.Count);
            return true;
        }

        internal static void ClearForSceneExit()
        {
            if (_instance == null)
            {
                return;
            }

            _instance.ClearTransientGameplayState("SceneExit");
        }

        private void Update()
        {
            if (!IsInMap() && (_pendingEntries.Count > 0 || _activeEntries.Count > 0 || _liveShards.Count > 0))
            {
                ClearTransientGameplayState("SceneChanged");
                return;
            }

            TickLiveShards();
        }

        private void TickLiveShards()
        {
            if (_liveShards.Count == 0)
            {
                return;
            }

            _shardScratch.Clear();
            float dt = Time.deltaTime;
            for (int index = 0; index < _liveShards.Count; index++)
            {
                ShardState shard = _liveShards[index];
                if (shard == null || !shard.Active || shard.Root == null || shard.Text == null)
                {
                    continue;
                }

                // Note was destroyed without a cut/miss release — keep cut shards in world space.
                if (shard.FollowsNote && shard.Root.transform.parent == null)
                {
                    if (shard.IsOutline)
                    {
                        RecycleShard(shard);
                        continue;
                    }

                    DetachShardToWorld(shard);
                }

                shard.Elapsed += dt;

                // Full HSV spectrum every SpectrumCycleSeconds for a fast rainbow flash.
                float hue = Mathf.Repeat(shard.Elapsed / Mathf.Max(0.01f, SpectrumCycleSeconds), 1f);
                Color color = Color.HSVToRGB(hue, 1f, 1f);

                if (shard.IsOutline && shard.FollowsNote)
                {
                    // Fixed shell around the cube — only recolor while attached.
                    color.a = shard.BaseColor.a;
                    shard.Text.color = color;
                    _shardScratch.Add(shard);
                    continue;
                }

                float t = Mathf.Clamp01(shard.Elapsed / Mathf.Max(0.01f, shard.Lifetime));
                if (shard.FollowsNote)
                {
                    shard.Root.transform.localPosition += shard.Velocity * dt;
                }
                else
                {
                    shard.Root.transform.position += shard.Velocity * dt;
                }

                shard.Velocity *= 0.985f;
                color.a = shard.BaseColor.a * (1f - t);
                shard.Text.color = color;

                if (t >= 1f)
                {
                    RecycleShard(shard);
                }
                else
                {
                    _shardScratch.Add(shard);
                }
            }

            _liveShards.Clear();
            _liveShards.AddRange(_shardScratch);
        }

        private void SpawnOutlineShell(
            Transform noteParent,
            string raiderName,
            int layer,
            List<ShardState> ownedShards)
        {
            if (noteParent == null)
            {
                return;
            }

            for (int index = 0; index < OutlineShardsPerNote; index++)
            {
                ShardState shard = GetOrCreateShard();
                if (shard == null)
                {
                    break;
                }

                Vector3 direction = FibonacciDirection(index, OutlineShardsPerNote);
                shard.Velocity = Vector3.zero;
                shard.Elapsed = index * (SpectrumCycleSeconds / OutlineShardsPerNote);
                shard.Lifetime = float.MaxValue;
                shard.BaseColor = Color.white;
                shard.Active = true;
                shard.FollowsNote = true;
                shard.IsOutline = true;

                shard.Root.transform.SetParent(noteParent, false);
                shard.Root.transform.localPosition = direction * OutlineShellRadius;
                shard.Root.transform.localRotation = Quaternion.identity;
                shard.Root.transform.localScale = Vector3.one;
                SetLayerRecursively(shard.Root, layer);

                shard.Text.text = raiderName;
                shard.Text.fontSize = OutlineFontSize;
                if (shard.Text.rectTransform != null)
                {
                    shard.Text.rectTransform.sizeDelta = new Vector2(OutlineTextWidth, 0.6f);
                }

                shard.Text.color = Color.HSVToRGB(
                    Mathf.Repeat(shard.Elapsed / SpectrumCycleSeconds, 1f),
                    1f,
                    1f);
                shard.Root.SetActive(true);
                _liveShards.Add(shard);
                ownedShards?.Add(shard);
            }
        }

        private void SpawnCutExplosion(Vector3 origin, string raiderName, int layer)
        {
            try
            {
                FireworksExplosionPool.Instance.SpawnRaidCutExplosion(origin);
            }
            catch (Exception ex)
            {
                _log.Warn("RaidFountainNoteManager: raid cut firework failed: " + ex.Message);
            }

            for (int index = 0; index < CutBurstShards; index++)
            {
                ShardState shard = GetOrCreateShard();
                if (shard == null)
                {
                    break;
                }

                Vector3 direction = FibonacciDirection(index, CutBurstShards);
                float speed = Mathf.Lerp(
                    CutBurstSpeedMin,
                    CutBurstSpeedMax,
                    index / (float)Mathf.Max(1, CutBurstShards - 1));
                shard.Velocity = direction * speed;
                shard.Elapsed = index * (SpectrumCycleSeconds / CutBurstShards);
                shard.Lifetime = CutBurstLifetimeSeconds;
                shard.BaseColor = Color.white;
                shard.Active = true;
                shard.FollowsNote = false;
                shard.IsOutline = false;

                shard.Root.transform.SetParent(null, false);
                shard.Root.transform.position = origin + (direction * 0.08f);
                shard.Root.transform.rotation = Quaternion.identity;
                shard.Root.transform.localScale = Vector3.one;
                SetLayerRecursively(shard.Root, layer);

                shard.Text.text = raiderName;
                shard.Text.fontSize = CutBurstFontSize;
                if (shard.Text.rectTransform != null)
                {
                    shard.Text.rectTransform.sizeDelta = new Vector2(CutBurstTextWidth, 2.5f);
                }

                shard.Text.color = Color.HSVToRGB(
                    Mathf.Repeat(shard.Elapsed / SpectrumCycleSeconds, 1f),
                    1f,
                    1f);
                shard.Root.SetActive(true);
                _liveShards.Add(shard);
            }
        }

        private void RecycleOwnedShards(ActiveEntry activeEntry)
        {
            if (activeEntry?.Shards == null)
            {
                return;
            }

            for (int index = 0; index < activeEntry.Shards.Count; index++)
            {
                ShardState shard = activeEntry.Shards[index];
                if (shard == null || !shard.Active)
                {
                    continue;
                }

                _liveShards.Remove(shard);
                RecycleShard(shard);
            }

            activeEntry.Shards.Clear();
        }

        private static void DetachShardToWorld(ShardState shard)
        {
            if (shard == null || shard.Root == null || !shard.FollowsNote)
            {
                return;
            }

            Transform rootTransform = shard.Root.transform;
            Vector3 worldPosition = rootTransform.position;
            Vector3 worldVelocity = rootTransform.parent != null
                ? rootTransform.parent.TransformDirection(shard.Velocity)
                : shard.Velocity;

            rootTransform.SetParent(null, true);
            rootTransform.position = worldPosition;
            shard.Velocity = worldVelocity;
            shard.FollowsNote = false;
        }

        private ShardState GetOrCreateShard()
        {
            while (_pool.Count > 0)
            {
                ShardState pooled = _pool.Dequeue();
                if (pooled != null && pooled.Root != null && pooled.Text != null)
                {
                    return pooled;
                }
            }

            return CreateShard(warm: false);
        }

        private ShardState CreateShard(bool warm)
        {
            // Mirror BombNotePatch flying-text warm path: activate offscreen first, then
            // apply font/material. Inactive bare TextMeshPro + fontMaterial access NREs.
            GameObject root = null;
            string stage = "alloc";
            try
            {
                root = new GameObject("BeatSurgeon_RaidTextShard");
                UnityEngine.Object.DontDestroyOnLoad(root);
                root.transform.position = new Vector3(0f, -2048f, 0f);
                root.SetActive(true);

                stage = "AddComponent.TextMeshPro";
                TextMeshPro text = root.AddComponent<TextMeshPro>();

                stage = "AddComponent.LookAtCamera";
                LookAtCamera billboard = root.AddComponent<LookAtCamera>();

                stage = "ResolveTekoFont";
                TMP_FontAsset tekoFont = ResolveTekoFont();

                stage = "TryApplySelectedBombFont";
                if (!FontBundleLoader.TryApplySelectedBombFont(text, tekoFont, cloneMaterial: true))
                {
                    stage = "TekoFallback";
                    if (!TrySetFontSafe(text, tekoFont))
                    {
                        _log.Warn(
                            "RaidFountainNoteManager: failed to apply bomb/Teko font to raid text shard"
                            + " | teko="
                            + (tekoFont != null ? tekoFont.name : "null"));
                        UnityEngine.Object.Destroy(root);
                        return null;
                    }

                    _log.Warn("RaidFountainNoteManager: bomb font apply failed; using Teko fallback for raid shards.");
                }

                stage = "ValidateMaterial";
                if (text.font == null || !TryGetUsableFontMaterial(text, out _))
                {
                    _log.Warn("RaidFountainNoteManager: raid text font or material not ready.");
                    UnityEngine.Object.Destroy(root);
                    return null;
                }

                stage = "ConfigureText";
                text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.enableAutoSizing = false;
                text.fontSize = OutlineFontSize;
                text.color = Color.white;
                text.text = string.Empty;
                if (text.rectTransform != null)
                {
                    text.rectTransform.sizeDelta = new Vector2(OutlineTextWidth, 0.6f);
                }

                stage = "Deactivate";
                root.SetActive(false);

                return new ShardState
                {
                    Root = root,
                    Text = text,
                    Billboard = billboard,
                    Active = false,
                    Lifetime = CutBurstLifetimeSeconds,
                    BaseColor = Color.white,
                    IsOutline = true
                };
            }
            catch (Exception ex)
            {
                _log.Warn(
                    "RaidFountainNoteManager: CreateShard failed at stage="
                    + stage
                    + " warm="
                    + warm
                    + " | "
                    + ex);
                if (root != null)
                {
                    UnityEngine.Object.Destroy(root);
                }

                return null;
            }
        }

        private static bool TrySetFontSafe(TMP_Text textComponent, TMP_FontAsset font)
        {
            if (textComponent == null || font == null)
            {
                return false;
            }

            try
            {
                textComponent.font = font;
                return textComponent.font != null;
            }
            catch (Exception ex)
            {
                _log.Warn("RaidFountainNoteManager: TrySetFontSafe failed: " + ex.Message);
                return false;
            }
        }

        private static bool TryGetUsableFontMaterial(TMP_Text textComponent, out Material material)
        {
            material = null;
            if (textComponent == null)
            {
                return false;
            }

            try
            {
                material = textComponent.fontSharedMaterial;
                if (material != null)
                {
                    return true;
                }
            }
            catch
            {
                // TMP may throw while material graph is incomplete.
            }

            try
            {
                // fontMaterial getter can NullReference on freshly created world-space TMP.
                material = textComponent.fontMaterial;
                return material != null;
            }
            catch (Exception ex)
            {
                _log.Warn("RaidFountainNoteManager: fontMaterial access failed: " + ex.Message);
                material = null;
                return false;
            }
        }

        private static TMP_FontAsset ResolveTekoFont()
        {
            if (_cachedTekoFont != null)
            {
                return _cachedTekoFont;
            }

            try
            {
                TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts == null || fonts.Length == 0)
                {
                    return null;
                }

                for (int i = 0; i < fonts.Length; i++)
                {
                    TMP_FontAsset font = fonts[i];
                    if (font == null || string.IsNullOrEmpty(font.name))
                    {
                        continue;
                    }

                    // Prefer base Teko assets over SiraLocalizer supplement atlases.
                    if (string.Equals(font.name, "Teko-Medium SDF", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(font.name, "Teko-Bold SDF", StringComparison.OrdinalIgnoreCase))
                    {
                        _cachedTekoFont = font;
                        return _cachedTekoFont;
                    }
                }

                for (int i = 0; i < fonts.Length; i++)
                {
                    TMP_FontAsset font = fonts[i];
                    if (font == null || string.IsNullOrEmpty(font.name))
                    {
                        continue;
                    }

                    if (font.name.IndexOf("Teko", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _cachedTekoFont = font;
                        return _cachedTekoFont;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn($"RaidFountainNoteManager: Teko font lookup failed: {ex.Message}");
            }

            return null;
        }

        private void RecycleShard(ShardState shard)
        {
            if (shard == null)
            {
                return;
            }

            shard.Active = false;
            shard.FollowsNote = false;
            shard.IsOutline = false;
            shard.Velocity = Vector3.zero;
            shard.Elapsed = 0f;
            if (shard.Root != null)
            {
                shard.Root.transform.SetParent(null, false);
                shard.Root.SetActive(false);
            }

            if (_pool.Count < MaxPoolSize)
            {
                _pool.Enqueue(shard);
            }
            else if (shard.Root != null)
            {
                UnityEngine.Object.Destroy(shard.Root);
            }
        }

        private void ClearTransientGameplayState(string reason)
        {
            _activeEntries.Clear();
            _pendingEntries.Clear();

            for (int index = 0; index < _liveShards.Count; index++)
            {
                RecycleShard(_liveShards[index]);
            }

            _liveShards.Clear();

            while (_pool.Count > 0)
            {
                ShardState pooled = _pool.Dequeue();
                if (pooled != null && pooled.Root != null)
                {
                    UnityEngine.Object.Destroy(pooled.Root);
                }
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                _log.Info("Cleared raid fountain state | reason=" + reason);
            }
        }

        private bool IsInMap()
        {
            if (_gameplayManager == null)
            {
                _gameplayManager = GameplayManager.GetInstance();
            }

            return _gameplayManager != null && _gameplayManager.IsInMap;
        }

        private static string NormalizeRaiderName(string raiderName)
        {
            return string.IsNullOrWhiteSpace(raiderName) ? "Unknown" : raiderName.Trim();
        }

        private static Vector3 FibonacciDirection(int index, int count)
        {
            float n = Mathf.Max(1, count);
            float y = 1f - ((index + 0.5f) * 2f / n);
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - (y * y)));
            float theta = (Mathf.PI * (1f + Mathf.Sqrt(5f))) * index;
            return new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }
        }
    }
}
