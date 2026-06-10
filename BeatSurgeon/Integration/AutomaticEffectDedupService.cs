using System;
using System.Collections.Concurrent;

namespace BeatSurgeon.Integration
{
    internal enum AutomaticEffectOrigin
    {
        EventSub = 0,
        TwitchChat = 1,
        IntegrationApi = 2
    }

    /// <summary>
    /// Prevents the same follow, subscription, or cheer/bit automatic effect from firing twice when
    /// both the Beat Surgeon native pipeline and the Integration API receive the same viewer event.
    /// </summary>
    internal sealed class AutomaticEffectDedupService
    {
        private readonly ConcurrentDictionary<string, DateTime> _claims =
            new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        internal static string BuildFollowKey(string service, string userId, string userName)
        {
            string identity = NormalizeIdentity(service, userId, userName);
            return "auto:follow:" + identity;
        }

        internal static string BuildSubscriptionKey(string service, string userId, string userName, string eventKind)
        {
            string identity = NormalizeIdentity(service, userId, userName);
            string kind = string.IsNullOrWhiteSpace(eventKind) ? "sub" : eventKind.Trim().ToLowerInvariant();
            return "auto:sub:" + identity + ":" + kind;
        }

        internal static string BuildCheerKey(string service, string userId, string userName, int amount)
        {
            string identity = NormalizeIdentity(service, userId, userName);
            int normalizedAmount = Math.Max(0, amount);
            return "auto:cheer:" + identity + ":" + normalizedAmount;
        }

        internal bool TryClaim(string dedupKey, AutomaticEffectOrigin origin)
        {
            if (string.IsNullOrWhiteSpace(dedupKey))
            {
                return false;
            }

            CleanupExpiredClaims();
            DateTime now = DateTime.UtcNow;
            DateTime expiresAt = now.AddSeconds(IntegrationApiConstants.AutomaticEffectDedupWindowSeconds);

            if (_claims.TryGetValue(dedupKey, out DateTime existing) && existing > now)
            {
                return false;
            }

            _claims[dedupKey] = expiresAt;
            return true;
        }

        private static string NormalizeIdentity(string service, string userId, string userName)
        {
            string normalizedService = string.IsNullOrWhiteSpace(service)
                ? "unknown"
                : service.Trim().ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(userId))
            {
                return normalizedService + ":" + userId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(userName))
            {
                return normalizedService + ":name:" + userName.Trim().ToLowerInvariant();
            }

            return normalizedService + ":anonymous";
        }

        private void CleanupExpiredClaims()
        {
            DateTime now = DateTime.UtcNow;
            foreach (string key in _claims.Keys)
            {
                if (_claims.TryGetValue(key, out DateTime expiresAt) && expiresAt <= now)
                {
                    _claims.TryRemove(key, out _);
                }
            }
        }
    }
}
