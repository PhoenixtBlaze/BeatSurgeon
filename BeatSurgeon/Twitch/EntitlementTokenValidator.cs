using System;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;

namespace BeatSurgeon.Twitch
{
    internal static class EntitlementTokenValidator
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("EntitlementTokenValidator");

        internal static bool TryVerifyAndParse(
            string signedToken,
            string expectedUserId,
            EntitlementProvider expectedProvider,
            out EntitlementsSnapshot snapshot)
        {
            snapshot = default(EntitlementsSnapshot);
            if (!JwtEd25519.TryVerify(signedToken, out JwtEd25519.VerifiedJwt verified))
            {
                _log.Warn("TryVerifyAndParse: signature/format verification failed (JwtEd25519.TryVerify returned false). provider=" + expectedProvider +
                    " tokenPresent=" + (!string.IsNullOrWhiteSpace(signedToken)));
                return false;
            }

            try
            {
                JObject payload = verified.Payload;
                long exp = payload["exp"]?.Value<long>() ?? 0;
                string sub = payload["sub"]?.ToString();
                if (exp <= 0 || string.IsNullOrWhiteSpace(sub))
                {
                    _log.Warn("TryVerifyAndParse: payload missing exp or sub claim. exp=" + exp +
                        " subPresent=" + (!string.IsNullOrWhiteSpace(sub)) + " provider=" + expectedProvider);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(expectedUserId) &&
                    !string.Equals(sub, expectedUserId, StringComparison.Ordinal))
                {
                    _log.Warn("TryVerifyAndParse: sub mismatch. expected='" + expectedUserId + "' actual='" + sub +
                        "' provider=" + expectedProvider + ". Likely the OAuth flow was completed under a different account.");
                    return false;
                }

                string provider = payload["provider"]?.ToString();
                if (expectedProvider == EntitlementProvider.Patreon)
                {
                    if (!string.Equals(provider, "patreon", StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Warn("TryVerifyAndParse: provider mismatch. expected=patreon actual='" + provider + "'");
                        return false;
                    }
                }
                else if (expectedProvider == EntitlementProvider.Twitch)
                {
                    if (!string.IsNullOrWhiteSpace(provider) &&
                        !string.Equals(provider, "twitch", StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Warn("TryVerifyAndParse: provider mismatch. expected=twitch actual='" + provider + "'");
                        return false;
                    }
                }

                int tierInt = payload["tier"]?.Value<int>() ?? 0;
                if (tierInt < 0 || tierInt > 3)
                {
                    _log.Warn("TryVerifyAndParse: tier out of range tier=" + tierInt + " provider=" + expectedProvider);
                    return false;
                }

                snapshot = new EntitlementsSnapshot
                {
                    Tier = (SupporterTier)tierInt,
                    ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime,
                    SignedEntitlementToken = signedToken
                };
                return true;
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "TryVerifyAndParse");
                return false;
            }
        }
    }
}