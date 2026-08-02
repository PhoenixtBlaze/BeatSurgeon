using System;
using System.Collections.Generic;
using System.Threading;

namespace BeatSurgeon
{
    /// <summary>
    /// Host-side helper that publishes gameplay effects to Multiplayer+ clients
    /// through the existing active_command relay.
    ///
    /// Every effect syncs as a one-shot when it starts on the host (no sticky clear).
    /// At most <see cref="MaxConcurrentActiveEffects"/> distinct effect keys may be synced
    /// concurrently — including duration effects AND instant ones (glitter/raid/fmsg/smsg/
    /// subcubes/bomb). A 4th distinct key still runs host-locally but is not published.
    /// Restarting an already-tracked key always publishes.
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

        private static int _oneShotSequence;
        private static int _suppressPublishDepth;

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
            }
        }

        internal static void ClearDurationTracking()
            => ClearActiveTracking();

        internal static void ClearActiveTracking()
        {
            lock (_activeLock)
            {
                _activeKeys.Clear();
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
            int seq = Interlocked.Increment(ref _oneShotSequence);
            if (seq <= 0)
            {
                seq = Interlocked.Increment(ref _oneShotSequence);
            }

            string payload = commandWithArgs.Trim() + " #mp" + seq.ToString("x");
            MultiplayerStateClient.SetActiveCommand(payload, requesterName, forceSend: true);
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
