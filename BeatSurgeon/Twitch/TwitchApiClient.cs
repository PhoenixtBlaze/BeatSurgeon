using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Zenject;

namespace BeatSurgeon.Twitch
{
    internal sealed class TwitchApiClient
    {
        internal static event Action OnSubscriberStatusChanged;

        private static readonly LogUtil _log = LogUtil.GetLogger("TwitchApiClient");
        private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static TwitchApiClient _instance;

        // Singleton HttpClient - never new-ed per request.
        private readonly HttpClient _http;
        private readonly TwitchAuthManager _authManager;

        internal static TwitchApiClient Instance => _instance ?? (_instance = new TwitchApiClient(TwitchAuthManager.Instance));

        internal string BroadcasterId { get; private set; }
        internal string BroadcasterName { get; private set; }

        private const string BackendBaseUrl = "https://phoenixblaze0.duckdns.org";
        private const string BackendEntitlementsUrl = BackendBaseUrl + "/entitlements";

        [Inject]
        public TwitchApiClient(TwitchAuthManager authManager)
        {
            _instance = this;
            _authManager = authManager;
            _http = new HttpClient
            {
                BaseAddress = new Uri("https://api.twitch.tv/helix/"),
                Timeout = TimeSpan.FromSeconds(15)
            };
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _log.Lifecycle("HttpClient initialized - BaseAddress=https://api.twitch.tv/helix/");
        }

        private async Task AuthorizeRequestAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string token = await _authManager.GetAccessTokenAsync(ct).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Client-Id", PluginConfig.Instance.ClientId);
        }

        private async Task<HttpResponseMessage> SendHelixAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await AuthorizeRequestAsync(request, ct).ConfigureAwait(false);
            HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            _log.Debug($"HTTP {request.Method} {request.RequestUri} => {(int)response.StatusCode}");

            if ((int)response.StatusCode == 429)
            {
                // Twitch rate-limited us. Honor the Retry-After before returning so the caller
                // doesn't immediately hammer the API again on the next attempt.
                int retryAfterSecs = 1;
                if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string> raVals))
                {
                    foreach (string v in raVals)
                    {
                        if (int.TryParse(v, out int parsed))
                            retryAfterSecs = Math.Min(parsed, 30);
                        break;
                    }
                }
                _log.Warn("HTTP 429 TooManyRequests on " + request.RequestUri + " - throttling " + retryAfterSecs + "s (Retry-After)");
                await Task.Delay(TimeSpan.FromSeconds(retryAfterSecs), ct).ConfigureAwait(false);
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // 401: our access token was rejected server-side. Proactively refresh it so
                // the next API call succeeds without requiring manual user re-auth.
                _log.Warn("HTTP 401 Unauthorized on " + request.RequestUri + " - refreshing token for subsequent calls");
                await _authManager.ForceRefreshTokenAsync(ct).ConfigureAwait(false);
            }

            return response;
        }

        internal async Task<string> CreateEventSubSubscriptionAsync(
            string type,
            string version,
            Dictionary<string, string> condition,
            CancellationToken ct = default(CancellationToken))
        {
            _log.Debug("CreateEventSubSubscription type=" + type);

            var body = new JObject
            {
                ["type"] = type,
                ["version"] = version,
                ["condition"] = JObject.FromObject(condition ?? new Dictionary<string, string>()),
                ["transport"] = new JObject
                {
                    ["method"] = "websocket",
                    // Twitch infers session from connected WS + bearer context for this setup flow.
                    // Session binding is handled by TwitchEventSubClient during WS lifecycle.
                    ["session_id"] = TwitchEventSubClient.CurrentSessionId ?? string.Empty
                }
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, "eventsub/subscriptions"))
            {
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await SendHelixAsync(request, ct).ConfigureAwait(false))
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _log.Debug("CreateEventSubSubscription response=" + response.StatusCode);

                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        // 409: a subscription for this reward + session already exists.
                        // Parse the existing subscription ID from the response and return it.
                        string existingId = JObject.Parse(json)["data"]?[0]?["id"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(existingId))
                        {
                            _log.Info("EventSub subscription already exists (409) - reusing id=" + existingId);
                            return existingId;
                        }
                        // 409 body may not contain data for cross-session duplicates; fall through to error.
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _log.Error("CreateEventSubSubscription FAILED status=" + response.StatusCode + " body=" + json);
                        throw new HttpRequestException("EventSub subscription creation failed: " + response.StatusCode);
                    }

                    string subscriptionId = JObject.Parse(json)["data"]?[0]?["id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(subscriptionId))
                    {
                        throw new InvalidOperationException("EventSub subscription create response did not contain a subscription id.");
                    }

                    _log.Info("EventSub subscription created type=" + type);
                    return subscriptionId;
                }
            }
        }

        internal async Task DeleteEventSubSubscriptionAsync(
            string subscriptionId,
            CancellationToken ct = default(CancellationToken))
        {
            _log.Debug("DeleteEventSubSubscription subscriptionId=" + subscriptionId);
            using (var request = new HttpRequestMessage(
                       HttpMethod.Delete, "eventsub/subscriptions?id=" + Uri.EscapeDataString(subscriptionId)))
            {
                using (HttpResponseMessage response = await SendHelixAsync(request, ct).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        _log.Info("EventSub subscription deleted subscriptionId=" + subscriptionId);
                    }
                    else
                    {
                        _log.Warn("DeleteEventSub failed status=" + response.StatusCode + " subscriptionId=" + subscriptionId);
                    }
                }
            }
        }

        internal async Task<string> CreateCustomRewardAsync(
            string channelUserId,
            string title,
            int cost,
            CancellationToken ct = default(CancellationToken))
        {
            JObject reward = await CreateOrUpdateCustomRewardAsync(
                channelUserId,
                rewardId: null,
                title: title,
                prompt: string.Empty,
                cost: cost,
                cooldownSeconds: 0,
                backgroundColorHex: null,
                enabled: true,
                ct: ct).ConfigureAwait(false);

            string rewardId = reward?["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(rewardId))
            {
                throw new InvalidOperationException("CreateCustomReward response missing id.");
            }

            return rewardId;
        }

        internal async Task<JObject> CreateCustomRewardAsync(
            string channelUserId,
            string title,
            string prompt,
            int cost,
            int cooldownSeconds,
            string backgroundColorHex,
            bool enabled,
            CancellationToken ct = default(CancellationToken))
        {
            return await CreateOrUpdateCustomRewardAsync(
                channelUserId,
                rewardId: null,
                title: title,
                prompt: prompt,
                cost: cost,
                cooldownSeconds: cooldownSeconds,
                backgroundColorHex: backgroundColorHex,
                enabled: enabled,
                ct: ct).ConfigureAwait(false);
        }

        internal async Task<JObject> UpdateCustomRewardAsync(
            string channelUserId,
            string rewardId,
            string title,
            string prompt,
            int cost,
            int cooldownSeconds,
            string backgroundColorHex,
            bool enabled,
            CancellationToken ct = default(CancellationToken))
        {
            return await CreateOrUpdateCustomRewardAsync(
                channelUserId,
                rewardId: rewardId,
                title: title,
                prompt: prompt,
                cost: cost,
                cooldownSeconds: cooldownSeconds,
                backgroundColorHex: backgroundColorHex,
                enabled: enabled,
                ct: ct).ConfigureAwait(false);
        }

        private async Task<JObject> CreateOrUpdateCustomRewardAsync(
            string channelUserId,
            string rewardId,
            string title,
            string prompt,
            int cost,
            int cooldownSeconds,
            string backgroundColorHex,
            bool enabled,
            CancellationToken ct)
        {
            bool isUpdate = !string.IsNullOrWhiteSpace(rewardId);
            var payload = new JObject
            {
                ["title"] = title ?? string.Empty,
                ["prompt"] = prompt ?? string.Empty,
                ["cost"] = Math.Max(1, cost),
                ["is_enabled"] = enabled,
                ["is_user_input_required"] = false
            };

            if (cooldownSeconds > 0)
            {
                payload["global_cooldown_setting"] = new JObject
                {
                    ["is_enabled"] = true,
                    ["global_cooldown_seconds"] = Math.Max(1, cooldownSeconds)
                };
            }
            else
            {
                payload["global_cooldown_setting"] = new JObject
                {
                    ["is_enabled"] = false,
                    ["global_cooldown_seconds"] = 0
                };
            }

            string normalizedHex = NormalizeHexColor(backgroundColorHex);
            if (!string.IsNullOrWhiteSpace(normalizedHex))
            {
                payload["background_color"] = normalizedHex;
            }

            string uri = "channel_points/custom_rewards?broadcaster_id=" + Uri.EscapeDataString(channelUserId);
            if (isUpdate)
            {
                uri += "&id=" + Uri.EscapeDataString(rewardId);
            }

            using (var request = new HttpRequestMessage(isUpdate ? new HttpMethod("PATCH") : HttpMethod.Post, uri))
            {
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await SendHelixAsync(request, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        string op = isUpdate ? "UpdateCustomReward" : "CreateCustomReward";
                        throw new HttpRequestException(op + " failed: " + response.StatusCode + " body=" + body);
                    }

                    return JObject.Parse(body)["data"]?[0] as JObject;
                }
            }
        }

        private static string NormalizeHexColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmed = value.Trim();
            if (!trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                trimmed = "#" + trimmed;
            }

            Color color;
            if (!ColorUtility.TryParseHtmlString(trimmed, out color))
            {
                return null;
            }

            return "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        internal async Task SetRewardEnabledAsync(
            string channelUserId,
            string rewardId,
            bool enabled,
            CancellationToken ct = default(CancellationToken))
        {
            var payload = new JObject { ["is_enabled"] = enabled };
            string uri = "channel_points/custom_rewards?broadcaster_id="
                         + Uri.EscapeDataString(channelUserId)
                         + "&id=" + Uri.EscapeDataString(rewardId);

            using (var request = new HttpRequestMessage(new HttpMethod("PATCH"), uri))
            {
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await SendHelixAsync(request, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new HttpRequestException("SetRewardEnabled failed: " + response.StatusCode + " body=" + body);
                    }
                }
            }
        }

        internal async Task<JArray> GetManageableRewardsAsync(string channelUserId, CancellationToken ct = default(CancellationToken))
        {
            string uri = "channel_points/custom_rewards?broadcaster_id="
                         + Uri.EscapeDataString(channelUserId)
                         + "&only_manageable_rewards=true";

            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                using (HttpResponseMessage response = await SendHelixAsync(request, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException("GetManageableRewards failed: " + response.StatusCode + " body=" + body);
                    }

                    return (JArray)(JObject.Parse(body)["data"] ?? new JArray());
                }
            }
        }

        internal async Task UpdateRedemptionStatusAsync(
            string channelUserId,
            string rewardId,
            string redemptionId,
            string status,
            CancellationToken ct = default(CancellationToken))
        {
            string uri = "channel_points/custom_rewards/redemptions?broadcaster_id="
                         + Uri.EscapeDataString(channelUserId)
                         + "&reward_id=" + Uri.EscapeDataString(rewardId)
                         + "&id=" + Uri.EscapeDataString(redemptionId);

            var payload = new JObject { ["status"] = status };
            using (var request = new HttpRequestMessage(new HttpMethod("PATCH"), uri))
            {
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await SendHelixAsync(request, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new HttpRequestException("UpdateRedemptionStatus failed: " + response.StatusCode + " body=" + body);
                    }
                }
            }
        }

        internal async Task<bool> SendChatMessageAsync(
            string channelUserId,
            string senderUserId,
            string message,
            CancellationToken ct = default(CancellationToken))
        {
            var payload = new JObject
            {
                ["broadcaster_id"] = channelUserId,
                ["sender_id"] = senderUserId,
                ["message"] = message
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, "chat/messages"))
            {
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await SendHelixAsync(request, ct).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }

                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _log.Warn("SendChatMessage failed status=" + response.StatusCode + " body=" + body);
                    return false;
                }
            }
        }

        internal async Task FetchBroadcasterAndEntitlementsAsync(CancellationToken ct = default(CancellationToken))
        {
            string token = await _authManager.GetAccessTokenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            await FetchIdentityAsync(token, ct).ConfigureAwait(false);
            await RefreshEntitlementsAsync(token, ct).ConfigureAwait(false);
        }

        internal async Task RefreshEntitlementsAsync(string userAccessToken, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, BackendEntitlementsUrl))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);
                    using (HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            InvalidateSupporterState();
                            OnSubscriberStatusChanged?.Invoke();
                            return;
                        }

                        JObject json = JObject.Parse(body);
                        string entitlementToken = json["entitlementToken"]?.ToString();
                        if (!TryVerifyAndParseEntitlement(entitlementToken, out EntitlementsSnapshot snapshot))
                        {
                            InvalidateSupporterState();
                            OnSubscriberStatusChanged?.Invoke();
                            return;
                        }

                        EntitlementsState.Set(snapshot);
                        PluginConfig.Instance.CachedSupporterTier = (int)snapshot.Tier;
                        PremiumVisualFeatureAccessController.SyncAllConfigEnabledStates();
                        OnSubscriberStatusChanged?.Invoke();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "RefreshEntitlementsAsync");
                InvalidateSupporterState();
                OnSubscriberStatusChanged?.Invoke();
            }
        }

        private static void InvalidateSupporterState()
        {
            EntitlementsState.Clear();
            if (PluginConfig.Instance != null)
            {
                PluginConfig.Instance.CachedSupporterTier = 0;
            }

            PremiumVisualFeatureAccessController.SyncAllConfigEnabledStates();
        }

        internal async Task<bool> CheckVisualsPermissionAsync(CancellationToken ct = default(CancellationToken))
        {
            string accessToken = await _authManager.GetAccessTokenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return false;
            }

            EntitlementsSnapshot current = EntitlementsState.Current;
            if (string.IsNullOrWhiteSpace(current.SignedEntitlementToken) ||
                DateTime.UtcNow >= current.ExpiresAtUtc.AddMinutes(-5))
            {
                _log.Info("Entitlement token expired or missing, refreshing...");
                await RefreshEntitlementsAsync(accessToken, ct).ConfigureAwait(false);
                current = EntitlementsState.Current;
                if (string.IsNullOrWhiteSpace(current.SignedEntitlementToken))
                {
                    _log.Warn("Failed to refresh entitlement token");
                    return false;
                }
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, BackendBaseUrl + "/visuals/permission"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.TryAddWithoutValidation("X-Entitlement", current.SignedEntitlementToken);
                using (HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _log.Warn("CheckVisualsPermission failed: " + response.StatusCode + " - " + body);
                        return false;
                    }

                    try
                    {
                        return JObject.Parse(body)["allowed"]?.Value<bool>() == true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        internal static IEnumerator GetSpriteFromUrl(string url, Action<Sprite> callback)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                callback?.Invoke(null);
                yield break;
            }

            if (_spriteCache.TryGetValue(url, out Sprite cached) && cached != null)
            {
                callback?.Invoke(cached);
                yield break;
            }

            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(null);
                    yield break;
                }

                Texture texture = DownloadHandlerTexture.GetContent(request);
                if (texture == null)
                {
                    callback?.Invoke(null);
                    yield break;
                }

                Sprite sprite = Sprite.Create(
                    (Texture2D)texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));

                _spriteCache[url] = sprite;
                callback?.Invoke(sprite);
            }
        }

        internal static void ClearCache()
        {
            foreach (Sprite sprite in _spriteCache.Values)
            {
                if (sprite == null) continue;
                if (sprite.texture != null) UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }

            _spriteCache.Clear();
        }

        private async Task FetchIdentityAsync(string userAccessToken, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, "users"))
            {
                request.Headers.TryAddWithoutValidation("Client-Id", _authManager.ClientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);
                using (HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException("FetchIdentity failed: " + response.StatusCode + " body=" + body);
                    }

                    JToken data = JObject.Parse(body)["data"]?[0];
                    if (data == null)
                    {
                        return;
                    }

                    BroadcasterId = data["id"]?.ToString();
                    BroadcasterName = data["login"]?.ToString();
                    PluginConfig.Instance.CachedBroadcasterId = BroadcasterId ?? string.Empty;
                    PluginConfig.Instance.CachedBotUserId = BroadcasterId ?? string.Empty;
                    PluginConfig.Instance.CachedBotUserLogin = BroadcasterName ?? string.Empty;
                    PluginConfig.Instance.CachedBroadcasterLogin = BroadcasterName ?? string.Empty;
                }
            }
        }

        private static bool TryVerifyAndParseEntitlement(string signedToken, out EntitlementsSnapshot snapshot)
        {
            snapshot = default(EntitlementsSnapshot);
            if (!JwtEd25519.TryVerify(signedToken, out JwtEd25519.VerifiedJwt verified))
            {
                return false;
            }

            JObject payload = verified.Payload;
            long exp = payload["exp"]?.Value<long>() ?? 0;
            string sub = payload["sub"]?.ToString();
            if (exp <= 0 || string.IsNullOrWhiteSpace(sub))
            {
                return false;
            }

            string expectedUserId = TwitchAuthManager.Instance?.BroadcasterId;
            if (!string.IsNullOrWhiteSpace(expectedUserId) &&
                !string.Equals(sub, expectedUserId, StringComparison.Ordinal))
            {
                return false;
            }

            int tierInt = payload["tier"]?.Value<int>() ?? 0;
            if (tierInt < 0 || tierInt > 3)
            {
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
    }
}
