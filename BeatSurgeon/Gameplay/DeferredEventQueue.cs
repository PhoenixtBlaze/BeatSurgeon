using System;
using System.Collections.Generic;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Gameplay
{
    internal enum EventKind
    {
        Follow,
        Bits,
        Subscription,
        Raid
    }

    /// <summary>
    /// Value type describing a single effect that arrived while the player was outside gameplay
    /// or while ranked gameplay/ranked-checking was active, and must be fired once an unranked
    /// gameplay scene is available.
    /// </summary>
    internal struct DeferredEventEntry
    {
        internal EventKind EventKind;
        internal string DisplayName;
        internal int BitAmount;          // 0 for Follow / Subscription entries; raid note count for Raid
        internal DateTime QueuedAtUtc;
        internal int RetryCount;         // starts at 0; max one retry
        internal string TierLabel;       // e.g. "Tier 1", "Prime"  (Subscription only)
        internal int CumulativeMonths;   // total months subbed         (Subscription only)
        internal int DurationMonths;     // months purchased this event (Subscription only)
        internal int GiftCount;          // gifted subscription count   (Subscription only)
        internal string EventSubKind;    // "sub" / "resub" / "giftsub" / "subend" (Subscription only)

        internal DeferredEventEntry(EventKind eventKind, string displayName, int bitAmount, DateTime queuedAtUtc, int retryCount = 0)
        {
            EventKind = eventKind;
            DisplayName = displayName;
            BitAmount = bitAmount;
            QueuedAtUtc = queuedAtUtc;
            RetryCount = retryCount;
            TierLabel = string.Empty;
            CumulativeMonths = 0;
            DurationMonths = 0;
            GiftCount = 0;
            EventSubKind = string.Empty;
        }

        internal DeferredEventEntry(EventKind eventKind, string displayName, DateTime queuedAtUtc, string tierLabel, int cumulativeMonths, int giftCount, string eventSubKind, int durationMonths = 0, int retryCount = 0)
        {
            EventKind = eventKind;
            DisplayName = displayName;
            BitAmount = 0;
            QueuedAtUtc = queuedAtUtc;
            RetryCount = retryCount;
            TierLabel = tierLabel ?? string.Empty;
            CumulativeMonths = cumulativeMonths;
            DurationMonths = durationMonths;
            GiftCount = giftCount;
            EventSubKind = eventSubKind ?? string.Empty;
        }
    }

    /// <summary>
    /// Session-scoped, thread-safe queue for automatic EventSub/IRC effects that arrived while the
    /// player was not in a gameplay scene or while ranked gameplay was blocking visuals.
    /// Caps at <see cref="MaxPendingEntries"/> (oldest dropped). Not persisted across quit.
    /// Chat commands and channel-point redemptions are intentionally never queued here.
    /// </summary>
    internal sealed class DeferredEventQueue
    {
        internal const int MaxPendingEntries = 10;

        private static readonly LogUtil _log = LogUtil.GetLogger("DeferredEventQueue");
        private readonly object _gate = new object();
        private readonly Queue<DeferredEventEntry> _queue = new Queue<DeferredEventEntry>(MaxPendingEntries);

        /// <summary>
        /// Live singleton set in ctor so consumers can find the queue even if Zenject field inject
        /// into GameplayManager failed (FromMethod does not Inject).
        /// </summary>
        internal static DeferredEventQueue Instance { get; private set; }

        /// <summary>
        /// Invoked after an entry is enqueued (any thread). Used by GameplayManager to reopen flush
        /// after an empty drain marked the scene as completed.
        /// </summary>
        internal Action OnEntryEnqueued;

        public DeferredEventQueue()
        {
            Instance = this;
        }

        internal bool HasPendingEntries
        {
            get
            {
                lock (_gate)
                {
                    return _queue.Count > 0;
                }
            }
        }

        internal int Count
        {
            get
            {
                lock (_gate)
                {
                    return _queue.Count;
                }
            }
        }

        /// <summary>
        /// Enqueue a deferred effect. Safe from any thread. Drops the oldest entry when full.
        /// </summary>
        internal void Enqueue(DeferredEventEntry entry)
        {
            DeferredEventEntry? dropped = null;

            lock (_gate)
            {
                while (_queue.Count >= MaxPendingEntries)
                {
                    dropped = _queue.Dequeue();
                }

                _queue.Enqueue(entry);
            }

            if (dropped.HasValue)
            {
                DeferredEventEntry lost = dropped.Value;
                _log.Warn(
                    "Deferred queue full (max "
                    + MaxPendingEntries
                    + "); dropped oldest "
                    + lost.EventKind
                    + " for "
                    + (string.IsNullOrWhiteSpace(lost.DisplayName) ? "Unknown" : lost.DisplayName)
                    + ".");
            }

            NotifyEntryEnqueued();
        }

        /// <summary>
        /// Re-enqueue without applying the cap (used for in-flight flush retries / mid-flush restore).
        /// </summary>
        internal void EnqueuePreserve(DeferredEventEntry entry)
        {
            lock (_gate)
            {
                _queue.Enqueue(entry);
            }

            NotifyEntryEnqueued();
        }

        /// <summary>
        /// Dequeue all pending entries into <paramref name="buffer"/>.
        /// Must be called from the Unity main thread.
        /// </summary>
        internal void DrainTo(List<DeferredEventEntry> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            lock (_gate)
            {
                while (_queue.Count > 0)
                {
                    buffer.Add(_queue.Dequeue());
                }
            }
        }

        private void NotifyEntryEnqueued()
        {
            Action handler = OnEntryEnqueued;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception ex)
            {
                _log.Warn("OnEntryEnqueued handler failed: " + ex.Message);
            }
        }
    }
}
