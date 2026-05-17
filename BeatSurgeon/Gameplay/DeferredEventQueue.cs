using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeatSurgeon.Gameplay
{
    internal enum EventKind
    {
        Follow,
        Bits,
        Subscription
    }

    /// <summary>
    /// Value type describing a single effect that arrived while the player was outside gameplay
    /// and must be fired the next time a gameplay scene loads.
    /// </summary>
    internal struct DeferredEventEntry
    {
        internal EventKind EventKind;
        internal string DisplayName;
        internal int BitAmount;          // 0 for Follow / Subscription entries
        internal DateTime QueuedAtUtc;
        internal int RetryCount;         // starts at 0; max one retry
        internal string TierLabel;       // e.g. "Tier 1", "Prime"  (Subscription only)
        internal int CumulativeMonths;   // total months subbed         (Subscription only)
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
            GiftCount = 0;
            EventSubKind = string.Empty;
        }

        internal DeferredEventEntry(EventKind eventKind, string displayName, DateTime queuedAtUtc, string tierLabel, int cumulativeMonths, int giftCount, string eventSubKind, int retryCount = 0)
        {
            EventKind = eventKind;
            DisplayName = displayName;
            BitAmount = 0;
            QueuedAtUtc = queuedAtUtc;
            RetryCount = retryCount;
            TierLabel = tierLabel ?? string.Empty;
            CumulativeMonths = cumulativeMonths;
            GiftCount = giftCount;
            EventSubKind = eventSubKind ?? string.Empty;
        }
    }

    /// <summary>
    /// Session-scoped, thread-safe queue for effects that arrived while the player was not in a
    /// gameplay scene. Enqueue is safe to call from any thread (including the EventSub WebSocket
    /// background thread). DrainTo is called on the Unity main thread at scene-start.
    ///
    /// The queue is intentionally NOT persisted to disk. Events that are pending when the player
    /// quits Beat Saber are silently dropped.
    /// </summary>
    internal sealed class DeferredEventQueue
    {
        private readonly ConcurrentQueue<DeferredEventEntry> _queue =
            new ConcurrentQueue<DeferredEventEntry>();

        /// <summary>
        /// Enqueue a deferred effect entry. Safe to call from any thread.
        /// </summary>
        internal void Enqueue(DeferredEventEntry entry)
        {
            _queue.Enqueue(entry);
        }

        /// <summary>
        /// Dequeue all pending entries into <paramref name="buffer"/>.
        /// Must be called from the Unity main thread.
        /// </summary>
        internal void DrainTo(List<DeferredEventEntry> buffer)
        {
            while (_queue.TryDequeue(out DeferredEventEntry entry))
            {
                buffer.Add(entry);
            }
        }
    }
}
