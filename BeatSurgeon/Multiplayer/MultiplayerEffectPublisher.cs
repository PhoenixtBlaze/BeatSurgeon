using System;
using System.Collections.Generic;
using System.Threading;

namespace BeatSurgeon
{
    /// <summary>
    /// Host-side helper that publishes gameplay effects to Multiplayer+ clients
    /// through the existing active_command relay.
    ///
    /// Every effect syncs as a one-shot (no sticky clear).
    /// At most <see cref="MaxConcurrentActiveEffects"/> distinct effect keys may be synced
    /// concurrently — including duration effects AND instant ones (glitter/raid/fmsg/smsg/
    /// subcubes/bomb). Chat and channel-point commands from every in-room player share a
    /// host FIFO: a new key waits until a slot frees, then one one-shot starts the effect
    /// on every machine. Bits, follows, and subs still run on the receiver immediately;
    /// a 4th of those is not published. Restarting an already-running key still publishes.
    /// </summary>
    internal static class MultiplayerEffectPublisher
    {
        internal const int MaxConcurrentActiveEffects = 3;

        /// <summary>Long-running gameplay modifiers (timers / until stopped).</summary>
        private static readonly HashSet<string> LongRunningEffectKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rainbow", "notecolor", "ghost", "disappear", "faster", "superfast", "slower", "flashbang"
        };

        /// <summary>Burst / queue effects that still occupy a concurrent sync slot while active.</summary>
        private static readonly HashSet<string> InstantEffectKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bomb", "glitter", "raid", "fmsg", "smsg", "subcubes"
        };

        private static readonly object _activeLock = new object();
        private static readonly List<string> _activeKeys = new List<string>();

        // Fixed for this process. The counter below restarts at 1 on every launch, and clients
        // keep recent token strings while their socket stays up. A new epoch makes a restarted
        // host's tokens distinct from the previous process, so those effects still apply.
        private static readonly string OneShotEpoch = Guid.NewGuid().ToString("N").Substring(0, 8);

        private static int _oneShotSequence;
        private static int _suppressPublishDepth;

        private const int MaxRoomQueue = 32;
        private const double ReservationTimeoutSeconds = 8;

        private sealed class RoomQueueEntry
        {
            internal string Command;
            internal string User;
            internal string Key;
        }

        private sealed class RoomReservation
        {
            internal string Token;
            internal string Key;
            internal bool AddedSlot;
            internal bool Applied;
            internal bool EchoReceived;
            internal DateTime CreatedUtc;
        }

        private static readonly Queue<RoomQueueEntry> _roomQueue = new Queue<RoomQueueEntry>();
        private static readonly List<RoomReservation> _reservations = new List<RoomReservation>();

        internal static bool HostPublishesSuppressed => _suppressPublishDepth > 0;

        private static bool CanPublish()
        {
            if (!(PluginConfig.Instance?.MultiplayerEffectsEnabled ?? true))
            {
                return false;
            }

            return SceneHelper.MpPlusInRoom
                && SceneHelper.MpPlusIsHost
                && !string.IsNullOrWhiteSpace(SceneHelper.MpPlusRoomCode);
        }

        internal static void BeginSuppressHostPublish()
        {
            Interlocked.Increment(ref _suppressPublishDepth);
        }

        internal static void EndSuppressHostPublish()
        {
            Interlocked.Decrement(ref _suppressPublishDepth);
        }

        internal static string CanonicalizeEffectKey(string effectKeyOrCommand)
        {
            string key = FirstToken(effectKeyOrCommand).Trim().ToLowerInvariant();
            switch (key)
            {
                case "bmsg":
                    return "bomb";
                case "notecolour":
                    return "notecolor";
                case "rainbownotes":
                    return "rainbow";
                case "ghostnotes":
                    return "ghost";
                case "disappearingarrows":
                    return "disappear";
                default:
                    return key;
            }
        }

        /// <summary>Legacy alias used by channel-point routing.</summary>
        internal static string CanonicalizeDurationKey(string effectKeyOrCommand)
            => CanonicalizeEffectKey(effectKeyOrCommand);

        internal static bool IsLongRunningEffectKey(string effectKeyOrCommand)
        {
            return LongRunningEffectKeys.Contains(CanonicalizeEffectKey(effectKeyOrCommand));
        }

        internal static bool IsInstantEffectKey(string effectKeyOrCommand)
        {
            return InstantEffectKeys.Contains(CanonicalizeEffectKey(effectKeyOrCommand));
        }

        /// <summary>
        /// Returns canonical key when the command is a long-running duration effect; otherwise null.
        /// Instant effects (bomb/glitter/…) return null so CP fulfill uses the capped instant path.
        /// </summary>
        internal static string TryGetDurationEffectKey(string commandWithArgs)
        {
            string canonical = CanonicalizeEffectKey(commandWithArgs);
            return LongRunningEffectKeys.Contains(canonical) ? canonical : null;
        }

        internal static void NotifyDurationStarted(string effectKey, string commandWithArgs, string requesterName = null)
        {
            if (HostPublishesSuppressed)
            {
                return;
            }

            TryPublishCapped(effectKey, commandWithArgs, requesterName);
        }

        internal static void NotifyDurationStartedForChannelPoint(string effectKey, string commandWithArgs, string requesterName = null)
        {
            TryPublishCapped(effectKey, commandWithArgs, requesterName);
        }

        /// <summary>
        /// Instant / burst effect start (bomb, glitter, raid, fmsg, smsg, subcubes). Counts toward
        /// the same concurrent cap as long-running effects.
        /// </summary>
        internal static void NotifyInstantStarted(string effectKey, string commandWithArgs, string requesterName = null)
        {
            if (HostPublishesSuppressed)
            {
                return;
            }

            TryPublishCapped(effectKey, commandWithArgs, requesterName);
        }

        internal static void NotifyInstantStartedForChannelPoint(string effectKey, string commandWithArgs, string requesterName = null)
        {
            TryPublishCapped(effectKey, commandWithArgs, requesterName);
        }

        /// <summary>Frees a concurrent slot when a long-running or instant effect ends on the host.</summary>
        internal static void NotifyDurationEnded(string effectKey)
            => NotifyEffectEnded(effectKey);

        internal static void NotifyEffectEnded(string effectKey)
        {
            string canonicalKey = CanonicalizeEffectKey(effectKey);
            if (string.IsNullOrWhiteSpace(canonicalKey))
            {
                return;
            }

            lock (_activeLock)
            {
                _activeKeys.Remove(canonicalKey);
                for (int i = _reservations.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_reservations[i].Key, canonicalKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _reservations.RemoveAt(i);
                    }
                }
            }

            PumpRoomQueue();
        }

        internal static void ClearDurationTracking()
            => ClearActiveTracking();

        internal static void ClearActiveTracking()
        {
            lock (_activeLock)
            {
                _activeKeys.Clear();
                _roomQueue.Clear();
                _reservations.Clear();
            }
        }

        /// <summary>
        /// Chat and channel-point effects that share the room queue. Bits, follows, and subs
        /// are intentionally absent so they keep playing on the player who received them.
        /// </summary>
        internal static bool IsRoomSyncedEffectKey(string commandKey)
        {
            string canonical = CanonicalizeEffectKey(commandKey);
            return LongRunningEffectKeys.Contains(canonical) || InstantEffectKeys.Contains(canonical);
        }

        internal enum RoomCommandAdmitResult
        {
            Published,
            Queued,
            QueueFull,
            Ignored
        }

        /// <summary>
        /// Host-only. Publishes immediately when the key is already running or a slot is free.
        /// Otherwise appends to the room FIFO. Does not start the effect; every machine,
        /// including the host, starts it when the one-shot comes back.
        /// </summary>
        internal static RoomCommandAdmitResult AdmitRoomCommand(string commandWithArgs, string requesterName)
        {
            if (!CanPublish() || string.IsNullOrWhiteSpace(commandWithArgs))
            {
                return RoomCommandAdmitResult.Ignored;
            }

            string key = CanonicalizeEffectKey(commandWithArgs);
            if (!IsRoomSyncedEffectKey(key))
            {
                return RoomCommandAdmitResult.Ignored;
            }

            string command = commandWithArgs.Trim();
            string user = string.IsNullOrWhiteSpace(requesterName) ? null : requesterName.Trim();
            string token = null;
            bool publishNow = false;

            lock (_activeLock)
            {
                if (HasUnappliedReservation(key))
                {
                    return EnqueueRoomCommand(command, user, key);
                }

                bool alreadyActive = _activeKeys.Contains(key);
                if (alreadyActive || _activeKeys.Count < MaxConcurrentActiveEffects)
                {
                    token = ReserveAndMint(key, addedSlot: !alreadyActive);
                    publishNow = true;
                }
                else
                {
                    return EnqueueRoomCommand(command, user, key);
                }
            }

            if (publishNow)
            {
                PublishReserved(command, user, token);
            }

            return RoomCommandAdmitResult.Published;
        }

        internal static bool IsAwaitingHostApply(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            lock (_activeLock)
            {
                for (int i = 0; i < _reservations.Count; i++)
                {
                    RoomReservation reservation = _reservations[i];
                    if (!reservation.Applied && string.Equals(reservation.Token, token, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static void NoteRoomEchoReceived(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            lock (_activeLock)
            {
                for (int i = 0; i < _reservations.Count; i++)
                {
                    if (string.Equals(_reservations[i].Token, token, StringComparison.Ordinal))
                    {
                        _reservations[i].EchoReceived = true;
                        return;
                    }
                }
            }
        }

        internal static void MarkRoomReservationApplied(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            lock (_activeLock)
            {
                for (int i = 0; i < _reservations.Count; i++)
                {
                    if (string.Equals(_reservations[i].Token, token, StringComparison.Ordinal))
                    {
                        _reservations[i].Applied = true;
                        return;
                    }
                }
            }
        }

        internal static void ReleaseRoomReservation(string token)
        {
            bool shouldPump = false;
            lock (_activeLock)
            {
                for (int i = 0; i < _reservations.Count; i++)
                {
                    RoomReservation reservation = _reservations[i];
                    if (!string.Equals(reservation.Token, token, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (reservation.AddedSlot && !reservation.Applied)
                    {
                        _activeKeys.Remove(reservation.Key);
                        shouldPump = true;
                    }

                    _reservations.RemoveAt(i);
                    break;
                }
            }

            if (shouldPump)
            {
                PumpRoomQueue();
            }
        }

        /// <summary>
        /// Drops a reservation whose one-shot never came back. Reservations that already
        /// echoed stay until apply succeeds or the sync client drops them.
        /// </summary>
        internal static void ReleaseStaleRoomReservations()
        {
            List<string> stale = null;
            DateTime now = DateTime.UtcNow;
            lock (_activeLock)
            {
                for (int i = 0; i < _reservations.Count; i++)
                {
                    RoomReservation reservation = _reservations[i];
                    if (reservation.Applied || reservation.EchoReceived)
                    {
                        continue;
                    }

                    if ((now - reservation.CreatedUtc).TotalSeconds < ReservationTimeoutSeconds)
                    {
                        continue;
                    }

                    if (stale == null)
                    {
                        stale = new List<string>();
                    }

                    stale.Add(reservation.Token);
                }
            }

            if (stale == null)
            {
                return;
            }

            for (int i = 0; i < stale.Count; i++)
            {
                Plugin.Log.Info(
                    "[MultiplayerEffectPublisher] Room reservation timed out token=" + stale[i]);
                ReleaseRoomReservation(stale[i]);
            }
        }

        /// <summary>
        /// Publish a one-shot, applying the concurrent cap using the command's first token as key.
        /// Prefer <see cref="NotifyInstantStarted"/> / <see cref="NotifyDurationStarted"/> when the key is known.
        /// </summary>
        internal static void PublishOneShot(string commandWithArgs, string requesterName = null)
        {
            if (HostPublishesSuppressed)
            {
                return;
            }

            TryPublishCapped(FirstToken(commandWithArgs), commandWithArgs, requesterName);
        }

        internal static void PublishFulfilledChannelPoint(string commandWithArgs, string requesterName = null)
        {
            string key = FirstToken(commandWithArgs);
            string canonical = CanonicalizeEffectKey(key);
            if (LongRunningEffectKeys.Contains(canonical))
            {
                TryPublishCapped(canonical, commandWithArgs, requesterName);
                return;
            }

            TryPublishCapped(canonical, commandWithArgs, requesterName);
        }

        private static bool TryPublishCapped(string effectKey, string commandWithArgs, string requesterName)
        {
            if (!CanPublish() || string.IsNullOrWhiteSpace(commandWithArgs))
            {
                return false;
            }

            string canonicalKey = CanonicalizeEffectKey(effectKey);
            if (string.IsNullOrWhiteSpace(canonicalKey))
            {
                return false;
            }

            bool shouldPublish;
            string activeSnapshot = string.Empty;
            lock (_activeLock)
            {
                if (_activeKeys.Contains(canonicalKey))
                {
                    shouldPublish = true;
                }
                else if (_activeKeys.Count >= MaxConcurrentActiveEffects)
                {
                    shouldPublish = false;
                    activeSnapshot = string.Join(",", _activeKeys);
                }
                else
                {
                    _activeKeys.Add(canonicalKey);
                    shouldPublish = true;
                }
            }

            if (!shouldPublish)
            {
                Plugin.Log.Info(
                    "[MultiplayerEffectPublisher] Concurrent-effect cap reached (" + MaxConcurrentActiveEffects +
                    "); '" + canonicalKey + "' runs host-local only (not synced). active=[" +
                    activeSnapshot + "]");
                return false;
            }

            PublishOneShotCore(commandWithArgs, requesterName);
            return true;
        }

        private static void PublishOneShotCore(string commandWithArgs, string requesterName)
        {
            string payload = commandWithArgs.Trim() + " #" + MintOneShotToken();
            MultiplayerStateClient.SetActiveCommand(payload, requesterName, forceSend: true);
        }

        private static string MintOneShotToken()
        {
            int seq = Interlocked.Increment(ref _oneShotSequence);
            if (seq <= 0)
            {
                seq = Interlocked.Increment(ref _oneShotSequence);
            }

            return "mp" + OneShotEpoch + seq.ToString("x");
        }

        private static bool HasUnappliedReservation(string key)
        {
            for (int i = 0; i < _reservations.Count; i++)
            {
                RoomReservation reservation = _reservations[i];
                if (!reservation.Applied && string.Equals(reservation.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static RoomCommandAdmitResult EnqueueRoomCommand(string command, string user, string key)
        {
            if (_roomQueue.Count >= MaxRoomQueue)
            {
                Plugin.Log.Info(
                    "[MultiplayerEffectPublisher] Room queue full (" + MaxRoomQueue +
                    "); dropped '" + key + "'.");
                return RoomCommandAdmitResult.QueueFull;
            }

            _roomQueue.Enqueue(new RoomQueueEntry
            {
                Command = command,
                User = user,
                Key = key
            });
            Plugin.Log.Info(
                "[MultiplayerEffectPublisher] Room queued key=" + key +
                " depth=" + _roomQueue.Count + " active=[" + string.Join(",", _activeKeys) + "]");
            return RoomCommandAdmitResult.Queued;
        }

        private static string ReserveAndMint(string key, bool addedSlot)
        {
            if (addedSlot)
            {
                _activeKeys.Add(key);
            }

            string token = MintOneShotToken();
            _reservations.Add(new RoomReservation
            {
                Token = token,
                Key = key,
                AddedSlot = addedSlot,
                Applied = false,
                EchoReceived = false,
                CreatedUtc = DateTime.UtcNow
            });
            return token;
        }

        private static void PublishReserved(string command, string user, string token)
        {
            MultiplayerStateClient.SetActiveCommand(command + " #" + token, user, forceSend: true);
            Plugin.Log.Info("[MultiplayerEffectPublisher] Room published token=" + token + " command=" + command);
        }

        private static void PumpRoomQueue()
        {
            while (true)
            {
                string command = null;
                string user = null;
                string token = null;

                lock (_activeLock)
                {
                    if (_roomQueue.Count == 0)
                    {
                        return;
                    }

                    RoomQueueEntry head = _roomQueue.Peek();
                    if (HasUnappliedReservation(head.Key))
                    {
                        return;
                    }

                    bool alreadyActive = _activeKeys.Contains(head.Key);
                    if (!alreadyActive && _activeKeys.Count >= MaxConcurrentActiveEffects)
                    {
                        return;
                    }

                    RoomQueueEntry entry = _roomQueue.Dequeue();
                    command = entry.Command;
                    user = entry.User;
                    token = ReserveAndMint(entry.Key, addedSlot: !alreadyActive);
                }

                PublishReserved(command, user, token);
            }
        }

        internal static string StripOneShotNonce(string activeCommand)
        {
            if (string.IsNullOrWhiteSpace(activeCommand))
            {
                return activeCommand;
            }

            int marker = activeCommand.LastIndexOf(" #mp", StringComparison.OrdinalIgnoreCase);
            if (marker <= 0)
            {
                return activeCommand.Trim();
            }

            string maybeNonce = activeCommand.Substring(marker + 4).Trim();
            if (maybeNonce.Length == 0)
            {
                return activeCommand.Trim();
            }

            for (int i = 0; i < maybeNonce.Length; i++)
            {
                char c = maybeNonce[i];
                bool hex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return activeCommand.Trim();
                }
            }

            return activeCommand.Substring(0, marker).Trim();
        }

        internal static string NormalizeCommandForPublish(string messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText))
            {
                return null;
            }

            string trimmed = messageText.Trim();
            if (trimmed.StartsWith("!", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1).TrimStart();
            }

            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        private static string FirstToken(string commandWithArgs)
        {
            if (string.IsNullOrWhiteSpace(commandWithArgs))
            {
                return string.Empty;
            }

            string trimmed = commandWithArgs.Trim();
            int sp = trimmed.IndexOf(' ');
            return sp < 0 ? trimmed : trimmed.Substring(0, sp);
        }
    }
}
