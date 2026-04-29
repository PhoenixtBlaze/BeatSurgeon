using System;
using System.Collections.Generic;
using System.Linq;
using BeatSurgeon.Utils;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    internal sealed class TestEffectManager : MonoBehaviour
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TestEffectManager");
        private static readonly int[] SupportedBitDenominations = { 10000, 5000, 1000, 100, 1 };

        private class QueuedEffectEntry
        {
            internal QueuedEffectEntry(int denomination, string requesterName)
            {
                Denomination = denomination;
                RequesterName = NormalizeRequesterName(requesterName);
            }

            internal int Denomination { get; private set; }
            internal string RequesterName { get; private set; }
        }

        private sealed class ActiveEffectEntry : QueuedEffectEntry
        {
            internal ActiveEffectEntry(GameNoteController controller, int denomination, string requesterName)
                : base(denomination, requesterName)
            {
                Controller = controller;
            }

            internal GameNoteController Controller { get; private set; }
        }

        internal const int MaxRequestedBits = 1000000;
        private const int MaxPendingEffects = 1024;

        private static TestEffectManager _instance;
        private static GameObject _go;

        private readonly LinkedList<QueuedEffectEntry> _pendingEffects = new LinkedList<QueuedEffectEntry>();
        private readonly Dictionary<GameNoteController, ActiveEffectEntry> _activeEffects = new Dictionary<GameNoteController, ActiveEffectEntry>();
        private GameplayManager _gameplayManager;

        public static TestEffectManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeon_TestEffectManager_GO");
                    UnityEngine.Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<TestEffectManager>();
                }

                return _instance;
            }
        }

        public int PendingCount => _pendingEffects.Count;
        public int ActiveCount => _activeEffects.Count;
        public bool HasPendingEffects => _pendingEffects.Count > 0;

        public bool QueueBits(int requestedBits, string requesterName, out int queuedEffects, out int queuedBits, out string breakdown)
        {
            queuedEffects = 0;
            queuedBits = 0;
            breakdown = string.Empty;

            if (!IsInMap())
            {
                _log.Warn("QueueBits ignored because gameplay is not active.");
                return false;
            }

            int clampedBits = Mathf.Clamp(requestedBits, 1, MaxRequestedBits);
            List<int> pendingEmitters = BuildEmitterSequence(clampedBits, out int selectedDenomination);
            if (pendingEmitters.Count == 0)
            {
                return false;
            }

            int requestedEffectCount = pendingEmitters.Count;

            int availableSlots = Mathf.Max(0, MaxPendingEffects - (_pendingEffects.Count + _activeEffects.Count));
            if (availableSlots <= 0)
            {
                _log.Warn("Pending test effect queue is full.");
                return false;
            }

            bool wasTruncated = pendingEmitters.Count > availableSlots;
            if (pendingEmitters.Count > availableSlots)
            {
                pendingEmitters = pendingEmitters.Take(availableSlots).ToList();
            }

            string normalizedRequesterName = NormalizeRequesterName(requesterName);
            foreach (int denomination in pendingEmitters)
            {
                _pendingEffects.AddLast(new QueuedEffectEntry(denomination, normalizedRequesterName));
                queuedEffects++;
                queuedBits += denomination;
            }

            breakdown = BuildBreakdown(pendingEmitters);

            _log.Info(
                "Queued glitter effects for "
                + normalizedRequesterName
                + " | requestedBits="
                + requestedBits
                + " queuedBits="
                + queuedBits
                + " requestedEffects="
                + requestedEffectCount
                + " queuedEffects="
                + queuedEffects
                + " pending="
                + _pendingEffects.Count
                + " active="
                + _activeEffects.Count
                + " denomination="
                + selectedDenomination
                + " truncated="
                + wasTruncated
                + " breakdown="
                + breakdown
                + " preview="
                + BuildSequencePreview(pendingEmitters));

            return queuedEffects > 0;
        }

        internal bool TryMarkNextEffect(GameNoteController controller, NoteData noteData, out int denomination)
        {
            denomination = 0;

            if (controller == null)
            {
                return false;
            }

            if (!BombManager.IsEligibleBombNote(noteData))
            {
                return false;
            }

            if (_activeEffects.ContainsKey(controller))
            {
                return false;
            }

            LinkedListNode<QueuedEffectEntry> nextPending = _pendingEffects.First;
            if (nextPending == null)
            {
                return false;
            }

            if (BombManager.IsBombWindowActive && BombManager.Instance.IsNoteMarkedAsBomb(noteData))
            {
                return false;
            }

            QueuedEffectEntry queuedEffect = nextPending.Value;
            denomination = queuedEffect.Denomination;
            _pendingEffects.RemoveFirst();
            _activeEffects[controller] = new ActiveEffectEntry(controller, denomination, queuedEffect.RequesterName);
            _log.Debug(
                "Marked glitter note denomination="
                + denomination
                + " requester="
                + queuedEffect.RequesterName
                + " pending="
                + _pendingEffects.Count
                + " active="
                + _activeEffects.Count);
            return true;
        }

        internal bool TryConsumeMarkedEffect(GameNoteController controller, out int denomination, out string requesterName)
        {
            denomination = 0;
            requesterName = "Unknown";
            if (controller == null)
            {
                return false;
            }

            if (!_activeEffects.TryGetValue(controller, out ActiveEffectEntry activeEffect))
            {
                return false;
            }

            _activeEffects.Remove(controller);
            denomination = activeEffect.Denomination;
            requesterName = activeEffect.RequesterName;
            _log.Debug(
                "Consumed glitter effect denomination="
                + denomination
                + " requester="
                + requesterName
                + " pending="
                + _pendingEffects.Count
                + " active="
                + _activeEffects.Count);
            return true;
        }

        internal bool TryRequeueMarkedEffect(GameNoteController controller, string reason, out int denomination)
        {
            denomination = 0;
            if (controller == null)
            {
                return false;
            }

            if (!_activeEffects.TryGetValue(controller, out ActiveEffectEntry activeEffect))
            {
                return false;
            }

            _activeEffects.Remove(controller);
            denomination = activeEffect.Denomination;
            _pendingEffects.AddFirst(new QueuedEffectEntry(denomination, activeEffect.RequesterName));
            _log.Debug(
                "Requeued glitter effect denomination="
                + denomination
                + " requester="
                + activeEffect.RequesterName
                + " reason="
                + (string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason)
                + " pending="
                + _pendingEffects.Count
                + " active="
                + _activeEffects.Count);
            return true;
        }

        private void Update()
        {
            if (!IsInMap() && (_pendingEffects.Count > 0 || _activeEffects.Count > 0))
            {
                ClearTransientGameplayState("SceneChanged");
            }
        }

        private void ClearTransientGameplayState(string reason)
        {
            if (_pendingEffects.Count == 0 && _activeEffects.Count == 0)
            {
                return;
            }

            foreach (var activeEffect in _activeEffects.Values)
            {
                try
                {
                    if (activeEffect?.Controller != null)
                    {
                        OutlineEmitterManager.Instance.DetachFromNote(activeEffect.Controller);
                        GlitterLoopEmitterManager.Instance.DetachFromNote(activeEffect.Controller);
                    }
                }
                catch { }
            }

            _activeEffects.Clear();
            _pendingEffects.Clear();
            _log.Info("Cleared pending glitter effects | reason=" + (string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason));
        }

        private bool IsInMap()
        {
            if (_gameplayManager == null)
            {
                _gameplayManager = GameplayManager.GetInstance();
            }

            return _gameplayManager != null && _gameplayManager.IsInMap;
        }

        private static List<int> BuildEmitterSequence(int totalBits, out int selectedDenomination)
        {
            selectedDenomination = ResolveUniformEmitterDenomination(totalBits);
            if (selectedDenomination <= 0)
            {
                return new List<int>(0);
            }

            int totalEmitters = CalculateEmitterCount(totalBits, selectedDenomination);
            var emitters = new List<int>(totalEmitters);

            for (int index = 0; index < totalEmitters; index++)
            {
                emitters.Add(selectedDenomination);
            }

            return emitters;
        }

        private static int ResolveUniformEmitterDenomination(int totalBits)
        {
            foreach (int denomination in SupportedBitDenominations)
            {
                if (totalBits >= denomination)
                {
                    return denomination;
                }
            }

            return SupportedBitDenominations[SupportedBitDenominations.Length - 1];
        }

        private static int CalculateEmitterCount(int totalBits, int denomination)
        {
            if (totalBits <= 0 || denomination <= 0)
            {
                return 0;
            }

            // New glitter scaling rule: cube count grows in 10-bit steps while the emitter tier
            // still follows the selected visual denomination bucket.
            return Mathf.Max(1, Mathf.CeilToInt(totalBits / 10f));
        }

        private static string BuildBreakdown(IReadOnlyList<int> emitters)
        {
            if (emitters == null || emitters.Count == 0)
            {
                return "none";
            }

            var pieces = new List<string>();
            foreach (int denomination in SupportedBitDenominations)
            {
                int count = emitters.Count(value => value == denomination);
                if (count > 0)
                {
                    pieces.Add(count + "x" + denomination);
                }
            }

            return string.Join(", ", pieces);
        }

        private static string BuildSequencePreview(IReadOnlyList<int> emitters)
        {
            if (emitters == null || emitters.Count == 0)
            {
                return "none";
            }

            const int previewCount = 8;
            int count = Math.Min(previewCount, emitters.Count);
            string preview = string.Join(",", emitters.Take(count).Select(value => value.ToString()));
            return emitters.Count > previewCount
                ? preview + ",..."
                : preview;
        }

        private static string NormalizeRequesterName(string requesterName)
        {
            return string.IsNullOrWhiteSpace(requesterName)
                ? "Unknown"
                : requesterName.Trim();
        }
    }
}