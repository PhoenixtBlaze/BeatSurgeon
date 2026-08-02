using System;
using System.Collections.Generic;
using BeatSurgeon.Utils;
using TMPro;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Queues the next N map notes for a raid effect using authored TMP emitters from
    /// surgeoneffects (<c>RaidFountainOutline</c> / <c>RaidFountainCutBurst</c>), with a
    /// pure-C# fallback if the bundle children are missing:
    /// tiny RGB name text outlining each cube, then on cut a sparse RGB
    /// name sphere that expands away from the note-approach path.
    /// Yields to glitter and trail-cube claims.
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
            internal Vector3 FlyStart;
            internal Vector3 FlyTarget;
            /// <summary>Slow post-expand end position for cut bursts.</summary>
            internal Vector3 FlyDriftTarget;
            internal float Elapsed;
            internal float Lifetime;
            internal float HueOffset;
            internal Color BaseColor;
            internal bool Active;
            /// <summary>True while the shard rides the note in local space.</summary>
            internal bool FollowsNote;
            /// <summary>Tiny shell text around the cube (vs large cut-burst shards).</summary>
            internal bool IsOutline;
            /// <summary>Cut shards expand on a sphere biased off the note highway.</summary>
            internal bool IsCutBurst;
            /// <summary>Menu Effects-tab preview; survives map-exit cleanup.</summary>
            internal bool IsMenuPreview;
        }

        private static readonly LogUtil _log = LogUtil.GetLogger("RaidFountainNoteManager");
        private static TMP_FontAsset _cachedTekoFont;

        internal const int MaxNotesPerRaid = 100;
        internal const int RecommendedWarmPoolSize = 72;
        private const int MaxPendingEntries = 256;
        private const int MaxActiveNotes = 64;
        private const int OutlineShardsPerNote = 36;
        private const int CutBurstShards = 6;
        private const int MaxPoolSize = 384;
        // Phase 1: snap to full sphere radius with no fade. Phase 2: short slow drift while fading.
        private const float CutBurstExpandSeconds = 0.35f;
        private const float CutBurstDriftSeconds = 1.50f;
        private const float CutBurstLifetimeSeconds = CutBurstExpandSeconds + CutBurstDriftSeconds;
        private const float CutBurstSphereRadiusMin = 0.2f;
        private const float CutBurstSphereRadiusMax = 2.0f;
        private const float CutBurstSphereRadiusDriftMax = 2.75f;
        private const float OutlineShellRadius = 0.38f;
        private const float SpectrumCycleSeconds = 0.5f;
        private const float OutlineFontSize = 0.18f;
        private const float CutBurstFontSize = 1.35f;
        private const float OutlineTextWidth = 1.2f;
        private const float CutBurstTextWidth = 6.5f;

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

            if (!FontBundleLoader.IsBombFontReady
                && SurgeonEffectsBundleService.GetRaidFountainTextTemplate(isOutline: true) == null)
            {
                if (!_fontWarned)
                {
                    _fontWarned = true;
                    _log.Warn("RaidFountainNoteManager: bomb font not ready and raid text bundle templates unavailable for warm pool.");
                }

                return false;
            }

            while (_pool.Count < desiredShardCount)
            {
                ShardState shard = CreateShard(warm: true, isOutline: true);
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

            if (_pendingEntries.Count == 0 && _activeEntries.Count == 0 && !HasNonPreviewLiveShards())
            {
                MultiplayerEffectPublisher.NotifyEffectEnded("raid");
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
            if (!IsInMap())
            {
                if (_pendingEntries.Count > 0 || _activeEntries.Count > 0 || HasNonPreviewLiveShards())
                {
                    ClearGameplayStatePreserveMenuPreview("SceneChanged");
                }

                TickLiveShards();
                return;
            }

            TickLiveShards();
        }

        /// <summary>
        /// Menu Effects-tab preview of the selected raid cut effect at <paramref name="origin"/>.
        /// Uses text "Preview" and does not require an active map.
        /// </summary>
        internal void SpawnCutExplosionPreview(Vector3 origin)
        {
            ClearMenuPreviewShards();

            string selection = RaidCutEffectSettings.GetSelectedOption();
            switch (selection)
            {
                case RaidCutEffectSettings.DefaultOption:
                default:
                    SpawnCutExplosion(origin, "Preview", layer: 0, isMenuPreview: true);
                    break;
            }
        }

        internal void ClearMenuPreviewShards()
        {
            if (_liveShards.Count == 0)
            {
                return;
            }

            _shardScratch.Clear();
            for (int index = 0; index < _liveShards.Count; index++)
            {
                ShardState shard = _liveShards[index];
                if (shard == null)
                {
                    continue;
                }

                if (shard.IsMenuPreview)
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

        /// <summary>
        /// Re-apply the selected Surgeon Font to pooled, live outline, and cut-burst raid text.
        /// </summary>
        internal void ApplySelectedSurgeonFontToAllRaidText()
        {
            TMP_FontAsset tekoFont = ResolveTekoFont();
            ApplySelectedFontToShardList(_liveShards, tekoFont);

            // Queue contents can't be enumerated without dequeue — snapshot via array.
            if (_pool.Count == 0)
            {
                return;
            }

            ShardState[] pooled = _pool.ToArray();
            _pool.Clear();
            for (int index = 0; index < pooled.Length; index++)
            {
                ShardState shard = pooled[index];
                if (shard != null && shard.Text != null)
                {
                    ApplySelectedFontToText(shard.Text, tekoFont);
                }

                if (shard != null)
                {
                    _pool.Enqueue(shard);
                }
            }
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
                float hue = Mathf.Repeat(
                    (shard.HueOffset + shard.Elapsed) / Mathf.Max(0.01f, SpectrumCycleSeconds),
                    1f);
                Color color = Color.HSVToRGB(hue, 1f, 1f);

                if (shard.IsOutline && shard.FollowsNote)
                {
                    // Fixed shell around the cube — only recolor while attached.
                    color.a = shard.BaseColor.a;
                    shard.Text.color = color;
                    _shardScratch.Add(shard);
                    continue;
                }

                if (shard.IsCutBurst)
                {
                    TickCutBurstShard(shard, color);
                    if (shard.Active)
                    {
                        _shardScratch.Add(shard);
                    }

                    continue;
                }

                float t = Mathf.Clamp01(shard.Elapsed / Mathf.Max(0.01f, shard.Lifetime));
                if (shard.FollowsNote)
                {
                    shard.Root.transform.localPosition += shard.Velocity * dt;
                    shard.Velocity *= 0.985f;
                }
                else
                {
                    shard.Root.transform.position += shard.Velocity * dt;
                    shard.Velocity *= 0.985f;
                }

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

            if (_pendingEntries.Count == 0 && _activeEntries.Count == 0 && !HasNonPreviewLiveShards())
            {
                MultiplayerEffectPublisher.NotifyEffectEnded("raid");
            }
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
                ShardState shard = GetOrCreateShard(isOutline: true);
                if (shard == null)
                {
                    break;
                }

                Vector3 direction = FibonacciDirection(index, OutlineShardsPerNote);
                shard.Velocity = Vector3.zero;
                shard.Elapsed = 0f;
                shard.HueOffset = index * (SpectrumCycleSeconds / OutlineShardsPerNote);
                shard.Lifetime = float.MaxValue;
                shard.BaseColor = Color.white;
                shard.Active = true;
                shard.FollowsNote = true;
                shard.IsOutline = true;
                shard.IsCutBurst = false;
                shard.IsMenuPreview = false;

                shard.Root.transform.SetParent(noteParent, false);
                shard.Root.transform.localPosition = direction * OutlineShellRadius;
                shard.Root.transform.localRotation = Quaternion.identity;
                shard.Root.transform.localScale = Vector3.one;
                SetLayerRecursively(shard.Root, layer);

                ApplySelectedFontToText(shard.Text, ResolveTekoFont());

                shard.Text.text = raiderName;
                shard.Text.fontSize = OutlineFontSize;
                if (shard.Text.rectTransform != null)
                {
                    shard.Text.rectTransform.sizeDelta = new Vector2(OutlineTextWidth, 0.6f);
                }

                shard.Text.color = Color.HSVToRGB(
                    Mathf.Repeat(shard.HueOffset / SpectrumCycleSeconds, 1f),
                    1f,
                    1f);
                shard.Root.SetActive(true);
                _liveShards.Add(shard);
                ownedShards?.Add(shard);
            }
        }

        private void TickCutBurstShard(ShardState shard, Color color)
        {
            float expandSeconds = Mathf.Max(0.01f, CutBurstExpandSeconds);
            float driftSeconds = Mathf.Max(0.01f, CutBurstDriftSeconds);

            if (shard.Elapsed < expandSeconds)
            {
                // Fast ease-out to full radius; stay fully opaque.
                float expandT = Mathf.Clamp01(shard.Elapsed / expandSeconds);
                float eased = 1f - ((1f - expandT) * (1f - expandT));
                shard.Root.transform.position = Vector3.Lerp(shard.FlyStart, shard.FlyTarget, eased);
                shard.Root.transform.localScale = Vector3.one;
                color.a = shard.BaseColor.a;
                shard.Text.color = color;
                return;
            }

            float driftElapsed = shard.Elapsed - expandSeconds;
            float driftT = Mathf.Clamp01(driftElapsed / driftSeconds);
            // Linear, slow crawl past full radius while fading out.
            shard.Root.transform.position = Vector3.Lerp(shard.FlyTarget, shard.FlyDriftTarget, driftT);
            float scale = Mathf.Lerp(1f, 0.7f, driftT);
            shard.Root.transform.localScale = Vector3.one * scale;
            color.a = shard.BaseColor.a * (1f - driftT);
            shard.Text.color = color;

            if (driftT >= 1f)
            {
                RecycleShard(shard);
            }
        }

        private void SpawnCutExplosion(Vector3 origin, string raiderName, int layer)
        {
            SpawnCutExplosion(origin, raiderName, layer, isMenuPreview: false);
        }

        private void SpawnCutExplosion(Vector3 origin, string raiderName, int layer, bool isMenuPreview)
        {
            Vector3 sphereOrigin = origin + (Vector3.up * 0.35f);

            for (int index = 0; index < CutBurstShards; index++)
            {
                ShardState shard = GetOrCreateShard(isOutline: false);
                if (shard == null)
                {
                    break;
                }

                Vector3 direction = BiasAwayFromNoteApproach(FibonacciDirection(index, CutBurstShards));
                Vector3 flyStart = sphereOrigin + (direction * CutBurstSphereRadiusMin);
                Vector3 flyTarget = sphereOrigin + (direction * CutBurstSphereRadiusMax);
                // Phase 2: prefer flying toward the follower canvas Start (same anchor bits/fmsg use).
                // Fall back to a short radial drift when the canvas template is unavailable.
                Vector3 flyDriftTarget = sphereOrigin + (direction * CutBurstSphereRadiusDriftMax);
                if (SurgeonEffectsBundleService.TryResolveFollowerCanvasStartWorldPosition(out Vector3 canvasStart)
                    && IsFiniteVector(canvasStart)
                    && (canvasStart - flyTarget).sqrMagnitude > 0.04f)
                {
                    flyDriftTarget = canvasStart;
                }

                shard.Velocity = Vector3.zero;
                shard.FlyStart = flyStart;
                shard.FlyTarget = flyTarget;
                shard.FlyDriftTarget = flyDriftTarget;
                shard.Elapsed = 0f;
                shard.HueOffset = index * (SpectrumCycleSeconds / CutBurstShards);
                shard.Lifetime = CutBurstLifetimeSeconds;
                shard.BaseColor = Color.white;
                shard.Active = true;
                shard.FollowsNote = false;
                shard.IsOutline = false;
                shard.IsCutBurst = true;
                shard.IsMenuPreview = isMenuPreview;

                shard.Root.transform.SetParent(null, false);
                shard.Root.transform.position = flyStart;
                shard.Root.transform.rotation = Quaternion.identity;
                shard.Root.transform.localScale = Vector3.one;
                SetLayerRecursively(shard.Root, layer);

                // Ensure menu preview / live cut always matches current Surgeon Font selection.
                ApplySelectedFontToText(shard.Text, ResolveTekoFont());

                shard.Text.text = raiderName;
                shard.Text.fontSize = CutBurstFontSize;
                if (shard.Text.rectTransform != null)
                {
                    shard.Text.rectTransform.sizeDelta = new Vector2(CutBurstTextWidth, 2.5f);
                }

                shard.Text.color = Color.HSVToRGB(
                    Mathf.Repeat(shard.HueOffset / SpectrumCycleSeconds, 1f),
                    1f,
                    1f);
                shard.Root.SetActive(true);
                _liveShards.Add(shard);
            }
        }

        /// <summary>
        /// Fold the forward hemisphere (toward upcoming notes / camera look) into
        /// sides, up, and behind so cut text does not sit in front of spawning cubes.
        /// </summary>
        private static Vector3 BiasAwayFromNoteApproach(Vector3 direction)
        {
            Vector3 approach = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
            if (approach.sqrMagnitude < 0.0001f)
            {
                approach = Vector3.forward;
            }

            approach.Normalize();
            direction.Normalize();

            float intoNotes = Vector3.Dot(direction, approach);
            if (intoNotes > 0f)
            {
                // Strip / reverse the component pointing into the note highway.
                direction -= approach * (intoNotes * 1.45f);
            }

            // Prefer a bit of lift so the sphere clears the play lane.
            direction += Vector3.up * 0.25f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                Vector3 side = Vector3.Cross(approach, Vector3.up);
                if (side.sqrMagnitude < 0.0001f)
                {
                    side = Vector3.right;
                }

                direction = (-approach * 0.35f) + (side.normalized * 0.65f) + (Vector3.up * 0.7f);
            }

            return direction.normalized;
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

        private ShardState GetOrCreateShard(bool isOutline)
        {
            while (_pool.Count > 0)
            {
                ShardState pooled = _pool.Dequeue();
                if (pooled != null && pooled.Root != null && pooled.Text != null)
                {
                    return pooled;
                }
            }

            return CreateShard(warm: false, isOutline: isOutline);
        }

        private ShardState CreateShard(bool warm, bool isOutline)
        {
            ShardState fromBundle = TryCreateShardFromBundle(isOutline);
            if (fromBundle != null)
            {
                return fromBundle;
            }

            // Fallback: pure-C# path when surgeoneffects is missing the raid TMP children.
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
                ConfigureRaidTextDefaults(text, isOutline);

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
                    IsOutline = isOutline
                };
            }
            catch (Exception ex)
            {
                _log.Warn(
                    "RaidFountainNoteManager: CreateShard failed at stage="
                    + stage
                    + " warm="
                    + warm
                    + " isOutline="
                    + isOutline
                    + " | "
                    + ex);
                if (root != null)
                {
                    UnityEngine.Object.Destroy(root);
                }

                return null;
            }
        }

        private ShardState TryCreateShardFromBundle(bool isOutline)
        {
            string stage = "GetRaidFountainTextTemplate";
            GameObject root = null;
            try
            {
                GameObject template = SurgeonEffectsBundleService.GetRaidFountainTextTemplate(isOutline);
                if (template == null)
                {
                    return null;
                }

                stage = "Instantiate";
                root = UnityEngine.Object.Instantiate(template);
                UnityEngine.Object.DontDestroyOnLoad(root);
                root.transform.SetParent(null, false);
                root.transform.position = new Vector3(0f, -2048f, 0f);
                root.name = isOutline
                    ? "BeatSurgeon_RaidFountainOutline"
                    : "BeatSurgeon_RaidFountainCutBurst";
                root.SetActive(true);

                stage = "GetComponent.TextMeshPro";
                TextMeshPro text = root.GetComponent<TextMeshPro>()
                    ?? root.GetComponentInChildren<TextMeshPro>(true);
                if (text == null)
                {
                    _log.Warn("RaidFountainNoteManager: bundle raid text template missing TextMeshPro.");
                    UnityEngine.Object.Destroy(root);
                    return null;
                }

                stage = "Ensure.LookAtCamera";
                LookAtCamera billboard = root.GetComponent<LookAtCamera>();
                if (billboard == null)
                {
                    billboard = root.AddComponent<LookAtCamera>();
                }

                // Prefer authored bundle font/material; optionally overlay selected bomb font.
                stage = "TryApplySelectedBombFont";
                TMP_FontAsset tekoFont = ResolveTekoFont();
                if (!FontBundleLoader.TryApplySelectedBombFont(text, tekoFont, cloneMaterial: true))
                {
                    if (text.font == null && !TrySetFontSafe(text, tekoFont))
                    {
                        _log.Warn("RaidFountainNoteManager: bundle raid shard has no usable font.");
                        UnityEngine.Object.Destroy(root);
                        return null;
                    }
                }

                stage = "ValidateMaterial";
                if (text.font == null || !TryGetUsableFontMaterial(text, out _))
                {
                    _log.Warn("RaidFountainNoteManager: bundle raid text font or material not ready.");
                    UnityEngine.Object.Destroy(root);
                    return null;
                }

                stage = "ConfigureText";
                ConfigureRaidTextDefaults(text, isOutline);

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
                    IsOutline = isOutline
                };
            }
            catch (Exception ex)
            {
                _log.Warn(
                    "RaidFountainNoteManager: TryCreateShardFromBundle failed at stage="
                    + stage
                    + " isOutline="
                    + isOutline
                    + " | "
                    + ex);
                if (root != null)
                {
                    UnityEngine.Object.Destroy(root);
                }

                return null;
            }
        }

        private static void ConfigureRaidTextDefaults(TextMeshPro text, bool isOutline)
        {
            if (text == null)
            {
                return;
            }

            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            text.fontSize = isOutline ? OutlineFontSize : CutBurstFontSize;
            text.color = Color.white;
            text.text = string.Empty;
            if (text.rectTransform != null)
            {
                text.rectTransform.sizeDelta = isOutline
                    ? new Vector2(OutlineTextWidth, 0.6f)
                    : new Vector2(CutBurstTextWidth, 2.5f);
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
            shard.IsCutBurst = false;
            shard.IsMenuPreview = false;
            shard.Velocity = Vector3.zero;
            shard.FlyStart = Vector3.zero;
            shard.FlyTarget = Vector3.zero;
            shard.FlyDriftTarget = Vector3.zero;
            shard.Elapsed = 0f;
            shard.HueOffset = 0f;
            if (shard.Root != null)
            {
                shard.Root.transform.SetParent(null, false);
                shard.Root.transform.localScale = Vector3.one;
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

        private bool HasNonPreviewLiveShards()
        {
            for (int index = 0; index < _liveShards.Count; index++)
            {
                ShardState shard = _liveShards[index];
                if (shard != null && shard.Active && !shard.IsMenuPreview)
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearGameplayStatePreserveMenuPreview(string reason)
        {
            _activeEntries.Clear();
            _pendingEntries.Clear();

            _shardScratch.Clear();
            for (int index = 0; index < _liveShards.Count; index++)
            {
                ShardState shard = _liveShards[index];
                if (shard == null)
                {
                    continue;
                }

                if (shard.IsMenuPreview)
                {
                    _shardScratch.Add(shard);
                }
                else
                {
                    RecycleShard(shard);
                }
            }

            _liveShards.Clear();
            _liveShards.AddRange(_shardScratch);

            if (!string.IsNullOrWhiteSpace(reason))
            {
                _log.Info("Cleared raid fountain gameplay state (kept menu preview) | reason=" + reason);
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

            MultiplayerEffectPublisher.NotifyEffectEnded("raid");

            if (!string.IsNullOrWhiteSpace(reason))
            {
                _log.Info("Cleared raid fountain state | reason=" + reason);
            }
        }

        private static void ApplySelectedFontToShardList(List<ShardState> shards, TMP_FontAsset tekoFont)
        {
            if (shards == null)
            {
                return;
            }

            for (int index = 0; index < shards.Count; index++)
            {
                ShardState shard = shards[index];
                if (shard?.Text == null)
                {
                    continue;
                }

                ApplySelectedFontToText(shard.Text, tekoFont);
            }
        }

        private static void ApplySelectedFontToText(TextMeshPro text, TMP_FontAsset tekoFont)
        {
            if (text == null)
            {
                return;
            }

            if (!FontBundleLoader.TryApplySelectedBombFont(text, tekoFont, cloneMaterial: true))
            {
                TrySetFontSafe(text, tekoFont);
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

        private static bool IsFiniteVector(Vector3 value)
        {
            return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z)
                || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
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
